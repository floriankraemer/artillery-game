# Game mechanics

Consolidated rules spec: formulas, constants and flow.

---

## 1. World model

| Thing | Value |
|---|---|
| Heightmap | `array[-5..55, -5..55] of Integer` (61×61). Playable 1..50. The border is forced to 0 after generation. First index = row = world X, second = column = world Y |
| Height units | Same units as projectile z. Typical terrain is −30..+100. h < −50 is clamped |
| Terrain height at a real point | Bilinear interpolation of the 4 surrounding integer cells (`TerrainHeight(x,y)`) |
| Side maps (per cell) | object map (trees/bushes/graves, +0x10 = burnt), effect map (acid 1..5, fire 6..10, gooey 11/12, burning gooey 13/14, lava ≥ 20) |
| Water | Per landscape: flag + level W (5/7/9/10). Cells ≤ W are drawn as water and re-randomised to W−1−Random(3) on each redraw |
| North | bearing 0 = +X (row) direction. Compass arrows are drawn at both sides of the grid |
| Tanks | position (x,y) is a grid cell 5..44. z = TerrainHeight(x+0.5, y+0.5) + 0.5 |
| RNG | Turbo Pascal `Random`/`Randomize`. The RNG is reseeded from the clock at every "wait for click" |

## 2. Turn structure and game flow

```
Startup checks → (intro unless /quick) → Options screen
  └─ Play → New game: cash = StartCash, scores/wins/inventory = 0
       for round = 1..Rounds:
          pick landscape (deterministic: least-used enabled one, highest index first)
          reset gravity/air defaults, generate the landscape
          turn order: Sequential (1,2,3,4) or Random permutation, new each round
          every player shops (humans: Market screen; computers: scripted buying)
          place tanks, set per-round stats
          repeat cycle (turnCounter += 1):
             for each player in order:
                S.D.S. strike if scheduled for this turn
                regen for all tanks (shield gens, recharger, auto repair, armour loss)
                [Metamorphosis: terrain morph + sky tint]
                player aims and fires (human UI or AI)
                resolve shot → deaths → settle/drown → round ends if ≤ 1 alive
          until ≤ 1 alive or turnCounter ≥ MaxShots (no winner)
          round results: winner +hull +200 and +1 win; cash update
       final ranking by TOTAL SCORE (wins shown but not used)
```

ESC menu during play: Sound, Detail, Delay, **Destruct** (own tank explodes), **End Round** (no winner, no interest), **End Game**, **Quit**.

## 3. Options (defaults)

| Option | Range | Default |
|---|---|---|
| Players | 2..4. Slots Blue, Red, Green, Purple. Names up to 9 chars | 2 humans, "Player 1", "Player 2" |
| Type | Human, Goldfish, Gerbil, Owl, Fox, Dolphin | Human |
| Rounds | 1..50 | 3 |
| Max Shots (turn cycles per round) | 5..50 | 25 |
| Detail | Low, Medium, High, Very High | Medium (Low on a 386) |
| Delay (ms per flight step) | 0..30 | 10 |
| Start Cash | 0..20000, step 1000, wraps | 2000 |
| Play Order | Sequential / Random | Random |
| Air Resistance | Off / On | Off |
| Landscapes | any subset of 27, at least 1 | only Grand Canyon |

Blank names for players 1/2 get a random computer name (8-name pools).

## 4. Aiming and projectile flight

**Controls:**
- Power 0..100.
- Bearing 0..359 (wraps).
- Elevation 0..80, or −20..110 with Better Turret.
- All step by 1 (left click +, right click −, or keys).

**Launch:**

```
P = Power/100 ; B = (360 − Bearing)·π/180 ; E = Elevation·π/180
pos = (tankX + 0.45, tankY + 0.45, TerrainHeight(pos) + 1)
v   = (cos B·P·cos E / 2.2,  sin B·P·cos E / 2.2,  sin E·P·2.2) × 1.1  [× 1.5 with Extra Power]
Cruise / Smart Cruise: Elevation forced to 0
```

