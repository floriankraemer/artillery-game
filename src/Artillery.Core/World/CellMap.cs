namespace Artillery.Core.World;

/// <summary>Per-cell byte map with the same -5..55 indexing as <see cref="Heightmap"/>.</summary>
public sealed class CellMap
{
    private readonly byte[] _v = new byte[Heightmap.Size * Heightmap.Size];

    public byte this[int x, int y]
    {
        get => Heightmap.InStorage(x, y) ? _v[(x - Heightmap.Min) * Heightmap.Size + (y - Heightmap.Min)] : (byte)0;
        set { if (Heightmap.InStorage(x, y)) _v[(x - Heightmap.Min) * Heightmap.Size + (y - Heightmap.Min)] = value; }
    }

    public void Clear() => Array.Clear(_v);

    public CellMap Clone()
    {
        var c = new CellMap();
        Array.Copy(_v, c._v, _v.Length);
        return c;
    }
}

/// <summary>
/// Ground effect cell values (original effect map at DS:0xd04e).
/// 0 none; 1..5 acid (strength); 6..10 fire (strength); 11 goo centre; 12 goo; 13/14 burning goo; >=20 lava (Volcano seeds 22).
/// </summary>
public static class Effect
{
    public const byte None = 0;
    public static bool IsAcid(byte v) => v >= 1 && v <= 5;
    public static bool IsFire(byte v) => v >= 6 && v <= 10;
    public static bool IsGoo(byte v) => v == 11 || v == 12;
    public static bool IsBurningGoo(byte v) => v == 13 || v == 14;
    public static bool IsLava(byte v) => v >= 20;
    /// <summary>Hot cells attract heat-seeking shells (fire or lava).</summary>
    public static bool IsHot(byte v) => IsFire(v) || IsLava(v);
}

/// <summary>Object (prop) kinds on the object map. +0x10 marks a burnt object.</summary>
public static class Prop
{
    public const byte None = 0;
    public const byte Tree = 1;
    public const byte TreeDark = 2;
    public const byte Palm = 3;
    public const byte Bush = 4;
    public const byte Grave = 0x11;
    public const byte DeadBush = 0x12;
    public const byte Stump = 0x13;
    public const byte BurntFlag = 0x10;
}
