# Golem Factory: The Clockwork Metropolis

The game's core concept: what it is, the loop it is built around, and the mechanics every other
design doc builds on. The details, the numbers and the reasoning behind them live in the docs
listed at the end.

## Overview

- **Genre:** a solo factory-automation game in the vein of Factorio and Satisfactory, in cozy,
  top-down pixel art.
- **Theme:** a steampunk city. The player is an **Artificer** in a wood-and-brass workshop,
  building and programming clockwork **golems**.
- **Goal:** build the city's **Clock Tower** in four stages from a whole factory's steady output.
  After the tower, play goes on without end (`endless-design.md`).

## The core loop

1. Gather raw goods by hand from the market street, and craft the first parts at the Hand-Crank
   Bench.
2. Build a golem, program it at the Workbench, and set it loose. It out-works the player at once.
3. Chain golems into lines: raw goods in, crafted goods out, joined by crates, belts and pipes.
4. Feed every golem steam, and grow the coal line that makes it.
5. Unlock bigger chassis and longer recipes, and scale the lines up.
6. Deliver to the Clock Tower at a sustained rate, stage by stage.

## The world clock

Everything runs on one simulation clock at a fixed tick rate (10 ticks a second), with pause and
speed controls. Golems act on its ticks, never on frames, so a factory runs the same way every
time.

## Golems and the Artificer

- **Golems** do the rigid, automated work: hauling, refining, assembling and shipping. A golem
  runs its program's steps in strict order, forever. If a step can't run (its input is empty, its
  output is full, it has no steam), it stalls and retries that step. It never skips, reorders or
  improvises.
- **The Artificer** does the flexible work: building, programming, carrying goods and golems,
  claiming unlocks, filing patents, and filling the city's contracts.

## Programming a golem

A golem's program is a set of punch cards slotted into its frame at the Workbench. Nothing takes
effect until the player pulls the ENGAGE lever.

| Part | Role | What it does |
|---|---|---|
| Logic Core | The trigger | When the golem runs: always, or on an interval. |
| Appendages | The steps | What it does, in order: Haul a good, Extract from a cart, Assemble a recipe, Push its output. |
| Chassis | The constraint | How many steps the golem can hold, and so how many inputs a recipe can have. |

Golems are fixed to a tile and face one of four directions. A golem takes from the tile behind it
and gives to the tile in front.

## Economy

- **No money.** Every price and every reward is goods.
- **The market street:** raw goods (Scrap, Coal, Copper Ore, Zinc Ore, Aether) come from supply
  carts the player adds and upgrades (`supply-design.md`).
- **Crates, the pack and the Post:** each depot is a crate of its own, the player carries a capped
  pack, and the Pneumatic Post is the shared store (`crates-design.md`).
- **Steam:** every working golem burns a boiler's Coke, so each new golem has a running cost
  (`progression-design.md` §3).

## Progression

- **The Assembly Line** offers the cards the factory can afford: recipes, steps and chassis.
  Claiming one makes it available at the Workbench (`progression-design.md` §8).
- **The golem cap:** at most 112 golems stand at once, where the frame budget holds. Nothing
  buys more; steam upkeep is what each golem costs.
- **Chassis tiers:** five chassis, from the two-slot Clockwork Scavenger to the six-slot Zeppelin
  Freight Loader (`digital-design.md`).
- **Land:** Floor Expansion widens the workshop, and the Docks open a second region
  (`region-design.md`).

## Patents

A patent saves a golem's program as a named blueprint, to build again at the construction station
with the program already engaged. Patenting is free.

## The city

- **Contracts:** the City Office posts orders for goods, one at a time, with a bonus for finishing
  quickly and no deadline (`contracts-design.md`).
- **Selling golems:** the city also orders golems. The player fills an order by selling golems by
  hand or by building them in a Golem Foundry, and is paid in goods in proportion to the golems'
  complexity (`golem-sales-design.md`).

## The Clock Tower

Four stages, each asking for a sustained rate of the game's most advanced goods. Progress scales
with the delivered rate, so a bigger factory finishes faster, and a stage can't fail, only take
longer (`progression-design.md` §7).

## Where the details live

- `digital-design.md`: the visual style, the golem roster and the Workbench.
- `progression-design.md`: the tech tree, steam scarcity, chassis unlocks and the Clock Tower.
- `cozy-automation-design.md`: depots, moods, loop labels and the Recycler.
- `roadmap.md`: what is done and what comes next.
