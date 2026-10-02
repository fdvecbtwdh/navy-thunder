using NavyThunder.Core.Mathematics;

namespace NavyThunder.Core.Geometry;

/// <summary>
/// Rigid placement of a ship's local frame in the world (Phase 01: translation +
/// yaw only — pitch/roll arrive with the float-state phase and slot into the same
/// rotations). Conventions (PROJECT_DESIGN §5.2):
///   World: Y up, combat on the XZ plane.
///   Ship local: +X starboard, +Y up, +Z bow; origin = waterline centre.
///   Heading h (deg): local +Z maps to world (sin h, 0, cos h) — the same convention
///   ShipNavigationSystem uses to integrate movement, so bow-forward at heading 90°
///   faces world +X.
/// Hit detection transforms the RAY with <see cref="ToLocal"/>/<see cref="ToLocalDirection"/>;
/// armor and part geometry stay local forever (PROJECT_DESIGN §5.2 ruling).
/// </summary>
public readonly struct ShipTransform
{
    public Vec3 Position { get; }
    public double HeadingDeg { get; }

    private readonly double _cos;
    private readonly double _sin;

    public ShipTransform(Vec3 position, double headingDeg)
    {
        Position = position;
        HeadingDeg = headingDeg;
        double rad = headingDeg * Math.PI / 180.0;
        _cos = Math.Cos(rad);
        _sin = Math.Sin(rad);
    }

    public static ShipTransform Identity => default;

    /// <summary>Local point → world point (translation + rotation).</summary>
    public Vec3 ToWorld(Vec3 local) => new(
        Position.X + local.X * _cos + local.Z * _sin,
        Position.Y + local.Y,
        Position.Z - local.X * _sin + local.Z * _cos);

    /// <summary>World point → local point (inverse translation + rotation).</summary>
    public Vec3 ToLocal(Vec3 world)
    {
        double dx = world.X - Position.X;
        double dz = world.Z - Position.Z;
        return new Vec3(
            dx * _cos - dz * _sin,
            world.Y - Position.Y,
            dx * _sin + dz * _cos);
    }

    /// <summary>Local direction → world direction (rotation only — never feed points here).</summary>
    public Vec3 ToWorldDirection(Vec3 localDirection) => new(
        localDirection.X * _cos + localDirection.Z * _sin,
        localDirection.Y,
        -localDirection.X * _sin + localDirection.Z * _cos);

    /// <summary>World direction → local direction (inverse rotation only).</summary>
    public Vec3 ToLocalDirection(Vec3 worldDirection)
    {
        return new Vec3(
            worldDirection.X * _cos - worldDirection.Z * _sin,
            worldDirection.Y,
            worldDirection.X * _sin + worldDirection.Z * _cos);
    }
}
