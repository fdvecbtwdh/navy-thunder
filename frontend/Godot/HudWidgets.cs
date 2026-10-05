using Godot;
using NavyThunder.Core.Armor;
using NavyThunder.Core.Ballistics;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Ships;

namespace NavyThunder.Frontend;

/// <summary>
/// P05-3 graphical HUD widgets (PHASE_05): helm gauge, reload ring, target card and the
/// P05-6 hit-feedback indicator. All are state-driven readers of Core state pushed in
/// from BattleScene3D each frame — no simulation logic lives here (PROJECT_DESIGN §3.2).
/// </summary>

/// <summary>Speed / throttle / rudder / heading instrument (bottom-left).</summary>
public sealed partial class HelmGauge : Control
{
    private Ship? _player;
    private const float Width = 250, Height = 118;

    public HelmGauge() => CustomMinimumSize = new Vector2(Width, Height);

    public void UpdateFrom(Ship? player)
    {
        _player = player;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        var white = new Color(1, 1, 1, 0.9f);
        var dim = new Color(1, 1, 1, 0.55f);

        if (_player is null || !_player.Alive)
        {
            DrawString(font, new Vector2(8, 24), _player is null ? "" : L10n.Tr("hud.destroyed"),
                HorizontalAlignment.Left, -1, 16, white);
            return;
        }

        // Speed readout (knots, large) + heading.
        DrawString(font, new Vector2(8, 34), $"{_player.SpeedKnots:0.0}", HorizontalAlignment.Left, -1, 26, white);
        DrawString(font, new Vector2(86, 34), "kn", HorizontalAlignment.Left, -1, 13, dim);
        DrawString(font, new Vector2(130, 34), $"HDG {(_player.HeadingDeg % 360 + 360) % 360:000}°",
            HorizontalAlignment.Left, -1, 15, dim);

        // Throttle bar (0..1) with label.
        DrawString(font, new Vector2(8, 56), "THR", HorizontalAlignment.Left, -1, 11, dim);
        var thrRect = new Rect2(42, 47, 150, 10);
        DrawRect(thrRect, new Color(0, 0, 0, 0.45f));
        DrawRect(new Rect2(thrRect.Position, new Vector2((float)_player.ThrottleCommand * thrRect.Size.X, thrRect.Size.Y)),
            new Color(0.25f, 0.75f, 0.95f));
        DrawRect(thrRect, new Color(1, 1, 1, 0.25f), false, 1f);

        // Rudder bar (-1..1) with centre notch and position marker.
        DrawString(font, new Vector2(8, 78), "RUD", HorizontalAlignment.Left, -1, 11, dim);
        var rudRect = new Rect2(42, 69, 150, 10);
        DrawRect(rudRect, new Color(0, 0, 0, 0.45f));
        float midX = rudRect.Position.X + rudRect.Size.X / 2f;
        DrawLine(new Vector2(midX, rudRect.Position.Y - 2), new Vector2(midX, rudRect.Position.Y + rudRect.Size.Y + 2),
            new Color(1, 1, 1, 0.4f), 1f);
        float markerX = midX + (float)_player.RudderCommand * (rudRect.Size.X / 2f - 2f);
        DrawRect(new Rect2(markerX - 3, rudRect.Position.Y - 2, 6, rudRect.Size.Y + 4), new Color(1f, 0.65f, 0.15f));
        DrawRect(rudRect, new Color(1, 1, 1, 0.25f), false, 1f);

        // Crew (combat effectivity proxy).
        double crewFrac = _player.CrewAlive / (double)System.Math.Max(1, _player.Definition.CrewTotal);
        DrawString(font, new Vector2(8, 100), L10n.Tr("hud.crew"), HorizontalAlignment.Left, -1, 11, dim);
        var crewRect = new Rect2(42, 91, 150, 8);
        DrawRect(crewRect, new Color(0, 0, 0, 0.45f));
        DrawRect(new Rect2(crewRect.Position, new Vector2((float)crewFrac * crewRect.Size.X, crewRect.Size.Y)),
            crewFrac > 0.5 ? new Color(0.4f, 0.8f, 0.4f) : new Color(0.9f, 0.4f, 0.2f));
    }
}

/// <summary>Circular reload indicator + shell type + aim mode (bottom-left, above gauge).</summary>
public sealed partial class ReloadRing : Control
{
    private double _reloadFraction = 1; // 1 = ready
    private bool _he;
    private bool _manualAiming;
    private const float Radius = 26;

    public ReloadRing() => CustomMinimumSize = new Vector2(Radius * 2 + 8, Radius * 2 + 8);

    public void UpdateFrom(double reloadFraction, bool he, bool manualAiming)
    {
        (_reloadFraction, _he, _manualAiming) = (reloadFraction, he, manualAiming);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var centre = new Vector2(Radius + 4, Radius + 4);
        DrawArc(centre, Radius, 0, Mathf.Tau, 40, new Color(0, 0, 0, 0.5f), 5f);
        // Reload progress sweeps clockwise from the top.
        float frac = (float)Mathf.Clamp(_reloadFraction, 0, 1);
        if (frac > 0.001f)
        {
            Color c = frac >= 1f ? new Color(0.35f, 0.9f, 0.4f) : new Color(0.95f, 0.7f, 0.15f);
            DrawArc(centre, Radius, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * frac, 40, c, 5f);
        }

        DrawString(ThemeDB.FallbackFont, centre + new Vector2(-13, 5), _he ? "HE" : "AP",
            HorizontalAlignment.Left, -1, 13, new Color(1, 1, 1, 0.95f));
        DrawString(ThemeDB.FallbackFont, new Vector2(2, Radius * 2 + 18),
            _manualAiming ? L10n.Tr("hud.aim.manual") : L10n.Tr("hud.aim.auto"),
            HorizontalAlignment.Left, -1, 10, new Color(1, 1, 1, 0.6f));
    }
}

