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
    private long _lastSeq;

    public List<Aircraft> Aircraft { get; } = [];
    public Dictionary<string, Aircraft> ByTargetId { get; } = [];

    /// <summary>Kinetic damage (HP) per cbrt(kg) of projectile mass (engine scale) -
    /// low so AA-class hits wound modules instead of deleting the airframe; heavy naval
    /// shells still one-shot any part at this scale.</summary>
    public double KineticDamagePerCbrtKg { get; init; } = 90.0;

    /// <summary>VT airburst wounds aircraft within this radius (WT barrage effectiveness).</summary>
    public double AirburstLethalRadiusM { get; init; } = 30.0;

    /// <summary>Diagnostics: airburst events seen and applications applied.</summary>
    public long AirburstsSeen;
    public long AirburstApplications;

    /// <summary>Airburst damage (HP) per cbrt(kg) of shell mass at the burst centre -
    /// tuned so a heavy barrage downs aircraft over sustained exposure while a lone
    /// ship gives attackers a window to press their strike.</summary>
    public double AirburstDamagePerCbrtKg { get; init; } = 60.0;

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
        foreach (var e in world.Events.After(_lastSeq))
        {
            if (e is ProjectileArmorImpact impact
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
            else if (e is ShellDetonation burst && burst.TargetId.Length == 0)
            {
                AirburstsSeen++;

                // VT airburst: fragments + blast wound every aircraft near the burst,
                // linear falloff to the lethal radius (barrage AA kill mechanism).
                foreach (var victim in Aircraft)
                {
                    if (!victim.Alive)
                    {
                        continue;
                    }

                    double d = Vec3.Distance(burst.Position, victim.WorldPosition);
                    if (d > AirburstLethalRadiusM)
                    {
                        continue;
                    }

                    // Fragments strike the OUTERMOST structure nearest the burst (point to
                    // part-box surface distance in the airframe's local frame); buried
                    // parts (pilot, oil) are only reached once the skin around them dies.
                    Vec3 local = burst.Position - victim.WorldPosition;
                    AircraftPartState? nearest = null;
                    double bestSurface = double.MaxValue;
                    foreach (var pt in victim.Parts.Values)
                    {
                        if (pt.Destroyed)
                        {
                            continue;
                        }

                        double gapX = Math.Max(pt.Definition.XMinM - local.X, Math.Max(0, local.X - pt.Definition.XMaxM));
                        double gapY = Math.Max(pt.Definition.YMinM - local.Y, Math.Max(0, local.Y - pt.Definition.YMaxM));
                        double gapZ = Math.Max(pt.Definition.ZMinM - local.Z, Math.Max(0, local.Z - pt.Definition.ZMaxM));
                        double surf = Math.Sqrt(gapX * gapX + gapY * gapY + gapZ * gapZ);
                        if (surf < bestSurface)
                        {
                            bestSurface = surf;
                            nearest = pt;
                        }
                    }

                    if (nearest is null)
                    {
                        continue;
                    }

                    double falloff = 1.0 - d / AirburstLethalRadiusM;
                    AirburstApplications++;
                    _registry.Apply(new DamageEvent
                    {
                        Channel = DamageChannel.Fragment,
                        SourceId = burst.ShellId,
                        TargetId = victim.TargetId,
                        Position = nearest.Center,
                        Amount = AirburstDamagePerCbrtKg * Math.Cbrt(ShellMass(burst.ShellId)) * falloff,
                        Tick = world.TickIndex,
                        Time = world.Time,
                    });
                }
            }
        }

        _lastSeq = world.Events.TotalRecorded;

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
    public static Aircraft Create(AircraftDefinition definition, string? instanceKey = null)
        => new(definition, instanceKey);

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
                BaseCenter = new Vec3(part.XMinM, cy, cz), Normal = new Vec3(-1, 0, 0),
                AxisU = new Vec3(0, 1, 0), AxisV = new Vec3(0, 0, 1), HalfU = hy, HalfV = hz,
            },
            BoxFace.XMax => new ArmorPlate
            {
                Id = id, Material = material, ThicknessMm = skin,
                BaseCenter = new Vec3(part.XMaxM, cy, cz), Normal = new Vec3(1, 0, 0),
                AxisU = new Vec3(0, 1, 0), AxisV = new Vec3(0, 0, 1), HalfU = hy, HalfV = hz,
            },
            BoxFace.YMin => new ArmorPlate
            {
                Id = id, Material = material, ThicknessMm = skin,
                BaseCenter = new Vec3(cx, part.YMinM, cz), Normal = new Vec3(0, -1, 0),
                AxisU = new Vec3(1, 0, 0), AxisV = new Vec3(0, 0, 1), HalfU = hx, HalfV = hz,
            },
            BoxFace.YMax => new ArmorPlate
            {
                Id = id, Material = material, ThicknessMm = skin,
                BaseCenter = new Vec3(cx, part.YMaxM, cz), Normal = new Vec3(0, 1, 0),
                AxisU = new Vec3(1, 0, 0), AxisV = new Vec3(0, 0, 1), HalfU = hx, HalfV = hz,
            },
            BoxFace.ZMin => new ArmorPlate
            {
                Id = id, Material = material, ThicknessMm = skin,
                BaseCenter = new Vec3(cx, cy, part.ZMinM), Normal = new Vec3(0, 0, -1),
                AxisU = new Vec3(1, 0, 0), AxisV = new Vec3(0, 1, 0), HalfU = hx, HalfV = hy,
            },
            _ => new ArmorPlate
            {
                Id = id, Material = material, ThicknessMm = skin,
                BaseCenter = new Vec3(cx, cy, part.ZMaxM), Normal = new Vec3(0, 0, 1),
                AxisU = new Vec3(1, 0, 0), AxisV = new Vec3(0, 1, 0), HalfU = hx, HalfV = hy,
            },
        };
    }
}
