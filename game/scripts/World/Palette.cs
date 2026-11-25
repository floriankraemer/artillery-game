using Godot;

namespace Artillery.Game.World;

/// <summary>Terrain palette schemes. Ramp = 19 height bands (colours 200..218); DAC values 0..63 divided by 63.</summary>
public static class Palette
{
    public const int Bands = 19;

    private static Color Dac(double r, double g, double b) =>
        new((float)Math.Clamp(Math.Round(r) / 63.0, 0, 1), (float)Math.Clamp(Math.Round(g) / 63.0, 0, 1), (float)Math.Clamp(Math.Round(b) / 63.0, 0, 1));

    private static readonly Color[] Scheme12 =
    {
        Dac(6,14,22), Dac(9,17,25), Dac(42,35,19), Dac(12,20,8), Dac(12,20,8), Dac(8,24,12), Dac(16,23,4), Dac(16,24,12), Dac(20,24,14),
        Dac(28,28,16), Dac(32,28,20), Dac(32,29,23), Dac(32,30,26), Dac(32,31,29), Dac(32,32,31), Dac(32,32,31), Dac(47,47,48), Dac(48,48,47), Dac(48,47,48),
    };

    private static Color Dark(Color c) => new(c.R * 0.55f, c.G * 0.55f, c.B * 0.55f);

    /// <summary>The 19-band ramp for a scheme (1..8, 11..15; others fall back to the earth ramp).</summary>
    public static Color[] Ramp(int scheme)
    {
        if (scheme == 12) return (Color[])Scheme12.Clone();
        if (scheme == 6) return HillsRamp();
        return Tune(RawRamp(scheme));
    }

    private static Color[] HillsRamp()
    {
        var lo = new Color(0.10f, 0.22f, 0.09f); var mid = new Color(0.36f, 0.52f, 0.17f); var hi = new Color(0.78f, 0.74f, 0.52f);
        var r = new Color[Bands];
        for (int i = 0; i < Bands; i++) { float t = i / (Bands - 1f); r[i] = t < 0.55f ? lo.Lerp(mid, t / 0.55f) : mid.Lerp(hi, (t - 0.55f) / 0.45f); }
        return r;
    }

    /// <summary>Keeps hue and band ordering but avoids clipping (max channel 0.85), over-saturation and pitch-black bands.</summary>
    private static Color[] Tune(Color[] r)
    {
        float m = 0.01f;
        foreach (var c in r) m = Math.Max(m, Math.Max(c.R, Math.Max(c.G, c.B)));
        float k = 0.9f / m;
        for (int i = 0; i < r.Length; i++)
        {
            var c = r[i]; float l = 0.3f * c.R + 0.59f * c.G + 0.11f * c.B;
            float R = Mix(l, c.R, 0.95f) * k, G = Mix(l, c.G, 0.95f) * k, B = Mix(l, c.B, 0.95f) * k;
            r[i] = new Color(0.14f + 0.86f * R, 0.14f + 0.86f * G, 0.14f + 0.86f * B);
        }
        return r;
    }

    private static float Mix(float a, float b, float t) => a + (b - a) * t;

    private static Color[] RawRamp(int scheme)
    {
        var r = new Color[Bands];
        for (int i = 0; i < Bands; i++)
        {
            int e = i & ~1; // even partner for the odd-black schemes (7, 8)
            r[i] = scheme switch
            {
                1 or 11 => Dac(3.6 * i, 3.6 * i, 3.6 * i),
                2 => Dac(5, 3.4 * i + 5, 3.4 * i + 5),
                3 or 13 => Dac(3.4 * i + 5, 5, 5),
                5 => Dac(5, 5, 3 * i + 5),
                6 => Dac(5, 3.4 * i + 5, i),
                7 => (i & 1) == 1 ? Dark(Dac(3.6 * e + 5, 3.6 * e + 5, 3.6 * e + 5)) : Dac(3.6 * i + 5, 3.6 * i + 5, 3.6 * i + 5),
                8 => (i & 1) == 1 ? Dark(Dac(2, 3.6 * e + 5, 2)) : Dac(2, 3.6 * i + 5, 2),
                15 => Dac(i * 7 / 3.0 + 2, 2, i * 5 / 3.0 + 2),
                _ => Dac(3.5 * i, 2.3 * i, 1.22 * i), // 4, 14: earth
            };
        }
        return r;
    }

    /// <summary>Water colour for a scheme (200/201 entries).</summary>
    public static Color Water(int scheme) => scheme switch
    {
        11 => Dac(5, 5, 45),
        12 => Dac(9, 17, 25),
        13 => Dac(50, 7, 0),
        _ => Dac(5, 5, 30),
    };
}
