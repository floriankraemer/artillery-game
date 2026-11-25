# Plan: an original 3D artillery game in Godot

The reference spec is [GAME-MECHANICS.md](GAME-MECHANICS.md).

## 0. Scope and ground rules

- **This is a new, original game in the turn-based 3D artillery genre.** It reuses the *mechanics* documented in GAME-MECHANICS.md (ballistics, casings, blast/crater math, upgrades, economy, landscape generator ideas, AI approach).
- **It does not copy any existing game's identity:**
  - It gets its own title.
  - It has its own art, UI layout, sounds and music.
  - Its item and landscape names are its own, or generic genre terms like "Missile" or "Napalm".
  - It does not reuse the original's name pools, cheat words, credits logo or texts.
- **Fidelity target:** gameplay numbers match the original by default, so the game "feels right". Every constant sits in data files so it can be retuned. The original bugs (GAME-MECHANICS §11) are fixed; only the corrected behaviour is implemented (§5).

## 1. Tech choices

| Topic | Decision |
|---|---|
| Engine | Godot 4.7 mono, Forward+. **Language changed to C# (.NET 8)** on 2026-11-22 to match the user's Wargame-Godot project: pure-C# core in `src/Artillery.Core` (no Godot refs) + xUnit tests, Godot node scripts in `game/`. The GDScript file names below are kept as module descriptions; the real layout is in `AGENTS.md` |
| Simulation | Plain GDScript classes (`RefCounted`) with no scene dependencies. Deterministic: one seeded RNG, fixed tick, `float` (64-bit). The original's 6-byte Real had a 40-bit mantissa, so results are close but not bit-identical |
| RNG | Port of Turbo Pascal `Random`: `seed = seed*0x08088405+1 (mod 2³²)`, `Random(n) = (seed*n) >> 32`. Useful for golden tests and replays |
| Terrain | `Heightmap` = `PackedInt32Array` 61×61 (index −5..55). Render with a `MeshInstance3D` + `ArrayMesh` rebuilt after each change. 50×50 grid = 2,500 quads, so rebuilds are cheap |
| Look | Low-poly flat-shaded terrain with per-vertex height-band colours from a palette resource. Brings the "PyroVision" banded look into real 3D with its own style |
| Data | `Resource` classes (`WeaponDef`, `CasingDef`, `UtilityDef`, `LandscapeDef`, `AiProfile`) stored as `.tres`. Numbers come from GAME-MECHANICS §6/§8/§10 |
| Save/config | `ConfigFile` (`user://settings.cfg`). No checksum tricks |
| Tests | GUT (Godot Unit Test) addon, plus headless runs (`godot --headless -s`) in CI |

## 2. Architecture

```
res://
  sim/                    # pure logic, no Nodes, fully unit-testable
    tp_random.gd          # TP7 RNG port
    heightmap.gd          # storage, bilinear height(), smoothing (5-pt, symmetric 8-pt)
    terrain_gen.gd        # Gen(mode, base, spikeMax, spikes, smooth) + 13 mode blocks
    effect_map.gd         # acid/fire/gooey/lava cells, per-turn tick
    ballistics.gd         # launch vector, flight loop, casings -> FlightResult (path, impact)
    blast.gd              # standard blast damage, crater, special-weapon handlers
    tank_state.gd         # hull, shield, inventory, upgrades, position
    match_state.gd        # players, round, turn order, scores, cash
    rules.gd              # turn upkeep, deaths, falls, drowning, round end, economy
    ai_player.gd          # Monte-Carlo search + skill noise + weapon/shop scripts
  data/
    weapons/*.tres  casings/*.tres  utilities/*.tres  landscapes/*.tres  ai/*.tres
    balance.tres          # gravity/air defaults, blast 15/cell, cash x5, interest 1.08 ...
  game/                   # presentation (Nodes)
    world/terrain_view.gd     # ArrayMesh from Heightmap, water plane, effect overlays
    world/props.gd            # trees/rocks via MultiMeshInstance3D
    world/tank_view.tscn      # tank model, turret aims at bearing/elevation, shield bubble
    world/projectile_view.gd  # replays FlightResult path: trail, sounds, camera follow
    world/fx/                 # explosions, craters dust, fire, acid, lava (GPUParticles3D)
    camera/camera_rig.gd      # Side/Iso/Plan presets, orbit, follow-shot, top-down picking
    ui/  main_menu, options, player_setup, shop, hud(aim panel, weapon/casing pickers), results
  main.gd                 # state machine: Menu -> Options -> Shop -> Round -> Results
```

**Key rule:** the sim computes a shot completely first (`FlightResult` plus a list of events: impact, damage, deaths, terrain changes). The view then *replays* those events with animation and delay. The AI uses the same `ballistics.gd`, just without a view, exactly like the original's silent mode.

