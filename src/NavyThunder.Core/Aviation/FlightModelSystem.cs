using NavyThunder.Core.Ballistics;
using NavyThunder.Core.Damage;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Torpedoes;
using NavyThunder.Core.World;

namespace NavyThunder.Core.Aviation;

public enum MissionKind
{
    None,
    /// <summary>Fly to the target ship, release a torpedo inside the envelope, egress.</summary>
    TorpedoStrike,
    /// <summary>Fly to the target ship, release bombs on approach, egress.</summary>
    BombStrike,
}

/// <summary>Strike order for one aircraft (R0.5/R1.2 seed).</summary>
public sealed class StrikeMission
{
    public required MissionKind Kind { get; init; }
    public required Func<Vec3> TargetPosition;
    public required Func<bool> TargetAlive;
    public required string TargetId;

    /// <summary>Resolves the live armor target of the struck ship (torpedo contact tests).</summary>
    public Func<NavyThunder.Core.Armor.ArmorTarget?>? TargetArmor { get; init; }

    /// <summary>Target velocity for lead-aimed torpedo release.</summary>
    public Func<Vec3> TargetVelocity { get; init; } = () => Vec3.Zero;

    public bool Released;
}

public sealed class AircraftFlightState
{
    public Vec3 Position;
    public Vec3 Velocity;
    public double HeadingDeg;
    public double AltitudeM;
    public double Throttle { get; set; } = 1.0;
    public double MissionAltitudeM { get; set; } = 500;
    public StrikeMission? Mission { get; set; }
    public bool Egrees => Mission?.Released ?? false;
}

public sealed record AircraftCrashed : SimulationEvent
{
    public string AircraftId { get; init; } = "";
    public Vec3 Position { get; init; }
    public override string Kind => "aircraft_crashed";
}

/// <summary>
/// Simplified flight dynamics (R1.3) consuming the damage model's FlightFeedback:
/// throttle×thrust accelerates along the heading, drag bleeds speed, rudder/aileron
/// authority gates turn rate, lift loss makes the aircraft sink, destroyed controls or
/// a dead pilot put it into an uncontrollable descent until it strikes the surface.
/// Strike missions (R0.5) fly the aircraft to its target and release torpedoes/bombs.
/// </summary>
public sealed class FlightModelSystem : ISimulationSystem
{
    private readonly DamageRegistry _registry;
    private readonly Dictionary<Aircraft, AircraftFlightState> _states = [];

    /// <summary>Water/surface level; descending through it crashes the aircraft.</summary>
    public double SurfaceLevelY { get; init; } = 0.0;

    public BallisticsSystem Ballistics { get; init; } = null!;
    public TorpedoSystem Torpedoes { get; init; } = null!;
    public IReadOnlyDictionary<string, Aircraft> AircraftByTargetId => _byTargetId;
    private readonly Dictionary<string, Aircraft> _byTargetId = [];

    /// <summary>Torpedo release envelope (approximation; WT: too fast/high drowns the fish).</summary>
    public double TorpedoMaxReleaseAltitudeM { get; init; } = 50;
    public double TorpedoMaxReleaseSpeedMs { get; init; } = 70;

    public string Name => "flight_model";

    public FlightModelSystem(DamageRegistry registry)
    {
        _registry = registry;
    }

    public void Initialize(SimulationWorld world)
    {
    }

    public AircraftFlightState Register(Aircraft aircraft, Vec3 position, double headingDeg, double speedMs, double altitudeM)
    {
        _byTargetId[aircraft.TargetId] = aircraft;
        double rad = headingDeg * Math.PI / 180.0;
        var state = new AircraftFlightState
        {
            Position = position,
            HeadingDeg = headingDeg,
            AltitudeM = altitudeM,
            Velocity = new Vec3(Math.Sin(rad) * speedMs, 0, Math.Cos(rad) * speedMs),
        };
        _states[aircraft] = state;
        aircraft.WorldPosition = position;
        aircraft.Velocity = state.Velocity;
        return state;
    }

