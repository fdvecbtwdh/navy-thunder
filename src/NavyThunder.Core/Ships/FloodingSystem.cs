using NavyThunder.Core.Damage;
using NavyThunder.Core.Armor;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Model;
using NavyThunder.Core.Fire;
using NavyThunder.Core.World;

namespace NavyThunder.Core.Ships;

/// <summary>
/// Flooding, pumps, buoyancy and list (MDR-0008). Only breaches below the waterline
/// admit water; pumps counter-flow; flooded compartments kill their crew, lose their
/// buoyancy share and drown fires; lateral imbalance produces list.
/// </summary>
public sealed class FloodingSystem : ISimulationSystem
{
    public const string BreachFlowCalibrationId = "flooding_rate_per_m_breach";

    private readonly DamageRegistry _registry;
    private readonly FireSystem? _fire;

    public List<Ship> Ships { get; } = [];

    /// <summary>Water-level fraction added per second per metre of breach radius (approximation).</summary>
    public double FlowPerBreathMeter { get; init; } = 0.08;

    /// <summary>List degrees per unit of lateral flooding imbalance (approximation).</summary>
    public double ListDegPerImbalance { get; init; } = 12.0;

    public string Name => "flooding";

    public FloodingSystem(DamageRegistry registry, FireSystem? fire = null)
    {
        _registry = registry;
        _fire = fire;
    }

    public void Initialize(SimulationWorld world)
    {
    }

    /// <summary>Records a breach on the part containing the point (below waterline only).</summary>
    public void CreateBreach(Ship ship, Vec3 localPoint, double radiusM)
    {
        var part = ship.PartAt(localPoint);
        if (part is null || part.Definition.YMinM >= 0 || part.Destroyed)
        {
            return; // above-waterline damage does not flood
        }

        part.Breached = true;
    }

    public void Update(SimulationWorld world, double deltaTime)
    {
        foreach (var ship in Ships)
        {
            if (ship.Lost)
            {
                continue;
            }

            double pumped = ship.PumpCapacityPerSecond * deltaTime;

            foreach (var part in ship.Parts.Values)
            {
                if (part.Definition.Kind is PartKind.Compartment or PartKind.Magazine or PartKind.Boiler
                    or PartKind.Engine or PartKind.Turbine or PartKind.Steering)
                {
                    // Inflow from below-waterline breaches.
                    if (part.Breached && !part.Flooded && part.Definition.YMinM < 0)
                    {
                        part.WaterLevel = Math.Min(1.0, part.WaterLevel + FlowPerBreathMeter * deltaTime);
                    }

                    // Pumps drain flooded compartments.
                    if (part.WaterLevel > 0 && pumped > 0)
                    {
                        double drain = Math.Min(part.WaterLevel, pumped);
                        part.WaterLevel -= drain;
                        pumped -= drain;
                    }
                }

                if (part.Flooded && part.Definition.Crew > 0 && part.CrewDeadFraction < 1.0)
                {
                    // A flooded compartment loses its crew (MDR-0006).
                    ship.CrewDead += part.Definition.Crew * (1 - part.CrewDeadFraction);
                    part.CrewDeadFraction = 1.0;
                }
            }

            // Lateral imbalance -> list.
            double imbalance = ship.Parts.Values.Sum(p =>
                (p.Breached ? p.WaterLevel : 0) * Math.Sign(p.Center.Z));
            ship.ListDeg = imbalance * ListDegPerImbalance;

            if (_fire is not null)
            {
                foreach (var part in ship.Parts.Values)
                {
                    if (part.Flooded)
                    {
                        _fire.FloodHost(world, $"{ship.TargetId}/{part.Definition.Id}");
                    }
                }
            }
        }
    }
}
