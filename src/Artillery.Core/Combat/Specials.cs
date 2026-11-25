using Artillery.Core.Items;
using Artillery.Core.Match;
using Artillery.Core.Util;
using Artillery.Core.World;

namespace Artillery.Core.Combat;

/// <summary>
/// Special weapon effects. The caller resolves the shot in the order of GAME-MECHANICS §5:
/// plain explosives get <c>Blast.Crater</c> + <c>Blast.StandardBlast</c> from the caller; for every other effect
/// call <see cref="Apply"/>, then (if <c>w.StandardBlast</c>, i.e. Earthquake and Hill Maker) run
/// <c>Blast.StandardBlast</c> yourself. <see cref="Apply"/> never runs the standard blast, except inside Air Strike (its own bombs).
/// </summary>
public static class Specials
{
    internal static readonly (int, int)[] Dirs4 = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    /// <summary>Applies the special effect of <paramref name="w"/> at <paramref name="impact"/>.</summary>
    public static void Apply(MatchState s, int shooter, WeaponDef w, Vec3d impact, (int x, int y)? clickTarget, List<GameEvent> ev)
    {
        int ix = Pas.Round(impact.X), iy = Pas.Round(impact.Y);
        bool onMap = ix >= 1 && ix <= 49 && iy >= 1 && iy <= 49;
        switch (w.Effect)
        {
            case WeaponEffect.Teleport: Teleport(s, shooter, impact, ev); return;
            case WeaponEffect.Flamethrower: Flamethrower(s, shooter, ev); return;
            case WeaponEffect.AirStrike: AirStrike(s, shooter, clickTarget, ev); return;
            case WeaponEffect.SatelliteAim: SatelliteAim(s, shooter, clickTarget, ev); return;
        }
        if (!onMap) return;
        switch (w.Effect)
        {
            case WeaponEffect.ShieldBreaker: ShieldBreaker(s, shooter, impact, ev); break;
            case WeaponEffect.Napalm: Napalm(s, impact, 5, 8, 4, ev); break;
            case WeaponEffect.SuperNapalm: Napalm(s, impact, 9, 10, 6, ev); break;
            case WeaponEffect.Goo: Goo(s, ix, iy, impact, ev); break;
            case WeaponEffect.AcidBomb: Acid(s, impact, 5, 4, 5, ev); break;
            case WeaponEffect.AcidFlood: Acid(s, impact, 10, 15, 8, ev); break;
            case WeaponEffect.Scatter: Scatter(s, impact, ev); break;
            case WeaponEffect.Shockwave: Shockwave(s, shooter, impact, ev); break;
            case WeaponEffect.Earthquake: Earthquake(s, ix, iy, ev); break;
            case WeaponEffect.Extinguisher: Extinguisher(s, ix, iy, impact, ev); break;
            case WeaponEffect.HillMaker: HillMaker(s, ix, iy, impact, ev); break;
            case WeaponEffect.WallMaker: WallMaker(s, ix, iy, impact, ev); break;
            case WeaponEffect.Volcano: Volcano(s, ix, iy, impact, ev); break;
        }
    }

    private static int Ground(MatchState s, int x, int y) => s.World.Height[x, y];

    // ---- fire / acid / goo ----------------------------------------------------------------------------

    /// <summary>Napalm / Super Napalm: 1420 samples on a random ring, value top - r/2 written where cell &lt; 4; goo ignites.</summary>
    private static void Napalm(MatchState s, Vec3d at, int rMax, int top, int visual, List<GameEvent> ev)
    {
        int ix = Pas.Round(at.X), iy = Pas.Round(at.Y);
        ev.Add(new ExplosionEvent(at, visual, ExplosionKind.Fire));
        var fx = s.World.Effects;
        for (int i = 1; i <= 1420; i++)
        {
            int r = s.Rng.Next(rMax);
            int x = ix + Pas.Round(r * Math.Cos(i)), y = iy + Pas.Round(r * Math.Sin(i));
            if (!Blast.Inside(x, y)) continue;
            byte v = fx[x, y];
            if (Effect.IsGoo(v)) fx[x, y] = 14;
            else if (v < 4) fx[x, y] = (byte)(top - r / 2);
        }
        ev.Add(new TerrainChangedEvent());
    }

