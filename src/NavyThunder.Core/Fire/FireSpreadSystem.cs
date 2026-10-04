using NavyThunder.Core.Model;
using NavyThunder.Core.Ships;
using NavyThunder.Core.World;

namespace NavyThunder.Core.Fire;

/// <summary>
/// Phase 04 fire spread along the ammunition chain (PHASE_04 P04-6, W5 + NavalArt):
/// a burning turret rolls to ignite the hoist (elevator) of the same turret group,
/// a burning hoist rolls to ignite the magazine — the shell path fire follows the
/// ammunition path. Deterministic via the "fire_spread" RNG stream. The final bang
/// comes from the existing chain: a destroyed magazine detonates (KillAdjudicator).
/// </summary>
public sealed class FireSpreadSystem : ISimulationSystem
{
    private readonly FireSystem _fire;
    private List<Ship> _ships;

    /// <summary>Seconds between spread rolls per burning host (W5 time feel: the chain
    /// from turret fire to magazine flash is a minutes-scale race against the DC crew).</summary>
    public double RollIntervalS { get; init; } = 30.0;

    public double TurretToHoistChance { get; init; } = 0.25;
    public double HoistToMagazineChance { get; init; } = 0.30;

    private readonly Dictionary<string, double> _nextRollAt = [];
    private long _lastFireCount;

    public string Name => "fire_spread";

    public FireSpreadSystem(FireSystem fire, List<Ship> ships)
    {
        _fire = fire;
        _ships = ships;
    }

    /// <summary>Ships live in a runner-owned list; re-point when wiring order differs.</summary>
    public List<Ship> Ships { set => _ships = value; }

    public void Initialize(SimulationWorld world)
    {
    }

    public void Update(SimulationWorld world, double deltaTime)
    {
        // Fires are keyed "<shipTargetId>/<partId>"; map them to ship parts once per tick.
        // Snapshot: spread rolls can ignite new hosts during enumeration.
        foreach (var fire in _fire.Fires.ToList())
        {
            if (!fire.Active)
            {
                continue;
            }

            int slash = fire.HostId.LastIndexOf('/');
            if (slash < 0)
            {
                continue;
            }

            string shipId = fire.HostId[..slash];
            var ship = _ships.FirstOrDefault(s => s.TargetId == shipId);
            if (ship is null)
            {
                continue;
            }

            var part = ship.Parts.Values.FirstOrDefault(p => p.Definition.Id == fire.HostId[(slash + 1)..]);
            if (part is null)
            {
                continue;
            }

            string key = fire.Id;
            if (!_nextRollAt.TryGetValue(key, out double next))
            {
                _nextRollAt[key] = world.Time + RollIntervalS;
                continue;
            }

            if (world.Time < next)
            {
                continue;
            }

            _nextRollAt[key] = world.Time + RollIntervalS;
            SpreadFrom(world, ship, part);
        }

        // Drop roll timers for dead fires (bounded bookkeeping).
        if (_fire.Fires.Count != _lastFireCount)
        {
            _lastFireCount = _fire.Fires.Count;
            var alive = _fire.Fires.Select(f => f.Id).ToHashSet();
            foreach (var dead in _nextRollAt.Keys.Where(k => !alive.Contains(k)).ToList())
            {
                _nextRollAt.Remove(dead);
            }
        }
    }

    private void SpreadFrom(SimulationWorld world, Ship ship, ShipPartState burning)
    {
        switch (burning.Definition.Kind)
        {
            case PartKind.Turret when burning.Definition.TurretGroup is { } group:
                TrySpread(world, ship, group, PartKind.Hoist, TurretToHoistChance);
                break;

            case PartKind.Hoist when burning.Definition.TurretGroup is { } group2:
                TrySpread(world, ship, group2, PartKind.Magazine, HoistToMagazineChance);
                break;
        }
    }

    private void TrySpread(SimulationWorld world, Ship ship, string group, PartKind kind, double chance)
    {
        foreach (var target in ship.Parts.Values.Where(p =>
                     p.Definition.TurretGroup == group && p.Definition.Kind == kind && !p.Destroyed))
        {
            // Down the elevator: the fire follows the ammunition path (W5).
            _fire.TryIgnite(world, $"{ship.TargetId}/{target.Definition.Id}", kind.ToString().ToLower(),
                target.Center, sourceMultiplier: chance / _fire.TryIgniteBaseProbability);
        }
    }
}
