using Artillery.Core.Match;
using Artillery.Core.Util;
using Artillery.Core.World;

namespace Artillery.Core.Combat;

/// <summary>Per-turn ground effect routine and landscape tank drift.</summary>
public static class EffectTicker
{
    /// <summary>
    /// Runs once after every shot: damages tanks standing on effect cells (shield first, no score), then decays/spreads effects:
    /// acid v -= Random(2) and lowers terrain by v div 3; fire v -= Random(2) (cleared below 6), lowers terrain by 1 only on melting
    /// landscapes, and burns props; goo spreads to lower orthogonal neighbours (p=8/10); burning goo ignites goo within 4 cells and burns out
    /// (p=1/4 per turn, chosen); lava makes 6 spread attempts per turn onto equal-or-lower neighbours. Then Gravity Well tank drift.
    /// </summary>
    public static void Tick(MatchState s, List<GameEvent> ev)
    {
        var w = s.World;
        var fx = w.Effects;
        bool any = false;

        // 1. damage to tanks standing on a cell (uses pre-decay values)
        foreach (var t in s.AllTanks)
        {
            if (!t.Alive) continue;
            int v = fx[(int)t.X, (int)t.Y];
            int dmg = v switch
            {
                0 => 0,
                >= 20 => 20 + s.Rng.Next(60),
                13 or 14 => 50 + s.Rng.Next(100),
                11 or 12 => 20 + s.Rng.Next(20),
                >= 6 and <= 10 => (v - 5) * 15 + s.Rng.Next(10),
                _ => v * 10 + s.Rng.Next(10),
            };
            if (dmg > 0) Blast.Hurt(t, dmg, ev);
        }

        // 2. decay / spread (reads the old map, writes the live one)
        var old = fx.Clone();
        bool melts = w.Landscape?.MeltsTerrain == true;
        var lava = new List<(int, int)>();
        for (int x = Heightmap.PlayMin; x <= Heightmap.PlayMax; x++)
        for (int y = Heightmap.PlayMin; y <= Heightmap.PlayMax; y++)
        {
            int v = old[x, y];
            if (v == 0) continue;
            any = true;
            if (Effect.IsAcid((byte)v))
            {
                v -= s.Rng.Next(2);
                if (v > 0) w.Height[x, y] -= v / 3;
                fx[x, y] = (byte)Math.Max(v, 0);
            }
            else if (Effect.IsFire((byte)v))
            {
                v -= s.Rng.Next(2);
                if (melts) w.Height[x, y] -= 1;
                Burn(w, x, y);
                fx[x, y] = v < 6 ? (byte)0 : (byte)v;
            }
            else if (Effect.IsGoo((byte)v))
            {
                foreach (var (dx, dy) in Specials.Dirs4)
                {
                    int nx = x + dx, ny = y + dy;
                    if (!Blast.Inside(nx, ny) || fx[nx, ny] != 0 || w.Height[nx, ny] >= w.Height[x, y]) continue;
                    if (s.Rng.Next(10) < 8) fx[nx, ny] = 12;
                }
            }
            else if (Effect.IsBurningGoo((byte)v))
            {
                int nx = x + s.Rng.Next(9) - 4, ny = y + s.Rng.Next(9) - 4;
                if (Blast.Inside(nx, ny) && Effect.IsGoo(fx[nx, ny])) fx[nx, ny] = (byte)(fx[nx, ny] + 2);
                Burn(w, x, y);
                if (s.Rng.Next(4) == 0) fx[x, y] = 0;
            }
            else if (Effect.IsLava((byte)v))
            {
                lava.Add((x, y));
                Burn(w, x, y);
            }
        }
        if (lava.Count > 0)
            for (int i = 0; i < 6; i++)
            {
                var (x, y) = lava[s.Rng.Next(lava.Count)];
                var (dx, dy) = Specials.Dirs4[s.Rng.Next(4)];
                int nx = x + dx, ny = y + dy;
                if (!Blast.Inside(nx, ny) || fx[nx, ny] >= 20 || w.Height[nx, ny] > w.Height[x, y]) continue;
                fx[nx, ny] = fx[x, y];
                lava.Add((nx, ny));
            }

        if (any) ev.Add(new TerrainChangedEvent());

        // 3. Gravity Well drift
        if (w.Landscape?.TankDrift == true)
            foreach (var t in s.AllTanks)
            {
                if (!t.Alive) continue;
                var from = t.Position;
                t.X = Pas.Clamp(t.X + s.Rng.Next(3) - 1, 5, 45);
                t.Y = Pas.Clamp(t.Y + s.Rng.Next(3) - 1, 5, 45);
                t.Z = w.Height[(int)t.X, (int)t.Y] + 0.5;
                if (from.X != t.X || from.Y != t.Y) ev.Add(new TankMovedEvent(t.Index, from, t.Position, MoveKind.Drift));
            }
    }

    /// <summary>Props on a burning cell are scorched (burnt flag).</summary>
    private static void Burn(WorldState w, int x, int y)
    {
        byte p = w.Props[x, y];
        if (p >= 1 && p <= 15) w.Props[x, y] = (byte)(p + Prop.BurntFlag);
    }
}