    /// <summary>Gooey blob: centre 11, orthogonal neighbours 12; +2 (burning goo) if the centre was on fire.</summary>
    private static void Goo(MatchState s, int ix, int iy, Vec3d at, List<GameEvent> ev)
    {
        var fx = s.World.Effects;
        int bonus = Effect.IsFire(fx[ix, iy]) ? 2 : 0;
        ev.Add(new ExplosionEvent(at, 2, ExplosionKind.Goo));
        fx[ix, iy] = (byte)(11 + bonus);
        foreach (var (dx, dy) in Dirs4)
            if (Blast.Inside(ix + dx, iy + dy)) fx[ix + dx, iy + dy] = (byte)(12 + bonus);
        ev.Add(new TerrainChangedEvent());
    }

    /// <summary>Acid bomb / flood: 2000 samples, value base - r (anything &gt; 3 becomes 5), written where cell &lt; 20 (non-positive values are skipped).</summary>
    private static void Acid(MatchState s, Vec3d at, int rMax, int baseVal, int visual, List<GameEvent> ev)
    {
        int ix = Pas.Round(at.X), iy = Pas.Round(at.Y);
        ev.Add(new ExplosionEvent(at, visual, ExplosionKind.Acid));
        var fx = s.World.Effects;
        for (int i = 1; i <= 2000; i++)
        {
            int r = s.Rng.Next(rMax);
            int x = ix + Pas.Round(r * Math.Cos(i)), y = iy + Pas.Round(r * Math.Sin(i));
            if (!Blast.Inside(x, y)) continue;
            int val = baseVal - r;
            if (val > 3) val = 5;
            if (val > 0 && fx[x, y] < 20) fx[x, y] = (byte)val;
        }
        ev.Add(new TerrainChangedEvent());
    }

    /// <summary>Extinguisher: within ±8, fire cleared unless Random(20)=2; burning goo and lava always cleared.</summary>
    private static void Extinguisher(MatchState s, int ix, int iy, Vec3d at, List<GameEvent> ev)
    {
        var fx = s.World.Effects;
        for (int x = ix - 8; x <= ix + 8; x++)
        for (int y = iy - 8; y <= iy + 8; y++)
        {
            if (!Blast.Inside(x, y)) continue;
            byte v = fx[x, y];
            if (Effect.IsFire(v)) { if (s.Rng.Next(20) != 2) fx[x, y] = 0; }
            else if (Effect.IsBurningGoo(v) || Effect.IsLava(v)) fx[x, y] = 0;
        }
        ev.Add(new ExplosionEvent(at, 7, ExplosionKind.Normal));
        ev.Add(new TerrainChangedEvent());
    }

    // ---- area damage ----------------------------------------------------------------------------------

    /// <summary>Shield breaker: d &lt; 7 and shield &gt; 0: drain (8-d)*30 + Random(30) + 10% of shield; shield only; enemies score the drain.</summary>
    private static void ShieldBreaker(MatchState s, int shooter, Vec3d at, List<GameEvent> ev)
    {
        ev.Add(new ExplosionEvent(at, 4, ExplosionKind.Shock));
        foreach (var t in s.AllTanks)
        {
            if (!t.Alive || t.Shield <= 0) continue;
            int d = Blast.Dist2D(t.X, t.Y, at.X, at.Y);
            if (d >= 7) continue;
            int drain = (8 - d) * 30 + s.Rng.Next(30) + Pas.Round(0.1 * t.Shield);
            int drained = t.DrainShield(drain);
            ev.Add(new TankDamagedEvent(t.Index, 0, drained, t.Hull, t.Shield));
            if (t.Index != shooter) Blast.AddScore(s, shooter, drained, "shield drain", ev);
        }
    }

