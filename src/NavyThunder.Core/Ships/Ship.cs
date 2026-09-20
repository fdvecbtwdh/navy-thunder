using NavyThunder.Core.Damage;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Model;
using NavyThunder.Core.World;

namespace NavyThunder.Core.Ships;

public enum ShipKillState
{
    Alive,
    Scuttled,          // crew below minimum surviving complement
    Foundered,         // buoyancy loss >= 100 % or unsinkability lost
    Capsized,          // list beyond the capsize angle
    Destroyed,         // crew annihilated or magazine detonation
}

public sealed class ShipPartState
{
    public ShipPartDefinition Definition { get; }
    public double Hp { get; internal set; }
    public double CrewDeadFraction { get; internal set; }
    public double WaterLevel { get; internal set; }   // 0..1, compartments with breaches
    public bool Breached { get; internal set; }
    public double RepairWork { get; internal set; }   // accumulated damage-control seconds
    public bool MagazineExploded { get; internal set; }

    public bool Destroyed { get; internal set; }
    public bool Flooded => WaterLevel >= 1.0;

    internal ShipPartState(ShipPartDefinition definition)
    {
        Definition = definition;
        Hp = definition.Hp;
    }

    public int CrewAlive => (int)Math.Round(Definition.Crew * (1.0 - Math.Clamp(CrewDeadFraction, 0, 1)));

    public bool Contains(Vec3 p) =>
        p.X >= Definition.XMinM && p.X <= Definition.XMaxM
        && p.Y >= Definition.YMinM && p.Y <= Definition.YMaxM
        && p.Z >= Definition.ZMinM && p.Z <= Definition.ZMaxM;

    public Vec3 Center => new(
        (Definition.XMinM + Definition.XMaxM) / 2,
        (Definition.YMinM + Definition.YMaxM) / 2,
        (Definition.ZMinM + Definition.ZMaxM) / 2);
}

public sealed class HullSectionState
{
    public HullSectionDefinition Definition { get; }
    public double Hp { get; internal set; }
    public bool Destroyed { get; internal set; }

    internal HullSectionState(HullSectionDefinition definition)
    {
        Definition = definition;
        Hp = definition.Hp;
    }
}

/// <summary>
/// The ship damage model (MDR-0006/0007/0008/0011): structural hull sections, crewed
/// compartments with the official linear crew-loss conversion, modules with functional
/// effects, first-stage ammunition, breaches and buoyancy bookkeeping. Pure state —
/// world-visible consequences (magazine detonations, scuttling) are executed by the
/// KillAdjudicatorSystem so the damage sink stays world-free.
/// </summary>
public sealed class Ship : Entity, IDamageSink
{
    public const string CrewHpPerMember = "crew_hp_per_member"; // wtReference id

    private readonly Dictionary<string, ShipPartState> _partsById = [];
    private readonly Dictionary<string, List<ShipPartState>> _partsBySection = [];
    private readonly Dictionary<string, List<ShipPartState>> _partsByTurretGroup = [];

    public ShipDefinition Definition { get; }
    public double CrewHpPer { get; init; } = 125.0;

    public IReadOnlyDictionary<string, ShipPartState> Parts => _partsById;
    public IReadOnlyList<HullSectionState> Sections { get; }
    public IReadOnlyCollection<string> TurretGroups { get; }

    public string TargetId { get; }

    /// <summary>World placement of the ship origin, integrated by the navigation system.</summary>
    public Vec3 WorldPosition { get; set; } = Vec3.Zero;

    // ---------------- navigation state (R0.2) ----------------

    /// <summary>Ordered rudder, -1 (port) .. +1 (starboard).</summary>
    public double RudderCommand { get; set; }

    /// <summary>Ordered throttle, 0 .. 1.</summary>
    public double ThrottleCommand { get; set; } = 1.0;

    public double HeadingDeg { get; set; }
    public double SpeedKnots { get; internal set; }
    public Team? Team { get; set; }

    /// <summary>When unsinkability was lost (irreversible - MDR-0007).</summary>
    public double? UnsinkabilityLostTime { get; internal set; }

    public double CrewDead { get; internal set; }

    /// <summary>Lifetime accumulated damage (battle-report aggregate, trim-safe).</summary>
    public double DamageTaken { get; internal set; }
    public int HitsTaken { get; internal set; }
    public double ListDeg { get; internal set; }
    public bool UnsinkabilityLost { get; internal set; }
    public ShipKillState KillState { get; internal set; } = ShipKillState.Alive;
    public string? KillReason { get; internal set; }
    public double? DestroyedTime { get; internal set; }

    // First-stage ammunition state per turret group.
    private readonly Dictionary<string, int> _readyRacks = [];
    private readonly Dictionary<string, double> _resupplyProgress = [];
    public double LastFiredTime { get; internal set; } = double.NegativeInfinity;
    /// <summary>Reload time multiplier while supplying from the main magazine (degraded).</summary>
    public double ReadyRackReloadFactor { get; internal set; } = 2.5;

