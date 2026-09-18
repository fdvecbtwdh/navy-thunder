using NavyThunder.Core.Mathematics;

namespace NavyThunder.Core.Ballistics;

public readonly record struct TrajectoryResult(Vec3 ImpactPosition, Vec3 ImpactVelocity, double TimeOfFlight)
{
    public double ImpactSpeed => ImpactVelocity.Length;

    /// <summary>Depression of the velocity vector below horizontal, in degrees.</summary>
    public double ImpactFallAngleDeg => Math.Asin(Math.Clamp(-ImpactVelocity.Y / Math.Max(ImpactSpeed, 1e-9), -1, 1)) * 180.0 / Math.PI;
}

/// <summary>
/// Deterministic firing-solution helpers: simulate shots with the production RK4
/// integrator and report ground/range crossings. No RNG involved.
/// </summary>
public static class Gunnery
{
    private const double StepH = 0.002;

    /// <summary>
    /// Simulates a shot from the origin toward +X at the given elevation until the
    /// projectile descends back through y = 0 (after having climbed above 1 m), and
    /// reports the interpolated ground crossing.
    /// </summary>
    public static TrajectoryResult SimulateToGround(double muzzleVelocityMs, double elevationRad, IDragModel drag, double maxTimeS = 300)
    {
        var p = MakeShot(muzzleVelocityMs, elevationRad);
        var integrator = new Rk4Integrator { DragModel = drag };

        double t = 0;
        Vec3 prevPos = p.Position;
        bool climbed = false;
        while (t < maxTimeS)
        {
            prevPos = p.Position;
            integrator.Step(ref p, StepH);
            t += StepH;
            if (p.Position.Y > 1)
            {
                climbed = true;
            }

            if (climbed && p.Position.Y <= 0)
            {
                double dy = p.Position.Y - prevPos.Y;
                double f = Math.Abs(dy) < 1e-12 ? 0.0 : prevPos.Y / (prevPos.Y - p.Position.Y);
                Vec3 pos = prevPos + (p.Position - prevPos) * f;
                Vec3 vel = p.Velocity; // step is small; velocity is effectively continuous
                return new TrajectoryResult(pos, vel, t - StepH + StepH * f);
            }
        }

        return new TrajectoryResult(p.Position, p.Velocity, t);
    }

    /// <summary>
    /// Simulates a shot until the projectile crosses the vertical plane at
    /// <paramref name="rangeM"/> (strike-velocity probe at any elevation).
    /// </summary>
    public static TrajectoryResult SimulateToRange(double muzzleVelocityMs, double elevationRad, IDragModel drag, double rangeM, double maxTimeS = 300)
    {
        var p = MakeShot(muzzleVelocityMs, elevationRad);
        var integrator = new Rk4Integrator { DragModel = drag };

        double t = 0;
        Vec3 prevPos = p.Position;
        while (t < maxTimeS)
        {
            prevPos = p.Position;
            integrator.Step(ref p, StepH);
            t += StepH;
            if (p.Position.X >= rangeM)
            {
                double f = Math.Abs(p.Position.X - prevPos.X) < 1e-12
                    ? 0.0
                    : (rangeM - prevPos.X) / (p.Position.X - prevPos.X);
                Vec3 pos = prevPos + (p.Position - prevPos) * f;
                Vec3 vel = p.Velocity;
                return new TrajectoryResult(pos, vel, t - StepH + StepH * f);
            }
        }

        return new TrajectoryResult(p.Position, p.Velocity, t);
    }

    /// <summary>
    /// Bisection firing solution: find the elevation whose ground crossing lands exactly
    /// on <paramref name="rangeM"/>, then report strike speed and fall angle there.
    /// </summary>
    public static TrajectoryResult SolveFiringSolution(double muzzleVelocityMs, IDragModel drag, double rangeM)
    {
        double lo = 0.001, hi = Math.PI / 4; // up to 45°, monotonic for surface fire
        for (int i = 0; i < 42; i++)
        {
            double mid = (lo + hi) / 2;
            var probe = SimulateToGround(muzzleVelocityMs, mid, drag);
            if (probe.ImpactPosition.X < rangeM)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return SimulateToGround(muzzleVelocityMs, (lo + hi) / 2, drag);
    }

    private static BallisticProjectile MakeShot(double muzzleVelocityMs, double elevationRad)
    {
        return new BallisticProjectile
        {
            Position = Vec3.Zero,
            Velocity = new Vec3(muzzleVelocityMs * Math.Cos(elevationRad), muzzleVelocityMs * Math.Sin(elevationRad), 0),
            MassKg = 1,
            Alive = true,
        };
    }
}

/// <summary>Extracted single-body RK4 step so gunnery and the world system share math.</summary>
public sealed class Rk4Integrator
{
    public Vec3 Gravity { get; set; } = new(0, -9.80665, 0);
    public IDragModel DragModel { get; set; } = new VacuumDrag();

    public void Step(ref BallisticProjectile p, double h)
    {
        Vec3 v = p.Velocity;
        Vec3 x = p.Position;

        Vec3 a1 = Gravity + DragModel.Acceleration(p);
        Vec3 k1x = v;

        var mid1 = p;
        mid1.Velocity = v + a1 * (h / 2);
        Vec3 a2 = Gravity + DragModel.Acceleration(mid1);
        Vec3 k2x = v + a1 * (h / 2);

        var mid2 = p;
        mid2.Velocity = v + a2 * (h / 2);
        Vec3 a3 = Gravity + DragModel.Acceleration(mid2);
        Vec3 k3x = v + a2 * (h / 2);

        var end = p;
        end.Velocity = v + a3 * h;
        Vec3 a4 = Gravity + DragModel.Acceleration(end);
        Vec3 k4x = v + a3 * h;

        p.Velocity = v + (a1 + (a2 + a3) * 2.0 + a4) * (h / 6);
        p.Position = x + (k1x + (k2x + k3x) * 2.0 + k4x) * (h / 6);
    }
}
