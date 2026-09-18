using NavyThunder.Core.Armor;
using NavyThunder.Core.Ballistics;
using NavyThunder.Core.Damage;
using NavyThunder.Core.Geometry;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Model;
using NavyThunder.Core.World;

namespace NavyThunder.Core.Aviation;

public sealed record AircraftLost : SimulationEvent
{
    public string AircraftId { get; init; } = "";
    public required AircraftState State { get; init; }
    public string Reason { get; init; } = "";
    public override string Kind => "aircraft_lost";
}

/// <summary>
/// Aircraft bridge + kill adjudication (MDR-0013 destroy_rules): routes armor impacts
/// to the part behind the struck skin plate, converts skin bursts into part damage,
/// enforces the Critical-G spar limit (overload with damaged spar -> wing detaches ->
/// instant kill) and the severe-damage tier (all controls gone or engine gone:
/// uncontrollable, finishable-off).
/// </summary>
public sealed class AircraftAdjudicatorSystem : ISimulationSystem
{
    private readonly DamageRegistry _registry;
    private readonly HashSet<(int ProjectileId, string PartId)> _wounded = [];
    private readonly HashSet<string> _reported = [];
    private readonly IReadOnlyDictionary<string, ShellDefinition> _shells;
    private int _processedEvents;

    public List<Aircraft> Aircraft { get; } = [];
    public Dictionary<string, Aircraft> ByTargetId { get; } = [];

    /// <summary>Kinetic damage (HP) per cbrt(kg) of projectile mass (engine scale, approximation).</summary>
    public double KineticDamagePerCbrtKg { get; init; } = 900.0;

    public string Name => "aircraft_adjudication";

    public AircraftAdjudicatorSystem(DamageRegistry registry, IReadOnlyDictionary<string, ShellDefinition> shells)
    {
        _registry = registry;
        _shells = shells;
    }

    public void Initialize(SimulationWorld world)
    {
    }

    public void Update(SimulationWorld world, double deltaTime)
    {
        var events = world.Events.All;
        while (_processedEvents < events.Count)
        {
            if (events[_processedEvents] is ProjectileArmorImpact impact
                && impact.Outcome == PlateResolution.Penetrated
                && ByTargetId.TryGetValue(impact.TargetId, out var aircraft))
            {
                var part = PartFromPlate(aircraft, impact.PlateId);
                if (part is not null && !part.Destroyed
                    && _wounded.Add((impact.ProjectileId, part.Definition.Id)))
                {
                    // One projectile wounds each part once (entry + exit faces = one wound).
                    double amount = KineticDamagePerCbrtKg
                                    * Math.Cbrt(ShellMass(impact.ShellId))
                                    * Math.Max(0.3, impact.ResidualEnergyFraction);
                    _registry.Apply(new DamageEvent
                    {
                        Channel = DamageChannel.Kinetic,
                        SourceId = impact.ShellId,
                        TargetId = aircraft.TargetId,
                        Position = part.Center,
                        Amount = amount,
                        Tick = world.TickIndex,
                        Time = world.Time,
                    });
                }
            }

            _processedEvents++;
        }

        foreach (var aircraft in Aircraft)
        {
            // Kills may originate in the damage path itself (pilot hit, ammo rack);
            // record the first transition out of Airborne exactly once.
            if (aircraft.State == AircraftState.Destroyed && _reported.Add(aircraft.TargetId))
            {
                world.Record(new AircraftLost
                {
                    AircraftId = aircraft.TargetId,
                    State = AircraftState.Destroyed,
                    Reason = aircraft.LossReason ?? "",
                });
                continue;
            }

            if (!aircraft.Alive)
            {
                continue;
            }

            // Critical-G spar rule: each damaged spar has its own reduced limit; exceeding
            // it tears that wing off (instant kill, WT destroy_rules).
            var feedback = aircraft.ComputeFeedback();
            bool torn = aircraft.WingDetached()
                        || aircraft.Parts.Values.Any(p =>
                            p.Definition.Kind == AircraftPartKind.WingSpar && !p.Destroyed
                            && aircraft.CurrentG > aircraft.Definition.DesignG * (0.45 + 0.55 * (p.Hp / p.Definition.Hp)));
            if (torn)
            {
                aircraft.Kill("wing_detached", world.Time);
            }

            // Severe damage: no control authority at all (uncontrollable, finishable-off).
            if (aircraft.State == AircraftState.Airborne
                && feedback.PitchAuthority <= 0
                && feedback.RollAuthority <= 0)
            {
                aircraft.State = AircraftState.SeverelyDamaged;
            }

            if (aircraft.State != AircraftState.Airborne && _reported.Add(aircraft.TargetId))
            {
                world.Record(new AircraftLost
                {
                    AircraftId = aircraft.TargetId,
                    State = aircraft.State,
                    Reason = aircraft.LossReason ?? "severe_damage",
                });
            }
        }
    }

