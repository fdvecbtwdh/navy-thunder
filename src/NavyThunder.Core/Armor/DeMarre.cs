using NavyThunder.Core.Model;

namespace NavyThunder.Core.Armor;

/// <summary>
/// de Marre penetration as used by War Thunder (2019 penetration rework; datamined
/// exponents are identical on every sampled shell). The absolute constant is not
/// published and comes from calibration (see MDR-0001).
///
///   pen_mm = Constant * K_shell * v^SpeedPow * m^MassPow / d^CaliberPow
///
/// with v in m/s (impact speed), m in kg, d in mm, result in mm of vertical plate.
/// </summary>
public static class DeMarre
{
    public const double SpeedPow = 1.43;   // wt reference: demarreSpeedPow
    public const double MassPow = 0.71;    // wt reference: demarreMassPow
    public const double CaliberPow = 1.07; // wt reference: demarreCaliberPow

    public static double PenetrationMm(double constant, ShellDefinition shell, double impactSpeedMs)
    {
        return constant
               * shell.DemarrePenetrationK
               * Math.Pow(impactSpeedMs, SpeedPow)
               * Math.Pow(shell.MassKg, MassPow)
               / Math.Pow(shell.CaliberMm, CaliberPow);
    }
}
