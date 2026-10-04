using NavyThunder.Core.Geometry;
using NavyThunder.Core.Mathematics;
using Xunit;

namespace NavyThunder.Core.Tests;

/// <summary>
/// Phase 01 foundation: ShipTransform math. Position vs direction transforms are
/// strictly separated; round trips and orthonormality are asserted over the heading
/// sweep PHASE_01 requires. Conventions: local +Z bow / +X starboard / +Y up,
/// heading 90° points the bow at world +X (matches ShipNavigationSystem integration).
/// </summary>
    [Trait("Bucket", "Fast")]
public class ShipTransformTests
{
    private const double PosEps = 1e-9;
    private const double DirEps = 1e-12;

    private static readonly double[] Headings = [0, 15, 37, 45, 90, 135, 180, 225, 270, 315, 359];

    // ---------------------------------------------------------------- axis basis

    [Theory]
    [InlineData(0, 0, 1)]    // heading, expected world bow X, expected world bow Z
    [InlineData(90, 1, 0)]
    [InlineData(180, 0, -1)]
    [InlineData(270, -1, 0)]
    public void Bow_Follows_Navigation_Heading_Convention(double heading, double wx, double wz)
    {
        var t = new ShipTransform(new Vec3(100, 0, -50), heading);

        var bow = t.ToWorldDirection(new Vec3(0, 0, 1));

        Assert.Equal(wx, bow.X, 10);
        Assert.Equal(0, bow.Y, 10);
        Assert.Equal(wz, bow.Z, 10);
    }

    [Fact]
    public void Starboard_Is_Perpendicular_Right_Of_Bow()
    {
        // heading 0: bow +Z world, starboard must be +X world.
        var t0 = new ShipTransform(Vec3.Zero, 0);
        var right0 = t0.ToWorldDirection(new Vec3(1, 0, 0));
        Assert.Equal(1, right0.X, 10);
        Assert.Equal(0, right0.Z, 10);

        // heading 90: bow +X world, starboard must be -Z world (facing east, right hand south).
        var t90 = new ShipTransform(Vec3.Zero, 90);
        var right90 = t90.ToWorldDirection(new Vec3(1, 0, 0));
        Assert.Equal(0, right90.X, 10);
        Assert.Equal(-1, right90.Z, 10);
    }

    [Fact]
    public void Up_Is_Preserved_By_Yaw()
    {
        foreach (double h in Headings)
        {
            var up = new ShipTransform(Vec3.Zero, h).ToWorldDirection(new Vec3(0, 1, 0));
            Assert.Equal(0, up.X, DirEps);
            Assert.Equal(1, up.Y, DirEps);
            Assert.Equal(0, up.Z, DirEps);
        }
    }

    // ----------------------------------------------------------- position transforms

