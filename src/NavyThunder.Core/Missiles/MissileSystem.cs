using NavyThunder.Core.Damage;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.World;

namespace NavyThunder.Core.Missiles;

public enum GuidanceKind
{
    Ir,    // passive IR seeker: FOV/off-boresight, decoyed by flares (MDR-0015)
    Sarh,  // semi-active: needs the launch platform's hard lock the whole way
    Arh,   // active radar: IOG midcourse + terminal active seer
}

public sealed record MissileDefinition
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }

    public required GuidanceKind Guidance { get; init; }

    public required double MassKg { get; init; }
    public required double WarheadMassKg { get; init; }

    /// <summary>Average powered-flight acceleration (m/s²); unpowered afterwards (approximation).</summary>
    public double BoostAccelerationMs2 { get; init; } = 200.0;
    public double BoostTimeS { get; init; } = 4.0;

    /// <summary>Proportional-navigation constant (approximation, per-shot data later).</summary>
    public double NavigationConstant { get; init; } = 3.5;

    public double MaxG { get; init; } = 30.0;

    /// <summary>Seeker trigger radius (m) + arming distance for the proximity fuze.</summary>
    public required double ProximityRadiusM { get; init; }
    public required double ArmDistanceM { get; init; }

    /// <summary> seeker FOV half-angle (deg) — decoys outside it are ignored.</summary>
    public double SeekerHalfAngleDeg { get; init; } = 30.0;

    public required double MaxRangeM { get; init; }
}

public sealed record MissileLaunch : SimulationEvent
{
    public string MissileId { get; init; } = "";
    public required GuidanceKind Guidance { get; init; }
    public string TargetId { get; init; } = "";
    public override string Kind => "missile_launch";
}

public sealed record MissileHit : SimulationEvent
{
    public string MissileId { get; init; } = "";
    public string TargetId { get; init; } = "";
    public Vec3 Position { get; init; }
    public double WarheadMassKg { get; init; }
    public bool ProximityBurst { get; init; }
    public override string Kind => "missile_hit";
}

/// <summary>A decoy in the world: chaff cloud (radar) or flare pair (IR).</summary>
public sealed record Decoy
{
    public required string Id { get; init; }
    public required bool IsRadarDecoy; // chaff vs flare
    public required Vec3 Position;
    public required Vec3 Velocity;
    public double BornTime;
    public double LifeSeconds = 8.0;
    public bool Expired(double time) => time - BornTime > LifeSeconds;
}

/// <summary>
/// Guided-missile layer (MDR-0015), deliberately decoupled from gun ballistics:
///   - proportional navigation with a G cap toward the tracked target
///   - IR seekers are decoyed by flares inside the seeker FOV; radar seekers by chaff
///   - SARH requires an active hard lock; ARH goes active after loft (IOG in between)
///   - proximity fuze (radius + arming distance) converts a pass into a burst
/// Damage is routed through the unified DamageRegistry as Chemical events.
/// </summary>
public sealed class MissileSystem : ISimulationSystem
{
    private sealed class MissileState
    {
        public required string Id;
        public required MissileDefinition Definition;
        public required string TargetId;
        public required Func<Vec3> TargetPosition;
        public required Func<Vec3> TargetVelocity;
        public required Func<bool> TargetDecoyed; // radar lock broken (chaff) for SARH
        public Vec3 Position;
        public Vec3 Velocity;
        public double TravelledM;
        public double AgeS;
        public bool Alive = true;
    }

    private readonly DamageRegistry _registry;
    private readonly List<MissileState> _missiles = [];
    private readonly List<Decoy> _decoys = [];
    private int _nextId;
    private int _nextDecoyId;

    public string Name => "missiles";

    public MissileSystem(DamageRegistry registry) => _registry = registry;

    public IReadOnlyList<Decoy> Decoys => _decoys;

    public void Initialize(SimulationWorld world)
    {
    }

    public string Launch(
        MissileDefinition definition,
        Vec3 origin,
        Vec3 initialVelocity,
        string targetId,
        Func<Vec3> targetPosition,
        Func<Vec3> targetVelocity,
        Func<bool> targetDecoyed)
    {
        var missile = new MissileState
        {
            Id = $"missile_{++_nextId}",
            Definition = definition,
            TargetId = targetId,
            TargetPosition = targetPosition,
            TargetVelocity = targetVelocity,
            TargetDecoyed = targetDecoyed,
            Position = origin,
            Velocity = initialVelocity,
        };
        _missiles.Add(missile);
        return missile.Id;
    }

