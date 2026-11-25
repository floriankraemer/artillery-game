using Artillery.Core.Items;
using Artillery.Core.Match;
using Artillery.Core.Util;
using Artillery.Core.World;

namespace Artillery.Core.Combat;

/// <summary>Crater digging and the standard blast damage pipeline (GAME-MECHANICS §5).</summary>
public static class Blast
{
    /// <summary>Unknown in the original (the "k" of <c>Random(30) + R*k &gt; 30</c>); chosen so big blasts clear most effects.</summary>
    private const int DampK = 3;

    /// <summary>Playable-cell test used by every effect (cells 2..49, i.e. strictly inside the border ring).</summary>
    internal static bool Inside(int x, int y) => x > 1 && x < 50 && y > 1 && y < 50;

    internal static int Dist2D(double ax, double ay, double bx, double by)
        => Pas.Round(Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by)));

    internal static void AddScore(MatchState s, int idx, long delta, string reason, List<GameEvent> ev)
    {
        if (idx < 1 || idx > s.PlayerCount || delta == 0) return;
        s[idx].RoundScore += delta;
        ev.Add(new ScoreEvent(idx, delta, reason));
    }

    /// <summary>Applies damage (shield first) and emits a <see cref="TankDamagedEvent"/>. Returns damage credited (capped at hull+shield).</summary>
    internal static int Hurt(Tank t, int dmg, List<GameEvent> ev)
    {
        int h0 = t.Hull, s0 = t.Shield;
        int credited = t.ApplyDamage(dmg);
        if (credited > 0 || h0 != t.Hull || s0 != t.Shield)
            ev.Add(new TankDamagedEvent(t.Index, h0 - Math.Max(t.Hull, 0), s0 - t.Shield, t.Hull, t.Shield));
        return credited;
    }

    /// <summary>Weapon definition used for hit bonus of indirect blasts (death explosions): the shooter's selected weapon.</summary>
    internal static WeaponDef BonusDef(MatchState s, int shooter)
    {
        if (shooter >= 1 && shooter <= s.PlayerCount)
        {
            var id = s[shooter].SelectedWeapon;
            var def = ItemCatalog.Weapons.FirstOrDefault(w => w.Id == id);
            if (def != null) return def;
        }
        return ItemCatalog.Get(WeaponId.BasicShell);
    }

    /// <summary>
    /// Explosion with digging (original FUN_185e_1922, dig = 1; §5 / 02 §2c). Only ever lowers terrain.
    /// Emits <see cref="ExplosionEvent"/> and <see cref="TerrainChangedEvent"/>.
    /// </summary>
    public static void Crater(MatchState s, Vec3d at, int radius, List<GameEvent> ev, ExplosionKind kind = ExplosionKind.Normal)
    {
        var w = s.World;
        var h = w.Height;
        int cx = Pas.Round(at.X), cy = Pas.Round(at.Y);
        ev.Add(new ExplosionEvent(at, radius, radius >= 25 ? ExplosionKind.Nuke : kind));
        int R = radius;
        if (R > 0 && cx >= 1 && cx <= 50 && cy >= 1 && cy <= 50)
        {
            int h0 = h[cx, cy];
            for (int i = -R; i <= R; i++)
            for (int j = -R; j <= R; j++)
            {
                int x = cx + i, y = cy + j;
                if (!Inside(x, y)) continue;
                int depth = Pas.Clamp(Pas.Round(4.0 * R - Math.Sqrt(4.5 * i * (4.5 * i) + 4.5 * j * (4.5 * j))), 0, 2 * R);
                int T = R < 9 ? Pas.Round(h0 - depth / 2.0) : Pas.Round((double)(h0 - depth));
                int cur = h[x, y];
                if (cur > T) { cur = (2 * cur + T + s.Rng.Next(3) - s.Rng.Next(3)) / 3; h[x, y] = cur; }

                if (depth > 2 && R > 2)
                {
                    byte p = w.Props[x, y];
                    if (p >= 1 && p <= 15) w.Props[x, y] = (byte)(p + Prop.BurntFlag);
                }
                if (depth > 8 && R > 2) w.Props[x, y] = 0;

                if (w.HasWater && h[x, y] < w.WaterLevel) h[x, y] = w.WaterLevel;

                if (depth > 2)
                {
                    int v = w.Effects[x, y];
                    if (v != 0)
                    {
                        // effect damping: partly or fully extinguishes fire/acid
                        if (s.Rng.Next(30) + R * DampK > 30 && s.Rng.Next(2) == 0 && v < 20) v = 0;
                        else if (v == 12) v = 0;
                        else if (v < 6) v /= 4;
                        else if (v <= 10) v = (v - 5) / 3 + 6;
                        w.Effects[x, y] = (byte)v;
                    }
                }
            }
            SmoothBox(h, cx, cy, Pas.Trunc(1.2 * R), 5);
            for (int x = cx - R - 1; x <= cx + R + 1; x++)
            for (int y = cy - R - 1; y <= cy + R + 1; y++)
                if (Heightmap.InStorage(x, y) && h[x, y] < -50) h[x, y] = -35;
        }
        ev.Add(new TerrainChangedEvent());
    }

    /// <summary>n passes of 5-point smoothing over a box of ±r around the centre, cells 3..47 (02 §2c).</summary>
    internal static void SmoothBox(Heightmap h, int cx, int cy, int r, int passes)
    {
        int x0 = Math.Max(3, cx - r), x1 = Math.Min(47, cx + r), y0 = Math.Max(3, cy - r), y1 = Math.Min(47, cy + r);
        if (x0 > x1 || y0 > y1) return;
        for (int p = 0; p < passes; p++)
        {
            var t = new int[x1 - x0 + 1, y1 - y0 + 1];
            for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                t[x - x0, y - y0] = Pas.Round((h[x, y] + h[x - 1, y] + h[x + 1, y] + h[x, y - 1] + h[x, y + 1]) / 5.0);
            for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                h[x, y] = t[x - x0, y - y0];
        }
    }

    /// <summary>
    /// Standard blast (02 §2b): linear 15/cell falloff, shield first, hit bonus for enemies, -2x for self.
    /// Does not dig; call <see cref="Crater"/> separately.
    /// </summary>
    public static void StandardBlast(MatchState s, int shooter, WeaponDef w, Vec3d at, int radius, List<GameEvent> ev)
    {
        int R = radius;
        foreach (var t in s.AllTanks)
        {
            if (!t.Alive) continue;
            int d = Dist2D(t.X, t.Y, at.X, at.Y);
            if (d > R) continue;
            if (d == 0) d = 1;
            if (t.Index != shooter) AddScore(s, shooter, w.HitBonus, "hit", ev);
            int dmg = (R - d) * Balance.BlastDamagePerCell + s.Rng.Next(R);
            int credited = Hurt(t, dmg, ev);
            if (t.Index != shooter) AddScore(s, shooter, credited, "damage", ev);
            else AddScore(s, shooter, -Balance.SelfDamagePenalty * credited, "self damage", ev);
        }
    }
}
