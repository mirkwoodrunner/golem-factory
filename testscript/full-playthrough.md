# Golem Factory — Phase 3 playtest: the full playthrough

**Runnable now: `docs/tuning-data-spec.md` §16 is built.** This judges Phase 3
(`docs/roadmap.md`): a complete run of chapters 1–10, to the Clock Tower's Foundation, **without
the playtest kit (F9)**, with no step anyone stalls on and the frame rate holding at the end.

**How it relates to the other scripts.** `testscript/phase-2-playtest.md` times a new player
through chapters 1–4; `testscript/phase-1-playtest.md` judges crates and the pack and allows F9.
This one plays the whole guide for real. Its start, keys and report location are the Phase 1
script's. Run it after the Phase 2 script, with a different player or the same one: chapters 1–4
are timed again here, but only the Phase 2 script's first-time player counts for Phase 2's target.

---

## Who and how

1. **One player, any experience.** A player who has done the Phase 2 script is fine. Tell them:
   "Follow the guide to the end. Think aloud if you like. Don't press F9."
2. **A fresh game.** Choose New game on the title; the player's own saves stay in their slots.
   Playtest mode on (`"playtest": true` in `godot/data/sandbox.json`), so the
   report writes.
3. **No F9, anywhere.** A kit use voids the timing of that chapter and every chapter after it; the
   report's "Playtest kit" section lists each use. The notes still count.
4. **Breaks are allowed between chapters.** Save into a slot (Tab, Save / Load), quit, and resume
   later with Continue or Load on the title. Each launch writes its own report (the previous one is archived as
   `playtest-report-<date>-<time>.md`), so a run in three sittings leaves three reports. Note the
   sitting's chapters on each.
5. **Don't help.** If asked, note the question and the step; answer only after **two full
   minutes** stuck, and mark the step **helped** in the notes. A helped step is a stuck step.
6. **Stop** when the guide reads "The Foundation is laid" (step `done`), or at three and a half
   hours of play, whichever comes first. Note where they stopped.

## The timing

The report's "Time on each step" table records each step's real seconds. Phase 3 adds a
**"Time per chapter"** table (chapter, time, budget) and a **"Chapters 1–10"** line, and a
**"Frame rate"** table (`docs/performance-report-template.md` §5). For a run in several sittings,
add the sittings' chapter times by hand in the notes.

| Ch. | Budget | What to watch |
|---|---|---|
| 1 | 5:00 | `coal`: the Coal cart costs 80 Scrap, from the starting Scrap cart. Did they find the marked spot? |
| 2 | 4:30 | The bench: the Presser now costs 10 Iron Plate + 5 Gear. How long did they crank? |
| 3 | 3:30 | `pipes2`; `coal-order`, the Coal cart's Rate rung ([I] at the cart); the coker grey while it waits |
| 4 | 2:00 | `turn`, `stall` |
| 5 | 20:00 | The Hauler's price (20 Iron Plate + 10 Gear + 30 Coke, `tuning-data-spec.md` §18 correction 10): how did they make it? `work-smelter` (a full Slag slot stops it) |
| 6 | 15:00 | `belts` pointing the right way; `copper-depot`'s label |
| 7 | 10:00 | `expand`, `bays`, `pack`: did they find the Assembly Line tab's rows? |
| 8 | 45:00 | Goal steps with no marks: did they know what to build? Brass's two ingots in one golem; Glass from Slag; Lens at 3 Glass |
| 9 | 30:00 | Collecting the Zeppelin's price; `launch` facing away from the depot |
| 10 | 45:00 | Building an Overclocker (the guide only mentions it); the Frame Section line; `stage1` at whatever rate they reach |
| 1–10 | **3:00:00** | |

**Passes when:** the player reaches `done` with no kit use, **no step is stuck** (below), and the
frame-rate table's last chapter meets the budget (p95 at or under 16.6 ms). Over a chapter's budget
alone is not a failure: note which step took the time, and whether it was the game (a price, a
rate, a wait) or the guide (a text, a mark). A chapter over budget by production alone goes to
`docs/tuning-data-spec.md`; a chapter over by confusion goes to the guide.

## Stuck-step signals

