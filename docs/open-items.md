# Golem Factory: open items

The backlog for the Godot game. The current state and what's next are in
`docs/godot-conversion-plan.md`, under "Next session: start here". The Unity-era backlog, with
the reasoning behind every item it closed, is `docs/history/open-items-unity.md`.

Everything the progression design specifies is built (`docs/progression-design.md`), and no node
on the Ledger is flagged as planned. What is left needs a person, a decision, or is polish.

## 1. Needs a person in front of the running game

`testscript/phase-1-playtest.md` runs it; the guide asks the questions and writes the report.

| What | Why only play can answer it |
|---|---|
| **The boiler fuel ratio** | 3 Coke/min per working golem. progression-design §12: "mis-tuned, this becomes a coal simulator." |
| **The manual era** | Budgeted at 12-15 minutes. The guide's step timings now measure it. |
| **Whether the Workbench still decides anything** | Recipes and chassis fix most of a program; the Haul good and batch dials are what's left to choose. |
| **Whether the market is an economy or a toll booth** | Truckloads are priced and arrive in bursts. |
| **Marked tiles** | The guide marks where to build. Do they help, or get in the way? Decides round 3 (below). |
| **The carry outline** | Is it clear where a carried golem will land? |
| **Golems waiting for input wear the red stall badge** | Waiting is normal in a running factory; the badge may be too loud. |
| **Every invented number** | Listed at the end of the playtest script. None has been felt. |

## 2. Decisions waiting on the playtest

- **Round 3 of the marked-tile plan** is merged (#54): the later chapters' tiles follow where
  the player actually built, instead of fixed offsets from the stalls. Following the marks plays
  the same either way. Rounds 1 and 2 are merged too: pipe steps accept any working route, and
  the guide says what is in the way of a marked tile.
- **Crates** (`docs/crates-design.md`): every depot is a window on one unlimited stockpile, so a
  golem anywhere reaches every good and nothing downstream ever fills. The design makes crates
  local and gives the player a pack. Its §5 questions are answered (2026-10-18), so it is ready
  to build, starting with its §9 steps 1–3. The
  boiler half was fixed on its own: a boiler holds at most 240 Coke (`SteamBoiler.CokeCapacity`).
- **Whether the Overclocker has an identity** beyond more slots: one exclusive card (`Repeat`)
  and the same dials. Decided after a full playthrough (`docs/roadmap.md`, decision 7).

## 3. Known gaps

- **Old saves resume the guide a few steps off.** Saves from before the guide stored its step by
  id (#49), or its golems' roles (#52), fall back to the step's index and to build order. A fresh
  game is unaffected.
- **A Clock Tower placed by hand in a save from before #47 is not rebuilt.** The tower is a
  fixture in the town square now; such a save restarts it at stage 1.
- **After a load mid-stage, the tower's starved alarm can show briefly** until the first
  delivery lands, because supply meters aren't saved. Without that, a line that broke since the
  save would never raise the alarm.

## 4. Polish, none of it blocking

- **The art is placeholder** throughout, and the Workbench's lever housing, display face and
  card faces most of all.
- **The depot label cycle** grows with the factory; twenty goods is twenty presses of `E`. A
  picker panel is the answer if it bites.
- **`Straining`**, the only predictive mood (9 of 12 units held): kept, but moves to hover only
  (`docs/roadmap.md`, decision 6).

## 5. Deliberate cuts

- No player collision; the player walks through buildings and golems.
- A belt hands off only to another belt: getting goods into a depot needs a golem.
- `Refine` stays id-routed rather than spatial, so an untyped take can't grab the wrong input.
- **Demolition refunds in full, and patents are free.** Settled design calls, not cuts; see
  `CLAUDE.md`.
