using NavyThunder.Core.Mathematics;

namespace NavyThunder.Core.World;

public interface ISimulationSystem
{
    string Name { get; }

    void Initialize(SimulationWorld world);

    void Update(SimulationWorld world, double deltaTime);
}

public interface IEntity
{
    int Id { get; }
}

/// <summary>Base class for world entities; the world assigns stable sequential ids.</summary>
public abstract class Entity : IEntity
{
    public int Id { get; internal set; }
}

/// <summary>
/// Fixed-timestep, deterministically ordered simulation world.
/// Systems update in registration order; random draws come only from named per-subsystem
/// streams derived from the master seed. No wall-clock reads anywhere — identical seeds
/// and inputs reproduce identical event logs, which later enables lockstep networking.
/// </summary>
public sealed class SimulationWorld
{
    public const ulong DefaultMasterSeed = 0x4E617659_5468756E; // "NavyThun"

    private readonly List<ISimulationSystem> _systems = [];
    private readonly List<IEntity> _entities = [];
    private readonly Dictionary<string, DeterministicRandom> _rngStreams = [];
    private int _nextEntityId;

    public double FixedDeltaTime { get; }
    public ulong MasterSeed { get; }
    public double Time { get; private set; }
    public ulong TickIndex { get; private set; }
    public EventLog Events { get; } = new();
    public IReadOnlyList<IEntity> Entities => _entities;

    public SimulationWorld(double fixedDeltaTime = 0.02, ulong masterSeed = DefaultMasterSeed)
    {
        if (fixedDeltaTime <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fixedDeltaTime));
        }

        FixedDeltaTime = fixedDeltaTime;
        MasterSeed = masterSeed;
    }

    public void AddSystem(ISimulationSystem system)
    {
        _systems.Add(system);
        system.Initialize(this);
    }

    /// <summary>Registers an entity and assigns its stable id.</summary>
    public void AddEntity(Entity entity)
    {
        if (entity.Id != 0)
        {
            throw new InvalidOperationException("Entity Id is assigned by the world and must be 0 before registration.");
        }

        entity.Id = ++_nextEntityId;
        _entities.Add(entity);
    }

    public DeterministicRandom Rng(string streamName)
    {
        if (!_rngStreams.TryGetValue(streamName, out var rng))
        {
            rng = RngStreams.Create(MasterSeed, streamName);
            _rngStreams[streamName] = rng;
        }

        return rng;
    }

    public void Record<T>(T e) where T : SimulationEvent
    {
        e.Tick = TickIndex;
        e.Time = Time;
        Events.Add(e);
    }

    public void Step()
    {
        if (_profileSystems)
        {
            foreach (var system in _systems)
            {
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                system.Update(this, FixedDeltaTime);
                _systemProfileTicks[system.Name] =
                    _systemProfileTicks.GetValueOrDefault(system.Name) + (System.Diagnostics.Stopwatch.GetTimestamp() - t0);
            }

            _profileTicks++;
            if (_profileTicks >= 300)
            {
                double totalMs = 0;
                var lines = new System.Collections.Generic.List<string>();
                foreach (var (name, ticks) in _systemProfileTicks)
                {
                    double ms = ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / _profileTicks;
                    totalMs += ms;
                    lines.Add($"    {name,-16} {ms,8:0.00} ms/step");
                }

                lines.Sort();
                Console.WriteLine($"SIM PROFILE ({_profileTicks} steps, total {totalMs:0.00} ms/step):");
                foreach (var line in lines)
                {
                    Console.WriteLine(line);
                }

                _systemProfileTicks.Clear();
                _profileTicks = 0;
            }
        }
        else
        {
            foreach (var system in _systems)
            {
                system.Update(this, FixedDeltaTime);
            }
        }

        Time += FixedDeltaTime;
        TickIndex++;
    }

    private readonly bool _profileSystems =
        Environment.GetEnvironmentVariable("NT_SIM_PROFILE") == "1";
    private readonly System.Collections.Generic.Dictionary<string, long> _systemProfileTicks = new();
    private long _profileTicks;

    /// <summary>Runs for at least <paramref name="durationSeconds"/> of simulated time.</summary>
    public void Run(double durationSeconds)
    {
        int steps = (int)Math.Ceiling(durationSeconds / FixedDeltaTime);
        for (int i = 0; i < steps; i++)
        {
            Step();
        }
    }
}
