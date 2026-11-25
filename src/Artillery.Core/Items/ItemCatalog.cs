namespace Artillery.Core.Items;

public enum WeaponId
{
    None = 0,
    BasicShell = 1, Tracer = 2, HeShell = 3, Missile = 4, HeavyMissile = 5, BigBomb = 6, ScatterShot = 7,
    ShieldBreaker = 8, Napalm = 9, SuperNapalm = 10, GooBomb = 11, AcidBomb = 12, AcidFlood = 13,
    Nuke = 14, BigNuke = 15, MegaNuke = 16, Teleporter = 17, Flamethrower = 18, Shockwave = 19,
    AirStrike = 20, Extinguisher = 21, HillMaker = 22, WallMaker = 23, Earthquake = 24, Volcano = 25,
    /// <summary>Hidden: aim the orbiting satellite (granted by a successful launch).</summary>
    SatelliteAim = 48,
    /// <summary>Hidden: launch the satellite (granted by buying SatelliteLaser).</summary>
    SatelliteLaunch = 49,
}

public enum UtilityId
{
    None = 0,
    ShieldGenerator = 1, GravityShield = 2, HullExtension = 3, ShieldRecharger = 4, AutoRepair = 5,
    BetterTurret = 6, HullArmour = 7, PowerBooster = 8, AimComputer = 9, Parachute = 10,
    DeadMansBomb = 11, Floats = 12, SatelliteLaser = 13,
}

public enum CasingId
{
    Impact = 1, Bouncing = 2, Rolling = 3, HeatSeeking = 4, Tunneling = 5, Cruise = 6, SmartCruise = 7,
}

/// <summary>Special effect handler for a weapon (original effect code at DS:0x5373).</summary>
public enum WeaponEffect
{
    Explosive = 0, ShieldBreaker = 1, Napalm = 2, Goo = 3, Teleport = 4, Scatter = 5, SuperNapalm = 6,
    AcidBomb = 7, AcidFlood = 8, Flamethrower = 9, Earthquake = 10, Extinguisher = 11,
    SatelliteLaunch = 13, SatelliteAim = 14, WallMaker = 15, AirStrike = 16, HillMaker = 17, Shockwave = 18, Volcano = 19,
}

/// <param name="Radius">Blast/effect radius in grid cells.</param>
/// <param name="HitBonus">Points per enemy hit by the standard blast (only paid when <paramref name="StandardBlast"/>).</param>
/// <param name="StandardBlast">Apply the standard blast damage after the effect.</param>
/// <param name="Projectile">Fired as a projectile (casings allowed); false = no flight, casing forced to Impact.</param>
public sealed record WeaponDef(WeaponId Id, string Name, int Price, int Space, WeaponEffect Effect, int Radius,
    int HitBonus, bool StandardBlast, bool Projectile, int FireSound, int ImpactSound, bool Hidden = false);

public sealed record UtilityDef(UtilityId Id, string Name, int Price, int Space, int MaxCount, string Description);

public sealed record CasingDef(CasingId Id, string Name, int Price);

/// <summary>Item data from the original game (docs/GAME-MECHANICS.md §6), with placeholder names.</summary>
public static class ItemCatalog
{
    public static readonly IReadOnlyList<WeaponDef> Weapons = new WeaponDef[]
    {
        new(WeaponId.BasicShell,   "Basic Shell",    0,    0, WeaponEffect.Explosive,     2,  5,  true,  true,  4,  0),
        new(WeaponId.Tracer,       "Tracer",         1,    0, WeaponEffect.Explosive,     0,  0,  false, true,  4,  0),
        new(WeaponId.HeShell,      "HE Shell",       10,   1, WeaponEffect.Explosive,     4,  10, true,  true,  4,  12),
        new(WeaponId.Missile,      "Missile",        50,   2, WeaponEffect.Explosive,     5,  10, true,  true,  4,  6),
        new(WeaponId.HeavyMissile, "Heavy Missile",  100,  4, WeaponEffect.Explosive,     6,  10, true,  true,  28, 6),
        new(WeaponId.BigBomb,      "Big Bomb",       250,  6, WeaponEffect.Explosive,     7,  10, true,  true,  28, 11),
        new(WeaponId.ScatterShot,  "Scatter Shot",   100,  5, WeaponEffect.Scatter,       0,  10, false, true,  28, 23),
        new(WeaponId.ShieldBreaker,"Shield Breaker", 350,  8, WeaponEffect.ShieldBreaker, 0,  0,  false, true,  28, 0),
        new(WeaponId.Napalm,       "Napalm",         300,  8, WeaponEffect.Napalm,        0,  40, false, true,  28, 22),
        new(WeaponId.SuperNapalm,  "Super Napalm",   400,  14,WeaponEffect.SuperNapalm,   0,  30, false, true,  28, 22),
        new(WeaponId.GooBomb,      "Goo Bomb",       420,  10,WeaponEffect.Goo,           0,  300,false, true,  4,  0),
        new(WeaponId.AcidBomb,     "Acid Bomb",      250,  10,WeaponEffect.AcidBomb,      0,  30, false, true,  28, 22),
        new(WeaponId.AcidFlood,    "Acid Flood",     400,  15,WeaponEffect.AcidFlood,     0,  30, false, true,  28, 22),
        new(WeaponId.Nuke,         "Nuke",           1000, 25,WeaponEffect.Explosive,     14, 0,  true,  true,  28, 5),
        new(WeaponId.BigNuke,      "Big Nuke",       1500, 40,WeaponEffect.Explosive,     22, 0,  true,  true,  2,  5),
        new(WeaponId.MegaNuke,     "Mega Nuke",      2100, 55,WeaponEffect.Explosive,     37, 0,  true,  true,  2,  27),
        new(WeaponId.Teleporter,   "Teleporter",     600,  9, WeaponEffect.Teleport,      35, 0,  false, true,  28, 0),
        new(WeaponId.Flamethrower, "Flamethrower",   500,  8, WeaponEffect.Flamethrower,  5,  80, false, false, 20, 0),
        new(WeaponId.Shockwave,    "Shockwave",      1800, 10,WeaponEffect.Shockwave,     22, 10, false, true,  28, 0),
        new(WeaponId.AirStrike,    "Air Strike",     1200, 4, WeaponEffect.AirStrike,     0,  0,  false, false, 0,  0),
        new(WeaponId.Extinguisher, "Extinguisher",   400,  5, WeaponEffect.Extinguisher,  7,  0,  false, true,  28, 0),
        new(WeaponId.HillMaker,    "Hill Maker",     800,  7, WeaponEffect.HillMaker,     8,  0,  true,  true,  28, 5),
        new(WeaponId.WallMaker,    "Wall Maker",     500,  8, WeaponEffect.WallMaker,     8,  140,false, true,  28, 15),
        new(WeaponId.Earthquake,   "Earthquake",     800,  15,WeaponEffect.Earthquake,    8,  10, true,  true,  28, 0),
        new(WeaponId.Volcano,      "Volcano",        2500, 60,WeaponEffect.Volcano,       8,  1250,false,true,  28, 15),
        new(WeaponId.SatelliteAim, "Aim Satellite",  0,    0, WeaponEffect.SatelliteAim,  0,  0,  false, false, 0,  0, Hidden: true),
        new(WeaponId.SatelliteLaunch,"Launch Satellite",0, 0, WeaponEffect.SatelliteLaunch,0, 0,  false, true,  2,  0, Hidden: true),
    };

