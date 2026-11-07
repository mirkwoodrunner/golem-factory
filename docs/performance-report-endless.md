# Performance report: the endless load (endless-design.md §4, step 1)

The second run of `docs/performance-report-template.md`, for endless play's golem cap: does the
frame budget hold at **160 golems, 800 belt items and 20 boilers**? Every number is the
`perf` scenario's, windowed (1280×720, vsync off), `--fixed-fps 60`, on 2026-11-07, on the
machine of `performance-report-phase3.md`.

```sh
"$G" --path godot --fixed-fps 60 -- --scenario perf                         # 96 golems
"$G" --path godot --fixed-fps 60 -- --scenario perf --perf-load=endless     # 160 golems
```

**Verdict: the budget holds at 96 golems; it does not hold at 160.** The cap is not yet set
(§4 of the design: the user's call on more optimising first).

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
