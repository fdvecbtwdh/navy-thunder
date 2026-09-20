using NavyThunder.Core.Armor;
using NavyThunder.Core.Ballistics;
using NavyThunder.Core.Damage;
using NavyThunder.Core.FireControl;
using NavyThunder.Core.Geometry;
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
    private readonly List<Ship> _trackedHulls = [];
    private readonly Dictionary<string, ArmorTarget> _armorByHull = new();
    private readonly Dictionary<string, Vec3> _spawnByHull = new();

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
        _trackedHulls.Add(ship);
        _spawnByHull[ship.TargetId] = ship.WorldPosition;
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

    /// <summary>Worst (largest) reload remaining across a ship's guns; 0 = all ready (R2.3 HUD).</summary>
    public double ReloadRemainingOf(string shipId)
    {
        double worst = 0;
        foreach (var gun in _guns)
        {
            if (gun.Ship.TargetId == shipId && gun.ReloadRemainingS > worst)
            {
                worst = gun.ReloadRemainingS;
            }
        }

        return worst;
    }

    public void CeaseFire(string shipId, string gunId) => _orders.Remove((shipId, gunId));

    public GunOrder? GetOrder(string shipId, string gunId) =>
        _orders.TryGetValue((shipId, gunId), out var order) ? order : null;

    public void Update(SimulationWorld world, double deltaTime)
    {
        FollowHullArmor();

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

            // Turret traverse limit: fire only when the mount has trained onto the lead
            // point. The lead is iterated against the BALLISTIC time of flight (the arc
            // takes far longer than range/muzzle-speed), so the guns lay for the target's
            // predicted position at impact.
            Vec3 predicted = targetPos;
            Gunnery.GunnerySolution firing = GunneryAt(shell, worldMount, HorizontalDistance(worldMount, predicted));
            double bearingRad = 0.0;
            for (int iter = 0; iter < 4; iter++)
            {
                double tof = firing.Trajectory.TimeOfFlight;
                predicted = targetPos + order.TargetVelocity() * tof;
                double horizontalRange = HorizontalDistance(worldMount, predicted);
                firing = GunneryAt(shell, worldMount, horizontalRange);
                bearingRad = Math.Atan2(predicted.X - worldMount.X, predicted.Z - worldMount.Z);
            }
            double desiredBearingDeg = bearingRad * 180.0 / Math.PI;
            double maxSlew = gun.Definition.TraverseDegPerS * deltaTime;
            gun.TurretHeadingDeg = RotateToward(gun.TurretHeadingDeg, desiredBearingDeg, maxSlew);
            double bearingError = Math.Abs(AngleDelta(gun.TurretHeadingDeg, desiredBearingDeg));

            gun.ReloadRemainingS = Math.Max(0, gun.ReloadRemainingS - deltaTime);
            // The trained-on-target tolerance must shrink with range: a fixed angular gate
            // looses long-range salvos while the mount is still slewing (2 deg at 25 km is
            // a ~900 m aiming error, far wider than any hull). Gate on a fraction of the
            // target silhouette instead.
            double bearingGateDeg = Math.Atan2(FireGateToleranceM, range) * 180.0 / Math.PI;
            if (bearingError > bearingGateDeg || gun.ReloadRemainingS > 0)
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

            // R0.6: distribute salvo aim points along the target's hull silhouette so
            // shells land fore/aft of the locking point instead of stacking on the center.
            // The silhouette's long axis comes from the target's armor envelope: hull
            // geometry in this sim is axis-aligned (plates and turret offsets translate,
            // never rotate), and spreading along the velocity vector would spray the salvo
            // across the narrow beam instead of along the length.
            Vec3 samplePoint = targetPos;
            double hullLength = order.TargetLengthM();
            if (hullLength > 1.0)
            {
                Vec3 targetVel = order.TargetVelocity();
                Vec3 hullAxis = HullLengthAxis(order.TargetId)
                                ?? (targetVel.LengthSquared > 1.0
                                    ? targetVel.Normalized()
                                    : new Vec3(Math.Sin(desiredBearingDeg * Math.PI / 180.0 + Math.PI / 2), 0,
                                               Math.Cos(desiredBearingDeg * Math.PI / 180.0 + Math.PI / 2)));
                double along = (rng.NextDouble() - 0.5) * hullLength;
                samplePoint += hullAxis * along;
            }

            double bearingSampleRad = Math.Atan2(samplePoint.X - worldMount.X, samplePoint.Z - worldMount.Z);
            double horizontalRangeSample = HorizontalDistance(worldMount, samplePoint);
            var sampleFiring = GunneryAt(shell, worldMount, horizontalRangeSample);

            Vec3 dir = new(
                Math.Sin(bearingSampleRad) * Math.Cos(sampleFiring.ElevationRad),
                Math.Sin(sampleFiring.ElevationRad),
                Math.Cos(bearingSampleRad) * Math.Cos(sampleFiring.ElevationRad));

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
                    ShooterId = gun.Ship.TargetId,
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


    /// <summary>
    /// Long axis of the target's armor envelope: the extent axis of her largest plate.
    /// Null when no armor target is registered for the id (caller falls back to the
    /// velocity direction).
    /// </summary>
    private Vec3? HullLengthAxis(string targetId)
    {
        if (!_armorByHull.TryGetValue(targetId, out var armor))
        {
            return null;
        }

        ArmorPlate? largest = null;
        foreach (var plate in armor.Plates)
        {
            if (largest is null
                || Math.Max(plate.HalfU, plate.HalfV) > Math.Max(largest.HalfU, largest.HalfV))
            {
                largest = plate;
            }
        }

        if (largest is null)
        {
            return null;
        }

        return largest.HalfU >= largest.HalfV ? largest.AxisU : largest.AxisV;
    }

    /// <summary>
    /// Integration bridge: hull armor plates must follow their ship as she moves, or hit
    /// detection keeps testing plates frozen at the spawn position while the hull sails
    /// kilometres away (the navigation system's Track hook is not wired for every host).
    /// Resolved lazily because armor targets are built after ship registration. Plate
    /// templates bake the spawn position into BaseCenter, so the live offset is the
    /// displacement from the registered (spawn) position.
    /// </summary>
    private void FollowHullArmor()
    {
        foreach (var ship in _trackedHulls)
        {
            if (!_armorByHull.TryGetValue(ship.TargetId, out var armor))
            {
                armor = _ballistics.Targets.FirstOrDefault(t => t.Id == ship.TargetId);
                if (armor is null)
                {
                    continue; // not built yet; retry on the next tick
                }

                _armorByHull[ship.TargetId] = armor;
            }

            Vec3 displacement = ship.WorldPosition - _spawnByHull.GetValueOrDefault(ship.TargetId);
            foreach (var plate in armor.Plates)
            {
                plate.Translate(displacement);
            }
        }
    }

    /// <summary>Aim-point height above the waterline: mid freeboard, so the descent
    /// straddles the belt band up to the weather deck instead of dropping through the
    /// unarmored waterline plane ahead of/under the hull silhouette.</summary>
    private const double AimHeightAboveWaterlineM = 4.0;

    /// <summary>How far off-train the mount may be at the moment of fire (m at the
    /// target). Keeps long-range salvos from loosing while the turret slews.</summary>
    private const double FireGateToleranceM = 25.0;

    private Gunnery.GunnerySolution GunneryAt(ShellDefinition shell, Vec3 mount, double range)
    {
        int bucket = Math.Max(0, (int)(range / 10));
        int heightCm = (int)(mount.Y * 10);
        if (!_solutionsByMount.TryGetValue((shell.Id, heightCm, bucket), out var solution))
        {
            // The firing solution must integrate the same physics the shells fly with:
            // the live ballistics system's drag model. Solving with a different drag
            // (e.g. a per-shell quadratic model while the world flies vacuum) shifts the
            // impact by kilometres and no shot can ever land on the target.
            // crossingY is the crossing height RELATIVE TO THE MUZZLE (the trajectory
            // origin is the gun), so a world aim height of AimHeightAboveWaterlineM from a
            // mount at world height mount.Y means crossingY = aim - mount.Y.
            solution = Gunnery.SolveFiringSolution(
                shell.MuzzleVelocityMs, _ballistics.DragModel, bucket * 10.0 + 5,
                crossingY: AimHeightAboveWaterlineM - mount.Y);
            _solutionsByMount[(shell.Id, heightCm, bucket)] = solution;
        }

        return solution;
    }

    private readonly Dictionary<(string ShellId, int HeightCm, int RangeBucket), Gunnery.GunnerySolution> _solutionsByMount = new();

    private static double HorizontalDistance(Vec3 a, Vec3 b)
    {
        double dx = a.X - b.X, dz = a.Z - b.Z;
        return Math.Sqrt(dx * dx + dz * dz);
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