    public static readonly IReadOnlyList<UtilityDef> Utilities = new UtilityDef[]
    {
        new(UtilityId.ShieldGenerator, "Shield Generator", 250,  5,   50, "+50 shield each; slow recharge while the shield is up."),
        new(UtilityId.GravityShield,   "Gravity Shield",   1500, 25,  1,  "Slows enemy shells near you; invisible to heat seekers."),
        new(UtilityId.HullExtension,   "Hull Extension",   1800, -60, 12, "+60 cargo space."),
        new(UtilityId.ShieldRecharger, "Shield Recharger", 1500, 25,  1,  "+10..19 shield every turn."),
        new(UtilityId.AutoRepair,      "Auto Repair",      1500, 30,  1,  "+5..19 hull every turn."),
        new(UtilityId.BetterTurret,    "Better Turret",    1200, 8,   1,  "Elevation range -20..110."),
        new(UtilityId.HullArmour,      "Hull Armour",      250,  4,   6,  "+50 hull per plate. Plates are lost while damaged."),
        new(UtilityId.PowerBooster,    "Power Booster",    800,  10,  1,  "Launch speed x1.5."),
        new(UtilityId.AimComputer,     "Aim Computer",     1500, 18,  1,  "Shows a predicted trajectory preview."),
        new(UtilityId.Parachute,       "Parachute",        100,  2,   25, "Cancels one fall of more than 5."),
        new(UtilityId.DeadMansBomb,    "Dead Man's Bomb",  1000, 20,  1,  "Huge explosion (radius 25) when your tank dies."),
        new(UtilityId.Floats,          "Floats",           500,  6,   5,  "Your tank floats on water instead of sinking."),
        new(UtilityId.SatelliteLaser,  "Satellite Laser",  3000, 45,  1,  "Launch a satellite, aim it, it strikes next turn."),
    };

    public static readonly IReadOnlyList<CasingDef> Casings = new CasingDef[]
    {
        new(CasingId.Impact, "Impact", 0), new(CasingId.Bouncing, "Bouncing", 100), new(CasingId.Rolling, "Rolling", 250),
        new(CasingId.HeatSeeking, "Heat Seeking", 350), new(CasingId.Tunneling, "Tunneling", 450),
        new(CasingId.Cruise, "Cruise", 500), new(CasingId.SmartCruise, "Smart Cruise", 600),
    };

    private static readonly Dictionary<WeaponId, WeaponDef> WeaponById = Weapons.ToDictionary(w => w.Id);
    private static readonly Dictionary<UtilityId, UtilityDef> UtilityById = Utilities.ToDictionary(u => u.Id);
    private static readonly Dictionary<CasingId, CasingDef> CasingById = Casings.ToDictionary(c => c.Id);

    public static WeaponDef Get(WeaponId id) => WeaponById[id];
    public static UtilityDef Get(UtilityId id) => UtilityById[id];
    public static CasingDef Get(CasingId id) => CasingById[id];
    public static IEnumerable<WeaponDef> ShopWeapons => Weapons.Where(w => !w.Hidden);
}
