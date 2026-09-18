using NavyThunder.Core.Armor;
using NavyThunder.Core.Geometry;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Model;
using Xunit;

namespace NavyThunder.Core.Tests;

public static class TestShells
{
    public static readonly PenetrationCalibration Calibration = new();

    public static readonly ShellDefinition Mk8 = new()
    {
        Id = "usn_406mm_mk8_mod6_apcbc",
        DisplayName = "406mm Mk8 Mod 6 APCBC",
        Category = ShellCategory.APCBC,
        CaliberMm = 406,
        MassKg = 1225,
        MuzzleVelocityMs = 762,
        ExplosiveMassKg = 18.55,
        FuseDelayS = 0.035,
        ExplodeThresholdMm = 38,
        DemarrePenetrationK = 1.0,
        DragCoefficient = 1.02746,
        DragCoefficientScale = 0.302, // fitted to the official range table (MDR-0002)
    };

    public static readonly ShellDefinition Mk32Sap = new()
    {
        Id = "usn_127mm_mk32_common_sap",
        DisplayName = "127mm Mk32 Common (SAP)",
        Category = ShellCategory.SAP,
        CaliberMm = 127,
        MassKg = 24.49,
        MuzzleVelocityMs = 792,
        ExplosiveMassKg = 1.17,
        FuseDelayS = 0.01,
        ExplodeThresholdMm = 6,
        DemarrePenetrationK = 0.87,
    };

    public static ShellDefinition Mk46 => Mk8 with
    {
        Id = "usn_127mm_mk46_special_common",
        DisplayName = "127mm Mk46 Special Common",
        Category = ShellCategory.SpecialCommon,
        CaliberMm = 127,
        MassKg = 25,
        MuzzleVelocityMs = 792,
        FuseDelayS = 0.01,
        ExplodeThresholdMm = 6,
        DemarrePenetrationK = 1.0,
    };

    public static readonly ShellDefinition Mk13He = new()
    {
        Id = "usn_406mm_mk13_hc",
        DisplayName = "406mm Mk13 HC",
        Category = ShellCategory.HE,
        CaliberMm = 406,
        MassKg = 862,
        MuzzleVelocityMs = 803,
        ExplosiveMassKg = 69.67,
        FuseDelayS = 0.001,
        ExplodeThresholdMm = 0.1,
        DemarrePenetrationK = 0.18,
        DragCoefficient = 1.02223,
        DragCoefficientScale = 0.27,
    };

    public static ArmorPlate Plate(double thicknessMm, string id = "plate") => new()
    {
        Id = id,
        Center = Vec3.Zero,
        Normal = new Vec3(-1, 0, 0),
        AxisU = new Vec3(0, 1, 0),
        AxisV = new Vec3(0, 0, 1),
        HalfU = 50,
        HalfV = 50,
        ThicknessMm = thicknessMm,
    };
}

public class DeMarreTests
{
    [Fact]
    public void Penetration_Scales_With_Velocity_To_The_Datamined_Exponent()
    {
        double pen100 = DeMarre.PenetrationMm(TestShells.Mk8, 100);
        double pen200 = DeMarre.PenetrationMm(TestShells.Mk8, 200);

        Assert.Equal(Math.Pow(2, DeMarre.SpeedPow), pen200 / pen100, 12);
    }

    [Fact]
    public void Penetration_Scales_Linearly_With_Shell_K()
    {
        double withK = DeMarre.PenetrationMm(TestShells.Mk32Sap, 762);
        double withoutK = DeMarre.PenetrationMm(TestShells.Mk32Sap with { DemarrePenetrationK = 1.0 }, 762);

        Assert.Equal(0.87, withK / withoutK, 12);
    }
}

public class ArmorResolverTests
{
    private static ArmorHitResult Resolve(ShellDefinition shell, double thicknessMm, double angleDeg, ulong seed = 7)
    {
        var resolver = new ArmorResolver(TestShells.Calibration);
        return resolver.Resolve(shell, shell.MuzzleVelocityMs, angleDeg, TestShells.Plate(thicknessMm), new DeterministicRandom(seed));
    }

