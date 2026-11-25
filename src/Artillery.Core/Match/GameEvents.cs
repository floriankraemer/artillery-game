using Artillery.Core.Items;
using Artillery.Core.Util;

namespace Artillery.Core.Match;

/// <summary>
/// Events emitted by the simulation for the presentation layer to replay in order.
/// The sim resolves a whole shot instantly; the view animates these.
/// </summary>
public abstract record GameEvent;

public sealed record SoundEvent(SoundId Sound) : GameEvent;
public sealed record MessageEvent(string Text) : GameEvent;

/// <summary>Projectile flight path (one point per simulation step).</summary>
public sealed record ProjectileFlightEvent(int Shooter, WeaponId Weapon, CasingId Casing, IReadOnlyList<Vec3d> Path, bool OffMap) : GameEvent;

/// <summary>Visual explosion at a point with radius in cells.</summary>
public sealed record ExplosionEvent(Vec3d Position, int Radius, ExplosionKind Kind) : GameEvent;

/// <summary>Terrain/effect/prop maps changed; view should rebuild from state.</summary>
public sealed record TerrainChangedEvent : GameEvent;

public sealed record TankDamagedEvent(int Tank, int HullDamage, int ShieldDamage, int Hull, int Shield) : GameEvent;
public sealed record TankMovedEvent(int Tank, Vec3d From, Vec3d To, MoveKind Kind) : GameEvent;
public sealed record TankDiedEvent(int Tank, bool OffMap) : GameEvent;
public sealed record ScoreEvent(int Tank, long Delta, string Reason) : GameEvent;

/// <summary>Flamethrower bursts: from the shooter along a bearing for a reach in cells.</summary>
public sealed record FlameEvent(int Shooter, Vec3d From, double BearingRad, double Reach) : GameEvent;

/// <summary>Satellite beam strike.</summary>
public sealed record BeamEvent(Vec3d Target) : GameEvent;

public enum ExplosionKind { Normal, Fire, Acid, Goo, Dirt, Shock, Nuke, Beam }
public enum MoveKind { Fall, Teleport, Knockback, Drift, Settle }

/// <summary>Sound cue ids (placeholder semantic names; the presentation maps them to audio files).</summary>
public enum SoundId
{
    None = 0, GooSplat = 1, LaunchHeavy = 2, AirStrike = 3, LaunchLight = 4, ExplosionNuke = 5, ExplosionMedium = 6,
    Countdown = 7, Shockwave = 8, Victory = 9, ExplosionBig = 11, ExplosionSmall = 12, ShieldBreak = 13, Splash = 14,
    Rumble = 15, AirStrikePlane = 16, Teleport = 21, Fire = 22, Scatter = 23, UiClick = 25, Button = 26, ExplosionHuge = 27,
    Launch = 28, TimeUp = 29, Menu = 30, Credits = 35,
}
