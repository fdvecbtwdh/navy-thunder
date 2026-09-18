using NavyThunder.Core.Damage;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Model;
using NavyThunder.Core.World;

namespace NavyThunder.Core.Aviation;

public enum AircraftState
{
    Airborne,
    /// <summary>Severe damage (2024-02 rules): uncontrollable, 80 % kill credit, can be finished off.</summary>
    SeverelyDamaged,
    Destroyed,
}

/// <summary>Per-tick flight-model feedback derived from damage (MDR-0013 FM write-back).</summary>
public readonly record struct FlightFeedback(
    double LiftMultiplier,
    double DragAdd,
    double ThrustFactor,
    double PitchAuthority,
    double RollAuthority,
    double YawAuthority,
    double CriticalG);

/// <summary>
/// The aircraft damage model (MDR-0013): segmented wings with spars and a Critical-G
/// limit, control surfaces/cables per axis, hydraulics, self-sealing fuel tanks with
/// leaks and fires, oil, radiators with progressive overheat, engine, pilot and ammo
/// rack (both instantly fatal), and the damage-to-flight-model feedback loop.
/// Pure state — world consequences run in <see cref="AircraftAdjudicatorSystem"/>.
/// </summary>
public sealed class Aircraft : Entity, IDamageSink
{
    private readonly Dictionary<string, AircraftPartState> _partsById = [];

    public AircraftDefinition Definition { get; }
    public string TargetId { get; }
    public double FuelKg { get; internal set; }
    public double OilKg { get; internal set; }
    public double CoolantKg { get; internal set; }
    public double CurrentG { get; set; } = 1.0;

    /// <summary>Set by the fire system when a fuel/oil fire burns this airframe.</summary>
    public bool OnFire { get; internal set; }

    public AircraftState State { get; internal set; } = AircraftState.Airborne;
    public string? LossReason { get; internal set; }
    public double? DestroyedTime { get; internal set; }

    public IReadOnlyDictionary<string, AircraftPartState> Parts => _partsById;
    public bool Alive => State != AircraftState.Destroyed;

    public Aircraft(AircraftDefinition definition)
    {
        Definition = definition;
        TargetId = $"aircraft:{definition.Id}";
        FuelKg = definition.FuelKg;
        OilKg = 60;
        CoolantKg = 90;
        foreach (var part in definition.Parts)
        {
            _partsById[part.Id] = new AircraftPartState(part);
        }
    }

    public AircraftPartState? PartAt(Vec3 localPoint)
    {
        AircraftPartState? best = null;
        double bestDistance = 0.75; // point damage near a box edge still attributes inward
        foreach (var part in _partsById.Values)
        {
            if (part.Destroyed || !part.Contains(localPoint))
            {
                continue;
            }

            double d = Vec3.Distance(part.Center, localPoint);
            if (d < bestDistance || best is null)
            {
                best = part;
                bestDistance = d;
            }
        }

        return best;
    }

    public void ApplyDamage(DamageEvent e)
    {
        if (!Alive)
        {
            return;
        }

        var part = PartAt(e.Position);
        if (part is null)
        {
            return;
        }

        DamagePart(part, e.Amount);
    }

    internal void DamagePart(AircraftPartState part, double amount)
    {
        if (part.Destroyed || amount <= 0)
        {
            return;
        }

        part.Hp -= amount;
        if (part.Hp <= 0)
        {
            part.Hp = 0;
            part.Destroyed = true;

            switch (part.Definition.Kind)
            {
                case AircraftPartKind.Pilot:
                    Kill("pilot_killed");
                    break;
                case AircraftPartKind.AmmoRack:
                    Kill("ammo_rack_detonation");
                    break;
                case AircraftPartKind.FuelTank when !part.Definition.SelfSealing:
                    Kill("fuel_tank_explosion");
                    break;
            }
        }
    }

    internal void Kill(string reason, double time = 0)
    {
        if (!Alive)
        {
            return;
        }

        State = AircraftState.Destroyed;
        LossReason = reason;
        DestroyedTime = time;
    }

