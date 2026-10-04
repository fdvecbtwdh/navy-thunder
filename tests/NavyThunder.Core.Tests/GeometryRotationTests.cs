using NavyThunder.Core.Armor;
using NavyThunder.Core.Ballistics;
using NavyThunder.Core.Damage;
using NavyThunder.Core.Geometry;
using NavyThunder.Core.Mathematics;
using NavyThunder.Core.Model;
using NavyThunder.Core.Ships;
using NavyThunder.Core.World;
using NavyThunder.Data;
using Xunit;
using Xunit.Abstractions;

namespace NavyThunder.Core.Tests;

/// <summary>
/// Phase 01 behaviour anchors: prove the hit geometry is genuinely ship-local —
/// heading changes must rotate the world relationship of plates/parts while the local
/// geometry itself stays byte-identical. Includes the anti-fake-rotation test (PHASE_01
/// §10): a bow-only plate MUST stop being hittable by the same world ray once the ship
/// turns away — only a real inverse-ShipTransform trace can pass.
/// </summary>
    [Trait("Bucket", "Fast")]
public class GeometryRotationTests(ITestOutputHelper output)
{
    private static ArmorTarget BowOnlyTarget()
    {
        // One plate on the bow face (local +Z end), nothing else.
        return new ArmorTarget { Id = "bow_only" }
            .Add(new ArmorPlate
            {
                Id = "bow_plate",
                Center = new Vec3(0, 0, 50),
                Normal = new Vec3(0, 0, 1),
                AxisU = new Vec3(1, 0, 0),
                AxisV = new Vec3(0, 1, 0),
                HalfU = 10,
                HalfV = 10,
                ThicknessMm = 100,
            });
    }

    private static Vec3 HeadingBow(double heading) => new(
        Math.Sin(heading * Math.PI / 180.0), 0, Math.Cos(heading * Math.PI / 180.0));

    // ------------------------------------------------------------------- Anchor A

    [Fact]
    public void AnchorA_Same_World_Ray_On_Turned_Ship_Hits_A_Different_Plane()
    {
        // A broadside belt on the port side (local -X face) plus the bow plate: at
        // heading 0 a +X-travelling ray crosses the belt plane; at heading 90 the belt
        // faces world +Z (its plane contains the ray) and the bow face has swung into
        // the ray's path — the same world ray now meets a different plate.
        var target = new ArmorTarget { Id = "anchorA" }
            .Add(new ArmorPlate
            {
                Id = "belt_port", Center = new Vec3(-10, 0, 0), Normal = new Vec3(-1, 0, 0),
                AxisU = new Vec3(0, 1, 0), AxisV = new Vec3(0, 0, 1), HalfU = 8, HalfV = 40,
                ThicknessMm = 100,
            })
            .Add(new ArmorPlate
            {
                Id = "bow_plate", Center = new Vec3(0, 0, 50), Normal = new Vec3(0, 0, 1),
                AxisU = new Vec3(1, 0, 0), AxisV = new Vec3(0, 1, 0), HalfU = 12, HalfV = 8,
                ThicknessMm = 100,
            });

        Vec3 origin = new(-200, 0, 0);
        Vec3 dir = new(1, 0, 0);

        target.TransformProvider = () => new ShipTransform(Vec3.Zero, 0);
        var hit0 = target.Trace(origin, dir);
        Assert.Contains(hit0, h => h.Plate.Id == "belt_port");

        target.TransformProvider = () => new ShipTransform(Vec3.Zero, 90);
        var hit90 = target.Trace(origin, dir);
        Assert.DoesNotContain(hit90, h => h.Plate.Id == "belt_port");
        Assert.Contains(hit90, h => h.Plate.Id == "bow_plate");
    }

    // ------------------------------------------------------------------- Anchor B

    [Fact]
    public void AnchorB_Local_Hit_Point_Is_Heading_Invariant()
    {
        var target = BowOnlyTarget();
        Vec3 origin = new(0, 0, -200);
        Vec3 dir = new(0, 0, 1); // tail-to-bow in world; at heading 0 this is stern->bow

        target.TransformProvider = () => new ShipTransform(Vec3.Zero, 0);
        var hit0 = Assert.Single(target.Trace(origin, dir));

        target.TransformProvider = () => new ShipTransform(Vec3.Zero, 180); // bow now faces -Z
        // The bow plate's outward normal now points -Z: the +Z ray meets its BACK face
        // (denominator negative -> hit registered with a flipped normal), so aim instead
        // from the new bow side.
        var hit180 = Assert.Single(target.Trace(new Vec3(0, 0, 200), new Vec3(0, 0, -1)));

        // Same plate face, same LOCAL impact point (the plate plane z=50 hit dead centre),
        // independent of heading.
        Assert.Equal(0, hit0.Hit.Point.X, 6);
        Assert.Equal(0, hit180.Hit.Point.X, 6);
        var local0 = new ShipTransform(Vec3.Zero, 0).ToLocal(hit0.Hit.Point);
        var local180 = new ShipTransform(Vec3.Zero, 180).ToLocal(hit180.Hit.Point);
        Assert.Equal(local0.X, local180.X, 6);
        Assert.Equal(local0.Z, local180.Z, 6);
    }