    public AircraftFlightState? StateOf(Aircraft aircraft) => _states.GetValueOrDefault(aircraft);

    public void Update(SimulationWorld world, double deltaTime)
    {
        foreach (var (aircraft, state) in _states)
        {
            if (!aircraft.Alive)
            {
                continue;
            }

            var feedback = aircraft.ComputeFeedback();

            // Uncontrollable: dead pilot / torn wing / total control loss = descending spin.
            bool uncontrollable = aircraft.State == AircraftState.Destroyed
                                  || (feedback.RollAuthority <= 0 && feedback.PitchAuthority <= 0);

            // Turn toward the mission heading.
            double desiredHeading = state.HeadingDeg;
            if (state.Mission is { } mission && !uncontrollable)
            {
                Vec3 aim = mission.Released
                    ? EgressPoint(state)
                    : mission.TargetPosition();
                desiredHeading = Math.Atan2(aim.X - state.Position.X, aim.Z - state.Position.Z) * 180.0 / Math.PI;
            }

            double authority = uncontrollable ? 0.25 : Math.Max(0.15, Math.Min(feedback.RollAuthority, feedback.YawAuthority));
            double maxTurn = 14.0 * authority * deltaTime;
            double delta = AngleDelta(state.HeadingDeg, desiredHeading);
            state.HeadingDeg = (state.HeadingDeg + Math.Clamp(delta, -maxTurn, maxTurn) + 360.0) % 360.0;

            // Speed: thrust against drag. A torpedo approach slows to the release envelope
            // (too fast/high and the fish drowns - MDR-0013 drop rules).
            double drag = 0.06 + feedback.DragAdd;
            double torpedoApproachCap = state.Mission?.Kind == MissionKind.TorpedoStrike && !state.Mission.Released
                ? TorpedoMaxReleaseSpeedMs / 120.0
                : 1.0;
            double targetSpeed = 120.0 * Math.Clamp(state.Throttle, 0.2, 1.0) * torpedoApproachCap * feedback.ThrustFactor;
            state.Velocity *= Math.Clamp(1.0 - drag * deltaTime, 0.0, 1.0);
            double speed = state.Velocity.Length;
            double rad = state.HeadingDeg * Math.PI / 180.0;
            Vec3 forward = new(Math.Sin(rad), 0, Math.Cos(rad));
            state.Velocity += forward * (Math.Max(0, targetSpeed - speed) * 0.25 * feedback.ThrustFactor * deltaTime);

            // Load factor from turning (feeds the Critical-G spar rule).
            double turnRateDegS = Math.Abs(Math.Clamp(delta, -maxTurn, maxTurn)) / Math.Max(1e-6, deltaTime);
            aircraft.CurrentG = 1.0 + (speed * turnRateDegS * Math.PI / 180.0) / 9.80665;

            // Altitude: climb/descend toward mission altitude; lift loss and
            // uncontrollability sink the airframe.
            double liftLoss = Math.Clamp(1.0 - feedback.LiftMultiplier, 0.0, 1.0);
            double sink = liftLoss * 40.0 + (uncontrollable ? 60.0 : 0.0);
            double climb = uncontrollable ? 0.0 : Math.Clamp(state.MissionAltitudeM - state.AltitudeM, -20.0, 10.0);
            state.AltitudeM += (climb - sink) * deltaTime;
            state.AltitudeM = Math.Max(0, state.AltitudeM);

            // Integrate.
            state.Position += state.Velocity * deltaTime;
            state.Position = state.Position with { Y = state.AltitudeM };
            aircraft.WorldPosition = state.Position;
            aircraft.Velocity = state.Velocity;

            // Surface strike.
            if (state.AltitudeM <= SurfaceLevelY + 0.01)
            {
                aircraft.Kill(uncontrollable ? "crashed_uncontrollable" : "crashed", world.Time);
                world.Record(new AircraftCrashed
                {
                    AircraftId = aircraft.TargetId,
                    Position = state.Position,
                });
                _registry.Apply(new DamageEvent
                {
                    Channel = DamageChannel.Kinetic,
                    SourceId = "crash",
                    TargetId = aircraft.TargetId,
                    Position = state.Position,
                    Amount = 9999,
                    Tick = world.TickIndex,
                    Time = world.Time,
                });
                continue;
            }

            // Weapon release (R0.5).
            if (state.Mission is { } m && !m.Released && m.TargetAlive())
            {
                double range = Vec3.Distance(state.Position, m.TargetPosition());
                if (range <= ReleaseRange(m.Kind))
                {
                    Vec3 los = m.TargetPosition() - state.Position;
                    double losDeg = Math.Atan2(los.X, los.Z) * 180.0 / Math.PI;
                    double alignError = Math.Abs(AngleDelta(state.HeadingDeg, losDeg));

                    // Torpedoes run straight: only release on a merged attack run
                    // (aligned with the LOS); bombs are ballistic and release regardless.
                    bool aligned = m.Kind != MissionKind.TorpedoStrike || alignError < 5.0;
                    if (aligned)
                    {
                        Release(world, aircraft, state, m);
                    }
                }
            }

            // Egress ends the attack run.
            if (state.Mission is { Released: true } done && !done.TargetAlive())
            {
                state.Mission = null;
            }
        }
    }

