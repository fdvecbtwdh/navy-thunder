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
/// Phase 01: the hull frame lives on the ship (<see cref="Ship.WorldTransform"/>) —
/// armor plates are ship-local and follow through the transform, not by translation.
/// </summary>
public sealed class ShipNavigationSystem : ISimulationSystem
{
    public List<Ship> Ships { get; } = [];

    public string Name => "navigation";

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

            // Rudder authority scales with speed through the water (no steering in stop).
            // Phase 04 (W2): a destroyed steering gear leaves the rudder STUCK at its last
            // ordered position — the ship yaws on (drift) instead of straightening out;
            // damage control can repair the gear (DamageControlSystem).
            double speedFactor = Math.Clamp(ship.SpeedKnots / Math.Max(1.0, ship.Definition.MaxSpeedKnots * 0.4), 0.0, 1.0);
            if (ship.HasHelm)
            {
                ship.StuckRudder = ship.RudderCommand;
            }
            double rudder = ship.HasHelm ? ship.RudderCommand : ship.StuckRudder;
            double turn = rudder * ship.Definition.TurnRateDegPerS * speedFactor * deltaTime;
            ship.HeadingDeg = (ship.HeadingDeg + turn + 360.0) % 360.0;

            double rad = ship.HeadingDeg * Math.PI / 180.0;
            double metersPerSecond = ship.SpeedKnots * 0.514444;
            ship.WorldPosition += new Mathematics.Vec3(
                Math.Sin(rad) * metersPerSecond * deltaTime,
                0,
                Math.Cos(rad) * metersPerSecond * deltaTime);
        }
    }
}
