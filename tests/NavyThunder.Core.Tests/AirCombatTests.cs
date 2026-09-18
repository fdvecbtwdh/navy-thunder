using NavyThunder.Core.Aviation;
using NavyThunder.Core.Armor;
using NavyThunder.Core.Ballistics;
using NavyThunder.Core.Damage;
using NavyThunder.Core.Explosions;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.World;
using NavyThunder.Data;
using Xunit;

namespace NavyThunder.Core.Tests;

/// <summary>
/// Air-combat wiring: guns hit skin plates, the bridge routes kinetic damage to the part
/// behind the plate, HE bursts fragment inside, the Critical-G spar rule and the kill /
/// severe-damage tiers run (MDR-0013).
/// </summary>
public class AirCombatTests
{
    private static (DataRepository Repo, SimulationWorld World, AirRig Rig) Build()
    {
        var repo = DataRepository.LoadFromDirectory(RepoLocator.FindDataDirectory());
        var world = new SimulationWorld(fixedDeltaTime: 0.02);
        var registry = new DamageRegistry();

        var aircraft = AircraftFactory.Create(repo.Aircraft["test_fighter"]);
        world.AddEntity(aircraft);
        registry.Register(aircraft);

        var armor = AircraftFactory.BuildArmorTarget(aircraft);

        var ballistics = new BallisticsSystem { Armor = new ArmorResolver(repo.ToPenetrationCalibration()), GroundLevelY = -500 };
        ballistics.Targets.Add(armor);

        var explosions = new ExplosionSystem(repo.ToExplosionModel(), repo.Shells, registry);
        explosions.Targets.Add(armor);

        var adjudicator = new AircraftAdjudicatorSystem(registry, repo.Shells);
        adjudicator.Aircraft.Add(aircraft);
        adjudicator.ByTargetId[aircraft.TargetId] = aircraft;

        world.AddSystem(ballistics);
        world.AddSystem(explosions);
        world.AddSystem(adjudicator);

        return (repo, world, new AirRig(aircraft, ballistics, registry, repo.Shells));
    }

    private sealed class AirRig(
        Aircraft aircraft,
        BallisticsSystem ballistics,
        DamageRegistry registry,
        IReadOnlyDictionary<string, NavyThunder.Core.Model.ShellDefinition> shells)
    {
        public Aircraft Aircraft { get; } = aircraft;
        private BallisticsSystem Ballistics { get; } = ballistics;
        private IReadOnlyDictionary<string, NavyThunder.Core.Model.ShellDefinition> Shells { get; } = shells;

        public void Shoot(string shellId, Vec3 aimPoint)
        {
            Ballistics.Spawn(new BallisticProjectile
            {
                Position = aimPoint + new Vec3(0, 0, -120),
                Velocity = new Vec3(0, 0, 800),
                MassKg = 0.05,
                Shell = Shells[shellId],
            });
        }

        public DamageRegistry Registry { get; } = registry;
    }

    [Fact]
    public void Pilot_Hit_Is_An_Instant_Kill()
    {
        var (_, world, rig) = Build();
        rig.Shoot("usn_127mm_m2_ap", new Vec3(2.0, 0.05, 0.2));
        world.Run(5);

        Assert.Equal(AircraftState.Destroyed, rig.Aircraft.State);
        Assert.Equal("pilot_killed", rig.Aircraft.LossReason);
        Assert.NotEmpty(world.Events.Of<AircraftLost>());
    }

    [Fact]
    public void Spar_Damage_Lowers_Critical_G_And_Overload_Tears_The_Wing_Off()
    {
        var (_, world, rig) = Build();
        var aircraft = rig.Aircraft;
        var spar = aircraft.Parts["spar_right"];

        double full = aircraft.ComputeFeedback().CriticalG;
        rig.Registry.Apply(new DamageEvent
        {
            Channel = DamageChannel.Kinetic,
            SourceId = "test",
            TargetId = aircraft.TargetId,
            Position = spar.Center,
            Amount = spar.Definition.Hp * 0.6,
        });

        double damaged = aircraft.ComputeFeedback().CriticalG;
        Assert.True(damaged < full, $"damaged spar must lower Critical G ({damaged:0.##} < {full:0.##})");
        Assert.Equal(AircraftState.Airborne, aircraft.State);

        aircraft.CurrentG = 6.0; // below design G, above the damaged spar's own limit (8*(0.45+0.55*0.4)=5.36)
        world.Step();

        Assert.Equal(AircraftState.Destroyed, aircraft.State);
        Assert.Equal("wing_detached", aircraft.LossReason);
    }

