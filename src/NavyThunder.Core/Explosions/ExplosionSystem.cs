using NavyThunder.Core.Armor;
using NavyThunder.Core.Ballistics;
using NavyThunder.Core.Damage;
using NavyThunder.Core.Geometry;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Model;
using NavyThunder.Core.World;

namespace NavyThunder.Core.Explosions;

public sealed record ExplosionBlast : SimulationEvent
{
    public string ShellId { get; init; } = "";
    public Vec3 Position { get; init; }
    public double TntEquivalentKg { get; init; }
    public double BlastRadiusM { get; init; }
    public double ArmorPunchMm { get; init; }
    public override string Kind => "explosion_blast";
}

public sealed record ArmorBreach : SimulationEvent
{
    public string ShellId { get; init; } = "";
    public string PlateId { get; init; } = "";
    public double PunchMm { get; init; }
    public double PlateThicknessMm { get; init; }
    public override string Kind => "armor_breach";
}

public sealed record FragmentImpact : SimulationEvent
{
    public string ShellId { get; init; } = "";
    public string TargetId { get; init; } = "";
    public string PlateId { get; init; } = "";
    public Vec3 Position { get; init; }
    public bool Penetrated { get; init; }
    public override string Kind => "fragment_impact";
}

public sealed record OverpressureWave : SimulationEvent
{
    public string ShellId { get; init; } = "";
    public Vec3 Position { get; init; }
    public double TntEquivalentKg { get; init; }
    public double RadiusM { get; init; }

    /// <summary>Overpressure only reaches exposed open-module crews on ships (MDR-0005).</summary>
    public bool ShipsOpenModulesOnly { get; init; } = true;
    public override string Kind => "overpressure_wave";
}

/// <summary>
/// Consumes ShellDetonation events and produces the three-part War Thunder explosion
/// model (MDR-0005): shockwave damage with distance falloff, a brisant armor punch at
/// the detonation point (breach = punch vs nearest plate), and a deterministic fragment
/// cone (golden-angle spiral, 30-45° per the official wiki) whose fragments are traced
/// against target plates. Emits overpressure waves per the official rule set.
/// </summary>
public sealed class ExplosionSystem : ISimulationSystem
{
    private readonly ExplosionModel _model;
    private readonly IReadOnlyDictionary<string, ShellDefinition> _shells;
    private readonly DamageRegistry _registry;
    private int _processedEvents;

    public List<ArmorTarget> Targets { get; } = [];

    /// <summary>
    /// FragmentImpact events are the highest-volume event stream (hundreds per burst).
    /// Keep them on for Protection Analysis runs; long battles disable them to bound
    /// memory (fragment DAMAGE still flows either way).
    /// </summary>
    public bool RecordFragmentImpacts { get; set; } = true;

    public string Name => "explosions";

    public ExplosionSystem(
        ExplosionModel model,
        IReadOnlyDictionary<string, ShellDefinition> shells,
        DamageRegistry registry)
    {
        _model = model;
        _shells = shells;
        _registry = registry;
    }

    public void Initialize(SimulationWorld world)
    {
    }

    public void Update(SimulationWorld world, double deltaTime)
    {
        var events = world.Events.All;
        while (_processedEvents < events.Count)
        {
            if (events[_processedEvents] is ShellDetonation d)
            {
                ProcessDetonation(world, d);
            }

            _processedEvents++;
        }
    }

    private void ProcessDetonation(SimulationWorld world, ShellDetonation d)
    {
        if (!_shells.TryGetValue(d.ShellId, out var shell))
        {
            return;
        }

        double tnt = _model.TntEquivalentKg(shell);
        if (tnt <= 0)
        {
            return; // solid shot: no chemical effect
        }

        double cbrt = Math.Cbrt(tnt);
        double blastRadius = _model.BlastRadiusM(tnt);
        double punchMm = _model.PunchMmPerCbrtTnt * cbrt;

        world.Record(new ExplosionBlast
        {
            ShellId = d.ShellId,
            Position = d.Position,
            TntEquivalentKg = tnt,
            BlastRadiusM = blastRadius,
            ArmorPunchMm = punchMm,
        });

        // Brisant punch: breach determination against every plate near the burst point.
        foreach (var target in Targets)
        {
            foreach (var plate in target.Plates)
            {
                double distance = Vec3.Distance(plate.Center, d.Position);
                if (distance <= blastRadius && punchMm >= plate.ThicknessMm)
                {
                    world.Record(new ArmorBreach
                    {
                        ShellId = d.ShellId,
                        PlateId = plate.Id,
                        PunchMm = punchMm,
                        PlateThicknessMm = plate.ThicknessMm,
                    });
                }
            }
        }

        // Blast damage: one chemical damage event per target in radius (the Phase 3 ship
        // model translates this into compartments/modules; Phase 4 into structure).
        foreach (var target in Targets)
        {
            double nearest = NearestPlateDistance(target, d.Position);
            if (nearest <= blastRadius)
            {
                Emit(world, new DamageEvent
                {
                    Channel = DamageChannel.Chemical,
                    SourceId = d.ShellId,
                    TargetId = target.Id,
                    Position = d.Position,
                    Amount = _model.BlastDamageAt(tnt, nearest),
                });
            }
        }

        EmitFragments(world, d, shell, tnt, cbrt);

        // Overpressure: official rule set — HE family always, AP bursters above ~170 g TNT.
        if (_model.ProducesOverpressure(shell))
        {
            world.Record(new OverpressureWave
            {
                ShellId = d.ShellId,
                Position = d.Position,
                TntEquivalentKg = tnt,
                RadiusM = _model.OverpressureRadiusMPerCbrtTnt * cbrt,
            });
        }
    }

