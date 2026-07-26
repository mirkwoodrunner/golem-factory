# golem-factory

Prototyping an automation game that could be implemented as either a video game or board game.

You play an Artificer who builds and programs clockwork **golems** — rigid, automated
workers that harvest resources, refine them, and haul them around a workshop floor. You
don't control golems directly; you assemble a *program* for each one out of punch-card-style
parts, then set it loose to run on its own.

## Running it

Open the project in Unity Hub (**Add**, not **New** — `ProjectSettings/`/`Packages/` are
already committed) and open one of two scenes:

- **`Assets/_Project/Scenes/Main.unity`** — the main demo. Seven golems are already built
  and running an assembly line on their own; you walk in as the Artificer and can
  reprogram any of them.
- **`Assets/_Project/Scenes/Sandbox.unity`** — start from nothing: an empty floor with raw
  resource nodes, where you harvest, build a construction station, and construct + program
  your first golem yourself.

Hit **Play** in either.

## Controls

| Input | Action |
|---|---|
| `W` `A` `S` `D` | Move around (camera follows you automatically) |
| Mouse scroll | Zoom camera in/out |
| `E` | Interact with whatever's nearest in range — harvest a resource node, open a construction station, or open the Workbench on a golem |
| Left click | Place (or remove, if clicking an occupied tile) a building, in Build mode |
| `Tab` | Open/close the management menu (Inventory / Assembly Line / Patents / Save & Load) |

## Programming a golem (the Workbench)

Walk up to any golem and press `E` — this opens the **Workbench**, a drag-and-drop screen
for editing that golem's program:

- **Card Vault** (right side) holds your available parts: **teal cards** are Logic Cores
  (the trigger — *when* the golem acts, e.g. always-on or on a timer), **copper cards** are
  Appendages (the action — *what* it does, e.g. extract, haul, refine).
- Drag one Logic Core and up to a chassis-limited number of Appendages onto the slots on the
  left. Pick the golem's **chassis** (its body/capacity) from the row of buttons.
- Nothing you drag takes effect immediately — it's a draft. Click **Engage Gears** to commit
  it and boot the golem into the world (costs Focus). Click **Close** (top-right) to leave the
  Workbench without committing.
- **Patent** saves the current draft as a reusable blueprint (also costs Focus) — load it back
  onto any golem later from the Patents tab.

Golems execute their program every tick, strictly in order, forever. If a step's input is
empty or its output is full, the golem just **stalls** and retries — it never skips or
improvises. A stall shows up as a warning over the golem and in the alerts strip at the top
of the screen.

## The management menu (`Tab`)

- **Inventory** — what's in each storage buffer (Scrap, Brass, Aether, …).
- **Assembly Line** — a rotating selection of draftable parts that get cheaper the longer
  they sit unclaimed. Claim one with Scrap to add it to your Card Vault.
- **Patents** — blueprints you've patented. **Load** one into whichever golem's Workbench is
  currently open.
- **Save / Load** — save your current progress (buffers, Focus, patents, every golem's
  program) to disk, or load it back.

## Building (Sandbox scene)

In `Sandbox.unity`, walk up to a resource node and press `E` to harvest it by hand, or open
the build panel (bottom-left) to pick a placeable (a Depot, or another Golem Construction
Station) and left-click a floor tile to place it — each costs Scrap/Brass, shown next to its
name. Left-click an already-placed building to remove it. At a construction station, press
`E` and pick a chassis to spend resources building a brand-new golem, which gets handed
straight to the Workbench for programming.
