using Artillery.Core.Items;

namespace Artillery.Core.Match;

/// <summary>
/// One player's decision for a turn. Power 0..100, Bearing 0..359 (0 = sim +X / north), Elevation within the tank's range.
/// Target = grid cell for click-targeted weapons (Air Strike, Satellite aim); null otherwise.
/// </summary>
public sealed record ShotInput(WeaponId Weapon, CasingId Casing, double Power, double Bearing, double Elevation,
    (int X, int Y)? Target = null);