**Flight loop.** Fixed tick, explicit Euler, max 1501 steps:

```
loop:
  zPrev = z ; pos += v
  casing-specific steering (Smart Cruise, Heat Guided, Cruise), Grav Shield damping
  if AirResistance or landscape == Black Hole Zone:  v *= (A + 1) / 2     (not for weapon effect type 9)
  v.z -= g
  if TerrainHeight(x,y) + 0.1 >= z: hit
  casing-specific contact handling (Bounce / Roll / Tunnel)
  if Round(x) or Round(y) outside 1..49: off map (no explosion)
until hit or steps > 1500
```

- Defaults: g = 0.01, A = 0.994. Per-landscape overrides are listed in §8.
- **There is no wind.**
- On flat ground with no air resistance, range ≈ 1.21·P²·sin(2E)/g cells.
- The computer players use **the same flight function** in a silent mode to test shots.

**Casings:**

| Casing | Behaviour |
|---|---|
| Impact | Explodes on ground contact. There is no in-flight tank collision |
| Bouncing | On contact: vz = −0.7·vz, vx,vy × 0.9, z += 0.2. Explodes when \|v\| < 0.4 or an enemy is within 2 cells (and < 5 above) |
| Rolling | On contact: snaps to the ground. Gets ±0.001 push per 0.1-cell height probe per axis. A height step > 0.4 is a wall. Re-lifted 0.9, friction × 0.96. Explodes when \|v\| < 0.05 or an enemy is within 2 |
| Heat Guided | Within 15 cells (and < 35 above): v += 0.005·sign toward the tank on x,y and vz −= 0.02. Dives (vz −= 0.1) over fire/lava cells. Ignores Grav Shield tanks |
| Tunneling | Underground it keeps going with v × 0.985 per step. Explodes when √(3vx² + 2vy²) < 0.1 (original bug: vz is ignored) |
| Cruise | z = (2·zPrev + H)/3 + 1 (follows terrain). Crashes if H > z. Dives near tanks (d < 3). Fizzles if \|vx\|,\|vy\| < 0.05 |
| Smart Cruise | Locks onto the enemy closest to the aim direction. Heading blends (300·v + dir)/301. Speed → 0.3 cells/step. Holds ≥ 1.5 above ground and avoids/climbs terrain. Explodes when moving away within 5 of the target, or when any tank is within 3 |

**Tank falling:**
- When the ground under a tank changes, the tank drops instantly.
- `fall = |ΔH|`. If fall > 5 and the tank has a Parachute, one parachute is used and fall = 0.
- If fall < 6 it is ignored. Otherwise damage = 2·fall (shield first).
- **Drowning:** a tank in water without Water Wings dies.

## 5. Damage, craters, death

**Standard blast.** Used by explosive weapons, Earthquake and Hill Builder:

```
for each living tank q: d = Round(2-D grid distance) ; skip if d > R ; d = max(d, 1)
   dmg = (R − d)·15 + Random(R)
   shield −= dmg ; overflow → hull ; hull ≤ 0 → dead
   score: enemy → +bonus[w] + min(dmg, hull+shield) ; self → −2·min(dmg, hull+shield)
```

**Crater.** Only explosions dig:

```
depth(i,j) = clamp(Round(4R − 4.5·√(i²+j²)), 0, 2R)
target T   = h0 − depth/2   (R < 9)   |   h0 − depth   (R ≥ 9)
h = (2h + T ± noise) div 3    if h > T      // moves 1/3 toward target per blast
then 5 passes of 5-point smoothing over ±1.2R
```

Explosions also scorch or remove trees, damp fire/acid, and refill water.

**Death:**
- Every tank that dies explodes with R = 4, or **R = 25 with a Death Nuke**. That explosion blasts nearby tanks, so chain reactions are possible.
- Each death gives **+200 to every surviving tank**.
- Tanks pushed off the map (Shock Wave, Teleport) don't explode and give no +200.

**Per-turn upkeep.** Applies to all live tanks, before each shot:

