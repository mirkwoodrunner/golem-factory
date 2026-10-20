# Performance report: the endgame load, Phase 3

The first filled-in copy of `docs/performance-report-template.md` §6, from Code agent B on
`phase3/tuning`, 2026-10-20. Every number comes from the `perf` scenario or the Core benchmark;
the run that printed it is named. The scenario writes the same tables to `user://perf-report.md`
on every run.

**Verdict: the budget holds at 96 golems, 500 belt items and 12 boilers, after optimising.
Stage 4's Conduit was not trimmed.**

## 1. The run

```
Date:      2026-10-20                 Commit:   18044ef (code) on phase3/tuning
Machine:   Intel Core i5-10400F @ 2.90 GHz (6 cores), NVIDIA GeForce RTX 2060, 16 GB (15 GB usable)
Godot:     4.7.2-stable mono, gl_compatibility   Run: windowed (1280×720, vsync off, MaxFps 0), and headless
Load:      golems 96   belt items 500   boilers 12
```

Commands:

```sh
G=".../Godot_v4.7.2-stable_mono_win64_console.exe"
"$G" --path godot --fixed-fps 60 -- --scenario perf               # windowed: the budget
"$G" --headless --path godot --fixed-fps 60 -- --scenario perf    # headless: Core's share
dotnet test godot/GolemFactory.sln -c Release --filter Category=Perf
```

### 1.1 The load as built (`Core/Diagnostics/EndgameLoad.cs`)

- **The room:** pack upgraded to its last rung, all six floor rows, bays to 100 slots, every
  price granted first and paid through the wallet.
- **The street:** eight Scavengers, `Extract(4), Push`, one at each of eight stalls (y −16) into
  a crate in front, on one boiler.
- **The workshop (y 5–19):** nine rows of golems facing east with a crate between each pair, so
  each golem's target crate is the next one's source. The rows are ordered so a golem's feeder
  can usually push the good it wants (a loader before a coker, a smelter before a Gear cutter),
  which is what makes a starved factory mostly grey and walks the probe's feeder chain.
  88 golems in §2's mix, on one steam network: a pipe row under every second golem row, one pipe
  column joining them, eleven boilers.
- **The mix:** 8 extractors, 23 cokers (Presser), 11 Coal and Scrap loaders, 12 smelters (R4, R5,
  R6), 4 Brass and Wire, 12 components (R3, R8–R12), 8 tier 4–5 Overclockers (R13, R15, R16, R18,
  and two cokers on Repeat), 15 carriers, 3 Zeppelins to masts. `TheLoad_IsWhatItSays` pins the
  counts.
- **Belts:** four closed rectangular loops (two on the street's south rows, two across the town
  square, clear of the tower), 200 cells, 500 items deposited onto the lanes a step apart. A
  closed loop is accepted by `BeltNetwork` and keeps every item moving without a sink.
- **Steady:** `Maintain()` every 2 s of game time sets each source crate to 25 of each good its
  taker hauls (half full, so the pusher behind has room), empties the line ends and the masts,
  and tops up the boilers. **Starved:** every crate is emptied and kept empty; the boilers stay
  full, so golems stall waiting for goods, never for steam.

The template's [M]s, answered:

- **Creative Mode** is a compose-time switch (`sandbox.json`) whose only readers are the stalls'
  starting stock and the market; it leaves steam burn and crates alone. The scenario runs the real
  `sandbox.json`, so the load gives each stall it uses a truckload of a million instead: the same
  effect.
- **The guide** is set to finished and dismissed (`TutorialGuide.Restore("finished", …)`), as an
  endgame player's is, so it neither shows nor reacts to the load.
- **The player** stands at (0, 11.5), in the middle of the chains, for every phase; the view shows
  about two thirds of the workshop's golems.

## 2. Frame time (ms), windowed

Final run (`perf`, windowed, run 1 of 2 at 18044ef). Run 2 agreed within 0.7 ms at p95.

| Phase | Frames | p50 | p95 | max | Pass (p95 ≤ 16.6, max ≤ 33.3) |
|---|---|---|---|---|---|
| `flowing-1x` | 1200 | 8.35 | 10.22 | 24.58 | yes |
| `flowing-4x` | 1200 | 8.34 | 9.33 | 24.91 | yes |
| `starved` | 1200 | 11.37 | 13.26 | 23.44 | yes |
| `hover` | 1200 | 12.28 | 14.35 | 23.58 | yes |
| `no-draw` | 1200 | 6.11 | 7.05 | 17.62 | n/a |

Run 2: p95 9.8, 9.3, 13.9, 15.1, 7.1; max 22.8, 25.3, 21.9, 21.1, 17.0.

