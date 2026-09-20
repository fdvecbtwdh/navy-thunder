using NavyThunder.Core.Aviation;
using NavyThunder.Core.Ballistics;
using NavyThunder.Core.FireControl;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Model;
using NavyThunder.Core.World;

namespace NavyThunder.Core.AntiAir;

public sealed record AAMount
{
    public required string Id { get; init; }
    public required Vec3 Position { get; init; }
    public required string ShellId { get; init; }

    /// <summary>Effective engagement radius (m).</summary>
    public required double RangeM { get; init; }

    /// <summary>Combined muzzle velocity of the mount (m/s).</summary>
    public required double MuzzleVelocityMs { get; init; }

    public double RoundsPerMinute { get; init; } = 40;

    /// <summary>Dispersion in milliradians (data-driven; crude on purpose, MDR-0014).</summary>
    public double HorizontalMrad { get; init; } = 4.0;
    public double VerticalMrad { get; init; } = 3.0;

    /// <summary>Optional radar: functional radar tightens the barrage, destroyed radar loosens it.</summary>
    public RadarSensor? Radar { get; init; }

    /// <summary>Ship-mounted mounts resolve their position each tick (the hull moves).</summary>
    public Func<Vec3>? PositionProvider { get; init; }

    /// <summary>Mount only fires while this holds (e.g. host ship still alive).</summary>
    public Func<bool>? IsActive { get; init; }

    public Vec3 ResolvePosition() => PositionProvider?.Invoke() ?? Position;

    public bool ResolvesActive() => IsActive?.Invoke() ?? true;
}

/// <summary>
/// AI anti-aircraft gunnery (MDR-0014): crude target selection (nearest air target in
/// range), ballistic lead solution, VT-proximity shells, elliptical dispersion widened
/// when the radar is knocked out. Deterministic through the "aa" RNG stream.
/// </summary>
public sealed class AntiAircraftSystem : ISimulationSystem
{
    private readonly IReadOnlyDictionary<string, ShellDefinition> _shells;
    private readonly Dictionary<string, double> _cooldown = [];
    private int _nextShotId;

    public List<AAMount> Mounts { get; } = [];
    public List<Aircraft> AirTargets { get; } = [];
    public BallisticsSystem Ballistics { get; init; } = null!;

    /// <summary>Shots fired / hits recorded for tests and Protection Analysis.</summary>
    public int ShotsFired { get; private set; }

    public string Name => "anti_air";

    public AntiAircraftSystem(IReadOnlyDictionary<string, ShellDefinition> shells)
    {
        _shells = shells;
    }

    public void Initialize(SimulationWorld world)
    {
    }

    private double EffectiveInterval(AAMount mount) => 60.0 / Math.Max(0.1, mount.RoundsPerMinute);

    private double DispersionMultiplier(AAMount mount) =>
        mount.Radar is { Functional: true } ? 1.0 : 1.5;

    public void Update(SimulationWorld world, double deltaTime)
    {
        foreach (var mount in Mounts)
        {
            if (!mount.ResolvesActive())
            {
                continue;
            }

            Vec3 position = mount.ResolvePosition();
            _cooldown.TryGetValue(mount.Id, out double cooldown);
            cooldown -= deltaTime;

            Aircraft? target = null;
            double bestRange = mount.RangeM;
            foreach (var candidate in AirTargets)
            {
                if (!candidate.Alive)
                {
                    continue;
                }

                double d = Vec3.Distance(candidate.WorldPosition, position);
                if (d <= bestRange)
                {
                    bestRange = d;
                    target = candidate;
                }
            }

            if (target is null || cooldown > 0)
            {
                _cooldown[mount.Id] = Math.Max(0, cooldown);
                continue;
            }

            _cooldown[mount.Id] = EffectiveInterval(mount);

            if (!_shells.TryGetValue(mount.ShellId, out var shell))
            {
                continue;
            }

            // Lead solution with ballistic time of flight; dispersion on top.
            var solution = FcsSolver.SolveLead(position, mount.MuzzleVelocityMs,
                target.WorldPosition, target.Velocity);
            Vec3 dir = (solution.AimPoint - position).Normalized();

            var dispersion = new DispersionModel
            {
                HorizontalMrad = mount.HorizontalMrad,
                VerticalMrad = mount.VerticalMrad,
                PenaltyMultiplier = DispersionMultiplier(mount),
            };
            dir = dispersion.Apply(dir, world.Rng("aa"));

            Ballistics.Spawn(new BallisticProjectile
            {
                Position = position,
                Velocity = dir * mount.MuzzleVelocityMs,
                MassKg = 1,
                Shell = shell,
            });
            ShotsFired++;
        }
    }
}
