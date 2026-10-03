using System.Text.Json;
using Godot;

namespace NavyThunder.Frontend;

/// <summary>
/// Minimal GLB reader for the Phase 03 converter's output (tools/convert_bim2_gltf.py):
/// JSON + binary chunk, POSITION/NORMAL/TEXCOORD_0 accessors, u32 indices, node
/// translation/rotation hierarchy, baseColorFactor materials. Hand-rolled because
/// GodotSharp 4.7 does not expose GLTFDocument to C# runtime builds and the assets
/// live outside res:// (no editor import step). Own pipeline, own format — inputs
/// are produced by the same repository's converter.
/// </summary>
public static class GlbLoader
{
    public static Node3D? Load(string path)
    {
        try
        {
            var data = File.ReadAllBytes(path);
            if (data.Length < 20 || data[0] != (byte)'g' || data[1] != (byte)'l')
            {
                return null;
            }

            uint jsonLen = BitConverter.ToUInt32(data, 12);
            var json = System.Text.Json.JsonDocument.Parse(
                new ReadOnlyMemory<byte>(data, 20, (int)jsonLen),
                new JsonDocumentOptions());
            int binChunkHdr = 20 + (int)jsonLen;
            uint binLen = BitConverter.ToUInt32(data, binChunkHdr);
            var bin = new ArraySegment<byte>(data, binChunkHdr + 8, (int)binLen);

            return BuildScene(json.RootElement, bin);
        }
        catch (Exception ex)
        {
            AppEnv.Error($"GlbLoader: {path}: {ex.Message}");
            return null;
        }
    }

    private static Node3D BuildScene(JsonElement root, ArraySegment<byte> bin)
    {
        var container = new Node3D { Name = "model" };

        var accessors = root.GetProperty("accessors");
        var views = root.GetProperty("bufferViews");
        var meshes = root.GetProperty("meshes").EnumerateArray().ToList();
        var nodes = root.GetProperty("nodes").EnumerateArray().ToList();
        var materials = root.TryGetProperty("materials", out var mats) ? mats.EnumerateArray().ToList() : [];

        var nodeObjects = new Node3D[nodes.Count];
        int surfTotal = 0, vertTotal = 0, meshInstances = 0;
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var n3 = new Node3D { Name = node.GetProperty("name").GetString() ?? $"node{i}" };
            if (node.TryGetProperty("translation", out var t))
            {
                n3.Position = new Vector3(t[0].GetSingle(), t[1].GetSingle(), t[2].GetSingle());
            }
            if (node.TryGetProperty("rotation", out var r))
            {
                n3.Quaternion = new Quaternion(r[0].GetSingle(), r[1].GetSingle(), r[2].GetSingle(), r[3].GetSingle());
            }
            if (node.TryGetProperty("mesh", out var mi))
            {
                var meshEl = meshes[mi.GetInt32()];
                var mesh = new ArrayMesh();
                int surf = 0;
                foreach (var prim in meshEl.GetProperty("primitives").EnumerateArray())
                {
                    // Outer array = one slot per semantic (ARRAY_VERTEX..ARRAY_INDEX),
                    // exactly ARRAY_MAX entries; empty slots are Variant.Nil.
                    var posArray = ReadVec3(prim.GetProperty("attributes").GetProperty("POSITION"), accessors, views, bin);
                    var arrays = new Godot.Collections.Array
                    {
                        posArray,
                        prim.GetProperty("attributes").TryGetProperty("NORMAL", out var ni)
                            ? ReadVec3(ni, accessors, views, bin)
                            : System.Array.Empty<Vector3>(),
                        default, // tangent
                        default, // color
                        prim.GetProperty("attributes").TryGetProperty("TEXCOORD_0", out var uvi)
                            ? ReadVec2(uvi, accessors, views, bin)
                            : System.Array.Empty<Vector2>(),
                        default, // uv2
                        default, // custom0
                        default, // custom1
                        default, // custom2
                        default, // custom3
                        default, // bones
                        default, // weights
                        ReadIndices(prim.GetProperty("indices"), accessors, views, bin),
                    };

                    mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                    vertTotal += posArray.Length;

                    var mat = new StandardMaterial3D();
                    if (prim.TryGetProperty("material", out var matIdx))
                    {
                        var m = materials[matIdx.GetInt32()];
                        if (m.TryGetProperty("pbrMetallicRoughness", out var pbr)
                            && pbr.TryGetProperty("baseColorFactor", out var col))
                        {
                            mat.AlbedoColor = new Color(col[0].GetSingle(), col[1].GetSingle(),
                                col[2].GetSingle(), 1f);
                        }
                    }
                    mesh.SurfaceSetMaterial(surf++, mat);
                }
                n3.AddChild(new MeshInstance3D { Mesh = mesh });
                meshInstances++;
                surfTotal += mesh.GetSurfaceCount();
            }
            nodeObjects[i] = n3;
            container.AddChild(n3);
        }

