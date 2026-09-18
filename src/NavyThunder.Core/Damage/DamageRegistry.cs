using NavyThunder.Core.Mathematics;

namespace NavyThunder.Core.Damage;

/// <summary>
/// Unified damage channels. Every source of harm is funneled through exactly one of
/// these so Protection Analysis and tests can audit the full damage chain (plan §C).
/// </summary>
public enum DamageChannel
{
    Kinetic,
    Chemical,
    Overpressure,
    HydroShock,
    Fragment,
    Fire,
    Flood,
}

/// <summary>One unit of harm routed to a target. Amount is on the generic engine HP scale.</summary>
public sealed record DamageEvent
{
    public required DamageChannel Channel { get; init; }

    /// <summary>Shell / torpedo / fire id that produced this damage.</summary>
    public required string SourceId { get; init; }

    public required string TargetId { get; init; }

    public Vec3 Position { get; init; }

    /// <summary>Radial extent of the damage (blast); 0 = point damage.</summary>
    public double Radius { get; init; }

    public required double Amount { get; init; }

    public ulong Tick { get; init; }
    public double Time { get; init; }
}

/// <summary>
/// Anything that can take damage: ship damage model (Phase 3), aircraft damage model
/// (Phase 4), or test sinks. Implementations translate generic amounts into module,
/// compartment, crew and structure effects per their own model.
/// </summary>
public interface IDamageSink
{
    string TargetId { get; }

    void ApplyDamage(DamageEvent e);
}

/// <summary>
/// Routes damage events to registered sinks and keeps the full log for Protection
/// Analysis and golden-file tests.
/// </summary>
public sealed class DamageRegistry
{
    private readonly Dictionary<string, IDamageSink> _sinks = [];
    private readonly List<DamageEvent> _log = [];

    public IReadOnlyList<DamageEvent> Log => _log;

    public void Register(IDamageSink sink) => _sinks[sink.TargetId] = sink;

    public void Apply(DamageEvent e)
    {
        _log.Add(e);
        if (_sinks.TryGetValue(e.TargetId, out var sink))
        {
            sink.ApplyDamage(e);
            return;
        }

        // Nested host ids ("ship:x/part", used by fires) route to their container sink.
        int slash = e.TargetId.LastIndexOf('/');
        while (slash > 0)
        {
            string prefix = e.TargetId[..slash];
            if (_sinks.TryGetValue(prefix, out sink))
            {
                sink.ApplyDamage(e);
                return;
            }

            slash = prefix.LastIndexOf('/');
        }
    }
}
