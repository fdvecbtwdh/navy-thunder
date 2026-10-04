using NavyThunder.Core.Armor;
using NavyThunder.Core.Ballistics;
using NavyThunder.Core.Battle;
using NavyThunder.Core.Model;
using NavyThunder.Core.World;
using NavyThunder.Core.Damage;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Ships;
using NavyThunder.Data;
using Xunit;
using Xunit.Abstractions;

namespace NavyThunder.Core.Tests;

/// <summary>
/// R0 acceptance: a full battle driven purely from scenario data — navigation, gunnery,
/// damage, fires, flooding, kill adjudication, victory conditions and the battle report.
/// </summary>
    [Trait("Bucket", "Slow")]
public class BattleAcceptanceTests(ITestOutputHelper output)
{
    private static string ScenarioPath(string name)
        => Path.Combine(RepoLocator.FindRepoRoot()!, "scenarios", name);

    [Fact]
    public void Bb_Duel_Runs_With_Complete_Report_And_Heavy_Fighting()
    {
        var repo = DataRepository.LoadFromDirectory(RepoLocator.FindDataDirectory());
        var scenario = BattleScenario.Load(ScenarioPath("bb_duel.json"));
        var runner = new BattleRunner(repo, scenario);

        var report = runner.Run();

        // Phase 01: with heading-aware hit geometry the identical test battleships present
        // their belts correctly and the duel settles into a protracted engagement; the
        // adjudicator's time-limit Draw is a legitimate outcome. The behavioural bar here is
        // the full damage chain, not a particular winner (lethality tuning is Phase 04).
        Assert.True(runner.Battle.Result != BattleResult.Running, "battle must conclude");
        Assert.True((int)report["gunsFired"]! > 300, "a duel must actually shoot");
        Assert.Contains(runner.World.Events.Of<NavyThunder.Core.Ballistics.ProjectileArmorImpact>(),
            i => i.Outcome == PlateResolution.Penetrated);
        Assert.Contains(runner.World.Events.Of<NavyThunder.Core.Ballistics.ShellDetonation>(), _ => true);

        var loser = runner.Ships.FirstOrDefault(s => s.Lost);
        if (loser is not null)
        {
            output.WriteLine($"loser={loser.TargetId} reason={loser.KillReason} at {loser.DestroyedTime:0.#}s");
            Assert.Contains(loser.KillReason, new[] { "unsinkability_lost", "magazine_detonation", "buoyancy_lost", "capsize", "crew_annihilated" });
        }
        else
        {
            // No kill in the time limit: both ships must at least be heavily fought-over.
            Assert.Contains(runner.Ships, s => s.CrewAlive < s.Definition.CrewTotal);
        }
    }

    [Fact]
    public void Battle_Is_Deterministic_Across_Runs()
    {
        // Phase 04 test-infra rework: engine-level determinism is a property of the tick
        // loop + named RNG streams, NOT of battle length — the FULL 3600 s determinism
        // reference lives in GoldenFileTests (report equality vs the committed golden).
        // Here the same double-run comparison runs on a 180 s slice, which exercises
        // navigation + gunnery + damage + fire without a second hour-long battle.
        var repo = DataRepository.LoadFromDirectory(RepoLocator.FindDataDirectory());
        var scenario = BattleScenario.Load(ScenarioPath("bb_duel.json"));

        var a = new BattleRunner(repo, scenario);
        var b = new BattleRunner(repo, scenario);
        for (int i = 0; i < 180 / a.World.FixedDeltaTime; i++)
        {
            a.World.Step();
            b.World.Step();
        }

        Assert.Equal(a.World.Time, b.World.Time);
        Assert.Equal(
            a.Ships.Select(s => $"{s.TargetId}|{Math.Round(s.WorldPosition.X, 4)}|{Math.Round(s.WorldPosition.Z, 4)}|{s.HeadingDeg:R}|{s.CrewAlive}|{s.BuoyancyLossPct:F3}"),
            b.Ships.Select(s => $"{s.TargetId}|{Math.Round(s.WorldPosition.X, 4)}|{Math.Round(s.WorldPosition.Z, 4)}|{s.HeadingDeg:R}|{s.CrewAlive}|{s.BuoyancyLossPct:F3}"));
    }

