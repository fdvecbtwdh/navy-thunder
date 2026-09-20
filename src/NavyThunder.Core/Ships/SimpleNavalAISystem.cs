using NavyThunder.Core.Armor;
using NavyThunder.Core.Ballistics;
using NavyThunder.Core.Damage;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Model;
using NavyThunder.Core.World;

namespace NavyThunder.Core.Ships;

public enum NavalManeuver
{
    Approach,   // beyond preferred band: close the range
    Broadside,  // in the band: cross the target, present full battery
    OpenUp,     // too close: open the range
    Disengage,  // heavily damaged: kite away from the threat
}

/// <summary>
/// Ship AI (R1.1):
///  - threat-weighted target selection (range, target health, and how much damage the
///    candidate has dealt to us - attributed via shooter-tagged impact events)
///  - maneuver state machine: approach / broadside crossing with deterministic zigzag /
///    open up / disengage when crippled, throttling through the navigation system
///  - fire discipline: guns engage the ordered target only inside range, holding fire
///    when a friendly ship sits inside the firing corridor
/// Deterministic: decisions re-evaluated on a fixed cadence; the zigzag is a time sine
/// with a per-ship phase; no RNG anywhere.
/// </summary>
public sealed class SimpleNavalAISystem : ISimulationSystem
{
    private sealed class ShipMind
    {
        public required Ship Ship;
        public string? TargetId;
        public NavalManeuver Maneuver = NavalManeuver.Approach;
        public double NextReplanS;
        public double ZigzagPhase;
        public readonly Dictionary<string, double> ThreatToUs = new();
    }

    private readonly DamageRegistry _registry;
    private readonly Dictionary<string, ShipMind> _minds = [];
    private readonly Dictionary<string, Ship> _shipsById = [];
    private int _processedEvents;

    public List<Ship> Ships { get; } = [];
    public GunSystem? Guns { get; set; }

    /// <summary>
    /// Preferred engagement band as fractions of the best gun range. Naval gunfire only
    /// converts against a maneuvering hull inside the band where ballistic time of flight
    /// stays short enough to lead her (about a quarter of maximum gun range): a 40 s arc
    /// to a zigzagging target lands hundreds of metres off no matter how good the solution
    /// is, so holding fire at the edge of gun range is wasted ammunition.
    /// </summary>
    public double PreferredRangeMinFraction { get; set; } = 0.25;
    public double PreferredRangeMaxFraction { get; set; } = 0.40;

    /// <summary>Hull fraction below which the ship disengages and kites away.</summary>
    public double DisengageHullFraction { get; set; } = 0.3;

    public string Name => "ship_ai";

    public SimpleNavalAISystem(DamageRegistry registry) => _registry = registry;

    public void Initialize(SimulationWorld world)
    {
    }

    public void Update(SimulationWorld world, double deltaTime)
    {
        AccumulateThreat(world);

        foreach (var mind in _minds.Values)
        {
            var ship = mind.Ship;
            if (ship.Lost)
            {
                continue;
            }

            if (world.Time >= mind.NextReplanS)
            {
                mind.NextReplanS = world.Time + 2.0;
                mind.TargetId = PickTarget(ship, mind);
            }

            var target = ResolveTarget(mind.TargetId);
            if (target is null)
            {
                ship.ThrottleCommand = 0.25; // no contacts: coast
                continue;
            }

            UpdateManeuver(world, ship, mind, target);
            UpdateGuns(ship, target);
        }
    }

    private void AccumulateThreat(SimulationWorld world)
    {
        var events = world.Events.All;
        while (_processedEvents < events.Count)
        {
            if (events[_processedEvents] is ProjectileArmorImpact impact
                && impact.Outcome == PlateResolution.Penetrated
                && _minds.TryGetValue(impact.TargetId, out var victim)
                && impact.ShooterId.Length > 0)
            {
                victim.ThreatToUs[impact.ShooterId] = victim.ThreatToUs.GetValueOrDefault(impact.ShooterId) + 1;
            }

            _processedEvents++;
        }
    }

    private Ship? ResolveTarget(string? targetId)
        => targetId is not null && _shipsById.TryGetValue(targetId, out var ship) && !ship.Lost ? ship : null;

    private string? PickTarget(Ship self, ShipMind mind)
    {
        double bestRange = BestGunRange(self);
        if (bestRange <= 0)
        {
            return null;
        }

        double totalThreat = mind.ThreatToUs.Values.Sum();
        string? best = null;
        double bestScore = double.MaxValue;

        foreach (var candidate in Ships)
        {
            if (candidate.Lost || candidate.Team is null || self.Team is null
                || !self.Team.IsHostileTo(candidate.Team))
            {
                continue;
            }

            double range = Vec3.Distance(self.WorldPosition, candidate.WorldPosition);
            double rangeScore = range / bestRange;
            var vitals = candidate.Parts.Values
                .Where(p => p.Definition.Kind is PartKind.Compartment or PartKind.Magazine)
                .ToList();
            double healthScore = vitals.Count == 0
                ? 1.0
                : vitals.Average(p => p.Hp / Math.Max(1.0, p.Definition.Hp));
            double threatScore = totalThreat > 0
                ? mind.ThreatToUs.GetValueOrDefault(candidate.TargetId) / totalThreat
                : 0.0;

            // Prefer: closer, weaker, and ships that have been hurting us.
            double score = rangeScore + 0.35 * healthScore - 0.5 * threatScore;
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate.TargetId;
            }
        }

