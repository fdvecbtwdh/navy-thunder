using NavyThunder.Core.Armor;
using NavyThunder.Core.Ballistics;
using NavyThunder.Core.Damage;
using NavyThunder.Core.Explosions;
using NavyThunder.Core.Fire;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Ships;
using NavyThunder.Core.Torpedoes;
using NavyThunder.Core.World;
using NavyThunder.Data;
using Xunit;
using Xunit.Abstractions;

namespace NavyThunder.Core.Tests;

/// <summary>
/// Full-stack combat wiring used by the ship interaction tests: ballistics, explosions,
/// damage bridge, flooding, fire, damage control and kill adjudication around the ships
/// loaded from the repository dataset (the data-driven path, not hand-built entities).
/// </summary>
public sealed class CombatHarness
{
    public SimulationWorld World { get; }
    public BallisticsSystem Ballistics { get; }
    public ExplosionSystem Explosions { get; }
    public DamageBridgeSystem Bridge { get; }
    public FloodingSystem Flooding { get; }
    public FireSystem Fire { get; }
    public DamageControlSystem DamageControl { get; }
    public KillAdjudicatorSystem Adjudicator { get; }
    public TorpedoSystem Torpedoes { get; }
    public DamageRegistry Registry { get; } = new();
    public List<Ship> Ships { get; } = [];
    public Dictionary<string, ArmorTarget> ArmorByTargetId { get; } = [];

    public CombatHarness(DataRepository repo, IEnumerable<string> shipIds, ulong seed = 0x4E617659_5468756E)
    {
        World = new SimulationWorld(fixedDeltaTime: 0.02, masterSeed: seed);
        Shells = repo.Shells;

        var resolver = new ArmorResolver(repo.ToPenetrationCalibration());
        // Deep-water setting so flat test shots at negative Y do not trigger fuseOnWater.
        Ballistics = new BallisticsSystem { Armor = resolver, GroundLevelY = -50 };
        Explosions = new ExplosionSystem(repo.ToExplosionModel(), repo.Shells, Registry);
        Bridge = new DamageBridgeSystem(Registry, repo.Shells);
        Fire = new FireSystem(repo.ToFireModel(), Registry);
        Flooding = new FloodingSystem(Registry, Fire);
        DamageControl = new DamageControlSystem { Fire = Fire, Flooding = Flooding };
        Adjudicator = new KillAdjudicatorSystem(Registry);
        Torpedoes = new TorpedoSystem(Registry, Flooding);

        foreach (var shipId in shipIds)
        {
            var ship = ShipFactory.Create(repo.Ships[shipId]);
            Ships.Add(ship);
            World.AddEntity(ship);
            Registry.Register(ship);
            Bridge.ShipsByTargetId[ship.TargetId] = ship;
            Flooding.Ships.Add(ship);
            DamageControl.Ships.Add(ship);
            Adjudicator.Ships.Add(ship);

            var armor = ShipFactory.BuildArmorTarget(ship);
            ArmorByTargetId[ship.TargetId] = armor;
            Ballistics.Targets.Add(armor);
            Explosions.Targets.Add(armor);
        }

        World.AddSystem(Ballistics);
        World.AddSystem(Explosions);
        World.AddSystem(Bridge);
        World.AddSystem(Flooding);
        World.AddSystem(Fire);
        World.AddSystem(DamageControl);
        World.AddSystem(Adjudicator);
        World.AddSystem(Torpedoes);
    }

    /// <summary>Fires a shell from 500 m off the ship's port side (travelling +Z) at a ship-local aim point.</summary>
    public void FireShellAt(Ship ship, string shellId, Vec3 aimPoint, double impactSpeed)
    {
        Ballistics.Spawn(new BallisticProjectile
        {
            Position = aimPoint + ship.WorldPosition + new Vec3(0, 0, -500),
            Velocity = new Vec3(0, 0, impactSpeed),
            MassKg = 100,
            Shell = Shells[shellId],
        });
    }

    public IReadOnlyDictionary<string, NavyThunder.Core.Model.ShellDefinition> Shells { get; }
}

public class ShipDataDrivenTests
{
    private static DataRepository Repo() => DataRepository.LoadFromDirectory(RepoLocator.FindDataDirectory());

    [Fact]
    public void Repository_Loads_Sample_Ships()
    {
        var repo = Repo();
        Assert.True(repo.Ships.Count >= 2);
        var dd = repo.Ships["test_destroyer"];
        Assert.Equal(NavyThunder.Core.Model.ShipClass.Destroyer, dd.Class);
        Assert.Equal(2, dd.HullSections.Count(s => s.Role == NavyThunder.Core.Model.HullSectionRole.Mid)
                        + dd.HullSections.Count(s => s.Role == NavyThunder.Core.Model.HullSectionRole.Bow));
    }

