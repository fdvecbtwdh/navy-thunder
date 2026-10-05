using Godot;
using NavyThunder.Core.Fire;
using NavyThunder.Core.Model;
using NavyThunder.Core.Ships;

namespace NavyThunder.Frontend;

/// <summary>
/// P05-4 graphical damage panel (top-left): hull sections as a scaled cross-section row
/// with fire/flood markers, module status grouped by kind (x-ray style list), replacing
/// the Phase-R2 text blood bars. Pure state reader — never writes Core state.
/// </summary>
public sealed partial class DamagePanel : PanelContainer
{
    private readonly VBoxContainer _root = new();
    private readonly HBoxContainer _sectionRow = new();
    private readonly Label _moduleHeader = new();
    private readonly VBoxContainer _moduleList = new();
    private Ship? _player;
    private FireSystem? _fire;

    public DamagePanel()
    {
        AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.09f, 0.12f, 0.72f),
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            ContentMarginLeft = 10, ContentMarginRight = 10,
            ContentMarginTop = 6, ContentMarginBottom = 8,
        });
        _root.AddThemeConstantOverride("separation", 4);
        AddChild(_root);

        _sectionRow.AddThemeConstantOverride("separation", 6);
        _root.AddChild(_sectionRow);
        _moduleHeader.AddThemeFontSizeOverride("font_size", 11);
        _moduleHeader.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.55f));
        _root.AddChild(_moduleHeader);
        _moduleList.AddThemeConstantOverride("separation", 0);
        _root.AddChild(_moduleList);
    }

    public void Bind(FireSystem fire) => _fire = fire;

    public void UpdateFrom(Ship? player)
    {
        if (player != _player)
        {
            _player = player;
            RebuildSections();
        }

        if (_player is null)
        {
            Visible = false;
            return;
        }

        Visible = true;
        UpdateSections();
        UpdateModules();
    }

    private void RebuildSections()
    {
        foreach (var child in _sectionRow.GetChildren())
        {
            _sectionRow.RemoveChild(child);
            child.QueueFree();
        }

        if (_player is null)
        {
            return;
        }

        foreach (var _ in _player.Sections)
        {
            _sectionRow.AddChild(new SectionBlock());
        }
    }

    private void UpdateSections()
    {
        var blocks = _sectionRow.GetChildren().OfType<SectionBlock>().ToList();
        for (int i = 0; i < blocks.Count && i < _player!.Sections.Count; i++)
        {
            var section = _player.Sections[i];
            bool fire = _fire is not null && _fire.Fires.Any(f => f.Active && _player.Parts.Values.Any(p =>
                p.Definition.SectionId == section.Definition.Id && f.HostId == $"{_player.TargetId}/{p.Definition.Id}"));
            bool flood = _player.Parts.Values.Any(p =>
                p.Definition.SectionId == section.Definition.Id && p.WaterLevel > 0.05);
            blocks[i].UpdateFrom(section, fire, flood);
        }
    }

    private void UpdateModules()
    {
        // Group parts by kind: "Engine 2/3" — red when any destroyed, yellow when damaged.
        var groups = _player!.Parts.Values
            .GroupBy(p => p.Definition.Kind)
            .OrderBy(g => Array.IndexOf(OrderedKinds, g.Key) is int i && i >= 0 ? i : 99);

        int line = 0;
        foreach (var g in groups)
        {
            int total = g.Count();
            int alive = g.Count(p => !p.Destroyed);
            double hpFrac = g.Average(p => System.Math.Clamp(p.Hp / System.Math.Max(1, p.Definition.Hp), 0, 1));
            bool destroyed = alive < total;
            bool damaged = alive == total && hpFrac < 0.995;
            var color = destroyed ? new Color(1f, 0.35f, 0.3f)
                : damaged ? new Color(1f, 0.8f, 0.3f)
                : new Color(0.55f, 1f, 0.6f);

            if (_moduleList.GetChildCount() <= line)
            {
                var lbl = new Label();
                lbl.AddThemeFontSizeOverride("font_size", 11);
                _moduleList.AddChild(lbl);
            }

            if (_moduleList.GetChild(line) is Label label)
            {
                string name = L10n.ModuleName(g.Key);
                label.Text = total > 1 ? $"{name} {alive}/{total}" : name;
                label.AddThemeColorOverride("font_color", color);
            }

            line++;
        }

        while (_moduleList.GetChildCount() > line)
        {
            var last = _moduleList.GetChild(_moduleList.GetChildCount() - 1);
            _moduleList.RemoveChild(last);
            last.QueueFree();
        }
    }

    private static readonly PartKind[] OrderedKinds =
    [
        PartKind.Magazine, PartKind.Turret, PartKind.Hoist, PartKind.ReadyRack,
        PartKind.Engine, PartKind.Boiler, PartKind.Turbine, PartKind.Funnel,
        PartKind.Steering, PartKind.FireControl, PartKind.Radar, PartKind.Pump,
        PartKind.TorpedoTube, PartKind.AntiTorpedo, PartKind.FuelTank, PartKind.Compartment,
    ];

    /// <summary>One hull-section block: HP colour fill, fire/flood/destroyed markers.</summary>
    private sealed partial class SectionBlock : Control
    {
        private double _hpFrac = 1;
        private bool _fire, _flood, _destroyed;
        private string _name = "";

        private const float Width = 52, Height = 46;

        public SectionBlock() => CustomMinimumSize = new Vector2(Width, Height);

        public void UpdateFrom(HullSectionState section, bool fire, bool flood)
        {
            _hpFrac = System.Math.Clamp(section.Hp / System.Math.Max(1, section.Definition.Hp), 0, 1);
            _fire = fire;
            _flood = flood;
            _destroyed = section.Destroyed;
            _name = L10n.SectionName(section.Definition.Role); // ids are too long for the 52 px block
            QueueRedraw();
        }

        public override void _Draw()
        {
            var font = ThemeDB.FallbackFont;
            var body = new Rect2(new Vector2(2, 14), new Vector2(Width - 4, Height - 20));
            DrawRect(body, new Color(0, 0, 0, 0.5f));

            // HP fill from the bottom, green→yellow→red with damage.
            var fill = _destroyed ? new Color(0.25f, 0.25f, 0.25f)
                : _hpFrac > 0.6 ? new Color(0.35f, 0.85f, 0.4f)
                : _hpFrac > 0.3 ? new Color(0.95f, 0.8f, 0.25f)
                : new Color(0.95f, 0.4f, 0.3f);
            float fillH = body.Size.Y * (float)_hpFrac;
            DrawRect(new Rect2(body.Position.X, body.End.Y - fillH, body.Size.X, fillH), fill);
            DrawRect(body, new Color(1, 1, 1, 0.3f), false, 1f);

            // Markers.
            float iconY = body.Position.Y + 3;
            if (_fire)
            {
                DrawCircle(new Vector2(body.Position.X + 9, iconY + 4), 4.5f, new Color(1f, 0.5f, 0.1f));
            }

            if (_flood)
            {
                DrawCircle(new Vector2(body.End.X - 9, iconY + 4), 4.5f, new Color(0.3f, 0.6f, 1f));
            }

            if (_destroyed)
            {
                DrawLine(body.Position, body.End, new Color(1, 0.2f, 0.15f, 0.9f), 2f);
                DrawLine(new Vector2(body.Position.X, body.End.Y), new Vector2(body.End.X, body.Position.Y),
                    new Color(1, 0.2f, 0.15f, 0.9f), 2f);
            }

            DrawString(font, new Vector2(2, 10), _name, HorizontalAlignment.Left, (int)Width, 10,
                new Color(1, 1, 1, 0.6f));
        }
    }
}