    [Fact]
    public void He_Shell_Bursts_On_Skin_And_Fragments_Damage_Structure()
    {
        var (_, world, rig) = Build();
        var aircraft = rig.Aircraft;
        rig.Shoot("usn_20mm_an_m2_hefi", new Vec3(3.0, 0.0, -0.65)); // left wing skin
        world.Run(5);

        Assert.NotEmpty(world.Events.Of<ShellDetonation>());
        Assert.True(aircraft.Parts.Values.Any(p => p.Hp < p.Definition.Hp), "fragments must chew structure");
        Assert.True(aircraft.Alive); // a single 20mm hit is not fatal by itself
    }

    [Fact]
    public void Fuel_Tank_Self_Sealing_Resists_Ignition()
    {
        var (_, world, rig) = Build();
        var aircraft = rig.Aircraft;
        var tank = aircraft.Parts["fuel_fwd"];

        // Multiple .50 AP passes through the self-sealing tank: leaks but does not explode.
        for (int i = 0; i < 4; i++)
        {
            rig.Shoot("usn_127mm_m2_ap", new Vec3(3.2, -0.2, 0.0));
        }

        world.Run(5);
        Assert.True(tank.Hp < tank.Definition.Hp || tank.Destroyed);
        Assert.True(aircraft.Alive, "self-sealing tank hits must not detonate the airframe");
        Assert.NotEqual("fuel_tank_explosion", aircraft.LossReason);
    }

    [Fact]
    public void Engine_And_Radiator_Damage_Write_Back_To_Thrust()
    {
        var (_, world, rig) = Build();
        var aircraft = rig.Aircraft;
        var radiator = aircraft.Parts["radiator"];

        double thrustBefore = aircraft.ComputeFeedback().ThrustFactor;
        Assert.Equal(1.0, thrustBefore, 12);

        rig.Registry.Apply(new DamageEvent
        {
            Channel = DamageChannel.Fragment,
            SourceId = "test",
            TargetId = aircraft.TargetId,
            Position = radiator.Center,
            Amount = radiator.Definition.Hp + 1, // coolant gone -> progressive thrust loss
        });

        var feedback = aircraft.ComputeFeedback();
        Assert.True(radiator.Destroyed);
        Assert.True(feedback.ThrustFactor < 1.0, "coolant loss must throttle thrust");

        rig.Registry.Apply(new DamageEvent
        {
            Channel = DamageChannel.Kinetic,
            SourceId = "test",
            TargetId = aircraft.TargetId,
            Position = aircraft.Parts["engine"].Center,
            Amount = 999,
        });

        Assert.Equal(0.0, aircraft.ComputeFeedback().ThrustFactor, 12);
    }

    [Fact]
    public void Cut_Elevator_Cable_Kills_Pitch_Authority_And_Triggers_Severe_Damage()
    {
        var (_, world, rig) = Build();
        var aircraft = rig.Aircraft;
        var cable = aircraft.Parts["cable_elevator"];
        var elevator = aircraft.Parts["elevator"];

        rig.Registry.Apply(new DamageEvent
        {
            Channel = DamageChannel.Fragment,
            SourceId = "test",
            TargetId = aircraft.TargetId,
            Position = cable.Center,
            Amount = cable.Definition.Hp + 1,
        });
        rig.Registry.Apply(new DamageEvent
        {
            Channel = DamageChannel.Fragment,
            SourceId = "test",
            TargetId = aircraft.TargetId,
            Position = elevator.Center,
            Amount = elevator.Definition.Hp + 1,
        });
        rig.Registry.Apply(new DamageEvent
        {
            Channel = DamageChannel.Fragment,
            SourceId = "test",
            TargetId = aircraft.TargetId,
            Position = aircraft.Parts["aileron_left"].Center,
            Amount = aircraft.Parts["aileron_left"].Definition.Hp + 1,
        });
        rig.Registry.Apply(new DamageEvent
        {
            Channel = DamageChannel.Fragment,
            SourceId = "test",
            TargetId = aircraft.TargetId,
            Position = aircraft.Parts["aileron_right"].Center,
            Amount = aircraft.Parts["aileron_right"].Definition.Hp + 1,
        });

        var feedback = aircraft.ComputeFeedback();
        Assert.Equal(0.0, feedback.PitchAuthority, 12);
        Assert.Equal(0.0, feedback.RollAuthority, 12);

        world.Step(); // adjudicator: all control gone -> severe damage tier
        Assert.Equal(AircraftState.SeverelyDamaged, aircraft.State);
        Assert.True(aircraft.Alive); // severe != finished off (2024-02 rules)
    }
}
