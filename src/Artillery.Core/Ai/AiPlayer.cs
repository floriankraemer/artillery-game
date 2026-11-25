using Artillery.Core.Items;
using Artillery.Core.Match;
using Artillery.Core.Physics;
using Artillery.Core.Util;
using Artillery.Core.World;

namespace Artillery.Core.Ai;

/// <summary>
/// Computer player (GAME-MECHANICS section 10): Monte-Carlo search with the real flight
/// simulator, then deliberate per-skill error that shrinks with each shot at the same target. Pure apart from
/// <see cref="Tank.Ai"/> memory and <c>s.Rng</c>; never mutates the world.
/// </summary>
public static class AiPlayer
{
    private const int MaxAttempts = 200;

    /// <summary>Chooses weapon, casing (always Impact), power, bearing and elevation for <paramref name="player"/> (4.2-4.4).</summary>
    public static ShotInput ChooseShot(MatchState s, int player)
    {
        var rng = s.Rng;
        var me = s[player];
        var mem = me.Ai;
        int n = mem.ShotsAtTarget;
        int type = (int)me.Type;
        int count = s.PlayerCount;

        // --- target bookkeeping ---
        int target = mem.Target;
        if (target >= 1 && target <= count && s[target].TeleportedThisRound) n = 0;
        if (target < 1 || target > count || target == player || !s[target].Alive || s[target].TeleportedThisRound) target = 0;
        if (me.TeleportedThisRound) n = Math.Max(0, n / 2 - 1);
        if (target == 0) n = 0;

        // --- teleport decision ---
        bool hasTele = me.Has(WeaponId.Teleporter);
        bool tele = false;
        int mx = (int)me.X, my = (int)me.Y;
        if (hasTele && rng.Next(6) != 1 && s.World.Effects[mx, my] > 8 && type >= 2) tele = true;
        else if (type >= 3 && hasTele && rng.Next(5) != 1 && SatelliteThreat(s, player, mx, my) > 100) tele = true;
        else if (hasTele && NearestTank(s, me.X, me.Y, player) < 4 && rng.Next(2) == 1) tele = true;

        int tdx = 0, tdy = 0;
        if (tele)
        {
            tele = false;
            for (int tries = 0; tries < 30 && !tele; tries++)
            {
                int dx = rng.Next(46) + 2, dy = rng.Next(46) + 2;
                var fx = s.World.Effects;
                if (s.World.Height[dx, dy] > s.World.WaterLevel + 10 && fx[dx, dy] == 0 && fx[dx + 2, dy + 2] == 0
                    && fx[dx - 7, dy - 3] == 0 && fx[dx + 6, dy - 9] == 0 && NearestTank(s, dx, dy, -1) > 15)
                { tele = true; tdx = dx; tdy = dy; }
            }
        }

        // --- pick a target ---
        if (target == 0)
        {
            int c = 0, t;
            do { t = rng.Next(count) + 1; c++; } while (!(t != player && s[t].Alive) && c <= 250);
            if (t == player || !s[t].Alive) return new ShotInput(WeaponId.BasicShell, CasingId.Impact, 50, 0, 45);
            target = t;
        }
        var tgt = s[target];
        double aimX = tele ? tdx : tgt.X, aimY = tele ? tdy : tgt.Y;
        double ddx = aimX - me.X, ddy = aimY - me.Y;
        int dist = Pas.Round(Math.Sqrt((tgt.X - me.X) * (tgt.X - me.X) + (tgt.Y - me.Y) * (tgt.Y - me.Y)));
        double bearing = Bearing(ddx, ddy);
        bool hasMem = mem.LastTarget == target && mem.Power > 0;

        // --- Monte-Carlo search ---
        var pos = Ballistics.LaunchPoint(s, me);
        double power, elevation;
        int tol = 1, attempts = 0;
        bool hit;
        do
        {
            attempts++;
            elevation = rng.Next(85) + 5;
            power = rng.Next(100);
            if (hasMem && attempts <= 30)
            {
                power = Math.Clamp(mem.Power + rng.Next(5) - rng.Next(5), 0, 100);
                elevation = Math.Clamp(mem.Elevation + rng.Next(5) - rng.Next(5), 0, 90);
                bearing = mem.Bearing;
                if (type >= 4 && s.TurnCounter > 1 && tgt.Has(UtilityId.GravityShield)) elevation = rng.Next(30) + 60;
            }
            else bearing = Bearing(ddx, ddy);

            var r = Ballistics.Fly(s, player, WeaponId.BasicShell, CasingId.Impact, pos,
                Ballistics.LaunchVelocity(me, power, bearing, elevation), false);
            double lx = r.Impact.X, ly = r.Impact.Y;
            hit = false;
            if (!r.OffMap)
            {
                if (tele) hit = Math.Abs(lx - tdx) <= 2 && Math.Abs(ly - tdy) <= 2;
                else
                    foreach (var t in s.AllTanks)
                        if (t.Index != player && t.Alive && Math.Abs(lx - t.X) <= tol && Math.Abs(ly - t.Y) <= tol) { hit = true; break; }
            }
            if (attempts > MaxAttempts) { hit = true; tele = false; }
            if (attempts % 30 == 2 && attempts > 5) tol++;
        } while (!hit);

        // --- shot counter and memory (exact solution before noise) ---
        if (target != mem.LastTarget || tgt.TeleportedThisRound) n = 0;
        n++;
        int k = n;
        if (me.Type != PlayerType.Dolphin && k > 1) k += rng.Next(2);
        mem.LastTarget = target; mem.Target = target; mem.ShotsAtTarget = n;
        mem.Power = power; mem.Bearing = bearing; mem.Elevation = elevation;

        if (!tele)
        {
            ApplyNoise(rng, me.Type, k, ref power, ref bearing);
            if (tgt.Has(UtilityId.GravityShield)) bearing += rng.Next(30);
        }
        power = Math.Clamp(power, 0, 100);
        int bearingOut = ((Pas.Round(bearing) % 360) + 360) % 360;

        var weapon = ChooseWeapon(s, me, tgt, dist, k, type, tele, ref power);
        return new ShotInput(weapon, CasingId.Impact, power, bearingOut, elevation, tele ? (tdx, tdy) : null);
    }

