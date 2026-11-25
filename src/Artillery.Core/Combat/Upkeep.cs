using Artillery.Core.Items;
using Artillery.Core.Match;

namespace Artillery.Core.Combat;

/// <summary>Start-of-turn tank upkeep (GAME-MECHANICS §5 table).</summary>
public static class Upkeep
{
    /// <summary>
    /// For every living tank, in order: shield generators (+2..4 if 0 &lt; shield &lt; 50*gens-10), recharger (+10..19 if shield &lt; 50*gens-20),
    /// auto repair (+5..19 hull if hull &lt; 50*armour+80), armour plate loss (one plate if armour &gt;= 1 and hull &lt; 100+50*armour).
    /// </summary>
    public static void StartOfTurn(MatchState s, List<GameEvent> ev)
    {
        foreach (var t in s.LivingTanks)
        {
            int gens = t.Count(UtilityId.ShieldGenerator);
            int armour = t.Count(UtilityId.HullArmour);
            if (t.Shield > 0 && t.Shield < Balance.ShieldPerGenerator * gens - 10) t.Shield += 2 + s.Rng.Next(3);
            if (t.Has(UtilityId.ShieldRecharger) && t.Shield < Balance.ShieldPerGenerator * gens - 20) t.Shield += 10 + s.Rng.Next(10);
            if (t.Has(UtilityId.AutoRepair) && t.Hull < Balance.HullPerArmour * armour + 80) t.Hull += 5 + s.Rng.Next(15);
            if (armour >= 1 && t.Hull < Balance.BaseHull + Balance.HullPerArmour * armour) t.Utilities[(int)UtilityId.HullArmour]--;
        }
    }
}
