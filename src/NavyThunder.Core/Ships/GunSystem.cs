using NavyThunder.Core.Ballistics;
using NavyThunder.Core.Damage;
using NavyThunder.Core.FireControl;
using NavyThunder.Core.Protection;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Model;
using NavyThunder.Core.World;

namespace NavyThunder.Core.Ships;

public sealed record GunFired : SimulationEvent
{
    public string ShipId { get; init; } = "";
    public string GunId { get; init; } = "";
    public string TargetId { get; init; } = "";
    public int Shells { get; init; }
    public override string Kind => "gun_fired";
}

/// <summary>A gun's current engagement order.</summary>
public sealed record GunOrder
{
    public required Func<Vec3> TargetPosition;
    public required Func<Vec3> TargetVelocity;
    public required string TargetId;

    /// <summary>Target hull length (m) — salvo aim points scatter along the silhouette.</summary>
    public Func<double> TargetLengthM { get; init; } = () => 0.0;
}

/// <summary>
/// Naval gunnery (R0.1): per-gun reload cycle driven by the turret group's ready-rack
/// state, turret traverse with a slew-rate limit, ballistic lead solution, elliptical
/// dispersion, and shell spawns into the shared ballistics pipeline. Turret/hoist
/// destruction and ready-rack depletion degrade the cycle exactly like the damage model
/// prescribes (MDR-0011).
/// </summary>
public sealed class GunSystem : ISimulationSystem
{
    private sealed class GunState
    {
        public required Ship Ship;
        public required NavalGunDefinition Definition;
        public required Vec3 MountPosition; // ship-local
        public double ReloadRemainingS;
        public double TurretHeadingDeg; // relative to ship heading
    }

    private readonly IReadOnlyDictionary<string, ShellDefinition> _shells;
    private readonly BallisticsSystem _ballistics;
    private readonly List<GunState> _guns = [];
    private readonly Dictionary<(string ShipId, string GunId), GunOrder> _orders = [];
    private readonly Dictionary<(string ShellId, int RangeBucket), Gunnery.GunnerySolution> _solutions = [];

    public string Name => "guns";

    public GunSystem(IReadOnlyDictionary<string, ShellDefinition> shells, BallisticsSystem ballistics)
    {
        _shells = shells;
        _ballistics = ballistics;
    }

    public void Initialize(SimulationWorld world)
    {
    }

    /// <summary>Registers all guns of a ship from its data definition.</summary>
    public void RegisterShip(Ship ship)
    {
        foreach (var gun in ship.Definition.Guns)
        {
            // Mount position = centroid of the group's turret parts (falls back to ship center).
            var turret = ship.Parts.Values
                .Where(p => p.Definition.TurretGroup == gun.TurretGroup)
                .ToList();
            Vec3 mount = turret.Count > 0
                ? new Vec3(
                    turret.Average(p => p.Center.X),
                    turret.Average(p => p.Center.Y),
                    turret.Average(p => p.Center.Z))
                : Vec3.Zero;

            _guns.Add(new GunState
            {
                Ship = ship,
                Definition = gun,
                MountPosition = mount,
                ReloadRemainingS = 0,
            });
        }
    }

    public void Engage(string shipId, string gunId, GunOrder order) => _orders[(shipId, gunId)] = order;

    public void CeaseFire(string shipId, string gunId) => _orders.Remove((shipId, gunId));

    public GunOrder? GetOrder(string shipId, string gunId) =>
        _orders.TryGetValue((shipId, gunId), out var order) ? order : null;

