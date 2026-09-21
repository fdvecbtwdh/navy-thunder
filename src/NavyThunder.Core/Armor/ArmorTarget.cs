using NavyThunder.Core.Geometry;
using NavyThunder.Core.Mathematics;

namespace NavyThunder.Core.Armor;

/// <summary>
/// A collection of armor plates forming one target's protection scheme (Phase 3 replaces
/// this with full ship compartment/module layout; the plate trace stays the same).
/// </summary>
public sealed class ArmorTarget
{
    private readonly List<ArmorPlate> _plates = [];

    public string Id { get; init; } = "target";

    public IReadOnlyList<ArmorPlate> Plates => _plates;

    public ArmorTarget Add(ArmorPlate plate)
    {
        _plates.Add(plate);
        return this;
    }

    /// <summary>
    /// Bounding sphere for broadphase rejection (R4.1); recomputed by the caller each
    /// tick via <see cref="RefreshBroadphase"/> since plates move with the hull.
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

        Vec3 min = new(double.MaxValue, double.MaxValue, double.MaxValue);
        Vec3 max = new(double.MinValue, double.MinValue, double.MinValue);
        foreach (var plate in _plates)
        {
            var c = plate.Center;
            min = new Vec3(Math.Min(min.X, c.X), Math.Min(min.Y, c.Y), Math.Min(min.Z, c.Z));
            max = new Vec3(Math.Max(max.X, c.X), Math.Max(max.Y, c.Y), Math.Max(max.Z, c.Z));
        }

        BroadphaseCenter = (min + max) / 2;
        // plates have extent: inflate by the largest plate half-diagonal so the
        // sphere fully contains every plate box (otherwise edge hits get culled)
        double maxHalfDiag = 0;
        foreach (var plate in _plates)
        {
            maxHalfDiag = Math.Max(maxHalfDiag,
                Math.Sqrt(plate.HalfU * plate.HalfU + plate.HalfV * plate.HalfV));
        }

        var centerToFarthest = 0.0;
        foreach (var plate in _plates)
        {
            centerToFarthest = Math.Max(centerToFarthest,
                (plate.Center - BroadphaseCenter).Length + maxHalfDiag);
        }

        BroadphaseRadius = centerToFarthest + 1.0;
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
    /// Allocation-free variant of <see cref="Trace"/> for hot loops (R4.1): fills the
    /// caller-owned buffer with hits ordered by distance, capped at maxDistance.
    /// Returns the number of valid entries.
    /// </summary>
    public int TraceNonAlloc(Vec3 origin, Vec3 direction, double maxDistance,
        List<(ArmorPlate Plate, RayHit Hit)> buffer)
    {
        buffer.Clear();
        foreach (var plate in _plates)
        {
            if (plate.IntersectRay(origin, direction, out var hit) && hit.Distance <= maxDistance)
            {
                buffer.Add((plate, hit));
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
    /// Traces a ray against all plates and returns the nearest hit per plate, ordered by
    /// distance — i.e. the layer sequence a shell would meet.
    /// </summary>
    public IReadOnlyList<(ArmorPlate Plate, RayHit Hit)> Trace(Vec3 origin, Vec3 direction)
    {
        List<(ArmorPlate, RayHit)> hits = [];
        foreach (var plate in _plates)
        {
            if (plate.IntersectRay(origin, direction, out var hit))
            {
                hits.Add((plate, hit));
            }
        }

        hits.Sort(static (a, b) => a.Item2.Distance.CompareTo(b.Item2.Distance));
        return hits;
    }
}
