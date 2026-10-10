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

- **Round 3 of the marked-tile plan**: plan the later chapters' tiles around where the player
  actually built, instead of fixed offsets from the stalls. Large (most of chapters 3-9). Only
  worth it if the playtest says the marks get in the way. Rounds 1 and 2 are built: pipe steps
  accept any working route, and the guide says what is in the way of a marked tile.
- **Rename "Extract Scrap"**: it takes whatever the stall behind holds (Coal, in chapter 3).
  The JSON is hand-edited now, so this is a data edit plus every guide step and test that names
  the card. **Saves store cards by name**: map the old name, or old saves lose the card.
- **Whether the Overclocker has an identity** beyond more slots: one exclusive card (`Repeat`)
  and the same dials.

## 3. Known gaps

- **Old saves resume the guide a few steps off.** Saves from before the guide stored its step by
  id (#49), or its golems' roles (#52), fall back to the step's index and to build order. A fresh
  game is unaffected.
- **A Clock Tower placed by hand in a save from before #47 is not rebuilt.** The tower is a
  fixture in the town square now; such a save restarts it at stage 1.
- **After a load mid-stage, the tower's starved alarm can show briefly** until the first
  delivery lands, because supply meters aren't saved. Without that, a line that broke since the
  save would never raise the alarm.
- **`godot/art/clock_tower.png`** is unused since the staged art. Delete it.

## 4. Polish, none of it blocking

- **The art is placeholder** throughout, and the Workbench's lever housing, display face and
  card faces most of all.
- **The depot label cycle** grows with the factory; twenty goods is twenty presses of `E`. A
  picker panel is the answer if it bites.
- **`Straining`**, the only predictive mood (9 of 12 units held): useful warning, or noise?

## 5. Deliberate cuts

- No player collision; the player walks through buildings and golems.
- A belt hands off only to another belt: getting goods into a depot needs a golem.
- `Refine` stays id-routed rather than spatial, so an untyped take can't grab the wrong input.
- **Demolition refunds in full, and patents are free.** Settled design calls, not cuts; see
  `CLAUDE.md`.
