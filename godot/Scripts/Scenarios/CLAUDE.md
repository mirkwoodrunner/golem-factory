Folder rules for the scenarios. The root `CLAUDE.md` has the project overview, commands and settled rules.
Paths here are relative to `godot/` unless they say otherwise.

# Scenarios

Current scenarios, one line each. **What each checks in full is in `docs/scenarios.md`**: read the
scenario's entry there, and its source, before changing it, and update the entry when its checks change.

**A step that throws fails its scenario** (`ScenarioRunner._Process`, 2026-11-26). Before, the
exception was logged and the run went on to PASS with that step's checks unmade. Don't catch
broadly inside a step to get past one: return a failure string instead.

| Scenario | Checks | Design doc |
|---|---|---|
| `world` | the shell, the sconces, the street's carts, walking the street |  |
| `build` | every placeable through the real build menu, drag runs, demolition refunds |  |
| `interact` | [E], hold-[E] and [G] at the cart, the bench and the station |  |
| `golems` | every chassis and every `AppendageActionType` on real steam |  |
| `golem-motion` | each chassis' work motion and frames, side by side |  |
| `workbench` | programming end to end by real drags, the next-drag hint, the read head |  |
| `management` | the HUD and the Management tabs, claims, pack upgrade, Extend, the Ledger |  |
| `save` | save, wreck, load, and two more rounds change nothing |  |
| `tutorial` | the guide's panel, its arrow, F1, Skip, clear of the Workbench |  |
| `playtest-kit` | F9 once per chapter from a fresh game, all ten |  |
| `inventory` | the Pack and Crate screens with real keys and clicks |  |
| `moods` | grey, red and blue on `StreetFixture`'s street, badges, collecting progress, the alerts strip |  |
| `hover` | the pointer's hover card, tiles and arrows, the feeder line |  |
| `font-glyphs` | the font covers printable Latin-1 plus → ≥ █ ░ |  |
| `perf` | the endgame profile and its frame budget | `performance-report-template.md` §4 |
| `art` | the art against `Tools/Art/art_manifest.json` | `art-pipeline-design.md` §7.1 |
| `audio` | every sound slot loads and is requested at its trigger, counted, never heard | `audio-design.md` §7 |
| `feel` | zoom steps, dust and pops, the card's landing, the alert strip's camera glide | `game-feel-design.md` §6 |
| `settings` | the Settings tab with real keys, clicks and the wheel | `menus-design.md` §4 |
| `accessibility` | Reduced motion and colour-blind marks on the `moods` street | `menus-design.md` §5 |
| `title` | the title screen, across a reload | `menus-design.md` §6 |
| `slots` | save slots and the autosave, across reloads | `menus-design.md` §7 |
| `overview` | the factory overview: map, production graph, alerts history | `menus-design.md` §8 |
| `creative` | Creative Mode, across reloads | `menus-design.md` §9 |
| `supply` | supply carts on the street | `supply-design.md` |
| `contracts` | city contracts | `contracts-design.md` §9 |
| `endless` | endless play after the tower | `endless-design.md` |
| `docks` | the Docks region | `region-design.md` |
| `golem-sales` | golem orders at the City Office, sold by hand | `golem-sales-design.md` |
| `gamepad` | every verb by pad, joypad events only | `steam-deck-design.md` D4 |
| `guide-ahead` | the guide's program step over the Workbench ahead of the guide | `guide-chapters-design.md` §9 |
| `gamepad-ui` | the screens by pad, joypad events only | `steam-deck-design.md` D6 |
| `tick-visuals` | the shaders on the factory's clock and the tile batches |  |

- `perf` also takes `--perf-golems=N` (a load of N golems in the endgame's proportions) and
  `--perf-load=endless` (160), for measuring the golem cap (`docs/performance-report-endless.md`).

**A scenario that reloads the scene** implements `IReloadingScenario` (or derives from
`StepScenario`, which does): the runner of the new scene hands it back (`Resumed`), and it finds
its nodes again. Count a reload from before the
click or key that causes it; the reload can land between two steps.

**Screen captures:** a windowed run with `--capture-dir=<dir>` writes `screen-*.png`, one per
screen (`world` the room, `management` the HUD and every tab, `build` the menu, `tutorial` the
guide panel, `interact` the construction panel, `inventory` the Pack and
Crate screens), beside the `critique-*` shots. They are deterministic under `--fixed-fps 60`,
so an art change that shouldn't move a screen is checked by comparing them pixel for pixel
(`docs/art-pipeline-design.md` §3.1).

**Scenario input:** `Input.ParseInputEvent` **without** `FlushBufferedEvents` for key presses,
and a tap must hold the key down across at least one frame. A press and release in the same
frame, or a flush from inside `_Process`, never reads as "just pressed" to a node that has
already run that frame.

A scenario that clicks a screen's control: see `godot/Scripts/UI/CLAUDE.md` (a control has no
size until the frame after it's built). Scenario runs never write the player's save or
playtest report: see `godot/Core/Tutorial/CLAUDE.md`.
