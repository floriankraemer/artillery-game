using Artillery.Core.Items;
using Artillery.Core.Util;

namespace Artillery.Core.Match;

public enum PlayerType { Human = 1, Goldfish = 2, Gerbil = 3, Owl = 4, Fox = 5, Dolphin = 6 }

/// <summary>Player + tank state. Index is 1-based (1..4) like the original.</summary>
public sealed class Tank
{
    public const int Infinite = 255;
    public const int HullDead = -1;        // just died; death handler will explode it
    public const int HullOffMap = -100;    // blasted/teleported off the map (no death blast, no survivor bonus)
    public const int HullGone = -10000;    // death processed

    public required int Index { get; init; }
    public required string Name { get; set; }
    public PlayerType Type { get; set; } = PlayerType.Human;
    public bool IsComputer => Type != PlayerType.Human;

    // Position: X/Y are integer-valued grid cells 5..44 (tank occupies cell X..X+1), Z = ground + 0.5.
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public Vec3d Position => new(X, Y, Z);

    public int Hull { get; set; }
    public int Shield { get; set; }
    public int MaxHull { get; set; } = Balance.BaseHull;
    public bool Alive => Hull > 0;
    public int DeathRadius { get; set; } = Balance.DeathBlastRadius;

    public long Cash { get; set; }
    public long RoundScore { get; set; }
    public long TotalScore { get; set; }
    public int Wins { get; set; }

    /// <summary>Counts indexed by enum value; 255 = infinite.</summary>
    public int[] Weapons { get; } = new int[50];
    public int[] Utilities { get; } = new int[14];
    public int[] Casings { get; } = new int[8];

    public WeaponId SelectedWeapon { get; set; } = WeaponId.BasicShell;
    public CasingId SelectedCasing { get; set; } = CasingId.Impact;

    /// <summary>Stored aim (kept between turns).</summary>
    public double Power { get; set; } = 50;
    public double Bearing { get; set; }
    public double Elevation { get; set; } = 45;

    /// <summary>Satellite strike scheduling: target cell and turn on which it fires (-1 = none).</summary>
    public int SatelliteTargetX { get; set; } = -1;
    public int SatelliteTargetY { get; set; } = -1;
    public int SatelliteFireTurn { get; set; } = -1;
    public bool TeleportedThisRound { get; set; }

    /// <summary>AI memory (exact last solution and shots at current target).</summary>
    public AiMemory Ai { get; } = new();

    public int Count(WeaponId w) => Weapons[(int)w];
    public int Count(UtilityId u) => Utilities[(int)u];
    public int Count(CasingId c) => Casings[(int)c];
    public bool Has(UtilityId u) => Utilities[(int)u] > 0;
    public bool Has(WeaponId w) => Weapons[(int)w] > 0;

    public int MinElevation => Has(UtilityId.BetterTurret) ? -20 : 0;
    public int MaxElevation => Has(UtilityId.BetterTurret) ? 110 : 80;

    /// <summary>Applies damage: shield first, overflow to hull. Returns damage actually absorbed (capped at hull+shield).</summary>
    public int ApplyDamage(int dmg)
    {
        if (!Alive || dmg <= 0) return 0;
        int credited = Math.Min(dmg, Hull + Shield);
        Shield -= dmg;
        if (Shield < 0) { Hull += Shield; Shield = 0; }
        if (Hull <= 0) Hull = HullDead;
        return credited;
    }

    /// <summary>Drains shield only (shield breaker). Returns amount drained.</summary>
    public int DrainShield(int amount)
    {
        int d = Math.Min(amount, Math.Max(Shield, 0));
        Shield -= d;
        return d;
    }
}

public sealed class AiMemory
{
    public int Target { get; set; }
    public int ShotsAtTarget { get; set; }
    public int LastTarget { get; set; }
    public double Power { get; set; }
    public double Bearing { get; set; }
    public double Elevation { get; set; }
}
