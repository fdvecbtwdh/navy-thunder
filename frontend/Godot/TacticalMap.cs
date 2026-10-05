using Godot;
using NavyThunder.Core.Mathematics;
using NavyThunder.Data;

namespace NavyThunder.Frontend;

/// <summary>
/// Tactical map (PHASE_02 §26): the Phase-R2 2D battle view, degraded to a read-only
/// overlay. Renders ships/effects/islands from the LIVE Core state on a CanvasLayer —
/// it owns no simulation state and no input, and shares the same runner the 3D scene
/// drives. Toggled with M.
/// </summary>
public partial class TacticalMap : CanvasLayer
{
    private sealed partial class Canvas : Node2D
    {
        public BattleRunner? Runner;
        public bool ShowFx;
        public readonly List<(Vec3 Pos, double Time, int Kind)> Effects = [];
        public double SimTime;

        private static readonly Color SeaColor = new(0.07f, 0.23f, 0.34f);
        private static readonly Color IslandColor = new(0.30f, 0.36f, 0.22f);
        private const float PixelsPerMeter = 0.02f;

        private static readonly (float X, float Z, float Radius)[] Islands =
        {
            (-9000f, -12000f, 900f),
            (11000f, 8000f, 1200f),
            (6000f, -15000f, 700f),
        };

        public override void _Draw()
        {
            var size = GetViewportRect().Size;
            var center = size / 2f;
            DrawRect(new Rect2(0, 0, size.X, size.Y), SeaColor);

            foreach (var (x, z, r) in Islands)
            {
                DrawCircle(center + WorldToScreen(x, z), r * PixelsPerMeter, IslandColor);
            }

            if (Runner is null)
            {
                return;
            }

            var labels = new List<(Vector2 At, string Text, float Alpha)>();
            foreach (var ship in Runner.Ships)
            {
                float alpha = 1f;
                if (!ship.Alive && ship.DestroyedTime is double sunkAt)
                {
                    float since = (float)(SimTime - sunkAt);
                    if (since > 30f)
                    {
                        continue;
                    }
                    alpha = 1f - since / 30f;
                }

                var pos = center + WorldToScreen(ship.WorldPosition.X, ship.WorldPosition.Z);
                double rad = ship.HeadingDeg * Math.PI / 180.0;
                var fwd = new Vector2((float)Math.Sin(rad), (float)Math.Cos(rad));
                var side = new Vector2(-fwd.Y, fwd.X);
                float halfL = (float)ship.Definition.LengthM * PixelsPerMeter;
                float halfB = (float)ship.Definition.BeamM * PixelsPerMeter / 2f + 1.2f;

                var team = ship.Team?.Id == "usn" ? Colors.DodgerBlue : Colors.IndianRed;
                team = new Color(team.R, team.G, team.B, alpha);

                Vector2 Local(double lx, double lz) => pos + fwd * (float)(lz * PixelsPerMeter) + side * (float)(lx * PixelsPerMeter);

                var hull = new Vector2[10]
                {
                    Local(0, ship.Definition.LengthM / 2f),
                    Local(ship.Definition.BeamM * 0.38, ship.Definition.LengthM * 0.30),
                    Local(ship.Definition.BeamM / 2f, ship.Definition.LengthM * 0.12),
                    Local(ship.Definition.BeamM / 2f, -ship.Definition.LengthM * 0.28),
                    Local(ship.Definition.BeamM * 0.40, -ship.Definition.LengthM * 0.44),
                    Local(ship.Definition.BeamM * 0.18, -ship.Definition.LengthM / 2f),
                    Local(-ship.Definition.BeamM * 0.18, -ship.Definition.LengthM / 2f),
                    Local(-ship.Definition.BeamM * 0.40, -ship.Definition.LengthM * 0.44),
                    Local(-ship.Definition.BeamM / 2f, -ship.Definition.LengthM * 0.28),
                    Local(-ship.Definition.BeamM / 2f, ship.Definition.LengthM * 0.12),
                };
                DrawColoredPolygon(hull, team);
                if (!ship.Alive)
                {
                    DrawPolyline(hull.Append(hull[0]).ToArray(), new Color(0.8f, 0.2f, 0.1f, alpha), 1.2f);
                }

                labels.Add((pos + new Vector2(6, -6), L10n.ShipMapLabel(ship), alpha));
            }

            // Draw labels after all ships with a simple de-overlap stagger: clustered
            // ships used to overprint their name glyphs into an unreadable stack.
            var usedRows = new List<float>();
            foreach (var (at, text, alpha) in labels)
            {
                var where = at;
                while (usedRows.Any(y => System.Math.Abs(y - where.Y) < 12f))
                {
                    where.Y += 12f;
                }

                usedRows.Add(where.Y);
                DrawString(ThemeDB.FallbackFont, where, text, HorizontalAlignment.Left, -1, 10,
                    new Color(1, 1, 1, 0.65f * alpha));
            }

            // P05-7: player ring + locked-target marker with a bearing line.
            var player = Runner.PlayerShip;
            if (player is { Alive: true })
            {
                var pPos = center + WorldToScreen(player.WorldPosition.X, player.WorldPosition.Z);
                DrawArc(pPos, 14f, 0, Mathf.Tau, 24, new Color(1, 1, 1, 0.85f), 1.6f);

                if (Runner.PlayerLockedTargetId is { } lockId &&
                    Runner.Ships.FirstOrDefault(s => s.TargetId == lockId && s.Alive) is { } locked)
                {
                    var lPos = center + WorldToScreen(locked.WorldPosition.X, locked.WorldPosition.Z);
                    DrawLine(pPos, lPos, new Color(1f, 0.55f, 0.1f, 0.55f), 1.2f);
                    DrawArc(lPos, 16f, 0, Mathf.Tau, 24, new Color(1f, 0.55f, 0.1f), 2f);
                }
            }

            if (ShowFx)
            {
                var simT = SimTime;
                Effects.RemoveAll(e => simT - e.Time > 3.0);
                foreach (var e in Effects)
                {
                    float age = (float)(simT - e.Time);
                    float a = 1f - age / 3f;
                    DrawArc(center + WorldToScreen(e.Pos.X, e.Pos.Z), 3f + age * 8f, 0, Mathf.Tau, 16,
                        new Color(1f, 0.6f, 0.2f, a), 1.5f);
                }
            }
        }

        private static Vector2 WorldToScreen(double x, double z) => new((float)(x * PixelsPerMeter), (float)(z * PixelsPerMeter));
    }

    private readonly Canvas _canvas = new();

    public override void _Ready()
    {
        Layer = 4;
        AddChild(_canvas);
        // Verification/UX hook: start with the map open (M toggles at runtime).
        Visible = OS.GetEnvironment("NT_FRONTEND_MAP") == "1";
    }

    public void Bind(BattleRunner runner)
    {
        _canvas.Runner = runner;
    }

    public void PushEffects(List<(Vec3 Pos, double Time, int Kind)> effects, double simTime, bool showFx)
    {
        _canvas.Effects.Clear();
        _canvas.Effects.AddRange(effects);
        _canvas.SimTime = simTime;
        _canvas.ShowFx = showFx;
        if (Visible)
        {
            _canvas.QueueRedraw();
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true } && KeyBinds.Down(KeyBinds.Map))
        {
            Visible = !Visible;
            if (Visible)
            {
                _canvas.QueueRedraw();
            }
        }
    }
}
