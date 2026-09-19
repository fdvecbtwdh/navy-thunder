using NavyThunder.Core.Aviation;
using NavyThunder.Core.Damage;
using NavyThunder.Core.Mathematics;
using NavyThunder.Data;
using Xunit;
using Xunit.Abstractions;

namespace NavyThunder.Core.Tests;

/// <summary>
/// R1.3 + R0.5 acceptance: aircraft fly under the simplified flight model with damage
/// feedback, strike missions release torpedoes/bombs against ships, and loss of control
/// ends in a crash.
/// </summary>
public class AirDeliveryTests(ITestOutputHelper output)
{
    private const string ScenarioFile = "air_strike.json";

    private static (DataRepository Repo, BattleRunner Runner) MakeBattle()
    {
        var repo = DataRepository.LoadFromDirectory(RepoLocator.FindDataDirectory());
        var scenario = BattleScenario.Load(Path.Combine(RepoLocator.FindRepoRoot()!, "scenarios", ScenarioFile));
        return (repo, new BattleRunner(repo, scenario));
    }

    [Fact]
    public void Aircraft_Fly_Toward_Targets_And_Close_Range()
    {
        var (_, runner) = MakeBattle();
        var aircraft = runner.Aircraft[0];
        var state = runner.FlightModel.StateOf(aircraft)!;
        Vec3 start = state.Position;

        runner.World.Step();
        world_Run(runner, 30);

        double startDist = Vec3.Distance(start, state.Mission!.TargetPosition());
        double nowDist = Vec3.Distance(state.Position, state.Mission.TargetPosition());
        Assert.True(nowDist < startDist - 1000, $"aircraft must close on target ({nowDist:0} < {startDist:0} - 1000)");
        Assert.True(aircraft.Alive);
    }

    [Fact]
    public void Torpedo_Bomber_Releases_Inside_Envelope_And_Fish_Runs()
    {
        var (_, runner) = MakeBattle();
        var aircraft = runner.Aircraft.First(a =>
            runner.FlightModel.StateOf(a)!.Mission?.Kind == MissionKind.TorpedoStrike);
        var state = runner.FlightModel.StateOf(aircraft)!;

        world_Run(runner, 240);

        output.WriteLine($"released={state.Mission!.Released} spawned={runner.Torpedoes.SpawnedCount} " +
                         $"alt={state.AltitudeM:0} spd={state.Velocity.Length:0} pos={state.Position}");
        Assert.True(state.Mission.Released, "torpedo bomber must release inside the envelope");
        Assert.True(runner.Torpedoes.SpawnedCount > 0, "the release must drop a fish");
        Assert.Contains(runner.World.Events.Of<NavyThunder.Core.Torpedoes.TorpedoHit>(),
            h => h.TargetId == "ship:test_battleship");
        output.WriteLine($"torpedo hit recorded; battle result={runner.Battle.Result}");
    }

    [Fact]
    public void Bomb_Bomber_Releases_And_Blast_Reaches_The_Ship()
    {
        var (_, runner) = MakeBattle();
        var aircraft = runner.Aircraft.First(a =>
            runner.FlightModel.StateOf(a)!.Mission?.Kind == MissionKind.BombStrike);
        var state = runner.FlightModel.StateOf(aircraft)!;

        world_Run(runner, 300);

        Assert.True(state.Mission!.Released, "bomber must release on approach");
        // The bomb must have burst: a shell detonation with the bomb's id prefix.
        Assert.Contains(runner.World.Events.Of<NavyThunder.Core.Ballistics.ShellDetonation>(),
            d => d.ShellId == "usn_1000lb_an_m64_bomb");
    }

    [Fact]
    public void Uncontrollable_Aircraft_Descends_And_Crashes()
    {
        var (_, runner) = MakeBattle();
        var aircraft = runner.Aircraft[0];
        var state = runner.FlightModel.StateOf(aircraft)!;

        // Destroy all control authority: elevator + its cable + both ailerons.
        foreach (var partId in new[] { "cable_elevator", "elevator", "aileron_left", "aileron_right" })
        {
            var part = aircraft.Parts[partId];
            runner.Registry.Apply(new DamageEvent
            {
                Channel = DamageChannel.Fragment,
                SourceId = "test",
                TargetId = aircraft.TargetId,
                Position = part.Center,
                Amount = part.Definition.Hp + 1,
            });
        }

        Assert.True(aircraft.Alive, "control loss must not be an instant kill");

        world_Run(runner, 60);

        // The crippled airframe descends out of control and strikes the surface.
        Assert.Equal(AircraftState.Destroyed, aircraft.State);
        Assert.NotEmpty(runner.World.Events.Of<AircraftCrashed>());
    }

    [Fact]
    public void Scenario_With_Aircraft_Is_Deterministic()
    {
        var repo = DataRepository.LoadFromDirectory(RepoLocator.FindDataDirectory());
        var scenario = BattleScenario.Load(Path.Combine(RepoLocator.FindRepoRoot()!, "scenarios", "bb_duel.json"));

        var a = new BattleRunner(repo, scenario);
        var b = new BattleRunner(repo, scenario);
        a.Run();
        b.Run();

        Assert.Equal(
            a.Aircraft.Select(ac => $"{ac.State}|{ac.WorldPosition.X:F1}|{ac.WorldPosition.Z:F1}"),
            b.Aircraft.Select(ac => $"{ac.State}|{ac.WorldPosition.X:F1}|{ac.WorldPosition.Z:F1}"));
    }

    private static void world_Run(BattleRunner runner, double seconds)
    {
        // Step until battle end or the requested duration, whichever comes first.
        double target = runner.World.Time + seconds;
        while (runner.Battle.Result == NavyThunder.Core.Battle.BattleResult.Running
               && runner.World.Time < target)
        {
            runner.World.Step();
        }
    }
}
