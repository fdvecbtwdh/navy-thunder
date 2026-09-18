using System.Text.Json;
using System.Text.Json.Serialization;
using NavyThunder.Core.Model;

namespace NavyThunder.Data;

/// <summary>
/// Loads and validates all JSON documents under the data directory. Documents are
/// discriminated by their top-level "kind" property. Loading is deterministic and
/// fails loudly: any validation error aborts the whole load with a full error list,
/// so a broken data file can never silently enter the engine.
/// </summary>
public sealed class DataRepository
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public IReadOnlyDictionary<string, ShellDefinition> Shells { get; }
    public IReadOnlyDictionary<string, TorpedoDefinition> Torpedoes { get; }
    public IReadOnlyDictionary<string, ShipDefinition> Ships { get; }
    public IReadOnlyDictionary<string, WtReferenceEntry> WtReferences { get; }
    public IReadOnlyDictionary<string, CalibrationEntry> Calibration { get; }

    private DataRepository(
        Dictionary<string, ShellDefinition> shells,
        Dictionary<string, TorpedoDefinition> torpedoes,
        Dictionary<string, ShipDefinition> ships,
        Dictionary<string, WtReferenceEntry> wtReferences,
        Dictionary<string, CalibrationEntry> calibration)
    {
        Shells = shells;
        Torpedoes = torpedoes;
        Ships = ships;
        WtReferences = wtReferences;
        Calibration = calibration;
    }

    public static DataRepository LoadFromDirectory(string dataDirectory)
    {
        if (!Directory.Exists(dataDirectory))
        {
            throw new DirectoryNotFoundException($"Data directory not found: {dataDirectory}");
        }

        var files = Directory.EnumerateFiles(dataDirectory, "*.json", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var docs = new List<(string Path, string Json)>(files.Count);
        foreach (var file in files)
        {
            docs.Add((file, File.ReadAllText(file)));
        }

        return LoadFromDocuments(docs);
    }

    public static DataRepository LoadFromDocuments(IEnumerable<(string Path, string Json)> documents)
    {
        var errors = new List<string>();
        var shells = new Dictionary<string, ShellDefinition>();
        var torpedoes = new Dictionary<string, TorpedoDefinition>();
        var ships = new Dictionary<string, ShipDefinition>();
        var wtReferences = new Dictionary<string, WtReferenceEntry>();
        var calibration = new Dictionary<string, CalibrationEntry>();

        foreach (var (path, json) in documents)
        {
            int errorCountBeforeDoc = errors.Count;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var kind = doc.RootElement.TryGetProperty("kind", out var k)
                    ? k.GetString()
                    : null;

                switch (kind)
                {
                    case "shellSet":
                        var shellSet = Deserialize<ShellSetDocument>(path, json);
                        CheckSchemaVersion(path, shellSet.SchemaVersion, errors);
                        foreach (var shell in shellSet.Shells)
                        {
                            DataValidator.Validate(shell, errors);
                            AddUnique(shells, shell.Id, shell, path, errors);
                        }

                        break;

                    case "torpedoSet":
                        var torpedoSet = Deserialize<TorpedoSetDocument>(path, json);
                        CheckSchemaVersion(path, torpedoSet.SchemaVersion, errors);
                        foreach (var torpedo in torpedoSet.Torpedoes)
                        {
                            DataValidator.Validate(torpedo, errors);
                            AddUnique(torpedoes, torpedo.Id, torpedo, path, errors);
                        }

                        break;

                    case "shipSet":
                        var shipSet = Deserialize<ShipSetDocument>(path, json);
                        CheckSchemaVersion(path, shipSet.SchemaVersion, errors);
                        foreach (var ship in shipSet.Ships)
                        {
                            DataValidator.Validate(ship, errors);
                            AddUnique(ships, ship.Id, ship, path, errors);
                        }

                        break;

                    case "wtReference":
                        var wtDoc = Deserialize<WtReferenceDocument>(path, json);
                        CheckSchemaVersion(path, wtDoc.SchemaVersion, errors);
                        foreach (var entry in wtDoc.Entries)
                        {
                            DataValidator.Validate(entry, errors);
                            AddUnique(wtReferences, entry.Id, entry, path, errors);
                        }

                        break;

                    case "calibration":
                        var calDoc = Deserialize<CalibrationDocument>(path, json);
                        CheckSchemaVersion(path, calDoc.SchemaVersion, errors);
                        foreach (var entry in calDoc.Entries)
                        {
                            DataValidator.Validate(entry, errors);
                            AddUnique(calibration, entry.Id, entry, path, errors);
                        }

                        break;

                    default:
                        errors.Add($"{path}: unknown or missing 'kind' discriminator");
                        break;
                }
            }
            catch (JsonException ex)
            {
                errors.Add($"{path}: JSON parse error: {ex.Message}");
            }

            // Tag document-level validation errors with their file for actionable output;
            // parse errors above already carry the path.
            for (int i = errorCountBeforeDoc; i < errors.Count; i++)
            {
                if (!errors[i].StartsWith(path, StringComparison.Ordinal))
                {
                    errors[i] = $"{path}: {errors[i]}";
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new DataValidationException(errors);
        }

        return new DataRepository(shells, torpedoes, ships, wtReferences, calibration);
    }

    public CalibrationEntry RequireCalibration(string id)
        => Calibration.TryGetValue(id, out var entry)
            ? entry
            : throw new KeyNotFoundException($"Missing calibration parameter: {id}");

    public ShellDefinition RequireShell(string id)
        => Shells.TryGetValue(id, out var shell)
            ? shell
            : throw new KeyNotFoundException($"Missing shell definition: {id}");

    private static T Deserialize<T>(string path, string json)
    {
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
               ?? throw new DataValidationException([$"{path}: document deserialized to null"]);
    }

    private static void CheckSchemaVersion(string path, int version, List<string> errors)
    {
        if (version != CurrentSchemaVersion)
        {
            errors.Add($"{path}: schemaVersion {version} != supported {CurrentSchemaVersion}");
        }
    }

    private static void AddUnique<T>(Dictionary<string, T> target, string? id, T value, string path, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            errors.Add($"{path}: entry Id is null or empty");
            return;
        }

        if (!target.TryAdd(id, value))
        {
            errors.Add($"{path}: duplicate id '{id}'");
        }
    }
}
