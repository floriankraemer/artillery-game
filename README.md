# Artillery3D

A turn-based 3D artillery game for 2 to 4 commanders (humans or CPU opponents), built with Godot 4.7 (C#). It is an original game
inspired by the mechanics of classic artillery games; names, texts and assets
are our own. Docs: [`docs/GAME-MECHANICS.md`](docs/GAME-MECHANICS.md) (rules and formulas), [`docs/GODOT-PLAN.md`](docs/GODOT-PLAN.md)
(architecture), [`docs/MULTIPLAYER.md`](docs/MULTIPLAYER.md) (online play), [`docs/WIND-AND-SETTINGS.md`](docs/WIND-AND-SETTINGS.md)
(random wind, settings), [`AGENTS.md`](AGENTS.md) (contributor rules).

## Requirements
- Godot 4.7 **mono** (.NET build) and the .NET 8 SDK.
- Windows paths are assumed by the helper scripts: Godot is expected in `M:\Godot_v4.7-stable_mono_win64`
  (override with a `tools/godot.path` file containing the folder or exe path).

## Run, build, test
```
dotnet build Artillery3D.slnx                  # core library + tests
dotnet test tests/Artillery.Core.Tests         # rules test suite (xUnit)
godot --path game                              # play (or tools/run-game.ps1 -Play; without -Play opens the editor)
tools/smoke.ps1                                # headless smoke test, fails on ERROR / SCRIPT ERROR
tools/sandbox.ps1 --lid=16 --view=iso          # standalone terrain / world viewer
tools/landscapes_sheet.ps1                     # renders all 27 landscapes into artifacts/screens/landscapes_sheet.png
```
Game flags (after `--`):
- `--autoplay`: headless-friendly CPU-only match at 8x speed, exits when done.
- `--humantest`: scripted human path (title, setup, shop buy/sell, aim, fire, pause, end game, results, quit) driven through the real UI; logs `[HUMANTEST]` steps.
- `--shot=ABS.png [--after=N] [--view=iso|side|plan] [--seed=N] [--info]`: save a battle screenshot and quit.

## Controls (battle)
| Key | Action |
|---|---|
| W / S | power up / down |
| A / D | bearing left / right |
| R / F | elevation up / down |
| Space | fire |
| Q / E | rotate camera |
| V | cycle view (side, iso, plan) |
| Esc | pause menu (resume, self-destruct, end round / game, quit) |

The HUD also has sliders, +/- buttons, weapon and casing selectors, and a replay button. Air strikes and satellite aim ask for a click on a target cell.

## Layout
```
src/Artillery.Core/     pure C# rules engine (deterministic, no Godot): terrain, ballistics, items, AI, match flow
tests/Artillery.Core.Tests/   xUnit tests with numbers from the specs
game/                   Godot project: scenes/, scripts/ (Main, Ui, Battle, World, Camera, Audio), theme/, audio/
docs/                   specs
tools/                  PowerShell helpers
```

## Status and known gaps
- Complete loop works: title, setup with landscape preview, shop, battle with 27 landscapes, all weapons/casings/utilities, 6 AI levels, round and final results.
- Rendering is deliberately simple: flat-shaded banded low-poly terrain, primitive tank and effect meshes, placeholder audio.
- Online play for 2 to 4 players works over a direct ENet connection (one host, others join by IP). It is a lockstep input relay, so every peer runs the same deterministic simulation. See `docs/MULTIPLAYER.md`.
- Optional random wind is a new rule that the original game does not have.
- Saved settings live in the Godot user directory; no gamepad support, no localisation.
- On exit Godot reports a couple of leaked objects after `--humantest` (cosmetic).

## License
Artillery3D is free software under the [GNU General Public License v3.0](LICENSE) (GPL-3.0-or-later). You may use, modify and redistribute it under those terms.

The license covers the code and assets in this project.
