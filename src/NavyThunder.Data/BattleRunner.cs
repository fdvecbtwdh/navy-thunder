using System.Text.Json;
using NavyThunder.Core.AntiAir;
using NavyThunder.Core.Armor;
using NavyThunder.Core.Aviation;
using NavyThunder.Core.Aviation;
using NavyThunder.Core.Ballistics;
using NavyThunder.Core.Battle;
using NavyThunder.Core.Damage;
using NavyThunder.Core.Explosions;
using NavyThunder.Core.Fire;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Model;
using NavyThunder.Core.Ships;
using NavyThunder.Core.Torpedoes;
using NavyThunder.Core.World;

namespace NavyThunder.Data;

public sealed record ScenarioShipSpawn
{
    public required string Ship { get; init; }
    public required double X { get; init; }
    public required double Z { get; init; }
    public double HeadingDeg { get; init; }
    public double Throttle { get; init; } = 1.0;

    /// <summary>Set false for static test targets that must not engage aircraft.</summary>
    public bool Aa { get; init; } = true;
}

public sealed record ScenarioAircraftSpawn
{
    public required string Aircraft { get; init; }
    public required double X { get; init; }
    public required double Z { get; init; }
    public double AltitudeM { get; init; } = 500;
    public double SpeedMs { get; init; } = 110;
    public double HeadingDeg { get; init; }
    /// <summary>"torpedoStrike" | "bombStrike"</summary>
    public required string Mission { get; init; }
    public required string TargetTeam { get; init; }
}

public sealed record ScenarioTeam
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required ScenarioShipSpawn[] Ships { get; init; }
    public ScenarioAircraftSpawn[] Aircraft { get; init; } = [];
}

public sealed record BattleScenario
{
    public required string Name { get; init; }
    public double MaxDurationS { get; init; } = 1800;
    public ulong Seed { get; init; } = 0x4E617659_5468756E;
    public required ScenarioTeam[] Teams { get; init; }

    public static BattleScenario Load(string path)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        return JsonSerializer.Deserialize<BattleScenario>(File.ReadAllText(path), options)
               ?? throw new InvalidDataException($"Scenario {path} deserialized to null");
    }
}

/// <summary>
/// Builds and runs a fully wired battle world from a scenario (R0.7/R0.8): navigation,
/// guns, ballistics, explosions, damage bridge, flooding, fire, damage control, kill
/// adjudication, battle adjudication and the simple naval AI. Deterministic per seed.
/// </summary>
public sealed class BattleRunner
{
    public SimulationWorld World { get; }
    public List<Ship> Ships { get; } = [];
    public BattleSystem Battle { get; }
    public DamageRegistry Registry { get; } = new();
    private readonly Dictionary<string, ArmorTarget> _armorByTargetId = new();
    public GunSystem Guns { get; }
    public DamageBridgeSystem Bridge { get; }
    public FireSystem Fire { get; }
    public FloodingSystem Flooding { get; }
    public DamageControlSystem DamageControl { get; }
    public KillAdjudicatorSystem Adjudicator { get; }
    public FlightModelSystem FlightModel { get; }
    public AircraftAdjudicatorSystem AircraftAdjudicator { get; }
    public NavyThunder.Core.Torpedoes.TorpedoSystem Torpedoes { get; }
    public AntiAircraftSystem AntiAir { get; }

    /// <summary>Test/diagnostic accessor for armor targets by ship target id.</summary>
    public NavyThunder.Core.Armor.ArmorTarget? ArmorLookup(string targetId) => _armorByTargetId.GetValueOrDefault(targetId);
    public List<NavyThunder.Core.Aviation.Aircraft> Aircraft { get; } = [];
    public BallisticsSystem Ballistics { get; }