| Upkeep | Rule |
|---|---|
| Shield Generators | +2..4 shield if 0 < shield < 50·gens − 10. A depleted shield does not regenerate |
| Shield Recharger | +10..19 if shield < 50·gens − 20 |
| Hull Auto Repair | +5..19 hull if hull < 50·armour + 80 |
| Hull Armour | a damaged tank loses one armour plate per turn |
| Ground effects | acid, fire, lava and gooey damage the tank standing on them, spread, and decay. Table in 02 §2e |

## 6. Items

**Starting stock:**
- Every tank has infinite **Tungsten Shell** and **Impact** casing.
- Tank capacity is 100 space, +60 per Hull Extension.
- Each purchase buys 1 unit.
- Selling refunds 100%.

**Weapons** (radius R in grid cells):

| # | Weapon | Price | Space | R | Effect |
|---|---|---|---|---|---|
| 1 | Tungsten Shell | 0 | 0 | 2 | blast, ∞ |
| 2 | Tracer | 1 | 0 | 0 | trace only |
| 3 | H.E. Shell | 10 | 1 | 4 | blast |
| 4 | Missile | 50 | 2 | 5 | blast |
| 5 | Large Missile | 100 | 4 | 6 | blast |
| 6 | Big Bomb | 250 | 6 | 7 | blast |
| 7 | Grape Shot | 100 | 5 | – | every tank with d < 8: 10..24 dmg; clears ground effects |
| 8 | Shield Ioniser | 350 | 8 | – | d < 7: shield −((8−d)·30 + R(30) + 10%) |
| 9 | Napalm | 300 | 8 | – | fire ring r < 5 (level 6..8) |
| 10 | Super Napalm | 400 | 14 | – | fire ring r < 9 (level 6..10) |
| 11 | Gooey Blob | 420 | 10 | – | gooey cells (spread downhill, burnable) |
| 12 | Acid Bomb | 250 | 10 | – | acid scatter r < 5 (eats terrain) |
| 13 | Acid Flood | 400 | 15 | – | acid scatter r < 10 |
| 14 | Fission Bomb | 1000 | 25 | 14 | blast |
| 15 | Fusion Bomb | 1500 | 40 | 22 | blast |
| 16 | Photon Bomb | 2100 | 55 | 37 | blast |
| 17 | Teleport | 600 | 9 | – | move own tank to the impact ±2, shield ×0.9; off map = death −200 |
| 18 | Flame-thrower | 500 | 8 | – | no flight. 9 bursts along the bearing, reach ~7.75. 8..27 shield dmg per burst, +10..14 score |
| 19 | Shock Wave | 1800 | 10 | – | d < 8 (incl. self): knocked away. Off map = death; landing 50..99 dmg |
| 20 | Air Strike | 1200 | 4 | – | click a target in plan view: 5 bombs along a line (R 5,5,6,5,5) |
| 21 | Flame-B-Gone | 400 | 5 | – | clears fire/lava within ±8 |
| 22 | Hill Builder | 800 | 7 | 8 | raises a hill (+8..28) and also blasts R 8 |
| 23 | Wall Builder | 500 | 8 | – | "+"-shaped wall, min + 25 high |
| 24 | Earthquake | 800 | 15 | 8 | two random-walk cracks (depth up to 25/40) plus blast R 8 |
| 25 | Volcano | 2500 | 60 | – | cone (min + 323/d up to 55), vent, lava that spreads every turn |

**Utilities:**

| Utility | Price | Space | Max | Effect |
|---|---|---|---|---|
| Shield Generator | 250 | 5 | 50 | +50 shield each, slow regen |
| Grav Shield | 1500 | 25 | 1 | damps enemy shells near you, immune to heat guidance, extra AI aim error |
| Hull Extension | 1800 | −60 | 12 | +60 capacity |
| Shield Recharger | 1500 | 25 | 1 | +10..19 shield/turn |
| Hull Auto Repair | 1500 | 30 | 1 | +5..19 hull/turn |
| Better Turret | 1200 | 8 | 1 | elevation −20..110 |
| Hull Armour | 250 | 4 | 6 | +50 hull each (start hull 100) |
| Extra Power | 800 | 10 | 1 | launch speed ×1.5 |
| Angle Calculator | 1500 | 18 | 1 | alternative aim readout |
| Parachute | 100 | 2 | 25 | negates one fall > 5 |
| Death Nuke | 1000 | 20 | 1 | death blast R 25 |
| Water Wings | 500 | 6 | 5 | survive water |
| Satellite Defence | 3000 | 45 | 1 | see below |

