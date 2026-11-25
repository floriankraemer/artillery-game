namespace Artillery.Core.Util;

/// <summary>Double-precision vector in simulation space: X = grid row ("north"), Y = grid column, Z = up (height units).</summary>
public readonly record struct Vec3d(double X, double Y, double Z)
{
    public static Vec3d operator +(Vec3d a, Vec3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3d operator -(Vec3d a, Vec3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3d operator *(Vec3d a, double k) => new(a.X * k, a.Y * k, a.Z * k);
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    public double HorizontalLength => Math.Sqrt(X * X + Y * Y);
    public override string ToString() => $"({X:0.###}, {Y:0.###}, {Z:0.###})";
}
