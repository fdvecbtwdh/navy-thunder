using NavyThunder.Core.Damage;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Missiles;
using NavyThunder.Core.World;
using NavyThunder.Data;
using Xunit;

namespace NavyThunder.Core.Tests;

public class MissileGuidanceTests
{
    private static (MissileSystem Missiles, DamageRegistry Registry) Make()
    {
        var registry = new DamageRegistry();
        return (new MissileSystem(registry), registry);
    }

    private static MissileDefinition IrMissile() => new()
    {
        Id = "test_ir_sam",
        DisplayName = "Test IR SAM",
        Guidance = GuidanceKind.Ir,
        MassKg = 90,
        WarheadMassKg = 9,
        BoostAccelerationMs2 = 250,
        BoostTimeS = 3,
        ProximityRadiusM = 12,
        ArmDistanceM = 60,
        MaxRangeM = 8000,
    };

    [Fact]
    public void Ir_Missile_Intercepts_A_Crossing_Target()
    {
        var (missiles, registry) = Make();
        var world = new SimulationWorld(fixedDeltaTime: 0.02);

        Vec3 targetPos = new(0, 500, -2000);
        Vec3 targetVel = new(0, 0, 150); // flying +Z across the launcher

        missiles.Launch(IrMissile(), Vec3.Zero, new Vec3(0, 80, 100),
            "aircraft:test_fighter",
            () => targetPos, () => targetVel, () => false);

        world.AddSystem(missiles);
        world.Run(25);

        var hit = Assert.Single(world.Events.Of<MissileHit>());
        Assert.Equal("aircraft:test_fighter", hit.TargetId);
        Assert.NotEmpty(registry.Log.Where(e => e.Channel == DamageChannel.Chemical));
    }

    [Fact]
    public void Flares_Inside_The_Seeker_Fov_Decoy_The_Ir_Missile()
    {
        var (missiles, _) = Make();
        var world = new SimulationWorld(fixedDeltaTime: 0.02);

        Vec3 targetPos = new(0, 500, -2000);
        Vec3 targetVel = new(0, 0, 150);

        var id = missiles.Launch(IrMissile(), Vec3.Zero, new Vec3(0, 80, 100),
            "aircraft:test_fighter",
            () => targetPos, () => targetVel, () => false);

        // Flare burst right after launch, inside the seeker cone near the line of sight.
        world.Step();
        missiles.Dispense(new Vec3(0, 450, -1900), new Vec3(0, 0, 0), radarDecoy: false, world.Time);

        world.Run(30);

        Assert.Empty(world.Events.Of<MissileHit>().Where(h => h.TargetId == "aircraft:test_fighter"));
    }

    [Fact]
    public void Sarh_Missile_Loses_Guidance_When_Chaff_Breaks_The_Lock()
    {
        var (missiles, registry) = Make();
        var world = new SimulationWorld(fixedDeltaTime: 0.02);

        bool lockBroken = false;
        var definition = IrMissile() with
        {
            Id = "test_sarh",
            Guidance = GuidanceKind.Sarh,
            MaxRangeM = 6000,
        };

        Vec3 targetPos = new(0, 500, -1500);
        missiles.Launch(definition, Vec3.Zero, new Vec3(0, 80, 120),
            "aircraft:test_fighter",
            () => targetPos, () => new Vec3(0, 0, 150), () => lockBroken);

        world.AddSystem(missiles);
        world.Run(3);
        lockBroken = true; // chaff Bloom: the illuminator no longer sees the target through it
        world.Run(30);

        // Without guidance the missile coasts past and self-destructs on range/ground.
        Assert.Empty(world.Events.Of<MissileHit>().Where(h => h.TargetId == "aircraft:test_fighter"));
    }

    [Fact]
    public void Proximity_Fuse_Respects_The_Arming_Distance()
    {
        var (missiles, _) = Make();
        var world = new SimulationWorld(fixedDeltaTime: 0.02);

        Vec3 targetPos = new(0, 0, -40); // inside the arming distance (60 m)
        missiles.Launch(IrMissile(), Vec3.Zero, new Vec3(0, 0, 100),
            "aircraft:test_fighter",
            () => targetPos, () => Vec3.Zero, () => false);

        world.AddSystem(missiles);
        world.Run(3);

        // Not armed yet: it flies THROUGH the target without bursting.
        Assert.Empty(world.Events.Of<MissileHit>());
    }
}