**Casings:**
- Impact 0 (∞), Bouncing 100, Rolling 250, Heat Guided 350, Tunneling 450, Cruise 500, Smart Cruise 600.
- Each costs 2 space.

**Satellite Defence System** (two hidden items):
1. Launch it as a shell that gets a +0.02/step upward boost.
2. If it climbs above z 260, it reaches orbit and gives 3 aim charges. If it hits ground first, it is destroyed.
3. Aim: click a target in plan view.
4. Next turn it strikes: `(5−d)·60 + R(30) + shield/2` for d < 4. The owner scores half.

## 7. Scoring and money

| Event | Points |
|---|---|
| Blast damage to an enemy | weapon hit bonus + damage dealt |
| Blast damage to self | −2 × damage |
| Any tank dies | +200 to **every** survivor |
| Last tank alive | + remaining hull + 200, +1 win |
| Teleport off map | −200 |
| Shock Wave kill | victim's shield (off map) or +200 (landing) |
| Ioniser / Flame-thrower / S.D.S. | shield drained / +10..14 per burst / half the damage |

**Round end:**

```
total += round
if round > 0: cash += 5·round
if round not aborted: cash = Round(cash·1.08)
cash = max(cash, 0)
computer players: cash += round      // extra AI bonus, even if negative
```

## 8. Landscapes

**Generator:** `Gen(mode, base, spikeMax, spikes, smoothPasses)`.
1. Fill with noise `base + R(10) − R(20)`.
2. Add `spikes` random cells at `R(spikeMax)`.
3. Run `smoothPasses` passes of 8-neighbour smoothing (the original kernel has a bug).
4. Run the mode block.
5. Zero the border.

| # | Name | Generator idea | Gravity | Air A | Water |
|---|---|---|---|---|---|
| 1 | Mars | noisy hills, red | 0.008 | 0.9999 | – |
| 2 | The Moon | craters, grey | 0.008 | 0.9999 | – |
| 3 | Neptune | very spiky, smooth ×10, cyan | 0.012 | 0.9999 | – |
| 4 | Cursed Earth | rough hills + graves | – | – | – |
| 5 | Electric Blue | hills, blue | 0.012 | 0.996 | – |
| 6 | Kryptonite | craters, green | – | 0.9999 | – |
| 7 | Pyros IV | terraces (plateaus), random purple | – | – | – |
| 8 | Dull Grid | hills, striped palette (grid look) | – | – | – |
| 9 | Compu-Scape | hills, green wireframe look | – | – | – |
| 10 | Metamorphosis | terrain morphs every turn, sky tints toward the leader | – | – | – |
| 11 | Spikey | flat + 50 spikes | – | – | – |
| 12 | Pyramids | 2 step pyramids + palms | – | – | – |
| 13 | Grand Canyon | high plateau cut by a meandering canyon | – | – | – |
| 14 | Rolling Hills | 1000 spikes, smooth ×15, trees | – | – | – |
| 15 | Antarctic | snow, fire melts terrain | – | – | yes (5) |
| 16 | Islands | low noise below sea, sand beaches, trees | – | – | yes (7) |
| 17 | Hell Fire | red, lava sea | 0.011 | 0.99 | yes (9) |
| 18 | Big Pyramid | one huge pyramid (apex +100) | – | – | – |
| 19 | Sine-Scape | 15·sin(0.005·B) + 8·cos(0.005·A) ripples | – | – | – |
| 20 | River Valley | V-shaped valley along a meandering river | – | – | yes (5) |
| 21 | Antarctic River | canyon river on snow | – | – | yes (10) |
| 22 | Black Hole Zone | 80/d funnel; air 1.01 always on (shots accelerate); tanks drift | – | 1.01 | – |
| 23 | Desert Island | circular island | – | – | yes (5) |
| 24 | Volcano | 323/d cone, lava crater | – | – | – |
| 25 | Mandelbrot Lake | Mandelbrot set heights, lake inside | – | – | yes (5) |
| 26 | Metropolis | 5×5 city blocks (10..59 high) with streets | – | – | – |
| 27 | Rain Forest | jungle river + dense trees | – | – | yes (5) |

