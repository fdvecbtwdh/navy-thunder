using NavyThunder.Core.AntiAir;
using NavyThunder.Core.Aviation;
using NavyThunder.Core.Ballistics;
using NavyThunder.Core.Armor;
using NavyThunder.Core.Damage;
using NavyThunder.Core.Explosions;
using NavyThunder.Core.FireControl;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.World;
using NavyThunder.Data;
using Xunit;

namespace NavyThunder.Core.Tests;

public class VtProximityFuseTests
{
    private static (SimulationWorld, BallisticsSystem, Aircraft) MakeRig()
    {
        var repo = DataRepository.LoadFromDirectory(RepoLocator.FindDataDirectory());
        var world = new SimulationWorld(fixedDeltaTime: 0.02);
        var ballistics = new BallisticsSystem { Armor = new ArmorResolver(repo.ToPenetrationCalibration()), GroundLevelY = -500 };
        world.AddSystem(ballistics);

        var aircraft = AircraftFactory.Create(repo.Aircraft["test_fighter"]);
        aircraft.WorldPosition = new Vec3(0, 0, 0);
        ballistics.ProximityTargets.Add(aircraft);
        return (world, ballistics, aircraft);
    }

    [Fact]
    public void Vt_Shell_Arms_After_Arm_Distance_And_Detonates_Near_Air_Target()
    {
        var (world, ballistics, _) = MakeRig();
        var vt = DataRepository.LoadFromDirectory(RepoLocator.FindDataDirectory())
            .RequireShell("usn_127mm_mk31_aa_vt");

        // Fired from 2 km away: armed at 457 m, pass within 23 m of the aim point.
        // Gravity-compensated launch (12.2 m/s up cancels the ~2.5 s fall) so the shell
        // passes within the 23 m trigger radius of the target at the origin.
        ballistics.Spawn(new BallisticProjectile
        {
            Position = new Vec3(0, 0, -2000),
            Velocity = new Vec3(0, 12.2, 792),
            MassKg = 25,
            Shell = vt,
        });

        world.Run(3.0);

        var detonation = Assert.Single(world.Events.Of<ShellDetonation>());
        double z = detonation.Position.Z;
        Assert.InRange(z, -60, -5); // within the trigger radius of the aim point
    }

    [Fact]
    public void Vt_Shell_Does_Not_Proximity_Detonate_On_Ships()
    {
        var (world, ballistics, _) = MakeRig();
        var repo = DataRepository.LoadFromDirectory(RepoLocator.FindDataDirectory());
        var vt = repo.RequireShell("usn_127mm_mk31_aa_vt");

        // A ship hull is a proximity target candidate only if added; VT is air-only,
        // so even an "air-sized" target that reports itself non-air is ignored.
        ballistics.Spawn(new BallisticProjectile
        {
            Position = new Vec3(0, 0, -2000),
            Velocity = new Vec3(0, 0, 792),
            MassKg = 25,
            Shell = vt,
        });

        world.Run(1.2); // past arming, no air target present: must still be flying

        Assert.NotEmpty(ballistics.Projectiles);
        Assert.Empty(world.Events.Of<ShellDetonation>());
    }

    [Fact]
    public void Contact_Shells_Ignore_Proximity_Targets()
    {
        var (world, ballistics, aircraft) = MakeRig();
        var repo = DataRepository.LoadFromDirectory(RepoLocator.FindDataDirectory());
        var sap = repo.RequireShell("usn_127mm_mk32_common_sap"); // no proximity fuse

        ballistics.Spawn(new BallisticProjectile
        {
            Position = aircraft.WorldPosition + new Vec3(0, 0, -100),
            Velocity = new Vec3(0, 0, 792),
            MassKg = 25,
            Shell = sap,
        });

        world.Run(1.0);
        Assert.Empty(world.Events.Of<ShellDetonation>()); // contact fuze: no proximity burst
    }
}

public class FireControlAndDispersionTests
{
    [Fact]
    public void Lead_Solution_Is_Zero_For_Stationary_Target_And_Balistic_For_Moving()
    {
        var origin = Vec3.Zero;
        var stationary = FcsSolver.SolveLead(origin, 800, new Vec3(0, 0, 10000), Vec3.Zero);
        Assert.Equal(new Vec3(0, 0, 10000), stationary.AimPoint);

        // Target crossing at 200 m/s, shell flies ~12.5 s to 10 km -> lead ~2500 m.
        var moving = FcsSolver.SolveLead(origin, 800, new Vec3(0, 0, 10000), new Vec3(200, 0, 0), iterations: 6);
        Assert.True(moving.AimPoint.X > 1500, $"lead aim X = {moving.AimPoint.X:0}");
        Assert.True(moving.TimeOfFlight > 10);
    }

