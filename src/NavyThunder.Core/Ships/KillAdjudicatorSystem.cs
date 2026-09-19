using NavyThunder.Core.Damage;
using NavyThunder.Core.Armor;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Model;
using NavyThunder.Core.World;

namespace NavyThunder.Core.Ships;

public sealed record ShipDestroyed : SimulationEvent
{
    public string ShipId { get; init; } = "";
    public required ShipKillState State { get; init; }
    public string Reason { get; init; } = "";
    public override string Kind => "ship_destroyed";
}

public sealed record MagazineDetonation : SimulationEvent
{
    public string ShipId { get; init; } = "";
    public string MagazinePartId { get; init; } = "";
    public Vec3 Position { get; init; }

    /// <summary>Detonation power scales with the remaining ammunition (MDR-0011).</summary>
    public int RoundsRemaining { get; init; }
    public double TntEquivalentKg { get; init; }
    public override string Kind => "magazine_detonation";
}

/// <summary>
/// The multi-channel kill adjudicator (MDR-0011/kill rules):
///   1. crew annihilated -> Destroyed
///   2. crew below the survival threshold -> Scuttled
///   3. buoyancy loss >= 100 % -> Foundered
///   4. unsinkability lost -> forced flooding, Foundered shortly after
///   5. |list| >= capsize angle -> Capsized
///   6. destroyed magazine -> detonation proportional to remaining ammo -> Destroyed
/// </summary>
public sealed class KillAdjudicatorSystem : ISimulationSystem
{
    private readonly DamageRegistry _registry;

    /// <summary>TNT equivalent per ready round in a magazine (approximation, MDR-0011).</summary>
    public double TntPerRoundKg { get; init; } = 12.0;

    /// <summary>Forced flooding rate once unsinkability is lost (fraction/s).</summary>
    public double ForcedFloodingRate { get; init; } = 0.05;

    /// <summary>Seconds between losing unsinkability and foundering (MDR-0007).</summary>
    public double UnsinkabilityGraceS { get; init; } = 180.0;

    public List<Ship> Ships { get; } = [];

    public string Name => "kill_adjudication";

    public KillAdjudicatorSystem(DamageRegistry registry) => _registry = registry;

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

            ship.CheckUnsinkability();

            // 6. Magazine detonation (non-linear module: destroyed -> "normally detonates").
            var magazine = ship.Parts.Values.FirstOrDefault(p =>
                p.Definition.Kind == PartKind.Magazine && p.Destroyed && !p.MagazineExploded);
            if (magazine is not null)
            {
                magazine.MagazineExploded = true;
                int rounds = EstimateMagazineRounds(ship);
                double tnt = rounds * TntPerRoundKg;
                world.Record(new MagazineDetonation
                {
                    ShipId = ship.TargetId,
                    MagazinePartId = magazine.Definition.Id,
                    Position = magazine.Center,
                    RoundsRemaining = rounds,
                    TntEquivalentKg = tnt,
                });

                // The blast wipes the ship's own internals.
                _registry.Apply(new DamageEvent
                {
                    Channel = DamageChannel.Chemical,
                    SourceId = "magazine",
                    TargetId = ship.TargetId,
                    Position = magazine.Center,
                    Radius = ship.Definition.LengthM,
                    Amount = magazine.Definition.Hp * 10 + 5000,
                    Tick = world.TickIndex,
                    Time = world.Time,
                });

                // A magazine detonation is immediately fatal (WT: "will normally detonate").
                ship.MarkDestroyed(ShipKillState.Destroyed, "magazine_detonation", world.Time);
            }

            // 4. Unsinkability lost: irreversible flooding, no patching can save her.
            //    (WT: she floods out and sinks - only the time is negotiable.)
            if (ship.UnsinkabilityLost)
            {
                ship.UnsinkabilityLostTime ??= world.Time;
                foreach (var part in ship.Parts.Values)
                {
                    if (part.Definition.YMinM < 0 && !part.Flooded)
                    {
                        part.Breached = true;
                        part.WaterLevel = Math.Min(1.0, part.WaterLevel + ForcedFloodingRate * deltaTime);
                    }
                }

                if (world.Time - ship.UnsinkabilityLostTime.Value >= UnsinkabilityGraceS)
                {
                    ship.MarkDestroyed(ShipKillState.Foundered, "unsinkability_lost", world.Time);
                }
            }

            if (ship.Lost)
            {
                world.Record(new ShipDestroyed
                {
                    ShipId = ship.TargetId,
                    State = ship.KillState,
                    Reason = ship.KillReason ?? "",
                });
                continue;
            }

            if (ship.BuoyancyLossPct >= 100.0)
            {
                ship.MarkDestroyed(ShipKillState.Foundered, "buoyancy_lost", world.Time);
            }
            else if (Math.Abs(ship.ListDeg) >= ship.Definition.CapsizeAngleDeg)
            {
                ship.MarkDestroyed(ShipKillState.Capsized, "capsize", world.Time);
            }
            else if (ship.CrewAlive <= 0)
            {
                ship.MarkDestroyed(ShipKillState.Destroyed, "crew_annihilated", world.Time);
            }
            else if (ship.CrewAlive <= ship.Definition.CrewSurviveThreshold)
            {
                ship.MarkDestroyed(ShipKillState.Scuttled, "below_survival_crew", world.Time);
            }

            if (ship.Lost)
            {
                world.Record(new ShipDestroyed
                {
                    ShipId = ship.TargetId,
                    State = ship.KillState,
                    Reason = ship.KillReason ?? "",
                });
            }
        }
    }

    private static int EstimateMagazineRounds(Ship ship)
    {
        // First-order estimate from magazine count when no authored ammo counts exist.
        int magazines = Math.Max(1, ship.Parts.Values.Count(p => p.Definition.Kind == PartKind.Magazine));
        return 40 * magazines;
    }
}
