using NavyThunder.Core.Mathematics;

namespace NavyThunder.Core.Geometry;

public readonly record struct RayHit(double Distance, Vec3 Point, Vec3 Normal);

/// <summary>
/// A finite oriented rectangle extruded along its normal — the fundamental armor unit.
/// ALL coordinates are SHIP-LOCAL (Phase 01): plates never move or rotate at runtime;
/// the owning <see cref="ArmorTarget"/> transforms world rays into the hull frame before
/// intersecting. Hit detection is exact ray/plane intersection with in-plane bounds.
/// </summary>
public sealed class ArmorPlate
{
    public required string Id { get; init; }

    /// <summary>Ship-local centre of the plate face (never modified after construction).</summary>
    public Vec3 Center { get; init; } = Vec3.Zero;

    /// <summary>Unit normal of the front (outward) face, ship-local.</summary>
    public required Vec3 Normal { get; init; }

    /// <summary>Unit in-plane axes (must be orthonormal with Normal), ship-local.</summary>
    public required Vec3 AxisU { get; init; }
    public required Vec3 AxisV { get; init; }

    public required double HalfU { get; init; }
    public required double HalfV { get; init; }

    public required double ThicknessMm { get; init; }

    /// <summary>Material class, e.g. "ship_structural_steel" (WT armorClass tag).</summary>
    public string Material { get; init; } = "ship_structural_steel";

    public double ThicknessM => ThicknessMm / 1000.0;

    /// <summary>
    /// Intersects a ray with the front face. The ray must be expressed in the SAME frame
    /// as the plate (ship-local); <see cref="ArmorTarget"/> converts world rays first.
    /// Returns the distance along the ray, the hit point, and a normal facing the ray.
    /// </summary>
    public bool IntersectRay(Vec3 origin, Vec3 direction, out RayHit hit)
    {
        double denom = direction.Dot(Normal);
        if (Math.Abs(denom) < 1e-9)
        {
            hit = default;
            return false; // parallel to the plate
        }

        double t = (Center - origin).Dot(Normal) / denom;
        if (t <= 0)
        {
            hit = default;
            return false; // behind the ray origin
        }

        Vec3 p = origin + direction * t;
        Vec3 d = p - Center;
        double u = d.Dot(AxisU);
        double v = d.Dot(AxisV);
        if (Math.Abs(u) > HalfU || Math.Abs(v) > HalfV)
        {
            hit = default;
            return false;
        }

        hit = new RayHit(t, p, denom > 0 ? Normal * -1 : Normal);
        return true;
    }

    /// <summary>
    /// Path length of a ray through the plate slab (entry face to back face), using the
    /// obliquity of the ray against the plate normal. Angle-only — frame invariant as
    /// long as the ray direction and the plate share a frame.
    /// </summary>
    public double SlabPathLength(Vec3 direction)
    {
        double cos = Math.Abs(direction.Normalized().Dot(Normal));
        return cos < 1e-6 ? double.MaxValue : ThicknessM / cos;
    }
}
