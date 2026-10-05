using Godot;

namespace NavyThunder.Frontend;

/// <summary>
/// P05-8 ESC pause overlay: 继续/重开/回主菜单/退出. Pausing is a presentation-loop
/// decision (BattleScene3D stops advancing the simulation while visible) — the
/// deterministic Core has no notion of pause. Hidden while the battle report is up.
/// </summary>
public sealed partial class PauseMenu : CanvasLayer
{
    private bool _paused;
    public bool Paused => _paused;

    private readonly ColorRect _dim = new() { Color = new Color(0, 0, 0, 0.55f), Visible = false };

    public override void _Ready()
    {
        Layer = 15;
        AddChild(_dim);

        var box = new VBoxContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
        };
        box.AddThemeConstantOverride("separation", 10);
        _dim.AddChild(box);

        var title = new Label
        {
            Text = L10n.Tr("pause.title"),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 28);
        box.AddChild(title);

        box.AddChild(MakeButton(L10n.Tr("pause.resume"), Close));
        box.AddChild(MakeButton(L10n.Tr("pause.restart"), () => GetTree().ChangeSceneToFile("res://Main.tscn")));
        box.AddChild(MakeButton(L10n.Tr("pause.menu"), () => GetTree().ChangeSceneToFile("res://Menu.tscn")));
        box.AddChild(MakeButton(L10n.Tr("pause.quit"), () => GetTree().Quit()));

        box.Ready += () => box.Position = -box.Size / 2f;
    }

    private static Button MakeButton(string text, System.Action action)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(220, 0) };
        button.Pressed += action;
        return button;
    }

    /// <summary>ESC toggles. Returns true when the press was consumed.</summary>
    public bool Toggle()
    {
        if (_paused)
        {
            Close();
            return true;
        }

        _paused = true;
        _dim.Visible = true;
        return true;
    }

    private void Close()
    {
        _paused = false;
        _dim.Visible = false;
    }
}
