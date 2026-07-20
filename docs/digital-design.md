# Golem Factory — Digital Prototype: Aesthetic, Roster & Interface

## Relationship to the other docs

- `docs/game-design.md` is the original tabletop concept (spiral Time Track, Brass
  Cog triggers, physical punch-card tiles).
- `docs/unity-implementation-plan.md` is the solo-digital architecture/milestone plan
  derived from it.
- This doc captures three settled design decisions for the digital version that
  sharpen or supersede earlier calls: the **visual style**, the **concrete golem
  roster**, and the **programming interface**. It does not introduce new mechanics —
  the Logic Core / Appendages / Chassis punch-card model from `game-design.md` is
  unchanged; this is what that model looks and feels like on screen.

## Core Gameplay Loop & Aesthetic

- **Gameplay loop**: the deep logistical engineering of Factorio/Satisfactory —
  physical resource lines, machine building, automation via the golem/punch-card
  system — not the abstracted or turn-based feel of the board game.
- **Aesthetic**: cozy, detailed **isometric pixel art**, closer to Stardew Valley than
  to Factorio's utilitarian top-down look, set inside a warm wood-and-brass
  steampunk artificer's workshop. This **supersedes** the "2D top-down, Factorio-style
  presentation" decision recorded in `unity-implementation-plan.md` — see the update
  there.

## The Golem Roster

Golems remain rigid, linear automated units defined by combining a **Logic Core**
(trigger) and **Appendages** (actions) into a **Chassis** (per the punch-card system
in `game-design.md`). The roster below is the concrete set of chassis archetypes that
combination is expected to produce across a playthrough, in rough progression order:

| Golem | Tier | Role |
|---|---|---|
| Clockwork Scavenger | Early game | Rickety tripod laborer; clears debris and supplies basic input lines. |
| Brass Presser | Early-mid game | Stationary, inline processor bolted to the floor; stamps raw scrap into brass bars. |
| Aether-Hauler | Mid game | Armored cargo shuttle on treads; safely moves high-value, unstable Aether crystals across long distances. |
| Mainspring Overclocker | Utility | Stationary clockwork butler; projects a harmonic wave that boosts the speed of surrounding machinery. |
| Zeppelin Freight Loader | Late game | Massive industrial behemoth; packs bulk shipments onto outgoing trade zeppelins at the loading dock. |

Each is a specific `ChassisDefinition` + default Logic Core/Appendage loadout, not a
separate code path — the underlying execution model (strictly linear, stalls rather
than adapts) is identical for all five.

## Grid & Movement Mechanics

- **Strict tile-snapping grid.** No free movement.
- **No pathfinding AI.** Golems are fixed to a tile and face one of four directions —
  North, South, East, West.
- **Fully linear actions.** A golem pulls from a designated **Source** tile
  (behind/beside it) and pushes to a **Target** tile (in front of it), both determined
  by its fixed facing. This is the spatial implementation of the "golems cannot pivot"
  rigidity rule from the tabletop design.

## The Programming Interface: Menu-Based Punch Card System

Considered against physical, floor-tile logic gates (a Redstone-style approach) and
rejected in favor of a dedicated menu screen — cleaner to read, easier to iterate on
without physical-layout puzzles competing with the factory-floor puzzle.

- **The Workbench UI**: a dedicated management screen — a mahogany-and-brass
  blueprint viewport of the selected golem's chassis on the left, and a **Card Vault**
  inventory grid on the right.
- **Drag-and-drop, color-coded cards** into chassis slots:
  - **Teal cards** — Logic Cores (timers, sensors; the trigger).
  - **Copper cards** — Appendages (arms, presses, tools; the action).
- **Diagnostic tape ticker**: a live readout tracking steam consumption and cycle
  speed as cards are slotted, giving immediate feedback before activation.
- **"Engage Gears" lever**: the physical/visual commit action — pulling it locks in
  the current card configuration and boots the golem into the game world.

## Open Follow-ups

- Isometric art means the Unity plan's `GridMap`/Tilemap setup needs an isometric
  tilemap + Y-sort layering, not an orthographic top-down grid — implementation
  details tracked in `unity-implementation-plan.md`.
- Not yet decided: whether all five roster golems ship by the M7 vertical-slice
  milestone, or are introduced progressively across later milestones.
