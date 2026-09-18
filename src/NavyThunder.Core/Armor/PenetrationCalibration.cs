using NavyThunder.Core.Model;

namespace NavyThunder.Core.Armor;

/// <summary>
/// Tunable armor-resolution parameters. Publicly confirmed WT values (overmatch 7:1,
/// ricochet ~70°, normalization ratio window 0.5–2.5, de Marre exponents) are defaults
/// here; everything not officially published (normalization multipliers, de Marre
/// constant) is an approximation owned by MDR-0001/MDR-0004 and calibrated against
/// stat-card data. Built from the calibration/reference datasets so data stays data.
/// </summary>
public sealed class PenetrationCalibration
{
    public double OvermatchRatio { get; init; } = 7.0;

    public double RicochetStartDeg { get; init; } = 65.0;
    public double RicochetFullDeg { get; init; } = 75.0;

    public double NormalizationRatioStart { get; init; } = 0.5;
    public double NormalizationRatioEnd { get; init; } = 2.5;

    /// <summary>
    /// Normalization thickness multiplier floor per shell category: effective thickness =
    /// LoS * multiplier. 1.0 = no normalization benefit. WT publishes the mechanism and
    /// the ratio window but not the table (MDR-0004) — fit against stat-card angled values.
    /// </summary>
    public IReadOnlyDictionary<ShellCategory, double> NormalizationMultiplierMin { get; init; }
        = new Dictionary<ShellCategory, double>
        {
            [ShellCategory.AP] = 0.70,
            [ShellCategory.APC] = 0.65,
            [ShellCategory.APBC] = 0.75,
            [ShellCategory.APCBC] = 0.65,
            [ShellCategory.SAP] = 0.85,
            [ShellCategory.Common] = 0.85,
            [ShellCategory.SpecialCommon] = 0.80,
            [ShellCategory.HE] = 1.0,
            [ShellCategory.AACommon] = 1.0,
            [ShellCategory.AAVT] = 1.0,
            [ShellCategory.Unknown] = 0.85,
        };

    public double NormalizationMultiplier(ShellCategory category, double caliberToPlateRatio)
    {
        double min = NormalizationMultiplierMin.GetValueOrDefault(category, 0.85);
        if (caliberToPlateRatio <= NormalizationRatioStart)
        {
            return 1.0;
        }

        if (caliberToPlateRatio >= NormalizationRatioEnd)
        {
            return min;
        }

        double t = (caliberToPlateRatio - NormalizationRatioStart)
                   / (NormalizationRatioEnd - NormalizationRatioStart);
        return 1.0 - t * (1.0 - min);
    }

    public static PenetrationCalibration FromDefaults(
        double overmatchRatio = 7.0,
        double ricochetStartDeg = 65.0,
        double ricochetFullDeg = 75.0)
    {
        return new PenetrationCalibration
        {
            OvermatchRatio = overmatchRatio,
            RicochetStartDeg = ricochetStartDeg,
            RicochetFullDeg = ricochetFullDeg,
        };
    }
}
