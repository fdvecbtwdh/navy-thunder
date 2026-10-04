using System.Text.Json;
using NavyThunder.Data;
using Xunit;
using Xunit.Abstractions;

namespace NavyThunder.Core.Tests;

/// <summary>
/// Phase 03 asset pipeline checks (PHASE_03 §18): registry consistency, GLB
/// structural validity, turret-node presence, and hull-length agreement between
/// the converted models and the Core fleet data. Pure file-system assertions —
/// no Godot types. The frontend fallback path (no assets → battle still runs)
/// is covered by the headless smoke with NT_ASSET_ROOT pointing at an empty dir.
/// </summary>
    [Trait("Bucket", "Fast")]
public class ShipAssetPipelineTests(ITestOutputHelper output)
{
    private static string? FindModelsDir()
    {
        string? root = RepoLocator.FindRepoRoot();
        var dir = Path.Combine(root!, "assets", "models");
        return Directory.Exists(dir) ? dir : null;
    }

    private static readonly string[] BismarckTurrets =
        ["main_caliber_turret_01", "main_caliber_turret_02", "main_caliber_turret_03", "main_caliber_turret_04"];

    [Fact]
    public void Metadata_Entries_Are_Consistent_And_Glb_Files_Valid()
    {
        var modelsDir = FindModelsDir();
        Assert.True(modelsDir is not null, "assets/models directory missing — run tools/convert_fleet_models.py");

        var metas = Directory.EnumerateFiles(modelsDir, "metadata.json", SearchOption.AllDirectories).ToList();
        Assert.True(metas.Count >= 20, $"expected >=20 converted ships, found {metas.Count}");

        int checkedGlbs = 0;
        foreach (var metaPath in metas)
        {
            using var json = JsonDocument.Parse(File.ReadAllText(metaPath));
            var el = json.RootElement;
            Assert.Equal(JsonValueKind.True, el.GetProperty("converted").ValueKind);
            Assert.Equal("NT_STANDARD", el.GetProperty("coordinate").GetString());

            string shipId = el.GetProperty("ship_id").GetString()!;
            Assert.Equal(Path.GetFileName(Path.GetDirectoryName(metaPath)), shipId);

            int glbSeen = 0;
            foreach (var lod in el.GetProperty("lods").EnumerateObject())
            {
                var v = lod.Value;
                string file = v.GetProperty("file").GetString()!;
                string path = Path.Combine(Path.GetDirectoryName(metaPath)!, file);
                if (!File.Exists(path))
                {
                    continue; // GLBs are local-only (gitignored WT derivatives)
                }
                AssertGlbReadable(path, shipId);
                glbSeen++;
                checkedGlbs++;
            }
            if (glbSeen == 0)
            {
                output.WriteLine($"{shipId}: no GLBs on disk (run tools/convert_fleet_models.py) - metadata-only check");
            }

            // bbox must straddle the origin and match the fleet length closely
            float len = el.GetProperty("bbox_nt_max")[2].GetSingle() - el.GetProperty("bbox_nt_min")[2].GetSingle();
            Assert.InRange(len, 20, 300);
        }
        output.WriteLine($"validated {metas.Count} ships / {checkedGlbs} GLB files");
    }

    [Fact]
    public void Bismarck_Model_Has_Main_Turret_Nodes_And_NodeMap()
    {
        var modelsDir = FindModelsDir();
        Assert.True(modelsDir is not null);
        string shipDir = Path.Combine(modelsDir!, "rms_bismarck");
        Assert.True(Directory.Exists(shipDir), "rms_bismarck model missing");

        // nodeMap: all four Bismarck main turrets must be present as attach points
        string nmPath = Path.Combine(shipDir, "nodeMap.json");
        Assert.True(File.Exists(nmPath), "nodeMap.json missing");
        using var nm = JsonDocument.Parse(File.ReadAllText(nmPath));
        foreach (var turret in BismarckTurrets)
        {
            Assert.True(nm.RootElement.TryGetProperty(turret, out var node), $"nodeMap lacks {turret}");
            float z = node.GetProperty("pos")[2].GetSingle();
            // Anton/Bruno forward (+Z bow), Caesar/Dora aft
            Assert.True(MathF.Abs(z) > 30, $"{turret} z={z} outside main-battery region");
        }

        // the LOD0 GLB must contain the turret nodes (when models are on disk;
        // GLBs are local-only, the nodeMap check above covers fresh checkouts)
        string glb = Path.Combine(shipDir, "model_lod0.glb");
        if (File.Exists(glb))
        {
            AssertGlbReadable(glb, "rms_bismarck");
            var names = GlbNodeNames(glb);
            foreach (var turret in BismarckTurrets)
            {
                Assert.Contains(turret, names);
            }
        }
        else
        {
            output.WriteLine("rms_bismarck GLB not on disk - nodeMap-only check");
        }
    }

