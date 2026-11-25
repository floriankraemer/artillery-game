using Artillery.Core.Match;

namespace Artillery.Core.World;

/// <summary>Round-start tank placement (GAME-MECHANICS §8).</summary>
public static class TankPlacement
{
    /// <summary>Places tanks 1..N on the current world and flattens a 4x4 pad (dx,dy in -1..2) under each.</summary>
    public static void Place(MatchState s)
    {
        var w = s.World;
        var h = w.Height;
        int n = s.PlayerCount;
        int D = n <= 2 ? 45 : n == 3 ? 40 : 35;
        int attempts = 0;
        for (int i = 1; i <= n; i++)
        {
            var t = s.Tanks[i];
            int x, y;
            bool bad;
            do
            {
                x = s.Rng.Next(40) + 5; y = s.Rng.Next(40) + 5;
                bad = false;
                for (int j = 1; j < i; j++)
                    if (Math.Abs(x - s.Tanks[j].X) < D && Math.Abs(y - s.Tanks[j].Y) < D) { bad = true; break; }
                if (w.Landscape?.VolcanoPlacement == true && h.HeightAt(x, y) > 45) bad = true;
                if (w.HasWater && h[x, y] < w.WaterLevel + 5) bad = true;
                if (bad && s.Rng.Next(20) == 1) D--;
                attempts++;
            } while (bad && attempts <= 1000);

            int g = h[x, y];
            for (int dx = -1; dx <= 2; dx++)
            for (int dy = -1; dy <= 2; dy++) h[x + dx, y + dy] = g;
            t.X = x; t.Y = y;
            t.Z = h.HeightAt(x + 0.5, y + 0.5) + 0.5;
        }
    }
}
