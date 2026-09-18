using NavyThunder.Core.Mathematics;
using Xunit;

namespace NavyThunder.Core.Tests;

public class Vec3Tests
{
    [Fact]
    public void Add_Subtract_Scale_Follow_Component_Math()
    {
        var a = new Vec3(1, 2, 3);
        var b = new Vec3(4, 5, 6);

        Assert.Equal(new Vec3(5, 7, 9), a + b);
        Assert.Equal(new Vec3(-3, -3, -3), a - b);
        Assert.Equal(new Vec3(2, 4, 6), a * 2);
        Assert.Equal(new Vec3(0.5, 1, 1.5), a / 2);
    }

    [Fact]
    public void Dot_And_Cross_Match_Known_Values()
    {
        var x = new Vec3(1, 0, 0);
        var y = new Vec3(0, 1, 0);

        Assert.Equal(0, x.Dot(y));
        Assert.Equal(1, x.Dot(x));
        Assert.Equal(new Vec3(0, 0, 1), x.Cross(y));
    }

    [Fact]
    public void Normalize_Zero_Vector_Is_Safe()
    {
        Assert.Equal(Vec3.Zero, Vec3.Zero.Normalized());
        Assert.Equal(1, new Vec3(3, 4, 0).Normalized().Length, 12);
    }

    [Fact]
    public void Length_Matches_Pythagoras()
    {
        Assert.Equal(5, new Vec3(3, 4, 0).Length, 12);
    }
}

public class DeterministicRandomTests
{
    [Fact]
    public void Same_Seed_Produces_Identical_Sequence()
    {
        var a = new DeterministicRandom(42);
        var b = new DeterministicRandom(42);

        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(a.NextUInt64(), b.NextUInt64());
        }
    }

    [Fact]
    public void Different_Seeds_Produce_Different_Sequences()
    {
        var a = new DeterministicRandom(1);
        var b = new DeterministicRandom(2);

        Assert.NotEqual(a.NextUInt64(), b.NextUInt64());
    }

    [Fact]
    public void NextDouble_Stays_In_Range()
    {
        var rng = new DeterministicRandom(7);

        for (int i = 0; i < 10_000; i++)
        {
            double d = rng.NextDouble();
            Assert.InRange(d, 0.0, 1.0);
        }
    }

    [Fact]
    public void Named_Streams_Are_Independent()
    {
        var fireStream = RngStreams.Create(123, "fire");
        var floodStream = RngStreams.Create(123, "flooding");

        // Two differently named streams from the same master seed must diverge.
        Assert.NotEqual(fireStream.NextUInt64(), floodStream.NextUInt64());
    }

    [Fact]
    public void NextInt_Respects_Range()
    {
        var rng = new DeterministicRandom(9);

        for (int i = 0; i < 1000; i++)
        {
            Assert.InRange(rng.NextInt(3, 7), 3, 6);
        }
    }
}