A step is **stuck** if any of these happens on it. For each, write the step id, the clock time from
the report, what the player said, and what was on screen (a screenshot with the OS's tool, Win+Shift+S).

| # | Signal | How to spot it | Severity |
|---|---|---|---|
| 1 | **The guide doesn't advance** after the player did what it asked | They say "I did that"; the step's counter is full or the thing is built | Highest: a `Done` check that reads the wrong state. Note exactly what they built and where |
| 2 | **Helped** | You answered after two minutes | High |
| 3 | **A step over 3× its chapter's median step time**, or **over 5:00** on a step that makes nothing (claim, place, program, turn, save) | The report's step table | High |
| 4 | **A red golem nobody noticed for over a minute** | Count red golems at each chapter's end; ask which they had seen | Medium |
| 5 | **The same guide text read three or more times** | They scroll back, re-read aloud, or hover F1 | Medium |
| 6 | **A price they couldn't pay** that the guide asked for | "Upgrade your pack" or "Requires:" on something a step names | High: invariant 1 or 3 of the tuning spec |
| 7 | **The market as a toll booth** | They stand at carts pressing E for over a minute at a time, or say so. Count the carts they add and the rungs they buy in chapters 8–10 | Medium: supply-design.md §3 |
| 8 | **The tower stalls** | The `stage1` headline reads "frozen" or "starved" for over a minute while they feed it | High: tuning spec §10.1 |
| 9 | **Boilers dry** | A golem blue for steam for over a minute; the coal line can't keep up | Medium: tuning spec §3 |
| 10 | **A crash, a hang, or an error** | The report's "Errors the game logged" | Highest |

## The frame rate

The report's **Frame rate** section records, per chapter, the frame time's median, 95th percentile
and worst, with the golems and belt items standing at the chapter's end
(`docs/performance-report-template.md` §5). Nothing to do but leave playtest mode on.

As a cross-check, run the game from a console with Godot's FPS print:

```sh
G=".../Godot_v4.7.2-stable_mono_win64_console.exe"
"$G" --path godot --print-fps
```

It prints `Project FPS: 60 (16.66 mspf)` each second. At each chapter's end, note one line and the
count of golems (the HUD's alerts strip, or Tab → Inventory). Note any visible stutter, and when.

**At the end** (step `done`, or where they stopped), leave the game running for one minute without
input and note the console's lowest FPS in that minute. That is the run's endgame number; the
`perf` scenario measures the designed endgame (96 golems) separately.

## The questions

Asked as **PLAYTEST · your call** cards (`PlaytestSession`), at the step named. New in Phase 3;
the existing questions stay where they are.

| Id | Asked at step | Question | Answers |
|---|---|---|---|
| `coal-share` | `copper-ingot` | How much of your factory was feeding the boilers? | About right · Too much · Hardly noticed it |
| `carts-late` | `post` | Keeping your carts stocked and upgraded: | Fine · A chore · I ran out a lot |
| `lens-glass` | `aether-cell` | Was there enough Glass for your Lenses? | Plenty · Just enough · Never enough |
| `overclocker` | `done` | Did you use Repeat on the Overclocker? | Often · Tried it · Didn't know it existed |
| `stage-rate` | `done` | How fast did the Foundation build at your rate? | Fair · Slow · It kept freezing |
| `whole-run` | `done` | Which chapter dragged? | None · 5 · 6 · 7 · 8 · 9 · 10 |

The ids are the questions' saved names, like a step's `Id`; don't rename one. `carts-late`
replaced `market-late` when supply carts replaced truckloads (docs/supply-design.md), with a new
id so a save that answered the old question is asked the new one; chapter 3's `market` became
`carts` the same way.

## After the run

1. Collect every `playtest-report*.md` from the user folder.
2. Fill the summary below and attach it to the reports.
3. File each stuck step against `docs/tuning-data-spec.md` (a number) or
   `docs/guide-chapters-design.md` (a text or a mark), and each frame-rate problem against
   `docs/performance-report-template.md`.

```
Player:              Date:            Sittings:
Reached step:        Kit used: yes/no (where)
Chapters 1–10: h:mm:ss  (target 3:00:00)

Ch | time  | budget | over by | stuck steps (signal #) | notes
1  |       | 5:00   |         |                        |
2  |       | 4:30   |         |                        |
3  |       | 3:30   |         |                        |
4  |       | 2:00   |         |                        |
5  |       | 20:00  |         |                        |
6  |       | 15:00  |         |                        |
7  |       | 10:00  |         |                        |
8  |       | 45:00  |         |                        |
9  |       | 30:00  |         |                        |
10 |       | 45:00  |         |                        |

Frame rate at the end:  p50 ___ ms   p95 ___ ms   worst ___ ms   golems ___   belt items ___
Lowest FPS in the idle minute: ___
Carts on the street at the end: ___   Rungs bought: ___
```

## Numbers to judge

| Number | Value | Lives in |
|---|---|---|
| Steam | 3 Coke a minute per working golem, 8 golems per boiler | `SteamNetwork` |
| Supply carts | One Scrap cart to start (120 a minute, holds 200); Coal 80 Scrap; Copper Ore 60 Iron Plate + 20 Gear; each with Rate and Capacity rungs | `sandbox.json` `supply` (supply-design.md §3) |
| Brass Presser | 60 Scrap + 10 Iron Plate + 5 Gear | `chassis.json` |
| Aether-Hauler | 20 Iron Plate + 10 Gear + 30 Coke | `chassis.json` |
| Lens | 3 Glass + 1 Brass | `recipes.json` R10 |
| Late prices | Overclocker + 120 Iron Plate, Zeppelin + 150, Freight Mast + 120: past a base pack of 100, so the pack upgrade matters; Post crate + 40 | `chassis.json`, `placeables.json` |
| Stage 1 | 6 Frame Sections a minute for 6:00 | `clock_tower_stages.json` |
| Repeat | up to the recipe's limit (11 for Coking, 5 for Gears) | `Golems/RepeatRules` |

Every other number is in `docs/tuning-data-spec.md`, with where it lives and what pins it.
