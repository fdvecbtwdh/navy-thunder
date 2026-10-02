using NavyThunder.Core.Armor;
using NavyThunder.Core.Ballistics;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.World;
using Xunit;

namespace NavyThunder.Core.Tests;

/// <summary>
/// World-level integration of projectile flight, plate hits, fuze arming and detonation.
/// Shots start at y=100 so horizontal trajectories stay above ground; drop over the short
/// test ranges is negligible (a few cm).
/// </summary>
public class BallisticsArmorIntegrationTests
{
    private static (SimulationWorld World, BallisticsSystem System) MakeWorld(params ArmorTarget[] targets)
    {
        var world = new SimulationWorld(fixedDeltaTime: 0.02);
        var system = new BallisticsSystem
        {
            Armor = new ArmorResolver(TestShells.Calibration),
        };
        foreach (var t in targets)
        {
            system.Targets.Add(t);
        }

        world.AddSystem(system);
        return (world, system);
    }

    private static ArmorTarget PlateWall(string id, double x, double y, double thicknessMm)
    {
        return new ArmorTarget { Id = id }
            .Add(new NavyThunder.Core.Geometry.ArmorPlate
            {
                Id = $"{id}_belt",
                Center = new Vec3(x, y, 0),
                Normal = new Vec3(-1, 0, 0),
                AxisU = new Vec3(0, 1, 0),
                AxisV = new Vec3(0, 0, 1),
                HalfU = 25,
                HalfV = 25,
                ThicknessMm = thicknessMm,
            });
    }

    /// <summary>Plate tilted so a +X trajectory meets it at 80° from the normal (deck-graze geometry).</summary>
    private static ArmorTarget ObliqueDeck(string id, double x, double y, double thicknessMm)
    {
        double rad = 10 * Math.PI / 180;
        return new ArmorTarget { Id = id }
            .Add(new NavyThunder.Core.Geometry.ArmorPlate
            {
                Id = $"{id}_deck",
                Center = new Vec3(x, y, 0),
                Normal = new Vec3(-Math.Sin(rad), Math.Cos(rad), 0),
                AxisU = new Vec3(Math.Cos(rad), Math.Sin(rad), 0),
                AxisV = new Vec3(0, 0, 1),
                HalfU = 25,
                HalfV = 25,
                ThicknessMm = thicknessMm,
            });
    }

    private static BallisticProjectile HorizontalShot(NavyThunder.Core.Model.ShellDefinition shell, double y = 100)
        => new()
        {
            Position = new Vec3(0, y, 0),
            Velocity = new Vec3(shell.MuzzleVelocityMs, 0, 0),
            MassKg = shell.MassKg,
            Shell = shell,
        };

    [Fact]
    public void Shell_Penetrates_Multi_Plate_Layout_And_Detonates_After_Fuse_Delay()
    {
        var (world, system) = MakeWorld(PlateWall("outer", 100, 100, 100), PlateWall("inner", 102, 100, 100));
        system.Spawn(HorizontalShot(TestShells.Mk8));

        world.Run(10);

        var impacts = world.Events.Of<ProjectileArmorImpact>().ToList();
        Assert.Equal(2, impacts.Count);
        Assert.All(impacts, e => Assert.Equal(PlateResolution.Penetrated, e.Outcome));
        Assert.True(impacts[0].FuzeTriggered);

        var detonation = Assert.Single(world.Events.Of<ShellDetonation>());
        Assert.True(detonation.AfterPenetration);
        Assert.False(detonation.OnWaterSurface);
        // Detonation lies behind the fuze-delay travel (0.035 s * ~762 m/s ≈ 26 m).
        Assert.True(detonation.Position.X > 102);
        Assert.True(detonation.Position.X < 140);
    }

    [Fact]
    public void Shell_Stopped_By_Thick_Plate_Detonates_At_The_Plate()
    {
        // 127mm Mk32 SAP (~660mm pen at calibration constant 1.0) vs an 800mm citadel belt.
        var (world, system) = MakeWorld(PlateWall("citadel", 100, 100, 800));
        system.Spawn(HorizontalShot(TestShells.Mk32Sap));

        world.Run(10);

        var impact = Assert.Single(world.Events.Of<ProjectileArmorImpact>());
        Assert.Equal(PlateResolution.Stopped, impact.Outcome);
        Assert.True(impact.FuzeTriggered);

        var detonation = Assert.Single(world.Events.Of<ShellDetonation>());
        Assert.False(detonation.AfterPenetration);
        Assert.InRange(detonation.Position.X, 95, 101);
        Assert.Empty(world.Events.Of<ProjectileGroundImpact>());
    }

    [Fact]
    public void Thin_Plate_Below_Fuze_Threshold_Passes_Trigger_Free_Then_Water_Detonates()
    {
        // Mk8 threshold is 38mm: a 20mm plate is pierced without arming the fuze.
        var (world, system) = MakeWorld(PlateWall("superstructure", 100, 100, 20));
        system.Spawn(HorizontalShot(TestShells.Mk8));

        world.Run(30);

        var impact = Assert.Single(world.Events.Of<ProjectileArmorImpact>());
        Assert.Equal(PlateResolution.Penetrated, impact.Outcome);
        Assert.False(impact.FuzeTriggered);

        var detonation = Assert.Single(world.Events.Of<ShellDetonation>());
        Assert.True(detonation.OnWaterSurface); // fuseOnWater = true
        Assert.True(detonation.Position.X > 100);
    }

    [Fact]
    public void Ricochet_Deflects_The_Projectile_Without_Detonating_On_The_Plate()
    {
        // 127mm SAP vs a 200mm deck plate met at 80°: certain ricochet; the shell skips
        // off and only detonates later at the water surface.
        var (world, system) = MakeWorld(ObliqueDeck("deck", 100, 100, 200));
        system.Spawn(HorizontalShot(TestShells.Mk32Sap));

        world.Run(70);

        var impact = Assert.Single(world.Events.Of<ProjectileArmorImpact>());
        Assert.Equal(PlateResolution.Ricocheted, impact.Outcome);
        Assert.False(impact.FuzeTriggered);

        var detonation = Assert.Single(world.Events.Of<ShellDetonation>());
        Assert.True(detonation.OnWaterSurface);
    }

    [Fact]
    public void Bare_Ballistics_Without_Shell_Still_Records_Ground_Impact()
    {
        var (world, system) = MakeWorld(PlateWall("belt", 100, 100, 200));
        system.Spawn(new BallisticProjectile
        {
            Position = new Vec3(0, 100, 0),
            Velocity = new Vec3(300, 0, 0),
            MassKg = 25,
        });

        world.Run(30);

        Assert.Empty(world.Events.Of<ProjectileArmorImpact>());
        Assert.Single(world.Events.Of<ProjectileGroundImpact>());
    }
}
