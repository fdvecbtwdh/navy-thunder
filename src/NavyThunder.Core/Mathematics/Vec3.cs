namespace NavyThunder.Core.Mathematics;

/// <summary>Double-precision 3D vector. All combat math uses double for cross-platform determinism.</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator -(Vec3 a) => new(-a.X, -a.Y, -a.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator *(double s, Vec3 a) => a * s;
    public static Vec3 operator /(Vec3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);

    public double Dot(Vec3 o) => X * o.X + Y * o.Y + Z * o.Z;

    public Vec3 Cross(Vec3 o) => new(
        Y * o.Z - Z * o.Y,
        Z * o.X - X * o.Z,
        X * o.Y - Y * o.X);

    public double LengthSquared => Dot(this);
    public double Length => Math.Sqrt(LengthSquared);

    public Vec3 Normalized()
    {
        double l = Length;
        return l > 0 ? this / l : Zero;
    }

    public static double Distance(Vec3 a, Vec3 b) => (a - b).Length;

    public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
}
