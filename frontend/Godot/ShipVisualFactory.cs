using Godot;

namespace NavyThunder.Frontend;

/// <summary>
/// Phase 03 asset pipeline consumer: loads converted ship models (.glb per LOD)
/// through the ShipAssetRegistry and caches the parsed PackedScene per ship id.
/// The factory NEVER gates gameplay — any load failure falls back to the
/// procedural placeholder inside <see cref="ShipVisual"/> (three-layer
/// separation: the model is presentation only, never collision/armor).
/// </summary>
public static class ShipVisualFactory
{
    private static readonly Dictionary<string, PackedScene?> SceneCache = new();
    private static readonly object Gate = new();

    /// <summary>Loads (and caches) the GLB scene for one LOD of a ship.</summary>
    public static Node3D? LoadModel(string shipId, string glbPath)
    {
        lock (Gate)
        {
            var cacheKey = $"{shipId}|{glbPath}";
            if (SceneCache.TryGetValue(cacheKey, out var cached))
            {
                return cached?.Instantiate() as Node3D;
            }

            PackedScene? packed = null;
            try
            {
                var scene = GlbLoader.Load(glbPath);
                if (scene is not null)
                {
                    AssignOwners(scene, scene);
                    packed = new PackedScene();
                    var err = packed.Pack(scene);
                    if (err != Error.Ok)
                    {
                        AppEnv.Error($"ShipVisualFactory: pack {glbPath}: {err}");
                        scene.QueueFree();
                        packed = null;
                    }
                    else
                    {
                        scene.QueueFree();
                    }
                }
            }
            catch (Exception ex)
            {
                AppEnv.Error($"ShipVisualFactory: failed to load {glbPath}: {ex.Message}");
            }

            SceneCache[cacheKey] = packed;
            return packed?.Instantiate() as Node3D;
        }
    }

    /// <summary>PackedScene.Pack only stores nodes owned by the packed root —
    /// nodes created via bare AddChild have a null owner and would be dropped.</summary>
    private static void AssignOwners(Node node, Node root)
    {
        foreach (var child in node.GetChildren())
        {
            child.Owner = root;
            AssignOwners(child, root);
        }
    }

    public static void ClearCache()
    {
        lock (Gate)
        {
            SceneCache.Clear();
        }
    }
}
