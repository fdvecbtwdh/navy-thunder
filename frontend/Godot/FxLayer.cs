using Godot;
using NavyThunder.Core.Mathematics;

namespace NavyThunder.Frontend;

/// <summary>
/// Phase 02 FX layer: consumes Core EventLog entries (via the battle scene's cursor loop)
/// and plays pooled, short-lived 3D gizmos — muzzle flashes, hit flashes, splashes,
/// explosions. Simple pooled spheres with TTL keep the node count bounded (PHASE_02 §37);
/// final particle work belongs to Phase 03/05.
/// </summary>
public partial class FxLayer : Node3D
{
    public enum Kind
    {
        MuzzleFlash,
        HitFlash,
        Splash,
        Explosion,
        BigExplosion,
        Sink,
    }

    private sealed class Puff
    {
        public MeshInstance3D Node = null!;
        public Kind Kind;
        public float Age;
        public float Life;
    }

    private const int PoolSize = 96;
    private readonly Puff[] _pool = new Puff[PoolSize];
    private readonly Dictionary<Kind, Material> _materials = [];
    private int _next;

    public override void _Ready()
    {
        _materials[Kind.MuzzleFlash] = Mat(new Color(1f, 0.85f, 0.3f), 2.2f);
        _materials[Kind.HitFlash] = Mat(new Color(1f, 0.55f, 0.15f), 1.2f);
        _materials[Kind.Splash] = Mat(new Color(0.85f, 0.95f, 1f, 0.8f), 0.2f);
        _materials[Kind.Explosion] = Mat(new Color(1f, 0.35f, 0.05f), 1.4f);
        _materials[Kind.BigExplosion] = Mat(new Color(1f, 0.25f, 0.02f), 2.4f);
        _materials[Kind.Sink] = Mat(new Color(0.5f, 0.6f, 0.62f, 0.6f), 0.2f);

        for (int i = 0; i < PoolSize; i++)
        {
            var node = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = 1f, Height = 2f },
                Visible = false,
            };
            AddChild(node);
            _pool[i] = new Puff { Node = node };
        }
    }

    private static StandardMaterial3D Mat(Color color, float emission) => new()
    {
        AlbedoColor = color,
        EmissionEnabled = true,
        Emission = color * emission,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    public void Spawn(Kind kind, Vec3 worldPos)
    {
        var puff = _pool[_next];
        _next = (_next + 1) % PoolSize;
        puff.Kind = kind;
        puff.Age = 0f;
        puff.Life = kind switch
        {
            Kind.MuzzleFlash => 0.35f,
            Kind.HitFlash => 0.9f,
            Kind.Splash => 1.6f,
            Kind.Explosion => 1.8f,
            Kind.BigExplosion => 3.2f,
            _ => 2.0f,
        };
        puff.Node.MaterialOverride = _materials[kind];
        puff.Node.Position = new Vector3((float)worldPos.X, (float)worldPos.Y, (float)worldPos.Z);
        float scale = kind switch
        {
            Kind.MuzzleFlash => 4f,
            Kind.HitFlash => 3f,
            Kind.Splash => 7f,
            Kind.Explosion => 9f,
            Kind.BigExplosion => 22f,
            _ => 12f,
        };
        puff.Node.Scale = Vector3.One * scale;
        puff.Node.Visible = true;
    }

    public override void _Process(double delta)
    {
        foreach (var puff in _pool)
        {
            if (!puff.Node.Visible)
            {
                continue;
            }

            puff.Age += (float)delta;
            float t = puff.Age / puff.Life;
            if (t >= 1f)
            {
                puff.Node.Visible = false;
                continue;
            }

            // Grow while fading (cheap one-gizmo approximation of an explosion bloom).
            puff.Node.Scale = puff.Node.Scale * (1f + (float)delta * (puff.Kind == Kind.Splash ? 0.4f : 1.1f));
            var mat = (StandardMaterial3D)puff.Node.MaterialOverride!;
            mat.AlbedoColor = new Color(mat.AlbedoColor.R, mat.AlbedoColor.G, mat.AlbedoColor.B, 1f - t);
        }
    }
}