    public int CrewAlive => Math.Max(0, Definition.CrewTotal - (int)CrewDead);
    public double BuoyancyLossPct => Parts.Values.Sum(p =>
        p.Definition.BuoyancySharePct * (p.Flooded ? 1.0 : p.WaterLevel));
    public bool Alive => KillState == ShipKillState.Alive;
    public bool Lost => KillState != ShipKillState.Alive;

    /// <summary>Propulsion availability 0..1 from engine/boiler/turbine state.</summary>
    public double SpeedFactor
    {
        get
        {
            var propulsion = Parts.Values.Where(p =>
                p.Definition.Kind is PartKind.Engine or PartKind.Boiler or PartKind.Turbine).ToList();
            if (propulsion.Count == 0)
            {
                return 1.0;
            }

            double alive = propulsion.Count(p => !p.Destroyed);
            return alive / propulsion.Count;
        }
    }

    public bool HasHelm => !Parts.Values.Any(p => p.Definition.Kind == PartKind.Steering && p.Destroyed);
    public double PumpCapacityPerSecond => Definition.PumpCapacityPerSecond
        * (1.0 - Parts.Values.Count(p => p.Definition.Kind == PartKind.Pump && p.Destroyed)
               / Math.Max(1.0, Parts.Values.Count(p => p.Definition.Kind == PartKind.Pump)));

    public Ship(ShipDefinition definition, string? instanceKey = null)
    {
        Definition = definition;
        TargetId = "ship:" + definition.Id + (instanceKey is null ? "" : $"#{instanceKey}");
        Sections = definition.HullSections.Select(s => new HullSectionState(s)).ToList();

        foreach (var part in definition.Parts)
        {
            _partsById[part.Id] = new ShipPartState(part);
            if (!_partsBySection.TryGetValue(part.SectionId, out var list))
            {
                _partsBySection[part.SectionId] = list = [];
            }

            list.Add(_partsById[part.Id]);

            if (part.TurretGroup is not null)
            {
                if (!_partsByTurretGroup.TryGetValue(part.TurretGroup, out var group))
                {
                    _partsByTurretGroup[part.TurretGroup] = group = [];
                }

                group.Add(_partsById[part.Id]);
                _readyRacks.TryAdd(part.TurretGroup, definition.FirstStageRoundsPerTurret);
                _resupplyProgress.TryAdd(part.TurretGroup, 0);
            }
        }

        TurretGroups = _readyRacks.Keys.ToList();
    }

    public ShipPartState? PartAt(Vec3 localPoint) =>
        Parts.Values.FirstOrDefault(p => !p.Destroyed && p.Contains(localPoint));

    public HullSectionState? SectionAtX(double xM) =>
        Sections.FirstOrDefault(s =>
            xM >= s.Definition.XMinM && xM <= s.Definition.XMaxM)
        ?? Sections.OrderBy(s => Math.Min(Math.Abs(xM - s.Definition.XMinM), Math.Abs(xM - s.Definition.XMaxM))).First();

    // ------------------------------------------------------------------ damage sink

    public void ApplyDamage(DamageEvent e)
    {
        if (Lost)
        {
            return;
        }

        switch (e.Channel)
        {
            case DamageChannel.Overpressure:
                ApplyOverpressure(e);
                break;

            case DamageChannel.Chemical when e.Radius > 0:
                ApplyRadial(e);
                break;

            default:
                ApplyPoint(e, e.Position, e.Amount);
                break;
        }
    }

    /// <summary>Point damage: the part containing the point takes the full amount.</summary>
    private void ApplyPoint(DamageEvent e, Vec3 localPoint, double amount)
    {
        // Fire events address parts as "ship:<id>/<partId>".
        if (e.TargetId.Contains('/'))
        {
            string partId = e.TargetId[(e.TargetId.IndexOf('/') + 1)..];
            if (_partsById.TryGetValue(partId, out var part))
            {
                DamagePart(part, amount, e.Channel);
            }

            return;
        }

        var hit = PartAt(localPoint);
        if (hit is null)
        {
            // Structure-only impact (outer hull, superstructure): feeds its hull section.
            DamageSection(SectionAtX(localPoint.X), amount);
            return;
        }

        DamagePart(hit, amount, e.Channel);
    }

    /// <summary>Radial (blast) damage distributed over parts inside the radius.</summary>
    private void ApplyRadial(DamageEvent e)
    {
        var affected = Parts.Values
            .Where(p => !p.Destroyed && Vec3.Distance(p.Center, e.Position) <= e.Radius)
            .ToList();
        if (affected.Count == 0)
        {
            return;
        }

        double weightSum = affected.Sum(p => 1.0 / Math.Max(1.0, Vec3.Distance(p.Center, e.Position)));
        foreach (var part in affected)
        {
            double w = 1.0 / Math.Max(1.0, Vec3.Distance(part.Center, e.Position));
            DamagePart(part, e.Amount * w / weightSum, e.Channel);
        }
    }

