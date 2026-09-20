using NavyThunder.Core.Damage;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Ships;
using NavyThunder.Core.World;

namespace NavyThunder.Core.Battle;

/// <summary>Victory by annihilation, with a time limit fallback (draw by surviving tonnage).</summary>
public sealed record VictoryConditions
{
    public double TimeLimitS { get; init; } = 1800;
}

public enum BattleResult
{
    Running,
    TeamWin,
    Draw,
}

public sealed record BattleEnded : SimulationEvent
{
    public string? WinnerTeamId { get; init; }
    public required BattleResult Result { get; init; }
    public string Reason { get; init; } = "";
    public override string Kind => "battle_ended";
}

/// <summary>
/// Battle framework (R0.7): team rosters, victory adjudication (annihilation / time
/// limit) and the end-of-battle transition. Runs last in the system order.
/// </summary>
public sealed class BattleSystem : ISimulationSystem
{
    public List<Team> Teams { get; } = [];
    public List<Ship> Ships { get; } = [];
    public VictoryConditions Victory { get; init; } = new();

    public BattleResult Result { get; private set; } = BattleResult.Running;
    public string? WinnerTeamId { get; private set; }
    public double DurationS { get; private set; }

    /// <summary>Incremental battle aggregates (trim-safe; survive event-log trimming).</summary>
    public int GunsFired { get; private set; }
    public int MagazineDetonations { get; private set; }
    public int TorpedoHits { get; private set; }
    public int ShipsLost { get; private set; }

    public string Name => "battle";

    private long _lastSeq;

    public BattleSystem()
    {
    }

    public void Initialize(SimulationWorld world)
    {
    }

    public void Update(SimulationWorld world, double deltaTime)
    {
        foreach (var e in world.Events.After(_lastSeq))
        {
            switch (e)
            {
                case Ships.GunFired g:
                    GunsFired += g.Shells;
                    break;
                case MagazineDetonation:
                    MagazineDetonations++;
                    break;
                case Torpedoes.TorpedoHit:
                    TorpedoHits++;
                    break;
                case ShipDestroyed:
                    ShipsLost++;
                    break;
            }
        }

        _lastSeq = world.Events.TotalRecorded;

        if (Result != BattleResult.Running)
        {
            return;
        }

        DurationS = world.Time;

        foreach (var team in Teams)
        {
            var enemies = Ships.Where(s => s.Team is not null && s.Team.Id != team.Id).ToList();
            var hostilesAlive = enemies.Count(s => !s.Lost);
            if (enemies.Count > 0 && hostilesAlive == 0)
            {
                Finish(world, BattleResult.TeamWin, team.Id, "annihilation");
                return;
            }

            var ownAlive = Ships.Count(s => s.Team?.Id == team.Id && !s.Lost);
            var ownTotal = Ships.Count(s => s.Team?.Id == team.Id);
            if (ownTotal > 0 && ownAlive == 0)
            {
                var winner = Teams.FirstOrDefault(t => t.Id != team.Id);
                Finish(world, BattleResult.TeamWin, winner?.Id, "annihilation");
                return;
            }
        }

        if (world.Time >= Victory.TimeLimitS)
        {
            Finish(world, BattleResult.Draw, null, "time_limit");
        }
    }

    private void Finish(SimulationWorld world, BattleResult result, string? winnerTeamId, string reason)
    {
        Result = result;
        WinnerTeamId = winnerTeamId;
        world.Record(new BattleEnded
        {
            WinnerTeamId = winnerTeamId,
            Result = result,
            Reason = reason,
        });
    }
}

/// <summary>End-of-battle summary assembled from the event log and damage ledger.</summary>
public static class BattleReportGenerator
{
    public static Dictionary<string, object?> Generate(
        SimulationWorld world,
        IReadOnlyList<Ship> ships,
        BattleSystem battle)
    {
        var battleEnded = world.Events.Of<BattleEnded>().LastOrDefault();

        var shipSummaries = ships.Select(ship =>
        {
            return new Dictionary<string, object?>
            {
                ["ship"] = ship.TargetId,
                ["team"] = ship.Team?.Id,
                ["state"] = ship.KillState.ToString(),
                ["lostAtS"] = ship.DestroyedTime is null ? null : Math.Round(ship.DestroyedTime.Value, 1),
                ["reason"] = ship.KillReason,
                ["crewAlive"] = ship.CrewAlive,
                ["damageTaken"] = Math.Round(ship.DamageTaken, 1),
                ["hits"] = ship.HitsTaken,
                ["sections"] = ship.Sections.Select(sec => new Dictionary<string, object?>
                {
                    ["id"] = sec.Definition.Id,
                    ["hpFrac"] = Math.Round(sec.Hp / Math.Max(1.0, sec.Definition.Hp), 2),
                    ["destroyed"] = sec.Destroyed,
                }).ToList(),
                ["partsDestroyed"] = ship.Parts.Values.Count(p => p.Destroyed),
                ["partsTotal"] = ship.Parts.Count,
            };
        }).ToList();

        return new Dictionary<string, object?>
        {
            ["result"] = battle.Result.ToString(),
            ["winner"] = battleEnded?.WinnerTeamId ?? battle.WinnerTeamId,
            ["reason"] = battleEnded?.Reason ?? "",
            ["durationS"] = Math.Round(battle.DurationS, 1),
            ["gunsFired"] = battle.GunsFired,
            ["magazineDetonations"] = battle.MagazineDetonations,
            ["torpedoHits"] = battle.TorpedoHits,
            ["shipsLost"] = battle.ShipsLost,
            ["ships"] = shipSummaries,
        };
    }
}
