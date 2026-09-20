using NavyThunder.Core.Battle;
using NavyThunder.Core.World;
using NavyThunder.Data;
using Xunit;
using Xunit.Abstractions;

namespace NavyThunder.Core.Tests;

/// <summary>
/// R1.7 headless acceptance: seeded AI-vs-AI battles (no player input) must produce every
/// core damage outcome at least once — fires, flooding, a magazine detonation, an aircraft
/// shootdown, ship kills and decisive victories — plus complete battle reports.
/// Deterministic per scenario seed; the naval duel carries the magazine-detonation bar
/// (AP volume into magazine boxes needs time), the combined-arms battle carries AA.
/// </summary>
public class HeadlessAcceptanceTests(ITestOutputHelper output)
{
    [Fact]
    public void Naval_Duel_Shows_Fires_Flooding_Detonation_Kills_And_Victory()
    {
        var runner = RunScenario("r1_naval_duel.json");

        Assert.Equal(BattleResult.TeamWin, runner.Battle.Result);
        Assert.True(runner.Battle.GunsFired > 50, "the AI battle must actually fight");

        // 起火: at least one fire ignited during the battle.
        Assert.True(runner.Fire.Fires.Count > 0, "expected at least one fire");

        // 进水: a breach led to water inside at least one hull part.
        Assert.Contains(runner.Ships, s => s.Parts.Values.Any(p => p.WaterLevel > 0));

        // 殉爆: a magazine detonation occurred.
        Assert.Contains(runner.World.Events.Of<NavyThunder.Core.Ships.MagazineDetonation>(), _ => true);

        // 击沉: at least one ship destroyed by damage.
        Assert.Contains(runner.Ships, s => s.Lost);

        output.WriteLine($"duel: result={runner.Battle.Result} winner={runner.Battle.WinnerTeamId} " +
                         $"fires={runner.Fire.Fires.Count} shipsLost={runner.Ships.Count(s => s.Lost)}");
    }

    [Fact]
    public void Combined_Arms_Battle_Shoots_Down_Aircraft_And_Decides()
    {
        var runner = RunScenario("r1_acceptance.json");

        // 胜负: decisive victory, not a timeout.
        Assert.Equal(BattleResult.TeamWin, runner.Battle.Result);
        Assert.NotNull(runner.Battle.WinnerTeamId);

        // 起火 / 进水 / 击沉 also occur in the combined-arms battle.
        Assert.True(runner.Fire.Fires.Count > 0, "expected at least one fire");
        Assert.Contains(runner.Ships, s => s.Parts.Values.Any(p => p.WaterLevel > 0));
        Assert.Contains(runner.Ships, s => s.Lost);

        // 击落: at least one aircraft shot down (VT airbursts) or lost in the strike.
        var lostAircraft = runner.Aircraft.Count(a => !a.Alive);
        Assert.True(lostAircraft > 0, "expected at least one aircraft lost");

        output.WriteLine($"combined: result={runner.Battle.Result} winner={runner.Battle.WinnerTeamId} " +
                         $"fires={runner.Fire.Fires.Count} aircraftLost={lostAircraft} " +
                         $"aaShots={runner.AntiAir.ShotsFired}");
    }

    private static BattleRunner RunScenario(string file)
    {
        var repo = DataRepository.LoadFromDirectory(RepoLocator.FindDataDirectory());
        var scenario = BattleScenario.Load(Path.Combine(RepoLocator.FindRepoRoot()!, "scenarios", file));
        var runner = new BattleRunner(repo, scenario);
        runner.Run();
        return runner;
    }
}
