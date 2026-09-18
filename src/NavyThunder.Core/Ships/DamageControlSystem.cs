using NavyThunder.Core.Fire;
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

    /// <summary>Patch time per breach (s) — WT Leviathans: 5-20 s for shell holes.</summary>
    public double BreachPatchSeconds { get; init; } = 12.0;

    public double ExtinguishSeconds { get; init; } = 10.0;

    public double UnwaterBoost { get; init; } = 1.5;

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
            if (ship.Lost || ship.CrewAlive <= ship.Definition.CrewRepairThreshold)
            {
                continue; // no damage control below the repair threshold (MDR-0006)
            }

            double speed = DcCoefficient(ship);
            var flows = Mode == DcMode.Automatic
                ? Priority
                : ManualFlow is { } m ? [m] : [];

            foreach (var flow in flows)
            {
                switch (flow)
                {
                    case DcFlow.Repair:
                        PatchBreaches(ship, deltaTime * speed);
                        break;
                    case DcFlow.Extinguishing:
                        Fire?.ExtinguishProgress(world, ship, ExtinguishSeconds / speed, deltaTime);
                        break;
                    case DcFlow.Unwatering:
                        Unwater(ship, deltaTime * speed * UnwaterBoost);
                        break;
                }
            }
        }
    }

    private void PatchBreaches(Ship ship, double work)
    {
        // Accumulate repair work per breached part via its WaterLevel-independent counter.
        foreach (var part in ship.Parts.Values)
        {
            if (!part.Breached || part.Destroyed)
            {
                continue;
            }

            part.RepairWork += work;
            if (part.RepairWork >= BreachPatchSeconds)
            {
                part.Breached = false;
                part.RepairWork = 0;
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

/// <summary>Extension keeps FireSystem decoupled from ships.</summary>
public static class DamageControlFireBridge
{
    private static readonly Dictionary<string, double> Progress = [];

    public static void ExtinguishProgress(this FireSystem fire, SimulationWorld world, Ship ship, double durationS, double deltaTime)
    {
        string key = ship.TargetId;
        if (!fire.Fires.Any(f => f.HostId.StartsWith(key, StringComparison.Ordinal) && f.Active))
        {
            Progress.Remove(key);
            return;
        }

        double value = Progress.GetValueOrDefault(key) + deltaTime / Math.Max(0.1, durationS);
        if (value >= 1.0)
        {
            foreach (var f in fire.Fires.Where(f => f.HostId.StartsWith(key, StringComparison.Ordinal) && f.Active))
            {
                fire.Extinguish(world, f.HostId);
            }

            Progress[key] = 0;
        }
        else
        {
            Progress[key] = value;
        }
    }
}
