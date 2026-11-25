using Artillery.Core;
using Artillery.Core.Items;
using Artillery.Core.Match;
using Xunit;

namespace Artillery.Core.Tests;

public class ShopEconomyTests
{
    private static Tank NewTank(long cash = 100000, PlayerType type = PlayerType.Human) =>
        new() { Index = 1, Name = "T", Type = type, Cash = cash };

    private static MatchState NewState(params Tank[] tanks)
    {
        var s = new MatchState(new MatchSettings());
        foreach (var t in tanks) s.Tanks.Add(t);
        return s;
    }

    // ---- Free space ---------------------------------------------------------------------------

    [Fact]
    public void FreeSpace_EmptyTankHasBaseCapacity()
    {
        Assert.Equal(Balance.BaseCapacity, Shop.FreeSpace(NewTank()));
    }

    [Fact]
    public void FreeSpace_CountsWeaponsUtilitiesAndCasingsTwoEach()
    {
        var t = NewTank();
        t.Weapons[(int)WeaponId.HeShell] = 3;           // 3 × 1
        t.Utilities[(int)UtilityId.GravityShield] = 1;  // 25
        t.Casings[(int)CasingId.Bouncing] = 2;          // 2 × 2
        Assert.Equal(100 - 3 - 25 - 4, Shop.FreeSpace(t));
    }

    [Fact]
    public void FreeSpace_IgnoresInfiniteItems()
    {
        var t = NewTank();
        t.Weapons[(int)WeaponId.BasicShell] = Tank.Infinite;
        t.Casings[(int)CasingId.Impact] = Tank.Infinite;
        Assert.Equal(Balance.BaseCapacity, Shop.FreeSpace(t));
    }

    [Fact]
    public void HullExtension_RaisesCapacityBy60()
    {
        var t = NewTank();
        t.Utilities[(int)UtilityId.HullExtension] = 1;
        Assert.Equal(160, Shop.FreeSpace(t));
    }

    // ---- Buying -------------------------------------------------------------------------------

    [Fact]
    public void Buy_Weapon_DebitsPriceAndAddsOneUnit()
    {
        var t = NewTank(cash: 1000);
        Assert.True(Shop.Buy(t, WeaponId.HeShell));
        Assert.Equal(990, t.Cash);
        Assert.Equal(1, t.Count(WeaponId.HeShell));
    }

    [Fact]
    public void Buy_RefusedWhenUnaffordable()
    {
        var t = NewTank(cash: 49);
        Assert.False(Shop.Buy(t, WeaponId.Missile));   // price 50
        Assert.Equal(49, t.Cash);
        Assert.Equal(0, t.Count(WeaponId.Missile));
    }

    [Fact]
    public void Buy_BasicShellAndImpactAreNotBuyable()
    {
        var t = NewTank();
        Assert.False(Shop.Buy(t, WeaponId.BasicShell));
        Assert.False(Shop.Buy(t, CasingId.Impact));
    }

    [Fact]
    public void Buy_HiddenSatelliteWeaponsAreNotBuyable()
    {
        var t = NewTank();
        Assert.False(Shop.Buy(t, WeaponId.SatelliteLaunch));
    }

    [Fact]
    public void Buy_RefusedWhenNoSpace()
    {
        var t = NewTank();
        t.Weapons[(int)WeaponId.Nuke] = 4;              // 4 × 25 = 100 space used
        Assert.Equal(0, Shop.FreeSpace(t));
        Assert.False(Shop.CanBuy(t, WeaponId.HeShell)); // needs 1
        Assert.False(Shop.Buy(t, WeaponId.HeShell));
    }

    [Fact]
    public void Buy_WeaponStackCappedAtMaxStack()
    {
        var t = NewTank();
        t.Weapons[(int)WeaponId.HeShell] = Balance.MaxStack;
        Assert.False(Shop.Buy(t, WeaponId.HeShell));
    }

    [Fact]
    public void Buy_UtilityRespectsMaxCount()
    {
        var t = NewTank();
        Assert.True(Shop.Buy(t, UtilityId.GravityShield));   // max 1
        Assert.False(Shop.Buy(t, UtilityId.GravityShield));
        Assert.Equal(1, t.Count(UtilityId.GravityShield));
    }

    [Fact]
    public void Buy_CasingCostsTwoSpaceAndNotImpact()
    {
        var t = NewTank(cash: 1000);
        Assert.True(Shop.Buy(t, CasingId.Bouncing));
        Assert.Equal(900, t.Cash);
        Assert.Equal(98, Shop.FreeSpace(t));
    }

