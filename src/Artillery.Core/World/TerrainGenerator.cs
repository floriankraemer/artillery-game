using Artillery.Core.Match;
using Artillery.Core.Util;

namespace Artillery.Core.World;

/// <summary>Landscape generator (GAME-MECHANICS §8). Deterministic given the passed <see cref="TpRandom"/>.</summary>
public static class TerrainGenerator
{
    /// <summary>Generate a full round landscape into <paramref name="world"/>.</summary>
    public static void Generate(WorldState world, LandscapeDef def, TpRandom rng)
    {
        world.Landscape = def;
        world.Gravity = def.Gravity;
        world.AirResistance = def.AirResistance;
        world.Effects.Clear();
        world.Props.Clear();

        Gen(world, def, rng);
        PlaceProps(world, def, rng);
    }

    private static void Gen(WorldState w, LandscapeDef def, TpRandom rng)
    {
        var h = w.Height;
        h.Fill(0);
        int b = def.Base;
        for (int r = 0; r <= 51; r++)
        for (int c = 0; c <= 51; c++)
        {
            int v = b + rng.Next(10) - rng.Next(20);
            h[r, c] = def.SmoothPasses < 2 ? b : v;
        }
        h.SetBorder(b + (def.HasWater ? 4 : 0));
        for (int i = 0; i < def.Spikes; i++)
        {
            int r = rng.Next(50) + 1, c = rng.Next(50) + 1;
            h[r, c] = rng.Next(def.SpikeMax);
        }
        for (int i = 0; i < def.SmoothPasses; i++) h.Smooth8();

        switch (def.Mode)
        {
            case GenMode.Pyramids: Pyramids(h, b, rng); break;
            case GenMode.Craters: Craters(h, b, rng); break;
            case GenMode.CanyonRiver: Canyon(h, b, rng); break;
            case GenMode.BigPyramid:
                for (int dr = -25; dr <= 25; dr++)
                for (int dc = -25; dc <= 25; dc++)
                    h[25 + dr, 25 + dc] = b + 100 - 4 * Math.Max(Math.Abs(dr), Math.Abs(dc));
                break;
            case GenMode.Sine:
                for (int r = 2; r <= 49; r++)
                for (int c = 2; c <= 49; c++)
                {
                    double a = (r - 1) * (r - 1) + (c - 1) * (c - 1), bb = (r + 1) * (r + 1) + (c + 1) * (c + 1);
                    h[r, c] = Pas.Clamp(Pas.Round(15 * Math.Sin(0.005 * bb) + 8 * Math.Cos(0.005 * a)), 0, 50);
                }
                break;
            case GenMode.ValleyRiver: Valley(h, rng); break;
            case GenMode.BlackHole:
                for (int i = 0; i < 3501; i++)
                {
                    int dr = rng.Next(20) - rng.Next(20), dc = rng.Next(20) - rng.Next(20);
                    int d = Math.Max(1, Pas.Round(Math.Sqrt(dr * dr + dc * dc)));
                    int v = Math.Min(50, Pas.Round(2.0 / d * 40));
                    h[25 + dr, 25 + dc] = b - v + 3;
                }
                break;
            case GenMode.Island:
                for (int r = -1; r <= 51; r++)
                for (int c = -1; c <= 51; c++)
                    if (Pas.Round(Math.Sqrt((25 - r) * (25 - r) + (25 - c) * (25 - c))) > rng.Next(5) + 20) h[r, c] = 0;
                for (int i = 0; i < 3; i++) h.Smooth8();
                break;
            case GenMode.Volcano: Volcano(w, b, rng); break;
            case GenMode.Mandelbrot: Mandelbrot(h); break;
            case GenMode.City: City(h, rng); break;
            case GenMode.JungleRiver: Jungle(h, def.WaterLevel, rng); break;
            case GenMode.Terraces:
                for (int r = 1; r <= 50; r++)
                for (int c = 1; c <= 50; c++)
                {
                    int v = Pas.Round(h[r, c] / 1.5) + 7;
                    if (v > 18) v = 48 + rng.Next(2);
                    if (v > 12 && v < 20) v += 15;
                    h[r, c] = v;
                }
                break;
        }
        h.SetBorder(0);
    }

    private static void Pyramids(Heightmap h, int b, TpRandom rng)
    {
        int pr = 0, pc = 0;
        for (int k = 0; k < 2; k++)
        {
            int cr, cc, tries = 0;
            do { cr = rng.Next(40) + 5; cc = rng.Next(40) + 5; }
            while (!(Math.Abs(cr - pr) > 16 && Math.Abs(cc - pc) > 16) && ++tries < 1000);
            for (int dr = -8; dr <= 8; dr++)
            for (int dc = -8; dc <= 8; dc++)
                h[cr + dr, cc + dc] = b + 40 - 5 * Math.Max(Math.Abs(dr), Math.Abs(dc));
            pr = cr; pc = cc;
        }
    }

