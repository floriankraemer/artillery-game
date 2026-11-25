using Artillery.Core.Util;

namespace Artillery.Core.Match;

/// <summary>Round-end money and match setup (docs/GAME-MECHANICS.md §7).</summary>
public static class Economy
{
    /// <summary>
    /// Round end, per tank: total += round; cash += 5·round if round &gt; 0; cash = Round(cash·1.08) unless
    /// the round was aborted; cash ≥ 0; computers get +round (even if negative); round = 0.
    /// </summary>
    public static void EndRound(MatchState s)
    {
        foreach (var t in s.AllTanks)
        {
            t.TotalScore += t.RoundScore;
            if (t.RoundScore > 0) t.Cash += Balance.CashPerScore * t.RoundScore;
            if (!s.RoundAborted) t.Cash = Pas.Round(t.Cash * Balance.Interest);
            t.Cash = Math.Max(t.Cash, 0);
            if (t.IsComputer && Balance.AiCashBonus) t.Cash += t.RoundScore;
            t.RoundScore = 0;
        }
    }

    /// <summary>New match: every tank gets the start cash, zero scores and wins, and empty inventory.</summary>
    public static void StartMatch(MatchState s)
    {
        foreach (var t in s.AllTanks)
        {
            t.Cash = s.Settings.StartCash;
            t.RoundScore = 0;
            t.TotalScore = 0;
            t.Wins = 0;
            Array.Clear(t.Weapons);
            Array.Clear(t.Utilities);
            Array.Clear(t.Casings);
        }
    }
}
