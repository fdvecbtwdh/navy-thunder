using NavyThunder.Core.Armor;
using NavyThunder.Core.Ballistics;
using NavyThunder.Core.Damage;
using NavyThunder.Core.Explosions;
using NavyThunder.Core.Fire;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Model;
using NavyThunder.Core.World;

namespace NavyThunder.Core.Ships;

/// <summary>
/// Bridges world combat events into ship damage. Ship-agnostic producers (ballistics,
/// explosions) emit events against the ship's target id; this system translates them:
///   - ProjectileArmorImpact (penetrated) -> kinetic damage traced through the interior
///     part boxes along the residual path (deeper penetration reaches deeper compartments)
///   - ShellDetonation against a ship -> chemical damage at the burst position
///   - OverpressureWave -> crew-only overpressure events to ships in range
/// Ships themselves stay free of world access.
/// </summary>
public sealed class DamageBridgeSystem : ISimulationSystem
{
    private readonly DamageRegistry _registry;
    private readonly IReadOnlyDictionary<string, ShellDefinition> _shells;
    private int _processedEvents;

    /// <summary>Engine damage (HP) per cbrt(kg) of shell mass — engine-internal WT scale (approximation).</summary>
    public double KineticDamagePerCbrtKg { get; init; } = 500.0;

    /// <summary>How far (m) a fully-residual penetrating shell keeps travelling through internals.</summary>
    public double InteriorTraceDepthM { get; init; } = 12.0;

    public Dictionary<string, Ship> ShipsByTargetId { get; } = [];

    /// <summary>Optional fire system: combat damage rolls ignition through it (MDR-0010).</summary>
    public FireSystem? Fire { get; set; }

    /// <summary>Shell-category ignition multipliers (HE families burn, caps less so).</summary>
    public double IgnitionMultiplier(ShellCategory category) => category switch
    {
        ShellCategory.HE or ShellCategory.Common or ShellCategory.AACommon or ShellCategory.AAVT => 1.0,
        ShellCategory.SAP or ShellCategory.SpecialCommon => 0.7,
        _ => 0.4,
    };

    public string Name => "damage_bridge";

    public DamageBridgeSystem(DamageRegistry registry, IReadOnlyDictionary<string, ShellDefinition> shells)
    {
        _registry = registry;
        _shells = shells;
    }

    public void Initialize(SimulationWorld world)
    {
    }

    public void Update(SimulationWorld world, double deltaTime)
    {
        var events = world.Events.All;
        while (_processedEvents < events.Count)
        {
            switch (events[_processedEvents])
            {
                case ProjectileArmorImpact impact when impact.Outcome == PlateResolution.Penetrated:
                    ApplyInteriorTrace(world, impact);
                    break;

                case ShellDetonation detonation when detonation.TargetId.Length > 0:
                    ApplyBurst(world, detonation);
                    break;

                case OverpressureWave wave:
                    ApplyOverpressure(world, wave);
                    break;
            }

            _processedEvents++;
        }
    }

    private void ApplyInteriorTrace(SimulationWorld world, ProjectileArmorImpact impact)
    {
        if (!ShipsByTargetId.TryGetValue(impact.TargetId, out var ship)
            || !_shells.TryGetValue(impact.ShellId, out var shell))
        {
            return;
        }

        Vec3 dir = impact.Direction.LengthSquared < 1e-9 ? new Vec3(1, 0, 0) : impact.Direction.Normalized();
        double residual = Math.Max(0.15, impact.ResidualEnergyFraction);
        double depth = Math.Min(InteriorTraceDepthM * residual, ship.Definition.LengthM);

        // Start just behind the struck plate's slab so the trace hits interior parts.
        Vec3 origin = impact.Position + dir * 0.5;
        double totalDamage = KineticDamagePerCbrtKg * Math.Cbrt(shell.MassKg);
        var traversed = TraceInterior(ship, origin, dir, depth);
        if (traversed.Count == 0)
        {
            // Stripped section: the shell still wrecks hull structure (sections must be
            // able to die even when every internal part is already gone - unsinkability).
            _registry.Apply(new DamageEvent
            {
                Channel = DamageChannel.Kinetic,
                SourceId = impact.ShellId,
                TargetId = ship.TargetId,
                Position = origin,
                Amount = totalDamage * residual * 0.5,
                Tick = world.TickIndex,
                Time = world.Time,
            });
            return;
        }

        double lengthSum = traversed.Sum(t => t.PathLength);
        foreach (var (part, pathLength) in traversed)
        {
            _registry.Apply(new DamageEvent
            {
                Channel = DamageChannel.Kinetic,
                SourceId = impact.ShellId,
                TargetId = ship.TargetId,
                Position = part.Center,
                Amount = totalDamage * (pathLength / lengthSum) * residual,
                Tick = world.TickIndex,
                Time = world.Time,
            });

            TryIgnitePart(world, ship, part, impact.ShellId, impact.ShellId);
        }
    }

