using NavyThunder.Core.Battle;
using NavyThunder.Core.Ships;
using NavyThunder.Data;
using Xunit;
using Xunit.Abstractions;

namespace NavyThunder.Core.Tests;

/// <summary>
/// Phase 02 regression: HandControlToPlayer must actually stop the naval AI from
/// steering the player's ship. Found during 3D visual verification — the AI update loop
/// iterates its minds, so removing the ship from the AI's ship list alone left the AI
/// overwriting the player's helm every tick.
/// </summary>
    [Trait("Bucket", "Integration")]
public class PlayerHandoverTests(ITestOutputHelper output)
{
    [Fact]
    public void Handed_Over_Ship_Is_Not_Steered_By_The_Ai()
    {
        var repo = DataRepository.LoadFromDirectory(RepoLocator.FindDataDirectory());
        var scenario = BattleScenario.Load(Path.Combine(RepoLocator.FindRepoRoot()!, "scenarios", "bb_duel.json"));
        var runner = new BattleRunner(repo, scenario);

        var player = runner.Ships[0]; // first friendly spawn (throttle 1.0 from scenario)
        runner.HandControlToPlayer(player.TargetId);
        double headingAtHandover = player.HeadingDeg;
        double rudderAtHandover = player.RudderCommand;

        // Run 120 s of battle with no player input: the AI may fight the second ship,
        // but the handed-over ship must keep her helm (straight line ahead).
        while (runner.World.Time < 120 && runner.Battle.Result == BattleResult.Running)
        {
            runner.World.Step();
        }

        double delta = System.Math.Abs(player.HeadingDeg - headingAtHandover);
        // wrap-around safe compare
        if (delta > 180)
        {
            delta = 360 - delta;
        }

        output.WriteLine($"player heading {headingAtHandover:0.#} -> {player.HeadingDeg:0.#} (delta {delta:0.##}°), " +
                         $"rudder {rudderAtHandover} -> {player.RudderCommand:+0.00;-0.00;0.00}");
        Assert.True(delta < 1.0, $"handed-over ship must not be steered by AI (heading drifted {delta:0.##}°)");
        Assert.DoesNotContain(player, runner.NavalAi.Ships);
    }
}
