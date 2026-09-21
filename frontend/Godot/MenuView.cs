using Godot;

namespace NavyThunder.Frontend;

/// <summary>R2.6 minimal main menu: start a battle or quit.</summary>
public partial class MenuView : CanvasLayer
{
    public override void _Ready()
    {
        var root = new VBoxContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f,
            AnchorTop = 0.5f, AnchorBottom = 0.5f,
        };
        root.AddThemeConstantOverride("separation", 18);
        AddChild(root);

        var title = new Label { Text = "NAVY THUNDER" };
        title.AddThemeFontSizeOverride("font_size", 42);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(title);

        var subtitle = new Label { Text = "naval combat, AI vs AI or you at the helm" };
        subtitle.AddThemeColorOverride("font_color", Colors.LightGray);
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(subtitle);

        var spacer = new Control { CustomMinimumSize = new Vector2(0, 20) };
        root.AddChild(spacer);

        var start = new Button { Text = "START BATTLE" };
        start.Pressed += () => GetTree().ChangeSceneToFile("res://Main.tscn");
        root.AddChild(start);

        var quit = new Button { Text = "QUIT" };
        quit.Pressed += () => GetTree().Quit();
        root.AddChild(quit);

        // Centre the box once its size is known.
        root.Ready += () => root.Position = -root.Size / 2f;
    }
}