/// <summary>Target information card (top-right): id/class, range, speed, heading.</summary>
public sealed partial class TargetCard : PanelContainer
{
    private readonly Label _text = new();

    public TargetCard()
    {
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.09f, 0.12f, 0.78f),
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            ContentMarginLeft = 10, ContentMarginRight = 10,
            ContentMarginTop = 6, ContentMarginBottom = 6,
        });
        _text.AddThemeFontSizeOverride("font_size", 13);
        AddChild(_text);
        Visible = false;
    }

    public void UpdateFrom(Ship? target, Vec3 playerPosition)
    {
        if (target is null)
        {
            Visible = false;
            return;
        }

        double range = Vec3.Distance(playerPosition, target.WorldPosition);
        _text.Text = $"{target.TargetId}\n" +
                     $"{L10n.Tr("hud.target.range")} {range / 1000:0.00} km\n" +
                     $"{L10n.Tr("hud.target.speed")} {target.SpeedKnots:0.0} kn\n" +
                     $"{L10n.Tr("hud.target.heading")} {(target.HeadingDeg % 360 + 360) % 360:000}°";
        Visible = true;
    }
}

/// <summary>P05-6 hit-feedback classifier: pure mapping from Core impact events to the
/// five icon states (击穿/跳弹/过穿/未穿/水柱). Pure so the smoke check can verify it.</summary>
public static class HitFeedback
{
    public enum Kind
    {
        Penetration, // 击穿
        Ricochet,    // 跳弹
        OverPen,     // 过穿 (penetrated but the fuze never triggered)
        NoPen,       // 未穿
        Splash,      // 水柱
    }

    public static Kind Classify(ProjectileArmorImpact impact) => impact.Outcome switch
    {
        PlateResolution.Ricocheted => Kind.Ricochet,
        PlateResolution.Stopped => Kind.NoPen,
        _ => impact.FuzeTriggered ? Kind.Penetration : Kind.OverPen,
    };

    public static (char Glyph, Color Color) IconOf(Kind kind) => kind switch
    {
        Kind.Penetration => ('◆', new Color(0.3f, 1f, 0.4f)),
        Kind.Ricochet => ('↗', new Color(1f, 0.65f, 0.15f)),
        Kind.OverPen => ('◇', new Color(1f, 0.95f, 0.3f)),
        Kind.NoPen => ('✕', new Color(1f, 0.3f, 0.25f)),
        Kind.Splash => ('≈', new Color(0.35f, 0.7f, 1f)),
        _ => ('?', Colors.White),
    };
}

/// <summary>Rolling five-state hit icons beside the crosshair + a hit-cam snapshot line
/// (last shell→plate outcome). Events are pushed from the BattleScene3D event cursor.</summary>
public sealed partial class HitIndicators : Control
{
    private sealed class Entry
    {
        public HitFeedback.Kind Kind;
        public double Age;
        public string? Detail; // hit cam: shell → plate snapshot
    }

    private readonly List<Entry> _entries = [];
    public const float LifetimeS = 2.2f;

    public void PushImpact(ProjectileArmorImpact impact)
    {
        _entries.Add(new Entry
        {
            Kind = HitFeedback.Classify(impact),
            Detail = $"{impact.ShellId} → {impact.PlateId} {impact.PlateThicknessMm:0}mm",
        });
        if (_entries.Count > 8)
        {
            _entries.RemoveAt(0);
        }
    }

    public void PushSplash() => _entries.Add(new Entry { Kind = HitFeedback.Kind.Splash });

    public override void _Process(double delta)
    {
        foreach (var e in _entries)
        {
            e.Age += delta;
        }

        _entries.RemoveAll(e => e.Age > LifetimeS);
        if (_entries.Count > 0)
        {
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        var size = GetViewportRect().Size;
        // Column just right of screen centre (crosshair area).
        float x = size.X / 2f + 46;
        float y = size.Y / 2f - 30;
        for (int i = _entries.Count - 1; i >= 0 && i >= _entries.Count - 6; i--)
        {
            var e = _entries[i];
            var (glyph, color) = HitFeedback.IconOf(e.Kind);
            float alpha = 1f - (float)e.Age / LifetimeS;
            DrawString(font, new Vector2(x, y - i * 20), glyph.ToString(),
                HorizontalAlignment.Left, -1, 18, new Color(color.R, color.G, color.B, alpha));
            if (i == _entries.Count - 1 && e.Detail is { } detail)
            {
                // hit-cam snapshot for the freshest hit only.
                DrawString(font, new Vector2(x + 22, y), detail,
                    HorizontalAlignment.Left, -1, 11, new Color(1, 1, 1, 0.75f * alpha));
            }
        }
    }
}
