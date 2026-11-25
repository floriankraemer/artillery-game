using Artillery.Core.Util;
using Artillery.Core.World;

namespace Artillery.Core.Match;

/// <summary>Terrain + per-cell maps + active landscape parameters for the current round.</summary>
public sealed class WorldState
{
    public Heightmap Height { get; } = new();
    public CellMap Effects { get; } = new();
    public CellMap Props { get; } = new();
    public LandscapeDef Landscape { get; set; } = null!;
    public double Gravity { get; set; } = Balance.DefaultGravity;
    public double AirResistance { get; set; } = Balance.DefaultAir;
    public bool HasWater => Landscape?.HasWater ?? false;
    public int WaterLevel => Landscape?.WaterLevel ?? 0;
}

public enum MatchPhase { Shopping, Playing, RoundOver, MatchOver }

/// <summary>Complete deterministic match state. All randomness comes from <see cref="Rng"/>.</summary>
public sealed class MatchState
{
    public MatchState(MatchSettings settings)
    {
        Settings = settings;
        Rng = new TpRandom(settings.Seed);
    }

    public MatchSettings Settings { get; }
    public TpRandom Rng { get; }
    public WorldState World { get; } = new();

    /// <summary>Tanks indexed 1..N (index 0 unused, null).</summary>
    public List<Tank> Tanks { get; } = new() { null! };
    public int PlayerCount => Tanks.Count - 1;
    public IEnumerable<Tank> AllTanks => Tanks.Skip(1);
    public IEnumerable<Tank> LivingTanks => AllTanks.Where(t => t.Alive);
    public Tank this[int index] => Tanks[index];

    public MatchPhase Phase { get; set; } = MatchPhase.Shopping;
    public int Round { get; set; }
    /// <summary>Turn-cycle counter within the round (1-based once play starts).</summary>
    public int TurnCounter { get; set; }
    /// <summary>Turn order for the round (player indices).</summary>
    public List<int> Order { get; } = new();
    /// <summary>Position in <see cref="Order"/> of the player whose turn it is.</summary>
    public int OrderPos { get; set; }
    public int CurrentPlayer => Order.Count > 0 ? Order[OrderPos] : 0;
    /// <summary>Usage counter per landscape id (deterministic rotation).</summary>
    public int[] LandscapeUsage { get; } = new int[28];
    /// <summary>Round ended via menu: no interest.</summary>
    public bool RoundAborted { get; set; }
    /// <summary>Winner of last round (0 = none).</summary>
    public int RoundWinner { get; set; }

    /// <summary>Wind blows toward this bearing (0..359, same convention as aiming); strength 0..<see cref="Balance.MaxWind"/>.</summary>
    public int WindBearing { get; set; }
    public int WindStrength { get; set; }
    /// <summary>Turns since the wind was last rolled; -1 = roll at the next turn start.</summary>
    public int WindAge { get; set; } = -1;
    /// <summary>Per-step velocity change from the wind in sim axes (0,0 when the setting is off or calm).</summary>
    public (double X, double Y) WindAccel
    {
        get
        {
            if (!Settings.Wind || WindStrength <= 0) return (0, 0);
            double b = (360.0 - WindBearing) * Math.PI / 180.0, a = WindStrength * Balance.WindAccelPerUnit;
            return (Math.Cos(b) * a, Math.Sin(b) * a);
        }
    }
}
