using System.Diagnostics;
using Artillery.Core.Ai;
using Artillery.Core.Items;
using Artillery.Core.Match;
using Artillery.Core.Physics;
using Artillery.Core.World;
using Xunit;

namespace Artillery.Core.Tests;

public class AiTests
{
    private static MatchState NewState(uint seed, PlayerType type, bool hilly = false, int ground = 0)
    {
        var s = new MatchState(new MatchSettings { Seed = seed });
        s.World.Landscape = new LandscapeDef { Id = 13, Name = "T", Mode = GenMode.Plain };
        s.World.Height.Fill(ground);
        if (hilly)
            for (int x = Heightmap.Min; x <= Heightmap.Max; x++)
                for (int y = Heightmap.Min; y <= Heightmap.Max; y++)
                    s.World.Height[x, y] = ground + (int)(4 * Math.Sin(x * 0.4) + 3 * Math.Cos(y * 0.5));
        void Add(int i, double x, double y, PlayerType t) =>
            s.Tanks.Add(new Tank { Index = i, Name = "T" + i, X = x, Y = y, Hull = 100, Type = t,
                Z = s.World.Height.HeightAt(x, y) + 0.5 });
        Add(1, 8, 12, type);
        Add(2, 34, 30, PlayerType.Human);
        s.Tanks[1].Weapons[1] = Tank.Infinite;
        s.Tanks[2].Weapons[1] = Tank.Infinite;
        s.TurnCounter = 1;
        return s;
    }

    private static FlightResult Sim(MatchState s, ShotInput a)
    {
        var t = s[1];
        return Ballistics.Fly(s, 1, WeaponId.BasicShell, CasingId.Impact, Ballistics.LaunchPoint(s, t),
            Ballistics.LaunchVelocity(t, a.Power, a.Bearing, a.Elevation), false);
    }

    private static double Miss(MatchState s, ShotInput a)
    {
        var r = Sim(s, a);
        var e = s[2];
        return Math.Sqrt((r.Impact.X - e.X) * (r.Impact.X - e.X) + (r.Impact.Y - e.Y) * (r.Impact.Y - e.Y));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DolphinLandsOnEnemy(bool hilly)
    {
        for (uint seed = 1; seed <= 12; seed++)
        {
            var s = NewState(seed * 7919, PlayerType.Dolphin, hilly);
            var shot = AiPlayer.ChooseShot(s, 1);
            Assert.Equal(CasingId.Impact, shot.Casing);
            var r = Sim(s, shot);
            Assert.True(Math.Abs(r.Impact.X - s[2].X) <= 2.5 && Math.Abs(r.Impact.Y - s[2].Y) <= 2.5,
                $"seed {seed}: landed ({r.Impact.X:F1},{r.Impact.Y:F1})");
        }
    }

    private static double MeanFirstMiss(PlayerType type, int runs)
    {
        double sum = 0;
        for (uint i = 1; i <= runs; i++)
        {
            var s = NewState(i * 104729, type);
            sum += Miss(s, AiPlayer.ChooseShot(s, 1));
        }
        return sum / runs;
    }

    [Fact]
    public void GoldfishFirstShotWorseThanFox() =>
        Assert.True(MeanFirstMiss(PlayerType.Goldfish, 40) > MeanFirstMiss(PlayerType.Fox, 40));

    [Fact]
    public void MemoryConvergesForGoldfish()
    {
        double first = 0, second = 0;
        const int runs = 40;
        for (uint i = 1; i <= runs; i++)
        {
            var s = NewState(i * 15485863, PlayerType.Goldfish);
            first += Miss(s, AiPlayer.ChooseShot(s, 1));
            second += Miss(s, AiPlayer.ChooseShot(s, 1));
            Assert.Equal(2, s[1].Ai.ShotsAtTarget);
        }
        Assert.True(second < first, $"first {first / runs:F1} second {second / runs:F1}");
    }

    [Fact]
    public void WarmUpUsesBasicShellThenBetterWeapon()
    {
        var s = NewState(99, PlayerType.Dolphin);
        s[1].Weapons[(int)WeaponId.HeShell] = 5;
        Assert.Equal(WeaponId.BasicShell, AiPlayer.ChooseShot(s, 1).Weapon);   // k = 1 <= 13 - 12
        Assert.Equal(WeaponId.HeShell, AiPlayer.ChooseShot(s, 1).Weapon);      // k = 2
        var g = NewState(99, PlayerType.Goldfish);
        g[1].Weapons[(int)WeaponId.HeShell] = 5;
        for (int i = 0; i < 5; i++) Assert.Equal(WeaponId.BasicShell, AiPlayer.ChooseShot(g, 1).Weapon);
    }

    [Fact]
    public void TeleportsOutOfLava()
    {
        int teleports = 0;
        for (uint seed = 1; seed <= 6; seed++)
        {
            var s = NewState(seed * 31337, PlayerType.Gerbil, ground: 20);
            s[1].X = 25; s[1].Y = 25;
            s[2].X = 5; s[2].Y = 5; s[2].Z = 20.5;
            s[1].Weapons[(int)WeaponId.Teleporter] = 1;
            s.World.Effects[25, 25] = 22;
            var shot = AiPlayer.ChooseShot(s, 1);
            if (shot.Weapon == WeaponId.Teleporter) { teleports++; Assert.NotNull(shot.Target); }
        }
        Assert.True(teleports >= 3, $"teleports {teleports}");
    }

    [Fact]
    public void DecisionIsFast()
    {
        var s = NewState(5, PlayerType.Dolphin, hilly: true);
        AiPlayer.ChooseShot(s, 1);   // warm up JIT
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 5; i++) { s[1].Ai.LastTarget = 0; AiPlayer.ChooseShot(s, 1); }
        Assert.True(sw.ElapsedMilliseconds / 5 < 200, $"{sw.ElapsedMilliseconds / 5} ms");
    }
}
