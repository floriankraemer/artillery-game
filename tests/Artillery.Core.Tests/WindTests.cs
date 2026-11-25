using Artillery.Core.Items;
using Artillery.Core.Match;
using Artillery.Core.Physics;
using Artillery.Core.World;
using Xunit;

namespace Artillery.Core.Tests;

public class WindTests
{
    private static MatchState NewState(bool wind, int bearing = 0, int strength = 0)
    {
        var s = new MatchState(new MatchSettings { Wind = wind });
        s.World.Landscape = new LandscapeDef { Id = 13, Name = "Flat", Mode = GenMode.Plain };
        s.World.Height.Fill(0);
        s.Tanks.Add(new Tank { Index = 1, Name = "A", X = 5, Y = 25, Hull = 100, Z = 0.5 });
        s.Tanks.Add(new Tank { Index = 2, Name = "B", X = 45, Y = 25, Hull = 100, Z = 0.5 });
        s.WindBearing = bearing; s.WindStrength = strength;
        return s;
    }

    private static FlightResult Shoot(MatchState s)
    {
        var t = s[1];
        return Ballistics.Fly(s, 1, WeaponId.BasicShell, CasingId.Impact, Ballistics.LaunchPoint(s, t), Ballistics.LaunchVelocity(t, 40, 0, 45), false);
    }

    [Fact]
    public void WindOffOrCalmChangesNothing()
    {
        var plain = Shoot(NewState(false));
        Assert.Equal(plain.Impact, Shoot(NewState(false, 90, 10)).Impact);   // setting off: strength ignored
        Assert.Equal(plain.Impact, Shoot(NewState(true, 90, 0)).Impact);     // calm
    }

    [Fact]
    public void TailwindExtendsAndHeadwindShortensRange()
    {
        double plain = Shoot(NewState(false)).Impact.X;
        Assert.True(Shoot(NewState(true, 0, 8)).Impact.X > plain + 3);     // bearing 0 = +x, same as the shot
        Assert.True(Shoot(NewState(true, 180, 8)).Impact.X < plain - 3);
    }

    [Fact]
    public void CrosswindDriftsSideways()
    {
        double plainY = Shoot(NewState(false)).Impact.Y;
        double east = Shoot(NewState(true, 90, 8)).Impact.Y;     // bearing 90 = -y
        double west = Shoot(NewState(true, 270, 8)).Impact.Y;    // bearing 270 = +y
        Assert.True(east < plainY - 3);
        Assert.True(west > plainY + 3);
    }

    [Fact]
    public void WindIsRerolledOnlyEveryNShots()
    {
        var e = new MatchEngine(new MatchSettings { Wind = true, WindChangeEvery = 3, Seed = 7, Rounds = 1, MaxShots = 50 });
        e.StartMatch(); e.StartRound();
        var seen = new List<(int, int)>();
        for (int i = 0; i < 9 && e.State.Phase == MatchPhase.Playing; i++)
        {
            e.BeginTurn();
            seen.Add((e.State.WindBearing, e.State.WindStrength));
            e.Fire(new ShotInput(WeaponId.BasicShell, CasingId.Impact, 10, 0, 45));
        }
        Assert.Equal(seen[0], seen[1]); Assert.Equal(seen[0], seen[2]);
        Assert.Equal(seen[3], seen[4]); Assert.Equal(seen[3], seen[5]);
        Assert.Equal(seen[6], seen[8]);
    }

    [Fact]
    public void WindIsDeterministicForTheSameSeed()
    {
        (int, int) Roll()
        {
            var e = new MatchEngine(new MatchSettings { Wind = true, Seed = 99 });
            e.StartMatch(); e.StartRound(); e.BeginTurn();
            return (e.State.WindBearing, e.State.WindStrength);
        }
        Assert.Equal(Roll(), Roll());
    }
}
