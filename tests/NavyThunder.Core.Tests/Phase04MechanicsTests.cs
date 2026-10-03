using NavyThunder.Core.Damage;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Model;
using NavyThunder.Core.Ships;
using NavyThunder.Data;
using Xunit;
using Xunit.Abstractions;

namespace NavyThunder.Core.Tests;

/// <summary>
/// Phase 04 mechanics (PHASE_04 §9): TDS absorption (MDR-0012), breach classes
/// (MDR-0008 addendum) and the 3-section unsinkability ruling (MDR-0007 addendum).
/// </summary>
public class Phase04MechanicsTests(ITestOutputHelper output)
{
    private static DataRepository Repo() =>
        DataRepository.LoadFromDirectory(RepoLocator.FindDataDirectory());

    /// <summary>Clones a fleet ship and injects a TDS belt part along one side.</summary>
    private static ShipDefinition WithTds(ShipDefinition src, double absorptionPct)
    {
        var tds = new ShipPartDefinition
        {
            Id = src.Id + "_tds_test",
            Kind = PartKind.AntiTorpedo,
            SectionId = src.HullSections.First(s => s.Role == HullSectionRole.Mid).Id,
            Hp = 3000,
            Crew = 0,
            XMinM = src.BeamM / 2 - 1.0, XMaxM = src.BeamM / 2 + 1.0,
            YMinM = -4, YMaxM = -1,
            ZMinM = -src.LengthM / 4, ZMaxM = src.LengthM / 4,
            HydroShockAbsorptionPct = absorptionPct,
        };
        return src with { Parts = [.. src.Parts, tds] };
    }

    private static DamageEvent HydroShockAt(Vec3 position, double amount, double radius) => new()
    {
        Channel = DamageChannel.HydroShock,
        SourceId = "test_torpedo",
        TargetId = "",
        Position = position,
        Radius = radius,
        Amount = amount,
        Tick = 0,
        Time = 0,
    };

    [Fact]
    public void Tds_Absorbs_HydroShock_And_Takes_The_Damage()
    {
        var repo = Repo();
        var plain = ShipFactory.Create(repo.Ships["uss_north_carolina"], "plain");
        var shielded = ShipFactory.Create(WithTds(repo.Ships["uss_north_carolina"], 0.6), "shielded");

        // Detonate inside the TDS belt volume (starboard, below waterline, mid).
        var point = new Vec3(plain.Definition.BeamM / 2, -2.5, 0);
        double amount = 800;

        plain.ApplyDamage(HydroShockAt(point, amount, 25) with { TargetId = plain.TargetId });
        shielded.ApplyDamage(HydroShockAt(point, amount, 25) with { TargetId = shielded.TargetId });

        double plainInterior = plain.Parts.Values
            .Where(p => p.Definition.Kind != PartKind.AntiTorpedo).Sum(p => p.Definition.Hp - p.Hp);
        double shieldedInterior = shielded.Parts.Values
            .Where(p => p.Definition.Kind != PartKind.AntiTorpedo).Sum(p => p.Definition.Hp - p.Hp);
        var tdsPart = shielded.Parts.Values.Single(p => p.Definition.Kind == PartKind.AntiTorpedo);

        output.WriteLine($"interior damage: plain={plainInterior:F0} shielded={shieldedInterior:F0}; " +
                         $"TDS hp loss={3000 - tdsPart.Hp:F0}");
        Assert.True(shieldedInterior < plainInterior * 0.7,
            $"TDS must absorb interior damage (plain {plainInterior:F0} vs shielded {shieldedInterior:F0})");
        Assert.True(shieldedInterior > 0.1, "shielded interior must still take some damage");
        Assert.True(tdsPart.Hp < 3000, "TDS layer must soak the absorbed energy");
    }

    [Fact]
    public void Destroyed_Tds_Protects_Nothing()
    {
        var repo = Repo();
        var plain = ShipFactory.Create(repo.Ships["uss_north_carolina"], "plain");
        var shielded = ShipFactory.Create(WithTds(repo.Ships["uss_north_carolina"], 0.6), "shielded");

        var tds = shielded.Parts.Values.Single(p => p.Definition.Kind == PartKind.AntiTorpedo);
        // Blown layer (W5 bugfix semantics): kill it through the damage path.
        shielded.ApplyDamage(new DamageEvent
        {
            Channel = DamageChannel.Kinetic,
            SourceId = "test",
            TargetId = shielded.TargetId,
            Position = tds.Center,
            Amount = tds.Definition.Hp * 10,
            Tick = 0,
            Time = 0,
        });
        Assert.True(tds.Destroyed, "TDS layer must be destroyed by the overload hit");

        var point = new Vec3(shielded.Definition.BeamM / 2, -2.5, 0);
        double amount = 800;
        plain.ApplyDamage(HydroShockAt(point, amount, 25) with { TargetId = plain.TargetId });
        shielded.ApplyDamage(HydroShockAt(point, amount, 25) with { TargetId = shielded.TargetId });

        double plainInterior = plain.Parts.Values.Where(p => p.Definition.Kind != PartKind.AntiTorpedo)
            .Sum(p => p.Definition.Hp - p.Hp);
        double shieldedInterior = shielded.Parts.Values.Where(p => p.Definition.Kind != PartKind.AntiTorpedo)
            .Sum(p => p.Definition.Hp - p.Hp);
        Assert.Equal(plainInterior, shieldedInterior, 1);
    }