    public void Update(SimulationWorld world, double deltaTime)
    {
        foreach (var gun in _guns)
        {
            var order = GetOrder(gun.Ship.TargetId, gun.Definition.Id);
            if (gun.Ship.Lost || order is null)
            {
                gun.ReloadRemainingS = Math.Max(0, gun.ReloadRemainingS - deltaTime);
                continue;
            }

            // Dead turret or destroyed hoists: gun is out (magazine feed still allows
            // other group mounts; the group reload factor covers hoist damage).
            if (gun.Ship.Parts.Values.Any(p =>
                    p.Definition.TurretGroup == gun.Definition.TurretGroup
                    && p.Definition.Kind == PartKind.Turret && p.Destroyed))
            {
                continue;
            }

            if (!_shells.TryGetValue(gun.Definition.ShellId, out var shell))
            {
                continue;
            }

            Vec3 worldMount = gun.Ship.WorldPosition + gun.MountPosition;
            Vec3 targetPos = order.TargetPosition();
            double range = Vec3.Distance(worldMount, targetPos);
            if (range > gun.Definition.RangeM)
            {
                gun.ReloadRemainingS = Math.Max(0, gun.ReloadRemainingS - deltaTime);
                continue;
            }

            // Turret traverse limit: fire only when the mount has trained onto the lead point.
            var solution = FcsSolver.SolveLead(worldMount, shell.MuzzleVelocityMs, targetPos, order.TargetVelocity());
            Vec3 toAim = solution.AimPoint - worldMount;
            double desiredBearingDeg = Math.Atan2(toAim.X, toAim.Z) * 180.0 / Math.PI;
            double maxSlew = gun.Definition.TraverseDegPerS * deltaTime;
            gun.TurretHeadingDeg = RotateToward(gun.TurretHeadingDeg, desiredBearingDeg, maxSlew);
            double bearingError = Math.Abs(AngleDelta(gun.TurretHeadingDeg, desiredBearingDeg));

            gun.ReloadRemainingS = Math.Max(0, gun.ReloadRemainingS - deltaTime);
            if (bearingError > 2.0 || gun.ReloadRemainingS > 0)
            {
                continue;
            }

            // Ready rack: rate degraded when supplying from the main magazine (MDR-0011).
            double reloadFactor = gun.Ship.CurrentReloadFactor(world, gun.Definition.TurretGroup);
            int salvo = Math.Min(gun.Definition.Barrels, Math.Max(1, gun.Ship.ReadyRackCount(gun.Definition.TurretGroup)));
            for (int i = 0; i < salvo; i++)
            {
                gun.Ship.ConsumeReadyRack(world, gun.Definition.TurretGroup);
            }

            // Dispersion + spawn: elevation from the ballistic firing solution (cached per
            // shell and 50 m range bucket), bearing from the lead solution.
            var dispersion = new DispersionModel
            {
                HorizontalMrad = gun.Definition.HorizontalMrad,
                VerticalMrad = gun.Definition.VerticalMrad,
            };
            var rng = world.Rng("guns");

            double horizontalRange = Math.Sqrt(toAim.X * toAim.X + toAim.Z * toAim.Z);
            int bucket = (int)(horizontalRange / 50);
            if (!_solutions.TryGetValue((shell.Id, bucket), out var firing))
            {
                firing = Gunnery.SolveFiringSolution(shell.MuzzleVelocityMs,
                    ProtectionScenario.MakeDrag(shell), bucket * 50.0 + 25);
                _solutions[(shell.Id, bucket)] = firing;
            }

            // R0.6: distribute salvo aim points along the target's hull silhouette so
            // shells land fore/aft of the locking point instead of stacking on the center.
            Vec3 samplePoint = targetPos;
            double hullLength = order.TargetLengthM();
            if (hullLength > 1.0)
            {
                Vec3 targetVel = order.TargetVelocity();
                Vec3 hullAxis = targetVel.LengthSquared > 1.0
                    ? targetVel.Normalized()
                    : new Vec3(Math.Sin(desiredBearingDeg * Math.PI / 180.0 + Math.PI / 2), 0,
                               Math.Cos(desiredBearingDeg * Math.PI / 180.0 + Math.PI / 2));
                double along = (rng.NextDouble() - 0.5) * hullLength;
                samplePoint += hullAxis * along;
            }

            double bearingRad = Math.Atan2(samplePoint.X - worldMount.X, samplePoint.Z - worldMount.Z);
            double horizontalRangeSample = Vec3.Distance(
                new Vec3(worldMount.X, 0, worldMount.Z), new Vec3(samplePoint.X, 0, samplePoint.Z));
            int sampleBucket = (int)(horizontalRangeSample / 50);
            if (!_solutions.TryGetValue((shell.Id, sampleBucket), out var sampleFiring))
            {
                sampleFiring = Gunnery.SolveFiringSolution(shell.MuzzleVelocityMs,
                    ProtectionScenario.MakeDrag(shell), sampleBucket * 50.0 + 25);
                _solutions[(shell.Id, sampleBucket)] = sampleFiring;
            }

            Vec3 dir = new(
                Math.Sin(bearingRad) * Math.Cos(sampleFiring.ElevationRad),
                Math.Sin(sampleFiring.ElevationRad),
                Math.Cos(bearingRad) * Math.Cos(sampleFiring.ElevationRad));

            for (int i = 0; i < salvo; i++)
            {
                Vec3 dispersed = dispersion.Apply(dir, rng);
                _ballistics.Spawn(new BallisticProjectile
                {
                    Position = worldMount,
                    Velocity = dispersed * shell.MuzzleVelocityMs,
                    MassKg = shell.MassKg,
                    Shell = shell,
                    LastTargetId = order.TargetId,
                });
            }

            gun.ReloadRemainingS = 60.0 / Math.Max(0.1, gun.Definition.RoundsPerMinute) * reloadFactor;
            world.Record(new GunFired
            {
                ShipId = gun.Ship.TargetId,
                GunId = gun.Definition.Id,
                TargetId = order.TargetId,
                Shells = salvo,
            });
        }
    }

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

    private static double RotateToward(double currentDeg, double targetDeg, double maxStep)
    {
        double delta = AngleDelta(currentDeg, targetDeg);
        if (Math.Abs(delta) <= maxStep)
        {
            return targetDeg;
        }

        return (currentDeg + Math.Sign(delta) * maxStep + 360.0) % 360.0;
    }
}
