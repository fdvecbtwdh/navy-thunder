using System.Text.Json;
using NavyThunder.Core.AntiAir;
using NavyThunder.Core.Armor;
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
}

public sealed record ScenarioTeam
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required ScenarioShipSpawn[] Ships { get; init; }
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
    public GunSystem Guns { get; }
    public DamageBridgeSystem Bridge { get; }
    public FireSystem Fire { get; }
    public FloodingSystem Flooding { get; }
    public DamageControlSystem DamageControl { get; }
    public KillAdjudicatorSystem Adjudicator { get; }
    public BallisticsSystem Ballistics { get; }

    public BattleRunner(DataRepository repo, BattleScenario scenario)
    {
        World = new SimulationWorld(fixedDeltaTime: 0.02, masterSeed: scenario.Seed);
        Battle = new BattleSystem { Victory = new VictoryConditions { TimeLimitS = scenario.MaxDurationS } };

        var resolver = new ArmorResolver(repo.ToPenetrationCalibration());
        Ballistics = new BallisticsSystem { Armor = resolver, GroundLevelY = -50 };
        var explosions = new ExplosionSystem(repo.ToExplosionModel(), repo.Shells, Registry);
        Bridge = new DamageBridgeSystem(Registry, repo.Shells);
        Fire = new FireSystem(repo.ToFireModel(), Registry);
        Flooding = new FloodingSystem(Registry, Fire);
        var navigation = new ShipNavigationSystem();
        Guns = new GunSystem(repo.Shells, Ballistics);
        var ai = new SimpleNavalAISystem { Guns = Guns };
        var damageControl = new DamageControlSystem { Fire = Fire, Flooding = Flooding };
        var adjudicator = new KillAdjudicatorSystem(Registry);
        DamageControl = damageControl;
        Adjudicator = adjudicator;

        foreach (var team in scenario.Teams)
        {
            var teamRecord = new Team { Id = team.Id, Name = team.Name };
            Battle.Teams.Add(teamRecord);
            foreach (var spawn in team.Ships)
            {
                var ship = ShipFactory.Create(repo.Ships[spawn.Ship]);
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
                ai.Ships.Add(ship);
                Guns.RegisterShip(ship);

                var armor = ShipFactory.BuildArmorTarget(ship);
                Ballistics.Targets.Add(armor);
                explosions.Targets.Add(armor);
            }
        }

        World.AddSystem(Ballistics);
        World.AddSystem(explosions);
        World.AddSystem(Bridge);
        World.AddSystem(Flooding);
        World.AddSystem(Fire);
        World.AddSystem(navigation);
        World.AddSystem(ai);
        World.AddSystem(Guns);
        World.AddSystem(damageControl);
        World.AddSystem(adjudicator);
        World.AddSystem(Battle);
    }

    /// <summary>Runs until the battle ends or the safety cap, then returns the report.</summary>
    public Dictionary<string, object?> Run(double safetyCapS = 7200)
    {
        double cap = Math.Min(safetyCapS, Battle.Victory.TimeLimitS + 300);
        while (Battle.Result == BattleResult.Running && World.Time < cap)
        {
            World.Step();
        }

        return BattleReportGenerator.Generate(World, Ships, Registry, Battle);
    }
}
