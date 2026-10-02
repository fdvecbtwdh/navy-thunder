using NavyThunder.Core.Geometry;
using NavyThunder.Core.Mathematics;

namespace NavyThunder.Core.Armor;

/// <summary>
/// A collection of armor plates forming one target's protection scheme. Plate geometry
/// is SHIP-LOCAL forever (Phase 01); the owner supplies a <see cref="TransformProvider"/>
/// (static targets may leave it null = identity). World rays are converted to the hull
/// frame here, so every caller keeps world-space semantics while the plates themselves
/// never move or rotate.
/// </summary>
public sealed class ArmorTarget
{
    private readonly List<ArmorPlate> _plates = [];

    public string Id { get; init; } = "target";

    /// <summary>
    /// Supplies the owner's current <see cref="Geometry.ShipTransform"/> (position +
    /// heading). Null = identity (tests and static targets). Wired by the battle runner;
    /// read once per trace/broadphase refresh.
    /// </summary>
    public Func<Geometry.ShipTransform>? TransformProvider { get; set; }

    public Geometry.ShipTransform Transform => TransformProvider?.Invoke() ?? Geometry.ShipTransform.Identity;

    public IReadOnlyList<ArmorPlate> Plates => _plates;

    public ArmorTarget Add(ArmorPlate plate)
    {
        _plates.Add(plate);
        return this;
    }

    // Local bounds never change (plates are immutable); cache them and only re-apply
    // the (moving, rotating) world centre on each refresh.

    private bool _localBoundsCached;
    private Vec3 _localCenter;
    private double _localRadius;

    /// <summary>
    /// Bounding SPHERE for broadphase rejection (R4.1): centre = transformed local
    /// bounds centre, radius = local extent + plate half-diagonal. Sphere is
    /// rotation-invariant, so a turning hull can never be missed by the broadphase.
    /// </summary>
    public Vec3 BroadphaseCenter { get; private set; }
    public double BroadphaseRadius { get; private set; }

    public void RefreshBroadphase()
    {
        if (_plates.Count == 0)
        {
            BroadphaseCenter = default;
            BroadphaseRadius = 0;
            return;
        }

        if (!_localBoundsCached)
        {
            Vec3 min = new(double.MaxValue, double.MaxValue, double.MaxValue);
            Vec3 max = new(double.MinValue, double.MinValue, double.MinValue);
            double maxHalfDiag = 0;
            foreach (var plate in _plates)
            {
                var c = plate.Center;
                min = new Vec3(Math.Min(min.X, c.X), Math.Min(min.Y, c.Y), Math.Min(min.Z, c.Z));
                max = new Vec3(Math.Max(max.X, c.X), Math.Max(max.Y, c.Y), Math.Max(max.Z, c.Z));
                maxHalfDiag = Math.Max(maxHalfDiag,
                    Math.Sqrt(plate.HalfU * plate.HalfU + plate.HalfV * plate.HalfV));
            }

            _localCenter = (min + max) / 2;
            // plates have extent: inflate by the largest plate half-diagonal so the
            // sphere fully contains every plate box (otherwise edge hits get culled)
            double centerToFarthest = 0;
            foreach (var plate in _plates)
            {
                centerToFarthest = Math.Max(centerToFarthest,
                    (plate.Center - _localCenter).Length + maxHalfDiag);
            }

            _localRadius = centerToFarthest + 1.0;
            _localBoundsCached = true;
        }

        BroadphaseCenter = Transform.ToWorld(_localCenter);
        BroadphaseRadius = _localRadius;
    }

    /// <summary>
    /// Broadphase reject: true when the segment (start, dir, length) cannot reach this
    /// target's bounding sphere — closest approach of the segment to the sphere center
    /// is farther than the sphere radius.
    /// </summary>
    public bool BroadphaseReject(Vec3 start, Vec3 dir, double length)
    {
        var toCenter = BroadphaseCenter - start;
        double along = toCenter.Dot(dir);
        double perp2 = toCenter.LengthSquared - along * along;
        double reach = along < 0 ? toCenter.LengthSquared : perp2;
        double limit = (BroadphaseRadius + length) * (BroadphaseRadius + length);
        return reach > limit;
    }

    /// <summary>
    /// Allocation-free variant of <see cref="Trace"/> for hot loops (R4.1): takes a WORLD
    /// ray, intersects the ship-local plates, fills the caller-owned buffer with hits
    /// ordered by distance (WORLD point/normal, local geometry), capped at maxDistance.
    /// Returns the number of valid entries.
    /// </summary>
    public int TraceNonAlloc(Vec3 origin, Vec3 direction, double maxDistance,
        List<(ArmorPlate Plate, RayHit Hit)> buffer)
    {
        var t = Transform;
        Vec3 localOrigin = t.ToLocal(origin);
        Vec3 localDir = t.ToLocalDirection(direction);

        buffer.Clear();
        foreach (var plate in _plates)
        {
            if (plate.IntersectRay(localOrigin, localDir, out var hit) && hit.Distance <= maxDistance)
            {
                buffer.Add((plate, ToWorld(t, hit)));
            }
        }

        // insertion sort: hit counts per segment are tiny (typically 1-4)
        for (int i = 1; i < buffer.Count; i++)
        {
            var cur = buffer[i];
            int j = i - 1;
            while (j >= 0 && buffer[j].Hit.Distance > cur.Hit.Distance)
            {
                buffer[j + 1] = buffer[j];
                j--;
            }

            buffer[j + 1] = cur;
        }

        return buffer.Count;
    }

    /// <summary>
    /// Traces a WORLD ray against all plates and returns the nearest hit per plate,
    /// ordered by distance — i.e. the layer sequence a shell would meet. Hit points and
    /// normals come back in WORLD space (distance is frame-invariant).
    /// </summary>
    public IReadOnlyList<(ArmorPlate Plate, RayHit Hit)> Trace(Vec3 origin, Vec3 direction)
    {
        var t = Transform;
        Vec3 localOrigin = t.ToLocal(origin);
        Vec3 localDir = t.ToLocalDirection(direction);

        List<(ArmorPlate, RayHit)> hits = [];
        foreach (var plate in _plates)
        {
            if (plate.IntersectRay(localOrigin, localDir, out var hit))
            {
                hits.Add((plate, ToWorld(t, hit)));
            }
        }

        hits.Sort(static (a, b) => a.Item2.Distance.CompareTo(b.Item2.Distance));
        return hits;
    }

    private static RayHit ToWorld(Geometry.ShipTransform t, RayHit localHit) => new(
        localHit.Distance,
        t.ToWorld(localHit.Point),
        t.ToWorldDirection(localHit.Normal));
}