    public BattleRunner(DataRepository repo, BattleScenario scenario)
    {
        World = new SimulationWorld(fixedDeltaTime: 0.02, masterSeed: scenario.Seed);
        Battle = new BattleSystem { Victory = new VictoryConditions { TimeLimitS = scenario.MaxDurationS } };

        var resolver = new ArmorResolver(repo.ToPenetrationCalibration());
        Ballistics = new BallisticsSystem { Armor = resolver, GroundLevelY = -50 };
        var explosions = new ExplosionSystem(repo.ToExplosionModel(), repo.Shells, Registry)
        {
            RecordFragmentImpacts = false, // bounded memory for long battles
        };
        Bridge = new DamageBridgeSystem(Registry, repo.Shells);
        Fire = new FireSystem(repo.ToFireModel(), Registry);
        Bridge.Fire = Fire; // combat damage rolls ignition through the fire system (MDR-0010)
        Flooding = new FloodingSystem(Registry, Fire);
        var navigation = new ShipNavigationSystem();
        Guns = new GunSystem(repo.Shells, Ballistics);
        var ai = new SimpleNavalAISystem(Registry) { Guns = Guns };
        var damageControl = new DamageControlSystem { Fire = Fire, Flooding = Flooding };
        var adjudicator = new KillAdjudicatorSystem(Registry);
        DamageControl = damageControl;
        Adjudicator = adjudicator;
        var torpedoSystem = new NavyThunder.Core.Torpedoes.TorpedoSystem(Registry, Flooding);
        Torpedoes = torpedoSystem;
        var antiAir = new AntiAircraftSystem(repo.Shells) { Ballistics = Ballistics };
        AntiAir = antiAir;
        FlightModel = new FlightModelSystem(Registry) { Ballistics = Ballistics, Torpedoes = torpedoSystem };
        if (repo.Shells.TryGetValue("usn_1000lb_an_m64_bomb", out var bombShell))
        {
            FlightModel.SetBombShell(MissionKind.BombStrike, bombShell);
        }

        torpedoSystem.ArmorFor = id => _armorByTargetId.GetValueOrDefault(id);
        if (repo.Torpedoes.TryGetValue("ijn_610mm_type93_mod1_mod2", out var airTorpedo))
        {
            torpedoSystem.SetAirLaunchTemplate(airTorpedo);
        }
        AircraftAdjudicator = new AircraftAdjudicatorSystem(Registry, repo.Shells);

        foreach (var team in scenario.Teams)
        {
            var teamRecord = new Team { Id = team.Id, Name = team.Name };
            Battle.Teams.Add(teamRecord);
            int shipIndex = 0;
            foreach (var spawn in team.Ships)
            {
                var ship = ShipFactory.Create(repo.Ships[spawn.Ship], $"{team.Id}-{shipIndex++}");
                ship.Team = teamRecord;
                ship.WorldPosition = new Vec3(spawn.X, 0, spawn.Z);
                ship.HeadingDeg = spawn.HeadingDeg;
                ship.ThrottleCommand = spawn.Throttle;
                Ships.Add(ship);
                World.AddEntity(ship);
                Battle.Ships.Add(ship);
                Registry.Register(ship);
                Bridge.ShipsByTargetId[ship.TargetId] = ship;
                Flooding.Ships.Add(ship);
                DamageControl.Ships.Add(ship);
                Adjudicator.Ships.Add(ship);
                navigation.Ships.Add(ship);
                ai.RegisterShip(ship);
                Guns.RegisterShip(ship);

                var armor = ShipFactory.BuildArmorTarget(ship);
                Ballistics.Targets.Add(armor);
                explosions.Targets.Add(armor);
                _armorByTargetId[ship.TargetId] = armor;

                // R1.4 (partial): hull AA battery - one VT barrage mount tracking the ship
                // until it dies; per-turret data-driven mounts remain R1.4 completion.
                // (spawn.Aa == false opts static test targets out of engaging aircraft)
                var aaMount = new AAMount
                {
                    Id = $"{ship.TargetId}/aa",
                    Position = Vec3.Zero, // superseded by the tracking provider
                    PositionProvider = () => ship.WorldPosition,
                    IsActive = () => ship.Alive,
                    ShellId = "usn_127mm_mk31_aa_vt",
                    RangeM = 5000,
                    MuzzleVelocityMs = 792,
                    RoundsPerMinute = 60,
                    HorizontalMrad = 15,
                    VerticalMrad = 12,
                };
                if (spawn.Aa)
                {
                    antiAir.Mounts.Add(aaMount);
                }
            }
        }

        // Aircraft squadrons (R0.5/R1.3): missions target the first enemy team's ships.
        var aircraftSpecs = new List<(NavyThunder.Core.Aviation.Aircraft Aircraft, string Mission, double AltitudeM, string EnemyTeamId)>();
        foreach (var team in scenario.Teams)
        {
            var teamRecord = Battle.Teams.First(t => t.Id == team.Id);
            var enemyTeamId = scenario.Teams.First(t => t.Id != team.Id).Id;
            int squadronIndex = 0;
            foreach (var spawn in team.Aircraft)
            {
                var aircraft = AircraftFactory.Create(repo.Aircraft[spawn.Aircraft], $"{team.Id}-{squadronIndex++}");
                aircraft.Team = teamRecord;
                Aircraft.Add(aircraft);
                World.AddEntity(aircraft);
                Registry.Register(aircraft);
                AircraftAdjudicator.Aircraft.Add(aircraft);
                AircraftAdjudicator.ByTargetId[aircraft.TargetId] = aircraft;

                var armor = AircraftFactory.BuildArmorTarget(aircraft);
                Ballistics.Targets.Add(armor);
                explosions.Targets.Add(armor);
                Ballistics.ProximityTargets.Add(aircraft);

                var targetShip = Ships.First(sh => sh.Team!.Id == enemyTeamId && !sh.Lost);
                var state = FlightModel.Register(aircraft,
                    new Vec3(spawn.X, spawn.AltitudeM, spawn.Z),
                    spawn.HeadingDeg, spawn.SpeedMs, spawn.AltitudeM);
                state.MissionAltitudeM = spawn.AltitudeM;
                state.Mission = BuildStrikeMission(targetShip, spawn.Mission);
                aircraftSpecs.Add((aircraft, spawn.Mission, spawn.AltitudeM, enemyTeamId));
            }
        }

        foreach (var aircraft in Aircraft)
        {
            antiAir.AirTargets.Add(aircraft);
        }

        // R1.2 retargeting: after a completed strike (target destroyed post-release) the
        // aircraft is handed the next live enemy ship until the enemy team is gone.
        FlightModel.MissionFactory = aircraft =>
        {
            var spec = aircraftSpecs.FirstOrDefault(s => s.Aircraft == aircraft);
            if (spec.Aircraft is null)
            {
                return null;
            }

            var target = Ships.FirstOrDefault(sh => sh.Team!.Id == spec.EnemyTeamId && !sh.Lost);
            return target is null ? null : BuildStrikeMission(target, spec.Mission);
        };

        World.AddSystem(Ballistics);
        World.AddSystem(antiAir);
        World.AddSystem(explosions);
        World.AddSystem(Bridge);
        World.AddSystem(Flooding);
        World.AddSystem(Fire);
        World.AddSystem(navigation);
        World.AddSystem(ai);
        World.AddSystem(Guns);
        World.AddSystem(damageControl);
        World.AddSystem(adjudicator);
        World.AddSystem(AircraftAdjudicator);
        World.AddSystem(FlightModel);
        World.AddSystem(Torpedoes);
        World.AddSystem(Battle);
    }

