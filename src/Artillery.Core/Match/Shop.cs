using Artillery.Core.Items;
using Artillery.Core.Util;

namespace Artillery.Core.Match;

/// <summary>
/// Shop rules and the computer shopping script (docs/GAME-MECHANICS.md §6, §10).
/// Every buy and sell moves exactly one unit. Infinite items (count 255) take no space.
/// </summary>
public static class Shop
{
    private const int MaxHullExtensions = 12;

    /// <summary>Free cargo space: capacity minus utility, weapon and casing (2..7) space.</summary>
    /// <remarks>Hull Extension has negative space, so it raises capacity (docs §6 "Tank capacity").</remarks>
    public static int FreeSpace(Tank t)
    {
        int free = Balance.BaseCapacity;
        foreach (var u in ItemCatalog.Utilities) free -= u.Space * t.Count(u.Id);
        foreach (var w in ItemCatalog.Weapons)
        {
            int n = t.Count(w.Id);
            if (n != Tank.Infinite) free -= w.Space * n;
        }
        for (int c = (int)CasingId.Bouncing; c <= (int)CasingId.SmartCruise; c++)
            free -= Balance.CasingSpace * t.Casings[c];
        return free;
    }

    // ---- Buying -----------------------------------------------------------------------------

    /// <summary>Weapons: not BasicShell, not hidden, cash ≥ price, count &lt; MaxStack, space fits.</summary>
    public static bool CanBuy(Tank t, WeaponId w)
    {
        var d = ItemCatalog.Get(w);
        return w != WeaponId.BasicShell && !d.Hidden
            && t.Cash >= d.Price && t.Count(w) < Balance.MaxStack && FreeSpace(t) >= d.Space;
    }

    /// <summary>Utilities: cash ≥ price, count &lt; MaxCount, space fits.</summary>
    public static bool CanBuy(Tank t, UtilityId u)
    {
        var d = ItemCatalog.Get(u);
        return t.Cash >= d.Price && t.Count(u) < d.MaxCount && FreeSpace(t) >= d.Space;
    }

    /// <summary>Casings: not Impact, cash ≥ price, count &lt; MaxStack, 2 space free.</summary>
    public static bool CanBuy(Tank t, CasingId c)
    {
        var d = ItemCatalog.Get(c);
        return c != CasingId.Impact
            && t.Cash >= d.Price && t.Count(c) < Balance.MaxStack && FreeSpace(t) >= Balance.CasingSpace;
    }

    public static bool Buy(Tank t, WeaponId w)
    {
        if (!CanBuy(t, w)) return false;
        t.Cash -= ItemCatalog.Get(w).Price;
        t.Weapons[(int)w]++;
        return true;
    }

    public static bool Buy(Tank t, UtilityId u)
    {
        if (!CanBuy(t, u)) return false;
        t.Cash -= ItemCatalog.Get(u).Price;
        t.Utilities[(int)u]++;
        return true;
    }

    public static bool Buy(Tank t, CasingId c)
    {
        if (!CanBuy(t, c)) return false;
        t.Cash -= ItemCatalog.Get(c).Price;
        t.Casings[(int)c]++;
        return true;
    }

    // ---- Selling ----------------------------------------------------------------------------

    /// <summary>Sell one weapon for a 100% refund. BasicShell (infinite) cannot be sold.</summary>
    public static bool Sell(Tank t, WeaponId w)
    {
        if (w == WeaponId.BasicShell || t.Count(w) <= 0) return false;
        t.Weapons[(int)w]--;
        t.Cash += ItemCatalog.Get(w).Price;
        return true;
    }

    /// <summary>Sell one utility; refused if free space would become negative (e.g. Hull Extension).</summary>
    public static bool Sell(Tank t, UtilityId u)
    {
        if (t.Count(u) <= 0) return false;
        t.Utilities[(int)u]--;
        if (FreeSpace(t) < 0)
        {
            t.Utilities[(int)u]++;
            return false;
        }
        t.Cash += ItemCatalog.Get(u).Price;
        return true;
    }

    /// <summary>Sell one casing; Impact cannot be sold; refused if free space would become negative.</summary>
    public static bool Sell(Tank t, CasingId c)
    {
        if (c == CasingId.Impact || t.Count(c) <= 0) return false;
        t.Casings[(int)c]--;
        if (FreeSpace(t) < 0)
        {
            t.Casings[(int)c]++;
            return false;
        }
        t.Cash += ItemCatalog.Get(c).Price;
        return true;
    }

    // ---- Shop entry -------------------------------------------------------------------------

    /// <summary>
    /// Run on every shop visit: BasicShell and Impact become infinite (255). A launched satellite
    /// (SatelliteLaunch still owned) is converted back into one SatelliteLaser utility.
    /// </summary>
    public static void PrepareForShopping(Tank t)
    {
        t.Weapons[(int)WeaponId.BasicShell] = Tank.Infinite;
        t.Casings[(int)CasingId.Impact] = Tank.Infinite;
        if (t.Weapons[(int)WeaponId.SatelliteLaunch] > 0)
        {
            t.Weapons[(int)WeaponId.SatelliteLaunch] = 0;
            t.Utilities[(int)UtilityId.SatelliteLaser] = 1;
        }
    }

    // ---- Computer shopping script -----------------------------------------------------------