    [Fact]
    public void Ap_Into_Battleship_Citadel_Detonates_Magazine_And_Destroys_Ship()
    {
        var repo = Repo();
        var harness = new CombatHarness(repo, ["test_battleship"]);
        var ship = harness.Ships[0];

        // Mk8 APCBC at point-blank into the port belt behind which magazine A sits.
        harness.FireShellAt(ship, "usn_406mm_mk8_mod6_apcbc", new Vec3(40, -4, -15), 762);
        harness.World.Run(30);

        var detonation = Assert.Single(harness.World.Events.Of<MagazineDetonation>());
        Assert.Equal(ship.TargetId, detonation.ShipId);
        Assert.True(ship.Lost);
        Assert.Equal(ShipKillState.Destroyed, ship.KillState);
        Assert.Equal("magazine_detonation", ship.KillReason);
    }

    [Fact]
    public void Torpedo_Hit_Floods_And_Can_Capsize_The_Target()
    {
        var repo = Repo();
        var harness = new CombatHarness(repo, ["test_destroyer"]);
        var ship = harness.Ships[0];

        var type93 = repo.Torpedoes["ijn_610mm_type93_mod1_mod2"];
        var armor = harness.ArmorByTargetId[ship.TargetId];
        harness.Torpedoes.Spawn(type93, ship.TargetId, armor,
            new Vec3(-14, -2, -2000), new Vec3(0, 0, 1)); // runs at 2 m depth at the aft magazine

        harness.World.Run(120);

        Assert.NotEmpty(harness.World.Events.Of<TorpedoHit>());
        Assert.True(ship.Parts.Values.Any(p => p.Breached), "torpedo must open a breach");
        Assert.True(ship.BuoyancyLossPct > 5, $"buoyancy loss = {ship.BuoyancyLossPct:0.#} %");
        // With the coarse centerline layout of the sample ships, heavy flooding either
        // sinks her or at minimum builds dangerous list (port/starboard split lands in Phase 7).
        Assert.True(Math.Abs(ship.ListDeg) > 3 || ship.BuoyancyLossPct >= 30 || ship.Lost,
            $"list={ship.ListDeg:0.#} buoyancy={ship.BuoyancyLossPct:0.#}");
    }

    [Fact]
    public void Sustained_Damage_Below_Survival_Crew_Scuttles_The_Ship()
    {
        var repo = Repo();
        var harness = new CombatHarness(repo, ["test_destroyer"]);
        var ship = harness.Ships[0];

        // Wipe crewed compartments round-robin; the survivors' threshold then scuttles her.
        int guard = 0;
        while (!ship.Lost && guard++ < 100)
        {
            foreach (var part in ship.Parts.Values
                         .Where(p => !p.Destroyed && p.Definition.Crew > 0
                                     && p.Definition.Kind != NavyThunder.Core.Model.PartKind.Magazine).ToList())
            {
                harness.Registry.Apply(new DamageEvent
                {
                    Channel = DamageChannel.Kinetic,
                    SourceId = "grind",
                    TargetId = ship.TargetId,
                    Position = part.Center,
                    Amount = part.Definition.Hp + 100,
                });
            }
        }

        harness.World.Step();
        Assert.True(ship.Lost);
        Assert.Equal(ShipKillState.Scuttled, ship.KillState);
    }

    [Fact]
    public void Engine_Destroyed_Reduces_Speed_And_Fire_Burns_Crew()
    {
        var repo = Repo();
        var harness = new CombatHarness(repo, ["test_destroyer"]);
        var ship = harness.Ships[0];

        var engine = ship.Parts["dd_engine"];
        Assert.Equal(1.0, ship.SpeedFactor, 12);

        // Fire on the engine kills its crew over time until extinguished.
        var fire = harness.Fire;
        fire.SetFlammability($"{ship.TargetId}/dd_engine", 100);
        Assert.True(fire.TryIgnite(harness.World, $"{ship.TargetId}/dd_engine", "engine", engine.Center));
        double crewBefore = ship.CrewDead;
        harness.World.Run(5);
        Assert.True(ship.CrewDead > crewBefore, "fire must burn the compartment crew");
        Assert.True(fire.Extinguish(harness.World, $"{ship.TargetId}/dd_engine"));

        harness.Registry.Apply(new DamageEvent
        {
            Channel = DamageChannel.Kinetic,
            SourceId = "test",
            TargetId = ship.TargetId,
            Position = engine.Center,
            Amount = engine.Definition.Hp + 1,
        });

        Assert.True(engine.Destroyed);
        Assert.True(ship.SpeedFactor < 1.0);
    }