    private static double ReleaseRange(MissionKind kind) => kind switch
    {
        MissionKind.TorpedoStrike => 800,
        MissionKind.BombStrike => 900,
        _ => 0,
    };

    private Vec3 EgressPoint(AircraftFlightState state)
    {
        // Turn away: reciprocal of current heading.
        double rad = (state.HeadingDeg + 180.0) * Math.PI / 180.0;
        return state.Position + new Vec3(Math.Sin(rad) * 5000, 0, Math.Cos(rad) * 5000);
    }

    private void Release(SimulationWorld world, Aircraft aircraft, AircraftFlightState state, StrikeMission mission)
    {
        mission.Released = true;

        if (mission.Kind == MissionKind.TorpedoStrike)
        {
            bool envelope = state.AltitudeM <= TorpedoMaxReleaseAltitudeM
                            && state.Velocity.Length <= TorpedoMaxReleaseSpeedMs;
            if (envelope)
            {
                Vec3 entry = state.Position with { Y = 0.5 };
                Torpedoes.SpawnAirLaunched(entry, mission.TargetPosition(), mission.TargetVelocity(),
                    mission.TargetId, mission.TargetArmor?.Invoke());
            }
            // Outside the envelope the fish drowns: the attack run is simply wasted.
            return;
        }

        // Bomb: ballistic body with the aircraft's velocity at release; the shared
        // explosion pipeline handles impact, fragments and overpressure.
        if (Ballistics is not null && _bombShells.TryGetValue(mission.Kind, out var bomb) )
        {
            Ballistics.Spawn(new BallisticProjectile
            {
                Position = state.Position with { Y = state.AltitudeM - 1 },
                Velocity = state.Velocity * 1.0,
                MassKg = bomb.MassKg,
                Shell = bomb,
                LastTargetId = mission.TargetId,
            });
        }
    }

    private readonly Dictionary<MissionKind, Model.ShellDefinition> _bombShells = [];

    /// <summary>Registers the bomb shell used for BombStrike releases (from data).</summary>
    public void SetBombShell(MissionKind kind, Model.ShellDefinition shell) => _bombShells[kind] = shell;

    private static double AngleDelta(double fromDeg, double toDeg)
    {
        double d = (toDeg - fromDeg) % 360.0;
        if (d > 180)
        {
            d -= 360;
        }

        if (d < -180)
        {
            d += 360;
        }

        return d;
    }
}