    /// <summary>Scatter shot: no split; effects in the 11x11 area cleared with p=4/5; every tank with d &lt; 8 loses 10..24 shield (spill to hull), no score.</summary>
    private static void Scatter(MatchState s, Vec3d at, List<GameEvent> ev)
    {
        int ix = Pas.Round(at.X), iy = Pas.Round(at.Y);
        ev.Add(new ExplosionEvent(at, 5, ExplosionKind.Normal));
        for (int x = ix - 5; x <= ix + 5; x++)
        for (int y = iy - 5; y <= iy + 5; y++)
            if (Blast.Inside(x, y) && s.Rng.Next(5) < 4) s.World.Effects[x, y] = 0;
        foreach (var t in s.AllTanks)
        {
            if (!t.Alive || Blast.Dist2D(t.X, t.Y, at.X, at.Y) >= 8) continue;
            Blast.Hurt(t, 10 + s.Rng.Next(15), ev);
        }
        ev.Add(new TerrainChangedEvent());
    }

    // ---- flamethrower ---------------------------------------------------------------------------------

    /// <summary>
    /// Flamethrower: 9 bursts, burst k reaches 1 + 0.75k cells along the bearing (7.75 at k=9), seeds fire at the end cell
    /// (7+Random(4)) and neighbours within 5 height units (7+Random(3)); every other tank inside a ±1.5 box of the burst end loses
    /// 8..27 shield, the shooter scores 10..14 per hit. BUG FIX: the box uses tank X for rows and tank Y for columns on every edge.
    /// </summary>
    private static void Flamethrower(MatchState s, int shooter, List<GameEvent> ev)
    {
        var me = s[shooter];
        double b = (360 - me.Bearing) * Math.PI / 180.0;       // same convention as launch (§4)
        double dx = Math.Cos(b), dy = Math.Sin(b);
        double sx = me.X + 0.45, sy = me.Y + 0.45;
        ev.Add(new FlameEvent(shooter, new Vec3d(sx, sy, me.Z), b, 1 + 0.75 * 9));
        var fx = s.World.Effects;
        var spill = new SortedSet<int>();
        for (int k = 1; k <= 9; k++)
        {
            double reach = 1 + 0.75 * k;
            double ex = sx + dx * reach, ey = sy + dy * reach;
            int cx = Pas.Round(ex), cy = Pas.Round(ey);
            if (Blast.Inside(cx, cy))
            {
                int hc = Ground(s, cx, cy);
                fx[cx, cy] = (byte)Math.Max((int)fx[cx, cy], 7 + s.Rng.Next(4));
                foreach (var (ox, oy) in Dirs4)
                {
                    int nx = cx + ox, ny = cy + oy;
                    if (!Blast.Inside(nx, ny) || Math.Abs(Ground(s, nx, ny) - hc) > 5) continue;
                    fx[nx, ny] = (byte)Math.Max((int)fx[nx, ny], 7 + s.Rng.Next(3));
                }
            }
            foreach (var t in s.AllTanks)
            {
                if (t.Index == shooter || !t.Alive) continue;
                if (Math.Abs(t.X + 0.5 - ex) > 1.5 || Math.Abs(t.Y + 0.5 - ey) > 1.5) continue;
                t.Shield -= 8 + s.Rng.Next(20);
                spill.Add(t.Index);
                Blast.AddScore(s, shooter, 10 + s.Rng.Next(5), "flame hit", ev);
            }
        }
        foreach (int q in spill)
        {
            var t = s[q];
            int h0 = t.Hull, s0 = Math.Max(0, t.Shield + 0);
            if (t.Shield < 0) { t.Hull += t.Shield; t.Shield = 0; }
            if (t.Hull <= 0) t.Hull = Tank.HullDead;
            ev.Add(new TankDamagedEvent(q, h0 - Math.Max(t.Hull, 0), s0 - t.Shield, t.Hull, t.Shield));
        }
        int tx = (int)me.X, ty = (int)me.Y;
        if (Effect.IsFire(fx[tx, ty])) fx[tx, ty] = 0;
        ev.Add(new SoundEvent(SoundId.Fire));
        ev.Add(new TerrainChangedEvent());
    }

