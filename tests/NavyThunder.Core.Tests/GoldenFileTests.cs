using NavyThunder.Core.Battle;
using NavyThunder.Data;
using Xunit;

namespace NavyThunder.Core.Tests;

/// <summary>
/// W4.3 golden file: the bb_duel scenario is fully deterministic, so its end-of-battle
/// summary is a stable fingerprint. Any engine change that alters battle outcomes shows
/// up as a golden diff; regenerate intentionally with NT_UPDATE_GOLDENS=1.
/// </summary>
public class GoldenFileTests
{
    private static string GoldenPath()
        => Path.Combine(RepoLocator.FindRepoRoot()!, "tests", "golden", "bb_duel_summary.json");

    private static string CaptureSummary()
    {
        var repo = DataRepository.LoadFromDirectory(RepoLocator.FindDataDirectory());
        var scenario = BattleScenario.Load(Path.Combine(RepoLocator.FindRepoRoot()!, "scenarios", "bb_duel.json"));
        var runner = new BattleRunner(repo, scenario);
        var report = runner.Run();

        // Normalize: state/reason/duration per ship (positions are float-noisy in text).
        var lines = new List<string>
        {
            $"result={runner.Battle.Result}",
            $"winner={runner.Battle.WinnerTeamId ?? "none"}",
        };
        foreach (var ship in runner.Ships.OrderBy(s => s.TargetId, StringComparer.Ordinal))
        {
            lines.Add($"{ship.TargetId}|{ship.KillState}|{ship.KillReason ?? "-"}|{ship.CrewAlive}");
        }
        return string.Join("\n", lines) + "\n";
    }

    [Fact]
    public void Bb_Duel_Matches_Golden_Summary()
    {
        var golden = GoldenPath();
        var actual = CaptureSummary();

        if (Environment.GetEnvironmentVariable("NT_UPDATE_GOLDENS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(golden)!);
            File.WriteAllText(golden, actual);
        }

        Assert.True(File.Exists(golden), $"golden file missing: {golden} (run with NT_UPDATE_GOLDENS=1 to create)");
        Assert.Equal(File.ReadAllText(golden), actual);
    }
}
