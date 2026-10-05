using NavyThunder.Core.Ballistics;
using NavyThunder.Core.Battle;
using NavyThunder.Core.Commands;
using NavyThunder.Core.Damage;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Model;
using NavyThunder.Core.Ships;
using NavyThunder.Data;
using Xunit;
using Xunit.Abstractions;

namespace NavyThunder.Core.Tests;

/// <summary>
/// Phase 05 tests (PHASE_05 §9): command-object behavior (P05-1), the two-tier aiming
/// model with spotter correction (P05-2), and damage-control panel semantics (P05-5).
/// Real BattleRunner, short slices only — no full battles (tests/README.md §4).
/// </summary>
[Trait("Bucket", "Integration")]
public class Phase05CommandTests(ITestOutputHelper output)
{
    private static BattleRunner MakeRunner(string? playerShip = null)
    {
        var repo = DataRepository.LoadFromDirectory(RepoLocator.FindDataDirectory());
        var scenario = BattleScenario.Load(Path.Combine(RepoLocator.FindRepoRoot()!, "scenarios", "bb_duel.json"));
        var runner = new BattleRunner(repo, scenario, playerShipOverride: playerShip);
        return runner;
    }

    private static (BattleRunner Runner, Ship Player, Ship Enemy) Handed(string? playerShip = null)
    {
        var runner = MakeRunner(playerShip);
        var player = runner.Ships[0];
        runner.HandControlToPlayer(player.TargetId);
        var enemy = runner.Ships.First(s => s.Team?.Id != player.Team?.Id);
        return (runner, player, enemy);
    }

    // ------------------------------------------------------------- P05-1 commands

    [Fact]
    public void Helm_Command_Sets_And_Clamps()
    {
        var (runner, player, _) = Handed();

        runner.Submit(new HelmCommand(Throttle: 1.5, Rudder: -2.0));
        Assert.Equal(1.0, player.ThrottleCommand);
        Assert.Equal(-1.0, player.RudderCommand);

        runner.Submit(new HelmCommand(Throttle: 0.5, Rudder: 0.25));
        Assert.Equal(0.5, player.ThrottleCommand);
        Assert.Equal(0.25, player.RudderCommand);

        // null members keep current values
        runner.Submit(new HelmCommand());
        Assert.Equal(0.5, player.ThrottleCommand);
        Assert.Equal(0.25, player.RudderCommand);
    }

    [Fact]
    public void Commands_Without_Player_Ship_Are_NoOps()
    {
        var runner = MakeRunner(); // no HandControlToPlayer
        runner.Submit(new HelmCommand(0.5, 0.5));
        runner.Submit(new GunEngageCommand(runner.Ships[1].TargetId));
        runner.Submit(new GunCeaseFireCommand());
        runner.Submit(new ShellSelectCommand(true));
        runner.Submit(new DcOrderCommand(DcMode.Manual));
        runner.Submit(new TargetAssignCommand("whatever"));
        Assert.Null(runner.PlayerShip);
    }

    [Fact]
    public void Engage_Command_Builds_Orders_On_All_Guns()
    {
        var (runner, player, enemy) = Handed();

        runner.Submit(new GunEngageCommand(enemy.TargetId));
        foreach (var gun in player.Definition.Guns)
        {
            var order = runner.Guns.GetOrder(player.TargetId, gun.Id);
            Assert.NotNull(order);
            Assert.Equal(enemy.TargetId, order!.TargetId);
            // live tracking: the order's aim follows the target's actual position
            Assert.Equal(enemy.WorldPosition, order.TargetPosition());
        }

        runner.Submit(new GunCeaseFireCommand());
        foreach (var gun in player.Definition.Guns)
        {
            Assert.Null(runner.Guns.GetOrder(player.TargetId, gun.Id));
        }
    }