        // reparent declared children (after every node exists)
        for (int i = 0; i < nodes.Count; i++)
        {
            if (!nodes[i].TryGetProperty("children", out var kids))
            {
                continue;
            }
            foreach (var k in kids.EnumerateArray())
            {
                var child = nodeObjects[k.GetInt32()];
                container.RemoveChild(child);
                nodeObjects[i].AddChild(child);
            }
        }
        GD.Print($"GlbLoader: {nodes.Count} nodes, {meshInstances} meshes, {surfTotal} surfaces, {vertTotal} verts");
        return container;
    }

    private static (int Offset, int Count) AccInfo(JsonElement acc, JsonElement views)
    {
        int view = acc.GetProperty("bufferView").GetInt32();
        int count = acc.GetProperty("count").GetInt32();
        int offset = views[view].TryGetProperty("byteOffset", out var bo) ? bo.GetInt32() : 0;
        return (offset, count);
    }

    private static Vector3[] ReadVec3(JsonElement accEl, JsonElement accessors, JsonElement views, ArraySegment<byte> bin)
    {
        var (ofs, count) = AccInfo(accessors[accEl.GetInt32()], views);
        var outArr = new Vector3[count];
        int p = bin.Offset + ofs;
        for (int i = 0; i < count; i++)
        {
            outArr[i] = new Vector3(
                BitConverter.ToSingle(bin.Array!, p),
                BitConverter.ToSingle(bin.Array!, p + 4),
                BitConverter.ToSingle(bin.Array!, p + 8));
            p += 12;
        }
        return outArr;
    }

    private static Vector2[] ReadVec2(JsonElement accEl, JsonElement accessors, JsonElement views, ArraySegment<byte> bin)
    {
        var (ofs, count) = AccInfo(accessors[accEl.GetInt32()], views);
        var outArr = new Vector2[count];
        int p = bin.Offset + ofs;
        for (int i = 0; i < count; i++)
        {
            outArr[i] = new Vector2(BitConverter.ToSingle(bin.Array!, p), BitConverter.ToSingle(bin.Array!, p + 4));
            p += 8;
        }
        return outArr;
    }

    private static int[] ReadIndices(JsonElement accEl, JsonElement accessors, JsonElement views, ArraySegment<byte> bin)
    {
        var acc = accessors[accEl.GetInt32()];
        var (ofs, count) = AccInfo(acc, views);
        var outArr = new int[count];
        int p = bin.Offset + ofs;
        if (acc.GetProperty("componentType").GetInt32() == 5125) // u32
        {
            for (int i = 0; i < count; i++)
            {
                outArr[i] = BitConverter.ToInt32(bin.Array!, p);
                p += 4;
            }
        }
        else // u16
        {
            for (int i = 0; i < count; i++)
            {
                outArr[i] = BitConverter.ToUInt16(bin.Array!, p);
                p += 2;
            }
        }
        return outArr;
    }
}
