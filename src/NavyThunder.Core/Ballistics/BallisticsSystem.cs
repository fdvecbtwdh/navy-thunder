using NavyThunder.Core.Armor;
using NavyThunder.Core.Geometry;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Model;
using NavyThunder.Core.World;

namespace NavyThunder.Core.Ballistics;

/// <summary>
/// Minimal projectile state for external ballistics. When <see cref="Shell"/> is set the
/// system additionally resolves armor hits and the shell's fuze; otherwise it is a pure
/// ballistic body (used by drag/trajectory tests).
/// </summary>
public struct BallisticProjectile
{
    public int Id;
    public Vec3 Position;
    public Vec3 Velocity;
    public double MassKg;
    public double AgeSeconds;
    public bool Alive;
    public ShellDefinition? Shell;

    /// <summary>Set when the fuze has fired and the shell detonates after its delay.</summary>
    public bool FuzePending;
    public double FuzeDetonationAtTime;

    /// <summary>Target id of the most recent armor interaction (empty = none).</summary>
    public string LastTargetId;

    public readonly double Speed => Velocity.Length;
}

public interface IDragModel
{
    /// <summary>Drag acceleration (m/s²) for the given projectile state; gravity is handled separately.</summary>
    Vec3 Acceleration(in BallisticProjectile projectile);
}

/// <summary>Vacuum: no drag. Used for Phase 0 smoke tests and as the zero-drag baseline.</summary>
public sealed class VacuumDrag : IDragModel
{
    public Vec3 Acceleration(in BallisticProjectile projectile) => Vec3.Zero;
}

/// <summary>
/// Constant-Cd quadratic drag: a = -(rho * Cd * A / 2m) * |v| * v.
/// Matches the War Thunder datamine model shape (single per-shell Cx constant); the
/// Mach-dependent refinement stays behind this interface for later (MDR-0002).
/// </summary>
public sealed class QuadraticDrag(double dragCoefficient, double caliberM, double projectileMassKg, double airDensity = 1.225) : IDragModel
{
    private readonly double _k = 0.5 * airDensity * dragCoefficient * (Math.PI * caliberM * caliberM / 4.0) / projectileMassKg;

    public Vec3 Acceleration(in BallisticProjectile projectile)
    {
        double speed = projectile.Speed;
        return speed <= 0 ? Vec3.Zero : projectile.Velocity * (-_k * speed);
    }
}

public sealed record ProjectileGroundImpact : SimulationEvent
{
    public int ProjectileId { get; init; }
    public Vec3 Position { get; init; }
    public Vec3 Velocity { get; init; }
    public double AgeSeconds { get; init; }

    public double ImpactSpeed => Velocity.Length;
    public override string Kind => "projectile_ground_impact";
}

public sealed record ProjectileArmorImpact : SimulationEvent
{
    public int ProjectileId { get; init; }
    public string ShellId { get; init; } = "";
    public string TargetId { get; init; } = "";
    public string PlateId { get; init; } = "";
    public Vec3 Position { get; init; }
    public Vec3 Direction { get; init; }
    public double ImpactSpeedMs { get; init; }
    public double ImpactAngleDeg { get; init; }
    public double PlateThicknessMm { get; init; }
    public double EffectiveThicknessMm { get; init; }
    public double PenetrationMm { get; init; }
    public required PlateResolution Outcome { get; init; }
    public bool FuzeTriggered { get; init; }
    public bool OvermatchApplied { get; init; }
    public double ResidualEnergyFraction { get; init; }

    public override string Kind => "projectile_armor_impact";
}

public sealed record ShellDetonation : SimulationEvent
{
    public int ProjectileId { get; init; }
    public string ShellId { get; init; } = "";
    public string TargetId { get; init; } = "";
    public Vec3 Position { get; init; }

    /// <summary>Velocity at detonation — the fragment cone axis (MDR-0005).</summary>
    public Vec3 Velocity { get; init; }

    /// <summary>True when the shell detonated after passing a plate (internal burst path).</summary>
    public bool AfterPenetration { get; init; }

    /// <summary>True when triggered by fuseOnWater at the surface rather than an armor plate.</summary>
    public bool OnWaterSurface { get; init; }

    public override string Kind => "shell_detonation";
}

/// <summary>
/// Integrates projectiles with fixed-substep RK4 under constant gravity and an
/// injectable drag model, resolving armor hits and fuzes along the way.
/// Deterministic: no wall clock, ordered iteration, RNG only from the "armor" stream.
/// </summary>
public sealed class BallisticsSystem : ISimulationSystem
{
    private readonly List<BallisticProjectile> _projectiles = [];
    private readonly Rk4Integrator _integrator = new();

    public Vec3 Gravity { get; init; } = new(0, -9.80665, 0);
    public IDragModel DragModel { get; init; } = new VacuumDrag();
    public int IntegrationSubsteps { get; init; } = 4;
    public double GroundLevelY { get; init; } = 0.0;
    public int NextProjectileId { get; private set; }

    /// <summary>Armor targets checked for hits; empty = pure ballistics.</summary>
    public List<ArmorTarget> Targets { get; } = [];

    /// <summary>Null disables armor resolution (bare-ballistics mode).</summary>
    public ArmorResolver? Armor { get; set; }

    public IReadOnlyList<BallisticProjectile> Projectiles => _projectiles;

    public string Name => "ballistics";

    public void Initialize(SimulationWorld world)
    {
        _integrator.Gravity = Gravity;
        _integrator.DragModel = DragModel;
    }

    public int Spawn(BallisticProjectile projectile)
    {
        projectile.Id = ++NextProjectileId;
        projectile.Alive = true;
        _projectiles.Add(projectile);
        return projectile.Id;
    }