    [Fact]
    public void Manual_Aim_Command_Fires_At_The_Aim_Point()
    {
        var (runner, player, _) = Handed();
        var point = new Vec3(800, 0, -1200);

        runner.Submit(new GunManualAimCommand(point));
        foreach (var gun in player.Definition.Guns)
        {
            var order = runner.Guns.GetOrder(player.TargetId, gun.Id);
            Assert.NotNull(order);
            Assert.Equal("", order!.TargetId); // manual laying is unguided
            Assert.Equal(point, order.TargetPosition());
        }
    }

    [Fact]
    public void Shell_Select_Command_Switches_He_And_Back()
    {
        var (runner, player, _) = Handed("uss_fletcher"); // has an HE shell in data

        string ap = player.Definition.Guns[0].ShellId;
        string he = player.Definition.Guns[0].HeShellId!;
        runner.Submit(new ShellSelectCommand(true));
        Assert.Equal(he, runner.Guns.ShellOf(player.TargetId));
        runner.Submit(new ShellSelectCommand(false));
        Assert.Equal(ap, runner.Guns.ShellOf(player.TargetId));
    }

    [Fact]
    public void Target_Assign_Command_Sets_And_Clears_Lock()
    {
        var (runner, _, enemy) = Handed();

        runner.Submit(new TargetAssignCommand(enemy.TargetId));
        Assert.Equal(enemy.TargetId, runner.PlayerLockedTargetId);
        runner.Submit(new TargetAssignCommand(null));
        Assert.Null(runner.PlayerLockedTargetId);
    }

    // ------------------------------------------------------------- P05-5 damage control

    [Fact]
    public void Dc_Order_Command_Changes_Mode_Priority_And_Manual_Flow()
    {
        var (runner, player, _) = Handed();

        runner.Submit(new DcOrderCommand(
            Mode: DcMode.Manual,
            Priority: [DcFlow.Extinguishing, DcFlow.Unwatering, DcFlow.Repair],
            ManualFlow: DcFlow.Extinguishing));

        var (mode, priority) = runner.DamageControl.GetOrders(player.TargetId);
        Assert.Equal(DcMode.Manual, mode);
        Assert.Equal(DcFlow.Extinguishing, priority[0]);
        Assert.Equal(DcFlow.Unwatering, priority[1]);
        Assert.Equal(DcFlow.Repair, priority[2]);
        Assert.Equal(DcFlow.Extinguishing, runner.DamageControl.ManualFlow);
    }

    [Fact]
    public void Manual_Dc_Flow_Selection_Changes_Repair_Outcome()
    {
        // The panel's manual flow switch must change what actually gets repaired
        // (PHASE_05 §9: 优先级切换改变修理顺序与结果). Breaches are created the way the
        // combat pipeline creates them: through FloodingSystem.CreateBreach.
        (int breachLeft, double waterLeft) RunWith(DcFlow manualFlow)
        {
            var (runner, player, _) = Handed();
            var part = player.Parts.Values.First(p =>
                p.Definition.YMinM < 0 && p.Definition.Kind == PartKind.Compartment && !p.Destroyed);
            runner.Flooding.CreateBreach(player, part.Center, 0.5,
                FloodingSystem.BreachClass.BlastMedium);
            Assert.True(part.Breached, "test setup must produce a breach");

            runner.Submit(new DcOrderCommand(Mode: DcMode.Manual, ManualFlow: manualFlow));
            while (runner.World.Time < 60 && runner.Battle.Result == BattleResult.Running)
            {
                runner.World.Step();
            }

            return (part.Breached ? 1 : 0, part.WaterLevel);
        }

        var repairing = RunWith(DcFlow.Repair);
        var unwatering = RunWith(DcFlow.Unwatering);
        output.WriteLine($"repair flow: breached={repairing.breachLeft} water={repairing.waterLeft:0.###}");
        output.WriteLine($"unwater flow: breached={unwatering.breachLeft} water={unwatering.waterLeft:0.###}");
        Assert.Equal(0, repairing.breachLeft); // the repair flow patches breaches
        Assert.Equal(1, unwatering.breachLeft); // unwatering alone never patches them
    }

    // ------------------------------------------------------------- P05-2 spot correction