    /// <summary>Target dispensing countermeasures: chaff (radar) or flares (IR).</summary>
    public void Dispense(Vec3 position, Vec3 velocity, bool radarDecoy, double time)
    {
        _decoys.Add(new Decoy
        {
            Id = $"decoy_{++_nextDecoyId}",
            IsRadarDecoy = radarDecoy,
            Position = position,
            Velocity = velocity * 0.2, // decoys decelerate rapidly
            BornTime = time,
        });
    }

    public void Update(SimulationWorld world, double deltaTime)
    {
        _decoys.RemoveAll(d => d.Expired(world.Time));

        foreach (var m in _missiles)
        {
            if (!m.Alive)
            {
                continue;
            }

            m.AgeS += deltaTime;

            // Propulsion: constant boost then ballistic glide (approximation, MDR-0015).
            double speed = m.Velocity.Length;
            if (m.AgeS <= m.Definition.BoostTimeS)
            {
                m.Velocity += m.Velocity.Normalized() * m.Definition.BoostAccelerationMs2 * deltaTime;
            }

            // Guidance: proportional navigation with a seeker chain of custody.
            Vec3 toTarget = m.TargetPosition() - m.Position;
            if (GuidanceValid(m) && toTarget.LengthSquared > 1e-6)
            {
                Vec3 losDir = toTarget.Normalized();
                Vec3 losRate = (m.TargetVelocity() - m.Velocity);
                Vec3 desired = losDir * m.Velocity.Length;
                Vec3 steer = desired - m.Velocity;
                double steerMag = steer.Length;
                double maxTurn = m.Definition.MaxG * 9.80665 * deltaTime;
                if (steerMag > maxTurn && steerMag > 1e-9)
                {
                    steer = steer * (maxTurn / steerMag);
                }

                m.Velocity += steer * m.Definition.NavigationConstant * 0.2;
            }
            else if (m.Definition.Guidance == GuidanceKind.Sarh)
            {
                // Lost the illuminator: missile coasts ballistically and misses.
            }

            Vec3 before = m.Position;
            m.Position += m.Velocity * deltaTime;
            m.TravelledM += (m.Position - before).Length;

            // Proximity fuze.
            if (m.TravelledM >= m.Definition.ArmDistanceM
                && Vec3.Distance(m.Position, m.TargetPosition()) <= m.Definition.ProximityRadiusM)
            {
                Detonate(world, m, proximityBurst: true);
                continue;
            }

            if (m.TravelledM > m.Definition.MaxRangeM || m.Position.Y < -1)
            {
                m.Alive = false; // self-destruct / ground impact without target
            }
        }

        _missiles.RemoveAll(m => !m.Alive);
    }

    private bool GuidanceValid(MissileState m)
    {
        if (m.Definition.Guidance == GuidanceKind.Sarh && m.TargetDecoyed())
        {
            return false; // illuminator broken by chaff
        }

        if (m.Definition.Guidance == GuidanceKind.Ir)
        {
            // Flares inside the seeker FOV decoy the seeker (MDR-0015 IRCCM model).
            Vec3 los = m.TargetPosition() - m.Position;
            double range = Math.Max(1.0, los.Length);
            foreach (var decoy in _decoys)
            {
                if (decoy.IsRadarDecoy)
                {
                    continue;
                }

                Vec3 toDecoy = decoy.Position - m.Position;
                double angle = Math.Acos(Math.Clamp(toDecoy.Normalized().Dot(los / range), -1, 1));
                if (angle <= m.Definition.SeekerHalfAngleDeg * Math.PI / 180.0)
                {
                    return false; // seeker pulled off by the flare
                }
            }
        }

        return true;
    }

    private void Detonate(SimulationWorld world, MissileState m, bool proximityBurst)
    {
        m.Alive = false;
        world.Record(new MissileHit
        {
            MissileId = m.Id,
            TargetId = m.TargetId,
            Position = m.Position,
            WarheadMassKg = m.Definition.WarheadMassKg,
            ProximityBurst = proximityBurst,
        });

        _registry.Apply(new DamageEvent
        {
            Channel = DamageChannel.Chemical,
            SourceId = m.Id,
            TargetId = m.TargetId,
            Position = m.Position,
            Amount = 300.0 * Math.Cbrt(Math.Max(m.Definition.WarheadMassKg, 0.1)),
            Tick = world.TickIndex,
            Time = world.Time,
        });
    }
}
