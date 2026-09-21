using Godot;

namespace NavyThunder.Frontend;

/// <summary>
/// R4.3: per-user storage (Godot user:// = %APPDATA%/Godot/app_userdata/NavyThunder),
/// file logging and crash capture. Install once at startup.
/// </summary>
public static class AppEnv
{
    public static string UserDir { get; private set; } = "";

    private static readonly object Gate = new();

    /// <summary>Initializes user directories and hooks global crash handlers.</summary>
    public static void Install()
    {
        UserDir = OS.GetUserDataDir();
        Directory.CreateDirectory(Path.Combine(UserDir, "logs"));
        Directory.CreateDirectory(Path.Combine(UserDir, "crash"));
        Directory.CreateDirectory(Path.Combine(UserDir, "saves"));

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrash("UnhandledException", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrash("UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    public static void Info(string message) => Write("logs/latest.log", "INFO ", message);

    public static void Error(string message) => Write("logs/latest.log", "ERROR", message);

    public static void WriteCrash(string kind, Exception? ex)
    {
        var name = $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.log";
        var text = $"{kind}\n{DateTime.Now:O}\n{ex}\n\n--- log tail ---\n{ReadTail("logs/latest.log", 8000)}";
        Write($"crash/{name}", "CRASH", text);
        GD.PushError($"{kind}: {ex?.Message}");
    }

    private static string ReadTail(string rel, int maxChars)
    {
        try
        {
            var p = Path.Combine(UserDir, rel);
            if (!File.Exists(p))
            {
                return "";
            }

            var text = File.ReadAllText(p);
            return text.Length <= maxChars ? text : text[^maxChars..];
        }
        catch
        {
            return "";
        }
    }

    private static void Write(string rel, string level, string message)
    {
        try
        {
            lock (Gate)
            {
                var p = Path.Combine(UserDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                File.AppendAllText(p, $"{DateTime.Now:HH:mm:ss.fff} {level} {message}\n");
            }
        }
        catch
        {
            // logging must never take the game down
        }

        if (level == "ERROR")
        {
            GD.PushError(message);
        }
        else
        {
            GD.Print(message);
        }
    }
}

/// <summary>R4.3: persisted user settings (user://settings.json).</summary>
public sealed class UserSettings
{
    public float MasterVolume { get; set; } = 0.8f;
    public float EffectsVolume { get; set; } = 1.0f;
    public string Language { get; set; } = "zh-CN";
    public string? LastShipId { get; set; }
    public string? LastScenario { get; set; }

    private static string Path_ => System.IO.Path.Combine(AppEnv.UserDir, "settings.json");

    public static UserSettings Load()
    {
        try
        {
            var p = Path_;
            if (System.IO.File.Exists(p))
            {
                return System.Text.Json.JsonSerializer.Deserialize<UserSettings>(
                    System.IO.File.ReadAllText(p)) ?? new UserSettings();
            }
        }
        catch (Exception ex)
        {
            AppEnv.Error($"settings load failed: {ex.Message}");
        }

        return new UserSettings();
    }

    public void Save()
    {
        try
        {
            System.IO.File.WriteAllText(Path_,
                System.Text.Json.JsonSerializer.Serialize(this,
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            AppEnv.Error($"settings save failed: {ex.Message}");
        }
    }
}