    /// <summary>Wing-spar health across both wings (0..1).</summary>
    public double SparHealth()
    {
        var spars = _partsById.Values
            .Where(p => p.Definition.Kind == AircraftPartKind.WingSpar)
            .ToList();
        return spars.Count == 0 ? 1.0 : spars.Average(p => p.Hp / p.Definition.Hp);
    }

    public bool WingDetached()
    {
        return _partsById.Values.Any(p =>
            p.Definition.Kind == AircraftPartKind.WingSpar && p.Destroyed);
    }

    public FlightFeedback ComputeFeedback()
    {
        double spar = SparHealth();
        double criticalG = Definition.DesignG * (0.45 + 0.55 * spar);

        // Lift from surviving wing surface; drag rises with holes and missing parts.
        var wings = _partsById.Values.Where(p =>
            p.Definition.Section is AircraftSection.LeftWing or AircraftSection.RightWing
            && p.Definition.Kind is AircraftPartKind.Airframe or AircraftPartKind.WingSpar).ToList();
        double wingHealth = wings.Count == 0 ? 1.0 : wings.Average(p => p.Hp / p.Definition.Hp);

        int destroyed = _partsById.Values.Count(p => p.Destroyed);
        double dragAdd = 0.06 * destroyed + (OnFire ? 0.15 : 0.0);

        var engine = _partsById.Values.FirstOrDefault(p => p.Definition.Kind == AircraftPartKind.Engine);
        double thrust = engine is { Destroyed: false } ? 1.0 : 0.0;
        var radiator = _partsById.Values.FirstOrDefault(p => p.Definition.Kind == AircraftPartKind.Radiator);
        if (engine is { Destroyed: false } && radiator is not null)
        {
            // Overheating: radiator damage -> progressive thrust loss (MDR-0013).
            double coolant = Math.Clamp(radiator.Hp / Math.Max(1.0, radiator.Definition.Hp), 0.0, 1.0);
            thrust *= Math.Clamp(0.25 + 0.75 * coolant, 0.25, 1.0);
        }

        double Axis(AircraftPartKind kind, AircraftSection section)
        {
            double best = 1.0;
            foreach (var p in _partsById.Values)
            {
                if (p.Definition.Kind != kind)
                {
                    continue;
                }

                double health = p.Destroyed ? 0.0 : p.Hp / p.Definition.Hp;
                if (p.Definition.Section == section || kind == AircraftPartKind.ControlCable)
                {
                    best = Math.Min(best, health);
                }
            }

            return best;
        }

        var pitch = Math.Min(Axis(AircraftPartKind.ControlSurface, AircraftSection.Tail),
            Math.Min(Axis(AircraftPartKind.ControlCable, AircraftSection.Tail), 1.0));
        var roll = Math.Min(
            Axis(AircraftPartKind.ControlSurface, AircraftSection.LeftWing),
            Axis(AircraftPartKind.ControlSurface, AircraftSection.RightWing));
        var yaw = Axis(AircraftPartKind.ControlSurface, AircraftSection.Tail);

        return new FlightFeedback(
            LiftMultiplier: Math.Clamp(wingHealth, 0.05, 1.0),
            DragAdd: dragAdd,
            ThrustFactor: thrust,
            PitchAuthority: pitch,
            RollAuthority: roll,
            YawAuthority: yaw,
            CriticalG: criticalG);
    }
}

public sealed class AircraftPartState
{
    public AircraftPartDefinition Definition { get; }
    public double Hp { get; internal set; }
    public bool Destroyed { get; internal set; }

    public AircraftPartState(AircraftPartDefinition definition)
    {
        Definition = definition;
        Hp = definition.Hp;
    }

    public Vec3 Center => new(
        (Definition.XMinM + Definition.XMaxM) / 2,
        (Definition.YMinM + Definition.YMaxM) / 2,
        (Definition.ZMinM + Definition.ZMaxM) / 2);

    public bool Contains(Vec3 p) =>
        p.X >= Definition.XMinM && p.X <= Definition.XMaxM
        && p.Y >= Definition.YMinM && p.Y <= Definition.YMaxM
        && p.Z >= Definition.ZMinM && p.Z <= Definition.ZMaxM;
}