    /// <summary>Bearing (degrees, 0..360) whose <see cref="Ballistics.LaunchVelocity"/> heads along (dx, dy).</summary>
    private static double Bearing(double dx, double dy)
    {
        if (dx == 0) dx = 1e-3;
        double b = 360.0 - Math.Atan2(dy, dx) * 180.0 / Math.PI;
        return b % 360.0;
    }

    private static double NearestTank(MatchState s, double x, double y, int exclude)
    {
        double best = double.MaxValue;
        foreach (var t in s.AllTanks)
        {
            if (t.Index == exclude || !t.Alive) continue;
            best = Math.Min(best, Math.Sqrt((t.X - x) * (t.X - x) + (t.Y - y) * (t.Y - y)));
        }
        return best;
    }

    private static int SatelliteThreat(MatchState s, int player, int x, int y)
    {
        int threat = 0;
        foreach (var t in s.AllTanks)
            if (t.Index != player && t.SatelliteTargetX >= 0 && Math.Abs(t.SatelliteTargetX - x) <= 4 && Math.Abs(t.SatelliteTargetY - y) <= 4)
                threat += 200;
        return threat;
    }

    /// <summary>Noise table 4.3. Elevation is never touched; Dolphin adds nothing.</summary>
    private static void ApplyNoise(TpRandom rng, PlayerType type, int k, ref double p, ref double b)
    {
        int R(int n) => rng.Next(n);
        switch (type)
        {
            case PlayerType.Goldfish:
                switch (k)
                {
                    case 1: b += 40 + R(40); p *= 0.7; break;
                    case 2: b += 30 + R(30); p *= 0.7; break;
                    case 3: b += -25 - R(25); p *= 0.7; break;
                    case 4: b += -20 - R(15); p *= 0.8; break;
                    case 5: b += 15 + R(15); p *= 0.8; break;
                    case 6: b += 10 + R(10); p *= 0.8; break;
                    case 7: b += -5 - R(5); p *= 1.1; break;
                    case 8: b += R(5); p *= 1.1; break;
                    case 9: p *= 1.1; break;
                }
                if (k < 15) { p += R(7) - R(7); b += R(7) - R(7); }
                break;
            case PlayerType.Gerbil:
                switch (k)
                {
                    case 1: b += 30 + R(30); p *= 0.7; break;
                    case 2: b += -20 - R(20); p *= 0.8; break;
                    case 3: b += 15 + R(15); p *= 1.1; break;
                    case 4: b += -10 - R(10); p *= 0.9; break;
                    case 5: b += 8 + R(8); break;
                    case 6: b += -5 - R(5); break;
                }
                if (k < 7) p += R(8) - R(8);
                break;
            case PlayerType.Owl:
                switch (k)
                {
                    case 1: b += -15 - R(35); p *= 0.7; break;
                    case 2: b += 10 + R(20); p *= 0.85; break;
                    case 3: b += -5 - R(10); p *= 1.1; break;
                    case 4: b += 5 + R(10); p *= 0.9; break;
                    case 5: p *= 1.06; b -= R(15); break;
                    case 6: b += R(5); break;
                }
                break;
            case PlayerType.Fox:
                switch (k)
                {
                    case 1: b += 5 + R(10); p *= 0.85; break;
                    case 2: b += -3 - R(5); p *= 1.1; break;
                    case 3: b += R(3); p *= 0.95; break;
                }
                break;
        }
    }

