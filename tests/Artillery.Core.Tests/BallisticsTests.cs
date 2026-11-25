using Artillery.Core.Items;
using Artillery.Core.Match;
using Artillery.Core.Physics;
using Artillery.Core.Util;
using Artillery.Core.World;
using Xunit;

namespace Artillery.Core.Tests;

public class BallisticsTests
{
    private static MatchState NewState(int tanks = 2, int ground = 0, bool air = false, bool water = false, int waterLevel = 0)
    {
        var s = new MatchState(new MatchSettings { AirResistance = air });
        s.World.Landscape = new LandscapeDef { Id = 13, Name = "Flat", Mode = GenMode.Plain, HasWater = water, WaterLevel = waterLevel };
        s.World.Height.Fill(ground);
        for (int i = 1; i <= tanks; i++)
            s.Tanks.Add(new Tank { Index = i, Name = "T" + i, X = 5, Y = 10 + 20 * (i - 1), Hull = 100, Z = ground + 0.5 });
        return s;
    }

    private static FlightResult Shoot(MatchState s, double power, double bearing, double elev,
        CasingId casing = CasingId.Impact, bool path = false, WeaponId weapon = WeaponId.BasicShell)
    {
        var t = s[1];
        return Ballistics.Fly(s, 1, weapon, casing, Ballistics.LaunchPoint(s, t),
            Ballistics.LaunchVelocity(t, power, bearing, elev), path);
    }

    [Fact]
    public void LaunchVectorMatchesSpec()
    {
        var s = NewState();
        var v = Ballistics.LaunchVelocity(s[1], 100, 0, 0);
        Assert.Equal(1 / 2.2, v.X, 9);
        Assert.Equal(0, v.Z, 9);
        var up = Ballistics.LaunchVelocity(s[1], 100, 0, 90);
        Assert.Equal(2.2, up.Z, 9);
        var east = Ballistics.LaunchVelocity(s[1], 100, 90, 0);   // bearing 90 -> -y
        Assert.True(east.Y < 0 && Math.Abs(east.X) < 1e-9);
        var lp = Ballistics.LaunchPoint(s, s[1]);
        Assert.Equal(new Vec3d(5.45, 10.45, 1), lp);
    }

    [Fact]
    public void FlatRangeMatchesFormula()
    {
        var s = NewState();
        double g = s.World.Gravity;
        var r = Shoot(s, 50, 0, 45);
        double expected = 1.21 * 0.25 * Math.Sin(Math.PI / 2) / g;   // ~30 cells
        Assert.False(r.OffMap);
        Assert.InRange(r.Impact.X - 5.45, expected - 2, expected + 5); // launch is 1 unit above ground
    }

    [Fact]
    public void PowerBoosterIncreasesRange()
    {
        var s = NewState();
        var plain = Shoot(s, 30, 0, 45);
        s[1].Utilities[(int)UtilityId.PowerBooster] = 1;
        var boosted = Shoot(s, 30, 0, 45);
        Assert.True(boosted.Impact.X > plain.Impact.X + 5);
    }

    [Fact]
    public void AirResistanceReducesRange()
    {
        var dry = Shoot(NewState(), 50, 0, 45);
        var wet = Shoot(NewState(air: true), 50, 0, 45);
        Assert.True(wet.Impact.X < dry.Impact.X - 2);
    }

    [Fact]
    public void FlamethrowerIgnoresAir()
    {
        var a = Shoot(NewState(air: true), 50, 0, 45, weapon: WeaponId.Flamethrower);
        var b = Shoot(NewState(air: false), 50, 0, 45, weapon: WeaponId.Flamethrower);
        Assert.Equal(a.Impact, b.Impact);
    }

    [Fact]
    public void OffMapDetected()
    {
        var s = NewState();
        var r = Shoot(s, 100, 0, 45);
        Assert.True(r.OffMap);
        Assert.True(Pas.Round(r.Impact.X) > 49);
    }

    [Fact]
    public void PathRecorded()
    {
        var s = NewState();
        var r = Shoot(s, 50, 0, 45, path: true);
        Assert.Equal(r.Steps, r.Path.Count);
        Assert.Empty(Shoot(s, 50, 0, 45, path: false).Path);
    }

    [Fact]
    public void FlightDoesNotMutateState()
    {
        var s = NewState();
        var before = s[1].Position;
        Shoot(s, 50, 0, 45);
        Assert.Equal(before, s[1].Position);
    }

    [Fact]
    public void BouncingBouncesThenExplodes()
    {
        var s = NewState();
        var plain = Shoot(s, 50, 0, 45, path: true);
        var b = Shoot(s, 50, 0, 45, CasingId.Bouncing, path: true);
        Assert.True(b.Impact.X > plain.Impact.X + 2);
        Assert.True(b.Steps > plain.Steps + 10);
        // bounced: z rises again after first ground contact
        int firstLow = b.Path.ToList().FindIndex(p => p.Z < 0.5);
        Assert.True(firstLow > 0);
        Assert.Contains(b.Path.Skip(firstLow + 1), p => p.Z > 0.3);
    }

    [Fact]
    public void TunnelingEndsBelowGround()
    {
        var s = NewState(ground: 10);
        var r = Shoot(s, 50, 0, 45, CasingId.Tunneling);
        Assert.True(r.Impact.Z < s.World.Height.HeightAt(r.Impact.X, r.Impact.Y));
    }