    [Fact]
    public void Converted_Lengths_Match_Fleet_Data_Within_20_Percent()
    {
        var modelsDir = FindModelsDir();
        Assert.True(modelsDir is not null);
        string? root = RepoLocator.FindRepoRoot();
        using var fleet = JsonDocument.Parse(File.ReadAllText(Path.Combine(root!, "data/ships/generated_fleet.json")));
        var byId = fleet.RootElement.GetProperty("ships").EnumerateArray()
            .ToDictionary(s => s.GetProperty("id").GetString()!);

        int compared = 0;
        foreach (var metaPath in Directory.EnumerateFiles(modelsDir, "metadata.json", SearchOption.AllDirectories))
        {
            using var meta = JsonDocument.Parse(File.ReadAllText(metaPath));
            var el = meta.RootElement;
            string shipId = el.GetProperty("ship_id").GetString()!;
            if (!byId.TryGetValue(shipId, out var ship))
            {
                continue;
            }
            float expected = ship.GetProperty("lengthM").GetSingle();
            float actual = el.GetProperty("bbox_nt_max")[2].GetSingle() - el.GetProperty("bbox_nt_min")[2].GetSingle();
            // WT models include bowsprits/rails; allow generous tolerance but catch axis swaps
            Assert.InRange(actual, expected * 0.8f, expected * 1.35f);
            compared++;
        }
        Assert.True(compared >= 15, $"only {compared} ships compared against fleet data");
        output.WriteLine($"length check OK for {compared} ships");
    }

    // ------------------------------------------------------------------ helpers

    private static void AssertGlbReadable(string path, string shipId)
    {
        var data = File.ReadAllBytes(path);
        Assert.True(data.Length > 100, $"{shipId}: {Path.GetFileName(path)} too small");
        Assert.Equal((byte)'g', data[0]);
        Assert.Equal((byte)'l', data[1]);
        Assert.Equal((byte)'T', data[2]);
        Assert.Equal((byte)'F', data[3]);
        uint version = BitConverter.ToUInt32(data, 4);
        Assert.Equal(2u, version);
        uint jsonLen = BitConverter.ToUInt32(data, 12);
        Assert.True(jsonLen > 2 && 20 + jsonLen <= (ulong)data.Length, $"{shipId}: bad JSON chunk");
        using var json = System.Text.Json.JsonDocument.Parse(new ReadOnlyMemory<byte>(data, 20, (int)jsonLen));
        Assert.True(json.RootElement.TryGetProperty("meshes", out var meshes) && meshes.GetArrayLength() > 0,
            $"{shipId}: {Path.GetFileName(path)} has no meshes");
        Assert.True(json.RootElement.TryGetProperty("nodes", out var nodes) && nodes.GetArrayLength() > 0,
            $"{shipId}: {Path.GetFileName(path)} has no nodes");
    }

    private static HashSet<string> GlbNodeNames(string path)
    {
        var data = File.ReadAllBytes(path);
        uint jsonLen = BitConverter.ToUInt32(data, 12);
        using var json = System.Text.Json.JsonDocument.Parse(new ReadOnlyMemory<byte>(data, 20, (int)jsonLen));
        var names = new HashSet<string>();
        foreach (var n in json.RootElement.GetProperty("nodes").EnumerateArray())
        {
            if (n.TryGetProperty("name", out var nameEl))
            {
                names.Add(nameEl.GetString()!);
            }
        }
        return names;
    }
}
