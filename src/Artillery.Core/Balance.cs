namespace Artillery.Core;

/// <summary>Tunable global constants. Values are the original game's (see docs/GAME-MECHANICS.md).</summary>
public static class Balance
{
    public const double DefaultGravity = 0.01;
    public const double DefaultAir = 0.994;
    public const int MaxFlightSteps = 1500;
    /// <summary>Wind: strength 0..MaxWind; each unit adds this much horizontal velocity per flight step.</summary>
    public const double WindAccelPerUnit = 0.0001;
    public const int MaxWind = 10;
    public const double LaunchScale = 1.1;
    public const double ExtraPowerScale = 1.5;
    public const double HorizontalDiv = 2.2;
    public const double VerticalMul = 2.2;

    public const int BlastDamagePerCell = 15;
    public const int SelfDamagePenalty = 2;
    public const int DeathBonusToSurvivors = 200;
    public const int WinnerBonus = 200;
    public const int TeleportOffMapPenalty = 200;
    public const int DeathBlastRadius = 4;
    public const int DeathNukeRadius = 25;

    public const int BaseHull = 100;
    public const int HullPerArmour = 50;
    public const int ShieldPerGenerator = 50;
    public const int BaseCapacity = 100;
    public const int CasingSpace = 2;
    public const int MaxStack = 99;

    public const int CashPerScore = 5;
    public const double Interest = 1.08;
    public const bool AiCashBonus = true;

    public const int MinFallForDamage = 6;
    public const int FallDamagePerUnit = 2;
    public const int ParachuteThreshold = 5;
}