    private static AircraftPartState? PartFromPlate(Aircraft aircraft, string plateId)
    {
        int colon = plateId.IndexOf(':');
        if (colon <= 0)
        {
            return null;
        }

        string partId = plateId[..colon];
        return aircraft.Parts.TryGetValue(partId, out var part) ? part : null;
    }

    private double ShellMass(string shellId)
    {
        return _shells.TryGetValue(shellId, out var shell) ? shell.MassKg : 0.1;
    }
}

/// <summary>Builds the aircraft's ArmorTarget: skin plates on every part box face.</summary>
public static class AircraftFactory
{
    public static Aircraft Create(AircraftDefinition definition) => new(definition);

    public static ArmorTarget BuildArmorTarget(Aircraft aircraft)
    {
        var target = new ArmorTarget { Id = aircraft.TargetId };
        foreach (var part in aircraft.Definition.Parts)
        {
            foreach (BoxFace face in Enum.GetValues<BoxFace>())
            {
                target.Add(MakeSkinPlate(part, face));
            }
        }

        return target;
    }

    private static ArmorPlate MakeSkinPlate(AircraftPartDefinition part, BoxFace face)
    {
        double hx = (part.XMaxM - part.XMinM) / 2;
        double hy = (part.YMaxM - part.YMinM) / 2;
        double hz = (part.ZMaxM - part.ZMinM) / 2;
        double cx = (part.XMinM + part.XMaxM) / 2;
        double cy = (part.YMinM + part.YMaxM) / 2;
        double cz = (part.ZMinM + part.ZMaxM) / 2;

        string id = $"{part.Id}:{face}";
        const string material = "aircraft_dural";
        double skin = part.SkinThicknessMm;
        return face switch
        {
            BoxFace.XMin => new ArmorPlate
            {
                Id = id, Material = material, ThicknessMm = skin,
                Center = new Vec3(part.XMinM, cy, cz), Normal = new Vec3(-1, 0, 0),
                AxisU = new Vec3(0, 1, 0), AxisV = new Vec3(0, 0, 1), HalfU = hy, HalfV = hz,
            },
            BoxFace.XMax => new ArmorPlate
            {
                Id = id, Material = material, ThicknessMm = skin,
                Center = new Vec3(part.XMaxM, cy, cz), Normal = new Vec3(1, 0, 0),
                AxisU = new Vec3(0, 1, 0), AxisV = new Vec3(0, 0, 1), HalfU = hy, HalfV = hz,
            },
            BoxFace.YMin => new ArmorPlate
            {
                Id = id, Material = material, ThicknessMm = skin,
                Center = new Vec3(cx, part.YMinM, cz), Normal = new Vec3(0, -1, 0),
                AxisU = new Vec3(1, 0, 0), AxisV = new Vec3(0, 0, 1), HalfU = hx, HalfV = hz,
            },
            BoxFace.YMax => new ArmorPlate
            {
                Id = id, Material = material, ThicknessMm = skin,
                Center = new Vec3(cx, part.YMaxM, cz), Normal = new Vec3(0, 1, 0),
                AxisU = new Vec3(1, 0, 0), AxisV = new Vec3(0, 0, 1), HalfU = hx, HalfV = hz,
            },
            BoxFace.ZMin => new ArmorPlate
            {
                Id = id, Material = material, ThicknessMm = skin,
                Center = new Vec3(cx, cy, part.ZMinM), Normal = new Vec3(0, 0, -1),
                AxisU = new Vec3(1, 0, 0), AxisV = new Vec3(0, 1, 0), HalfU = hx, HalfV = hy,
            },
            _ => new ArmorPlate
            {
                Id = id, Material = material, ThicknessMm = skin,
                Center = new Vec3(cx, cy, part.ZMaxM), Normal = new Vec3(0, 0, 1),
                AxisU = new Vec3(1, 0, 0), AxisV = new Vec3(0, 1, 0), HalfU = hx, HalfV = hy,
            },
        };
    }
}
