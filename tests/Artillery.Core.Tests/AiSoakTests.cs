using Artillery.Core.Ai;
using Artillery.Core.Match;
using Xunit;

namespace Artillery.Core.Tests;

/// <summary>Full computer-vs-computer matches with the real AI on every landscape: must finish without exceptions.</summary>
public class AiSoakTests
{
    [Theory]
    [InlineData(1u), InlineData(7u), InlineData(42u)]
    public void FourComputersPlayAllLandscapes(uint seed)
    {
        var settings = new MatchSettings
        {
            Seed = seed, Rounds = 27, MaxShots = 10, StartCash = 10000,
            Players = new()
            {
                new("A", PlayerType.Goldfish), new("B", PlayerType.Owl),
                new("C", PlayerType.Fox), new("D", PlayerType.Dolphin),
            },
            Landscapes = Enumerable.Range(1, 27).ToHashSet(),
        };
        var engine = new MatchEngine(settings) { AiDecide = AiPlayer.ChooseShot };
        engine.StartMatch();
        int guard = 0;
        while (engine.State.Phase != MatchPhase.MatchOver && guard++ < 20000)
        {
            switch (engine.State.Phase)
            {
                case MatchPhase.Shopping: engine.StartRound(); break;
                case MatchPhase.Playing: engine.BeginTurn(); if (engine.State.Phase == MatchPhase.Playing) engine.PlayComputerTurn(); break;
                case MatchPhase.RoundOver: engine.BeginRoundSetup(); break;
            }
        }
        Assert.Equal(MatchPhase.MatchOver, engine.State.Phase);
        Assert.Equal(4, engine.FinalRanking().Count());
    }
}