    [Fact]
    public void DamageControl_Patches_Breaches_Unless_Crew_Is_Below_The_Repair_Threshold()
    {
        var repo = Repo();
        var harness = new CombatHarness(repo, ["test_destroyer"]);
        var ship = harness.Ships[0];
        var flooding = harness.Flooding;

        flooding.CreateBreach(ship, new Vec3(0, -2, 0), 2.0);
        var floodedPart = ship.Parts.Values.First(p => p.Breached);
        harness.World.Run(30);
        Assert.False(floodedPart.Breached); // patched by DC within the patch window

        // Now push crew below the repair threshold: no DC action possible.
        ship.ForceCrewDead(ship.Definition.CrewTotal - ship.Definition.CrewRepairThreshold + 5);
        flooding.CreateBreach(ship, new Vec3(0, -2, 0), 2.0);
        var second = ship.Parts.Values.First(p => p.Breached);
        harness.World.Run(30);
        Assert.True(second.Breached);
    }

    [Fact]
    public void Destroyer_Loses_Unsinkability_From_Any_Section_Battleship_Needs_Two_Mid()
    {
        var repo = Repo();
        var harness = new CombatHarness(repo, ["test_destroyer", "test_battleship"]);
        var dd = harness.Ships[0];
        var bb = harness.Ships[1];

        dd.ForceSectionDestroyed("dd_bow");
        dd.CheckUnsinkability();
        Assert.True(dd.UnsinkabilityLost);

        bb.ForceSectionDestroyed("bb_bow");
        bb.CheckUnsinkability();
        Assert.False(bb.UnsinkabilityLost); // the bow never counts

        bb.ForceSectionDestroyed("bb_mid1");
        bb.CheckUnsinkability();
        Assert.False(bb.UnsinkabilityLost); // one mid section is not enough either

        bb.ForceSectionDestroyed("bb_mid2");
        bb.CheckUnsinkability();
        Assert.True(bb.UnsinkabilityLost); // two mid sections: unsinkability lost
    }

    [Fact]
    public void First_Stage_Ammo_Resupplies_From_The_Main_Magazine()
    {
        var repo = Repo();
        var harness = new CombatHarness(repo, ["test_destroyer"]);
        var ship = harness.Ships[0];
        var world = harness.World;

        for (int i = 0; i < ship.Definition.FirstStageRoundsPerTurret; i++)
        {
            ship.ConsumeReadyRack(world, "A");
        }

        Assert.Equal(0, ship.ReadyRackCount("A"));
        Assert.True(ship.CurrentReloadFactor(world, "A") > 1.0); // degraded reload from main magazine

        // Firing pauses the resupply: keep firing and the rack stays empty.
        world.Run(10);
        ship.ConsumeReadyRack(world, "A");
        world.Run(10);
        Assert.Equal(0, ship.ReadyRackCount("A"));

        // Stop firing: the ~35 s resupply completes and the nominal reload factor returns.
        world.Run(40);
        Assert.Equal(ship.Definition.FirstStageRoundsPerTurret, ship.ReadyRackCount("A"));
        Assert.Equal(1.0, ship.CurrentReloadFactor(world, "A"));
    }

    [Fact]
    public void Overpressure_Kills_Open_Mount_Crews_Only()
    {
        var repo = Repo();
        var harness = new CombatHarness(repo, ["test_destroyer"]);
        var ship = harness.Ships[0];

        double before = ship.CrewDead;
        harness.Registry.Apply(new DamageEvent
        {
            Channel = DamageChannel.Overpressure,
            SourceId = "he_burst",
            TargetId = ship.TargetId,
            Position = new Vec3(0, 4, 0),
            Radius = 8,
            Amount = 2000,
        });

        // The AA mount (open, crew 10) sits in the burst radius and is wiped; the
        // enclosed bridge keeps its crew and full HP - overpressure never touches HP.
        Assert.True(ship.CrewDead > before);
        Assert.Equal(1.0, ship.Parts["dd_aa_mount"].CrewDeadFraction);
        Assert.Equal(0.0, ship.Parts["dd_bridge"].CrewDeadFraction);
        Assert.Equal(300, ship.Parts["dd_bridge"].Hp);
    }
}
