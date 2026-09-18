using NavyThunder.Core.Armor;
using NavyThunder.Core.Ballistics;
using NavyThunder.Core.Geometry;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Ballistics;
using NavyThunder.Core.Model;

namespace NavyThunder.Core.Protection;

/// <summary>
/// Single-plate protection scenario: fire a shell at a plate of given thickness and
/// impact angle, from muzzle (range 0) or from a ballistic range. This is the seed of
/// the Protection Analysis tool — the full-chain version (multi-plate ship targets,
/// fragments, modules, crew) grows around it in Phases 2-3.
/// </summary>
public static class ProtectionScenario
{
    /// <summary>Fixed stream seed for scenario ricochet rolls — deterministic reports.</summary>
    public const ulong ScenarioSeed = 0x50524F54_5EC5;

    public static ArmorHitResult ResolveAtImpact(
        ShellDefinition shell,
        PenetrationCalibration calibration,
        double plateThicknessMm,
        double impactAngleDeg,
        double impactSpeedMs)
    {
        var plate = MakeFrontalPlate(plateThicknessMm);
        var resolver = new ArmorResolver(calibration);
        var rng = new DeterministicRandom(ScenarioSeed);
        return resolver.Resolve(shell, impactSpeedMs, impactAngleDeg, plate, rng);
    }

    /// <summary>
    /// Ricochet probability implied by the calibration band for the given angle
    /// (0 outside the band, 1 at/beyond the full-ricochet angle).
    /// </summary>
    public static double RicochetProbability(PenetrationCalibration calibration, double impactAngleDeg)
    {
        if (impactAngleDeg < calibration.RicochetStartDeg)
        {
            return 0.0;
        }

        double t = (impactAngleDeg - calibration.RicochetStartDeg)
                   / Math.Max(1e-9, calibration.RicochetFullDeg - calibration.RicochetStartDeg);
        return Math.Clamp(t, 0.0, 1.0);
    }

    public static ArmorPlate MakeFrontalPlate(double thicknessMm, string id = "plate")
    {
        // Plate faces -X (toward a shell travelling in +X); tilt is handled through the
        // impact angle parameter, matching stat-card semantics (angle from normal).
        return new ArmorPlate
        {
            Id = id,
            Center = new Vec3(0, 0, 0),
            Normal = new Vec3(-1, 0, 0),
            AxisU = new Vec3(0, 1, 0),
            AxisV = new Vec3(0, 0, 1),
            HalfU = 50,
            HalfV = 50,
            ThicknessMm = thicknessMm,
        };
    }

    internal static QuadraticDrag MakeDrag(ShellDefinition shell) => new(
        (shell.DragCoefficient ?? 0.35) * shell.DragCoefficientScale,
        shell.CaliberMm / 1000.0,
        shell.MassKg);

    /// <summary>Strike velocity and fall angle of a shell at the given range (bisection firing solution).</summary>
    public static TrajectoryResult StrikeAtRange(ShellDefinition shell, double rangeM)
    {
        var drag = MakeDrag(shell);
        return Gunnery.SolveFiringSolution(shell.MuzzleVelocityMs, drag, rangeM);
    }
}
