using Artillery.Core.Items;
using Artillery.Core.Match;
using Xunit;

namespace Artillery.Core.Tests;

public class MatchEngineTests
{
    private static MatchEngine New(int rounds = 2, PlayOrder order = PlayOrder.Sequential, int maxShots = 25, uint seed = 1,
        int players = 2, params int[] lands)
    {
        var st = new MatchSettings
        {
            Rounds = rounds, MaxShots = maxShots, Order = order, Seed = seed,
            Landscapes = new HashSet<int>(lands.Length > 0 ? lands : new[] { 13 }),
            Players = Enumerable.Range(1, players).Select(i => new PlayerSetup("AI" + i, PlayerType.Fox)).ToList(),
        };
        var e = new MatchEngine(st);
        e.StartMatch();
        return e;
    }

    private static void PlayRound(MatchEngine e)
    {
        e.StartRound();
        int guard = 0;
        while (e.State.Phase == MatchPhase.Playing && guard++ < 5000) e.PlayComputerTurn();
        Assert.NotEqual(MatchPhase.Playing, e.State.Phase);
    }

    [Theory]
    [InlineData(1u, 13)] [InlineData(2u, 10)] [InlineData(3u, 22)] [InlineData(4u, 24)] [InlineData(5u, 16)] [InlineData(6u, 3)]
    public void HeadlessMatchRunsToMatchOver(uint seed, int land)
    {
        var e = New(rounds: 3, seed: seed, players: 3, lands: land);
        while (true)
        {
            PlayRound(e);
            if (e.State.Phase == MatchPhase.MatchOver) break;
            e.BeginRoundSetup();
        }
        Assert.Equal(3, e.State.Round);
        Assert.Equal(3, e.FinalRanking().Count);
    }

    [Fact]
    public void TurnOrder_SequentialVsRandom()
    {
        var a = New(order: PlayOrder.Sequential, players: 4);
        Assert.Equal(new[] { 1, 2, 3, 4 }, a.State.Order);
        bool differs = false;
        for (uint sd = 1; sd < 10; sd++)
        {
            var b = New(order: PlayOrder.Random, players: 4, seed: sd);
            Assert.Equal(new[] { 1, 2, 3, 4 }, b.State.Order.OrderBy(x => x).ToArray());
            if (!b.State.Order.SequenceEqual(new[] { 1, 2, 3, 4 })) differs = true;
        }
        Assert.True(differs);
    }

    [Fact]
    public void LandscapeRotation_LeastUsedHighestIdFirst()
    {
        var e = New(rounds: 6, lands: new[] { 3, 7, 13 });
        var seen = new List<int> { e.State.World.Landscape.Id };
        for (int i = 0; i < 4; i++) { e.BeginRoundSetup(); seen.Add(e.State.World.Landscape.Id); }
        Assert.Equal(new[] { 13, 7, 3, 13, 7 }, seen);
    }

    [Fact]
    public void WinnerBonus_AndWinsAwarded()
    {
        var e = New(rounds: 1);
        e.StartRound();
        e.State[2].Hull = 1; e.State[2].Shield = 0;
        e.State[1].Hull = 100;
        e.SelfDestruct();                       // whoever's turn it is dies
        Assert.Equal(MatchPhase.MatchOver, e.State.Phase);
        int w = e.State.RoundWinner;
        Assert.NotEqual(0, w);
        Assert.Equal(1, e.State[w].Wins);
        // +200 death bonus for surviving + hull + 200 winner bonus (+ blast score), all banked in TotalScore
        Assert.True(e.State[w].TotalScore >= e.State[w].Wins * 200 + 200);
        Assert.Equal(0, e.State[w].RoundScore);
    }

    [Fact]
    public void MaxShots_EndsRoundWithNoWinner()
    {
        var e = New(rounds: 1, maxShots: 5);
        e.StartRound();
        foreach (var t in e.State.AllTanks) { t.Hull = t.MaxHull = 100000; }
        int turns = 0;
        while (e.State.Phase == MatchPhase.Playing && turns++ < 100)
        {
            e.BeginTurn();
            e.Fire(new ShotInput(WeaponId.BasicShell, CasingId.Impact, 0, 0, 45)); // harmless: power 0
            foreach (var t in e.State.AllTanks) { t.Hull = 100000; t.Shield = 0; }
        }
        Assert.Equal(10, turns);
        Assert.Equal(0, e.State.RoundWinner);
        Assert.Equal(MatchPhase.MatchOver, e.State.Phase);
        Assert.All(e.State.AllTanks, t => Assert.Equal(0, t.Wins));
    }

