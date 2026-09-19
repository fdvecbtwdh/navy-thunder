using NavyThunder.Core.Mathematics;
using NavyThunder.Data;
using Xunit;
using Xunit.Abstractions;

namespace NavyThunder.Core.Tests;

public class DbgTests(ITestOutputHelper output)
{
    [Fact]
    public void Dbg()
    {
        var repo = DataRepository.LoadFromDirectory(RepoLocator.FindDataDirectory());
        var scenario = BattleScenario.Load(Path.Combine(RepoLocator.FindRepoRoot()!, "scenarios", "air_strike.json"));
        var runner = new BattleRunner(repo, scenario);
        var tb = runner.Aircraft[0];
        var st = runner.FlightModel.StateOf(tb)!;
        double nextPrint = 0;
        while (runner.Battle.Result == NavyThunder.Core.Battle.BattleResult.Running && runner.World.Time < 120)
        {
            runner.World.Step();
            if (runner.World.Time >= nextPrint)
            {
                var mission = st.Mission;
                output.WriteLine($"t={runner.World.Time:0.0} pos=({st.Position.X:0},{st.Position.Z:0}) spd={st.Velocity.Length:0.0} " +
                                 $"rel={(mission?.Released ?? false)} range={Vec3.Distance(st.Position, mission?.TargetPosition() ?? Vec3.Zero):0}");
                nextPrint += 5;
            }
        }
        foreach (var e in runner.World.Events.All)
            output.WriteLine($"evt {e.Kind} @{e.Time:0.#}s");
    }
}