    [Fact]
    public void Heading_Zero_Translation_Only()
    {
        var t = new ShipTransform(new Vec3(500, 0, -700), 0);

        var world = t.ToWorld(new Vec3(10, 5, -20));
        Assert.Equal(510, world.X, PosEps);
        Assert.Equal(5, world.Y, PosEps);
        Assert.Equal(-720, world.Z, PosEps);

        var local = t.ToLocal(new Vec3(510, 5, -720));
        Assert.Equal(10, local.X, PosEps);
        Assert.Equal(5, local.Y, PosEps);
        Assert.Equal(-20, local.Z, PosEps);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void Bow_Offset_Lands_Ahead_Of_Ship_At_Each_Cardinal_Heading(double heading)
    {
        // A point 100 m forward of the origin must appear 100 m along the world bow vector.
        var t = new ShipTransform(new Vec3(2000, 0, -3000), heading);
        var bowWorld = t.ToWorldDirection(new Vec3(0, 0, 1));
        var p = t.ToWorld(new Vec3(0, 0, 100));

        Assert.Equal(2000 + bowWorld.X * 100, p.X, 8);
        Assert.Equal(-3000 + bowWorld.Z * 100, p.Z, 8);
    }

    // ------------------------------------------------------------------ round trips

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(37)]
    [InlineData(45)]
    [InlineData(90)]
    [InlineData(135)]
    [InlineData(180)]
    [InlineData(225)]
    [InlineData(270)]
    [InlineData(359)]
    public void Position_RoundTrip_ToLocal_ToWorld(double heading)
    {
        var t = new ShipTransform(new Vec3(1234.5, 0, -987.6), heading);
        var p = new Vec3(33.3, -7.7, 44.4);

        var round = t.ToWorld(t.ToLocal(p));
        Assert.Equal(p.X, round.X, PosEps);
        Assert.Equal(p.Y, round.Y, PosEps);
        Assert.Equal(p.Z, round.Z, PosEps);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(37)]
    [InlineData(45)]
    [InlineData(90)]
    [InlineData(135)]
    [InlineData(180)]
    [InlineData(225)]
    [InlineData(270)]
    [InlineData(359)]
    public void Position_RoundTrip_ToWorld_ToLocal(double heading)
    {
        var t = new ShipTransform(new Vec3(-543.2, 0, 876.5), heading);
        var p = new Vec3(-1200, 3, 250);

        var round = t.ToLocal(t.ToWorld(p));
        Assert.Equal(p.X, round.X, PosEps);
        Assert.Equal(p.Y, round.Y, PosEps);
        Assert.Equal(p.Z, round.Z, PosEps);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(37)]
    [InlineData(45)]
    [InlineData(90)]
    [InlineData(135)]
    [InlineData(180)]
    [InlineData(225)]
    [InlineData(270)]
    [InlineData(359)]
    public void Direction_RoundTrip_Both_Ways(double heading)
    {
        var t = new ShipTransform(new Vec3(9, 0, -9), heading);
        var d = new Vec3(0.3, -0.4, 0.5).Normalized();

        var a = t.ToLocalDirection(t.ToWorldDirection(d));
        Assert.Equal(d.X, a.X, DirEps);
        Assert.Equal(d.Y, a.Y, DirEps);
        Assert.Equal(d.Z, a.Z, DirEps);

        var b = t.ToWorldDirection(t.ToLocalDirection(d));
        Assert.Equal(d.X, b.X, DirEps);
        Assert.Equal(d.Y, b.Y, DirEps);
        Assert.Equal(d.Z, b.Z, DirEps);
    }

    // ------------------------------------------------------------------ orthogonality

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(37)]
    [InlineData(45)]
    [InlineData(90)]
    [InlineData(135)]
    [InlineData(180)]
    [InlineData(225)]
    [InlineData(270)]
    [InlineData(359)]
    public void Rotated_Basis_Stays_Orthonormal(double heading)
    {
        var t = new ShipTransform(Vec3.Zero, heading);
        var r = t.ToWorldDirection(new Vec3(1, 0, 0));
        var u = t.ToWorldDirection(new Vec3(0, 1, 0));
        var f = t.ToWorldDirection(new Vec3(0, 0, 1));

        Assert.Equal(1, r.Length, DirEps);
        Assert.Equal(1, u.Length, DirEps);
        Assert.Equal(1, f.Length, DirEps);
        Assert.Equal(0, r.Dot(u), DirEps);
        Assert.Equal(0, r.Dot(f), DirEps);
        Assert.Equal(0, u.Dot(f), DirEps);
    }

    // --------------------------------------------------- direction is not a position

    [Fact]
    public void Direction_Transform_Ignores_Translation()
    {
        var atOrigin = new ShipTransform(Vec3.Zero, 90);
        var farAway = new ShipTransform(new Vec3(10000, 0, -10000), 90);
        var d = new Vec3(0, 0, 1);

        var a = atOrigin.ToWorldDirection(d);
        var b = farAway.ToWorldDirection(d);

        Assert.Equal(a.X, b.X, DirEps);
        Assert.Equal(a.Y, b.Y, DirEps);
        Assert.Equal(a.Z, b.Z, DirEps);
    }

    [Fact]
    public void Point_Transform_Includes_Translation()
    {
        var t = new ShipTransform(new Vec3(10000, 0, -10000), 90);

        var world = t.ToWorld(new Vec3(0, 0, 1));

        // bow east: +1 local Z becomes +1 world X from the ship's position
        Assert.Equal(10001, world.X, 8);
        Assert.Equal(-10000, world.Z, 8);
    }
}
