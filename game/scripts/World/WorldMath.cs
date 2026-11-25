using Godot;
using Artillery.Core.Util;

namespace Artillery.Game.World;

/// <summary>
/// Simulation space to Godot space (GODOT-PLAN §2).
/// Sim: X = grid row ("north", bearing 0), Y = grid column (bearing 90 points to -Y, see Ballistics.LaunchVelocity), Z = height units (up).
/// Godot (right-handed, Y up, -Z forward): north = -Z, east = +X, up = +Y.
///   gx = Center - simY,  gz = Center - simX,  gy = simZ * HeightScale.
/// Right-handed check: sim (north, west, up) maps onto Godot (-Z, -X, +Y), a proper rotation. Vertices 1..50 span 49 units centred on the origin.
/// Bearing b (deg): sim direction (cos b, -sin b) maps to Godot (sin b, 0, -cos b); a node's RotationY for it is -b.
/// </summary>
public static class WorldMath
{
    public const float HeightScale = 0.25f;
    public const float CellSize = 1f;
    public const float Center = 25.5f;
    public const float BaseY = -3f;

    public static Vector3 ToGodot(double x, double y, double z) => new((float)(Center - y), (float)(z * HeightScale), (float)(Center - x));
    public static Vector3 ToGodot(Vec3d v) => ToGodot(v.X, v.Y, v.Z);
    /// <summary>Godot horizontal position to sim (x row, y column).</summary>
    public static (double x, double y) ToSim(Vector3 g) => (Center - g.Z, Center - g.X);
    /// <summary>Sim horizontal direction (dx, dy) to a Godot direction.</summary>
    public static Vector3 SimDir(double dx, double dy) => new((float)-dy, 0, (float)-dx);
    /// <summary>Yaw (radians, about +Y) of a turret for a sim bearing in degrees.</summary>
    public static float BearingToYaw(double bearingDeg) => (float)(-bearingDeg * Math.PI / 180.0);

    /// <summary>Player colours 1..4: blue, red, green, purple.</summary>
    public static Color PlayerColor(int index, bool light = false)
    {
        int i = Math.Clamp(index, 1, 4) - 1;
        Color[] mid = { new(10 / 63f, 10 / 63f, 43 / 63f), new(43 / 63f, 10 / 63f, 10 / 63f), new(10 / 63f, 43 / 63f, 10 / 63f), new(43 / 63f, 10 / 63f, 43 / 63f) };
        Color[] lt = { new(30 / 63f, 30 / 63f, 1), new(1, 30 / 63f, 30 / 63f), new(30 / 63f, 1, 30 / 63f), new(1, 30 / 63f, 1) };
        return light ? lt[i] : mid[i].Lerp(lt[i], 0.35f);
    }

    public static StandardMaterial3D VertexColorMaterial(bool transparent = false, bool unshaded = false) => new()
    {
        VertexColorUseAsAlbedo = true,
        VertexColorIsSrgb = true,
        Roughness = 1f,
        Transparency = transparent ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled,
        ShadingMode = unshaded ? BaseMaterial3D.ShadingModeEnum.Unshaded : BaseMaterial3D.ShadingModeEnum.PerPixel,
    };
}