    private StrikeMission BuildStrikeMission(Ship targetShip, string missionKind)
    {
        Vec3 TargetVel() => new(
            Math.Sin(targetShip.HeadingDeg * Math.PI / 180.0) * targetShip.SpeedKnots * 0.514444,
            0,
            Math.Cos(targetShip.HeadingDeg * Math.PI / 180.0) * targetShip.SpeedKnots * 0.514444);
        return new StrikeMission
        {
            Kind = missionKind == "torpedoStrike" ? MissionKind.TorpedoStrike : MissionKind.BombStrike,
            TargetId = targetShip.TargetId,
            TargetPosition = () => targetShip.WorldPosition,
            TargetAlive = () => !targetShip.Lost,
            TargetVelocity = TargetVel,
            TargetArmor = () => _armorByTargetId.GetValueOrDefault(targetShip.TargetId),
        };
    }

    /// <summary>Runs until the battle ends or the safety cap, then returns the report.</summary>
    public Dictionary<string, object?> Run(double safetyCapS = 7200)
    {
        double cap = Math.Min(safetyCapS, Battle.Victory.TimeLimitS + 300);
        while (Battle.Result == BattleResult.Running && World.Time < cap)
        {
            World.Step();
        }

        return BattleReportGenerator.Generate(World, Ships, Battle);
    }
}