**Coordinates:**
- Grid cell = 1 world unit on X/Z.
- Height unit = configurable `height_scale` on Y (start at ~0.25 so +100 peaks look mountainous).
- Map the original's X(row)/Y(col)/z onto Godot X/Z/Y.
- Bearing 0 = +X ("North"), as in the original.

## 3. Milestones

Each milestone ends in a playable or testable build. Estimates assume one developer.

| # | Milestone | Content | Done when |
|---|---|---|---|
| M0 | Skeleton (1–2 d) | Godot project, folder layout, GUT, CI headless test run, `tp_random.gd` | RNG test vectors pass (seed 0 sequence matches the known Turbo Pascal vectors) |
| M1 | Terrain (3–5 d) | Heightmap, bilinear height, generator core + modes 0 (noise/spikes/smooth), pyramids, sine, craters. Terrain mesh with height-band colours, water plane. Camera rig (3 presets + orbit) | A few landscapes look right and regenerate per seed. Unit tests for `height()` and smoothing |
| M2 | One shot (4–6 d) | Tank placement (distance rules, 4×4 pad), aim UI (power/bearing/elevation, keyboard + mouse), launch vector, flight loop (gravity, optional air), Impact casing, trail, crater + 5 smoothing passes, standard blast damage, falling + fall damage | Hot-seat 2 humans can shoot each other on flat and hilly maps. Range test: P=1, E=45°, g=0.01 lands ≈121 cells (no air) |
| M3 | Match loop (3–4 d) | Turn order (sequential/random), rounds, Max Shots, deaths/death blast/chain reactions, +200 per death to survivors, winner bonus, results screens, ESC menu (self-destruct, end round, end game) | Full multi-round 2–4 player match with final ranking by total score |
| M4 | Economy + shop (4–5 d) | Item resources, space/capacity, buy/sell (100% refund), grey/maxed states, cash ×5 + interest ×1.08, the per-turn upkeep utilities (shield gens, recharger, auto repair, armour loss), Parachute, Water Wings + drowning, Extra Power, Better Turret | The shop matches the item table. Economy unit tests reproduce the round-end formula |
| M5 | Casings (4–6 d) | Bouncing, Rolling, Heat Guided, Tunneling, Cruise, Smart Cruise with the exact constants (01 §Q4). Grav Shield damping | Each casing has a scripted test scenario plus a visual sandbox scene |
| M6 | Special weapons (7–10 d) | Effect map + per-turn tick (acid, fire, gooey, lava), Napalm/Super, Acid Bomb/Flood, Gooey, Flame-B-Gone, Ioniser, Grape Shot, Flame-thrower, Shock Wave, Teleport, Earthquake, Hill/Wall Builder, Volcano, Air Strike (plan-view target pick), Satellite (launch → orbit → aim → strike), Death Nuke | Every weapon has a sandbox test and a sim unit test of its numbers |
| M7 | AI (4–6 d) | Monte-Carlo aim with tolerance growth, memory of the last exact shot, skill-noise tables, weapon-choice rules, teleport escapes, shop scripts, AI cash bonus | Dolphin hits within ±1 cell in most tests; Goldfish misses widely at first and converges. AI-vs-AI headless soak test: 100 matches, no hangs |
| M8 | All landscapes (5–7 d) | The remaining generator modes: canyon/river variants, big pyramid, black hole (+ drift, air 1.01), island, volcano, Mandelbrot, city blocks, jungle river, terraces, morphing landscape. Props (trees, bushes) and per-landscape gravity/air/water/palette. Rename the set and give it your own themes | Each `LandscapeDef` has a preview thumbnail in the options screen |
| M9 | Presentation (open) | Your own SFX/music, explosion and fire VFX, camera follow shot, tank models, UI skin, settings (detail → mesh triangles/props/particles; delay → playback speed), save/load settings | Polish pass |
| M10 | Balance + extras | Tuning pass using the `balance.tres` knobs. Optional: wind (not in the original), online or AI-only spectator mode, replays (deterministic seed + inputs) | — |

## 4. Implementation notes per system

### Ballistics (`ballistics.gd`)

```gdscript
func launch(tank: TankState, power: float, bearing_deg: float, elev_deg: float) -> Vector3:
    var p := power / 100.0
    var b := deg_to_rad(360.0 - bearing_deg)
    var e := deg_to_rad(elev_deg)
    var v := Vector3(cos(b) * p * cos(e) / 2.2, sin(b) * p * cos(e) / 2.2, sin(e) * p * 2.2)  # x,y = grid, z = up
    v *= 1.1
    if tank.has(&"extra_power"): v *= 1.5
    return v

func fly(pos: Vector3, v: Vector3, ctx: FlightContext) -> FlightResult:
    for step in 1501:
        var z_prev := pos.z
        pos += v
        _casing_steer(ctx, pos, v, z_prev)          # smart cruise / heat / cruise
        if ctx.air_on: v *= (ctx.air_a + 1.0) / 2.0
        v.z -= ctx.gravity
        var hit := ctx.hm.height(pos.x, pos.y) + 0.1 >= pos.z
        hit = _casing_contact(ctx, pos, v, hit)      # bounce / roll / tunnel
        ctx.record(pos)
        if hit: return FlightResult.impact(pos, ctx.path)
        if not _in_bounds(roundi(pos.x), roundi(pos.y)): return FlightResult.off_map(ctx.path)
    return FlightResult.timeout(pos, ctx.path)
```