        return best;
    }

    private static Vec3 ShipVelocity(Ship ship)
    {
        double rad = ship.HeadingDeg * Math.PI / 180.0;
        double mps = ship.SpeedKnots * 0.514444;
        return new Vec3(Math.Sin(rad) * mps, 0, Math.Cos(rad) * mps);
    }

    private static double BestGunRange(Ship ship) =>
        ship.Definition.Guns.Length == 0 ? 0 : ship.Definition.Guns.Max(g => g.RangeM);

    private void UpdateManeuver(SimulationWorld world, Ship ship, ShipMind mind, Ship target)
    {
        double range = Vec3.Distance(ship.WorldPosition, target.WorldPosition);
        double bestRange = BestGunRange(ship);
        double hullFraction = HullFraction(ship);
        bool inTrouble = ship.BuoyancyLossPct > 8.0
                         || ship.Parts.Values.Any(p => p.Definition.Kind == PartKind.FuelTank && p.Destroyed);

        if (hullFraction < DisengageHullFraction || inTrouble)
        {
            mind.Maneuver = NavalManeuver.Disengage;
        }
        else if (range < PreferredRangeMinFraction * bestRange)
        {
            mind.Maneuver = NavalManeuver.OpenUp;
        }
        else if (range > PreferredRangeMaxFraction * bestRange)
        {
            mind.Maneuver = NavalManeuver.Approach;
        }
        else if (mind.Maneuver is NavalManeuver.Approach or NavalManeuver.Disengage)
        {
            mind.Maneuver = NavalManeuver.Broadside;
        }

        double bearing = BearingDeg(ship.WorldPosition, target.WorldPosition);
        switch (mind.Maneuver)
        {
            case NavalManeuver.Approach:
                ship.ThrottleCommand = 1.0;
                SteerTo(ship, bearing);
                break;

            case NavalManeuver.Broadside:
            {
                // Crossing course with a deterministic zigzag to spoil enemy solutions.
                ship.ThrottleCommand = 0.7;
                double zigzag = Math.Sin(world.Time * 0.06 + mind.ZigzagPhase) * 18.0;
                SteerTo(ship, (bearing + 90 + zigzag) % 360.0);
                break;
            }

            case NavalManeuver.OpenUp:
                ship.ThrottleCommand = 0.8;
                SteerTo(ship, (bearing + 150) % 360.0);
                break;

            case NavalManeuver.Disengage:
                ship.ThrottleCommand = 1.0;
                SteerTo(ship, (bearing + 180) % 360.0);
                break;
        }
    }

    private static double HullFraction(Ship ship)
    {
        var hulls = ship.Parts.Values
            .Where(p => p.Definition.Kind is PartKind.Compartment or PartKind.Engine or PartKind.Boiler)
            .ToList();
        return hulls.Count == 0
            ? 1.0
            : hulls.Average(p => p.Hp / Math.Max(1.0, p.Definition.Hp));
    }

    private static void SteerTo(Ship ship, double desiredBearingDeg)
    {
        double delta = AngleDelta(ship.HeadingDeg, desiredBearingDeg);
        ship.RudderCommand = Math.Clamp(delta / 20.0, -1.0, 1.0);
    }

    private void UpdateGuns(Ship ship, Ship target)
    {
        if (Guns is null)
        {
            return;
        }

        double range = Vec3.Distance(ship.WorldPosition, target.WorldPosition);
        foreach (var gun in ship.Definition.Guns)
        {
            bool inRange = range <= gun.RangeM;
            bool clear = inRange && !FriendlyInLineOfFire(ship, target);
            if (clear)
            {
                Guns.Engage(ship.TargetId, gun.Id, new GunOrder
                {
                    TargetId = target.TargetId,
                    TargetPosition = () => target.WorldPosition,
                    TargetVelocity = () => ShipVelocity(target),
                    TargetLengthM = () => target.Definition.LengthM,
                });
            }
            else
            {
                Guns.CeaseFire(ship.TargetId, gun.Id);
            }
        }
    }

    /// <summary>Hold fire when a friendly ship sits inside the firing corridor.</summary>
    private bool FriendlyInLineOfFire(Ship self, Ship target)
    {
        Vec3 from = self.WorldPosition;
        Vec3 to = target.WorldPosition;
        Vec3 line = to - from;
        double length = line.Length;
        if (length < 1.0)
        {
            return false;
        }

        Vec3 dir = line / length;
        foreach (var friendly in Ships)
        {
            if (ReferenceEquals(friendly, self) || friendly.Lost
                || friendly.Team is null || self.Team is null
                || self.Team.IsHostileTo(friendly.Team))
            {
                continue;
            }

            Vec3 rel = friendly.WorldPosition - from;
            double along = rel.Dot(dir);
            // Only the final stretch blocks fire: firing over distant friendly masts
            // is normal practice, driving through a friend is not.
            if (along < length * 0.75)
            {
                continue;
            }

            if ((rel - dir * along).Length < 80)
            {
                return true;
            }
        }

        return false;
    }

    public void RegisterShip(Ship ship)
    {
        Ships.Add(ship);
        _shipsById[ship.TargetId] = ship;
        var mind = new ShipMind
        {
            Ship = ship,
            ZigzagPhase = (ship.Id % 7) * 0.9,
            NextReplanS = (ship.Id % 5) * 0.4,
        };
        _minds[ship.TargetId] = mind;
    }

    private static double BearingDeg(Vec3 from, Vec3 to)
    {
        Vec3 d = to - from;
        return Math.Atan2(d.X, d.Z) * 180.0 / Math.PI;
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
}
