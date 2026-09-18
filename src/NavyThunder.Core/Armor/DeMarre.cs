using NavyThunder.Core.Model;

namespace NavyThunder.Core.Armor;

/// <summary>
/// The official War Thunder de Marre implementation, transcribed from the wiki
/// calculator's embedded JavaScript (wiki.warthunder.com/jacob_de_marre, source
/// verified 2026-09-19; see MDR-0001):
///
///   pen_mm = 100 * v^1.43 * m^0.71 / (1900^1.43 * (d_mm/100)^1.07) * K_shell [* knap]
///
/// - Reference speed 1900 m/s (APCR family: 3000). Exponents datamined per-shell and
///   identical everywhere (1.43 / 0.71 / 1.07).
/// - knap: the explosive-percentage penalty (official piecewise curve), applied to all
///   shells — it reproduces both the tank anchor (M61) and the naval range tables.
/// </summary>
public static class DeMarre
{
    public const double SpeedPow = 1.43;
    public const double MassPow = 0.71;
    public const double CaliberPow = 1.07;
    public const double ReferenceSpeedMs = 1900.0;

    /// <summary>Official knap curve over explosive mass percentage (0..100 scale input).</summary>
    public static double ExplosivePenaltyFraction(double explosiveMassKg, double shellMassKg)
    {
        double tnt = shellMassKg <= 0 ? 0 : explosiveMassKg / shellMassKg * 100.0;
        return tnt switch
        {
            < 0.65 => 1.0,
            < 1.6 => 1.0 + (tnt - 0.65) * (0.93 - 1.0) / (1.6 - 0.65),
            < 2.0 => 0.93 + (tnt - 1.6) * (0.90 - 0.93) / (2.0 - 1.6),
            < 3.0 => 0.90 + (tnt - 2.0) * (0.85 - 0.90) / (3.0 - 2.0),
            < 4.0 => 0.85 + (tnt - 3.0) * (0.75 - 0.85) / (4.0 - 3.0),
            _ => 0.75,
        };
    }

    public static double PenetrationMm(ShellDefinition shell, double impactSpeedMs)
    {
        double dCm = shell.CaliberMm / 100.0;
        return 100.0
               * Math.Pow(impactSpeedMs, SpeedPow)
               * Math.Pow(shell.MassKg, MassPow)
               / (Math.Pow(ReferenceSpeedMs, SpeedPow) * Math.Pow(dCm, CaliberPow))
               * shell.DemarrePenetrationK
               * ExplosivePenaltyFraction(shell.ExplosiveMassKg, shell.MassKg);
    }
}
