namespace NavyThunder.Core.World;

public interface ISimulationEvent
{
    ulong Tick { get; }
    double Time { get; }
    string Kind { get; }
}

public abstract record SimulationEvent : ISimulationEvent
{
    public ulong Tick { get; internal set; }
    public double Time { get; internal set; }
    public abstract string Kind { get; }
}

/// <summary>
/// Append-only combat log. Every damage-relevant occurrence is recorded here so that
/// Protection Analysis and automated tests can assert on the exact same event stream.
/// </summary>
public sealed class EventLog
{
    private readonly List<SimulationEvent> _events = [];

    public IReadOnlyList<SimulationEvent> All => _events;

    internal void Add(SimulationEvent e) => _events.Add(e);

    public IEnumerable<T> Of<T>() where T : SimulationEvent => _events.OfType<T>();

    public void Clear() => _events.Clear();
}
