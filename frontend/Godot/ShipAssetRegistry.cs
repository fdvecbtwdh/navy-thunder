using System.Text.Json;
using Godot;

namespace NavyThunder.Frontend;

/// <summary>
/// One converted ship model (Phase 03 asset pipeline output). Layout per
/// assets/models/&lt;ship_id&gt;/metadata.json written by tools/convert_fleet_models.py.
/// </summary>
public sealed record ShipAssetEntry(
    string ShipId,
    string? NodeMapPath,
    Vector3 BBoxMin,
    Vector3 BBoxMax,
    IReadOnlyDictionary<int, (string File, float RangeM)> Lods);

/// <summary>
/// Data-driven ship id → model lookup (PHASE_03 §4): scans
/// &lt;assetRoot&gt;/models/*/metadata.json once, lazily. Missing entries mean the
/// ship keeps its procedural fallback — the game never requires a model.
/// </summary>
public static class ShipAssetRegistry
{
    private static Dictionary<string, ShipAssetEntry>? _entries;
    private static readonly object Gate = new();

    /// <summary>Locates the repo assets root (env override, then exe/cwd walk-up).</summary>
    public static string? ResolveRoot()
    {
        var env = System.Environment.GetEnvironmentVariable("NT_ASSET_ROOT");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env))
        {
            return env;
        }

        foreach (var start in new[] { System.AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = start;
            for (int i = 0; i < 8 && dir is not null; i++)
            {
                var candidate = Path.Combine(dir, "assets", "models");
                if (Directory.Exists(candidate))
                {
                    return Path.Combine(dir, "assets");
                }
                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            }
        }
        return null;
    }

    public static bool TryGet(string shipId, out ShipAssetEntry entry)
    {
        entry = null!;
        var all = EnsureLoaded();
        // Core TargetIds are "ship:<id>" (+ "#instance" suffix); registry keys are bare ids.
        int colon = shipId.IndexOf(':');
        if (colon >= 0)
        {
            shipId = shipId[(colon + 1)..];
        }
        int hash = shipId.IndexOf('#');
        if (hash >= 0)
        {
            shipId = shipId[..hash];
        }
        return all.TryGetValue(shipId, out entry!);
    }

    public static IReadOnlyDictionary<string, ShipAssetEntry> All => EnsureLoaded();

    private static Dictionary<string, ShipAssetEntry> EnsureLoaded()
    {
        lock (Gate)
        {
            if (_entries is not null)
            {
                return _entries;
            }

            _entries = new Dictionary<string, ShipAssetEntry>();
            var root = ResolveRoot();
            if (root is null)
            {
                GD.PushWarning("ShipAssetRegistry: assets/models not found — all ships use procedural fallback");
                return _entries;
            }

            foreach (var metaPath in Directory.EnumerateFiles(Path.Combine(root, "models"), "metadata.json", SearchOption.AllDirectories))
            {
                try
                {
                    var json = JsonDocument.Parse(File.ReadAllText(metaPath));
                    var el = json.RootElement;
                    if (!el.TryGetProperty("converted", out var conv) || conv.ValueKind != JsonValueKind.True)
                    {
                        continue;
                    }

                    string shipId = el.GetProperty("ship_id").GetString()!;
                    var lods = new Dictionary<int, (string, float)>();
                    foreach (var lod in el.GetProperty("lods").EnumerateObject())
                    {
                        var v = lod.Value;
                        string file = Path.Combine(Path.GetDirectoryName(metaPath)!, v.GetProperty("file").GetString()!);
                        float range = v.GetProperty("range_m").GetSingle();
                        lods[int.Parse(lod.Name)] = (file, range);
                    }

                    Vector3 min = Vector3.Zero, max = Vector3.Zero;
                    if (el.TryGetProperty("bbox_nt_min", out var bmin))
                    {
                        min = new Vector3(bmin[0].GetSingle(), bmin[1].GetSingle(), bmin[2].GetSingle());
                        var bmax = el.GetProperty("bbox_nt_max");
                        max = new Vector3(bmax[0].GetSingle(), bmax[1].GetSingle(), bmax[2].GetSingle());
                    }

                    string? nodeMap = el.TryGetProperty("node_map", out var nmp) && nmp.ValueKind == JsonValueKind.String
                        ? Path.Combine(Path.GetDirectoryName(metaPath)!, nmp.GetString()!)
                        : null;
                    _entries[shipId] = new ShipAssetEntry(shipId, nodeMap, min, max, lods);
                }
                catch (Exception ex)
                {
                    GD.PushWarning($"ShipAssetRegistry: bad metadata {metaPath}: {ex.Message}");
                }
            }
            AppEnv.Info($"ShipAssetRegistry: {_entries.Count} ship models loaded from {root}");
            return _entries;
        }
    }
}
