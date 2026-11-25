using Artillery.Core;
using Artillery.Core.Combat;
using Artillery.Core.Items;
using Artillery.Core.Match;
using Artillery.Core.Util;
using Artillery.Core.World;
using Xunit;

namespace Artillery.Core.Tests;

public class CombatTests
{
    private static MatchState NewState(int tanks = 2, int flat = 20)
    {
        var s = new MatchState(new MatchSettings { Seed = 777 });
        for (int i = 1; i <= tanks; i++)
            s.Tanks.Add(new Tank { Index = i, Name = "P" + i, X = 10 + 10 * i, Y = 25, Hull = 100 });
        s.World.Height.Fill(flat);
        foreach (var t in s.AllTanks) t.Z = flat + 0.5;
        return s;
    }

    private static WeaponDef Def(WeaponId id) => ItemCatalog.Get(id);

    [Fact]
    public void MissileDamageAtDistanceOne()
    {
        for (uint seed = 1; seed < 20; seed++)
        {
            var s = NewState();
            s.Rng.Seed = seed;
            s[2].X = 31; s[2].Y = 25;      // d = 1 from impact (30,25)
            s[1].X = 5; s[1].Y = 5;
            Blast.StandardBlast(s, 1, Def(WeaponId.Missile), new Vec3d(30, 25, 20), 5, new List<GameEvent>());
            int dmg = 100 - s[2].Hull;
            Assert.InRange(dmg, 60, 64);
            Assert.Equal(10 + dmg, s[1].RoundScore);   // hit bonus + damage
        }
    }

    [Fact]
    public void ShieldAbsorbsFirstThenHull()
    {
        var s = NewState();
        s[2].X = 31; s[2].Y = 25; s[2].Shield = 20; s[1].X = 5; s[1].Y = 5;
        var ev = new List<GameEvent>();
        Blast.StandardBlast(s, 1, Def(WeaponId.Missile), new Vec3d(30, 25, 20), 5, ev);
        Assert.Equal(0, s[2].Shield);
        Assert.InRange(s[2].Hull, 100 - 44, 100 - 40);
        Assert.Equal(20, ev.OfType<TankDamagedEvent>().Single().ShieldDamage);

        var s2 = NewState();
        s2[2].X = 31; s2[2].Y = 25; s2[2].Shield = 500; s2[1].X = 5; s2[1].Y = 5;
        Blast.StandardBlast(s2, 1, Def(WeaponId.Missile), new Vec3d(30, 25, 20), 5, new List<GameEvent>());
        Assert.Equal(100, s2[2].Hull);
        Assert.InRange(s2[2].Shield, 436, 440);
    }

    [Fact]
    public void SelfDamageCostsDoubleScore()
    {
        var s = NewState();
        s[1].X = 30; s[1].Y = 25; s[2].X = 5; s[2].Y = 5;
        Blast.StandardBlast(s, 1, Def(WeaponId.Missile), new Vec3d(30, 25, 20), 5, new List<GameEvent>());
        int dmg = 100 - s[1].Hull;          // d = 0 -> 1
        Assert.InRange(dmg, 60, 64);
        Assert.Equal(-2 * dmg, s[1].RoundScore);
    }

    [Fact]
    public void CraterProfileOnlyDigsAndIsDeepestAtCentre()
    {
        var s = NewState(2, 30);
        var before = s.World.Height.Clone();
        var ev = new List<GameEvent>();
        Blast.Crater(s, new Vec3d(25, 25, 30), 5, ev);
        var h = s.World.Height;
        for (int x = 1; x <= 50; x++)
        for (int y = 1; y <= 50; y++)
            Assert.True(h[x, y] <= before[x, y], $"raised at {x},{y}");
        Assert.True(h[25, 25] < 30);
        Assert.True(h[25, 25] <= h[25, 28]);
        Assert.Equal(30, h[25, 40]);
        Assert.Contains(ev, e => e is TerrainChangedEvent);
        Assert.Contains(ev, e => e is ExplosionEvent);
        // centre: depth = min(20, 10) = 10 -> T = 25; one blast moves ~1/3 of the way (before smoothing)
        Assert.InRange(h[25, 25], 25, 29);
    }