    /// <summary>Independent ignition roll on a damaged part (MDR-0010: not tied to remaining HP).</summary>
    private void TryIgnitePart(SimulationWorld world, Ship ship, ShipPartState part, string shellId, string sourceId)
    {
        if (Fire is null || part.Destroyed || !part.Definition.Open && part.Definition.Kind is not (PartKind.Compartment or PartKind.Boiler or PartKind.Engine or PartKind.FuelTank))
        {
            return;
        }

        if (!_shells.TryGetValue(shellId, out var shell))
        {
            return;
        }

        double multiplier = IgnitionMultiplier(shell.Category)
                            * (part.Definition.Kind == PartKind.FuelTank ? 1.2 : 1.0);
        Fire.TryIgnite(world, $"{ship.TargetId}/{part.Definition.Id}",
            part.Definition.Kind == PartKind.Engine ? "engine" : "compartment",
            part.Center, multiplier);
    }

    /// <summary>Segment-vs-AABB walk over the ship's part boxes, ordered by entry distance.</summary>
    public static IReadOnlyList<(ShipPartState Part, double PathLength)> TraceInterior(
        Ship ship, Vec3 origin, Vec3 dir, double maxDepth)
    {
        var hits = new List<(ShipPartState Part, double Enter, double Length)>();
        foreach (var part in ship.Parts.Values)
        {
            if (part.Destroyed)
            {
                continue;
            }

            if (RayAabb(origin, dir, part, maxDepth, out double tEnter, out double tExit))
            {
                hits.Add((part, tEnter, tExit - tEnter));
            }
        }

        hits.Sort((a, b) => a.Enter.CompareTo(b.Enter));
        return hits.Select(h => (h.Part, h.Length)).ToList();
    }

    private static bool RayAabb(Vec3 origin, Vec3 dir, ShipPartState part, double maxDepth, out double tEnter, out double tExit)
    {
        tEnter = 0;
        tExit = maxDepth;
        ReadOnlySpan<(double O, double D, double Min, double Max)> axes = stackalloc (double, double, double, double)[3]
        {
            (origin.X, dir.X, part.Definition.XMinM, part.Definition.XMaxM),
            (origin.Y, dir.Y, part.Definition.YMinM, part.Definition.YMaxM),
            (origin.Z, dir.Z, part.Definition.ZMinM, part.Definition.ZMaxM),
        };

        foreach (var (o, d, min, max) in axes)
        {
            if (Math.Abs(d) < 1e-9)
            {
                if (o < min || o > max)
                {
                    return false;
                }

                continue;
            }

            double t1 = (min - o) / d;
            double t2 = (max - o) / d;
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }

            tEnter = Math.Max(tEnter, t1);
            tExit = Math.Min(tExit, t2);
            if (tEnter > tExit)
            {
                return false;
            }
        }

        return tExit > 0 && tEnter < maxDepth;
    }

    private void ApplyBurst(SimulationWorld world, ShellDetonation detonation)
    {
        if (!ShipsByTargetId.TryGetValue(detonation.TargetId, out var ship)
            || !_shells.TryGetValue(detonation.ShellId, out var shell))
        {
            return;
        }

        // Chemical damage at the burst position; ExplosionSystem additionally handles
        // the fragment cone and the blast against the ship's armor plates.
        _registry.Apply(new DamageEvent
        {
            Channel = DamageChannel.Chemical,
            SourceId = detonation.ShellId,
            TargetId = ship.TargetId,
            Position = detonation.Position,
            Amount = 300.0 * Math.Cbrt(Math.Max(shell.ExplosiveMassKg, 0.1)),
            Tick = world.TickIndex,
            Time = world.Time,
        });
    }

    private void ApplyOverpressure(SimulationWorld world, OverpressureWave wave)
    {
        foreach (var ship in ShipsByTargetId.Values)
        {
            if (ship.Parts.Values.Any(p => Vec3.Distance(p.Center + ship.WorldPosition, wave.Position) <= wave.RadiusM))
            {
                _registry.Apply(new DamageEvent
                {
                    Channel = DamageChannel.Overpressure,
                    SourceId = wave.ShellId,
                    TargetId = ship.TargetId,
                    Position = wave.Position,
                    Radius = wave.RadiusM,
                    Amount = 200.0 * Math.Cbrt(Math.Max(wave.TntEquivalentKg, 0.1)),
                    Tick = world.TickIndex,
                    Time = world.Time,
                });
            }
        }
    }
}
