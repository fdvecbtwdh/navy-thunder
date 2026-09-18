using NavyThunder.Core.Armor;
using NavyThunder.Core.Explosions;
using NavyThunder.Core.Fire;
using NavyThunder.Core.Model;

namespace NavyThunder.Data;

/// <summary>
/// Binds calibration/reference JSON entries to engine parameter objects. Every value
/// pulled here is either a WT reference fact (wtReference) or a documented approximation
/// (calibration, approximation:true) — the binding keeps engine defaults and JSON in one
/// place so Phase 7 recalibration only touches data files.
/// </summary>
public static class CalibrationBinding
{
    public static PenetrationCalibration ToPenetrationCalibration(this DataRepository repo)
    {
        double constant = repo.WtReferences.TryGetValue("de_marre_constant_calibrated", out var c)
            ? c.Value
            : repo.Calibration.TryGetValue("de_marre_constant", out var approx)
                ? approx.Value
                : 1.0;

        return new PenetrationCalibration
        {
            DeMarreConstant = constant,
            OvermatchRatio = repo.WtReferences.GetValueOrDefault("overmatch_thickness_ratio")?.Value ?? 7.0,
            RicochetStartDeg = repo.Calibration.GetValueOrDefault("ricochet_angle_start_deg")?.Value ?? 65.0,
            RicochetFullDeg = repo.Calibration.GetValueOrDefault("ricochet_angle_full_deg")?.Value ?? 75.0,
        };
    }

    public static ExplosionModel ToExplosionModel(this DataRepository repo)
    {
        double? Cal(string id) => repo.Calibration.TryGetValue(id, out var e) ? e.Value : null;

        double apheThreshold = repo.WtReferences.TryGetValue("overpressure_aphe_tnt_threshold_kg", out var t)
            ? t.Value
            : 0.17;

        return new ExplosionModel
        {
            BlastRefRadiusM = Cal("blast_ref_radius_m") ?? 4.0,
            BlastRefDamage = Cal("blast_ref_damage") ?? 220.0,
            PunchMmPerCbrtTnt = Cal("punch_mm_per_cbrt_tnt") ?? 30.0,
            FragmentsPerKgTnt = Cal("fragments_per_kg_tnt") ?? 20.0,
            FragmentSpeedMs = Cal("fragment_speed_ms") ?? 1200.0,
            FragmentMaxRangeM = Cal("fragment_max_range_m") ?? 60.0,
            FragmentPenMmPerCbrtTnt = Cal("fragment_pen_mm_per_cbrt_tnt") ?? 8.0,
            FragmentConeHalfAngleDeg = Cal("fragment_cone_half_angle_deg") ?? 37.5,
            OverpressureRadiusMPerCbrtTnt = Cal("overpressure_radius_m_per_cbrt_tnt") ?? 2.0,
            OverpressureApheTntThresholdKg = apheThreshold,
        };
    }

    public static FireModel ToFireModel(this DataRepository repo)
    {
        double? Cal(string id) => repo.Calibration.TryGetValue(id, out var e) ? e.Value : null;

        return new FireModel
        {
            IgnitionBaseProbability = Cal("fire_ignition_base_probability") ?? 0.05,
            CompartmentBurnDps = Cal("fire_compartment_burn_dps") ?? 15.0,
        };
    }
}