    // ------------------------------------------------------------------- Anchor C

    [Fact]
    public void AnchorC_Heading_Change_Never_Modifies_Plate_Data()
    {
        var target = BowOnlyTarget();
        var before = target.Plates[0].Center;

        for (double heading = 0; heading <= 359; heading += 37)
        {
            target.TransformProvider = () => new ShipTransform(new Vec3(555, 0, 444), heading);
            target.RefreshBroadphase();
            Assert.Equal(before, target.Plates[0].Center);
        }
    }

    // --------------------------------------------------------- anti-fake-rotation

    [Fact]
    public void Bow_Plate_Stops_Blocking_The_World_Ray_When_The_Ship_Turns_Away()
    {
        // World-axis-aligned geometry would keep the plate normal at +Z forever and the
        // ray below would always hit. Only inverse(shipTransform) makes the hit vanish.
        var target = BowOnlyTarget();
        Vec3 origin = new(0, 0, -200);
        Vec3 dir = new(0, 0, 1);

        target.TransformProvider = () => new ShipTransform(Vec3.Zero, 0);
        Assert.Single(target.Trace(origin, dir)); // bow faces the ray

        target.TransformProvider = () => new ShipTransform(Vec3.Zero, 90); // bow faces +X
        Assert.Empty(target.Trace(origin, dir)); // ray travels parallel to the plate plane
    }

    [Fact]
    public void Turning_Ship_Is_Never_Culled_By_The_Broadphase()
    {
        var target = BowOnlyTarget();
        foreach (double heading in new[] { 0.0, 37.0, 90.0, 143.0, 270.0 })
        {
            target.TransformProvider = () => new ShipTransform(new Vec3(8000, 0, -15000), heading);
            target.RefreshBroadphase();

            // Ray aimed straight at the transformed bow plate centre must survive the reject.
            var t = target.Transform;
            var plateWorld = t.ToWorld(target.Plates[0].Center);
            Vec3 start = plateWorld + HeadingBow(heading) * -300;
            Vec3 dir = HeadingBow(heading);
            Assert.False(target.BroadphaseReject(start, dir, 300),
                $"broadphase culled a reachable hit at heading {heading}");
        }
    }

    // -------------------------------------------------- interior trace under rotation

    [Fact]
    public void Interior_Trace_Follows_The_Hull_Frame()
    {
        var repo = DataRepository.LoadFromDirectory(RepoLocator.FindDataDirectory());
        var ship = ShipFactory.Create(repo.Ships["test_battleship"]);
        var world = new SimulationWorld();
        world.AddEntity(ship);

        // One fixed WORLD ray (5 m starboard of the ship centre, travelling world +Z).
        // Heading 0 (bow +Z): it runs down the keel through the midships compartments.
        // Heading 90 (bow +X): in the hull frame it becomes a beam-wise ray 5 m off the
        // centreline — a different local line, so the traversed parts must differ. Equal
        // results would mean the trace ignored the hull rotation (world-axis behaviour).
        Vec3 worldStart = ship.WorldPosition + new Vec3(5, -3, 0);
        Vec3 worldDir = new(0, 0, 1);
        const double depth = 200;

        ship.HeadingDeg = 0;
        var t0 = ship.WorldTransform;
        var trace0 = DamageBridgeSystem.TraceInterior(
            ship, t0.ToLocal(worldStart), t0.ToLocalDirection(worldDir), depth);

        ship.HeadingDeg = 90;
        var t90 = ship.WorldTransform;
        var trace90 = DamageBridgeSystem.TraceInterior(
            ship, t90.ToLocal(worldStart), t90.ToLocalDirection(worldDir), depth);

        var parts0 = trace0.Select(p => p.Part.Definition.Id).ToList();
        var parts90 = trace90.Select(p => p.Part.Definition.Id).ToList();

        output.WriteLine($"heading0 traced: [{string.Join(", ", parts0)}]");
        output.WriteLine($"heading90 traced: [{string.Join(", ", parts90)}]");
        Assert.NotEmpty(parts0);
        Assert.NotEqual(parts0, parts90);
    }
}
