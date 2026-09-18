using NavyThunder.Core.Mathematics;

namespace NavyThunder.Core.FireControl;

public sealed record FireControlSolution(Vec3 AimPoint, double TimeOfFlight, bool Valid)
{
    public static readonly FireControlSolution Invalid = new(default, 0, false);
}

/// <summary>
/// Fire-control solver and dispersion (MDR-0014): fixed-point lead computation over the
/// ballistic time-of-flight, plus a data-driven elliptical dispersion applied on top of
/// the aim direction. Deterministic via the caller's RNG stream.
/// </summary>
public static class FcsSolver
{
    /// <summary>
    /// Iterative lead solution: aim where the target will be when the shell arrives.
    /// Two to three iterations converge well inside naval gun ranges.
    /// </summary>
    public static FireControlSolution SolveLead(Vec3 origin, double muzzleSpeedMs, Vec3 targetPos, Vec3 targetVel, int iterations = 3)
    {
        Vec3 aim = targetPos;
        double tof = 0;
        for (int i = 0; i < iterations; i++)
        {
            tof = Vec3.Distance(origin, aim) / Math.Max(1.0, muzzleSpeedMs);
            aim = targetPos + targetVel * tof;
        }

        return new FireControlSolution(aim, tof, tof > 0);
    }

    /// <summary>
    /// Rangefinder error model: manual rangefinding reports a biased distance
    /// (Arcade-like FCS is near-exact). Error grows with range (approximation).
    /// </summary>
    public static double RangefinderDistance(double trueRangeM, double fractionalError, DeterministicRandom rng)
    {
        return trueRangeM * (1.0 + fractionalError * (rng.NextDouble() * 2.0 - 1.0));
    }
}

/// <summary>Elliptical dispersion in milliradians around the aim direction (data-driven, MDR-0014).</summary>
public sealed class DispersionModel
{
    public double HorizontalMrad { get; init; } = 2.0;
    public double VerticalMrad { get; init; } = 1.2;

    /// <summary>Extra multiplier when the fire-control/radar suite is knocked out.</summary>
    public double PenaltyMultiplier { get; set; } = 1.0;

    public Vec3 Apply(Vec3 aimDirection, DeterministicRandom rng)
    {
        (Vec3 u, Vec3 v) = Orthonormal(aimDirection.Normalized());
        double h = Gaussian(rng) * HorizontalMrad * PenaltyMultiplier / 1000.0;
        double v2 = Gaussian(rng) * VerticalMrad * PenaltyMultiplier / 1000.0;
        return (aimDirection.Normalized() + u * h + v * v2).Normalized();
    }

    /// <summary>Standard normal via Box-Muller on the provided stream (deterministic).</summary>
    public static double Gaussian(DeterministicRandom rng)
    {
        double u1 = Math.Max(1e-12, rng.NextDouble());
        double u2 = rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    private static (Vec3, Vec3) Orthonormal(Vec3 axis)
    {
        Vec3 helper = Math.Abs(axis.Y) < 0.9 ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0);
        Vec3 u = axis.Cross(helper).Normalized();
        Vec3 v = axis.Cross(u);
        return (u, v);
    }
}

/// <summary>
/// Radar state machine for the missile era and naval fire control (MDR-0014/0015):
/// search, soft track (TWS) and hard lock (STT); loss of the radar module disables it.
/// </summary>
public sealed class RadarSensor
{
    public enum RadarState
    {
        Off,
        Search,
        SoftLock,  // track while scan
        HardLock,  // single target track
    }

    public RadarState State { get; set; } = RadarState.Search;

    /// <summary>False once the radar module is destroyed: no lock, FCS falls back locally.</summary>
    public bool Functional { get; set; } = true;

    public string? TrackedTargetId { get; set; }

    public bool CanGuideSarhMissile => Functional && State == RadarState.HardLock;

    public void NotifyModuleDestroyed()
    {
        Functional = false;
        State = RadarState.Off;
        TrackedTargetId = null;
    }
}
