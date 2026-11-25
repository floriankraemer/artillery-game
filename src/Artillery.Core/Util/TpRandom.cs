namespace Artillery.Core.Util;

/// <summary>
/// Port of the Turbo Pascal 7 System.Random generator (32-bit LCG).
/// seed = seed * 0x08088405 + 1 (mod 2^32); Random(n) = (seed * n) >> 32.
/// Deterministic: the whole simulation must draw randomness only from an instance of this class.
/// </summary>
public sealed class TpRandom
{
    public uint Seed { get; set; }

    public TpRandom(uint seed) => Seed = seed;

    private uint Step()
    {
        Seed = unchecked(Seed * 0x08088405u + 1u);
        return Seed;
    }

    /// <summary>Random(n): integer in 0..n-1. Returns 0 for n &lt;= 0 (as TP does).</summary>
    public int Next(int n)
    {
        uint s = Step();
        if (n <= 0) return 0;
        return (int)(((ulong)s * (uint)n) >> 32);
    }

    /// <summary>Random (Real): value in [0,1).</summary>
    public double NextDouble() => Step() / 4294967296.0;

    /// <summary>Random(n) - Random(n) style symmetric jitter helper.</summary>
    public int Jitter(int n) => Next(n) - Next(n);

    public TpRandom Clone() => new(Seed);
}