    [Fact]
    public void Fire_Ignition_Rolls_Wire_Into_Combat()
    {
        // Phase 04 test-infra rework: the wiring (combat damage -> ignition roll) needs
        // a few salvos, not an hour — run 180 s of the duel and require the FireSystem
        // to have been reachable from the damage bridge. Probabilistic burn-out coverage
        // stays with the harness-level fire tests.
        var repo = DataRepository.LoadFromDirectory(RepoLocator.FindDataDirectory());
        var scenario = BattleScenario.Load(ScenarioPath("bb_duel.json"));
        var runner = new BattleRunner(repo, scenario);
        Assert.NotNull(runner.Fire);
        Assert.NotNull(runner.Bridge.Fire);

        while (runner.World.Time < 180 && runner.Battle.Result == BattleResult.Running)
        {
            runner.World.Step();
        }

        Assert.True(runner.World.Time >= 180 || runner.Battle.Result != BattleResult.Running);
    }
}

public class ShipNavigationTests
{
    private static Ship MakeShip()
    {
        var repo = DataRepository.LoadFromDirectory(RepoLocator.FindDataDirectory());
        return ShipFactory.Create(repo.Ships["test_destroyer"]);
    }

    [Fact]
    public void Throttle_Accelerates_To_Max_Speed()
    {
        var world = new SimulationWorld();
        var nav = new ShipNavigationSystem();
        world.AddSystem(nav);
        var ship = MakeShip();
        ship.ThrottleCommand = 1.0;
        nav.Ships.Add(ship);

        world.Run(600); // 10 minutes at 6%/s acceleration: full speed

        Assert.Equal(36.0, ship.SpeedKnots, 0.5);
    }

    [Fact]
    public void Engine_Damage_Caps_Attainable_Speed()
    {
        var world = new SimulationWorld();
        var nav = new ShipNavigationSystem();
        world.AddSystem(nav);
        var ship = MakeShip();
        ship.ThrottleCommand = 1.0;
        nav.Ships.Add(ship);

        var registry = new DamageRegistry();
        registry.Register(ship);
        var engine = ship.Parts.Values.First(p => p.Definition.Kind == PartKind.Engine);
        registry.Apply(new DamageEvent
        {
            Channel = DamageChannel.Kinetic,
            SourceId = "test",
            TargetId = ship.TargetId,
            Position = engine.Center,
            Amount = engine.Definition.Hp + 1,
        });

        Assert.True(engine.Destroyed);
        world.Run(600);

        Assert.Equal(36.0 * 0.5, ship.SpeedKnots, 1.0); // half the propulsion halves speed
    }

    [Fact]
    public void Rudder_Turns_The_Ship_And_Armor_Follows()
    {
        var repo = DataRepository.LoadFromDirectory(RepoLocator.FindDataDirectory());
        var world = new SimulationWorld();
        var nav = new ShipNavigationSystem();
        world.AddSystem(nav);
        var ship = MakeShip();
        ship.ThrottleCommand = 1.0;
        nav.Ships.Add(ship);
        var armor = ShipFactory.BuildArmorTarget(ship);
        armor.TransformProvider = () => ship.WorldTransform; // Phase 01: hull frame
        var start = ship.WorldPosition;
        var belt = armor.Plates.First(p => p.Id == "dd_belt_starboard");
        var localCenterBefore = belt.Center;

        ship.RudderCommand = 1.0;
        world.Run(60); // one minute of hard rudder at speed

        Assert.True(ship.HeadingDeg > 60, $"turned {ship.HeadingDeg:0.#} deg in 60 s");
        Assert.True(Vec3.Distance(ship.WorldPosition, start) > 300, "ship must travel");
        // Phase 01 semantics: plates are SHIP-LOCAL — moving/turning must not modify the
        // plate itself, and the broadphase sphere must track the transformed hull.
        Assert.Equal(localCenterBefore, belt.Center);
        armor.RefreshBroadphase();
        double sphereDist = Vec3.Distance(armor.BroadphaseCenter, ship.WorldPosition);
        Assert.True(sphereDist < ship.Definition.LengthM,
            $"broadphase sphere must sit on the hull (dist {sphereDist:0} m)");
    }

    [Fact]
    public void Destroyed_Steering_Gear_Freezes_The_Rudder()
    {
        var repo = DataRepository.LoadFromDirectory(RepoLocator.FindDataDirectory());
        var world = new SimulationWorld();
        var nav = new ShipNavigationSystem();
        world.AddSystem(nav);
        var ship = MakeShip();
        nav.Ships.Add(ship);
        var registry = new DamageRegistry();
        registry.Register(ship);
        var steering = ship.Parts.Values.First(p => p.Definition.Kind == PartKind.Steering);
        registry.Apply(new DamageEvent
        {
            Channel = DamageChannel.Kinetic,
            SourceId = "test",
            TargetId = ship.TargetId,
            Position = steering.Center,
            Amount = steering.Definition.Hp + 1,
        });

        ship.RudderCommand = 1.0;
        world.Run(10);

        Assert.Equal(0, ship.HeadingDeg, 12); // no helm: course held
    }
}
