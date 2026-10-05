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
    private string? _capturingAction; // P05-9: awaiting the next key press for a rebind
    private readonly Dictionary<string, Button> _bindButtons = [];

    public override void _Ready()
    {
        AppEnv.Install();
        AppEnv.Info("menu booted");
        _settings = UserSettings.Load();
        KeyBinds.Load(_settings);
        SessionState.SelectedShipId = _settings.LastShipId;

        var root = new VBoxContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f,
            AnchorTop = 0.5f, AnchorBottom = 0.5f,
        };
        root.AddThemeConstantOverride("separation", 14);
        AddChild(root);

        L10n.Language = _settings.Language;

        var title = new Label { Text = $"{L10n.Tr("menu.title")}  v{GameVersion.Full}" };
        title.AddThemeFontSizeOverride("font_size", 42);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        root.AddChild(title);

        var subtitle = new Label { Text = L10n.Tr("menu.subtitle") };
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

        // R3.4 settings: volumes + language, persisted to settings.json.
        var settingsBtn = new Button { Text = L10n.Tr("menu.settings") };
        root.AddChild(settingsBtn);

        var panel = new VBoxContainer { Visible = false };
        panel.AddThemeConstantOverride("separation", 8);
        root.AddChild(panel);

        var masterLabel = new Label();
        var masterLabelUpdate = () => masterLabel.Text = $"{L10n.Tr("settings.master")} {_settings.MasterVolume:0.00}";
        var master = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = _settings.MasterVolume };
        master.ValueChanged += v =>
        {
            _settings.MasterVolume = (float)v;
            masterLabelUpdate();
            AudioManager.ApplyVolumes(_settings.MasterVolume, _settings.EffectsVolume, _settings.AmbientVolume);
            _settings.Save();
        };
        masterLabelUpdate();
        panel.AddChild(masterLabel);
        panel.AddChild(master);

        var fxLabel = new Label();
        var fxLabelUpdate = () => fxLabel.Text = $"{L10n.Tr("settings.effects")} {_settings.EffectsVolume:0.00}";
        var fx = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = _settings.EffectsVolume };
        fx.ValueChanged += v =>
        {
            _settings.EffectsVolume = (float)v;
            fxLabelUpdate();
            AudioManager.ApplyVolumes(_settings.MasterVolume, _settings.EffectsVolume, _settings.AmbientVolume);
            _settings.Save();
        };
        fxLabelUpdate();
        panel.AddChild(fxLabel);
        panel.AddChild(fx);

        // P05-9: ambient volume — was hardcoded 0.6 until now.
        var ambLabel = new Label();
        var ambLabelUpdate = () => ambLabel.Text = $"{L10n.Tr("settings.ambient")} {_settings.AmbientVolume:0.00}";
        var amb = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.05, Value = _settings.AmbientVolume };
        amb.ValueChanged += v =>
        {
            _settings.AmbientVolume = (float)v;
            ambLabelUpdate();
            AudioManager.ApplyVolumes(_settings.MasterVolume, _settings.EffectsVolume, _settings.AmbientVolume);
            _settings.Save();
        };
        ambLabelUpdate();
        panel.AddChild(ambLabel);
        panel.AddChild(amb);

        // P05-9 minimal key rebinding: click a key button, then press the new key.
        panel.AddChild(new Label { Text = L10n.Tr("settings.keys") });
        var keysGrid = new GridContainer { Columns = 2 };
        keysGrid.AddThemeConstantOverride("h_separation", 12);
        foreach (var action in KeyBinds.Actions)
        {
            var keyLabel = new Label { Text = L10n.Tr($"key.{action}") };
            keyLabel.AddThemeFontSizeOverride("font_size", 13);
            keysGrid.AddChild(keyLabel);
            var bindBtn = new Button { Text = KeyBinds.LabelOf(action), CustomMinimumSize = new Vector2(90, 0) };
            bindBtn.Pressed += () =>
            {
                _capturingAction = action;
                bindBtn.Text = L10n.Tr("key.press");
            };
            _bindButtons[action] = bindBtn;
            keysGrid.AddChild(bindBtn);
        }

        panel.AddChild(keysGrid);

        var lang = new OptionButton { };
        lang.AddItem("中文 zh-CN");
        lang.AddItem("English en");
        lang.Selected = _settings.Language == "en" ? 1 : 0;
        lang.ItemSelected += idx =>
        {
            _settings.Language = (long)idx == 1 ? "en" : "zh-CN";
            L10n.Language = _settings.Language;
            AppEnv.Info("language: " + _settings.Language);
            _settings.Save();
            GetTree().ReloadCurrentScene();
        };
        panel.AddChild(new Label { Text = L10n.Tr("settings.language") });
        panel.AddChild(lang);

        settingsBtn.Pressed += () => panel.Visible = !panel.Visible;

        var spacer2 = new Control { CustomMinimumSize = new Vector2(0, 16) };
        root.AddChild(spacer2);

        var start = new Button { Text = L10n.Tr("menu.start") };
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

        var quit = new Button { Text = L10n.Tr("menu.quit") };
        quit.Pressed += () => GetTree().Quit();
        root.AddChild(quit);

        root.Ready += () => root.Position = -root.Size / 2f;

        // R5.2: automated full-chain verification (menu -> battle -> report).
        if (OS.GetEnvironment("NT_FRONTEND_AUTO") == "1")
        {
            var envShip = OS.GetEnvironment("NT_FRONTEND_SHIP");
            if (envShip.Length > 0 && _shipIds.Contains(envShip))
            {
                SessionState.SelectedShipId = envShip;
            }
            else if (_shipIds.Count > 0)
            {
                SessionState.SelectedShipId = _shipIds[Math.Max(0, _shipPicker.Selected)];
            }

            AppEnv.Info("AUTO flow: starting battle with " + SessionState.SelectedShipId);
            var tree = GetTree();
            tree.CreateTimer(1.5).Timeout += () => GetTree().ChangeSceneToFile("res://Main.tscn");
        }

        var shot = OS.GetEnvironment("NT_FRONTEND_SHOT");
        if (shot.Length > 0)
        {
            var tree = GetTree();
            tree.CreateTimer(1.0).Timeout += () =>
            {
                var img = ((Viewport)tree.Root).GetTexture().GetImage();
                img.SavePng(shot);
                GD.Print("menu shot saved: " + shot);
            };
        }
    }

    /// <summary>P05-9: while a rebind button is armed, the next key press becomes the binding.</summary>
    public override void _UnhandledInput(InputEvent e)
    {
        if (_capturingAction is null || e is not InputEventKey { Pressed: true } key)
        {
            return;
        }

        if (key.Keycode is Key.Shift or Key.Ctrl or Key.Alt or Key.Meta)
        {
            return; // ignore bare modifiers
        }

        KeyBinds.Set(_capturingAction, key.Keycode);
        _bindButtons[_capturingAction].Text = KeyBinds.LabelOf(_capturingAction);
        AppEnv.Info($"rebind {_capturingAction} -> {key.Keycode}");
        _capturingAction = null;
        GetViewport().SetInputAsHandled();
    }

    private List<(string Id, string Name)> LoadFleetShips()
    {
        var result = new List<(string, string)>();
        try
        {
            var exeDir = System.IO.Path.GetDirectoryName(OS.GetExecutablePath());
            var fleet = System.IO.Path.Combine(exeDir ?? "", "data", "ships", "generated_fleet.json");
            if (!System.IO.File.Exists(fleet))
            {
                string repoRoot = ProjectSettings.GlobalizePath("res://");
                while (repoRoot is not null && !File.Exists(System.IO.Path.Combine(repoRoot, "NavyThunder.slnx")))
                {
                    repoRoot = System.IO.Directory.GetParent(repoRoot)?.FullName;
                }

                fleet = System.IO.Path.Combine(repoRoot!, "data", "ships", "generated_fleet.json");
            }

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
