# Performance report: the endgame load

**Status: template and spec, 2026-10-19, for the user's approval.** Phase 3 of `docs/roadmap.md`:
"Profile the endgame at about 96 golems and 500 belt items. If it doesn't hold, cut stage 4's
Conduit demand." §1–§5 specify what the Code agent builds (the `perf` scenario, the Core load
builder, the sampler, a Core benchmark, the playtest's frame-rate table). §6 onward is the report,
left blank, filled once per run.

Siblings: `docs/tuning-data-spec.md` (§10.2, the Conduit rule), `testscript/full-playthrough.md`
(the human run's frame-rate table).

## 0. Invariants

1. **Rules in Core, nodes draw.** The load is built by a Core class on `SandboxWorld`, through the
   player's verbs (build mode, the station, `GolemProgramWriter`), never by hand-made nodes. The
   scenario only drives it and reads Godot's monitors.
2. **Profiling costs nothing when off.** The sampler is opt-in by null (`SandboxWorld.Perf` is null
   unless a perf run sets it), as stall facts are (`onboarding-feedback-design.md` §0.6). No
   allocation per call when on.
3. **Determinism is untouched.** Measuring reads clocks; it never changes what a tick does.
4. **No player file is touched.** The scenario writes nothing to `save.json`; its report goes to
   `user://perf-report.md`.
5. **Every new system comes with a scenario**: `perf` is it.

## 1. The budget

| What | Budget | Why |
|---|---|---|
| Frame time, p95 | **≤ 16.6 ms** (60 fps) | `project.godot` sets no frame cap (vsync on, so 60 on a 60 Hz display), every scenario runs at `--fixed-fps 60`, and no doc sets another |
| Frame time, worst | **≤ 33.3 ms** | A worst frame of two frames' length reads as a hitch, not a stutter |
| Core per frame (tick share + presentation reads), p95 | ≤ 6 ms | Leaves ~10 ms for nodes and the renderer |
| One simulation tick, p95 | ≤ 4 ms | At 4× speed (40 ticks/s) a 60 fps frame runs at most one tick |
| Renderer CPU, p95 | ≤ 8 ms | Y-sorted sprites, belt `_Draw`, badges |

The p95 and worst rows decide pass or fail; the other rows say where to look when it fails. The
sub-budgets are this spec's proposal, not measured; §8 records what the first run finds.

## 2. The load

**`Core/Diagnostics/EndgameLoad.cs`** (new, engine-free): `static EndgameLoadResult Build(SandboxWorld
world, EndgameLoadSpec spec)`, defaults `Golems = 96`, `BeltItems = 500`, `Boilers = 12`. It:

- buys all six floor rows (`FloorExpansion`, costs granted), so the load has the endgame's room;
- places every building through `BuildModeController` with its cost granted, as the kit's
  `PlaceGranted` does, and builds every golem at the starter station (`TryConstructGolem`), places
  it (`SetPlacement`), and programs it through `GolemProgramWriter.TryWrite`;
- uses Creative Mode (`GameMode.IsCreativeMode`) so stalls never empty and the load stays steady
  [M: confirm Creative Mode leaves steam burn and crates untouched];
- powers every golem: 12 boilers, each with ≤ 8 golems in reach and 240 Coke, plus pipe;
- returns the golems by role, the crates, and the belt cells, for the scenario's phases.

**The golem mix**, from `progression-design.md` §7's stage-4 factory, each on the program the guide
teaches for its job (`tuning-data-spec.md` §2):

| Role | Count | Chassis | Program |
|---|---|---|---|
| Extractor at a stall | 8 | Scavenger | Extract(4), Push |
| Coker | 23 | Presser | Extract(1) or Haul(Coal, 1), R1, Push |
| Coal and Scrap loader | 11 | Scavenger | Haul(good, 4), Push |
| Smelter (R4, R5, R6) | 12 | Hauler | 2 Hauls, Assemble, Push |
| Brass and Wire (R7, R19) | 4 | Hauler, Presser | |
| Components (R8, R9, R10, R11, R12, R3) | 12 | Presser, Hauler | |
| Tier 4–5 (R13, R15, R16, R18), 2 with Repeat on R1 | 8 | Overclocker | 3 Hauls, Assemble, Push; or Haul, Assemble, Repeat, Push |
| Carriers and Slag routing | 15 | Hauler, Scavenger | 2–3 Hauls, Push |
| Zeppelins | 3 | Zeppelin | Haul, Freight Launch, to masts |
| **Total** | **96** | | |

Each golem stands between a source crate (or a stall, or a belt) and a target crate, pre-filled
with 50 of each input so lines run without upstream golems keeping up [M: the cells; a grid in the
workshop rows, the street and the town square].

**Belt items:** closed rectangular belt loops, enough cells that 500 items ride them, filled by
depositing straight onto segments until the count is reached. A loop keeps every item moving
forever without a sink [M: whether `BeltPlacementRules` accepts a closed loop; if not, straight
runs of 50 cells, each fed at the head by an extractor and emptied at the tail by an unloader
pushing into a crate the load empties every 10 s]. Belt items are counted as the sum of every
segment's items.

**Not in the load:** the player's walking, the Workbench and Management screens, the guide. The
guide panel stays hidden (`tutorial: false` for the run [M: or `TutorialPanel` hidden]).

## 3. What is measured

**`Core/Diagnostics/PerfSampler.cs`** (new, engine-free): named sections, each with a call count
and total, max and per-frame microseconds from `Stopwatch.GetTimestamp`. `using (perf?.Measure("tick"))`
returns a struct, so a null sampler costs a null check and no allocation. `BeginFrame()` /
`EndFrame()` close a frame's totals into a histogram (p50, p95, max). `SandboxWorld.Perf` holds it,
null by default.

| Section | Wraps | Why |
|---|---|---|
| `tick` | each `SimulationClock` tick inside `SandboxWorld.Advance` | the simulation's whole cost |
| `tick.golems`, `tick.belts`, `tick.steam`, `tick.other` | the clock's registrants, grouped by type [M: the registrant types] | which system dominates a tick |
| `probe.read` | `StallFactsProbe.Read` (count and time) | the review's and critique's flagged hot spot: it walks feeders depth-first for every stalled golem, every frame (`onboarding-feedback-design.md` §3.7, §16.1) |
| `mood` | `GolemEntity.Mood` (count and time; includes `probe.read` and `StallTriage`) | read by `GolemNode`, `AlertsStrip`, `Hud` each frame |
| `strip` | `AlertsStrip.Reconcile` | walks every golem's mood |
| `hover` | `SandboxWorld.HoverCard` | composes a card each frame while hovering |
| `loop.walk` | the depth-first `Delivers` walk inside the probe (count of golems visited) | to see whether the walk, not the read, is the cost |

**From Godot**, read by the scenario each frame:

| Measure | Source |
|---|---|
| Frame time | `Time.GetTicksUsec()` between consecutive `_Process` calls: the whole frame, process and render |
| Process time | `Performance.GetMonitor(Performance.Monitor.TimeProcess)` |
| Renderer CPU and GPU | `RenderingServer.ViewportSetMeasureRenderTime(rid, true)`, then `ViewportGetMeasuredRenderTimeCpu` / `…Gpu` |
| Draw calls, objects | `Performance.Monitor.RenderTotalDrawCallsInFrame`, `RenderTotalObjectsInFrame` |
| Nodes | `Performance.Monitor.ObjectNodeCount` |

**Y-sort draw** has no section of its own: it shows in renderer CPU. The scenario's `no-draw` phase
hides the world's drawing (golem nodes, `BuildingsLayer`, belt runs: `Visible = false`) for a
window, so the difference is what drawing the world costs.

## 4. The `perf` scenario

`Scripts/Scenarios/PerfScenario.cs`, registered in `ScenarioRunner.Scenarios` as `perf`, in
`Sandbox.tscn`. Root `CLAUDE.md`'s scenario list gains it.

```sh
G=".../Godot_v4.7.2-stable_mono_win64_console.exe"
"$G" --path godot --fixed-fps 60 -- --scenario perf               # windowed: the real budget
"$G" --headless --path godot --fixed-fps 60 -- --scenario perf    # headless: Core and nodes only
```

`--fixed-fps 60` makes every frame 1/60 s of game time, so each run simulates exactly the same
ticks; the wall-clock time a frame takes is then its real cost. The scenario sets
`DisplayServer.WindowSetVsyncMode(Disabled)` and `Engine.MaxFps = 0`, so a frame is never held
to the display.

**Phases**, each 1,200 frames (20 s of game time) after a 120-frame warm-up:

| Phase | Setup | Measures |
|---|---|---|
| `flowing-1x` | the load running, speed 1× | the steady factory |
| `flowing-4x` | speed 4× (`ClockReadout.SpeedPresets`) | a tick nearly every frame |
| `starved` | every pre-filled crate emptied: ≥ 60 golems stalled waiting, most grey | `probe.read` at its worst |
| `hover` | `starved`, with the pointer on a waiting golem (`World.Inspector.Point`) | the hover card each frame |
| `no-draw` | `flowing-1x`, world drawing hidden | renderer cost by difference |

**Checks before measuring**, failing the scenario if not met: 96 golems stand; every golem is
powered (no `NoSteam`); belt items ≥ 500 after warm-up; in `starved`, stalled golems ≥ 60.

**Output.** One line per phase:

```
[perf] phase=flowing-1x frames=1200 p50=9.8 p95=12.1 max=19.4 tick_p95=2.1 core_p95=3.0 probe_calls=0 probe_ms=0.0 mood_ms=0.4 render_cpu_p95=6.2 render_gpu_p95=3.1 draws=410 nodes=2380 golems=96 belt_items=512
```

then `[scenario perf] PASS` when every windowed phase but `no-draw` has p95 ≤ 16.6 and max ≤ 33.3,
else `FAIL` naming the phase and the number. Headless runs report the same lines and judge only
`core_p95 ≤ 6` (there is no renderer). It writes the lines and a filled copy of §6's tables to
`user://perf-report.md`.

## 5. In the playtest

`PlaytestNode` feeds a Core **`FrameTimeStats`** (new, `Core/Diagnostics`, shared with the scenario's
histograms) each frame with the frame's wall milliseconds, keyed by the guide's current chapter.
`PlaytestSession.Compose` gains a section after "Time on each step":

```
## Frame rate

| Chapter | Frames | p50 ms | p95 ms | worst ms | golems | belt items |
```

The golems and belt items are counted at the chapter's last frame. `PlaytestSessionTests` checks the
section's shape and the percentiles from a known series.

Also in `PlaytestSession.Compose`, before "Your answers" (`testscript/full-playthrough.md`):

- a "Time per chapter" table (chapter, time, budget from `tuning-data-spec.md` §14), from the step
  timings already recorded;
- a line "Chapters 1–10: h:mm:ss (target 3:00:00) · kit used: no", once `done` is reached, beside
  the existing "Chapters 1–4" line.

### 5.1 A Core benchmark

**`Core.Tests/Diagnostics/EndgameLoadTests`**, two tests:

- `TheLoad_IsWhatItSays` (always runs): `EndgameLoad.Build` on a composed world; 96 golems, all
  powered after one tick; ≥ 500 belt items after 600 ticks; emptying the crates leaves ≥ 60
  stalled. Deterministic, no timing. It keeps the scenario's load honest.
- `TheLoad_TicksAndReadsWithinBudget` (`[Category("Perf")]`, `[Explicit]`, so it never fails a
  normal `dotnet test`): advances 1,200 frames at 1/60 s with the sampler on, and each frame reads
  every golem's `Mood` once and reconciles the alerts strip once (what the nodes do), in both
  `flowing` and `starved`. Prints the sections' p50, p95 and max. Asserts p95 of tick plus
  presentation ≤ 6 ms, as a coarse alarm for an order-of-magnitude regression.

Run it with `dotnet test godot/GolemFactory.sln --filter Category=Perf`.

A Core benchmark is sensible because the suspected hot spots (`StallFactsProbe`, the feeder walk,
the belts' tick) are all Core and engine-free; it measures them without rendering noise.

### 5.2 If it fails

In this order, re-running `perf` after each:

1. **Read the sections.** Name the top Core section and the renderer share (`no-draw`).
2. **If `probe.read` or `mood` dominates `starved`:** make the probe cheaper without changing a
   verdict. Candidates: read each golem's facts once per frame (a frame cache in
   `StallFactsProbe`, cleared on `BeginFrame`), and build the push-into and take-from indices
   (store → golems) once per frame instead of scanning `Others` per read. `StallTriageTests` must
   pass unchanged.
3. **If the renderer dominates:** belt cargo already draws in one `_Draw` per run; check badges and
   tile overlays, and the y-sorted node count.
4. **If it still fails** with the probe no longer the top Core section, apply
   `tuning-data-spec.md` §10.2: remove stage 4's Aether Conduit line, rebuild the load at the
   smaller count (~91 golems [M]), and re-run.

## 6. The report

*Fill one copy per run. Leave a row blank rather than estimate.*

```
Date:               Commit:                  Machine (CPU, GPU, RAM):
Godot:              Run: windowed / headless  Load: golems ___  belt items ___  boilers ___
```

### 6.1 Frame time (ms)

| Phase | Frames | p50 | p95 | max | Pass (p95 ≤ 16.6, max ≤ 33.3) |
|---|---|---|---|---|---|
| `flowing-1x` | | | | | |
| `flowing-4x` | | | | | |
| `starved` | | | | | |
| `hover` | | | | | |
| `no-draw` | | | | | n/a |

### 6.2 Simulation tick (ms)

| Phase | Ticks | p50 | p95 | max | `tick.golems` p95 | `tick.belts` p95 | `tick.steam` p95 |
|---|---|---|---|---|---|---|---|
| `flowing-1x` | | | | | | | |
| `flowing-4x` | | | | | | | |
| `starved` | | | | | | | |

### 6.3 Core hot spots, per frame

| Phase | `mood` calls | `mood` ms p95 | `probe.read` calls | `probe.read` ms p95 | `loop.walk` golems visited p95 | `strip` ms p95 | `hover` ms p95 |
|---|---|---|---|---|---|---|---|
| `flowing-1x` | | | | | | | |
| `starved` | | | | | | | |
| `hover` | | | | | | | |

### 6.4 Renderer

| Phase | Render CPU p95 | Render GPU p95 | Draw calls | Objects | Nodes |
|---|---|---|---|---|---|
| `flowing-1x` | | | | | |
| `no-draw` | | | | | |
| Difference (world drawing) | | | | | |

### 6.5 The Core benchmark (`--filter Category=Perf`)

| Phase | Tick + presentation p50 | p95 | max |
|---|---|---|---|
| `flowing` | | | |
| `starved` | | | |

### 6.6 Verdict

```
Budget held at 96 golems / 500 belt items:  yes / no
Top Core section:            Renderer share:
Action taken (§5.2 step):    Re-run result:
Stage 4 Conduit trimmed:     yes / no  (tuning-data-spec.md §10.2)
```

## 7. Tests and scenarios, summarised

| Kind | Name | What |
|---|---|---|
| Scenario | `perf` | §4 |
| Core | `EndgameLoadTests.TheLoad_IsWhatItSays` | §5.1, always |
| Core | `EndgameLoadTests.TheLoad_TicksAndReadsWithinBudget` | §5.1, explicit |
| Core | `PerfSamplerTests` | a null sampler measures nothing; sections count and time; percentiles of a known series |
| Core | `PlaytestSessionTests` | the Frame rate and Time per chapter sections (§5) |

## 8. Measurements, as built

Empty until the first run. Record each run's §6 here, newest first, with the commit.