    [Fact]
    public void DeathGivesBonusToEverySurvivor()
    {
        var s = NewState(3);
        s[1].X = 5; s[1].Y = 5; s[2].X = 45; s[2].Y = 45; s[3].X = 25; s[3].Y = 25;
        s[3].Hull = Tank.HullDead;
        var ev = new List<GameEvent>();
        Deaths.Resolve(s, 1, ev);
        Assert.Equal(200, s[1].RoundScore);
        Assert.Equal(200, s[2].RoundScore);
        Assert.Equal(Tank.HullGone, s[3].Hull);
        Assert.Contains(ev, e => e is TankDiedEvent { Tank: 3, OffMap: false });
    }

    [Fact]
    public void DeadMansBombRadius25ChainsToNeighbours()
    {
        var s = NewState(3);
        s[1].X = 5; s[1].Y = 5; s[3].X = 25; s[3].Y = 25; s[2].X = 38; s[2].Y = 25;   // 13 cells away
        s[3].Utilities[(int)UtilityId.DeadMansBomb] = 1;
        s[2].Hull = 1000;
        s[3].Hull = Tank.HullDead;
        Deaths.Resolve(s, 1, new List<GameEvent>());
        Assert.InRange(1000 - s[2].Hull, (25 - 13) * 15, (25 - 13) * 15 + 24);
        Assert.Equal(0, s[3].Count(UtilityId.DeadMansBomb));

        // chain: a weak tank inside the radius dies and explodes too, both deaths pay the survivor
        var c = NewState(3);
        c[3].X = 25; c[3].Y = 25; c[2].X = 28; c[2].Y = 25; c[2].Hull = 10;
        c[1].X = 5; c[1].Y = 5;
        c[3].DeathRadius = 25; c[3].Hull = Tank.HullDead;
        Deaths.Resolve(c, 1, new List<GameEvent>());
        Assert.Equal(Tank.HullGone, c[2].Hull);
        Assert.Equal(Tank.HullGone, c[3].Hull);
        Assert.True(c[1].RoundScore >= 400);
    }

    [Fact]
    public void OffMapTanksGiveNoBonusAndAreReportedOnce()
    {
        var s = NewState(2);
        s[2].Hull = Tank.HullOffMap;
        var ev = new List<GameEvent>();
        Deaths.Resolve(s, 1, ev);
        Deaths.Resolve(s, 1, ev);
        Assert.Equal(0, s[1].RoundScore);
        Assert.True(ev.OfType<TankDiedEvent>().Single().OffMap);
    }

    [Fact]
    public void NapalmCreatesFireThatDecays()
    {
        var s = NewState();
        var ev = new List<GameEvent>();
        Specials.Apply(s, 1, Def(WeaponId.Napalm), new Vec3d(25, 25, 20), null, ev);
        int count = 0;
        for (int x = 1; x <= 50; x++) for (int y = 1; y <= 50; y++)
            if (Effect.IsFire(s.World.Effects[x, y])) count++;
        Assert.True(count > 20);
        Assert.True(Effect.IsFire(s.World.Effects[25, 25]));
        for (int i = 0; i < 40; i++) EffectTicker.Tick(s, ev);
        for (int x = 1; x <= 50; x++) for (int y = 1; y <= 50; y++)
            Assert.False(Effect.IsFire(s.World.Effects[x, y]));
    }

    [Fact]
    public void FireDamagesTankStandingOnIt()
    {
        var s = NewState();
        s[1].X = 5; s[1].Y = 5;
        s[2].X = 25; s[2].Y = 25;
        s.World.Effects[25, 25] = 8;
        EffectTicker.Tick(s, new List<GameEvent>());
        Assert.InRange(100 - s[2].Hull, 45, 54);
    }

    [Fact]
    public void AcidLowersTerrain()
    {
        var s = NewState();
        var ev = new List<GameEvent>();
        Specials.Apply(s, 1, Def(WeaponId.AcidBomb), new Vec3d(25, 25, 20), null, ev);
        Assert.True(Effect.IsAcid(s.World.Effects[25, 25]));
        EffectTicker.Tick(s, ev);
        Assert.True(s.World.Height[25, 25] < 20);
    }

    [Fact]
    public void MeltingLandscapeLowersTerrainUnderFire()
    {
        var s = NewState();
        s.World.Landscape = new LandscapeDef { Id = 15, Name = "T", Mode = GenMode.Plain, MeltsTerrain = true };
        s.World.Effects[25, 25] = 9;
        EffectTicker.Tick(s, new List<GameEvent>());
        Assert.True(s.World.Height[25, 25] < 20);
    }