    [Fact]
    public void Buy_CasingRefusedWithLessThanTwoSpace()
    {
        var t = NewTank();
        t.Weapons[(int)WeaponId.Nuke] = 3;              // 75 used, 25 free
        t.Weapons[(int)WeaponId.HeShell] = 23;          // 23 used, 2 free
        Assert.Equal(2, Shop.FreeSpace(t));
        Assert.True(Shop.Buy(t, CasingId.Rolling));     // exactly 2 free is enough
        Assert.False(Shop.Buy(t, CasingId.Bouncing));   // now 0 free
    }

    // ---- Hull extension -----------------------------------------------------------------------

    [Fact]
    public void Buy_HullExtensionAddsSixtySpace()
    {
        var t = NewTank(cash: 5000);
        t.Weapons[(int)WeaponId.Nuke] = 4;              // full
        Assert.False(Shop.Buy(t, WeaponId.Nuke));       // no space
        Assert.True(Shop.Buy(t, UtilityId.HullExtension));
        Assert.Equal(60, Shop.FreeSpace(t));
        Assert.True(Shop.Buy(t, WeaponId.Nuke));        // now fits
    }

    [Fact]
    public void Sell_HullExtensionRefusedWhenSpaceWouldGoNegative()
    {
        var t = NewTank(cash: 0);
        t.Utilities[(int)UtilityId.HullExtension] = 1;  // capacity 160
        t.Weapons[(int)WeaponId.Nuke] = 6;              // 150 used, FreeSpace 10
        Assert.Equal(10, Shop.FreeSpace(t));

        Assert.False(Shop.Sell(t, UtilityId.HullExtension));   // would leave capacity 100 with 150 used
        Assert.Equal(1, t.Count(UtilityId.HullExtension));
        Assert.Equal(0, t.Cash);
    }

    [Fact]
    public void Sell_HullExtensionAllowedWhenItFits()
    {
        var t = NewTank(cash: 0);
        t.Utilities[(int)UtilityId.HullExtension] = 1;
        t.Weapons[(int)WeaponId.Nuke] = 2;              // 50 used, after sale 100 cap -> 50 free
        Assert.True(Shop.Sell(t, UtilityId.HullExtension));
        Assert.Equal(0, t.Count(UtilityId.HullExtension));
        Assert.Equal(1800, t.Cash);
    }

    // ---- Selling ------------------------------------------------------------------------------

    [Fact]
    public void Sell_Weapon_RefundsFullPriceOfOneUnit()
    {
        var t = NewTank(cash: 1000);
        Shop.Buy(t, WeaponId.Missile);                  // 950
        Shop.Buy(t, WeaponId.Missile);                  // 900
        Assert.True(Shop.Sell(t, WeaponId.Missile));
        Assert.Equal(950, t.Cash);
        Assert.Equal(1, t.Count(WeaponId.Missile));
    }

    [Fact]
    public void Sell_RefusedWhenNoneOwned()
    {
        var t = NewTank();
        Assert.False(Shop.Sell(t, WeaponId.Missile));
        Assert.False(Shop.Sell(t, UtilityId.Parachute));
        Assert.False(Shop.Sell(t, CasingId.Bouncing));
    }

    [Fact]
    public void Sell_BasicShellAndImpactNotSellable()
    {
        var t = NewTank();
        t.Weapons[(int)WeaponId.BasicShell] = Tank.Infinite;
        t.Casings[(int)CasingId.Impact] = Tank.Infinite;
        Assert.False(Shop.Sell(t, WeaponId.BasicShell));
        Assert.False(Shop.Sell(t, CasingId.Impact));
        Assert.Equal(Tank.Infinite, t.Count(WeaponId.BasicShell));
    }

    [Fact]
    public void Sell_CasingRefundsPrice()
    {
        var t = NewTank(cash: 0);
        t.Casings[(int)CasingId.Cruise] = 1;
        Assert.True(Shop.Sell(t, CasingId.Cruise));
        Assert.Equal(500, t.Cash);
    }

    // ---- Shop entry ---------------------------------------------------------------------------

    [Fact]
    public void PrepareForShopping_SetsInfiniteAndConvertsLaunchedSatellite()
    {
        var t = NewTank();
        t.Weapons[(int)WeaponId.SatelliteLaunch] = 1;
        Shop.PrepareForShopping(t);
        Assert.Equal(Tank.Infinite, t.Count(WeaponId.BasicShell));
        Assert.Equal(Tank.Infinite, t.Count(CasingId.Impact));
        Assert.Equal(0, t.Count(WeaponId.SatelliteLaunch));
        Assert.Equal(1, t.Count(UtilityId.SatelliteLaser));
    }

    // ---- Round-end cash -----------------------------------------------------------------------

    [Fact]
    public void EndRound_CashFormula_Score100Cash1000_Gives1620()
    {
        var human = NewTank(cash: 1000);
        human.RoundScore = 100;
        var s = NewState(human);

        Economy.EndRound(s);

        Assert.Equal(1620, human.Cash);   // (1000 + 5·100) × 1.08
        Assert.Equal(100, human.TotalScore);
        Assert.Equal(0, human.RoundScore);
    }

