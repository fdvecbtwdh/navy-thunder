namespace NavyThunder.Core.World;

public interface ISimulationEvent
{
    ulong Tick { get; }
    double Time { get; }
    string Kind { get; }
}

public abstract record SimulationEvent : ISimulationEvent
{
    /// <summary>Monotonic sequence across all events of the world (survives log trimming).</summary>
    public long Seq { get; internal set; }
    public ulong Tick { get; internal set; }
    public double Time { get; internal set; }
    public abstract string Kind { get; }
}

/// <summary>
/// Append-only combat log. Long battles produce millions of events, so the log is a
/// bounded ring: once MaxEvents is exceeded the oldest events are trimmed in chunks.
/// Consumers keep a sequence number and read events After(lastSeq) — trimming shifts
/// positions, not sequences, so consumers never miss or replay events.
/// </summary>
public sealed class EventLog
{
    private readonly List<SimulationEvent> _events = [];
    private long _nextSeq;
    private long _firstSeq; // Seq of _events[0]

    /// <summary>Maximum retained events; oldest are trimmed in 10k chunks beyond this.</summary>
    public int MaxEvents { get; set; } = 200_000;

    public long TotalRecorded => _nextSeq;
    public long FirstSeq => _firstSeq;

    /// <summary>Currently retained events (bounded; oldest may be trimmed).</summary>
    public IReadOnlyList<SimulationEvent> All => _events;

    public SimulationEvent this[int index] => _events[index];

    /// <summary>Events with Seq &gt;= afterSeq, in order.</summary>
    public IEnumerable<SimulationEvent> After(long afterSeq)
    {
        // First retained seq is _firstSeq; index offset = afterSeq - _firstSeq (clamped).
        long offset = Math.Max(0, afterSeq - _firstSeq);
        if (offset >= _events.Count)
        {
            yield break;
        }

        for (int i = (int)offset; i < _events.Count; i++)
        {
            yield return _events[i];
        }
    }

    public IEnumerable<T> Of<T>() where T : SimulationEvent => _events.OfType<T>();

    public IEnumerable<T> After<T>(long afterSeq) where T : SimulationEvent
        => After(afterSeq).OfType<T>();

    internal void Add(SimulationEvent e)
    {
        e.Seq = _nextSeq++;
        _events.Add(e);
        if (_events.Count > MaxEvents)
        {
            _events.RemoveRange(0, 10_000);
            _firstSeq += 10_000;
        }
    }

    public void Clear()
    {
        _events.Clear();
        _firstSeq = _nextSeq;
    }
}
