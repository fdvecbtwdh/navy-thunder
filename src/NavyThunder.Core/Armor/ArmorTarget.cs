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
