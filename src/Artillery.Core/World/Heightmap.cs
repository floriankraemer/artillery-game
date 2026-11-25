namespace Artillery.Core.World;

/// <summary>
/// Integer heightmap indexed [x, y] for x, y in -5..55 (61x61). Playable cells are 1..50.
/// x = grid row (bearing 0 / "north" direction), y = grid column. Heights are in simulation height units.
/// </summary>
public sealed class Heightmap
{
    public const int Min = -5;
    public const int Max = 55;
    public const int Size = Max - Min + 1; // 61
    public const int PlayMin = 1;
    public const int PlayMax = 50;

    private readonly int[] _h = new int[Size * Size];

    public static bool InStorage(int x, int y) => x >= Min && x <= Max && y >= Min && y <= Max;

    /// <summary>Reads return 0 outside storage; writes outside storage are ignored (original code wrote unchecked).</summary>
    public int this[int x, int y]
    {
        get => InStorage(x, y) ? _h[(x - Min) * Size + (y - Min)] : 0;
        set { if (InStorage(x, y)) _h[(x - Min) * Size + (y - Min)] = value; }
    }

    /// <summary>Bilinear terrain height at a real position (original FUN_3164_76cd).</summary>
    public double HeightAt(double x, double y)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        double fx = x - ix, fy = y - iy;
        return this[ix, iy] * (1 - fx) * (1 - fy)
             + this[ix + 1, iy] * fx * (1 - fy)
             + this[ix + 1, iy + 1] * fx * fy
             + this[ix, iy + 1] * (1 - fx) * fy;
    }

    public void Fill(int value) => Array.Fill(_h, value);

    public Heightmap Clone()
    {
        var c = new Heightmap();
        Array.Copy(_h, c._h, _h.Length);
        return c;
    }

    public void CopyFrom(Heightmap other) => Array.Copy(other._h, _h, _h.Length);

    /// <summary>
    /// Symmetric 8-neighbour smoothing over 1..50 (fixed version of the original kernel, which double-counted one diagonal).
    /// t = (sum of 8 neighbours) div 8, written via a temp copy. Integer division truncates toward zero like Pascal div.
    /// </summary>
    public void Smooth8()
    {
        var t = Clone();
        for (int x = 1; x <= 50; x++)
        for (int y = 1; y <= 50; y++)
        {
            int s = this[x - 1, y] + this[x + 1, y] + this[x, y - 1] + this[x, y + 1]
                  + this[x - 1, y - 1] + this[x + 1, y + 1] + this[x - 1, y + 1] + this[x + 1, y - 1];
            t[x, y] = s / 8;
        }
        CopyFrom(t);
    }

    /// <summary>
    /// 5-point smoothing (cell + 4-neighbours inside 1..49), Round(sum/count), over cells 1..49 (original FUN_185e_00cf, fixed edge copy).
    /// </summary>
    public void Smooth5()
    {
        var t = Clone();
        for (int x = 1; x <= 49; x++)
        for (int y = 1; y <= 49; y++)
        {
            int sum = this[x, y], n = 1;
            void Add(int a, int b) { if (a >= 1 && a <= 49 && b >= 1 && b <= 49) { sum += this[a, b]; n++; } }
            Add(x - 1, y); Add(x + 1, y); Add(x, y - 1); Add(x, y + 1);
            t[x, y] = Util.Pas.Round((double)sum / n);
        }
        CopyFrom(t);
    }

    /// <summary>Set the border ring (row/col 1 and 50) to a value. The original zeroes it after generation.</summary>
    public void SetBorder(int value)
    {
        for (int i = 1; i <= 50; i++)
        {
            this[i, 1] = value; this[1, i] = value; this[50, i] = value; this[i, 50] = value;
        }
    }
}