    public void Update(SimulationWorld world, double deltaTime)
    {
        double h = deltaTime / IntegrationSubsteps;
        for (int i = _projectiles.Count - 1; i >= 0; i--)
        {
            var p = _projectiles[i];
            for (int s = 0; s < IntegrationSubsteps && p.Alive; s++)
            {
                Vec3 before = p.Position;
                _integrator.Step(ref p, h);
                if (Armor is not null && p.Shell is not null && Targets.Count > 0)
                {
                    HandleArmorHits(ref p, world, before);
                }
            }

            if (!p.Alive)
            {
                _projectiles[i] = p;
                continue;
            }

            p.AgeSeconds += deltaTime;

            if (p.FuzePending && world.Time >= p.FuzeDetonationAtTime)
            {
                Detonate(ref p, world, afterPenetration: true);
            }
            else if (p.Position.Y <= GroundLevelY)
            {
                // fuseOnWater = true for all naval shells: surface contact detonates (MDR-0003).
                if (p.Shell is not null)
                {
                    Detonate(ref p, world, afterPenetration: false, onWaterSurface: true);
                }
                else
                {
                    p.Alive = false;
                    _projectiles[i] = p;
                    world.Record(new ProjectileGroundImpact
                    {
                        ProjectileId = p.Id,
                        Position = p.Position,
                        Velocity = p.Velocity,
                        AgeSeconds = p.AgeSeconds,
                    });
                    continue;
                }
            }

            _projectiles[i] = p;
        }

        _projectiles.RemoveAll(p => !p.Alive);
    }

    private void HandleArmorHits(ref BallisticProjectile p, SimulationWorld world, Vec3 segmentStart)
    {
        Vec3 segment = p.Position - segmentStart;
        double segmentLength = segment.Length;
        if (segmentLength < 1e-9)
        {
            return;
        }

        Vec3 dir = segment / segmentLength;
        ArmorPlate? bestPlate = null;
        ArmorTarget? bestTarget = null;
        RayHit bestHit = default;
        foreach (var target in Targets)
        {
            foreach (var (plate, hit) in target.Trace(segmentStart, dir))
            {
                if (hit.Distance > segmentLength)
                {
                    break; // sorted by distance — rest are further away
                }

                if (bestPlate is null || hit.Distance < bestHit.Distance)
                {
                    bestPlate = plate;
                    bestTarget = target;
                    bestHit = hit;
                }
            }
        }

        if (bestPlate is not { } hitPlate || bestTarget is not { } hitTarget)
        {
            return;
        }

        var resolver = Armor!;
        double impactAngleDeg = Math.Acos(Math.Clamp(Math.Abs(dir.Dot(hitPlate.Normal)), 0.0, 1.0)) * 180.0 / Math.PI;
        var result = resolver.Resolve(p.Shell!, p.Speed, impactAngleDeg, hitPlate, world.Rng("armor"));

        p.LastTargetId = hitTarget.Id;
        world.Record(new ProjectileArmorImpact
        {
            ProjectileId = p.Id,
            ShellId = p.Shell!.Id,
            TargetId = hitTarget.Id,
            Position = bestHit.Point,
            Direction = dir,
            PlateId = result.PlateId,
            ImpactSpeedMs = result.ImpactSpeedMs,
            ImpactAngleDeg = result.ImpactAngleDeg,
            PlateThicknessMm = result.PlateThicknessMm,
            EffectiveThicknessMm = result.EffectiveThicknessMm,
            PenetrationMm = result.PenetrationMm,
            Outcome = result.Outcome,
            FuzeTriggered = result.FuzeTriggered,
            OvermatchApplied = result.OvermatchApplied,
            ResidualEnergyFraction = result.ResidualEnergyFraction,
        });

        switch (result.Outcome)
        {
            case PlateResolution.Ricocheted:
            {
                // Reflect about the face normal; the fuze does not fire on ricochet.
                Vec3 n = bestHit.Normal;
                p.Position = bestHit.Point - dir * 0.01;
                p.Velocity = p.Velocity - n * (2.0 * p.Velocity.Dot(n));
                break;
            }

            case PlateResolution.Penetrated:
            {
                // Advance to just past the slab so multi-plate layouts resolve layer by layer.
                double slab = hitPlate.SlabPathLength(dir);
                p.Position = bestHit.Point + dir * (slab + 0.001);
                if (result.FuzeTriggered && !p.FuzePending)
                {
                    p.FuzePending = true;
                    p.FuzeDetonationAtTime = world.Time + p.Shell!.FuseDelayS;
                }

                break;
            }

            case PlateResolution.Stopped:
            {
                p.Position = bestHit.Point;
                p.Alive = false;
                if (result.FuzeTriggered)
                {
                    RecordDetonation(world, p, p.Position, p.Velocity, afterPenetration: false, onWaterSurface: false);
                }

                // No fuze fire on too-thin plates: inert stop (kinetic-only damage in Phase 2).
                break;
            }
        }
    }

    private void Detonate(ref BallisticProjectile p, SimulationWorld world, bool afterPenetration, bool onWaterSurface = false)
    {
        p.Alive = false;
        RecordDetonation(world, p, p.Position, p.Velocity, afterPenetration, onWaterSurface);
    }

    private static void RecordDetonation(
        SimulationWorld world,
        in BallisticProjectile p,
        Vec3 position,
        Vec3 velocity,
        bool afterPenetration,
        bool onWaterSurface)
    {
        world.Record(new ShellDetonation
        {
            ProjectileId = p.Id,
            ShellId = p.Shell?.Id ?? "",
            TargetId = p.LastTargetId,
            Position = position,
            Velocity = velocity,
            AfterPenetration = afterPenetration,
            OnWaterSurface = onWaterSurface,
        });
    }

}
