using NavyThunder.Core.Battle;
using NavyThunder.Core.FireControl;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Ships;
using NavyThunder.Core.World;

namespace NavyThunder.Core.Ships;

/// <summary>
/// Minimal ship AI (R0 seed, R1.1 expands): engage the nearest hostile ship with every
/// operational gun, close range when outside gun range, steer toward the broadside
/// bearing once in range so the full battery bears.
/// </summary>
public sealed class SimpleNavalAISystem : ISimulationSystem
{
    public List<Ship> Ships { get; } = [];
    public GunSystem? Guns { get; set; }

    public string Name => "ship_ai";

    public void Initialize(SimulationWorld world)
    {
    }

    public void Update(SimulationWorld world, double deltaTime)
    {
        foreach (var ship in Ships)
        {
            if (ship.Lost)
            {
                continue;
            }

            var target = PickTarget(ship);
            if (target is null)
            {
                continue;
            }

            Vec3 targetPos = target.WorldPosition;
            var targetVel = Vec3.Zero;
            double range = Vec3.Distance(ship.WorldPosition, targetPos);

            // Hold fire outside gun envelope; engage everything inside it.
            double bestRange = BestGunRange(ship);
            if (range > bestRange)
            {
                ship.ThrottleCommand = 1.0;
                SteerToward(ship, targetPos);
            }
            else
            {
                // Broadside bearing: present the widest angle to the target.
                ship.ThrottleCommand = 0.5;
                SteerToBroadside(ship, targetPos);
            }

            if (Guns is not null && range <= bestRange * 1.05)
            {
                foreach (var gun in ship.Definition.Guns)
                {
                    Guns.Engage(ship.TargetId, gun.Id, new GunOrder
                    {
                        TargetId = target.TargetId,
                        TargetPosition = () => target.WorldPosition,
                        TargetVelocity = () => targetVel,
                    });
                }
            }
        }
    }

    private static double BestGunRange(Ship ship) =>
        ship.Definition.Guns.Length == 0 ? 0 : ship.Definition.Guns.Max(g => g.RangeM);

    private Ship? PickTarget(Ship self)
    {
        Ship? best = null;
        double bestRange = double.MaxValue;
        foreach (var candidate in Ships)
        {
            if (candidate.Lost || candidate.Team is null || self.Team is null
                || !self.Team.IsHostileTo(candidate.Team))
            {
                continue;
            }

            double d = Vec3.Distance(self.WorldPosition, candidate.WorldPosition);
            if (d < bestRange)
            {
                bestRange = d;
                best = candidate;
            }
        }

        return best;
    }

    private static void SteerToward(Ship ship, Vec3 point)
    {
        double bearing = BearingDeg(ship.WorldPosition, point);
        double delta = AngleDelta(ship.HeadingDeg, bearing);
        ship.RudderCommand = Math.Clamp(delta / 20.0, -1.0, 1.0);
    }

    private static void SteerToBroadside(Ship ship, Vec3 point)
    {
        double bearing = BearingDeg(ship.WorldPosition, point);
        double broadside = (bearing + 90) % 360;
        double delta = AngleDelta(ship.HeadingDeg, broadside);
        ship.RudderCommand = Math.Clamp(delta / 25.0, -1.0, 1.0);
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