"–" means the default: g 0.01, A 0.994. Exact generator arguments, palettes and light factors are in 03 §2.4–5.

**Tank placement:**
- Random cells 5..44 on both axes, at least 45/40/35 apart (2/3/4 players). The distance relaxes if placement keeps failing.
- Not in or near water (W + 5), and not high on the Volcano.
- A 4×4 pad under each tank is flattened.

## 9. Rendering (classic approach)

- 640×480×256 BGI. The terrain is 49×49 filled quads (two triangles each at High detail), drawn back to front. One of 8 traversal orders is chosen from the camera angle. Each cell is colour-coded by height band:
  ```
  colour = 200 + (sum of 4 corners)/12 + light·(h[r][c] − h[r+1][c+1])
  ```
- It is not true 3D. The four rotated grid corners are perspective-squashed and the grid is bilinearly interpolated between them. Height is a vertical pixel offset: 2.5 (Side), 1.8 (Iso), 0 (Plan).
- Views: Side, Isometric, Plan. The camera rotates in 2° steps. Every player starts the round in Iso at 0.05 rad.
- Shot trace: a line in the player's colour plus a ground shadow line.

## 10. Computer players

**Search.** Monte-Carlo search with the real flight simulator:
- Random Power 0..99 and Elevation 5..89, with the bearing pointing straight at the target.
- For the first 30 tries, it searches near the remembered shot from its previous turn instead (±4).
- It accepts a landing within ±tol cells of **any** enemy. tol starts at 1 and grows by 1 every 30 tries; after 200 tries it accepts anything.

**Deliberate error.** It stores the exact solution, then adds error that shrinks with each shot at the same target. Only power and bearing are noised, never elevation:

| Skill | Error |
|---|---|
| Goldfish | +40..79° bearing, power ×0.7 at first; shrinks over 9 shots |
| Gerbil | 30..59° bearing at first; shrinks over 6 shots |
| Owl | −15..−49° bearing at first; shrinks over 6 shots |
| Fox | 5..14° bearing at first; shrinks over 3 shots |
| Dolphin | none |

**Weapon choice.** Rule list: distance, target hull/shield and random rolls pick the weapon. Each skill level uses the cheap shell for its first shots at a new target (13 − 2·level). It teleports away from fire, S.D.S. targets and nearby tanks. It always uses the Impact casing.

**Shopping.** A fixed priority buy list; Gerbil and above get a second, longer list. The AI gets +1× its round score in cash.

## 11. Known bugs (fixed, not replicated)

1. The 8-neighbour smoothing counts one diagonal neighbour twice and skips the opposite one. This gives terrain a slight directional bias.
2. The Tunneling stop test ignores vz.
3. Shock Wave applies a +π correction twice, so the push direction can be wrong.
4. Metropolis reads an uninitialised street index.
5. One edge of the Flame-thrower hit box compares a row value against tank y.
6. Fall damage uses the absolute value, so a tank raised by terrain is also damaged.
7. Smart Cruise with no valid target reads array slot 0.

## 12. Remaining unknowns

- Exact Earthquake crack smoothing constants.
- Tank sprite vertices for all 8 octants.
- Exact meaning of the Angle Calculator display.
- The Metamorphosis morph clamps are partly inferred.
- What happens in Smart Cruise with no target.

Runtime observation in DOSBox would settle all of these.
