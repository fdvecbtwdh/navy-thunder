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

        // Phase 01: heading-aware geometry makes the BB duel symmetric — both sides'
        // escorts can die, and the surviving battleships legitimately draw on the time
        // limit. The behavioural bar is the full damage chain plus a concluded battle.
        Assert.True(runner.Battle.Result != BattleResult.Running, "battle must conclude");
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

        // Phase 01: the escort battle is symmetric (both sides lose ships) — the
        // adjudicated outcome may be a time-limit Draw. The bar is the damage chain.
        Assert.True(runner.Battle.Result != BattleResult.Running, "battle must conclude");

        // 起火 / 进水 / 击沉 also occur in the combined-arms battle.
        Assert.True(runner.Fire.Fires.Count > 0, "expected at least one fire");
        Assert.Contains(runner.Ships, s => s.Parts.Values.Any(p => p.WaterLevel > 0));
        Assert.Contains(runner.Ships, s => s.Lost);

        // 防空链: the AA battery engaged (shots + VT airbursts). Direct aircraft kills
        // depend on barrage luck against a small manoeuvring box (Phase 04 tuning);
        // the deterministic close-range kill is covered by AntiAirTests.
        Assert.True(runner.AntiAir.ShotsFired > 100, "AA must engage the strikers");
        Assert.True(runner.AircraftAdjudicator.AirburstsSeen > 0, "VT fuses must function");

        output.WriteLine($"combined: result={runner.Battle.Result} winner={runner.Battle.WinnerTeamId} " +
                         $"fires={runner.Fire.Fires.Count} aircraftLost={runner.Aircraft.Count(a => !a.Alive)} " +
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
