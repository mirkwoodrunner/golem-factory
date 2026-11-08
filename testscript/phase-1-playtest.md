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
| **E** | Interact: **tap** to take from a cart, add a cart on a roped-off spot, fuel a boiler, collect from a crate (a depot or a Freight Mast: you take what fits, the rest stays), or program a golem. **Hold** at a stall to keep harvesting, or at the Hand-Crank Bench to crank. |
| **I** | Your pack. Standing at a crate, the crate's screen instead: crate and pack side by side. Click a good to move one, Shift-click to move what fits. The crate's label is set here. **I**, **E** or **Escape** closes it. |
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
| 1 | Scrap, Coal, Coke and Iron by hand; a boiler; your first golem; **collecting from its crate** | A, B, E |
| 2 | Gears; claim a recipe; a Brass Presser making Iron Plate from the first crate | C |
| 3 | Coking automated, and two boilers joined | D, E |
| 4 | Patents; turning a golem with R; a stall on purpose | C |
| 5 | The Aether-Hauler, Iron Smelting, and its Slag; **feeding the smelter's crate by hand** | F |
| 6 | Belts and a labelled depot at the Copper stall (label set on the crate screen) | F, I2 |
| 7 | Floor Expansion, the bay upgrade, **the pack upgrade**, the Ledger, save and load | F, G, H, I4 |
| 8 | Goal steps: every good the Zeppelin costs; **the Pneumatic Post** | F, G |
| 9 | The Zeppelin, its Freight Launch card, a Freight Mast, and **unloading the mast's crate** | G |
| 10 | The Clock Tower in the town square: Frame Sections, and its Foundation | (new) |

---

## Crates and the pack: what this playtest is for

Phase 1 (`docs/crates-design.md`) made every depot a **crate** of its own, holding 50 of each good,
and gave you a **pack** that carries 100 of each. Golems reach only the crates beside them; your
building, claiming and ordering spend from the pack. The guide walks you through it; these are the
moments to watch.

- **Collecting (E at a crate).** You take what fits, and the rest stays. Try it with a nearly full
  pack: the crate should keep what didn't fit, and nothing should vanish.
- **A full pack.** At 100 of a good, a stall's prompt goes grey and says "pack full". Did you
  understand what to do?
- **Crates filling and emptying.** A crate stops at 50. A golem pushing into a full crate stops
  with "Crate(x,y) full"; one hauling from an empty crate stops with "no input". Watch the
  Presser's output crate in chapter 2 and the smelter's in chapter 5.
- **Feeding a converter by hand (chapter 5).** Press **I** at the smelter's input depot and
  Shift-click Scrap, then Coke, in your pack to move all that fits. A plain click moves one. Is the
  screen obvious, and did Shift-click do what your hands expected?
- **The label picker (chapter 6).** On the crate screen, click the label and pick Copper Ore. A
  labelled crate refuses other goods from your pack, and still lets you take out what was already
  in it.
- **Demolishing a full crate (X).** Its cost and its contents come back into your pack. With your
  pack too full, the wrecking bar refuses and says "pack full of ___", even with room in the Post:
  nothing is torn down. Try both.
- **The mast's crate (chapter 9).** The Zeppelin's freight lands in the mast's own crate, not your
  pack. Collect it with E, or stand a golem beside the mast.
- **The Pneumatic Post (chapter 8).** Every Post crate opens onto one shared store. A golem pushing
  into one Post crate fills every other, and your building spends from the Post once your pack
  runs short. Does it take a chore off you?
- **The pack upgrade (chapter 7).** On Management's Assembly Line tab, the Pack row's **Upgrade**
  (60 Iron Plate + 30 Gear) makes the pack carry 150 of each; later rungs make it 200 and 250.
  The Pack screen's cap line should change at once. A rung priced in a good you haven't made yet
  reads `Requires: …`, greyed.
- **Floor Expansion's prices (chapter 7 onwards).** The first extension is 80 Scrap + 40 Iron
  Plate; the second is 120 Scrap + 60 Iron Plate + 30 Gear, more Scrap than a 100 pack holds, so
  it says `Upgrade your pack: this needs 120 Scrap, your pack holds 100` until you upgrade (or
  the Post holds the rest). Later ones ask Copper Ingot, Brass, Casing, Glass, Lens, Mainsprings
  and Aether Cells. Try an extension before upgrading: did the message tell you what to do? Were
  the later ones worth it?
- **An old save.** Load a save from before crates: everything you had is in your pack (even above
  100), every crate starts empty, and the load says so once.

**Done when** (Phase 1, `docs/roadmap.md`): **a badly laid-out factory runs visibly worse than a
good one, and belts get built because a line needs them.** Please judge both in your notes. Did
you ever move a building to shorten a walk? Did a line stall because its crate was far from where
its goods were made? Did you build a belt the guide hadn't asked for?

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
  factory? Each stage wants a steady **rate** of goods; a pile you kept counts too, but no faster
  than three times the rate.
- **Demolish (X)** refunds the full cost, plus what the building held, a crate's goods included.
  That is settled, not a tuning question. Did moving things around ever feel punishing anyway?

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
| Supply carts | a Coal cart 80 Scrap · 30 a minute, holds 100 · the starting Scrap cart 120 a minute, holds 200 |
| Cart upgrades | Rate and Capacity rungs, each priced in the next tier's goods (supply-design.md §3) |
| Slag Heap | 1 Coke per 4 Slag |
| Assembly Line card price | 4× the recipe's own inputs, decaying while it waits |
| Golem bays | 10, then +6 per upgrade |
| Floor expansion | +2 rows, six purchases, from Scrap and Plate up to Casing and Mainspring |
| Freight launch | a flat 24 ticks, however far |
| A golem's "hold nearly full" warning | 9 of 12 units |
| A crate (depot or mast) | 50 of each good |
| Your pack | 100, then 150, 200, 250 by upgrade |
| The Pneumatic Post's shared store | 200 of each good |
| A Post crate | 10 Brass + 5 Casing |

## The questions

The guide asks these as **PLAYTEST · your call** cards (`PlaytestSession`), each at the step named,
just after you have lived through what it asks about. New in Phase 1 are marked.

| Asked at step | Question | New |
|---|---|---|
| `gears` | Collecting from the crate with E: did taking what fits feel easy, or like a chore? (Easy · Fine · A chore) | yes |
| `coking-card` | Did you fill your pack to 100 of something? Did "pack full" tell you what to do? (Never filled it · Filled it and knew · Filled it and was stuck) | yes |
| `slag-heap` | You fed the smelter's crate by hand on the crate screen. Was it clear? (Clear · Fiddly · I couldn't find it) | yes |
| `copper` | Did a golem stop because its crate was full or empty? Did its badge say why? (Never saw it · Yes, clearly · Yes, but I was confused) | yes |
| `unloader` | You labelled a depot on the crate screen. Did the picker work? (Good · Fiddly · Couldn't find it) | reworded (was the E-cycle) |
| `expand` | Has where you put things started to matter: walks between crates, or a line that needed a belt? (It matters · A little · Not yet) | yes |
| `ledger` | The pack upgrade: did you need the room, and was it easy to find? (Needed it · Nice to have · Didn't notice it) | yes |
| `glass` | The Pneumatic Post: did it take a chore off you, or add one? (Took one off · No difference · Added one) | yes |
| `tower-visit` | The Copper landed in the mast's crate. Did you find it? (Straight away · Eventually · No) | yes |
| `frame-section` | Later extensions cost Brass, Casing, Glass and Mainsprings. Was spending them on room worth it? (Worth it · Too dear · Never got that far) | yes |

The existing questions stay where they are.
