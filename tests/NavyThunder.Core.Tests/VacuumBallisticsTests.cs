using NavyThunder.Core.Ballistics;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace NavyThunder.Core.Tests;

public class VacuumBallisticsTests(ITestOutputHelper output)
{
    private const double G = 9.80665;

    private static (SimulationWorld World, ProjectileGroundImpact? Impact) FireVacuumShot(double speed, double angleDeg, double durationS)
    {
        var world = new SimulationWorld(fixedDeltaTime: 0.02);
        var ballistics = new BallisticsSystem { DragModel = new VacuumDrag(), IntegrationSubsteps = 4 };
        world.AddSystem(ballistics);

        double rad = angleDeg * Math.PI / 180.0;
        ballistics.Spawn(new BallisticProjectile
        {
            Position = new Vec3(0, 0, 0),
            Velocity = new Vec3(speed * Math.Cos(rad), speed * Math.Sin(rad), 0),
            MassKg = 1225,
        });

        world.Run(durationS);
        return (world, world.Events.Of<ProjectileGroundImpact>().SingleOrDefault());
    }

    [Fact]
    public void Vacuum_45deg_Range_Matches_Analytic_Within_One_Percent()
    {
        const double v = 800.0;
        double expectedRange = v * v / G;

        var (_, impact) = FireVacuumShot(v, 45, 200);

        Assert.NotNull(impact);
        output.WriteLine($"range={impact!.Position.X:F1}m expected={expectedRange:F1}m");
        Assert.Equal(expectedRange, impact.Position.X, expectedRange * 0.01);
    }

    [Fact]
    public void Vacuum_Flight_Time_Matches_Analytic()
    {
        const double v = 800.0;
        double expectedTime = 2 * v * Math.Sin(Math.PI / 4) / G;

        var (_, impact) = FireVacuumShot(v, 45, 200);

        Assert.NotNull(impact);
        Assert.Equal(expectedTime, impact!.AgeSeconds, expectedTime * 0.01);
    }

    [Fact]
    public void Vacuum_Impact_Speed_Equals_Muzzle_Speed()
    {
        var (_, impact) = FireVacuumShot(762, 30, 200);

        Assert.NotNull(impact);
        Assert.Equal(762, impact!.ImpactSpeed, 762 * 0.005);
    }

    [Fact]
    public void Vertical_Shot_Returns_To_Launch_Point()
    {
        var (_, impact) = FireVacuumShot(300, 90, 120);

        Assert.NotNull(impact);
        Assert.Equal(0, impact!.Position.X, 1e-6);
        Assert.Equal(300, impact.ImpactSpeed, 1.5);
        Assert.Equal(2 * 300 / G, impact.AgeSeconds, 0.61); // 1% of ~61.2 s
    }

    [Fact]
    public void Identical_Shots_Are_Bitwise_Deterministic()
    {
        var a = FireVacuumShot(762, 22.5, 120);
        var b = FireVacuumShot(762, 22.5, 120);

        Assert.NotNull(a.Impact);
        Assert.NotNull(b.Impact);
        Assert.Equal(a.Impact!.Position, b.Impact!.Position);
        Assert.Equal(a.Impact.Velocity, b.Impact.Velocity);
        Assert.Equal(a.Impact.AgeSeconds, b.Impact.AgeSeconds);
    }

    [Fact]
    public void QuadraticDrag_Slows_The_Projectile()
    {
        // 127mm Mk34-like shot with its datamined Cx: drag must reduce impact speed below muzzle speed.
        var world = new SimulationWorld(fixedDeltaTime: 0.02);
        var ballistics = new BallisticsSystem
        {
            DragModel = new QuadraticDrag(dragCoefficient: 0.35192, caliberM: 0.127, projectileMassKg: 25),
            IntegrationSubsteps = 4,
        };
        world.AddSystem(ballistics);
        ballistics.Spawn(new BallisticProjectile
        {
            Position = new Vec3(0, 100, 0),
            Velocity = new Vec3(792, 0, 0),
            MassKg = 25,
        });

        world.Run(60);
        var impact = world.Events.Of<ProjectileGroundImpact>().SingleOrDefault();

        Assert.NotNull(impact);
        Assert.True(impact!.Position.X > 1000, "with drag the shell still flies >1 km");
        Assert.True(impact.ImpactSpeed < 792, "drag must bleed speed");
        // Free fall from 100 m takes ~4.52 s; drag couples axes but must not change that much.
        Assert.InRange(impact.AgeSeconds, 4.2, 6.0);
    }
}
