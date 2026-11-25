namespace Artillery.Core.Match;

public enum PlayOrder { Sequential = 0, Random = 1 }

public sealed record PlayerSetup(string Name, PlayerType Type);

public sealed class MatchSettings
{
    public List<PlayerSetup> Players { get; set; } = new()
    {
        new("Player 1", PlayerType.Human),
        new("Player 2", PlayerType.Human),
    };
    public int Rounds { get; set; } = 3;          // 1..50
    public int MaxShots { get; set; } = 25;       // 5..50 turn cycles per round
    public int StartCash { get; set; } = 2000;    // 0..20000 step 1000
    public PlayOrder Order { get; set; } = PlayOrder.Random;
    public bool AirResistance { get; set; }
    /// <summary>Random wind pushes projectiles sideways; re-rolled every <see cref="WindChangeEvery"/> shots.</summary>
    public bool Wind { get; set; }
    /// <summary>1..10: how many shots (turns) the wind stays the same before a new random one is rolled.</summary>
    public int WindChangeEvery { get; set; } = 3;
    /// <summary>Enabled landscape ids (1..27). At least one.</summary>
    public HashSet<int> Landscapes { get; set; } = new() { 13 };
    public uint Seed { get; set; } = 12345;
}