    private static void Craters(Heightmap h, int b, TpRandom rng)
    {
        int count = rng.Next(4) + 1;
        var centres = new List<(int r, int c)>();
        for (int k = 0; k < count; k++)
        {
            int cr, cc, tries = 0;
            bool bad;
            do
            {
                cr = rng.Next(45) + 2; cc = rng.Next(45) + 2;
                bad = centres.Any(p => Math.Abs(cr - p.r) <= 12 && Math.Abs(cc - p.c) <= 12);
            } while (bad && ++tries < 1000);
            centres.Add((cr, cc));
            int R = rng.Next(5) + 4;
            rng.Next(5); // original draws an unused Random(5)+6
            for (int i = 1; i <= 1800; i++)
            {
                double a = i / 100.0;
                int rho = rng.Next(R);
                int row = Pas.Round(Math.Sin(a) * rho + cr), col = Pas.Round(Math.Cos(a) * rho + cc);
                if (row > 0 && col > 0 && row < 50 && col < 50) h[row, col] = b + rho - 2 * R - 3;
            }
            for (int i = 1; i <= 640; i++)
            {
                double a = i / 100.0;
                int row = Pas.Round(Math.Sin(a) * (R + 1) + cr), col = Pas.Round(Math.Cos(a) * (R + 1) + cc);
                if (row > 0 && col > 0 && row < 50 && col < 50) h[row, col] = b;
            }
        }
    }

    /// <summary>Meandering line walk (modes 3 and 12); stamps (distance, plusSide, minusSide) rings, outermost first.</summary>
    private static void Walk(Heightmap h, TpRandom rng, double step, double jitter, int centre,
        (double d, int plus, int minus)[] rings)
    {
        bool alongCol = rng.Next(2) == 0;
        double a = alongCol ? 0 : Math.PI / 2;
        int start = rng.Next(10) + 20;
        double row = alongCol ? start : 0, col = alongCol ? 0 : start;
        for (int n = 0; n < 50000; n++)
        {
            row += Math.Sin(a) * step; col += Math.Cos(a) * step;
            if (row < 0 || col < 0 || row > 50 || col > 50) break;
            h[Pas.Round(row), Pas.Round(col)] = centre;
            foreach (var (d, plus, minus) in rings)
            {
                h[Pas.Round(row + Math.Sin(a + Math.PI / 2) * d), Pas.Round(col + Math.Cos(a + Math.PI / 2) * d)] = plus;
                h[Pas.Round(row + Math.Sin(a - Math.PI / 2) * d), Pas.Round(col + Math.Cos(a - Math.PI / 2) * d)] = minus;
            }
            if (rng.Next(5) == 0) a += jitter;
            if (rng.Next(5) == 0) a -= jitter;
        }
    }

    private static void Canyon(Heightmap h, int b, TpRandom rng) =>
        Walk(h, rng, 0.25, 0.12, -5, new[]
        {
            (3.0, b - 10, b - 10), (2.5, b - 15, b - 15), (2.0, 8, 8), (1.5, 4, 4), (1.0, 0, 0), (0.5, -2, -2), (0.2, -5, -5),
        });

    private static void Jungle(Heightmap h, int W, TpRandom rng)
    {
        Walk(h, rng, 0.25, 0.06, W, new[]
        {
            (2.4, W + 10, W + 10), (2.0, W + 4, W + 4), (1.6, W, W), (1.2, W, W), (1.0, W, W - 6),
            (0.8, W - 6, W - 6), (0.5, W - 6, W - 6), (0.2, W - 6, W - 6),
        });
        h.Smooth8(); h.Smooth8();
    }

    private static void Valley(Heightmap h, TpRandom rng)
    {
        bool alongCol = rng.Next(2) == 0;
        double a = alongCol ? 0 : Math.PI / 2;
        int start = rng.Next(10) + 20;
        double row = alongCol ? start : -5, col = alongCol ? -5 : start;
        for (int n = 0; n < 50000; n++)
        {
            row += Math.Sin(a) / 3; col += Math.Cos(a) / 3;
            if (row < -5 || col < -5 || row > 55 || col > 55) break;
            h[Pas.Round(row), Pas.Round(col)] = -10;
            for (int d = 1; d <= 50; d++)
                for (int side = -1; side <= 1; side += 2)
                {
                    int rr = Pas.Round(row + Math.Sin(a + side * Math.PI / 2) * d), cc = Pas.Round(col + Math.Cos(a + side * Math.PI / 2) * d);
                    if (rr > 0 && cc > 0 && rr < 50 && cc < 50) h[rr, cc] = Pas.Round(2.0 * d) + rng.Next(10) - rng.Next(10);
                }
            if (rng.Next(5) == 0) a += 0.05;
            if (rng.Next(5) == 0) a -= 0.05;
        }
        for (int i = 0; i < 3; i++) h.Smooth5();
    }

