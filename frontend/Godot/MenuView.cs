using Godot;
using NavyThunder.Data;

namespace NavyThunder.Frontend;

/// <summary>R3.4 cross-scene state: the ship the player selected in the menu.</summary>
public static class SessionState
{
    public static string? SelectedShipId { get; set; }
}

/// <summary>R3.4 minimal main menu: ship selection (from the fleet data) + start/quit.</summary>
public partial class MenuView : CanvasLayer
{
    private UserSettings _settings = new();
    private OptionButton? _shipPicker;
    private readonly List<string> _shipIds = [];

    public override void _Ready()
    {
        AppEnv.Install();
        AppEnv.Info("menu booted");
        _settings = UserSettings.Load();
        SessionState.SelectedShipId = _settings.LastShipId;

        var root = new VBoxContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f,
            AnchorTop = 0.5f, AnchorBottom = 0.5f,
        };
        root.AddThemeConstantOverride("separation", 14);
        AddChild(root);

        var title = new Label { Text = "NAVY THUNDER" };
        title.AddThemeFontSizeOverride("font_size", 42);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(title);

        var subtitle = new Label { Text = "pick your ship" };
        subtitle.AddThemeColorOverride("font_color", Colors.LightGray);
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(subtitle);

        _shipPicker = new OptionButton { CustomMinimumSize = new Vector2(340, 0) };
        foreach (var (id, name) in LoadFleetShips())
        {
            _shipPicker.AddItem(name);
            _shipIds.Add(id);
        }

        int selected = _shipIds.IndexOf(_settings.LastShipId ?? "");
        _shipPicker.Selected = Math.Max(0, selected);
        root.AddChild(_shipPicker);

        var spacer = new Control { CustomMinimumSize = new Vector2(0, 16) };
        root.AddChild(spacer);

        var start = new Button { Text = "START BATTLE" };
        start.Pressed += () =>
        {
            int i = Math.Max(0, _shipPicker.Selected);
            if (i < _shipIds.Count)
            {
                SessionState.SelectedShipId = _shipIds[i];
                _settings.LastShipId = _shipIds[i];
                _settings.Save();
            }

            GetTree().ChangeSceneToFile("res://Main.tscn");
        };
        root.AddChild(start);

        var quit = new Button { Text = "QUIT" };
        quit.Pressed += () => GetTree().Quit();
        root.AddChild(quit);

        root.Ready += () => root.Position = -root.Size / 2f;
    }

    private List<(string Id, string Name)> LoadFleetShips()
    {
        var result = new List<(string, string)>();
        try
        {
            string repoRoot = ProjectSettings.GlobalizePath("res://");
            while (repoRoot is not null && !File.Exists(System.IO.Path.Combine(repoRoot, "NavyThunder.slnx")))
            {
                repoRoot = System.IO.Directory.GetParent(repoRoot)?.FullName;
            }

            var fleet = System.IO.Path.Combine(repoRoot!, "data", "ships", "generated_fleet.json");
            var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(fleet));
            foreach (var ship in doc.RootElement.GetProperty("ships").EnumerateArray())
            {
                var id = ship.GetProperty("id").GetString()!;
                var cls = ship.GetProperty("class").GetString() ?? "";
                result.Add((id, id.Replace("_", " ").ToUpperInvariant() + "  [" + cls + "]"));
            }
        }
        catch (Exception ex)
        {
            AppEnv.Error("fleet list failed: " + ex.Message);
            result.Add(("test_battleship", "TEST BATTLESHIP"));
        }

        return result;
    }
}
