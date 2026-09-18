using NavyThunder.Core.Mathematics;
using NavyThunder.Core.World;

namespace NavyThunder.Core.Ballistics;

/// <summary>
/// Minimal projectile state for external-ballistics integration.
/// Phase 1 adds fuze state and payload references on top of this struct.
/// </summary>
public struct BallisticProjectile
{
    public int Id;
    public Vec3 Position;
    public Vec3 Velocity;
    public double MassKg;
    public double AgeSeconds;
    public bool Alive;

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

/// <summary>
/// Integrates projectiles with fixed-substep RK4 under constant gravity and an
/// injectable drag model. Deterministic: no wall clock, ordered iteration, no RNG use.
/// </summary>
public sealed class BallisticsSystem : ISimulationSystem
{
    private readonly List<BallisticProjectile> _projectiles = [];

    public Vec3 Gravity { get; init; } = new(0, -9.80665, 0);
    public IDragModel DragModel { get; init; } = new VacuumDrag();
    public int IntegrationSubsteps { get; init; } = 4;
    public double GroundLevelY { get; init; } = 0.0;
    public int NextProjectileId { get; private set; }

    public IReadOnlyList<BallisticProjectile> Projectiles => _projectiles;

    public string Name => "ballistics";

    public void Initialize(SimulationWorld world)
    {
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
            for (int s = 0; s < IntegrationSubsteps; s++)
            {
                Integrate(ref p, h);
            }

            p.AgeSeconds += deltaTime;

            if (p.Position.Y <= GroundLevelY)
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
            }
            else
            {
                _projectiles[i] = p;
            }
        }

        _projectiles.RemoveAll(p => !p.Alive);
    }

    private void Integrate(ref BallisticProjectile p, double h)
    {
        Vec3 v = p.Velocity;
        Vec3 x = p.Position;

        Vec3 a1 = Gravity + DragModel.Acceleration(p);
        Vec3 k1x = v;

        var mid1 = p;
        mid1.Velocity = v + a1 * (h / 2);
        Vec3 a2 = Gravity + DragModel.Acceleration(mid1);
        Vec3 k2x = v + a1 * (h / 2);

        var mid2 = p;
        mid2.Velocity = v + a2 * (h / 2);
        Vec3 a3 = Gravity + DragModel.Acceleration(mid2);
        Vec3 k3x = v + a2 * (h / 2);

        var end = p;
        end.Velocity = v + a3 * h;
        Vec3 a4 = Gravity + DragModel.Acceleration(end);
        Vec3 k4x = v + a3 * h;

        p.Velocity = v + (a1 + (a2 + a3) * 2.0 + a4) * (h / 6);
        p.Position = x + (k1x + (k2x + k3x) * 2.0 + k4x) * (h / 6);
    }
}
