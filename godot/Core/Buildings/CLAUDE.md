Folder rules for buildings, demolition and the wrecking bar. The root `CLAUDE.md` has the project overview, commands and settled rules.
Paths here are relative to `godot/` unless they say otherwise. `BuildModeController` and
`BuildDragPath` are in `Core/Player/`; buildings are drawn in `Scripts/Buildings/`.

# Buildings

Buildings are Core `PlaceableBuilding`s. Godot draws them in `Scripts/Buildings/BuildingsLayer`,
one `BuildingView` each, by listening to `BuildingPlaced`, `BuildingRemoved` and
`ConnectedShapesChanged`. Don't give a building its own scene with logic in it.

# Building, demolition and the wrecking bar

- **Demolition refunds the full cost. This is a settled design call**: the game is cozy, so
  placing and reorganising must not be punitive. Do not reintroduce a salvage fraction as
  "balance". Three rules keep it honest:
  - **Only what the player paid for**: the price is gated on `PlaceableBuilding.IsRuntimePlaced`.
    (What a building *holds* comes back whoever built it.)
  - **Only a player's own click**: `ClearRuntimePlacedBuildings` (a load) passes `refund: false`,
    or save/load/save/load is an infinite duplicator.
  - **Never destroy what it cannot hand back**: `RefundWouldFit` is asked before teardown.
- **The two tools are exclusive in both directions**: entering demolish puts the placeable
  down, and picking a placeable puts the wrecking bar down. The invariant lives on
  `BuildModeController`, not on any menu.
- **The wrecking bar takes golems too** (`IGolemDismantler`), refunding chassis and cargo
  (`GolemDismantleRules`). **A click takes a golem; a drag never does**, because a refund can't
  restore a program. A golem the player is carrying is refused.
- **Selling a golem to the city** (`TrySellGolem`, docs/golem-sales-design.md §2.1) shares the
  dismantle's teardown but pays back the cargo only: the city's order pays for the chassis. It
  takes a carried golem (that is how it reaches the office) and refuses one nobody built.
- **A Golem Foundry** (`PlaceableGolemFoundry`) is a crate the rules already know, an
  `IIntakeCrate` owning its buffer, so saving, refunds and the Crate screen need nothing new; its
  build under way is saved on its `BuildingEntry` and refunded on demolition.
- **Golems are NOT `GridMap` occupants**; only buildings are. Anything asking "what stands
  here" scans for golems (`TryFindGolemAt`). There is no golem-by-cell registry.
- **Click-and-drag lays a run that points along itself** (`Player/BuildDragPath`, orthogonal,
  axis-first). Which placeables drag is a per-prefab flag, `IsDragPlaceable`. **A drag is not a
  click**: it never demolishes. It stops at an obstruction rather than skipping it, and
  wiggling back over the run re-anchors without re-facing. The wrecking bar's drag needs no
  flag, never stops at an empty cell, and never takes a golem.
- **A belt's and a pipe's picture is derived from its links**, never the other way round
  (`World/BeltShapeRules`, `Steam/PipeShapeRules` beside the routing rules). A steam pipe has
  no facing; R only orients an isolated stub.

Crate demolition and refunds into the pack are in `godot/Core/Economy/CLAUDE.md`; the Clock
Tower fixture is in `godot/Core/ClockTower/CLAUDE.md`.