    [Fact]
    public void Breach_Classes_Order_Flow_And_Patch_Time()
    {
        Assert.True(FloodingSystem.ClassSpec(FloodingSystem.BreachClass.TorpedoLarge).FlowMultiplier
                    > FloodingSystem.ClassSpec(FloodingSystem.BreachClass.BlastMedium).FlowMultiplier);
        Assert.True(FloodingSystem.ClassSpec(FloodingSystem.BreachClass.BlastMedium).FlowMultiplier
                    > FloodingSystem.ClassSpec(FloodingSystem.BreachClass.ShellSmall).FlowMultiplier);
        // W5 patch tiers: shell holes fast, torpedo holes slow, blast between.
        Assert.Equal(5.0, FloodingSystem.ClassSpec(FloodingSystem.BreachClass.ShellSmall).PatchSeconds);
        Assert.Equal(12.0, FloodingSystem.ClassSpec(FloodingSystem.BreachClass.BlastMedium).PatchSeconds);
        Assert.Equal(20.0, FloodingSystem.ClassSpec(FloodingSystem.BreachClass.TorpedoLarge).PatchSeconds);
    }

    [Fact]
    public void Torpedo_Breach_Carries_Large_Class()
    {
        var repo = Repo();
        var flooding = new FloodingSystem(new DamageRegistry());
        var ship = ShipFactory.Create(repo.Ships["uss_fletcher"], "torp");
        flooding.Ships.Add(ship);

        flooding.CreateBreach(ship, new Vec3(2, -1.5, 0), 4.0, FloodingSystem.BreachClass.TorpedoLarge);
        var part = ship.Parts.Values.First(p => p.Breached);
        Assert.Equal(FloodingSystem.BreachClass.TorpedoLarge, part.BreachClass);
    }

    [Fact]
    public void Capital_Unsinkability_Needs_Three_Destroyed_Mid_Sections()
    {
        var repo = Repo();
        var ship = ShipFactory.Create(repo.Ships["uss_north_carolina"], "sink");
        Assert.True(ship.Definition.Class.IsCapital());

        var mid = ship.Sections.Where(s => s.Definition.Role == HullSectionRole.Mid).ToList();
        Assert.True(mid.Count >= 3, "capital templates must author >=3 mid sections for the ruling");

        // Sections die through structure damage (ApplyPoint outside every part box).
        static void WreckSection(Ship s, HullSectionState section)
        {
            s.ApplyDamage(new DamageEvent
            {
                Channel = DamageChannel.Chemical,
                SourceId = "test",
                TargetId = s.TargetId,
                Position = new Vec3(0, 40, (section.Definition.ZMinM + section.Definition.ZMaxM) / 2),
                Amount = section.Hp * 2 + 1000,
                Tick = 0,
                Time = 0,
            });
        }

        WreckSection(ship, mid[0]);
        WreckSection(ship, mid[1]);
        ship.CheckUnsinkability();
        Assert.False(ship.UnsinkabilityLost, "2 destroyed mid sections must NOT lose unsinkability");

        WreckSection(ship, mid[2]);
        ship.CheckUnsinkability();
        Assert.True(ship.UnsinkabilityLost, "3 destroyed mid sections = unsinkability lost (W4)");
    }

    [Fact]
    public void Aa_Mounts_Come_From_Ship_Data_Not_Hardcode()
    {
        var repo = Repo();
        var iowa = repo.Ships["uss_iowa"];
        Assert.NotEmpty(iowa.AaMounts);
        Assert.All(iowa.AaMounts, m =>
        {
            Assert.True(m.RangeM > 0 && m.MuzzleVelocityMs > 0 && m.Count > 0);
            Assert.False(string.IsNullOrWhiteSpace(m.ShellId));
        });
        // Deterministic expansion: BattleRunner spawns exactly Count mounts per group.
        int expected = iowa.AaMounts.Sum(m => m.Count);
        Assert.True(expected >= 4, $"Iowa should carry a real AA battery, got {expected}");
    }
}