    [Fact]
    public void SelfDestruct_KillsCurrentAndAdvances()
    {
        var e = New(rounds: 2, players: 3);
        e.StartRound();
        int me = e.CurrentPlayer;
        e.SelfDestruct();
        Assert.False(e.State[me].Alive);
        Assert.NotEqual(me, e.CurrentPlayer);
        Assert.Equal(MatchPhase.Playing, e.State.Phase);
    }

    [Fact]
    public void EndRoundNow_NoWinnerNoInterest()
    {
        var e = New(rounds: 2);
        e.StartRound();
        e.State[1].Cash = 1000;
        e.EndRoundNow();
        Assert.Equal(MatchPhase.RoundOver, e.State.Phase);
        Assert.True(e.State.RoundAborted);
        Assert.Equal(0, e.State.RoundWinner);
        Assert.Equal(1000, e.State[1].Cash);
    }

    [Fact]
    public void RoundSetup_AppliesArmourShieldAndSatelliteConversion()
    {
        var e = New();
        var t = e.State[1];
        t.Utilities[(int)UtilityId.HullArmour] = 2;
        t.Utilities[(int)UtilityId.ShieldGenerator] = 3;
        t.Utilities[(int)UtilityId.DeadMansBomb] = 1;
        t.Utilities[(int)UtilityId.SatelliteLaser] = 1;
        e.StartRound();
        Assert.Equal(200, t.Hull);
        Assert.Equal(200, t.MaxHull);
        Assert.Equal(150, t.Shield);
        Assert.Equal(25, t.DeathRadius);
        Assert.Equal(1, t.Count(WeaponId.SatelliteLaunch));
        Assert.Equal(0, t.Count(UtilityId.SatelliteLaser));
    }

    [Fact]
    public void SatelliteFlow_LaunchAimStrikeNextTurn()
    {
        var e = New(rounds: 2);
        e.State[1].Utilities[(int)UtilityId.SatelliteLaser] = 1;
        e.StartRound();
        Assert.Equal(1, e.CurrentPlayer);
        foreach (var t in e.State.AllTanks) t.Hull = t.MaxHull = 5000;

        e.BeginTurn();
        var ev = e.Fire(new ShotInput(WeaponId.SatelliteLaunch, CasingId.Impact, 100, 0, 80));
        Assert.Contains(ev, x => x is MessageEvent m && m.Text.Contains("orbit"));
        Assert.Equal(3, e.State[1].Count(WeaponId.SatelliteAim));
        Assert.Equal(0, e.State[1].Count(WeaponId.SatelliteLaunch));

        e.BeginTurn(); e.Fire(new ShotInput(WeaponId.BasicShell, CasingId.Impact, 0, 0, 45)); // player 2
        Assert.Equal(1, e.CurrentPlayer);
        e.BeginTurn();
        var victim = e.State[2];
        ev = e.Fire(new ShotInput(WeaponId.SatelliteAim, CasingId.Impact, 0, 0, 45, ((int)victim.X, (int)victim.Y)));
        Assert.Equal(2, e.State[1].Count(WeaponId.SatelliteAim));
        Assert.Equal(e.State.TurnCounter + 1, e.State[1].SatelliteFireTurn);

        e.BeginTurn(); e.Fire(new ShotInput(WeaponId.BasicShell, CasingId.Impact, 0, 0, 45)); // player 2
        Assert.Equal(e.State[1].SatelliteFireTurn, e.State.TurnCounter);
        var begin = e.BeginTurn();
        Assert.Contains(begin, x => x is BeamEvent);
        Assert.Equal(-1, e.State[1].SatelliteFireTurn);
    }

    [Fact]
    public void Fire_InvalidWeaponFallsBackToBasicShell()
    {
        var e = New();
        e.StartRound();
        var ev = e.Fire(new ShotInput(WeaponId.Nuke, CasingId.Cruise, 500, 720, 999));
        Assert.Contains(ev, x => x is ProjectileFlightEvent f && f.Weapon == WeaponId.BasicShell && f.Casing == CasingId.Impact);
    }
}