Headless (no renderer; judged on Core's share, p95 ≤ 6): frame p95 4.8, 4.9, 8.7, 8.3, 3.3;
Core p95 0.17, 0.20, 1.84, 1.74, 0.15. PASS.

## 3. Simulation tick (ms), windowed

| Phase | Ticks | p50 | p95 | max | `tick.golems` p95 | `tick.belts` p95 | `tick.steam` p95 |
|---|---|---|---|---|---|---|---|
| `flowing-1x` | 200 | 0.16 | 0.25 | 1.02 | 0.07 | 0.04 | 0.05 |
| `flowing-4x` | 801 | 0.15 | 0.23 | 0.48 | 0.11 | 0.04 | 0.06 |
| `starved` | 200 | 0.17 | 0.22 | 0.38 | 0.08 | 0.03 | 0.05 |

The tick is far inside its 4 ms sub-budget. The `tick.*` columns are per frame (a frame runs at
most one tick, even at 4×).

## 4. Core hot spots, per frame, windowed

| Phase | `mood` calls | `mood` ms p95 | `probe.read` calls | `probe.read` ms p95 | `loop.walk` golems visited p95 | `strip` ms p95 | `hover` ms p95 |
|---|---|---|---|---|---|---|---|
| `flowing-1x` | 97 | 0.03 | 0 | 0.00 | 0 | 0.00 | 0.01 |
| `starved` | 97 | 1.60 | 118 | 1.96 | 100 | 0.00 | 0.05 |
| `hover` | 97 | 1.70 | 118 | 2.23 | 100 | 0.00 | 0.04 |

`probe.read` is above `mood` because the badges' captions and the hover card read the facts
outside a mood too. Starved, 88 golems stall: 43 grey, 45 red in the scenario's phase.

## 5. Renderer, windowed

| Phase | Render CPU p95 | Render GPU p95 | Draw calls | Objects | Nodes |
|---|---|---|---|---|---|
| `flowing-1x` | 4.31 | 1.09 | 779 | 1940 | 3645 |
| `no-draw` | 2.89 | 0.94 | 571 | 1328 | 3645 |
| Difference (world drawing) | 1.42 | 0.15 | 208 | 612 | |

`no-draw` hides the golems, their badges, `BuildingsLayer` and the belts. What remains (the floor
tiles, the shell's walls, props and sconce lights, the HUD) costs 2.9 ms of render CPU.

## 6. Node time, per frame (p95 ms, windowed)

The scenario also times the per-frame nodes (`Scripts/NodePerf.cs`), so a frame's process time
can be read beside Core's sections:

| Phase | `node.golems` | `node.buildings` | `node.world` | others, each |
|---|---|---|---|---|
| `flowing-1x` | 1.12 | 0.57 | 0.20 | ≤ 0.16 |
| `starved` | 3.39 | 0.51 | 0.59 | ≤ 0.20 |

`node.golems` includes each golem's mood read, and so the probe.

## 7. The Core benchmark (`--filter Category=Perf`, Release)

`EndgameLoadTests.TheLoad_TicksAndReadsWithinBudget`: 1,200 frames at 1/60 s, each reading every
golem's mood and reconciling the strip (184 probe reads a frame starved, twice the game's).

| Phase | Tick + presentation p50 | p95 | max |
|---|---|---|---|
| `flowing` | 0.02 | 0.18 | 23.0 |
| `starved` | 0.40 | 2.91 | 6.80 |

Both under the 6 ms alarm. The flowing max is the first measured frame (JIT); p95 is 0.18.

## 8. What was optimised, before and after

Template §5.2, in order, re-running `perf` after each step.

**Step 1: the first windowed run failed every phase.** It already had a first, cached form of the
probe index; the frame p95s were 17.1, 18.5, 22.5 and 23.3, the maxes 41.2, 36.5, 35.2 and 36.1.
Headless, before any change, Core's starved p95 was 5.89 ms (probe 4.63, mood 4.14) and the
nodes' `node.ui` was 3–4 ms in every phase.

**Step 2: the probe.** `StallFactsProbe` scanned every golem, through LINQ, for every feeder and
drainer it looked up, so a read was O(golems) per link of the feeder walk.
- It now builds, per read on a bare probe and per frame in the world, indices of who pushes into
  and takes from each store and belt lane, and rebuilds them the moment any golem's cell,
  facing, mast, launch card or runnability changes. Programs, holds and stalls are still read
  live.
- A per-frame cache of each golem's facts was tried and removed: the `moods` scenario writes a
  program and reads the mood in the same step, and the cache answered with the frame's old
  verdict. A cached answer is a changed verdict.
- `TheFramedProbe_AnswersAsABareOne` compares every golem of the starved load against a fresh
  probe; every `StallTriageTests` case passes unchanged.
- Starved, headless, same load and state: **`probe.read` p95 5.07 → 1.93 ms, `mood` 4.08 → 1.55,
  Core p95 4.08 → 1.55**. Windowed: probe 5.14 → 1.96.

**Step 3: the renderer and the nodes.** Named from the node sections, each change draws exactly
what it drew before:
- **Badges: one `CanvasLayer` and one font for all golems.** Every `GolemNode` built its own
  `CanvasLayer` (layer 4) and its own emboldened `FontVariation` for its badge: 96 canvases and 96
  glyph caches, paid even with no badge showing. One shared layer (`GolemBadges`, added deferred)
  and one font: **flowing render CPU p95 8.4 → 4.3 ms, starved 10.3 → 4.5**.
- **Badges re-measured every frame.** `ResetSize` ran on every golem every frame, and each golem
  scanned the screen group; now only after the words or the scale change, and the scan runs once
  a frame. With the probe's step, starved `node.golems` p95 went 6.4 → 3.4 ms (what remains is
  mostly the mood read).