    [Fact]
    public void Spot_Correction_Walks_Aim_Onto_The_Target_Within_Three_Salvos()
    {
        var (runner, player, enemy) = Handed();
        runner.Submit(new GunEngageCommand(enemy.TargetId));

        var gun = player.Definition.Guns[0];
        (string, double, int)? before = null;
        while (runner.World.Time < 240 && runner.Battle.Result == BattleResult.Running)
        {
            runner.World.Step();
            var spot = runner.Guns.SpotOf(player.TargetId, gun.Id);
            if (spot is not null && spot.Value.Item3 >= 1 && before is null)
            {
                before = spot; // first salvo fired: full initial rangefinder bias remains
            }

            if (spot is { Item3: >= 3 })
            {
                break; // three salvos locked the bracket (W7 AB)
            }
        }

        var final = runner.Guns.SpotOf(player.TargetId, gun.Id);
        Assert.NotNull(final);
        Assert.Equal(enemy.TargetId, final!.Value.Item1);
        Assert.True(final.Value.Item3 >= 3, $"expected ≥3 salvos fired, got {final.Value.Item3}");
        Assert.NotNull(before);
        Assert.True(System.Math.Abs(final.Value.Item2) < System.Math.Abs(before!.Value.Item2),
            "the spotter must shrink the range bias salvo over salvo");
    }

    [Fact]
    public void Manual_Laying_Never_Enters_The_Spot_Loop()
    {
        var (runner, player, _) = Handed();
        runner.Submit(new GunManualAimCommand(player.WorldPosition + new Vec3(0, 0, 3000)));
        while (runner.World.Time < 60 && runner.Battle.Result == BattleResult.Running)
        {
            runner.World.Step();
        }

        foreach (var gun in player.Definition.Guns)
        {
            Assert.Null(runner.Guns.SpotOf(player.TargetId, gun.Id));
        }
    }

    // ------------------------------------------------------------- P05-1 determinism

    [Fact]
    public void Scripted_Command_Sequence_Replays_Deterministically()
    {
        Dictionary<string, object?> Run()
        {
            var runner = MakeRunner();
            var player = runner.Ships[0];
            runner.HandControlToPlayer(player.TargetId);
            var enemy = runner.Ships.First(s => s.Team?.Id != player.Team?.Id);

            var pending = new Queue<(double AtS, GameplayCommand Cmd)>(new[]
            {
                (5.0, (GameplayCommand)new HelmCommand(Throttle: 0.8)),
                (10.0, new HelmCommand(Rudder: 0.5)),
                (20.0, new GunEngageCommand(enemy.TargetId)),
                (40.0, new ShellSelectCommand(true)),
                (60.0, new HelmCommand(Throttle: 1.0, Rudder: -0.4)),
                (80.0, new DcOrderCommand(Mode: DcMode.Manual, ManualFlow: DcFlow.Repair)),
                (100.0, new GunCeaseFireCommand()),
                (120.0, new TargetAssignCommand(enemy.TargetId)),
            });

            while (runner.World.Time < 240 && runner.Battle.Result == BattleResult.Running)
            {
                runner.World.Step();
                while (pending.TryPeek(out var next) && runner.World.Time >= next.AtS)
                {
                    runner.Submit(pending.Dequeue().Cmd);
                }
            }

            return new Dictionary<string, object?>
            {
                ["time"] = runner.World.Time,
                ["pos"] = $"{player.WorldPosition.X:0.000},{player.WorldPosition.Z:0.000}",
                ["heading"] = player.HeadingDeg,
                ["speed"] = player.SpeedKnots,
                ["salvos"] = runner.Battle.GunsFired,
                ["shell"] = runner.Guns.ShellOf(player.TargetId),
                ["result"] = runner.Battle.Result.ToString(),
                ["gunsFiredEnemy"] = runner.World.Events.TotalRecorded,
            };
        }

        var a = Run();
        var b = Run();
        foreach (var key in a.Keys)
        {
            Assert.Equal(a[key], b[key]);
        }

        output.WriteLine("scripted replay: " + string.Join(", ", a.Select(kv => $"{kv.Key}={kv.Value}")));
    }
}
