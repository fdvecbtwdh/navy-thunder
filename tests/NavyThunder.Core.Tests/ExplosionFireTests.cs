using NavyThunder.Core.Armor;
using NavyThunder.Core.Ballistics;
using NavyThunder.Core.Damage;
using NavyThunder.Core.Explosions;
using NavyThunder.Core.Fire;
using NavyThunder.Core.Geometry;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Model;
using NavyThunder.Core.World;
using Xunit;

namespace NavyThunder.Core.Tests;

public class ExplosionSystemTests
{
    private static readonly ShellDefinition Mk13He = new()
    {
        Id = "usn_406mm_mk13_hc",
        DisplayName = "406mm Mk13 HC",
        Category = ShellCategory.HE,
        CaliberMm = 406,
        MassKg = 862,
        MuzzleVelocityMs = 803,
        ExplosiveMassKg = 69.67,
        FuseDelayS = 0.001,
        ExplodeThresholdMm = 0.1,
        DemarrePenetrationK = 0.15,
    };

    private static readonly ShellDefinition TinyAphe = new()
    {
        Id = "test_tiny_aphe",
        DisplayName = "Tiny APHE",
        Category = ShellCategory.AP,
        CaliberMm = 76,
        MassKg = 6.8,
        MuzzleVelocityMs = 750,
        ExplosiveMassKg = 0.1, // below the 170 g overpressure threshold
        FuseDelayS = 0.02,
        ExplodeThresholdMm = 15,
        DemarrePenetrationK = 1.0,
    };

    private static readonly IReadOnlyDictionary<string, ShellDefinition> ShellTable =
        new Dictionary<string, ShellDefinition>
        {
            [Mk13He.Id] = Mk13He,
            [TinyAphe.Id] = TinyAphe,
            [TestShells.Mk8.Id] = TestShells.Mk8,
        };

    private static ArmorTarget PlateAt(string id, double x, double thicknessMm)
    {
        return new ArmorTarget { Id = id }
            .Add(new ArmorPlate
            {
                Id = $"{id}_p",
                Center = new Vec3(x, 0, 0),
                Normal = new Vec3(-1, 0, 0),
                AxisU = new Vec3(0, 1, 0),
                AxisV = new Vec3(0, 0, 1),
                HalfU = 60,
                HalfV = 60,
                ThicknessMm = thicknessMm,
            });
    }

    private static (SimulationWorld, ExplosionSystem, DamageRegistry) MakeWorld(params ArmorTarget[] targets)
    {
        var world = new SimulationWorld();
        var registry = new DamageRegistry();
        var explosions = new ExplosionSystem(new ExplosionModel(), ShellTable, registry);
        foreach (var t in targets)
        {
            explosions.Targets.Add(t);
        }

        world.AddSystem(explosions);
        return (world, explosions, registry);
    }

    private static ShellDetonation Detonate(string shellId, Vec3 pos, Vec3 vel) => new()
    {
        ShellId = shellId,
        Position = pos,
        Velocity = vel,
    };

    [Fact]
    public void Blast_Damage_Decreases_With_Distance_And_Stops_At_Radius()
    {
        // Mk13 HE: blast radius = 4.0 * cbrt(69.67) ≈ 16.4 m.
        var close = PlateAt("close", 5, 500);       // 5 m from burst
        var far = PlateAt("far", 14, 500);          // 14 m — inside, falloff visible
        var outside = PlateAt("outside", 300, 500); // way beyond blast radius
        var (world, _, registry) = MakeWorld(close, far, outside);

        world.Record(Detonate(Mk13He.Id, Vec3.Zero, new Vec3(1, 0, 0)));
        world.Step();

        var blasts = registry.Log.Where(e => e.Channel == DamageChannel.Chemical).ToList();
        var closeAmount = blasts.Single(e => e.TargetId == "close").Amount;
        var farAmount = blasts.Single(e => e.TargetId == "far").Amount;
        Assert.True(closeAmount > farAmount, "blast damage must fall off with distance");
        Assert.True(farAmount > 0);
        Assert.DoesNotContain(blasts, e => e.TargetId == "outside");
    }

    [Fact]
    public void Brisant_Punch_Breaches_Thin_Plates_At_The_Burst_Point()
    {
        // Mk13 HE: cbrt(69.67) ≈ 4.1 → punch ≈ 123 mm: breaches 10 mm, not 300 mm.
        var thin = PlateAt("thin", 0.5, 10);
        var heavy = PlateAt("heavy", 0.5, 300);
        var (world, _, _) = MakeWorld(thin, heavy);

        world.Record(Detonate(Mk13He.Id, Vec3.Zero, new Vec3(1, 0, 0)));
        world.Step();

        var breaches = world.Events.Of<ArmorBreach>().ToList();
        Assert.Contains(breaches, b => b.PlateId == "thin_p");
        Assert.DoesNotContain(breaches, b => b.PlateId == "heavy_p");
    }

