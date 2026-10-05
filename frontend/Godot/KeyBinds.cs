using System.Collections.Generic;
using Godot;

namespace NavyThunder.Frontend;

/// <summary>
/// P05-9 minimal key rebinding: gameplay actions map to Godot keys, persisted in
/// settings.json ("keybinds": {action: keyCode}). Camera keys stay fixed (minimal set —
/// image-quality tiers remain Phase 08). Load once at boot; Set() saves through
/// UserSettings.
/// </summary>
public static class KeyBinds
{
    public const string ThrottleUp = "throttle_up";
    public const string ThrottleDown = "throttle_down";
    public const string RudderPort = "rudder_port";
    public const string RudderStbd = "rudder_stbd";
    public const string RudderCenter = "rudder_center";
    public const string ShellToggle = "shell_toggle";
    public const string AimMode = "aim_mode";
    public const string Lock = "lock";
    public const string Map = "map";
    public const string Pause = "pause";

    /// <summary>Order shown in the settings UI.</summary>
    public static readonly string[] Actions =
    [
        ThrottleUp, ThrottleDown, RudderPort, RudderStbd, RudderCenter,
        ShellToggle, AimMode, Lock, Map, Pause,
    ];

    private static readonly Dictionary<string, Key> Defaults = new()
    {
        [ThrottleUp] = Key.W,
        [ThrottleDown] = Key.S,
        [RudderPort] = Key.A,
        [RudderStbd] = Key.D,
        [RudderCenter] = Key.X,
        [ShellToggle] = Key.R,
        [AimMode] = Key.G,
        [Lock] = Key.T,
        [Map] = Key.M,
        [Pause] = Key.Escape,
    };

    private static Dictionary<string, Key> _binds = new(Defaults);
    private static UserSettings? _settings;

    public static void Load(UserSettings settings)
    {
        _settings = settings;
        _binds = new Dictionary<string, Key>(Defaults);
        if (settings.Keybinds is not { } saved)
        {
            return;
        }

        foreach (var (action, keyName) in saved)
        {
            if (Defaults.ContainsKey(action) && System.Enum.TryParse(keyName, out Key key))
            {
                _binds[action] = key;
            }
        }
    }

    /// <summary>True while the action's key is held (polling style, battle loop).</summary>
    public static bool Down(string action) => _binds.TryGetValue(action, out var key) && Input.IsKeyPressed(key);

    public static Key KeyOf(string action) => _binds.GetValueOrDefault(action);

    public static string LabelOf(string action)
    {
        Key key = KeyOf(action);
        return key == Key.Escape ? "ESC" : key.ToString().ToUpperInvariant();
    }

    /// <summary>Rebinds an action and persists immediately.</summary>
    public static void Set(string action, Key key)
    {
        if (!Defaults.ContainsKey(action) || _settings is null)
        {
            return;
        }

        _binds[action] = key;
        _settings.Keybinds = new Dictionary<string, string>();
        foreach (var (name, bound) in _binds)
        {
            _settings.Keybinds[name] = bound.ToString();
        }

        _settings.Save();
    }
}
