# Performance report: the endless load (endless-design.md §4, step 1)

The second run of `docs/performance-report-template.md`, for endless play's golem cap: does the
frame budget hold at **160 golems, 800 belt items and 20 boilers**? Every number is the
`perf` scenario's, windowed (1280×720, vsync off), `--fixed-fps 60`, on 2026-11-07, on the
machine of `performance-report-phase3.md`.

```sh
"$G" --path godot --fixed-fps 60 -- --scenario perf                         # 96 golems
"$G" --path godot --fixed-fps 60 -- --scenario perf --perf-load=endless     # 160 golems
```

**Verdict: after optimising (§4), the budget holds at 112 golems and not at 118. The golem cap
is 112** (the user's call of 2026-11-07: optimise more first, then cap where it holds).

## 1. The load at 160 (`EndgameLoadSpec.Endless`)

The 96-golem layout, unchanged, plus the same chains repeated on every band the whole floor
holds: north of the starter buildings up to the floor's north edge (y 5–24), then two bands
south of them (y −10 to −5). One steam network; a boiler at each golem row's east end, then at
the pipe rows' west ends: 19 in the workshop, 1 on the street. 800 items on the same four loops.

## 2. Found on the way: the 96-golem budget had slipped

Before any change, on this commit and on `798ee75` (before contracts), the starved and hover
phases were over the budget at 96 golems: starved p95 17.6 ms, hover 17.8. Phase 3 measured
13.3 and 14.4. The cost was the stall-facts probe: every golem's mood was classified every
frame, twice (its view and the sound director), and each read re-checked every golem's
placement, so a starved frame grew with the square of the golem count (probe 3.2 ms at 96,
8.7 at 160).

**The change:** a view re-classifies a golem at once when its own state changes (state,
stall, place, facing, carried, program, hold), and otherwise every fourth frame, staggered
(`GolemMoodRules.ReclassifyEveryFrames`, `ShouldReclassify`, `OwnStateSignature`). The sound
director classifies a new stall at once and then on the golem's turn. Only a neighbour's change
(a feeder carried away) can show up to 4 frames (67 ms) late. Every scenario passes, `moods`'
"red at once" (within a second) included.

## 3. Windowed, `--fixed-fps 60`, after the change

| Phase | 96: p95 / max ms | 160: p95 / max ms |
|---|---|---|
| `flowing-1x` | 13.7 / 19.0 | **17.7 / 49.3** |
| `flowing-4x` | 14.4 / 25.6 | **18.6** / 28.6 |
| `starved` | 15.5 / 21.6 | **20.9** / 29.9 |
| `hover` | 15.9 / 26.8 | **22.8** / 30.6 |
| `unlit` | 12.9 / 17.9 | 15.8 / 21.3 |
| `no-draw` | 9.8 / 14.5 | 12.8 / 18.8 |

Budget: p95 ≤ 16.6 ms, max ≤ 33.3 ms. Bold is over.

At 160, flowing: `node.golems` 2.8 ms and `node.buildings` 2.3 ms a frame (p95), render CPU
5.7, Core's tick 0.5. Starved adds the probe (3.0 ms p95, still growing faster than the golem
count) and mood (1.6).

Straight lines through the two loads put each phase's limit at about: flowing-1x 142 golems,
flowing-4x 125, starved 109, hover 102. A cap set from these would be about 100 golems, a
dozen above the campaign's endgame.

## 4. More optimising (the user's call), and the cap

Three more changes, then the loads between:

- **Stall facts:** the probe re-checks its index of who feeds whom when a golem's layout changes
  (`Golems/GolemLayout.Epoch`: moved, turned, carried, removed, a mast bound, a program
  written or a card added or taken off), not on every read; each new frame still rebuilds it
  whole, so a direct field write is caught a frame later at most. Probe at 160: 3.0 → 0.5 ms.
- **Golem nodes** ask once whether the puff picture exists, not every frame per golem.
- **Building nodes** skip the feel update while nothing animates, and a pipe changes its
  picture only when its heat turns. At 160: `node.golems` 2.8 → 2.4 ms, `node.buildings`
  2.4 → 1.9.

Windowed, `--fixed-fps 60`, `--perf-golems=N` (five belt items a golem, the boilers the layout
lays). p95 ms; bold is over 16.6.

| Golems | flowing-1x | flowing-4x | starved | hover | unlit | max over 33.3? |
|---|---|---|---|---|---|---|
| 96 | 12.8 | 13.6 | 13.6 | 14.5 | 11.8 | no (29.3) |
| 112 | 13.3 | 14.7 | 14.7 | 15.9 | 13.3 | no (26.8) |
| 118 | 13.6 | 15.1 | 14.8 | **16.7** | 13.3 | no (27.1) |
| 128 | 14.0 | **19.6** | 15.8 | **16.9** | 14.0 | yes (41.1) |
| 144 | 14.6 | 15.9 | 15.9 | **17.7** | 13.7 | yes (39.2) |
| 160 | 15.5 | **16.7** | **17.1** | **18.8** | 14.6 | yes (39.8) |

The runs swing by about ±2 ms at p95 between runs (128's flowing-4x 19.6 came with a render CPU
spike of 3 ms; its other run read 15.8). Hover is the phase that sets the cap: the camera on a
waiting golem in the densest rows draws more; the card itself costs 0.2 ms. What remains is
drawing (about 920 draw calls) and the nodes' own work; the next gains are a render pass
(belt cargo and buildings batched), not more rules.

**The cap: 112 golems**, the largest load that passed every phase. The Assembly Bays' campaign
top is 100 slots, the endgame's 96 golems and room to rebuild; endless play adds two tiers, to
112 (endless-design §2).

