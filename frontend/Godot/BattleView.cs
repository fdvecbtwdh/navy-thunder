using Godot;
using NavyThunder.Data;

namespace NavyThunder.Frontend;

/// <summary>
/// R2.0 vertical-slice seed: drives a headless Core battle from the Godot frame loop
/// and draws live ship positions/heading. NT_FRONTEND_SMOKE=1 turns the view into a
/// headless smoke harness (prints progress, quits at battle end) for CI verification.
/// </summary>
public partial class BattleView : Node2D
{
    private BattleRunner? _runner;
    private double _simSpeed = 4.0;         // simulated seconds per real second
    private double _smokeAccumulator;
    private double _nextReportAt = 30;

    public override void _Ready()
    {
        string repoRoot = ProjectSettings.GlobalizePath("res://");
        while (repoRoot is not null && !File.Exists(Path.Combine(repoRoot, "NavyThunder.slnx")))
        {
            var parent = Directory.GetParent(repoRoot);
            repoRoot = parent?.FullName;
        }
        if (repoRoot is null)
        {
            GD.PushError("repo root not found above the Godot project");
            return;
        }

        var repo = DataRepository.LoadFromDirectory(Path.Combine(repoRoot, "data"));
        var scenario = BattleScenario.Load(Path.Combine(repoRoot, "scenarios", "bb_duel.json"));
        _runner = new BattleRunner(repo, scenario);
        GD.Print($"R2.0: battle wired, ships={_runner.Ships.Count}, " +
                 $"result={_runner.Battle.Result}");
    }

    public override void _Process(double delta)
    {
        if (_runner is null)
        {
            return;
        }

        bool smoke = OS.GetEnvironment("NT_FRONTEND_SMOKE") == "1";
        double budget = smoke ? 0.25 : delta * _simSpeed;
        _smokeAccumulator += budget;
        while (_smokeAccumulator >= _runner.World.FixedDeltaTime
               && _runner.Battle.Result == NavyThunder.Core.Battle.BattleResult.Running)
        {
            _runner.World.Step();
            _smokeAccumulator -= _runner.World.FixedDeltaTime;
        }

        if (smoke)
        {
            if (_runner.World.Time >= _nextReportAt)
            {
                GD.Print($"R2.0 smoke: t={_runner.World.Time:0}s " +
                         $"alive={_runner.Ships.Count(s => s.Alive)} " +
                         $"result={_runner.Battle.Result}");
                _nextReportAt += 30;
            }
            if (_runner.Battle.Result != NavyThunder.Core.Battle.BattleResult.Running)
            {
                GD.Print($"R2.0 smoke done: result={_runner.Battle.Result} " +
                         $"winner={_runner.Battle.WinnerTeamId ?? "none"} at t={_runner.World.Time:0}s");
                GetTree().Quit();
            }
        }
        else
        {
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (_runner is null)
        {
            return;
        }

        const float pixelsPerMeter = 0.055f;
        var origin = GetViewportRect().Size / 2f;
        foreach (var ship in _runner.Ships)
        {
            if (!ship.Alive)
            {
                continue;
            }

            var pos = origin + new Vector2(
                (float)ship.WorldPosition.X * pixelsPerMeter,
                (float)ship.WorldPosition.Z * pixelsPerMeter);
            double rad = ship.HeadingDeg * System.Math.PI / 180.0;
            var heading = new Vector2((float)System.Math.Sin(rad), (float)System.Math.Cos(rad));
            Color teamColor = ship.Team?.Id == "usn"
                ? Colors.DodgerBlue : Colors.IndianRed;
            DrawCircle(pos, 9f, teamColor);
            DrawLine(pos, pos + heading * 26f, Colors.White, 2f);
            DrawString(ThemeDB.FallbackFont, pos + new Vector2(12, -12),
                ship.TargetId, HorizontalAlignment.Left, -1, 12, Colors.LightGray);
        }
    }
}