    private void EmitFragments(SimulationWorld world, ShellDetonation d, ShellDefinition shell, double tnt, double cbrt)
    {
        int count = (int)Math.Min(Math.Round(_model.FragmentsPerKgTnt * tnt), 4096);
        if (count <= 0)
        {
            return;
        }

        double fragPenMm = _model.FragmentPenMmPerCbrtTnt * cbrt;
        Vec3 axis = d.Velocity.LengthSquared < 1e-9 ? new Vec3(0, 1, 0) : d.Velocity.Normalized();
        (Vec3 u, Vec3 v) = Orthonormal(axis);
        double cosHalf = Math.Cos(_model.FragmentConeHalfAngleDeg * Math.PI / 180.0);
        const double goldenAngle = 2.39996322973;

        for (int i = 0; i < count; i++)
        {
            // Stratified golden-angle spiral: uniform-ish, fully deterministic (no RNG).
            double t = (i + 0.5) / count;
            double cosPolar = 1.0 - t * (1.0 - cosHalf);
            double sinPolar = Math.Sqrt(Math.Max(0.0, 1.0 - cosPolar * cosPolar));
            double azimuth = i * goldenAngle;
            Vec3 dir = axis * cosPolar
                       + u * (sinPolar * Math.Cos(azimuth))
                       + v * (sinPolar * Math.Sin(azimuth));

            TraceFragment(world, d.ShellId, d.Position, dir, fragPenMm);
        }
    }

    private void TraceFragment(SimulationWorld world, string shellId, Vec3 origin, Vec3 dir, double fragPenMm)
    {
        Vec3 pos = origin;
        for (int hop = 0; hop < 4; hop++)
        {
            ArmorPlate? bestPlate = null;
            ArmorTarget? bestTarget = null;
            RayHit bestHit = default;
            double bestDistance = _model.FragmentMaxRangeM;

            foreach (var target in Targets)
            {
                foreach (var (plate, hit) in target.Trace(pos, dir))
                {
                    if (hit.Distance < bestDistance)
                    {
                        bestDistance = hit.Distance;
                        bestPlate = plate;
                        bestTarget = target;
                        bestHit = hit;
                    }
                }
            }

            if (bestPlate is not { } plateHit || bestTarget is not { } targetHit)
            {
                return; // flew clear of everything
            }

            bool penetrated = plateHit.ThicknessMm <= fragPenMm;
            if (RecordFragmentImpacts)
            {
                world.Record(new FragmentImpact
                {
                    ShellId = shellId,
                    TargetId = targetHit.Id,
                    PlateId = plateHit.Id,
                    Position = bestHit.Point,
                    Penetrated = penetrated,
                });
            }

            if (!penetrated)
            {
                return;
            }

            _registry.Apply(new DamageEvent
            {
                Channel = DamageChannel.Fragment,
                SourceId = shellId,
                TargetId = targetHit.Id,
                Position = bestHit.Point,
                Amount = 2.0, // per-fragment interior effect; compartment model consumes (Phase 3)
                Tick = world.TickIndex,
                Time = world.Time,
            });

            pos = bestHit.Point + dir * (plateHit.SlabPathLength(dir) + 0.01);
        }
    }

    private static double NearestPlateDistance(ArmorTarget target, Vec3 point)
    {
        double best = double.MaxValue;
        foreach (var plate in target.Plates)
        {
            best = Math.Min(best, Vec3.Distance(plate.Center, point));
        }

        return best;
    }

    private static (Vec3 U, Vec3 V) Orthonormal(Vec3 axis)
    {
        Vec3 helper = Math.Abs(axis.Y) < 0.9 ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0);
        Vec3 u = axis.Cross(helper).Normalized();
        Vec3 v = axis.Cross(u);
        return (u, v);
    }

    private void Emit(SimulationWorld world, DamageEvent e)
    {
        _registry.Apply(e with { Tick = world.TickIndex, Time = world.Time });
    }
}