    [Fact]
    public void Fragment_Cone_Strikes_Forward_Arc_Only_And_Stops_On_Thick_Plates()
    {
        // Cone axis +X; a plate 10 m ahead is inside the 37.5° half-angle, one 10 m
        // behind is not. 300 mm plates stop every fragment (frag pen ≈ 33 mm).
        var ahead = PlateAt("ahead", 10, 300);
        var behind = PlateAt("behind", -10, 300);
        var (world, _, registry) = MakeWorld(ahead, behind);

        world.Record(Detonate(Mk13He.Id, Vec3.Zero, new Vec3(1, 0, 0)));
        world.Step();

        var impacts = world.Events.Of<FragmentImpact>().ToList();
        Assert.NotEmpty(impacts);
        Assert.All(impacts, f => Assert.Equal("ahead", f.TargetId));
        Assert.All(impacts, f => Assert.False(f.Penetrated));
        Assert.Empty(registry.Log.Where(e => e.Channel == DamageChannel.Fragment));
    }

    [Fact]
    public void Fragments_Penetrate_Thin_Plates_And_Deal_Fragment_Damage()
    {
        var thin = PlateAt("thin", 10, 5); // 5 mm << ~33 mm fragment pen
        var (world, _, registry) = MakeWorld(thin);

        world.Record(Detonate(Mk13He.Id, Vec3.Zero, new Vec3(1, 0, 0)));
        world.Step();

        var impacts = world.Events.Of<FragmentImpact>().ToList();
        Assert.NotEmpty(impacts);
        Assert.True(impacts.Count(f => f.Penetrated) > 0);
        Assert.NotEmpty(registry.Log.Where(e => e.Channel == DamageChannel.Fragment));
    }

    [Fact]
    public void Overpressure_Follows_The_Official_Rule_Set()
    {
        var (worldHe, _, _) = MakeWorld();
        worldHe.Record(Detonate(Mk13He.Id, Vec3.Zero, new Vec3(1, 0, 0)));
        worldHe.Record(Detonate(TestShells.Mk8.Id, Vec3.Zero, new Vec3(1, 0, 0))); // APHE 18.55 kg >= 0.17 kg
        worldHe.Step();

        Assert.Equal(2, worldHe.Events.Of<OverpressureWave>().Count());

        var (worldTiny, _, _) = MakeWorld();
        worldTiny.Record(Detonate(TinyAphe.Id, Vec3.Zero, new Vec3(1, 0, 0))); // APHE 0.1 kg < 0.17 kg
        worldTiny.Step();

        Assert.Empty(worldTiny.Events.Of<OverpressureWave>());
    }

    [Fact]
    public void Explosion_Processing_Is_Deterministic()
    {
        var (a, _, _) = MakeWorld(PlateAt("p", 10, 5));
        var (b, _, _) = MakeWorld(PlateAt("p", 10, 5));
        a.Record(Detonate(Mk13He.Id, Vec3.Zero, new Vec3(1, 0, 0)));
        b.Record(Detonate(Mk13He.Id, Vec3.Zero, new Vec3(1, 0, 0)));
        a.Step();
        b.Step();

        var fa = a.Events.Of<FragmentImpact>().Select(f => $"{f.PlateId}|{f.Position.X:F4}").Take(200);
        var fb = b.Events.Of<FragmentImpact>().Select(f => $"{f.PlateId}|{f.Position.X:F4}").Take(200);
        Assert.Equal(fa, fb);
    }
}

public class FireSystemTests
{
    private static (SimulationWorld, FireSystem, DamageRegistry) MakeWorld(double baseProbability = 0.05)
    {
        var world = new SimulationWorld();
        var registry = new DamageRegistry();
        var fire = new FireSystem(new FireModel { IgnitionBaseProbability = baseProbability }, registry);
        world.AddSystem(fire);
        return (world, fire, registry);
    }

    [Fact]
    public void Ignition_Chance_Multiplies_Flammability_And_Is_Hp_Independent()
    {
        var (world, fire, _) = MakeWorld(baseProbability: 0.05);
        fire.SetFlammability("dry", 30);      // 0.05 * 30 = 1.5 → always ignites
        fire.SetFlammability("wet", 0.0);     // never ignites

        Assert.True(fire.TryIgnite(world, "dry", "compartment", Vec3.Zero));
        Assert.False(fire.TryIgnite(world, "wet", "compartment", Vec3.Zero));
        Assert.False(fire.TryIgnite(world, "dry", "compartment", Vec3.Zero)); // already burning
    }

    [Fact]
    public void Fire_Burns_Its_Host_Until_Extinguished()
    {
        var (world, fire, registry) = MakeWorld(baseProbability: 1.0);

        Assert.True(fire.TryIgnite(world, "comp_1", "compartment", Vec3.Zero));
        world.Run(2.0);
        double burned = registry.Log.Where(e => e.Channel == DamageChannel.Fire).Sum(e => e.Amount);
        Assert.Equal(15.0 * 2.0, burned, 0.5); // default compartment DPS

        Assert.True(fire.Extinguish(world, "comp_1"));
        double before = registry.Log.Count;
        world.Run(1.0);
        Assert.Equal(before, registry.Log.Count); // extinguished: nothing new
    }

    [Fact]
    public void Flooding_Drowns_Fires()
    {
        var (world, fire, registry) = MakeWorld(baseProbability: 1.0);
        fire.TryIgnite(world, "comp_2", "compartment", Vec3.Zero);
        world.Run(0.5);
        Assert.NotEmpty(registry.Log);

        fire.FloodHost(world, "comp_2");
        int count = registry.Log.Count;
        world.Run(0.5);
        Assert.Equal(count, registry.Log.Count);
    }
}