    // ---- shock wave / teleport ------------------------------------------------------------------------

    /// <summary>
    /// Shock wave: each living tank (incl. the shooter) with d &lt; 8 is flung. Horizontal 1/3 cell per step, vz=(9-d)/9, gravity 0.1.
    /// BUG FIX: pushed directly away from the (jittered) epicentre. Off map: hull -100, shooter scores victim's shield (not self).
    /// Landing: 50..99 damage; a kill gives +200 (not self).
    /// </summary>
    private static void Shockwave(MatchState s, int shooter, Vec3d at, List<GameEvent> ev)
    {
        ev.Add(new ExplosionEvent(at, 8, ExplosionKind.Shock));
        ev.Add(new SoundEvent(SoundId.Shockwave));
        double ex = at.X, ey = at.Y;
        foreach (var t in s.AllTanks)
        {
            if (!t.Alive) continue;
            ex += s.Rng.Jitter(3); ey += s.Rng.Jitter(3);     // cumulative ±2 jitter per tank
            int d = Blast.Dist2D(t.X, t.Y, ex, ey);
            if (d >= 8) continue;
            double ax = t.X - ex, ay = t.Y - ey;
            double ang = (ax == 0 && ay == 0) ? s.Rng.Next(360) * Math.PI / 180 : Math.Atan2(ay, ax);
            double vx = Math.Cos(ang) / 3, vy = Math.Sin(ang) / 3, vz = (9 - d) / 9.0;
            double x = t.X, y = t.Y, z = t.Z;
            var from = t.Position;
            bool off = false;
            for (int step = 0; step < 400; step++)
            {
                x += vx; y += vy; z += vz; vz -= 0.1;
                if (x > 48 || x < 2 || y > 48 || y < 2) { off = true; break; }
                if (z <= s.World.Height.HeightAt(x, y) + 0.5) break;
            }
            if (off)
            {
                if (t.Index != shooter) Blast.AddScore(s, shooter, t.Shield, "shockwave off map", ev);
                t.Shield = 0; t.Hull = Tank.HullOffMap;
                ev.Add(new TankMovedEvent(t.Index, from, new Vec3d(x, y, z), MoveKind.Knockback));
                continue;
            }
            t.X = Pas.Clamp(Pas.Round(x), 2, 48); t.Y = Pas.Clamp(Pas.Round(y), 2, 48);
            t.Z = Ground(s, (int)t.X, (int)t.Y) + 0.5;
            ev.Add(new TankMovedEvent(t.Index, from, t.Position, MoveKind.Knockback));
            Blast.Hurt(t, 50 + s.Rng.Next(50), ev);
            if (!t.Alive)
            {
                t.Shield = 0;
                if (t.Index != shooter) Blast.AddScore(s, shooter, Balance.DeathBonusToSurvivors, "shockwave kill", ev);
            }
        }
    }

