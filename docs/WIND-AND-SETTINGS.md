# Wind, settings menu and UI changes

## Random wind

An optional random wind pushes projectiles sideways. It is a new rule layered on top of the mechanics in `GAME-MECHANICS.md` section 4; it does not exist in the original game.

### Match options

| Option | Where | Default |
|---|---|---|
| Random Wind (on/off) | Setup screen, `MatchSettings.Wind` | on in the setup screen (`SetupScreen.DefaultSettings`), off in the core default |
| Wind Changes (every 1..10 shots) | Setup screen, `MatchSettings.WindChangeEvery` | 3 |

The slider is disabled while wind is off. Both options are saved with the setup's Save button, shown in the online lobby and sent to all players (see `MULTIPLAYER.md`).

### Rules

- **State** (`MatchState`): `WindBearing` (0..359, the direction the wind blows toward, same scale as the aiming bearing), `WindStrength` (0..`Balance.MaxWind`, currently 10) and `WindAge` (turns since the last roll, -1 = roll at the next turn start).
- **Rolling** (`MatchEngine.UpdateWind`, called from `BeginTurn`): on the first turn of every round and then whenever `WindAge` reaches `WindChangeEvery`. Bearing is `Rng.Next(360)`. Strength is the lower of two `Rng.Next(MaxWind + 1)` draws, so calm and light wind is common and strong wind is rare. Everything uses the match RNG, so the result is deterministic and identical on every online peer. A message event ("Wind changed: strength 4, blowing toward 135") is emitted on each roll.
- **Reset:** `StartRound` sets `WindAge = -1` and `WindStrength = 0`.
- **Effect** (`Ballistics.Fly`): each flight step, before air resistance, the horizontal velocity changes by `MatchState.WindAccel` = `WindStrength * Balance.WindAccelPerUnit` (currently 0.0001) along the wind bearing. It applies to free-flying shells: plain, bouncing, rolling and heat-seeking casings, only while the projectile is above the ground. It does **not** apply to cruise and smart-cruise casings, satellite launches, flamethrower flames, or tunnelling underground.
- **AI and aim preview** call `Ballistics.Fly`, so computer players and the yellow aim line compensate for wind automatically.

### Display

The top line of the battle HUD shows `Wind: 6 toward 135°` or `Wind: calm` (nothing when wind is off). A toast announces each change. "Toward 135°" uses the same degrees as the bearing slider, so firing at the same bearing means a tailwind.

### Tuning

Constants in `src/Artillery.Core/Balance.cs`:

- `WindAccelPerUnit`: force per strength point. Halving it halves the drift.
- `MaxWind`: highest strength. Lowering it also lowers the average, since strength is drawn from 0..MaxWind.

The strength roll is in `MatchEngine.UpdateWind`.

Tests: `tests/Artillery.Core.Tests/WindTests.cs` (wind off or calm changes nothing, tailwind and headwind change range, crosswind drifts sideways, re-roll interval, determinism per seed).

## Air resistance default

The setup screen starts with air resistance on (`SetupScreen.DefaultSettings`, also used by Reset). The core default `MatchSettings.AirResistance` is still false so existing tests are unchanged. A value saved earlier with the Save button is kept.

## Settings menu

`game/scripts/Ui/SettingsScreen.cs`, opened by the title screen's **Settings** button. It saves immediately to `user://settings.cfg`, section `ui` (`UiSettings`):

| Setting | Key | Default |
|---|---|---|
| Camera follows the projectile | `camera_follow` | **off** |
| Animation speed (0.25x to 4x) | `animation_speed` | 1x |
| Master volume | `master_volume` | 100% |

The follow-camera key was renamed from `follow_projectile` so the new default (off) also applies to existing installs. The same options are available in the in-game pause menu. Esc closes the settings screen.

## Landscape preview

On the setup screen, hovering a landscape button shows its 3D preview (`SetupScreen.ShowPreviewFor`). The old Preview button was removed. The preview regenerates with the current seed, and drag-to-orbit and mouse wheel zoom work as before.

## Display and layout

- **Fullscreen:** the game starts in borderless fullscreen (`window/size/mode=3` in `game/project.godot`). F11 toggles a normal window (`Main._UnhandledKeyInput`).
- **Scaling:** `window/stretch/mode="canvas_items"` with `aspect="expand"`, so the 2D UI scales to any resolution and fills the window. The design size is 1600x900.
- **Screens built in code** share the framed-card look of the title screen: `UiKit.Root` (background, anchors) and the amber-bordered panel used by the multiplayer and settings screens.
- **Setup screen overflow:** the status line under the player list now wraps. Before, a long message widened the left column and pushed the landscape list and preview off the right edge of the screen.