    [Fact]
    public void CruiseFollowsTerrain()
    {
        var s = NewState(ground: 5);
        for (int x = 20; x <= 55; x++) for (int y = -5; y <= 55; y++) s.World.Height[x, y] = 8;
        var r = Shoot(s, 40, 0, 0, CasingId.Cruise, path: true);
        Assert.True(r.Steps > 30);
        foreach (var p in r.Path.Take(r.Path.Count - 1))
            Assert.True(p.Z >= s.World.Height.HeightAt(p.X, p.Y), $"below ground at {p}");
        var mid = r.Path[Math.Min(25, r.Path.Count - 1)];
        Assert.InRange(mid.Z - s.World.Height.HeightAt(mid.X, mid.Y), 2.5, 3.5);
    }

    [Fact]
    public void CruiseDivesOntoTank()
    {
        var s = NewState();
        s[2].X = 25; s[2].Y = 10;
        var r = Shoot(s, 40, 0, 0, CasingId.Cruise);
        Assert.InRange(r.Impact.X, 22, 28);
        Assert.False(r.OffMap);
    }

    [Fact]
    public void SmartCruiseWithoutTargetActsAsCruise()
    {
        var s = NewState(tanks: 1);
        var a = Shoot(s, 40, 0, 0, CasingId.SmartCruise);
        var b = Shoot(s, 40, 0, 0, CasingId.Cruise);
        Assert.Equal(b.Impact, a.Impact);
    }

    [Fact]
    public void SmartCruiseReachesTarget()
    {
        var s = NewState();
        s[2].X = 30; s[2].Y = 14;
        s[2].Z = 0.5;
        var r = Shoot(s, 40, 0, 0, CasingId.SmartCruise);
        Assert.False(r.OffMap);
        double d = Math.Sqrt(Math.Pow(r.Impact.X - 30.5, 2) + Math.Pow(r.Impact.Y - 14.5, 2));
        Assert.True(d < 6, $"ended {d} from target");
    }

    [Fact]
    public void HeatSeekerBendsTowardTank()
    {
        var s = NewState();
        s[2].X = 25; s[2].Y = 16;
        var plain = Shoot(s, 50, 0, 45);
        var heat = Shoot(s, 50, 0, 45, CasingId.HeatSeeking);
        Assert.True(heat.Impact.Y > plain.Impact.Y + 0.5);
        s[2].Utilities[(int)UtilityId.GravityShield] = 1;
        var shielded = Shoot(s, 50, 0, 45, CasingId.HeatSeeking);
        Assert.Equal(plain.Impact.Y, shielded.Impact.Y, 6);
    }

    [Fact]
    public void GravityShieldDampsVerticalSpeed()
    {
        var s = NewState();
        s[2].X = 20; s[2].Y = 10;
        var plain = Shoot(s, 50, 0, 45);
        s[2].Utilities[(int)UtilityId.GravityShield] = 1;
        var damped = Shoot(s, 50, 0, 45);
        Assert.NotEqual(plain.Impact.X, damped.Impact.X);
    }

    [Fact]
    public void SatelliteReachesOrbit()
    {
        var s = NewState();
        var r = Shoot(s, 100, 0, 90, weapon: WeaponId.SatelliteLaunch);
        Assert.True(r.ReachedOrbit);
        Assert.True(r.Impact.Z > 260);
        var low = Shoot(s, 20, 0, -45, weapon: WeaponId.SatelliteLaunch);
        Assert.False(low.ReachedOrbit);
    }

    [Fact]
    public void FallDamageOnlyDownwardAndParachute()
    {
        var s = NewState();
        var ev = new List<GameEvent>();
        // fell 10 units: 20 damage
        TankPhysics.ApplyFalls(s, new() { [1] = 10.5 }, ev);
        Assert.Equal(80, s[1].Hull);
        Assert.Contains(ev, e => e is TankDamagedEvent d && d.Tank == 1 && d.HullDamage == 20);
        // ground rose: no damage
        s[2].Hull = 100;
        TankPhysics.ApplyFalls(s, new() { [2] = -20.5 }, ev);
        Assert.Equal(100, s[2].Hull);
        // small fall ignored (5)
        TankPhysics.ApplyFalls(s, new() { [2] = 5.5 }, ev);
        Assert.Equal(100, s[2].Hull);
        // parachute consumed
        s[2].Utilities[(int)UtilityId.Parachute] = 2;
        TankPhysics.ApplyFalls(s, new() { [2] = 30.5 }, ev);
        Assert.Equal(100, s[2].Hull);
        Assert.Equal(1, s[2].Count(UtilityId.Parachute));
        s[2].Utilities[(int)UtilityId.Parachute] = 0; // shield absorbs first
        s[2].Shield = 10;
        TankPhysics.ApplyFalls(s, new() { [2] = 10.5 }, ev);   // 20 dmg: 10 shield + 10 hull
        Assert.Equal(0, s[2].Shield);
        Assert.Equal(90, s[2].Hull);
    }

    [Fact]
    public void SettleDropsTankToGround()
    {
        var s = NewState(ground: 7);
        s[1].Z = 30;
        var ev = new List<GameEvent>();
        TankPhysics.Settle(s, ev);
        Assert.Equal(7.5, s[1].Z, 9);
        Assert.Contains(ev, e => e is TankMovedEvent m && m.Tank == 1 && m.Kind == MoveKind.Fall);
    }

    [Fact]
    public void DrowningVersusFloats()
    {
        var s = NewState(water: true, waterLevel: 5);
        s[2].Utilities[(int)UtilityId.Floats] = 1;
        s[1].Shield = 30;
        var ev = new List<GameEvent>();
        TankPhysics.Settle(s, ev);
        Assert.Equal(Tank.HullDead, s[1].Hull);
        Assert.Equal(0, s[1].Shield);
        Assert.Equal(100, s[2].Hull);
        Assert.Equal(5.5, s[2].Z, 9);
    }
}