    /// <summary>Teleport: own tank to impact ±2 (clamped 4..45), shield x0.9. Off map (outside 1..49): hull -100, shield 0, -200.</summary>
    private static void Teleport(MatchState s, int shooter, Vec3d at, List<GameEvent> ev)
    {
        var t = s[shooter];
        int ix = Pas.Round(at.X), iy = Pas.Round(at.Y);
        t.TeleportedThisRound = true;
        ev.Add(new SoundEvent(SoundId.Teleport));
        if (ix < 1 || ix > 49 || iy < 1 || iy > 49)
        {
            t.Hull = Tank.HullOffMap; t.Shield = 0;
            Blast.AddScore(s, shooter, -Balance.TeleportOffMapPenalty, "teleport off map", ev);
            return;
        }
        var from = t.Position;
        t.X = Pas.Clamp(ix + s.Rng.Next(3) - s.Rng.Next(3), 4, 45);
        t.Y = Pas.Clamp(iy + s.Rng.Next(3) - s.Rng.Next(3), 4, 45);
        t.Shield = Pas.Trunc(t.Shield * 0.9);
        t.Z = Ground(s, (int)t.X, (int)t.Y) + 0.5;
        ev.Add(new TankMovedEvent(shooter, from, t.Position, MoveKind.Teleport));
    }

    // ---- terrain builders -----------------------------------------------------------------------------

    /// <summary>
    /// Earthquake. Two random-walk cracks of 250 steps starting at the impact, digging only.
    /// Original constants are unknown (02 §6.5), chosen here: walk step 0.25 cell, heading turns by (Random(3)-1)*0.4 rad per step,
    /// depth = max(2, cap*(1 - j/250)) with cap 25 (crack 1) / 40 (crack 2), each visited cell lowered by depth and its 4 neighbours by depth/2,
    /// then 2 smoothing passes over the touched box. Callers add the standard R 8 blast afterwards (Dmg flag).
    /// </summary>
    private static void Earthquake(MatchState s, int ix, int iy, List<GameEvent> ev)
    {
        var h = s.World.Height;
        int minX = ix, maxX = ix, minY = iy, maxY = iy;
        for (int k = 1; k <= 2; k++)
        {
            int cap = k == 1 ? 25 : 40;
            double heading = s.Rng.Next(360) * Math.PI / 180, px = ix, py = iy;
            var seen = new HashSet<(int, int)>();
            for (int j = 1; j <= 250; j++)
            {
                heading += (s.Rng.Next(3) - 1) * 0.4;
                px += Math.Cos(heading) * 0.25; py += Math.Sin(heading) * 0.25;
                int cx = Pas.Round(px), cy = Pas.Round(py);
                if (!Blast.Inside(cx, cy)) break;
                if (!seen.Add((cx, cy))) continue;
                int depth = Math.Max(2, Pas.Round(cap * (1 - j / 250.0)));
                h[cx, cy] -= depth;
                foreach (var (ox, oy) in Dirs4)
                    if (Blast.Inside(cx + ox, cy + oy)) h[cx + ox, cy + oy] -= depth / 2;
                minX = Math.Min(minX, cx); maxX = Math.Max(maxX, cx); minY = Math.Min(minY, cy); maxY = Math.Max(maxY, cy);
            }
        }
        int cxm = (minX + maxX) / 2, cym = (minY + maxY) / 2, r = Math.Max(maxX - minX, maxY - minY) / 2 + 2;
        Blast.SmoothBox(h, cxm, cym, r, 2);
        ev.Add(new SoundEvent(SoundId.Rumble));
        ev.Add(new TerrainChangedEvent());
    }

    /// <summary>Hill maker: n=clamp(Round(28-4.5*dist),4,14) over ±7, target h0+2n, 1/3 move upward, then 3 smoothing passes over ±8. Callers add the R 8 blast.</summary>
    private static void HillMaker(MatchState s, int ix, int iy, Vec3d at, List<GameEvent> ev)
    {
        var h = s.World.Height;
        ev.Add(new ExplosionEvent(at, 8, ExplosionKind.Dirt));
        int h0 = h[ix, iy];
        for (int i = -7; i <= 7; i++)
        for (int j = -7; j <= 7; j++)
        {
            int x = ix + i, y = iy + j;
            if (!Blast.Inside(x, y)) continue;
            int n = Pas.Clamp(Pas.Round(28 - 4.5 * Math.Sqrt(i * i + j * j)), 4, 14);
            int T = h0 + 2 * n;
            if (h[x, y] < T) h[x, y] = (2 * h[x, y] + T + s.Rng.Next(3) - s.Rng.Next(3)) / 3;
        }
        Blast.SmoothBox(h, ix, iy, 8, 3);
        ev.Add(new TerrainChangedEvent());
    }

