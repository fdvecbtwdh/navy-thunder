using Godot;
using NavyThunder.Data;
using NavyThunder.Core.Mathematics;

namespace NavyThunder.Frontend;

/// <summary>
/// R2.0/R2.1 vertical-slice view: drives a Core battle from the Godot frame loop and
/// renders the battlefield - sea with wave bands, horizon, islands, hulls oriented by
/// heading with wakes. Environment knobs:
///   NT_FRONTEND_SMOKE=1  headless smoke harness (prints progress, quits at battle end)
///   NT_FRONTEND_SHOT=x.png  save a viewport capture once the battle reaches shot time
/// </summary>
public partial class BattleView : Node2D
{
    private BattleRunner? _runner;
    private double _simSpeed = 4.0;         // simulated seconds per real second
    private double _smokeAccumulator;
    private double _nextReportAt = 30;
    private bool _shotTaken;
    private string _shotPath = "";

    private static readonly Color SeaColor = new(0.07f, 0.23f, 0.34f);
    private static readonly Color SeaBandColor = new(0.10f, 0.28f, 0.40f);
    private static readonly Color SkyColor = new(0.55f, 0.70f, 0.82f);
    private static readonly Color IslandColor = new(0.30f, 0.36f, 0.22f);
    private const float PixelsPerMeter = 0.095f;

    // Decorative islands (world coordinates, metres).
    private static readonly (float X, float Z, float Radius)[] Islands =
    {
        (-9000f, -12000f, 900f),
        (11000f, 8000f, 1200f),
        (6000f, -15000f, 700f),
    };

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
        _shotPath = OS.GetEnvironment("NT_FRONTEND_SHOT");
        GD.Print($"R2.1: battle wired, ships={_runner.Ships.Count}, result={_runner.Battle.Result}");
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
                         $"alive={_runner.Ships.Count(s => s.Alive)} result={_runner.Battle.Result}");
                _nextReportAt += 30;
            }
            if (_runner.Battle.Result != NavyThunder.Core.Battle.BattleResult.Running)
            {
                GD.Print($"R2.0 smoke done: result={_runner.Battle.Result} " +
                         $"winner={_runner.Battle.WinnerTeamId ?? "none"} at t={_runner.World.Time:0}s");
                GetTree().Quit();
            }
            return;
        }

        if (!_shotTaken && _shotPath.Length > 0 && _runner.World.Time >= 60)
        {
            var img = GetViewport().GetTexture().GetImage();
            img.SavePng(_shotPath);
            _shotTaken = true;
            GD.Print($"R2.1 shot saved: {_shotPath} at t={_runner.World.Time:0}s");
        }

        QueueRedraw();
    }

    private Vector2 ToScreen(Vec3 world) =>
        GetViewportRect().Size / 2f
        + new Vector2((float)world.X * PixelsPerMeter, (float)world.Z * PixelsPerMeter);

    public override void _Draw()
    {
        var size = GetViewportRect().Size;

        // Sky band above the horizon; sea fills the rest.
        DrawRect(new Rect2(0, 0, size.X, size.Y * 0.18f), SkyColor);
        DrawRect(new Rect2(0, size.Y * 0.18f, size.X, size.Y * 0.82f), SeaColor);

        // Wave bands: fixed horizontal stripes scrolling slowly with sim time.
        double t = _runner?.World.Time ?? 0;
        for (int i = 0; i < 22; i++)
        {
            float y = size.Y * 0.18f + (i + 0.5f) * (size.Y * 0.82f / 22f)
                      + (float)Mathf.Sin((float)t * 0.7f + i * 1.7f) * 4f;
            DrawLine(new Vector2(0, y), new Vector2(size.X, y), SeaBandColor, 1.5f);
        }

        // Islands.
        foreach (var (x, z, r) in Islands)
        {
            var c = ToScreen(new Vec3(x, 0, z));
            DrawCircle(c, r * PixelsPerMeter, IslandColor);
            DrawArc(c, r * PixelsPerMeter + 4f, 0, Mathf.Tau, 48,
                new Color(0.75f, 0.78f, 0.65f, 0.5f), 3f);
        }

        if (_runner is null)
        {
            return;
        }

        // Ships: oriented hull rectangle + wake.
        foreach (var ship in _runner.Ships)
        {
            if (!ship.Alive)
            {
                continue;
            }

            var pos = ToScreen(ship.WorldPosition);
            double rad = ship.HeadingDeg * System.Math.PI / 180.0;
            var forward = new Vector2((float)System.Math.Sin(rad), (float)System.Math.Cos(rad));
            var side = new Vector2(-forward.Y, forward.X);

            float halfL = (float)ship.Definition.LengthM * PixelsPerMeter / 2f;
            float halfB = (float)ship.Definition.BeamM * PixelsPerMeter / 2f + 1.5f;
            var p1 = pos + forward * halfL + side * halfB;
            var p2 = pos + forward * halfL - side * halfB;
            var p3 = pos - forward * halfL - side * halfB;
            var p4 = pos - forward * halfL + side * halfB;

            Color teamColor = ship.Team?.Id == "usn" ? Colors.DodgerBlue : Colors.IndianRed;
            DrawColoredPolygon(new[] { p1, p2, p3, p4 }, teamColor);
            DrawCircle(pos, halfB, teamColor.Darkened(0.25f)); // superstructure
            DrawLine(pos - forward * halfL * 2.2f, pos - forward * halfL,
                new Color(1, 1, 1, 0.35f), 2f);                   // wake

            DrawString(ThemeDB.FallbackFont, pos + new Vector2(12, -12),
                ship.TargetId, HorizontalAlignment.Left, -1, 12, Colors.LightGray);
        }
    }
}
