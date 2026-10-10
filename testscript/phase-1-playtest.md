# Golem Factory — Playtest How-To

**The script is in the game now.** The step-by-step guide walks the whole run this file used to
spell out, and in **playtest mode** it asks its questions as you reach the moments they are
about. Your answers, how long each step took, which chapters you skipped, and every error the
game logged go into a report file. This page only tells you how to start, what the keys are,
and what to look at that the guide does not reach.

---

## Start

1. Open `godot/project.godot` in **Godot 4.7.2 mono** and press **F5**, or from a shell:
   ```sh
   "G:/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64/Godot_v4.7.2-stable_mono_win64_console.exe" --path godot
   ```
2. Playtest mode is on while `"playtest": true` is set in `godot/data/sandbox.json`, as it is on
   this branch. The guide's header then ends in `F9 skips`.
3. Play. Answer the cards titled **PLAYTEST · your call** as they come up. Add a note whenever you
   have one: notes are the most useful part of the report.

**The report** is `user://playtest-report.md`, which on Windows is
`%APPDATA%\Godot\app_userdata\Golem Factory\playtest-report.md`. It is rewritten as you go, so
quitting at any point still leaves it complete up to there. **Playing over several sittings is
fine:** each launch keeps the previous session's report as `playtest-report-<date>-<time>.md` in
the same folder before starting a new one. Send all of them.

---

## Keys

| Key | Does |
|---|---|
| **WASD** | Move |
| **E** | Interact: **tap** to harvest a stall, order a truckload from an empty one, fuel a boiler, label a depot, or program a golem. **Hold** at the Hand-Crank Bench to crank. |
| **R** | Turn the build ghost while holding a placeable, or the golem you are carrying. Otherwise, cycle the bench's recipe, or turn the golem you are standing by. |
| **G** | Pick up the golem you stand by; press again to set it down. While you carry it, an outline on the floor shows the tile it will land on (red where it can't), with the tiles it will take from and push to. |
| **1–9, 0, -, =** | Pick a build-menu tile. **X** is Demolish. Hover a tile for its cost. |
| **Left click / drag** | Place a building; belts and pipes lay a run as you drag. |
| **Escape / right click** | Put the placeable down. |
| **Tab** | Management: Inventory · Assembly Line · Patents · Save/Load · Ledger |
| **F1** | Hide or show the guide. |
| **F9** | **Playtest kit:** fast-forward the current guide chapter by doing it for you. |

---

## The chapters, and skipping them

**F9** completes the current chapter with the player's own verbs: it builds the golems, lays
the depots and pipes, and programs them, so you land on the ground a player who did the chapter
would stand on. Use it to reach a later chapter quickly, or to get past one you are stuck on.
The report records every chapter you skipped, so skipped and played time are never confused.
Chapter 7's Save and Load, done by F9, use their own file (`kit-save.json`), never your save.

| Ch. | Guide chapter | Old script part |
|---|---|---|
| 1 | Scrap, Coal, Coke and Iron by hand; a boiler; your first golem | A, B, E |
| 2 | Gears; claim a recipe; a Brass Presser making Iron Plate | C |
| 3 | Coking automated, and two boilers joined | D, E |
| 4 | Patents; turning a golem with R; a stall on purpose | C |
| 5 | The Aether-Hauler, Iron Smelting, and its Slag | F |
| 6 | Belts and a labelled depot at the Copper stall | F, I2 |
| 7 | Floor Expansion, the bay upgrade, the Ledger, save and load | F, G, H, I4 |
| 8 | Goal steps: every good the Zeppelin costs | F, G |
| 9 | The Zeppelin, its Freight Launch card, and a Freight Mast | G |
| 10 | The Clock Tower in the town square: Frame Sections, and its Foundation | (new) |

---

## Off the guide: please also look at these

The guide never sends you to these. Each one is a judgement call that only a person can make.

- **Golem moods.** A working golem shows **nothing**, which is deliberate: forty "working"
  badges would make a factory unreadable. A golem out of steam shows a **blue** badge, not a
  red one. Does a field of blue read as one fault with one fix? Does a golem that is only
  waiting for input look too alarming in red?
- **The Scrap Recycler** (build menu). Anything goes in, Scrap comes out, and it burns Coke.
  It disposes of Slag at half the Slag Heap's rate, giving a Scrap for the difference. Is that
  a real choice, or is one of them obviously right?
- **The belt splitter.** Split one belt into two lines. Does it do what you expected?
- **The workshop.** Look at the hearth and shelves on the back wall. Do they read as somebody's
  workshop? Is anything standing where you wanted to build?
- **The Clock Tower** waits in the town square south of the street, roped off until you build a
  Zeppelin, and its picture grows with every stage you complete. Is it a goal worth the whole
  factory? Each stage wants a steady **rate** of freshly made goods, not a pile of them.
- **Demolish (X)** refunds the full cost, plus what the building held. That is settled, not a
  tuning question. Did moving things around ever feel punishing anyway?

---

## Known gaps: please don't report these

- **A belt hands off only to another belt.** Getting goods into a depot needs a golem doing
  Push at the end of the run. This is deliberate.
- **No player collision.** You walk through everything.
- **The art is placeholder** throughout.

---

## Numbers to judge

Every number here was set by a developer, and most have never been felt. If one felt wrong, say
which way in a note, or add it under the matching question in the report.

| Number | Value |
|---|---|
| Coke burned per **working** golem | 3/min (an idle golem burns none) |
| Truckload prices | Scrap free · Coal 10 · Copper/Zinc 20 · Aether 40 Scrap |
| Truckload size and delay | 30–40 units · 40–120 ticks |
| Slag Heap | 1 Coke per 4 Slag |
| Assembly Line card price | 4× the recipe's own inputs, decaying while it waits |
| Golem bays | 10, then +6 per upgrade |
| Floor expansion | +2 rows, rising cost, capped at +12 |
| Freight launch | a flat 24 ticks, however far |
| A golem's "hold nearly full" warning | 9 of 12 units |
