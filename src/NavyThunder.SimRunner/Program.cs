using NavyThunder.Core.Ballistics;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.World;

// SimRunner — Phase 0: deterministic vacuum demo shot.
// Phase 1+ will drive full scripted engagements from JSON scenarios and emit
// comparable CSV/JSON reports.

double speedMs = args.Length > 0 && double.TryParse(args[0], out var s) ? s : 800.0;
double angleDeg = args.Length > 1 && double.TryParse(args[1], out var a) ? a : 45.0;

var world = new SimulationWorld(fixedDeltaTime: 0.02, masterSeed: SimulationWorld.DefaultMasterSeed);
var ballistics = new BallisticsSystem { DragModel = new VacuumDrag(), IntegrationSubsteps = 4 };
world.AddSystem(ballistics);

double rad = angleDeg * Math.PI / 180.0;
ballistics.Spawn(new BallisticProjectile
{
    Position = new Vec3(0, 0, 0),
    Velocity = new Vec3(speedMs * Math.Cos(rad), speedMs * Math.Sin(rad), 0),
    MassKg = 1225,
});

world.Run(600);

var impact = world.Events.Of<ProjectileGroundImpact>().SingleOrDefault();
if (impact is null)
{
    Console.Error.WriteLine("Projectile did not land within the simulated duration.");
    return 1;
}

Console.WriteLine(
    $$"""
    {
      "mode": "vacuum",
      "speedMs": {{speedMs}},
      "angleDeg": {{angleDeg}},
      "impactRangeM": {{impact.Position.X:F2}},
      "flightTimeS": {{impact.AgeSeconds:F3}},
      "impactSpeedMs": {{impact.ImpactSpeed:F2}},
      "analyticRangeM": {{speedMs * speedMs / 9.80665:F2}}
    }
    """);
return 0;