    [Fact]
    public void TeleportOffMapKillsWithPenalty()
    {
        var s = NewState();
        Specials.Apply(s, 1, Def(WeaponId.Teleporter), new Vec3d(60, 25, 0), null, new List<GameEvent>());
        Assert.Equal(Tank.HullOffMap, s[1].Hull);
        Assert.Equal(-200, s[1].RoundScore);
        Assert.True(s[1].TeleportedThisRound);
    }

    [Fact]
    public void TeleportOnMapMovesTankAndTrimsShield()
    {
        var s = NewState();
        s[1].Shield = 100;
        Specials.Apply(s, 1, Def(WeaponId.Teleporter), new Vec3d(30, 40, 0), null, new List<GameEvent>());
        Assert.InRange(s[1].X, 28, 32);
        Assert.InRange(s[1].Y, 38, 42);
        Assert.Equal(90, s[1].Shield);
        Assert.True(s[1].Alive);
    }

    [Fact]
    public void ShieldBreakerDrainsOnlyShield()
    {
        var s = NewState();
        s[1].X = 5; s[1].Y = 5; s[2].X = 31; s[2].Y = 25; s[2].Shield = 500;
        Specials.Apply(s, 1, Def(WeaponId.ShieldBreaker), new Vec3d(30, 25, 20), null, new List<GameEvent>());
        Assert.Equal(100, s[2].Hull);
        Assert.InRange(500 - s[2].Shield, 7 * 30 + 50, 7 * 30 + 29 + 50);
        Assert.True(s[1].RoundScore > 0);
    }

    [Fact]
    public void ShockwavePushesAwayFromEpicentre()
    {
        var s = NewState();
        s[1].Hull = Tank.HullGone;                 // skipped, keeps the epicentre jitter within +-2
        s[2].X = 25; s[2].Y = 30; s[2].Z = 20.5;
        Specials.Apply(s, 1, Def(WeaponId.Shockwave), new Vec3d(25, 25, 20), null, new List<GameEvent>());
        Assert.True(s[2].Y >= 30, $"{s[2].X},{s[2].Y},{s[2].Hull}");
        Assert.True(s[2].Hull < 100);              // landing damage
    }

    [Fact]
    public void ShockwaveOffMapScoresShield()
    {
        var s = NewState();
        s[1].X = 5; s[1].Y = 5;
        s[2].X = 47; s[2].Y = 25; s[2].Shield = 30; s[2].Z = 20.5;
        Specials.Apply(s, 1, Def(WeaponId.Shockwave), new Vec3d(44, 25, 20), null, new List<GameEvent>());
        Assert.Equal(Tank.HullOffMap, s[2].Hull);
        Assert.Equal(30, s[1].RoundScore);
    }

    [Fact]
    public void FlamethrowerHitsTankAlongBearing()
    {
        var s = NewState();
        s[1].X = 10; s[1].Y = 25; s[1].Bearing = 0;    // bearing 0 = +X
        s[2].X = 14; s[2].Y = 25; s[2].Shield = 500;
        var ev = new List<GameEvent>();
        Specials.Apply(s, 1, Def(WeaponId.Flamethrower), new Vec3d(10, 25, 20), null, ev);
        Assert.True(s[2].Shield < 500);
        Assert.True(s[1].RoundScore >= 10);
        Assert.Equal(100, s[1].Hull);
        Assert.Contains(ev, e => e is FlameEvent);
    }

    [Fact]
    public void SatelliteAimAndStrike()
    {
        var s = NewState();
        s.TurnCounter = 3;
        s[1].X = 5; s[1].Y = 5; s[1].Weapons[(int)WeaponId.SatelliteAim] = 3;
        Specials.Apply(s, 1, Def(WeaponId.SatelliteAim), default, (25, 25), new List<GameEvent>());
        Assert.Equal(4, s[1].SatelliteFireTurn);
        Assert.Equal(2, s[1].Weapons[(int)WeaponId.SatelliteAim]);
        s[2].X = s[1].SatelliteTargetX; s[2].Y = s[1].SatelliteTargetY; s[2].Hull = 1000;
        Specials.SatelliteStrike(s, s[1], new List<GameEvent>());
        Assert.InRange(1000 - s[2].Hull, 300, 329);   // d = 0: 5*60 + Random(30)
        Assert.True(s[1].RoundScore >= 150);
        Assert.Equal(-1, s[1].SatelliteFireTurn);
    }