    /// <summary>Overpressure kills exposed crews only — never hull/module HP (MDR-0005).</summary>
    private void ApplyOverpressure(DamageEvent e)
    {
        foreach (var part in Parts.Values)
        {
            if (!part.Definition.Open || part.Destroyed)
            {
                continue;
            }

            if (Vec3.Distance(part.Center, e.Position) <= e.Radius)
            {
                CrewDead += Math.Min(part.Definition.Crew * (1 - part.CrewDeadFraction), e.Amount / CrewHpPer);
                part.CrewDeadFraction = 1.0; // exposed gun crews are wiped by a nearby burst
            }
        }
    }

    private void DamagePart(ShipPartState part, double amount, DamageChannel channel)
    {
        if (part.Destroyed || amount <= 0)
        {
            return;
        }

        part.Hp -= amount;
        if (part.Definition.Crew > 0)
        {
            CrewDead += Math.Min(amount / CrewHpPer, part.Definition.Crew * (1 - part.CrewDeadFraction));
            part.CrewDeadFraction = Math.Min(1.0, part.CrewDeadFraction + amount / CrewHpPer / Math.Max(1, part.Definition.Crew));
        }

        DamageSection(SectionAtX(part.Center.X), amount * 0.25); // structure spillover

        if (part.Hp <= 0)
        {
            part.Destroyed = true;
            if (part.Definition.Crew > 0)
            {
                CrewDead += part.Definition.Crew * (1 - part.CrewDeadFraction);
                part.CrewDeadFraction = 1.0;
            }

            if (part.Definition.YMinM < 0)
            {
                part.Breached = true; // destroyed below-waterline compartment = breach (MDR-0008)
            }
        }
    }

    private void DamageSection(HullSectionState? section, double amount)
    {
        if (section is null || section.Destroyed)
        {
            return;
        }

        section.Hp -= amount;
        if (section.Hp <= 0)
        {
            section.Destroyed = true;
        }
    }

    // ------------------------------------------------------- unsinkability (MDR-0007)

    public void CheckUnsinkability()
    {
        if (UnsinkabilityLost)
        {
            return; // irreversible (MDR-0007)
        }

        if (Definition.Class.IsCapital())
        {
            int destroyedMid = Sections.Count(s => s.Definition.Role == HullSectionRole.Mid && s.Destroyed);
            if (destroyedMid >= 2)
            {
                UnsinkabilityLost = true;
                UnsinkabilityLostTime ??= null;
            }

            return;
        }

        // Small craft: any destroyed section sinks her.
        UnsinkabilityLost = Sections.Any(s => s.Destroyed);
    }

    // ------------------------------------------------------ first-stage ammo (MDR-0011)

    /// <summary>Returns the current reload factor for the group (1 nominal, higher when resupplying).</summary>
    public double CurrentReloadFactor(SimulationWorld world, string turretGroup)
    {
        UpdateResupply(world);
        return _readyRacks.GetValueOrDefault(turretGroup) > 0 ? 1.0 : ReadyRackReloadFactor;
    }

    public int ReadyRackCount(string turretGroup) => _readyRacks.GetValueOrDefault(turretGroup);

    public void ConsumeReadyRack(SimulationWorld world, string turretGroup)
    {
        LastFiredTime = world.Time;
        if (_readyRacks.TryGetValue(turretGroup, out int rounds) && rounds > 0)
        {
            _readyRacks[turretGroup] = rounds - 1;
        }
    }

    /// <summary>Resupply from the main magazine: ~35 s, paused while firing (MDR-0011).</summary>
    public void UpdateResupply(SimulationWorld world)
    {
        foreach (var group in TurretGroups)
        {
            if (_readyRacks.GetValueOrDefault(group) > 0)
            {
                _resupplyProgress[group] = 0;
                continue;
            }

            if (world.Time - LastFiredTime < 2.0)
            {
                continue; // firing pauses the resupply
            }

            _resupplyProgress[group] += 1.0 / Math.Max(0.1, Definition.ResupplySeconds) * world.FixedDeltaTime;
            if (_resupplyProgress[group] >= 1.0)
            {
                _readyRacks[group] = Definition.FirstStageRoundsPerTurret;
                _resupplyProgress[group] = 0;
            }
        }
    }

    /// <summary>Ready-rack hit: small detonation, permanent degraded reload (MDR-0011).</summary>
    public void DestroyReadyRack(string turretGroup)
    {
        _readyRacks[turretGroup] = 0;
        ReadyRackReloadFactor = 2.5;
    }

    // ------------------------------------------------------- scripting/scene helpers

    /// <summary>Forces crew losses (scripted attrition in scenarios/tests).</summary>
    public void ForceCrewDead(double dead) => CrewDead = Math.Max(CrewDead, dead);

    /// <summary>Forces a hull section to its destroyed state (scripted scenarios/tests).</summary>
    public void ForceSectionDestroyed(string sectionId)
    {
        var section = Sections.First(s => s.Definition.Id == sectionId);
        section.Hp = 0;
        section.Destroyed = true;
    }

    internal void MarkDestroyed(ShipKillState state, string reason, double time)
    {
        if (Lost)
        {
            return;
        }

        KillState = state;
        KillReason = reason;
        DestroyedTime = time;
    }
}
