using NavyThunder.Core.World;
using Xunit;

namespace NavyThunder.Core.Tests;

public sealed class OrderedSystem(string name, List<string> log) : ISimulationSystem
{
    public string Name => name;

    public void Initialize(SimulationWorld world)
    {
    }

    public void Update(SimulationWorld world, double deltaTime)
    {
        log.Add($"{world.TickIndex}:{name}");
    }
}

public sealed class CounterEntity : Entity
{
    public int Count;
}

public sealed class CountingSystem : ISimulationSystem
{
    public string Name => "counting";

    public void Initialize(SimulationWorld world)
    {
    }

    public void Update(SimulationWorld world, double deltaTime)
    {
        foreach (var e in world.Entities.OfType<CounterEntity>())
        {
            e.Count++;
        }
    }
}

public class SimulationWorldTests
{
    [Fact]
    public void Systems_Update_In_Registration_Order()
    {
        var log = new List<string>();
        var world = new SimulationWorld();
        world.AddSystem(new OrderedSystem("a", log));
        world.AddSystem(new OrderedSystem("b", log));
        world.AddSystem(new OrderedSystem("c", log));

        world.Step();

        Assert.Equal(["0:a", "0:b", "0:c"], log);
    }

    [Fact]
    public void Time_And_Tick_Advance_With_Fixed_Delta()
    {
        var world = new SimulationWorld(fixedDeltaTime: 0.02);
        world.Run(1.0);

        Assert.Equal(50UL, world.TickIndex);
        Assert.Equal(1.0, world.Time, 12);
    }

    [Fact]
    public void Entities_Get_Unique_Stable_Ids()
    {
        var world = new SimulationWorld();
        var e1 = new CounterEntity();
        var e2 = new CounterEntity();
        world.AddEntity(e1);
        world.AddEntity(e2);

        Assert.Equal(1, e1.Id);
        Assert.Equal(2, e2.Id);
    }

    [Fact]
    public void Identical_Worlds_Produce_Identical_Event_Logs()
    {
        foreach (var seed in new[] { 1UL, 0xDEADBEEFUL, SimulationWorld.DefaultMasterSeed })
        {
            var worlds = new[] { MakeSeededWorld(seed), MakeSeededWorld(seed) };
            worlds[0].Run(2.0);
            worlds[1].Run(2.0);

            var a = worlds[0].Events.All.Select(e => $"{e.Tick}|{e.Kind}");
            var b = worlds[1].Events.All.Select(e => $"{e.Tick}|{e.Kind}");
            Assert.Equal(a, b);
        }
    }

    private static SimulationWorld MakeSeededWorld(ulong seed)
    {
        var world = new SimulationWorld(fixedDeltaTime: 0.02, masterSeed: seed);
        world.AddSystem(new CountingSystem());
        world.AddEntity(new CounterEntity());
        // Touch an RNG stream so seeded behavior participates in the comparison.
        world.Rng("fire").NextDouble();
        return world;
    }
}
