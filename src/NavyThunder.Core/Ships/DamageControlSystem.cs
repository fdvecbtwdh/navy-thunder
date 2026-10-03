using NavyThunder.Core.Fire;
using NavyThunder.Core.Model;
using NavyThunder.Core.World;

namespace NavyThunder.Core.Ships;

public enum DcFlow
{
    Repair,        // patch breaches (5-20 s class, MDR-0007/0008)
    Extinguishing, // put out fires
    Unwatering,    // boost pumping
}

public enum DcMode
{
    /// <summary>Spearhead-style automatic flows with player-set priority (default).</summary>
    Automatic,
    /// <summary>Legacy manual three-action mode (WT keeps it as a switch).</summary>
    Manual,
}

/// <summary>
/// Damage control, both WT modes (MDR-0009). Automatic: the three flows run
/// concurrently, ordered by the player's priority list. Manual: one action at a time.
/// Nothing progresses when free crew is below the repair threshold. A per-ship DC
/// coefficient (size/crew/generation) scales flow speed.
/// </summary>
public sealed class DamageControlSystem : ISimulationSystem
{
    public List<Ship> Ships { get; } = [];
    public FireSystem? Fire { get; init; }
    public FloodingSystem? Flooding { get; init; }

    public DcMode Mode { get; set; } = DcMode.Automatic;

    /// <summary>Priority order for the automatic mode (default: repair > extinguish > unwater).</summary>
    public List<DcFlow> Priority { get; set; } = [DcFlow.Repair, DcFlow.Extinguishing, DcFlow.Unwatering];

    /// <summary>Active manual flow (Manual mode only).</summary>
    public DcFlow? ManualFlow { get; set; }

    /// <summary>
    /// Per-ship damage-control orders (Phase 04, PHASE_04 P04-6): the SAME interface
    /// serves the player and the AI — one call sets mode/priority for one ship, falling
    /// back to the fleet-wide defaults when the ship has no override.
    /// </summary>
    private readonly Dictionary<string, (DcMode Mode, List<DcFlow> Priority)> _orders = [];

    public void SetOrders(string shipTargetId, DcMode? mode = null, IReadOnlyList<DcFlow>? priority = null)
    {
        var existing = _orders.GetValueOrDefault(shipTargetId, (Mode, Priority));
        _orders[shipTargetId] = (mode ?? existing.Mode, priority is { Count: > 0 } ? [.. priority] : existing.Priority);
    }

    public (DcMode Mode, IReadOnlyList<DcFlow> Priority) GetOrders(string shipTargetId)
    {
        var o = _orders.GetValueOrDefault(shipTargetId);
        return (o.Mode, o.Priority is { Count: > 0 } p ? p : Priority);
    }

    /// <summary>Patch time fallback (s) for legacy callers; per-class tiers live in
    /// FloodingSystem.ClassSpec (W5 5-20 s: shell holes fast, torpedo holes slow).</summary>
    public double BreachPatchSeconds { get; init; } = 12.0;

    public double ExtinguishSeconds { get; init; } = 10.0;

    public double UnwaterBoost { get; init; } = 1.5;

    private readonly DamageControlFireBridge _fireBridge = new();

    public string Name => "damage_control";

    public DamageControlSystem()
    {
    }

    public void Initialize(SimulationWorld world)
    {
    }

    /// <summary>DC coefficient: bigger crews/newer generations handle damage slightly better.</summary>
    private static double DcCoefficient(Ship ship) =>
        1.0 / (1.0 + 0.15 * (ship.Definition.DcGeneration - 1));

    public void Update(SimulationWorld world, double deltaTime)
    {
        foreach (var ship in Ships)
        {
            ship.UpdateResupply(world); // first-stage replenishment runs every tick (MDR-0011)

            if (ship.Lost || ship.CrewAlive <= ship.Definition.CrewRepairThreshold)
            {
                continue; // no damage control below the repair threshold (MDR-0006)
            }

            double speed = DcCoefficient(ship);
            var (shipMode, shipPriority) = GetOrders(ship.TargetId);
            var flows = shipMode == DcMode.Automatic
                ? shipPriority
                : ManualFlow is { } m ? [m] : [];

            foreach (var flow in flows)
            {
                switch (flow)
                {
                    case DcFlow.Repair:
                        PatchBreaches(ship, deltaTime * speed);
                        break;
                    case DcFlow.Extinguishing:
                        _ = Fire is not null && _fireBridge.ExtinguishProgress(Fire, world, ship, ExtinguishSeconds / speed, deltaTime);
                        break;
                    case DcFlow.Unwatering:
                        Unwater(ship, deltaTime * speed * UnwaterBoost);
                        break;
                }
            }
        }
    }

    /// <summary>Repair time for a destroyed steering gear (W2: the rudder is repairable,
    /// not lost forever; DC work restores it).</summary>
    public double SteeringRepairSeconds { get; init; } = 20.0;

    private void PatchBreaches(Ship ship, double work)
    {
        // Accumulate repair work per breached part via its WaterLevel-independent counter.
        foreach (var part in ship.Parts.Values)
        {
            if (part.Breached && !part.Destroyed)
            {
                part.RepairWork += work;
                double needed = FloodingSystem.ClassSpec(part.BreachClass ?? FloodingSystem.BreachClass.BlastMedium).PatchSeconds;
                if (part.RepairWork >= needed)
                {
                    part.Breached = false;
                    part.RepairWork = 0;
                }
            }

            // Phase 04 (W2): a destroyed steering gear is repairable by the repair flow.
            if (part.Destroyed && part.Definition.Kind == PartKind.Steering)
            {
                part.RepairWork += work;
                if (part.RepairWork >= SteeringRepairSeconds)
                {
                    part.Destroyed = false;
                    part.Hp = Math.Max(part.Hp, part.Definition.Hp * 0.3);
                    part.RepairWork = 0;
                    part.Breached = false;
                }
            }
        }
    }

    private void Unwater(Ship ship, double work)
    {
        // Extra pumping beyond the passive pump outfit.
        double extra = ship.Definition.PumpCapacityPerSecond * (work / Math.Max(1e-6, work + 1)) * 0.02;
        foreach (var part in ship.Parts.Values)
        {
            if (part.WaterLevel > 0)
            {
                part.WaterLevel = Math.Max(0, part.WaterLevel - extra);
            }
        }
    }
}

/// <summary>Per-instance extinguish progress (never static: battles must not share state).</summary>
public sealed class DamageControlFireBridge
{
    private readonly Dictionary<string, double> _progress = [];

    public bool ExtinguishProgress(FireSystem fire, SimulationWorld world, Ship ship, double durationS, double deltaTime)
    {
        string key = ship.TargetId;
        if (!fire.Fires.Any(f => f.HostId.StartsWith(key, StringComparison.Ordinal) && f.Active))
        {
            _progress.Remove(key);
            return false;
        }

        double value = _progress.GetValueOrDefault(key) + deltaTime / Math.Max(0.1, durationS);
        if (value >= 1.0)
        {
            foreach (var f in fire.Fires.Where(f => f.HostId.StartsWith(key, StringComparison.Ordinal) && f.Active))
            {
                fire.Extinguish(world, f.HostId);
            }

            _progress[key] = 0;
            return true;
        }

        _progress[key] = value;
        return false;
    }
}