    /// <summary>Wall maker: min of 400 samples within ±4 (floor 0); 500 strip hits (2 wide, ±7 long, random axis) raise cells to min+25.</summary>
    private static void WallMaker(MatchState s, int ix, int iy, Vec3d at, List<GameEvent> ev)
    {
        var h = s.World.Height;
        int min = int.MaxValue;
        for (int i = 0; i < 400; i++)
        {
            int x = Pas.Clamp(ix + s.Rng.Next(9) - 4, 3, 47), y = Pas.Clamp(iy + s.Rng.Next(9) - 4, 3, 47);
            min = Math.Min(min, h[x, y]);
        }
        min = Math.Max(min, 0);
        for (int i = 0; i < 500; i++)
        {
            int axis = s.Rng.Next(2), along = s.Rng.Next(15) - 7, across = s.Rng.Next(2);
            int x = axis == 0 ? ix + along : ix + across, y = axis == 0 ? iy + across : iy + along;
            if (Blast.Inside(x, y) && h[x, y] < min + 25) h[x, y] = min + 25;
        }
        ev.Add(new ExplosionEvent(at, 8, ExplosionKind.Dirt));
        ev.Add(new TerrainChangedEvent());
    }

    /// <summary>Volcano: cone (min + min(Round(187/d)-20, 55) + Random(4), 3500 samples ±13), vent (1800 samples r&lt;3 set to min+42), lava seed 22 at two cells.</summary>
    private static void Volcano(MatchState s, int ix, int iy, Vec3d at, List<GameEvent> ev)
    {
        var h = s.World.Height;
        int min = int.MaxValue;
        for (int i = 0; i < 1000; i++)
        {
            int x = Pas.Clamp(ix + s.Rng.Next(15) - 7, 2, 49), y = Pas.Clamp(iy + s.Rng.Next(15) - 7, 2, 49);
            min = Math.Min(min, h[x, y]);
        }
        for (int i = 0; i < 3500; i++)
        {
            int a = s.Rng.Next(27) - 13, b = s.Rng.Next(27) - 13;
            int x = ix + a, y = iy + b;
            if (!Blast.Inside(x, y)) continue;
            double d = Math.Sqrt(a * a + b * b);
            int hh = d < 0.5 ? 55 : Math.Min(Pas.Round(1.7 / d * 110) - 20, 55);
            int target = min + hh + s.Rng.Next(4);
            if (hh > 0 && h[x, y] < target) h[x, y] = target;
        }
        for (int i = 0; i < 1800; i++)
        {
            int a = s.Rng.Next(7) - 3, b = s.Rng.Next(7) - 3;
            if (a * a + b * b >= 9 || !Blast.Inside(ix + a, iy + b)) continue;
            h[ix + a, iy + b] = min + 42;
        }
        s.World.Effects[ix, iy] = 22;
        if (Blast.Inside(ix + 1, iy + 1)) s.World.Effects[ix + 1, iy + 1] = 22;
        ev.Add(new ExplosionEvent(at, 8, ExplosionKind.Dirt));
        ev.Add(new SoundEvent(SoundId.Rumble));
        ev.Add(new TerrainChangedEvent());
    }

    // ---- targeted -------------------------------------------------------------------------------------

    private static (int x, int y) JitterTarget(MatchState s, (int x, int y) t)
        => (Pas.Clamp(t.x + s.Rng.Next(3) - 1, 1, 49), Pas.Clamp(t.y + s.Rng.Next(3) - 1, 1, 49));

