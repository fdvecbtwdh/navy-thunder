using Godot;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Ships;
using NavyThunder.Data;

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

        // R2.2: the first USN ship answers to the helm; the chase camera follows it.
        _runner.HandControlToPlayer(_runner.Ships[0].TargetId);
        _playerShip = _runner.PlayerShip;
        var cam = new Camera2D { Enabled = true };
        AddChild(cam);
        cam.MakeCurrent();
        _camera = cam;

        var hud = new CanvasLayer();
        AddChild(hud);
        _hud = new Label
        {
            Position = new Vector2(16, 10),
            Text = "",
        };
        _hud.AddThemeFontSizeOverride("font_size", 16);
        hud.AddChild(_hud);

        GD.Print($"R2.2: battle wired, ships={_runner.Ships.Count}, " +
                 $"player={_playerShip?.TargetId ?? "none"}, result={_runner.Battle.Result}");
    }

    private Ship? _playerShip;
    private Camera2D? _camera;
    private Label? _hud;

    private Ship? _aimTarget;

    /// <summary>Mouse position in battle metres (canvas centre = battle origin).</summary>
    private Vec3 MouseWorld() =>
        new((GetGlobalMousePosition().X - GetViewportRect().Size.X / 2f) / PixelsPerMeter, 0,
            (GetGlobalMousePosition().Y - GetViewportRect().Size.Y / 2f) / PixelsPerMeter);

    /// <summary>R2.3 fire control: engage the enemy under the cursor, cease otherwise.</summary>
    private void ApplyFireControl()
    {
        if (_playerShip is null || !_playerShip.Alive || _runner is null)
        {
            return;
        }

        var mouse = MouseWorld();
        Ship? hover = null;
        double best = double.MaxValue;
        foreach (var ship in _runner.Ships)
        {
            if (!ship.Alive || ship.Team?.Id == _playerShip.Team?.Id)
            {
                continue;
            }

            double d = Vec3.Distance(ship.WorldPosition, mouse);
            double capture = System.Math.Max(120, ship.Definition.LengthM);
            if (d < capture && d < best)
            {
                best = d;
                hover = ship;
            }
        }

        _aimTarget = hover;
        foreach (var gun in _playerShip.Definition.Guns)
        {
            if (hover is null)
            {
                _runner.Guns.CeaseFire(_playerShip.TargetId, gun.Id);
                continue;
            }

            var victim = hover;
            _runner.Guns.Engage(_playerShip.TargetId, gun.Id, new GunOrder
            {
                TargetId = victim.TargetId,
                TargetPosition = () => victim.WorldPosition,
                TargetVelocity = () => new Vec3(
                    System.Math.Sin(victim.HeadingDeg * System.Math.PI / 180.0) * victim.SpeedKnots * 0.514444,
                    0,
                    System.Math.Cos(victim.HeadingDeg * System.Math.PI / 180.0) * victim.SpeedKnots * 0.514444),
                TargetLengthM = () => victim.Definition.LengthM,
            });
        }
    }

    /// <summary>Per-frame helm input: W/S throttle, A/D rudder, X centers rudder.</summary>
    private void ApplyHelm(double delta)
    {
        if (_playerShip is null || !_playerShip.Alive)
        {
            return;
        }

        const double throttleRate = 0.25;
        const double rudderRate = 0.8;
        if (Input.IsKeyPressed(Key.W))
        {
            _playerShip.ThrottleCommand = System.Math.Min(1.0, _playerShip.ThrottleCommand + throttleRate * delta);
        }
        if (Input.IsKeyPressed(Key.S))
        {
            _playerShip.ThrottleCommand = System.Math.Max(0.0, _playerShip.ThrottleCommand - throttleRate * delta);
        }
        if (Input.IsKeyPressed(Key.A))
        {
            _playerShip.RudderCommand = System.Math.Max(-1.0, _playerShip.RudderCommand - rudderRate * delta);
        }
        if (Input.IsKeyPressed(Key.D))
        {
            _playerShip.RudderCommand = System.Math.Min(1.0, _playerShip.RudderCommand + rudderRate * delta);
        }
        if (Input.IsKeyPressed(Key.X))
        {
            _playerShip.RudderCommand = 0;
        }
    }

    public override void _Process(double delta)
    {
        if (_runner is null)
        {
            return;
        }

        bool smoke = OS.GetEnvironment("NT_FRONTEND_SMOKE") == "1";
        // NT_FRONTEND_QUICK=1 fast-forwards (~200x) to reach the report quickly.
        double budget = OS.GetEnvironment("NT_FRONTEND_QUICK") == "1"
            ? delta * 200.0
            : smoke ? 0.25 : delta * _simSpeed;
        _smokeAccumulator += budget;
        while (_smokeAccumulator >= _runner.World.FixedDeltaTime
               && _runner.Battle.Result == NavyThunder.Core.Battle.BattleResult.Running)
        {
            _runner.World.Step();
            _smokeAccumulator -= _runner.World.FixedDeltaTime;
        }

        ApplyHelm(delta);
        ApplyFireControl();
        CollectEffects();

        if (_playerShip is not null && _camera is not null)
        {
            _camera.Position = ToScreen(_playerShip.WorldPosition);
        }
        if (_hud is not null && _playerShip is not null)
        {
            _hud.Text = BuildHudText();
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
        }

        // R2.6: the battle report replaces the fight once the adjudicator calls it.
        if (_runner.Battle.Result != NavyThunder.Core.Battle.BattleResult.Running)
        {
            ShowReport();
        }
        else
        {
            QueueRedraw();
        }
    }

    private bool _reportShown;

    /// <summary>R2.6 battle report overlay: outcome, losses, and the way back out.</summary>
    private void ShowReport()
    {
        if (_reportShown)
        {
            return;
        }

        _reportShown = true;
        var report = new CanvasLayer { Layer = 20 };
        AddChild(report);

        var box = new VBoxContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f,
            AnchorTop = 0.5f, AnchorBottom = 0.5f,
        };
        box.AddThemeConstantOverride("separation", 14);
        report.AddChild(box);

        var winner = _runner.Battle.WinnerTeamId ?? "none";
        var title = new Label
        {
            Text = _runner.Battle.Result == NavyThunder.Core.Battle.BattleResult.TeamWin
                ? $"VICTORY - {winner.ToUpperInvariant()}"
                : "BATTLE OVER - DRAW",
        };
        title.AddThemeFontSizeOverride("font_size", 36);
        box.AddChild(title);

        int lost = _runner.Ships.Count(s => s.Lost);
        var detail = new Label
        {
            Text = $"duration {_runner.World.Time:0}s  |  ships lost {lost}/{_runner.Ships.Count}  |  " +
                   $"salvos {_runner.Battle.GunsFired}\n" +
                   string.Join("\n", _runner.Ships.Select(s =>
                       $"{s.TargetId}: {(s.Lost ? $"LOST ({s.KillReason})" : $"alive, crew {s.CrewAlive}/{s.Definition.CrewTotal}")}")),
        };
        detail.AddThemeColorOverride("font_color", Colors.LightGray);
        box.AddChild(detail);

        var again = new Button { Text = "BACK TO MENU" };
        again.Pressed += () => GetTree().ChangeSceneToFile("res://Menu.tscn");
        box.AddChild(again);

        box.Ready += () => box.Position = -box.Size / 2f;
        if (_shotPath.Length > 0)
        {
            // Capture after the overlay has actually drawn (defer past this frame).
            var tree = GetTree();
            var path = _shotPath;
            tree.CreateTimer(0.5).Timeout += () =>
            {
                var img = ((Viewport)tree.Root).GetTexture().GetImage();
                img.SavePng(path);
            };
        }
        QueueRedraw();
        GD.Print($"R2.6 report shown: winner={winner} shipsLost={lost}");
    }

    private Vector2 ToScreen(Vec3 world) =>
        GetViewportRect().Size / 2f
        + new Vector2((float)world.X * PixelsPerMeter, (float)world.Z * PixelsPerMeter);

    private readonly List<(Vec3 Pos, double Time, bool Hit)> _effects = [];
    private long _fxCursor;

    /// <summary>Collects recent impacts/detonations for the effect layer.</summary>
    private void CollectEffects()
    {
        if (_runner is null)
        {
            return;
        }

        foreach (var e in _runner.World.Events.After(_fxCursor))
        {
            switch (e)
            {
                case NavyThunder.Core.Ballistics.ShellDetonation d:
                    _effects.Add((d.Position, d.Time, d.TargetId.Length > 0));
                    break;
                case NavyThunder.Core.Ballistics.ProjectileArmorImpact impact:
                    _effects.Add((impact.Position, impact.Time, true));
                    break;
            }
        }

        _fxCursor = _runner.World.Events.TotalRecorded;
    }

    public override void _Draw()
    {
        var size = GetViewportRect().Size;
        // Background must fill the viewport in WORLD space (the camera translates the
        // canvas), so anchor everything on the camera centre with generous margins.
        var camCenter = _camera?.Position ?? size / 2f;
        var x0 = camCenter.X - size.X * 1.5f;
        var x1 = camCenter.X + size.X * 1.5f;
        var y0 = camCenter.Y - size.Y * 1.5f;
        var y1 = camCenter.Y + size.Y * 1.5f;

        DrawRect(new Rect2(x0, y0, x1 - x0, y1 - y0), SeaColor);

        // Wave bands: world-space horizontal stripes with a slow sim-time shimmer.
        double t = _runner?.World.Time ?? 0;
        float step = 44f;
        float phase = (float)(t * 12.0) % step;
        for (float y = y0 - step; y < y1 + step; y += step)
        {
            float yy = y + (float)Mathf.Sin((y + (float)t * 40f) * 0.02f) * 5f + phase;
            DrawLine(new Vector2(x0, yy), new Vector2(x1, yy), SeaBandColor, 1.5f);
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

        // R2.5 hit feedback: fading splash (water) / flash (hit) rings.
        var simT = _runner.World.Time;
        _effects.RemoveAll(e => simT - e.Time > 2.0);
        foreach (var e in _effects)
        {
            float age = (float)(simT - e.Time);
            float alpha = 1f - age / 2f;
            var p = ToScreen(e.Pos);
            float rr = 4f + age * 14f;
            var col = e.Hit
                ? new Color(1f, 0.55f, 0.15f, alpha)
                : new Color(0.85f, 0.95f, 1f, alpha * 0.8f);
            DrawArc(p, rr, 0, Mathf.Tau, 24, col, 2f);
            DrawCircle(p, 3f, new Color(col.R, col.G, col.B, alpha * 0.6f));
        }

        // R2.3 aim indicator: ring on the hovered enemy + reload status.
        if (_aimTarget is { Alive: true } aim)
        {
            var ap = ToScreen(aim.WorldPosition);
            float rr = (float)aim.Definition.LengthM * PixelsPerMeter;
            DrawArc(ap, rr + 8f, 0, Mathf.Tau, 48, Colors.Orange, 2f);
            DrawArc(ToScreen(MouseWorld()), 6f, 0, Mathf.Tau, 24,
                new Color(1f, 0.6f, 0.1f, 0.8f), 1.5f);
        }
    }

    /// <summary>R2.2/R2.3/R2.4 status readout: helm, guns, hull sections, fire/flood.</summary>
    private string BuildHudText()
    {
        var ship = _playerShip!;
        string helm = ship.Alive
            ? $"{ship.TargetId}  SPD {ship.SpeedKnots:0.0} kn  HDG {ship.HeadingDeg:0}  " +
              $"THR {ship.ThrottleCommand:0.00}  RUD {ship.RudderCommand:+0.00;-0.00;0.00}"
            : $"{ship.TargetId} DESTROYED";
        double reload = _runner!.Guns.ReloadRemainingOf(ship.TargetId);
        string guns = ship.Alive
            ? (reload > 0 ? $"GUNS reloading {reload:0.0}s" : "GUNS ready") + "  |  hover an enemy to engage"
            : "";

        // Hull sections: hp bar + fire/flood markers (R2.4 damage HUD).
        var lines = new List<string> { helm, guns };
        foreach (var section in ship.Sections)
        {
            double frac = System.Math.Clamp(section.Hp / section.Definition.Hp, 0.0, 1.0);
            bool fire = _runner!.Fire.Fires.Any(f => f.Active && ship.Parts.Values.Any(pt =>
                pt.Definition.SectionId == section.Definition.Id &&
                f.HostId == $"{ship.TargetId}/{pt.Definition.Id}"));
            bool flood = ship.Parts.Values.Any(pt =>
                pt.Definition.SectionId == section.Definition.Id && pt.WaterLevel > 0.05);
            string marker = (fire ? " [FIRE]" : "") + (flood ? " [FLOOD]" : "");
            lines.Add($"{section.Definition.Id,-10} {frac,4:P0}  " +
                      new string('#', (int)System.Math.Round(frac * 20)).PadRight(20) + marker +
                      (section.Destroyed ? "  DESTROYED" : ""));
        }

        lines.Add($"CREW {ship.CrewAlive}/{ship.Definition.CrewTotal}  |  W/S throttle  A/D rudder  X center  " +
                  $"|  t={_runner!.World.Time:0}s");
        return string.Join("\n", lines);
    }
}