    [Fact]
    public void AirStrikeDropsFiveCraters()
    {
        var s = NewState();
        s[1].X = 5; s[1].Y = 5; s[2].X = 45; s[2].Y = 45;
        var ev = new List<GameEvent>();
        Specials.Apply(s, 1, Def(WeaponId.AirStrike), default, (25, 25), ev);
        Assert.Equal(5, ev.OfType<ExplosionEvent>().Count());
        Assert.Equal(WeaponId.BasicShell, s[1].SelectedWeapon);
    }

    [Fact]
    public void BuildersRaiseTerrain()
    {
        var s = NewState();
        Specials.Apply(s, 1, Def(WeaponId.HillMaker), new Vec3d(25, 25, 20), null, new List<GameEvent>());
        Assert.True(s.World.Height[25, 25] > 20);
        var w = NewState();
        Specials.Apply(w, 1, Def(WeaponId.WallMaker), new Vec3d(25, 25, 20), null, new List<GameEvent>());
        Assert.Equal(45, Enumerable.Range(18, 15).Max(i => w.World.Height[25, i]));
        var v = NewState();
        Specials.Apply(v, 1, Def(WeaponId.Volcano), new Vec3d(25, 25, 20), null, new List<GameEvent>());
        Assert.True(v.World.Height[25, 25] >= 55);
        Assert.True(Effect.IsLava(v.World.Effects[25, 25]));
    }

    [Fact]
    public void EarthquakeOnlyDigs()
    {
        var s = NewState();
        var before = s.World.Height.Clone();
        Specials.Apply(s, 1, Def(WeaponId.Earthquake), new Vec3d(25, 25, 20), null, new List<GameEvent>());
        bool lower = false;
        for (int x = 1; x <= 50; x++) for (int y = 1; y <= 50; y++)
        {
            Assert.True(s.World.Height[x, y] <= before[x, y]);
            lower |= s.World.Height[x, y] < before[x, y];
        }
        Assert.True(lower);
    }

    [Fact]
    public void ExtinguisherClearsLavaAndBurningGoo()
    {
        var s = NewState();
        s.World.Effects[25, 25] = 22; s.World.Effects[26, 26] = 13;
        Specials.Apply(s, 1, Def(WeaponId.Extinguisher), new Vec3d(25, 25, 20), null, new List<GameEvent>());
        Assert.Equal(0, s.World.Effects[25, 25]);
        Assert.Equal(0, s.World.Effects[26, 26]);
    }

    [Fact]
    public void UpkeepRules()
    {
        var s = NewState();
        var t = s[1];
        t.Utilities[(int)UtilityId.ShieldGenerator] = 4;     // max 200
        t.Shield = 100;
        Upkeep.StartOfTurn(s, new List<GameEvent>());
        Assert.InRange(t.Shield, 102, 104);

        t.Shield = 0;                                        // depleted: no generator regen
        Upkeep.StartOfTurn(s, new List<GameEvent>());
        Assert.Equal(0, t.Shield);

        t.Utilities[(int)UtilityId.ShieldRecharger] = 1;     // works from 0
        Upkeep.StartOfTurn(s, new List<GameEvent>());
        Assert.InRange(t.Shield, 10, 19);

        t.Shield = 180;                                      // not below 50*gens-20: recharger idle, generators only (< 190)
        Upkeep.StartOfTurn(s, new List<GameEvent>());
        Assert.InRange(t.Shield, 182, 184);

        var r = NewState();
        var u = r[1];
        u.Utilities[(int)UtilityId.AutoRepair] = 1;
        u.Utilities[(int)UtilityId.HullArmour] = 2;
        u.Hull = 100;
        Upkeep.StartOfTurn(r, new List<GameEvent>());
        Assert.InRange(u.Hull, 105, 119);
        Assert.Equal(1, u.Count(UtilityId.HullArmour));      // damaged: loses a plate
        u.Hull = 200;                                        // >= 100 + 50*1 and >= 50*1+80: nothing happens
        Upkeep.StartOfTurn(r, new List<GameEvent>());
        Assert.Equal(1, u.Count(UtilityId.HullArmour));
        Assert.Equal(200, u.Hull);
    }

    [Fact]
    public void GravityWellDriftsTanksWithinBounds()
    {
        var s = NewState();
        s.World.Landscape = new LandscapeDef { Id = 22, Name = "T", Mode = GenMode.BlackHole, TankDrift = true };
        s[1].X = 5; s[1].Y = 45;
        for (int i = 0; i < 200; i++) EffectTicker.Tick(s, new List<GameEvent>());
        Assert.InRange(s[1].X, 5, 45);
        Assert.InRange(s[1].Y, 5, 45);
    }
}