    /// <summary>
    /// One script line: a buy code (weapon id, 100+utility id, or 200+casing id), a target count
    /// (rolled when the line is reached), and an optional gate that may consume randomness.
    /// </summary>
    private readonly record struct Step(Func<TpRandom, int> Code, Func<TpRandom, int> Target,
        Func<MatchState, bool>? Gate = null);

    private static Step W(int id, Func<TpRandom, int> target, Func<MatchState, bool>? gate = null) =>
        new(_ => id, target, gate);

    private static Step U(int id, Func<TpRandom, int> target, Func<MatchState, bool>? gate = null) =>
        new(_ => 100 + id, target, gate);

    private static Func<TpRandom, int> F(int n) => _ => n;
    private static Func<TpRandom, int> Rnd(int n) => r => r.Next(n);
    private static Func<TpRandom, int> Plus(int a, int n) => r => a + r.Next(n);
    /// <summary>Gate "[R(n)=eq]": consumes one Random(n) draw.</summary>
    private static Func<MatchState, bool> RollIs(int n, int eq) => s => s.Rng.Next(n) == eq;
    private static readonly Func<MatchState, bool> HasWater = s => s.World.HasWater;

    /// <summary>List used by every computer (Goldfish and up).</summary>
    private static readonly Step[] BaseScript =
    {
        W(6, F(1)), U(1, Plus(2, 2)), W(5, F(2)), W(4, Rnd(5)), W(3, Rnd(5)), W(6, F(2)), W(8, F(2)),
        U(12, F(1), HasWater),                                  // Floats (water levels only)
        U(1, Plus(4, 4)), W(8, F(3)), W(6, Rnd(5)), W(9, Rnd(2)), W(10, Rnd(2)), W(11, Rnd(3)), U(7, Rnd(2)),
        U(10, F(1)), W(17, Plus(1, 2)), U(1, Plus(9, 6)), W(8, Plus(3, 2)), W(6, Plus(3, 4)), W(10, Plus(1, 3)),
        W(14, Plus(1, 2)), W(15, Rnd(2)), W(17, Plus(1, 4)),
        new(r => 12 + r.Next(2), Rnd(3)),                       // Acid Bomb or Acid Flood
        U(7, Rnd(5)), W(24, Rnd(2)), U(1, Plus(14, 5)), W(8, F(4)), W(19, Rnd(2)),
        U(4, Rnd(2), RollIs(2, 1)),                             // [R(2)=1: U4 → R(2)]
        W(17, Plus(3, 3)), U(7, Plus(3, 3)), U(10, F(3)), W(18, Rnd(2)), W(21, Rnd(2)), W(5, F(5)),
        W(16, Rnd(2), RollIs(4, 1)),                            // [R(4)=1: W16 → R(2)]
    };

    /// <summary>Extra list for Gerbil, Owl, Fox and Dolphin (type ≥ Gerbil).</summary>
    private static readonly Step[] ExtraScript =
    {
        U(1, Plus(20, 5)), W(8, Plus(4, 2)), W(15, F(1)),
        W(16, Rnd(2), RollIs(2, 0)),                            // [R(2)=0: W16 → R(2)]
        U(4, F(1)), U(7, Plus(4, 3)), W(6, F(6)), U(1, F(24)), W(8, F(6)), W(6, F(8)), W(11, F(2)), W(10, F(4)),
        U(1, F(28)), W(25, Rnd(2)), W(8, F(8)), W(17, F(5)), U(1, F(35)), W(15, F(1)), U(1, F(40)),
        U(11, Rnd(2)), W(15, F(2)), W(16, F(1)), U(4, F(1)), U(5, F(1)), U(7, F(6)), W(15, F(3)), W(16, F(2)),
        W(14, F(4)), W(15, F(4)), U(1, F(50)),
    };

    /// <summary>
    /// Fixed computer buying script (GAME-MECHANICS §10). Goldfish runs the base list; Gerbil and above
    /// also run the extra list. Buys one unit at a time; a weapon that does not fit triggers a Hull
    /// Extension purchase first (count &lt; 12 and cash &gt; 1800).
    /// </summary>
    public static void AiShop(MatchState s, Tank t)
    {
        PrepareForShopping(t);
        RunScript(s, t, BaseScript);
        if (t.Type >= PlayerType.Gerbil) RunScript(s, t, ExtraScript);
    }

    private static void RunScript(MatchState s, Tank t, Step[] script)
    {
        foreach (var step in script)
        {
            if (step.Gate is { } gate && !gate(s)) continue;
            int code = step.Code(s.Rng);
            int target = step.Target(s.Rng);
            BuyTo(t, code, target);
        }
    }

    private static void BuyTo(Tank t, int code, int target)
    {
        if (code < 100)
        {
            var w = (WeaponId)code;
            var def = ItemCatalog.Get(w);
            while (t.Count(w) < target)
            {
                if (Buy(t, w)) continue;
                bool affordable = t.Cash >= def.Price;
                if (!affordable || FreeSpace(t) >= def.Space) break;
                if (!TryHullExtension(t)) break;
            }
        }
        else if (code < 200)
        {
            var u = (UtilityId)(code - 100);
            while (t.Count(u) < target && Buy(t, u)) { }
        }
        else
        {
            var c = (CasingId)(code - 200);
            while (t.Count(c) < target && Buy(t, c)) { }
        }
    }

    private static bool TryHullExtension(Tank t) =>
        t.Cash > ItemCatalog.Get(UtilityId.HullExtension).Price
        && t.Count(UtilityId.HullExtension) < MaxHullExtensions
        && Buy(t, UtilityId.HullExtension);
}