    private static int Sign(MatchState s) => s.Rng.Next(2) == 0 ? -1 : 1;

    /// <summary>
    /// Air strike: click target (±1 jitter), five bombs (rows -(R(6)+6), -(R(4)+4), ±3, +(R(3)+3), +(R(5)+5); radii 5,5,6,5,5),
    /// each Crater + StandardBlast using the Basic Shell definition (bonus 5). The shooter's selected weapon becomes Basic Shell.
    /// A null click target does nothing.
    /// </summary>
    private static void AirStrike(MatchState s, int shooter, (int x, int y)? click, List<GameEvent> ev)
    {
        if (click is not { } c) return;
        var (tx, ty) = JitterTarget(s, c);
        var basic = ItemCatalog.Get(WeaponId.BasicShell);
        s[shooter].SelectedWeapon = WeaponId.BasicShell;
        ev.Add(new SoundEvent(SoundId.AirStrikePlane));
        var bombs = new (int row, int col, int r)[5];
        bombs[0] = (-(s.Rng.Next(6) + 6), s.Rng.Next(4) * Sign(s), 5);
        bombs[1] = (-(s.Rng.Next(4) + 4), 3 * Sign(s), 5);
        bombs[2] = (3 * Sign(s), 3 * Sign(s), 6);
        bombs[3] = (s.Rng.Next(3) + 3, 3 * Sign(s), 5);
        bombs[4] = (s.Rng.Next(5) + 5, 3 * Sign(s), 5);
        foreach (var (row, col, r) in bombs)
        {
            int x = Pas.Clamp(tx + row, 2, 49), y = Pas.Clamp(ty + col, 2, 49);
            var p = new Vec3d(x, y, Ground(s, x, y));
            Blast.Crater(s, p, r, ev);
            Blast.StandardBlast(s, shooter, basic, p, r, ev);
        }
    }

    /// <summary>Aim satellite: stores the (±1 jittered) target and fires next turn; consumes one aim charge.</summary>
    private static void SatelliteAim(MatchState s, int shooter, (int x, int y)? click, List<GameEvent> ev)
    {
        if (click is not { } c) return;
        var t = s[shooter];
        var (x, y) = JitterTarget(s, c);
        t.SatelliteTargetX = x; t.SatelliteTargetY = y;
        t.SatelliteFireTurn = s.TurnCounter + 1;
        int slot = (int)WeaponId.SatelliteAim;
        if (t.Weapons[slot] > 0 && t.Weapons[slot] != Tank.Infinite) t.Weapons[slot]--;
    }

    /// <summary>
    /// Satellite strike at the owner's stored target: for living tanks with d &lt; 4: dmg=(5-d)*60+Random(30)+shield/2;
    /// the owner scores dmg/2 for foreign victims. Clears the stored target. Callers then run <c>Deaths.Resolve</c>.
    /// </summary>
    public static void SatelliteStrike(MatchState s, Tank owner, List<GameEvent> ev)
    {
        if (owner.SatelliteFireTurn < 0) return;
        double tx = owner.SatelliteTargetX, ty = owner.SatelliteTargetY;
        ev.Add(new BeamEvent(new Vec3d(tx, ty, Ground(s, owner.SatelliteTargetX, owner.SatelliteTargetY))));
        foreach (var t in s.AllTanks)
        {
            if (!t.Alive) continue;
            int d = Blast.Dist2D(t.X, t.Y, tx, ty);
            if (d >= 4) continue;
            int dmg = (5 - d) * 60 + s.Rng.Next(30) + t.Shield / 2;
            if (t.Index != owner.Index) Blast.AddScore(s, owner.Index, dmg / 2, "satellite", ev);
            Blast.Hurt(t, dmg, ev);
        }
        owner.SatelliteTargetX = -1; owner.SatelliteTargetY = -1; owner.SatelliteFireTurn = -1;
    }
}