    [Fact]
    public void EndRound_AbortedRoundHasNoInterest()
    {
        var human = NewTank(cash: 1000);
        human.RoundScore = 100;
        var s = NewState(human);
        s.RoundAborted = true;

        Economy.EndRound(s);

        Assert.Equal(1500, human.Cash);   // no ×1.08
    }

    [Fact]
    public void EndRound_ComputerGetsExtraRoundScoreInCash()
    {
        var ai = NewTank(cash: 1000, type: PlayerType.Owl);
        ai.RoundScore = 100;
        var s = NewState(ai);

        Economy.EndRound(s);

        Assert.Equal(1620 + 100, ai.Cash);
    }

    [Fact]
    public void EndRound_NegativeScoreGivesNoBonusAndCashNeverNegative()
    {
        var human = NewTank(cash: 0);
        human.RoundScore = -300;
        var s = NewState(human);

        Economy.EndRound(s);

        Assert.Equal(0, human.Cash);
        Assert.Equal(-300, human.TotalScore);
    }

    // ---- Match start --------------------------------------------------------------------------

    [Fact]
    public void StartMatch_ResetsCashScoresWinsAndInventory()
    {
        var t = NewTank(cash: 1);
        t.TotalScore = 500;
        t.RoundScore = 20;
        t.Wins = 2;
        t.Weapons[(int)WeaponId.Nuke] = 3;
        t.Utilities[(int)UtilityId.HullExtension] = 2;
        t.Casings[(int)CasingId.Cruise] = 1;
        var s = NewState(t);
        s.Settings.StartCash = 2000;

        Economy.StartMatch(s);

        Assert.Equal(2000, t.Cash);
        Assert.Equal(0, t.TotalScore);
        Assert.Equal(0, t.RoundScore);
        Assert.Equal(0, t.Wins);
        Assert.Equal(0, t.Count(WeaponId.Nuke));
        Assert.Equal(0, t.Count(UtilityId.HullExtension));
        Assert.Equal(0, t.Count(CasingId.Cruise));
    }

    // ---- Computer shopping --------------------------------------------------------------------

    [Theory]
    [InlineData(PlayerType.Goldfish, 1)]
    [InlineData(PlayerType.Owl, 2)]
    public void AiShop_SpendsMoneyAndNeverExceedsSpace(PlayerType type, int seed)
    {
        for (int run = 0; run < 25; run++)
        {
            var ai = NewTank(cash: 500 + run * 900, type: type);
            var s = new MatchState(new MatchSettings { Seed = (uint)(seed * 1000 + run) });
            s.Tanks.Add(ai);
            long before = ai.Cash;

            Shop.AiShop(s, ai);

            Assert.True(ai.Cash >= 0);
            Assert.True(ai.Cash <= before);
            Assert.True(Shop.FreeSpace(ai) >= 0, $"run {run}: free space {Shop.FreeSpace(ai)}");
            Assert.Equal(Tank.Infinite, ai.Count(WeaponId.BasicShell));
        }
    }

    [Fact]
    public void AiShop_RichComputerBuysWeaponsAndHullExtension()
    {
        var ai = NewTank(cash: 200000, type: PlayerType.Dolphin);
        var s = new MatchState(new MatchSettings { Seed = 777 });
        s.Tanks.Add(ai);

        Shop.AiShop(s, ai);

        Assert.True(Shop.FreeSpace(ai) >= 0);
        Assert.True(ai.Count(UtilityId.HullExtension) > 0, "a full tank should trigger the hull extension fallback");
        Assert.True(ai.Count(WeaponId.Nuke) + ai.Count(WeaponId.HeShell) + ai.Count(WeaponId.Missile) > 0);
    }

    [Fact]
    public void AiShop_FloatsOnlyOnWaterLevels()
    {
        var dry = NewTank(cash: 200000, type: PlayerType.Dolphin);
        var sDry = new MatchState(new MatchSettings { Seed = 5 });
        sDry.Tanks.Add(dry);
        Shop.AiShop(sDry, dry);
        Assert.Equal(0, dry.Count(UtilityId.Floats));

        var wet = NewTank(cash: 200000, type: PlayerType.Dolphin);
        var sWet = new MatchState(new MatchSettings { Seed = 5 });
        sWet.Tanks.Add(wet);
        sWet.World.Landscape = new Artillery.Core.World.LandscapeDef
        {
            Id = 15, Name = "Test", Mode = Artillery.Core.World.GenMode.Plain, HasWater = true, WaterLevel = 5,
        };
        Shop.AiShop(sWet, wet);
        Assert.True(wet.Count(UtilityId.Floats) >= 1);
    }
}
