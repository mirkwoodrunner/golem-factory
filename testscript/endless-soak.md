# Golem Factory: endless play soak

A long session of endless play (`docs/endless-design.md`), watched for what only time shows: memory
or handles that climb, a frame rate that sinks, a save that grows while the factory doesn't, and
whether the work after the tower still holds a player. Adapted from Claude Code Game Studios'
soak-test protocol (MIT).

**How it relates to the other scripts.** `full-playthrough.md` ends at the Foundation (stage 1).
This one starts where the campaign ends, at a won tower, and plays on. It allows F9 (the kit stops
at stage 1 anyway) and needs no new player.

---

## Before the first run

- **A save at the won tower.** Stage 4 complete, the City Office on its generated contracts, the
  civic works open. A full playthrough continued past stage 4 gives one. Copy it aside before the
  soak (`%APPDATA%\Godot\app_userdata\Golem Factory\saves\<slot>.json`): the soak plays it on.
- **No such save yet?** There's no shortcut to one: the kit stops at stage 1 and Creative has no
  contracts or civic works. Run the stability half on the latest save you have, and mark the rows
  that need endless play *not assessed*.
- **An idle machine.** Close games and renders. The Phase 3 perf run read twice its real numbers
  with Satisfactory open (`docs/roadmap.md`).
- **Playtest mode on** (`"playtest": true` in `godot/data/sandbox.json`), so the report's frame
  rate and "Errors the game logged" sections write. A launch archives the previous report.

## Start

From the repository root, in PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File Tools\Soak\soak-sample.ps1
```

It launches the windowed mono build with `--print-fps` into a log, prints the folder it writes, and
adds a row to `samples.csv` every minute until the game closes: minutes, working set and private
memory (MB), handles, threads, the lowest and mean frame rate since the last row, and the newest
save's size. It only reads; your saves and settings are the game's as usual.

1. On the title, **Continue** (or Load the won-tower save).
2. Note the log's V-Sync line (`godot.log`, near the top): the frame rate is capped at the
   monitor's refresh, so record that rate as the ceiling.
3. Play normally: take contracts, build civic stages, extend the factory toward the cap of 112
   golems. Save into a slot at each checkpoint.

**Length: two hours.** Checkpoints at T+0, 20, 40, 60, 80, 100 and 120 minutes. At T+60, also
quit to the title and load the slot you just saved (below).

## At each checkpoint

Copy the CSV's latest row into the table, then look at the game.

| T+ | private MB | handles | fps min / mean | save KB | golems | contract (Endless_k) | civic work · stage | contracts done | goods made |
|---|---|---|---|---|---|---|---|---|---|
| 0 | | | | | | | | | |
| 20 | | | | | | | | | |
| 40 | | | | | | | | | |
| 60 | | | | | | | | | |
| 80 | | | | | | | | | |
| 100 | | | | | | | | | |
| 120 | | | | | | | | | |

The contract is on the notice at the City Office; the civic work on the tower's HUD panel. On the
overview (Tab, then Overview): the golem count on the map's line ("112 golems · ... working"), and
goods made and contracts done in the running totals under the map.

**Still working?** Yes or no each time, with what you saw for any no:

- No crash, freeze or long hitch since the last checkpoint.
- Sound still plays: the claim chime on a contract, the ambient loops.
- The HUD, the alerts, the hover card and the Crate screen still draw and read right.
- No golem red that you didn't cause; grey only where something waits.
- The office's shelf: is anything waiting there, and could you collect it?

**Still playing?** A line each:

- What you were doing, and whether it still felt worth doing.
- Any "seen this before" since the last checkpoint: a contract, a request, a line.
- Any frustration, and its cause. Any moment you were pleased, and its cause.
- What you did with the last contract's reward. (Endless rewards may have nowhere left to go:
  the design audit's open question.)

## T+60: save and load

Save into a slot, quit to the title, load it. Check that the contract on the notice, its delivered
count, the civic stage's progress, the shelf, the running totals and the golem count are exactly
what they were. Note how long the load took. A difference is a finding (endless invariant 9: saves
restore every counter exactly).

## Reading it

Read every number against this run's own early rows. A soak looks for growth, not for a size.

| Signal | What it suggests |
|---|---|
| Private memory still rising at T+60 and after, beyond about 20% over T+20 | A leak. Note what was on screen as it climbed. |
| Handles or threads climbing from checkpoint to checkpoint | A leak of files, timers or nodes. |
| fps min falling while the factory didn't grow | Something costs more over time, not per golem. |
| Save KB growing while the buildings and golems didn't | Something accumulates in the save: the alerts, the shelf, a counter. |
| A load that differs from the save | A counter that doesn't round-trip. |
| "Still playing" lines going flat | Endless play running out of reasons, and when. |

The playtest report's "Frame rate" table gives p50, p95 and worst frame for the chapter the guide
is in (after the guide ends, its last chapter): compare p95 with the 16.6 ms budget
(`docs/performance-report-template.md` §5). "Errors the game logged" lists anything Godot reported.

## Verdict

- **PASS:** two hours, every "still working" a yes, no signal above, the load exact.
- **PASS WITH CONCERNS:** minor drift or fatigue; say which and when it started.
- **NOT ASSESSED:** the run ended before T+120 for a reason other than a fault, or started from a
  save short of the won tower. Say how far it got and which checkpoints were never reached. A short
  soak can't pass: slow leaks and fatigue don't show early.
- **FAIL:** a crash, a hang, a confirmed leak, a load that differs, or play that stopped being
  worth doing. A crash at T+20 is a FAIL however short the run.

Keep the soak's folder (`samples.csv`, `godot.log`) and the playtest report together, and write
the verdict, the table and the notes beside them. Bugs and design notes go to `docs/open-items.md`.
