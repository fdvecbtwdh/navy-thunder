using Godot;
using NavyThunder.Core.Mathematics;

namespace NavyThunder.Frontend;

/// <summary>
/// Renders live shell and torpedo states from Core (PHASE_02 §16): shells from
/// <c>BallisticsSystem.Projectiles</c>, torpedoes from <c>TorpedoSystem.DebugTorpedoes</c>.
/// Pure read — the frontend never integrates ballistics; nodes are pooled and swept by
/// projectile id each frame.
/// </summary>
public partial class ProjectileTracers : Node3D
{
    private sealed class Tracer
    {
        public MeshInstance3D Node = null!;
        public int LastSeenFrame;
    }

    private readonly Dictionary<int, Tracer> _shells = [];
    private readonly Dictionary<string, Tracer> _torpedoes = [];
    private readonly Stack<MeshInstance3D> _free = [];
    private int _frame;

    private Material _shellMat = null!, _torpedoMat = null!;

    public override void _Ready()
    {
        _shellMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.75f, 0.25f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.6f, 0.15f) * 1.6f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        _torpedoMat = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.16f, 0.18f) };

        for (int i = 0; i < 160; i++)
        {
            var node = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.8f, 0.8f, 9f) },
                MaterialOverride = _shellMat,
                Visible = false,
            };
            AddChild(node);
            _free.Push(node);
        }
    }

    public void Sync(IReadOnlyList<NavyThunder.Core.Ballistics.BallisticProjectile> projectiles,
        IReadOnlyList<(string Id, Vec3 Position, Vec3 Velocity, bool Armed)> torpedoes)
    {
        _frame++;

        foreach (var p in projectiles)
        {
            if (!_shells.TryGetValue(p.Id, out var tracer))
            {
                if (!_free.TryPop(out var node))
                {
                    break; // pool exhausted: skip extra shells this frame
                }
                tracer = new Tracer { Node = node };
                _shells[p.Id] = tracer;
                node.Visible = true;
            }

            tracer.LastSeenFrame = _frame;
            var pos = p.Position;
            tracer.Node.Position = new Vector3((float)pos.X, (float)pos.Y, (float)pos.Z);
            var dir = p.Velocity.Normalized();
            if (dir.LengthSquared > 0.5)
            {
                // -Z of the box faces the flight direction (symmetric box: any alignment ok).
                tracer.Node.LookAt(tracer.Node.Position + new Vector3((float)dir.X, (float)dir.Y, (float)dir.Z));
            }
        }

        foreach (var t in torpedoes)
        {
            if (!_torpedoes.TryGetValue(t.Id, out var tracer))
            {
                if (!_free.TryPop(out var node))
                {
                    break;
                }
                node.Mesh = new BoxMesh { Size = new Vector3(1.2f, 1.2f, 11f) };
                node.MaterialOverride = _torpedoMat;
                tracer = new Tracer { Node = node };
                _torpedoes[t.Id] = tracer;
                node.Visible = true;
            }

            tracer.LastSeenFrame = _frame;
            tracer.Node.Position = new Vector3((float)t.Position.X, -1.5f, (float)t.Position.Z);
            var dir = t.Velocity.Normalized();
            if (dir.LengthSquared > 0.5)
            {
                tracer.Node.LookAt(tracer.Node.Position + new Vector3((float)dir.X, 0, (float)dir.Z));
            }
        }

        // Sweep tracers whose simulation body vanished this frame.
        Sweep(_shells);
        Sweep(_torpedoes);
    }

    private void Sweep<T>(Dictionary<T, Tracer> map)
    {
        List<T>? dead = null;
        foreach (var (key, tracer) in map)
        {
            if (tracer.LastSeenFrame != _frame)
            {
                (dead ??= []).Add(key);
            }
        }

        if (dead is null)
        {
            return;
        }

        foreach (var key in dead)
        {
            var tracer = map[key];
            tracer.Node.Visible = false;
            tracer.Node.Scale = Vector3.One;
            _free.Push(tracer.Node);
            map.Remove(key);
        }
    }
}