    private static void Volcano(WorldState w, int b, TpRandom rng)
    {
        var h = w.Height;
        int cr = 25 + rng.Next(10) - rng.Next(10), cc = 25 + rng.Next(10) - rng.Next(10);
        for (int i = 0; i < 2501; i++)
        {
            int dr = rng.Next(25) - rng.Next(25), dc = rng.Next(25) - rng.Next(25);
            int d = Math.Max(1, Pas.Round(Math.Sqrt(dr * dr + dc * dc)));
            int v = Math.Min(55, Pas.Round(1.7 / d * 190));
            int r = cr + dr, c = cc + dc;
            if (r > 0 && c > 0 && r < 50 && c < 50) h[r, c] = b + v + rng.Next(4);
        }
        for (int i = 1; i <= 1800; i++)
        {
            double a = i / 100.0;
            int rho = rng.Next(4);
            h[Pas.Round(Math.Sin(a) * rho + cr), Pas.Round(Math.Cos(a) * rho + cc)] = b + 48;
        }
        // lava seeds: centre, +-1/+-2 orthogonal, +-1 diagonal (13 cells)
        for (int dr = -2; dr <= 2; dr++)
        for (int dc = -2; dc <= 2; dc++)
        {
            bool orth = dr == 0 || dc == 0, diag = Math.Abs(dr) == 1 && Math.Abs(dc) == 1;
            if (orth || diag) w.Effects[cr + dr, cc + dc] = 22;
        }
        h.Smooth8(); h.Smooth8();
    }

    private static void Mandelbrot(Heightmap h)
    {
        for (int i = 0; i <= 50; i++)
        for (int j = 0; j <= 50; j++)
        {
            double cx = (i + 0.5) * 0.056 - 2.2, cy = (j + 0.5) * 0.052 - 1.3, x = 0, y = 0;
            int n = 0;
            while (n < 16 && x * x + y * y <= 5) { double t = x * x - y * y + cx; y = 2 * x * y + cy; x = t; n++; }
            int ret = (n % 16) + 32;
            h[50 - i, j] = (ret - 30) * 2;
        }
    }

    private static void City(Heightmap h, TpRandom rng)
    {
        int sr = rng.Next(5) + 1, sc = rng.Next(5) + 1; // fix: original left one index uninitialised
        for (int i = 1; i <= 5; i++)
        for (int j = 1; j <= 5; j++)
        {
            if (i == sr || j == sc) continue;
            int hh = rng.Next(50) + 10;
            for (int r = 8 * i - 1; r <= 8 * i + 4; r++)
            for (int c = 8 * j - 1; c <= 8 * j + 4; c++) h[r, c] = hh;
        }
    }

    // ---- object placer ----

    private static void PlaceProps(WorldState w, LandscapeDef def, TpRandom rng)
    {
        for (int i = 0; i < def.Props.Count; i++)
        {
            var p = def.Props[i];
            Place(w, p, rng);
            // Archipelago: one extra scatter of each of the two kinds with probability 1/2
            if (def.Id == 16 && i < 2 && rng.Next(2) == 0) Place(w, p, rng);
        }
    }

    private static void Place(WorldState w, PropScatter p, TpRandom rng)
    {
        var h = w.Height;
        int r = 0, c = 0;
        for (int t = 0; t < 100; t++)
        {
            r = rng.Next(48) + 1; c = rng.Next(48) + 1;
            if (h[r, c] > p.MinHeight - 5 && h[r, c] < p.MaxHeight + 5) break;
        }
        for (int k = 0; k < p.Count; k++)
        {
            int d = rng.Next(p.Radius);
            if (rng.Next(5) == 0) d += 4;
            double a = rng.Next(10000);
            int rr = Pas.Round(Math.Sin(a) * d + r), cc = Pas.Round(Math.Cos(a) * d + c);
            if (rr > 1 && rr < 48 && cc > 1 && cc < 48 && h[rr, cc] > p.MinHeight && h[rr, cc] < p.MaxHeight)
                w.Props[rr, cc] = p.Kind;
        }
    }

    /// <summary>Metamorphosis per-turn terrain morph.</summary>
    public static void Morph(WorldState world, TpRandom rng)
    {
        var h = world.Height;
        for (int r = 1; r <= 50; r++)
        for (int c = 1; c <= 50; c++)
        {
            if (rng.Next(3) != 0) continue;
            int v = h[r, c] + rng.Next(65) - rng.Next(65);
            if (v > 100) v = 100 - rng.Next(25);
            if (v < -20) v = -rng.Next(20);
            h[r, c] = v;
        }
        for (int i = 0; i < 3; i++) h.Smooth5();
    }
}
