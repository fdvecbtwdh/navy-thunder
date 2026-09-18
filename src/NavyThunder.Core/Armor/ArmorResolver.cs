using NavyThunder.Core.Geometry;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Model;

namespace NavyThunder.Core.Armor;

public enum PlateResolution
{
    Penetrated,
    Stopped,
    Ricocheted,
}

/// <summary>Full outcome of one projectile-vs-plate resolution; feeds events and PA reports.</summary>
public sealed record ArmorHitResult
{
    public required string PlateId { get; init; }
    public required double ImpactSpeedMs { get; init; }
    public required double ImpactAngleDeg { get; init; }
    public required double PlateThicknessMm { get; init; }
    public required double EffectiveThicknessMm { get; init; }
    public required double PenetrationMm { get; init; }
    public required PlateResolution Outcome { get; init; }
    public required bool FuzeTriggered { get; init; }
    public required bool OvermatchApplied { get; init; }

    /// <summary>0..1 share of the penetration capability left after the plate (0 when stopped).</summary>
    public double ResidualEnergyFraction { get; init; }
}

/// <summary>
/// Resolves a single projectile-vs-plate interaction following the War Thunder order of
/// operations (MDR-0004): overmatch check -> ricochet roll -> normalization thickness
/// multiplier -> de Marre penetration comparison. Ricochet rolls consume the "armor"
/// RNG stream so results stay reproducible.
/// </summary>
public sealed class ArmorResolver(PenetrationCalibration calibration)
{
    public PenetrationCalibration Calibration { get; } = calibration;

    public ArmorHitResult Resolve(
        ShellDefinition shell,
        double impactSpeedMs,
        double impactAngleDeg,
        ArmorPlate plate,
        DeterministicRandom rng)
    {
        double ratio = shell.CaliberMm / plate.ThicknessMm;
        bool overmatch = ratio >= Calibration.OvermatchRatio;

        bool ricocheted = false;
        if (!overmatch && impactAngleDeg >= Calibration.RicochetStartDeg)
        {
            double t = (impactAngleDeg - Calibration.RicochetStartDeg)
                       / Math.Max(1e-9, Calibration.RicochetFullDeg - Calibration.RicochetStartDeg);
            double probability = Math.Clamp(t, 0.0, 1.0);
            ricocheted = rng.NextDouble() < probability;
        }

        if (ricocheted)
        {
            return new ArmorHitResult
            {
                PlateId = plate.Id,
                ImpactSpeedMs = impactSpeedMs,
                ImpactAngleDeg = impactAngleDeg,
                PlateThicknessMm = plate.ThicknessMm,
                EffectiveThicknessMm = plate.ThicknessMm,
                PenetrationMm = DeMarre.PenetrationMm(Calibration.DeMarreConstant, shell, impactSpeedMs),
                Outcome = PlateResolution.Ricocheted,
                FuzeTriggered = false, // fuseOnRicochet = false (MDR-0003)
                OvermatchApplied = false,
            };
        }

        double cos = Math.Cos(impactAngleDeg * Math.PI / 180.0);
        double los = plate.ThicknessMm / Math.Max(cos, 1e-6);
        double effective = overmatch
            ? plate.ThicknessMm // angle fully ignored
            : los * Calibration.NormalizationMultiplier(shell.Category, ratio);

        double pen = DeMarre.PenetrationMm(Calibration.DeMarreConstant, shell, impactSpeedMs);
        bool penetrated = pen >= effective;
        bool fuzeTriggered = plate.ThicknessMm >= shell.ExplodeThresholdMm;

        return new ArmorHitResult
        {
            PlateId = plate.Id,
            ImpactSpeedMs = impactSpeedMs,
            ImpactAngleDeg = impactAngleDeg,
            PlateThicknessMm = plate.ThicknessMm,
            EffectiveThicknessMm = effective,
            PenetrationMm = pen,
            Outcome = penetrated ? PlateResolution.Penetrated : PlateResolution.Stopped,
            FuzeTriggered = fuzeTriggered,
            OvermatchApplied = overmatch,
            ResidualEnergyFraction = penetrated ? Math.Clamp((pen - effective) / pen, 0.0, 1.0) : 0.0,
        };
    }
}
