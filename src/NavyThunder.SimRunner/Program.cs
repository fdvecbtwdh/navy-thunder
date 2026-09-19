using System.Globalization;
using System.Text.Json;
using NavyThunder.Data;

// SimRunner v2 (R0.8): scenario-driven deterministic battles.
//   nt-sim --scenario <path> [--out <report.json>]
//   nt-sim [speedMs] [angleDeg]   (legacy vacuum demo)

var scenarioPath = (string?)null;
var outPath = "battle-report.json";

for (int i = 0; i < args.Length - 1; i++)
{
    switch (args[i])
    {
        case "--scenario": scenarioPath = args[i + 1]; break;
        case "--out": outPath = args[i + 1]; break;
    }
}

if (scenarioPath is null && args.Length > 0 && double.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
{
    RunVacuumDemo(args);
    return 0;
}

if (scenarioPath is null)
{
    Console.WriteLine("""
        Navy Thunder — SimRunner
        Usage:
          nt-sim --scenario <path> [--out <report.json>]
          nt-sim [speedMs] [angleDeg]   (legacy vacuum demo)
        """);
    return 0;
}

var repo = DataRepository.LoadFromDirectory(RepoLocator.FindDataDirectory());
var scenario = BattleScenario.Load(scenarioPath);
var runner = new BattleRunner(repo, scenario);

Console.Error.WriteLine($"battle '{scenario.Name}': {runner.Ships.Count} ships, seed {scenario.Seed}");
var report = runner.Run();

var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
{
    WriteIndented = true,
    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
});
File.WriteAllText(outPath, json);
Console.WriteLine(json);
Console.Error.WriteLine($"report -> {outPath}");
return 0;

static void RunVacuumDemo(string[] args)
{
    double speedMs = double.Parse(args[0], CultureInfo.InvariantCulture);
    double angleDeg = args.Length > 1 ? double.Parse(args[1], CultureInfo.InvariantCulture) : 45;

    var world = new NavyThunder.Core.World.SimulationWorld();
    var ballistics = new NavyThunder.Core.Ballistics.BallisticsSystem();
    world.AddSystem(ballistics);
    double rad = angleDeg * Math.PI / 180.0;
    ballistics.Spawn(new NavyThunder.Core.Ballistics.BallisticProjectile
    {
        Position = new NavyThunder.Core.Mathematics.Vec3(0, 0, 0),
        Velocity = new NavyThunder.Core.Mathematics.Vec3(speedMs * Math.Cos(rad), speedMs * Math.Sin(rad), 0),
        MassKg = 1225,
    });
    world.Run(600);
    var impact = world.Events.Of<NavyThunder.Core.Ballistics.ProjectileGroundImpact>().Single();
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
    {
        mode = "vacuum",
        speedMs,
        angleDeg,
        impactRangeM = Math.Round(impact.Position.X, 2),
        flightTimeS = Math.Round(impact.AgeSeconds, 3),
        impactSpeedMs = Math.Round(impact.ImpactSpeed, 2),
    }));
}
