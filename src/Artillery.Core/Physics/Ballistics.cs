using Artillery.Core.Items;
using Artillery.Core.Match;
using Artillery.Core.Util;
using Artillery.Core.World;

namespace Artillery.Core.Physics;

/// <summary>Result of a projectile flight. Impact is the final position (below ground for tunneling).</summary>
public sealed record FlightResult(Vec3d Impact, bool OffMap, bool ReachedOrbit, IReadOnlyList<Vec3d> Path, int Steps);

/// <summary>Projectile launch and flight (GAME-MECHANICS section 4). Pure: never mutates state, no RNG.</summary>
public static class Ballistics
{
    /// <summary>Launch velocity before the x1.1 / power-booster scaling applied in <see cref="Fly"/>. Caller forces elevation 0 for Cruise/SmartCruise.</summary>
    public static Vec3d LaunchVelocity(Tank t, double power, double bearingDeg, double elevationDeg)
    {
        double p = power / 100.0, b = (360.0 - bearingDeg) * Math.PI / 180.0, e = elevationDeg * Math.PI / 180.0;
        return new Vec3d(Math.Cos(b) * p * Math.Cos(e) / Balance.HorizontalDiv,
                         Math.Sin(b) * p * Math.Cos(e) / Balance.HorizontalDiv,
                         Math.Sin(e) * p * Balance.VerticalMul);
    }

    /// <summary>Muzzle position: (X+0.45, Y+0.45, ground+1).</summary>
    public static Vec3d LaunchPoint(MatchState s, Tank t)
    {
        double x = t.X + 0.45, y = t.Y + 0.45;
        return new Vec3d(x, y, s.World.Height.HeightAt(x, y) + 1);
    }

    private static double Heading(double vx, double vy)
    {
        double h = Math.Atan2(vx, vy);
        return h < 0 ? h + 2 * Math.PI : h;
    }

