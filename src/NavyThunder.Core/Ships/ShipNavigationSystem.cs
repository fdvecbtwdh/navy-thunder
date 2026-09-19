using NavyThunder.Core.Armor;
using NavyThunder.Core.World;

namespace NavyThunder.Core.Ships;

public sealed record Team
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    public bool IsHostileTo(Team other) => !ReferenceEquals(this, other);
}

/// <summary>
/// Ship navigation (R0.2): throttle/rudder commands integrate into speed and heading;
/// engine damage caps attainable speed (SpeedFactor from the damage model writes back
/// into propulsion exactly like the flight-model feedback loop does for aircraft).
/// </summary>
public sealed class ShipNavigationSystem : ISimulationSystem
{
    private readonly List<(Ship Ship, ArmorTarget Armor)> _tracked = [];

    public List<Ship> Ships { get; } = [];

    public string Name => "navigation";

    /// <summary>Registers the armor target that must follow the ship as she moves.</summary>
    public void Track(Ship ship, ArmorTarget armor) => _tracked.Add((ship, armor));

    public void Initialize(SimulationWorld world)
    {
    }

    public void Update(SimulationWorld world, double deltaTime)
    {
        foreach (var ship in Ships)
        {
            if (ship.Lost)
            {
                continue;
            }

            double maxSpeed = ship.Definition.MaxSpeedKnots
                              * Math.Clamp(ship.ThrottleCommand, 0.0, 1.0)
                              * ship.SpeedFactor;
            double accel = ship.Definition.MaxSpeedKnots * ship.Definition.AccelerationFactor * deltaTime;
            ship.SpeedKnots = Math.Abs(ship.SpeedKnots - maxSpeed) <= accel
                ? maxSpeed
                : ship.SpeedKnots + Math.Sign(maxSpeed - ship.SpeedKnots) * accel;

            // Rudder authority scales with speed through the water (no steering in stop);
            // a destroyed steering gear freezes the rudder but the ship keeps way on.
            double speedFactor = Math.Clamp(ship.SpeedKnots / Math.Max(1.0, ship.Definition.MaxSpeedKnots * 0.4), 0.0, 1.0);
            double rudder = ship.HasHelm ? ship.RudderCommand : 0.0;
            double turn = rudder * ship.Definition.TurnRateDegPerS * speedFactor * deltaTime;
            ship.HeadingDeg = (ship.HeadingDeg + turn + 360.0) % 360.0;

            double rad = ship.HeadingDeg * Math.PI / 180.0;
            double metersPerSecond = ship.SpeedKnots * 0.514444;
            ship.WorldPosition += new Mathematics.Vec3(
                Math.Sin(rad) * metersPerSecond * deltaTime,
                0,
                Math.Cos(rad) * metersPerSecond * deltaTime);
        }

        // Armor targets follow their hulls: hit detection always uses live positions.
        foreach (var (ship, armor) in _tracked)
        {
            foreach (var plate in armor.Plates)
            {
                plate.Translate(ship.WorldPosition);
            }
        }
    }
}
