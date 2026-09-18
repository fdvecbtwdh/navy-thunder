using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Model;

namespace NavyThunder.Core.Explosions;

/// <summary>
/// Explosion effect parameters. The WT formula shapes behind these numbers are NOT
/// publicly published (MDR-0005): what is public is the composition (shockwave +
/// brisant + fragment cone 30-45°) and the overpressure rules. Everything numeric here
/// is therefore a documented approximation fitted to community observations and tuned in
/// Phase 7 against recorded behaviors.
/// </summary>
public sealed class ExplosionModel
{
    /// <summary>Blast reference radius (m) for 1 kg TNT eq; radius scales with ∛TNT.</summary>
    public double BlastRefRadiusM { get; init; } = 4.0;

    /// <summary>Blast damage (engine HP) at the reference radius for 1 kg TNT eq.</summary>
    public double BlastRefDamage { get; init; } = 220.0;

    /// <summary>Armor punch (mm) per ∛TNT for the brisant effect at the detonation point.</summary>
    public double PunchMmPerCbrtTnt { get; init; } = 30.0;

    public double FragmentsPerKgTnt { get; init; } = 20.0;

    public double FragmentSpeedMs { get; init; } = 1200.0;

    /// <summary>Fragment cone half-angle (°) — WT wiki: fragments spray in a 30–45° cone.</summary>
    public double FragmentConeHalfAngleDeg { get; init; } = 37.5;

    public double FragmentMaxRangeM { get; init; } = 60.0;

    /// <summary>Fragment armor penetration (mm) per ∛TNT of the burst.</summary>
    public double FragmentPenMmPerCbrtTnt { get; init; } = 8.0;

    /// <summary>Overpressure effect radius (m) per ∛TNT (open-module crew kills, ships).</summary>
    public double OverpressureRadiusMPerCbrtTnt { get; init; } = 2.0;

    /// <summary>APHE bursters below this TNT eq produce no overpressure (official: ~170 g).</summary>
    public double OverpressureApheTntThresholdKg { get; init; } = 0.17;

    /// <summary>Effective TNT-equivalent mass of the burst.</summary>
    public double TntEquivalentKg(ShellDefinition shell) => shell.ExplosiveMassKg;

    public double BlastRadiusM(double tntKg) => BlastRefRadiusM * Math.Cbrt(Math.Max(tntKg, 0));

    /// <summary>Blast damage with inverse-square-style falloff beyond the reference radius.</summary>
    public double BlastDamageAt(double tntKg, double distanceM)
    {
        double cbrt = Math.Cbrt(Math.Max(tntKg, 1e-9));
        double refRadius = BlastRefRadiusM * cbrt;
        double falloff = 1.0 / (1.0 + Math.Pow(distanceM / Math.Max(refRadius, 1e-9), 2));
        return BlastRefDamage * cbrt * falloff;
    }

    /// <summary>Whether this shell's burst generates overpressure per the official rule set.</summary>
    public bool ProducesOverpressure(ShellDefinition shell)
    {
        double tnt = TntEquivalentKg(shell);
        if (shell.Category is ShellCategory.HE or ShellCategory.Common or ShellCategory.AACommon or ShellCategory.AAVT)
        {
            return tnt > 0; // HE family: overpressure-capable by design
        }

        // AP-family bursters need the ~170 g TNT threshold.
        return tnt >= OverpressureApheTntThresholdKg;
    }
}
