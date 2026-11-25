# AGENTS.md — Artillery3D

Original turn-based 3D artillery game (Godot 4.7 mono + C#). Mechanics follow classic artillery games; this is NOT a copy: use the placeholder names already in the code, never another game's names, texts or assets.

## Specs (authoritative)
- `docs/GAME-MECHANICS.md`: consolidated rules with exact formulas.
- `docs/GODOT-PLAN.md`: architecture and decisions. §5 lists the **bug fixes to apply** (no legacy toggles).
- `docs/MULTIPLAYER.md`: online play (lockstep input relay, lobby, testing). `docs/WIND-AND-SETTINGS.md`: random wind, settings menu, display/UI changes.

## Layout
```
src/Artillery.Core/        pure C# (net8.0), NO Godot references. Deterministic.
  Util/   TpRandom (TP7 RNG port), Pas (Round half-away-from-zero, Trunc), Vec3d
  World/  Heightmap (61x61, index -5..55, playable 1..50), CellMap, LandscapeDef
  Items/  ItemCatalog (all item data, placeholder names)
  Match/  Tank, MatchSettings, MatchState/WorldState, GameEvents
  Balance.cs  tunable constants
tests/Artillery.Core.Tests/  xUnit
game/                        Godot project (presentation only)
```

## Rules
1. Core never references Godot. All randomness through `MatchState.Rng` / a passed `TpRandom` — never `System.Random`, never time.
2. Port formulas exactly from the specs (constants, step order, Pascal `Round` = `Pas.Round`, `Trunc` = `Pas.Trunc`, `Random(n)` = `rng.Next(n)`). Apply the bug fixes from GODOT-PLAN §5 instead of reproducing bugs.
3. Sim resolves instantly and appends `GameEvent`s to a `List<GameEvent>` for the view to replay.
4. Only touch files in your assigned scope. Do not edit shared contracts (`Tank`, `MatchState`, `GameEvents`, `ItemCatalog`, `Heightmap`, `Balance`) — if you truly need a change, make it minimal and additive (new member), and say so in your report.
5. Every module gets xUnit tests (numbers from the spec: e.g. blast damage table, crater depth, flat-ground range, cash formula).
6. Build/test: `dotnet build Artillery3D.slnx` and `dotnet test tests/Artillery.Core.Tests` from the repo root. Must be green before you finish.
7. Keep code compact and readable; XML doc comments on public API citing the spec section.
