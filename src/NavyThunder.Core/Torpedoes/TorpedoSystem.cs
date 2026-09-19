using NavyThunder.Core.Armor;
using NavyThunder.Core.Ballistics;
using NavyThunder.Core.Damage;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Model;
using NavyThunder.Core.Ships;
using NavyThunder.Core.World;

namespace NavyThunder.Core.Torpedoes;

public sealed record TorpedoHit : SimulationEvent
{
    public string TorpedoId { get; init; } = "";
    public string TargetId { get; init; } = "";
    public string PlateId { get; init; } = "";
    public Vec3 Position { get; init; }
    public double WarheadMassKg { get; init; }
    public double BreachRadiusM { get; init; }
    public override string Kind => "torpedo_hit";
}

/// <summary>
/// Straight-running torpedoes (MDR-0012): fixed running depth, contact fuze only,
/// arming distance, hydroShock underwater damage and a large flooding breach on hit.
/// </summary>
public sealed class TorpedoSystem : ISimulationSystem
{
    private sealed class TorpedoState
    {
        public required string Id;
        public required TorpedoDefinition Definition;
        public required string TargetId;
        public required ArmorTarget Armor;
        public required Vec3 Position;
        public required Vec3 Velocity;
        public double TravelledM;
        public bool Alive = true;
    }

    private readonly DamageRegistry _registry;
    private readonly FloodingSystem? _flooding;
    private readonly List<TorpedoState> _torpedoes = [];
    private int _nextId;

    public string Name => "torpedoes";

    public TorpedoSystem(DamageRegistry registry, FloodingSystem? flooding = null)
    {
        _registry = registry;
        _flooding = flooding;
    }

    public void Initialize(SimulationWorld world)
    {
    }

    public string Spawn(TorpedoDefinition torpedo, string targetId, ArmorTarget targetArmor, Vec3 origin, Vec3 direction)
    {
        var state = new TorpedoState
        {
            Id = $"torpedo_{++_nextId}",
            Definition = torpedo,
            TargetId = targetId,
            Armor = targetArmor,
            Position = origin,
            Velocity = direction.Normalized() * torpedo.SpeedMs,
        };
        _torpedoes.Add(state);
        return state.Id;
    }

    public void Update(SimulationWorld world, double deltaTime)
    {
        foreach (var t in _torpedoes)
        {
            if (!t.Alive)
            {
                continue;
            }

            Vec3 before = t.Position;
            Vec3 step = t.Velocity * deltaTime;
            t.Position += step;
            t.TravelledM += step.Length;

            if (t.TravelledM < t.Definition.ArmDistanceM)
            {
                continue; // not yet armed (MDR-0012)
            }

            if (t.TravelledM > t.Definition.RangeM)
            {
                t.Alive = false; // fuel exhausted: sinks without detonating
                continue;
            }

            Vec3 dir = t.Velocity.Normalized();
            foreach (var (plate, hit) in t.Armor.Trace(before, dir))
            {
                if (hit.Distance > step.Length)
                {
                    break;
                }

                // Contact fuze: any hull plate contact detonates (fixed depth running).
                double patchRadius = t.Definition.ExplosionPatchRadiusM is { Length: > 0 } r ? r[1] : 8.0;
                world.Record(new TorpedoHit
                {
                    TorpedoId = t.Id,
                    TargetId = t.TargetId,
                    PlateId = plate.Id,
                    Position = hit.Point,
                    WarheadMassKg = t.Definition.WarheadMassKg,
                    BreachRadiusM = patchRadius,
                });

                _flooding?.CreateBreach(world, t.TargetId, hit.Point, patchRadius);
                _registry.Apply(new DamageEvent
                {
                    Channel = DamageChannel.HydroShock,
                    SourceId = t.Id,
                    TargetId = t.TargetId,
                    Position = hit.Point,
                    Radius = patchRadius * 2,
                    Amount = 800.0 * Math.Cbrt(Math.Max(t.Definition.WarheadMassKg, 0.1)) / 10.0,
                    Tick = world.TickIndex,
                    Time = world.Time,
                });

                t.Alive = false;
                break;
            }
        }

        _torpedoes.RemoveAll(t => !t.Alive);
    }
}