    /// <summary>Simulates a whole flight. <paramref name="vel"/> is the raw launch velocity from <see cref="LaunchVelocity"/>.</summary>
    public static FlightResult Fly(MatchState s, int shooter, WeaponId weapon, CasingId casing, Vec3d pos, Vec3d vel, bool recordPath)
    {
        var w = s.World;
        var hm = w.Height;
        double g = w.Gravity;
        bool air = (s.Settings.AirResistance || (w.Landscape?.ForceAir ?? false)) && weapon != WeaponId.Flamethrower;
        double airK = (w.AirResistance + 1) / 2;
        var wind = s.WindAccel;
        bool sds = weapon == WeaponId.SatelliteLaunch;
        if (sds) casing = CasingId.Impact;
        Tank? me = shooter >= 1 && shooter < s.Tanks.Count ? s.Tanks[shooter] : null;

        double x = pos.X, y = pos.Y, z = pos.Z, vx = vel.X, vy = vel.Y, vz = vel.Z;

        // Smart cruise pre-loop: speed and target lock (closest to aim direction).
        double sc = Math.Sqrt(vx * vx + vy * vy);
        Tank? target = null;
        if (casing == CasingId.SmartCruise)
        {
            double h0 = Heading(vx, vy), best = 20;
            foreach (var t in s.AllTanks)
            {
                if (t.Index == shooter || !t.Alive) continue;
                double d = Math.Abs(Heading(t.X + 0.5 - x, t.Y + 0.5 - y) - h0);
                if (d > Math.PI) d = 2 * Math.PI - d;
                if (d < best) { best = d; target = t; }
            }
            if (target == null) casing = CasingId.Cruise; // BUG FIX: no valid target -> plain cruise
        }

        double k = Balance.LaunchScale;
        if (me != null && me.Has(UtilityId.PowerBooster)) k *= Balance.ExtraPowerScale;
        vx *= k; vy *= k; vz *= k;

        var path = new List<Vec3d>();
        int steps = 0;
        bool hit = false, orbit = false;
        double prevDist = -1;

        do
        {
            double zPrev = z;
            x += vx; y += vy; z += vz; steps++;

            if (sds) { vx *= 0.99; vy *= 0.99; vz += 0.02; }

            if (casing == CasingId.SmartCruise && target != null)
            {
                vz = 0;
                double tx = target.X + 0.5, ty = target.Y + 0.5;
                double desired = Heading(tx - x, ty - y);
                vx = (300 * vx + Math.Sin(desired)) / 301;
                vy = (300 * vy + Math.Cos(desired)) / 301;
                double h = Heading(vx, vy);
                double px = x, py = y;
                double Probe(double a) => hm.HeightAt(px + Math.Sin(a) * 2, py + Math.Cos(a) * 2);
                if (z > target.Z)
                {
                    z = (20 * z + target.Z + 1.5) / 21;
                    if (hm.HeightAt(x, y) + 1.5 > z)
                    {
                        double L = Probe(h - 0.5), C = Probe(h), R = Probe(h + 0.5);
                        if (Math.Abs(R - L) > 0.5)
                        {
                            if (L < C && L < R) h -= 0.008;
                            if (R < C && R < L) h += 0.008;
                        }
                    }
                }
                if (z < target.Z && hm.HeightAt(x, y) + 1.5 > z)
                {
                    double L = Probe(h - 0.5), C = Probe(h), R = Probe(h + 0.5);
                    if (Math.Abs(R - L) > 0.5)
                    {
                        if (L > C && L > R) h -= 0.008;
                        if (R > C && R > L) h += 0.008;
                    }
                }
                vx = Math.Sin(h) * sc; vy = Math.Cos(h) * sc; sc = (sc + 6) / 21;
                double gh = hm.HeightAt(x, y);
                if (gh + 1.5 > z) z = gh + 1.5;
                if (Math.Sqrt(vx * vx + vy * vy) < 0.1) z = gh - 0.1;
                double dist = Math.Sqrt((tx - x) * (tx - x) + (ty - y) * (ty - y));
                if (steps >= 2 && prevDist >= 0 && prevDist < dist && dist < 5) hit = true;
                prevDist = dist;
                foreach (var t in s.AllTanks)
                    if (t.Index != shooter && t.Alive && Dist(t, x, y) < 3) hit = true;
            }

            if (casing == CasingId.HeatSeeking)
            {
                byte c = w.Effects[Pas.Round(x), Pas.Round(y)];
                if (Effect.IsHot(c)) vz -= 0.1;
                foreach (var t in s.AllTanks)
                {
                    if (t.Index == shooter || !t.Alive || t.Has(UtilityId.GravityShield)) continue;
                    if (Dist(t, x, y) < 15 && z - t.Z < 35)
                    {
                        vx += 0.005 * Math.Sign(t.X - x); vy += 0.005 * Math.Sign(t.Y - y); vz -= 0.02;
                    }
                }
            }

            foreach (var t in s.AllTanks)
            {
                if (t.Index == shooter || !t.Alive || !t.Has(UtilityId.GravityShield)) continue;
                double d = Dist(t, x, y);
                if (Pas.Round(d) < 5) vz *= 1 - (d + 5) / 250;
            }

            if (casing == CasingId.Cruise)
            {
                double gh = hm.HeightAt(x, y);
                z = (2 * zPrev + gh) / 3 + 1;
                if (Math.Abs(vx) < 0.05 && Math.Abs(vy) < 0.05) z = gh - 0.1;
                if (gh > z) hit = true;
                foreach (var t in s.AllTanks)
                {
                    if (t.Index == shooter || !t.Alive) continue;
                    double d = Dist(t, x, y);
                    if (d < 3) z -= (3 - d) * 1.5;
                }
            }

            // Wind: sideways push on free-flying shells (not cruise missiles, satellites or flames, not while underground).
            if (wind.X != 0 || wind.Y != 0)
                if (casing is not (CasingId.Cruise or CasingId.SmartCruise) && !sds && weapon != WeaponId.Flamethrower && z > hm.HeightAt(x, y) + 0.1)
                { vx += wind.X; vy += wind.Y; }

            if (air) { vx *= airK; vy *= airK; vz *= airK; }
            vz -= g;

            int gx = Pas.Round(x), gy = Pas.Round(y);
            if (recordPath) path.Add(new Vec3d(x, y, z));
            if (hm.HeightAt(x, y) + 0.1 >= z) hit = true;

            switch (casing)
            {
                case CasingId.Bouncing when hit:
                    hit = false;
                    foreach (var t in s.AllTanks)
                        if (t.Index != shooter && t.Alive && Dist(t, x, y) <= 2 && z - t.Z < 5) hit = true;
                    if (Math.Sqrt(vx * vx + vy * vy + vz * vz) < 0.4) hit = true;
                    vz = -vz; z += 0.2;
                    vz *= 0.7; vx *= 0.9; vy *= 0.9;
                    break;

                case CasingId.Rolling when hm.HeightAt(x, y) + 0.2 >= z:
                {
                    hit = false;
                    z = hm.HeightAt(x, y);
                    double hxp = hm.HeightAt(x + 0.1, y), hxm = hm.HeightAt(x - 0.1, y);
                    double hyp = hm.HeightAt(x, y + 0.1), hym = hm.HeightAt(x, y - 0.1);
                    if (hxp > z) vx -= 0.001; else if (hxp < z) vx += 0.001;
                    if (hxm > z) vx += 0.001; else if (hxm < z) vx -= 0.001;
                    if (hyp > z) vy -= 0.001; else if (hyp < z) vy += 0.001;
                    if (hym > z) vy += 0.001; else if (hym < z) vy -= 0.001;
                    if (hxp - z > 0.4 && vx > 0) { vx = 0; vy /= 3; }
                    if (hxm - z > 0.4 && vx < 0) { vx = 0; vy /= 3; }
                    if (hyp - z > 0.4 && vy > 0) { vy = 0; vx /= 3; }
                    if (hym - z > 0.4 && vy < 0) { vy = 0; vx /= 3; }
                    vz = 0; z += 0.9;
                    vx *= 0.96; vy *= 0.96;
                    foreach (var t in s.AllTanks)
                        if (t.Index != shooter && t.Alive && Dist(t, x, y) < 2) hit = true;
                    if (Math.Sqrt(vx * vx + vy * vy + vz * vz) < 0.05) hit = true;
                    break;
                }

                case CasingId.Tunneling:
                    if (hit) { vx *= 0.985; vy *= 0.985; vz *= 0.985; hit = false; }
                    // Horizontal speed only: underground vz converges to ~-0.67 (gravity vs drag), so a 3D test never fires.
                    // The original's odd weights sqrt(3vx²+2vy²) are normalised to plain horizontal speed.
                    if (Math.Sqrt(vx * vx + vy * vy) < 0.1) hit = true;
                    break;
            }

            if (sds && !hit && z > 260) { orbit = true; break; }
            if (gx < 1 || gx > 49 || gy < 1 || gy > 49) hit = true;
        } while (!hit && steps <= Balance.MaxFlightSteps);

        bool off = orbit || Pas.Round(x) < 1 || Pas.Round(x) > 49 || Pas.Round(y) < 1 || Pas.Round(y) > 49;
        return new FlightResult(new Vec3d(x, y, z), off, orbit, path, steps);
    }

    private static double Dist(Tank t, double x, double y)
    {
        double dx = t.X - x, dy = t.Y - y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
