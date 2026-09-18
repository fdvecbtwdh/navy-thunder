using NavyThunder.Core.Damage;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.World;

namespace NavyThunder.Core.Fire;

/// <summary>Fire parameters — WT never publishes ignition probabilities or burn DPS (MDR-0010).</summary>
public sealed class FireModel
{
    /// <summary>Base per-hit ignition chance; final chance = base × host flammability × source multiplier.</summary>
    public double IgnitionBaseProbability { get; init; } = 0.05;

    public double CompartmentBurnDps { get; init; } = 15.0;

    public double EngineBurnDps { get; init; } = 12.0;

    public double FuelTankBurnDps { get; init; } = 20.0;

    public double DpsFor(string hostKind) => hostKind switch
    {
        "engine" => EngineBurnDps,
        "fuel" => FuelTankBurnDps,
        _ => CompartmentBurnDps,
    };
}

public sealed class FireInstance
{
    public required string Id { get; init; }
    public required string HostId { get; init; }
    public required string HostKind { get; init; }
    public Vec3 Position { get; init; }
    public double StartedTime { get; init; }
    public double? ExtinguishedTime { get; internal set; }
    public bool Active => ExtinguishedTime is null;
}

/// <summary>
/// Fire entities and ignition rolls. Ignition is an independent roll that does NOT
/// depend on the host's remaining HP (official WT rule, MDR-0010); fires burn their host
/// over time until extinguished by damage control or flooding. Deterministic via the
/// "fire" RNG stream.
/// </summary>
public sealed class FireSystem : ISimulationSystem
{
    private readonly FireModel _model;
    private readonly DamageRegistry _registry;
    private readonly List<FireInstance> _fires = [];
    private readonly Dictionary<string, double> _hostFlammability = [];
    private int _nextFireId;

    public IReadOnlyList<FireInstance> Fires => _fires;

    public string Name => "fire";

    public FireSystem(FireModel model, DamageRegistry registry)
    {
        _model = model;
        _registry = registry;
    }

    public void Initialize(SimulationWorld world)
    {
    }

    /// <summary>Sets a host's flammability multiplier (compartments "highly flammable", engines by type…).</summary>
    public void SetFlammability(string hostId, double multiplier) => _hostFlammability[hostId] = multiplier;

    /// <summary>Independent ignition roll; ignores host HP by design (MDR-0010).</summary>
    public bool TryIgnite(SimulationWorld world, string hostId, string hostKind, Vec3 position, double sourceMultiplier = 1.0)
    {
        if (_fires.Any(f => f.HostId == hostId && f.Active))
        {
            return false; // already burning
        }

        double flammability = _hostFlammability.GetValueOrDefault(hostId, 1.0);
        double chance = _model.IgnitionBaseProbability * flammability * sourceMultiplier;
        if (world.Rng("fire").NextDouble() >= chance)
        {
            return false;
        }

        _fires.Add(new FireInstance
        {
            Id = $"fire_{++_nextFireId}",
            HostId = hostId,
            HostKind = hostKind,
            Position = position,
            StartedTime = world.Time,
        });
        return true;
    }

    /// <summary>Damage-control extinguishing (MDR-0009).</summary>
    public bool Extinguish(SimulationWorld world, string hostId)
    {
        var fire = _fires.FirstOrDefault(f => f.HostId == hostId && f.Active);
        if (fire is null)
        {
            return false;
        }

        fire.ExtinguishedTime = world.Time;
        return true;
    }

    /// <summary>Flooding drowns fires on the host (community-confirmed WT behavior).</summary>
    public void FloodHost(SimulationWorld world, string hostId)
    {
        Extinguish(world, hostId);
    }

    public void Update(SimulationWorld world, double deltaTime)
    {
        foreach (var fire in _fires)
        {
            if (!fire.Active)
            {
                continue;
            }

            _registry.Apply(new DamageEvent
            {
                Channel = DamageChannel.Fire,
                SourceId = fire.Id,
                TargetId = fire.HostId,
                Position = fire.Position,
                Amount = _model.DpsFor(fire.HostKind) * deltaTime,
                Tick = world.TickIndex,
                Time = world.Time,
            });
        }
    }
}