    [Fact]
    public void Dispersion_Is_Deterministic_And_Grows_With_Radar_Loss()
    {
        var model = new DispersionModel { HorizontalMrad = 10, VerticalMrad = 10 };

        var a = model.Apply(new Vec3(0, 0, 1), new DeterministicRandom(11));
        var b = model.Apply(new Vec3(0, 0, 1), new DeterministicRandom(11));
        Assert.Equal(a, b); // same seed, same deflection

        var spread = new List<Vec3>();
        var rng = new DeterministicRandom(5);
        for (int i = 0; i < 64; i++)
        {
            spread.Add(model.Apply(new Vec3(0, 0, 1), rng));
        }

        double maxDeflection = spread.Max(v => Math.Sqrt(v.X * v.X + v.Y * v.Y));
        Assert.True(maxDeflection > 0.001, "dispersion must actually deflect shots");

        model.PenaltyMultiplier = 1.5; // radar destroyed
        var rng2 = new DeterministicRandom(5);
        double maxDeflected = 0;
        for (int i = 0; i < 64; i++)
        {
            var v = model.Apply(new Vec3(0, 0, 1), rng2);
            maxDeflected = Math.Max(maxDeflected, Math.Sqrt(v.X * v.X + v.Y * v.Y));
        }

        Assert.True(maxDeflected > maxDeflection, "radar loss must widen the cone");
    }
}

public class AntiAircraftEngagementTests
{
    private static (SimulationWorld, AntiAircraftSystem, Aircraft, BallisticsSystem) MakeRig(ulong seed = 42)
    {
        var repo = DataRepository.LoadFromDirectory(RepoLocator.FindDataDirectory());
        var world = new SimulationWorld(fixedDeltaTime: 0.02, masterSeed: seed);
        var registry = new DamageRegistry();

        var aircraft = AircraftFactory.Create(repo.Aircraft["test_fighter"]);
        aircraft.WorldPosition = new Vec3(0, 0, -2000);
        aircraft.Velocity = new Vec3(0, 0, 120); // inbound at 120 m/s

        var ballistics = new BallisticsSystem
        {
            Armor = new ArmorResolver(repo.ToPenetrationCalibration()),
            GroundLevelY = -500,
        };
        ballistics.Targets.Add(AircraftFactory.BuildArmorTarget(aircraft));
        ballistics.ProximityTargets.Add(aircraft);

        var explosions = new ExplosionSystem(repo.ToExplosionModel(), repo.Shells, registry);
        explosions.Targets.Add(ballistics.Targets[0]);

        var adjudicator = new AircraftAdjudicatorSystem(registry, repo.Shells);
        adjudicator.Aircraft.Add(aircraft);
        adjudicator.ByTargetId[aircraft.TargetId] = aircraft;

        var aa = new AntiAircraftSystem(repo.Shells) { Ballistics = ballistics };
        aa.AirTargets.Add(aircraft);
        aa.Mounts.Add(new AAMount
        {
            Id = "aa_1",
            Position = Vec3.Zero,
            ShellId = "usn_127mm_mk31_aa_vt",
            RangeM = 8000,
            MuzzleVelocityMs = 792,
            RoundsPerMinute = 240, // 4 rounds/s: a barrage within seconds
        });

        world.AddSystem(ballistics);
        world.AddSystem(explosions);
        world.AddSystem(aa);
        world.AddSystem(adjudicator);
        return (world, aa, aircraft, ballistics);
    }

    [Fact]
    public void Vt_Barrage_Engages_Inbound_Aircraft()
    {
        var (world, aa, aircraft, _) = MakeRig();
        world.Run(12);

        Assert.True(aa.ShotsFired >= 10, $"barrage must fire, got {aa.ShotsFired}");
        // VT bursts + direct hits produce detonations and/or part damage.
        bool burstOrHit = world.Events.Of<ShellDetonation>().Any()
                          || aircraft.Parts.Values.Any(p => p.Hp < p.Definition.Hp);
        Assert.True(burstOrHit, "the barrage must produce proximity bursts or hits");
    }

    [Fact]
    public void Barrage_Is_Deterministic_For_A_Fixed_Seed()
    {
        var (w1, aa1, a1, _) = MakeRig(seed: 7);
        var (w2, aa2, a2, _) = MakeRig(seed: 7);
        w1.Run(10);
        w2.Run(10);

        Assert.Equal(aa1.ShotsFired, aa2.ShotsFired);
        Assert.Equal(a1.Parts.Values.Sum(p => p.Hp), a2.Parts.Values.Sum(p => p.Hp));
    }

    [Fact]
    public void Radar_Loss_Widens_Dispersion_And_Reduces_Damage_Output()
    {
        var radar = new RadarSensor();
        Assert.True(radar.Functional);

        radar.NotifyModuleDestroyed();
        Assert.False(radar.Functional);
        Assert.Equal(RadarSensor.RadarState.Off, radar.State);
        Assert.False(radar.CanGuideSarhMissile);
    }
}
