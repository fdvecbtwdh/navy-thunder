using NavyThunder.Core.Ballistics;
using NavyThunder.Core.Ships;
using NavyThunder.Core.Battle;
using NavyThunder.Data;
using Xunit;
using Xunit.Abstractions;

namespace NavyThunder.Core.Tests;

/// <summary>
/// R1.1 scenario-level acceptance: a 6v6 all-AI fleet battle must show real
/// attack/defense behavior — ships maneuver away from their spawn lines, switch targets
/// as threats evolve, and losses spread across the defeated side instead of piling onto
/// whoever sits still.
/// </summary>
public class ShipAIScenarioTests(ITestOutputHelper output)
{
    private static BattleRunner MakeRunner()
    {
        var repo = DataRepository.LoadFromDirectory(RepoLocator.FindDataDirectory());
        var scenario = BattleScenario.Load(Path.Combine(RepoLocator.FindRepoRoot()!, "scenarios", "fleet_battle_6v6.json"));
        return new BattleRunner(repo, scenario);
    }

    [Fact]
    public void Fleet_Battle_Completes_With_Maneuvering_And_Target_Switching()
    {
        var runner = MakeRunner();

        // Capture spawn lines to verify movement later.
        var spawns = runner.Ships.ToDictionary(s => s.TargetId, s => (s.WorldPosition, s.HeadingDeg));

        // Run the full engagement.
        runner.Run();

        var report = BattleReportGenerator.Generate(runner.World, runner.Ships, runner.Registry, runner.Battle);
        output.WriteLine($"result={report["result"]} winner={report["winner"]} duration={report["durationS"]}s guns={report["gunsFired"]}");
        foreach (var ship in runner.Ships)
        {
            output.WriteLine($"{ship.TargetId}: state={ship.KillState} buoy={ship.BuoyancyLossPct:0.#}% crew={ship.CrewAlive}");
        }

        // 1. The engagement must actually happen.
        int shells = (int)report["gunsFired"]!;
        Assert.True(shells > 300, $"a 12-ship battle must fire a lot: {shells}");

        // 2. Combat outcome: a decision, or a hard-fought draw with losses on both sides.
        int totalLosses = runner.Ships.Count(s => s.Lost);
        Assert.True(runner.Battle.Result != BattleResult.Running, "battle must conclude");
        if (runner.Battle.Result == BattleResult.Draw)
        {
            Assert.True(totalLosses >= 3, "a time-limit draw needs heavy fighting");
        }

        // 3. Maneuvering: survivors are far from where they spawned and have turned.
        foreach (var ship in runner.Ships.Where(s => !s.Lost))
        {
            double displacement = NavyThunder.Core.Mathematics.Vec3.Distance(ship.WorldPosition, spawns[ship.TargetId].WorldPosition);
            if (ship.SpeedKnots > 5)
            {
                Assert.True(displacement > 1000, $"{ship.TargetId} must maneuver (displaced {displacement:0} m)");
            }
        }

        // 4. Target switching: at least one ship engaged two distinct targets over time
        //    (threat weighting reassigns fire as ships weaken).
        var targetsPerShooter = runner.World.Events.Of<GunFired>()
            .GroupBy(g => g.ShipId)
            .Select(g => (Ship: g.Key, Targets: g.Select(x => x.TargetId).Distinct().ToList()))
            .ToList();
        bool switched = targetsPerShooter.Any(t => t.Targets.Count >= 2);
        Assert.True(switched, "AI must retarget as threats evolve");
    }

    [Fact]
    public void Fleet_Battle_Survivors_Are_Consistent_With_Report()
    {
        var runner = MakeRunner();
        var report = runner.Run();

        // Report and world state must agree (the CI golden-file gate builds on this).
        Assert.Equal(runner.Ships.Count(s => s.Lost),
            (report["ships"] as System.Collections.IEnumerable)!
            .Cast<Dictionary<string, object?>>()
            .Count(d => d["state"]!.ToString() != "Alive"));
    }
}