- **The build menu restyled all 13 rows every frame** (seven theme overrides each, every one a
  theme notification): `node.menu` p95 **2.7 → 0.02 ms** in every phase. This one isn't the
  load's: it cost every frame of every game.

| Windowed frame p95 / max (ms) | `flowing-1x` | `flowing-4x` | `starved` | `hover` |
|---|---|---|---|---|
| First run (cached probe, old nodes) | 17.1 / 41.2 | 18.5 / 36.5 | 22.5 / 35.2 | 23.3 / 36.1 |
| Original probe, nodes fixed | 9.2 / 21.5 | 12.9 / 25.1 | 15.6 / 23.9 | **17.9** / 28.3 |
| Final (indexed probe, nodes fixed) | 10.2 / 24.6 | 9.3 / 24.9 | 13.3 / 23.4 | 14.4 / 23.6 |

The first run's hover phase also moved the player beside the hovered golem, a different view; it
now stands where the starved phase does, so only the hover differs. That fix is in the second and
third rows.

**Step 4, the Conduit trim: not needed.** The budget holds with margin (worst p95 15.1 over two
runs, worst max 25.3), so `tuning-data-spec.md` §10.2's rule does not fire and stage 4 keeps its
Aether Conduit line.

## 9. Verdict

```
Budget held at 96 golems / 500 belt items:  yes
Top Core section:   probe.read (1.96 ms p95, starved)   Renderer share:  4.3–4.5 ms of 9–14 ms
Action taken (§5.2 step):  2 (probe indices) and 3 (badge layer and font, badge sizing, build menu)
Re-run result:      PASS windowed (two runs), PASS headless
Stage 4 Conduit trimmed:   no  (tuning-data-spec.md §10.2)
```

## 10. Stage 4 measurements for `tuning-data-spec.md` §18

`EndgameLoadTests.StageFour_CoalShare_AndTheConduitLine` (explicit, `Category=Perf`):

- **The load's burn:** 288 Coke a minute with all 96 golems working, 3 a golem: 10.5 guide
  cokers' output (27.3 a minute each). The load stands 25 Coke makers (23 cokers and two
  Repeat Overclockers) and 7 Coal loaders: **32 of 96, a third**.
- **From the recipes**, stage 4 at full demand (2 Chronometer Cores, 2 Great Cogs, 1 Aether
  Conduit a minute), each line at the guide's program shape (Haul each input, Assemble, Push) and
  §2's step times, raw goods hauled by loaders at 24 a minute, and 3 Coke a minute for every
  golem: **73.1 golem-equivalents, of which the coal line (cokers 12.1 + Coal loaders 17.8) is
  29.8, 41 %**. That is the design's 41 % at 6 Coke a minute, not the "about a third" §3
  expected at 3: smelting Coke (R4, R5, R6) and hauling the Coal by loader keep it up. Coal from
  a stall by extractor (Extract(4) is 16 ticks for 4 against a loader's 10) would raise it.
- **The Conduit line costs 5.6 golem-equivalents** (73.1 → 67.5): R18 itself 0.16, the Aether
  Cells 0.21, Lens 0.32, Glass 0.75, Brass 0.22, Copper and Zinc smelting 0.75, Wire 0.08, their
  ore and Aether loaders 1.1, and the Coke and Coal for all of them 2.0. §10.2 guessed about 5.