    /// <summary>Weapon choice rules 4.4 (later rules override earlier ones; every candidate must be owned).</summary>
    private static WeaponId ChooseWeapon(MatchState s, Tank me, Tank tgt, int dist, int k, int type, bool tele, ref double power)
    {
        var rng = s.Rng;
        var fx = s.World.Effects;
        int w = 1;
        bool Has(int id) => me.Weapons[id] > 0;
        bool R(int n, int v) => rng.Next(n) == v;

        if (tele) w = (int)WeaponId.Teleporter;
        else
        {
            if (Has(3)) w = 3;
            if (Has(4)) w = 4;
            if (Has(5)) w = 5;
            if (Has(6) && R(2, 0)) w = 6;

            if (s.World.WaterLevel + 8 < tgt.Z && fx[(int)tgt.X, (int)tgt.Y] == 0)
            {
                if (Has(9) && R(6, 1) && dist > 2) w = 9;
                if (Has(10) && R(6, 1) && dist > 8) w = 10;
                if (Has(11) && R(6, 1) && dist > 9) w = 11;
                if (Has(12) && R(8, 1)) w = 12;
                if (Has(13) && R(8, 1) && dist > 7) w = 13;
            }
            if (Has(8) && tgt.Shield > 50 && dist > 4 && !R(8, 0)) w = 8;
            if (Has(14) && dist > 15 && (tgt.Hull > 50 ? R(8, 1) : R(3, 1) && R(8, 1))) w = 14;
            if (Has(15) && dist > 20 && (tgt.Hull > 80 ? R(8, 1) : R(3, 1) && R(8, 1))) w = 15;
            if (Has(16) && dist > 35 && tgt.Hull > 50 && tgt.Shield > 50 && R(2, 1)) w = 16;
            if (Has(15) && dist > 20 && tgt.Hull > 95 && tgt.Shield > 200 && R(2, 1)) w = 15;
            if (Has(16) && dist > 30 && tgt.Hull > 95 && tgt.Shield > 400 && R(2, 1)) w = 16;
            if (Has(18) && dist < 8) w = 18;
            if (Has(19) && dist > 7 && R(6, 0)) w = 19;
            if (Has(25) && dist > 20 && R(3, 0)) w = 25;
            if (Has(24) && dist > 15 && R(10, 0)) w = 24;
            if (Has(21))
            {
                int own = fx[(int)me.X, (int)me.Y];
                if ((own == 9 || own == 10 || own > 12) && R(2, 0)) { w = 21; power = 0; }
            }
            if (k <= 13 - 2 * type && w != 21) w = 1;
        }
        if (w == 21 && R(80, 1)) w = 1;
        return (WeaponId)w;
    }
}