    [Fact]
    public void Overmatch_Ignores_Angle_Completely()
    {
        // 406mm vs 50mm = 8.1:1 >= 7:1 — even inside the ricochet band the shell must
        // neither ricochet nor gain LoS thickness (WT wiki normalization rules).
        var result = Resolve(TestShells.Mk8, 50, 80);

        Assert.Equal(PlateResolution.Penetrated, result.Outcome);
        Assert.True(result.OvermatchApplied);
        Assert.Equal(50, result.EffectiveThicknessMm, 3);
    }

    [Fact]
    public void Ricochet_Certain_Beyond_Full_Angle_Never_Below_Start_Angle()
    {
        var beyond = Resolve(TestShells.Mk32Sap, 200, 80);
        Assert.Equal(PlateResolution.Ricocheted, beyond.Outcome);
        Assert.False(beyond.FuzeTriggered); // fuseOnRicochet = false

        var shallow = Resolve(TestShells.Mk32Sap, 200, 30);
        Assert.NotEqual(PlateResolution.Ricocheted, shallow.Outcome);
    }

    [Fact]
    public void Ricochet_Roll_Is_Deterministic_For_A_Fixed_Seed()
    {
        var a = Resolve(TestShells.Mk32Sap, 200, 70, seed: 42);
        var b = Resolve(TestShells.Mk32Sap, 200, 70, seed: 42);

        Assert.Equal(a.Outcome, b.Outcome);
    }

    [Fact]
    public void Normalization_Reduces_Effective_Thickness_At_Angle()
    {
        // 406 vs 300mm: ratio 1.35 inside the 0.5-2.5 normalization window.
        var result = Resolve(TestShells.Mk8, 300, 30);
        double los = 300 / Math.Cos(30 * Math.PI / 180);
        double expectedMult = TestShells.Calibration.NormalizationMultiplier(ShellCategory.APCBC, 406.0 / 300);
        double expected = los * expectedMult;

        Assert.Equal(expected, result.EffectiveThicknessMm, 6);
        Assert.True(result.EffectiveThicknessMm < los);
        Assert.False(result.OvermatchApplied);
    }

    [Fact]
    public void Fuze_Triggers_On_Thick_Plates_Only()
    {
        // Mk8 explodeThreshold = 38mm.
        Assert.True(Resolve(TestShells.Mk8, 50, 0).FuzeTriggered);
        Assert.False(Resolve(TestShells.Mk8, 20, 0).FuzeTriggered);
    }

    [Fact]
    public void Residual_Energy_Is_Positive_On_Penetration_Zero_On_Stop()
    {
        var through = Resolve(TestShells.Mk8, 50, 0);
        Assert.True(through.ResidualEnergyFraction > 0);

        var stopped = Resolve(TestShells.Mk32Sap, 800, 0);
        Assert.Equal(PlateResolution.Stopped, stopped.Outcome);
        Assert.Equal(0, stopped.ResidualEnergyFraction);
    }
}

public class ArmorPlateTests
{
    [Fact]
    public void Ray_Intersects_Front_Face_With_Facing_Normal()
    {
        var plate = TestShells.Plate(100);
        var hit = plate.IntersectRay(new Vec3(-10, 0, 0), new Vec3(1, 0, 0), out var ray);
        Assert.True(hit);
        Assert.Equal(10, ray.Distance, 9);
        Assert.Equal(new Vec3(-1, 0, 0), ray.Normal);
    }

    [Fact]
    public void Ray_Missing_In_Plane_Bounds_Does_Not_Hit()
    {
        var plate = TestShells.Plate(100);
        bool hit = plate.IntersectRay(new Vec3(-10, 100, 0), new Vec3(1, 0, 0), out _);
        Assert.False(hit);
    }

    [Fact]
    public void Slab_Path_Length_Uses_Obliquity()
    {
        var plate = TestShells.Plate(100);
        double vertical = plate.SlabPathLength(new Vec3(1, 0, 0));
        double at60 = plate.SlabPathLength(new Vec3(Math.Cos(Math.PI / 3), Math.Sin(Math.PI / 3), 0));

        Assert.Equal(0.1, vertical, 9);      // 100mm in meters
        Assert.Equal(0.2, at60, 9);          // /cos(60°)
    }
}
