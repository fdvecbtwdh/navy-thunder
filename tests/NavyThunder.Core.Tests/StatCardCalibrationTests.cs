using NavyThunder.Core.Armor;
using NavyThunder.Core.Ballistics;
using NavyThunder.Core.Protection;
using Xunit;

namespace NavyThunder.Core.Tests;

/// <summary>
/// Calibration gates against the official War Thunder stat-card range table for the
/// 406mm/50 Mk 7 (Iowa wiki page, Gaijin-generated data; captured 2026-09-19).
/// 0° columns gate the full chain (de Marre + knap + drag-integrated strike velocity);
/// 30°/60° columns gate the angle model's upper bound.
/// </summary>
public class StatCardCalibrationTests
{
    private static double PenAtRange(NavyThunder.Core.Model.ShellDefinition shell, double rangeM)
    {
        var strike = ProtectionScenario.StrikeAtRange(shell, rangeM);
        return DeMarre.PenetrationMm(shell, strike.ImpactSpeed);
    }

    [Theory]
    [InlineData(1000, 857)]
    [InlineData(2500, 821)]
    [InlineData(5000, 765)]
    [InlineData(7500, 714)]
    [InlineData(10000, 666)]
    [InlineData(15000, 578)]
    public void Mk8_Zero_Deg_Column_Matches_Official_Range_Table(double rangeM, double expectedMm)
    {
        double pen = PenAtRange(TestShells.Mk8, rangeM);
        Assert.InRange(pen, expectedMm * 0.97, expectedMm * 1.03);
    }

    [Theory]
    [InlineData(1000, 146)]
    [InlineData(5000, 79)]
    [InlineData(10000, 40)]
    public void Mk46_Zero_Deg_Column_Matches_Official_Range_Table(double rangeM, double expectedMm)
    {
        double pen = PenAtRange(TestShells.Mk46, rangeM);
        Assert.InRange(pen, expectedMm * 0.93, expectedMm * 1.07);
    }

    [Theory]
    [InlineData(1000, 106)]
    [InlineData(5000, 90)]
    public void Mk13_He_Zero_Deg_Column_Matches_Official_Range_Table(double rangeM, double expectedMm)
    {
        double pen = PenAtRange(TestShells.Mk13He, rangeM);
        Assert.InRange(pen, expectedMm * 0.88, expectedMm * 1.12); // HE tables noisier; Phase 7 refines
    }

    [Fact]
    public void Knap_Curve_Matches_The_Official_Piecewise_Definition()
    {
        Assert.Equal(1.0, DeMarre.ExplosivePenaltyFraction(0.5, 100), 12);   // 0.5 % < 0.65
        Assert.Equal(0.75, DeMarre.ExplosivePenaltyFraction(5, 100), 12);    // 5 % >= 4
        double mid = DeMarre.ExplosivePenaltyFraction(1.125, 100);           // midpoint 0.65..1.6
        Assert.Equal((1.0 + 0.93) / 2, mid, 12);
    }

    [Fact]
    public void Mk8_Muzzle_Penetration_Matches_Official_Calculator_Value()
    {
        // Official calculator semantics: base 942.2 * K=1.0 * knap(1.514 % -> 0.9363) = 882.2.
        double pen = DeMarre.PenetrationMm(TestShells.Mk8, 762);
        Assert.InRange(pen, 882 * 0.99, 882 * 1.01);
    }
}

/// <summary>
/// Angle-model gates from the stat card's 30°/60° columns: the listed plate thickness is
/// penetrable at that angle, and materially thicker plates are not. WT publishes the
/// normalization mechanism but not the multiplier table, so these are bounds (MDR-0004).
/// </summary>
public class AngleModelCalibrationTests
{
    private static readonly PenetrationCalibration Calibration = new();

    private static ArmorHitResult ResolveAt(double plateMm, double angleDeg, double impactSpeed)
    {
        var resolver = new ArmorResolver(Calibration);
        return resolver.Resolve(
            TestShells.Mk8, impactSpeed, angleDeg,
            ProtectionScenario.MakeFrontalPlate(plateMm),
            new NavyThunder.Core.Mathematics.DeterministicRandom(ProtectionScenario.ScenarioSeed));
    }

    [Fact]
    public void Stat_Card_30deg_Plate_Is_Penetrable_At_The_1000m_Strike_Speed()
    {
        double strike = ProtectionScenario.StrikeAtRange(TestShells.Mk8, 1000).ImpactSpeed;
        Assert.Equal(PlateResolution.Penetrated, ResolveAt(657, 30, strike).Outcome);
    }

    [Fact]
    public void Stat_Card_60deg_Plate_Is_Penetrable_At_The_1000m_Strike_Speed()
    {
        double strike = ProtectionScenario.StrikeAtRange(TestShells.Mk8, 1000).ImpactSpeed;
        Assert.Equal(PlateResolution.Penetrated, ResolveAt(300, 60, strike).Outcome);
    }

    [Fact]
    public void Materially_Thicker_Angled_Plates_Reject_The_1000m_Shot()
    {
        double strike = ProtectionScenario.StrikeAtRange(TestShells.Mk8, 1000).ImpactSpeed;
        Assert.Equal(PlateResolution.Stopped, ResolveAt(657 * 1.5, 30, strike).Outcome);
        Assert.Equal(PlateResolution.Stopped, ResolveAt(300 * 2.0, 60, strike).Outcome);
    }
}