- The sim stays in grid space (x, y horizontal, z up). The view converts to Godot's Y-up.
- **Playback speed:** replay the path at N steps per frame (the "Delay" option becomes a speed slider). Physics does not depend on the frame rate.

### Terrain changes

- Crater: `blast.gd` uses the profile from GAME-MECHANICS §5, with the target height and the 1/3 move toward it.
- Builders raise the terrain.
- After any change: re-run water fill, mark the heightmap dirty, then rebuild the mesh once per event batch.

### Rendering the "banded" look

- Vertex colour = palette[band]. Band = (sum of the cell's 4 corners)/12, plus slope shading `light·(h[r][c] − h[r+1][c+1])`.
- Turn on flat shading (duplicate vertices per triangle) for the faceted look. Add real directional light on top if wanted.
- The original's 4-corner pseudo-projection does **not** need porting. Use a real `Camera3D` with presets:
  - Side: low pitch.
  - Iso: ~35°.
  - Plan: top-down orthographic.
- Keep the 2° rotate step as a keyboard option.

### Hidden-information UX

The original showed one static view per turn. In 3D, add:
- an aim preview arc (shown only with an "Angle Calculator"-style utility, which gives that item a clear role in the new game);
- a "replay last shot" ghost trail.

### AI

- Run the Monte-Carlo search on a `WorkerThreadPool` task so the UI stays responsive.
- Up to 200 trials × 1501 steps is about 300k cheap iterations, fine in GDScript. Keep the 200-trial cap and the tolerance growth from 04 §4.2.
- Keep the skill-noise tables in `ai/*.tres` so difficulty is data, not code.

### Determinism and tests

- One `TpRandom` per match, seeded at match start. Optionally re-seed per turn from (match seed, turn) to make replays robust. The original reseeded from the clock at every click.
- **Golden tests:**
  - launch vectors;
  - flat-ground range;
  - blast damage table (Missile at d=1 → 60..64);
  - crater depth profile;
  - round-end cash (score 100, cash 1000 → Round((1000+500)·1.08) = 1620);
  - AI noise ranges per skill;
  - landscape generator invariants (border = 0, playable heights within the expected range per mode).

## 5. Decisions (settled 2026-11-22)

1. **Name and theme:** generic placeholders for now. Working title **"Artillery3D"**, with genre-standard item names (Missile, Napalm, Heavy Bomb, …) and descriptive landscape names. Branding comes later, before the M8/M9 art pass. Keep every display name in the `.tres` data so a rename touches no code.
2. **Original bugs:** **fixed, with no toggles.** Implement only the corrected behaviour (GAME-MECHANICS §11) and drop all `legacy_*` paths.
   - Smoothing uses a symmetric 8-neighbour kernel.
   - The Tunneling stop test uses plain horizontal speed (the original weighted vx and vy unevenly; vz must stay excluded or the shell never stops underground).
   - Shock Wave pushes away from the epicentre.
   - City streets are chosen deterministically.
   - The Flame-thrower uses a correct hit box.
   - Fall damage applies only to downward falls.
   - Smart Cruise with no target flies straight on as a Cruise.
3. **Economy:** keep the original values (×5 score, ×1.08 interest, AI +1× bonus) as defaults in `balance.tres`. Retune in M10.
4. **Multiplayer:** hot-seat 2–4 players plus AI now, but **online-ready**:
   - The sim is strictly deterministic: one seeded `TpRandom` per match, fixed tick, and no frame-rate- or Node-dependent logic in `sim/`.
   - Each turn is fully described by (match seed, turn index, player input), which also gives replays for free.
   - Avoid `Dictionary` iteration order and platform float functions where results feed back into the sim.
5. **Visual style:** **banded low-poly**: flat-shaded faceted terrain with height-band vertex colours, simple low-poly tanks and props, and a stylised particle VFX palette.

## 6. Risks

| Risk | Mitigation |
|---|---|
| Unverified details (Earthquake smoothing, morph clamps, Smart Cruise with no target, tank sprite) | Make a sensible design choice and mark it. Optionally confirm by running the original in DOSBox (freeware v6.01) |
| Float vs Real48 differences change marginal hits | Irrelevant for a new game. Golden tests use tolerances |
| GDScript AI performance | Worker thread; cap trials; reduce step count for far-off-map early exit |
| Scope creep from 25 weapons + 27 landscapes | Ship M0–M5 with ~8 weapons and ~6 landscapes as a vertical slice, then add the rest from data |
