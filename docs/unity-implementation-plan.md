# Golem Factory — Unity Implementation Plan (Solo Digital Prototype)

## Context

`docs/game-design.md` documents "Golem Factory: The Clockwork Metropolis," a tabletop
concept built around rival Artificers programming automated "golems" via punch-card
tiles, competing on a shared "Time Track." This plan adapts it into a **solo digital
video game in Unity**, explicitly styled like Factorio/Satisfactory — not a digitized
board-game UI.

Settled design decisions:

- **Solo scope for v1**, not multiplayer. Mechanics that only make sense with rival
  players — Patent Registry royalties paid to *other* players, competitive turn order
  on a shared time track, contested Assembly Line drafting — are **sidelined**, but
  systems should be architected so they can grow into their multiplayer board-game
  form later without a rewrite.
- **World model: spatial**, Factorio/Satisfactory-style — golems and conveyors are
  physically placed on a map and resources visibly flow between them (not an
  abstracted logic-graph/schematic game).
- **Presentation: cozy isometric pixel art**, Stardew Valley-esque, set in a
  wood-and-brass steampunk workshop — supersedes the earlier "2D top-down,
  Factorio-style" call. See `docs/digital-design.md` for the full aesthetic, golem
  roster, and Workbench UI spec this section implements. The underlying spatial
  simulation (grid truth, belts, golem execution) is unaffected by this change; only
  the tilemap/rendering/camera setup differs from a plain orthographic top-down grid.
- **Primary focus for the first build**: factory automation mechanics and golem
  programming (the punch-card Logic Core / Appendages / Chassis system), since that's
  the mechanic most unique to this design and least dependent on other players.

No Unity project exists in this repo yet. This is a from-scratch architecture and
milestone plan.

## Recommended Approach

### Unity setup
- Unity 6 LTS, 2D (URP) template, URP 2D Renderer.
- Packages: new Input System, 2D Tilemap + Extras (isometric grid layout, Y-sort on
  the sprite renderers), Cinemachine v3, TextMeshPro, Test Framework (EditMode +
  PlayMode). Skip Addressables, DOTS/ECS, and any netcode package for v1.
- Isometric presentation is a rendering/tilemap concern only — `GridMap` truth stays
  `Vector2Int`-indexed exactly as in a top-down grid (see Spatial simulation systems
  below); only the `Grid`/`Tilemap` components and camera setup use Unity's isometric
  layout instead of orthographic-rectangular.
- **Simulation architecture: plain, data-oriented C# — not DOTS/ECS.** Drive
  everything from one central fixed-tick loop (`SimulationClock` + `ITickable`
  registrants) instead of per-object `Update()`. The known perf trap in this genre is
  a GameObject per belt item, not "absence of ECS" — avoid it by modeling belt items
  as plain structs in manager-owned arrays, not GameObjects. This keeps a clean seam
  to migrate hot paths to DOTS later if profiling ever demands it, without paying
  ECS's authoring cost now.
- Deterministic grid math for placement/movement/flow; no physics-driven simulation
  logic (physics reserved for cosmetic effects only, if used at all).

### Translating the Time Track / Brass Cog mechanic into a solo real-time game
This is the key design translation, since the tabletop mechanic is fundamentally
turn-based and competitive:

- **World Simulation Clock**: a `SimulationClock` advances a `Tick` counter at a fixed
  rate (play/pause/speed controls), independent of player input — golems act on this
  clock, Factorio-style.
- **Brass Cog → generalized Cog Trigger types**, evaluated each tick by a
  `GolemTriggerSystem`, exposed via an event bus (`TickAdvanced`, `ThresholdCrossed`,
  `GolemCompleted`):
  - **Interval** — fires every N ticks (default/simplest).
  - **Threshold** — fires when a linked inventory crosses a quantity.
  - **Signal (chained)** — fires when another named golem completes its cycle,
    recreating chain-reaction automation between golems.
  - **AlwaysOn** — loops continuously as fast as its program's step durations allow.
- **Artificer Focus meter** — a distinct resource, separate from the passive
  simulation clock, that gates *intellectual* Artificer actions (reprogramming a
  golem's punch cards, filing a blueprint, swapping a chassis) while keeping raw
  building/placement free and instant. This preserves the core asymmetry from the
  design brief — golems run rigidly and automatically on the world clock; Artificers
  act flexibly but are resource-gated — and doubles as the seam for later "furthest
  behind goes next" competitive turn order.
- Treat v1 as an open-ended sandbox with no forced end condition; the "Clock Tower"
  end-game is a good candidate for a later optional mega-project goal system, not a
  v1 requirement.

### Golem programming data model
Hybrid: **ScriptableObjects for authored definitions**, plain serializable classes for
runtime/save instance state. No custom DSL for v1.

- `LogicCoreDefinition` (SO) — trigger type + default params.
- `AppendageActionDefinition` (SO) — action type (Haul, ExtractFromNode, Refine,
  LoadIntoBuffer, ...) + params.
- `ChassisDefinition` (SO) — max appendage slots, tier, build cost.
- `GolemProgram` (plain class, per-instance/savable) — references a chassis, a
  configured logic core instance, and an ordered list of appendage instances.
- `GolemEntity` (MonoBehaviour) — holds a `GolemProgram` and execution state
  (`currentStepIndex`, `Idle`/`Running`/`Stalled`), implements `ITickable`.
- Five authored `ChassisDefinition` presets cover the named roster from
  `docs/digital-design.md` (Clockwork Scavenger, Brass Presser, Aether-Hauler,
  Mainspring Overclocker, Zeppelin Freight Loader) — each a preset slot
  count/tier/sprite plus a default Logic Core + Appendage loadout players can
  reprogram. They share the one `GolemEntity`/`GolemProgram` execution path; no
  per-golem subclassing.

**Execution is strictly linear and non-adaptive** — this is the mechanical heart of
the "golems are rigid, cannot pivot" design requirement. Each tick, an `Idle` golem
checks its trigger; once `Running`, it executes the current appendage's
`TryExecute(context)`. If a precondition fails (empty source, full destination), the
golem does **not** skip/reorder/substitute — it enters `Stalled` and retries the same
step every tick until conditions clear, emitting a `GolemStalledEvent` for UI
feedback. No branching exists in the model at all; rigidity is structural, not a rule
check. Chassis capacity is enforced only at authoring time in the programming UI.

### Spatial simulation systems
- **Grid**: Tilemap is purely visual; simulation truth lives in a separate `GridMap`
  (`Vector2Int` → occupancy) decoupled from rendering.
- **Belts**: performance-critical — no GameObject per item. `BeltSegment` holds a
  fixed-capacity lane of `ItemStack{itemType, progress}` structs; a `ConveyorSystem`
  manager ticks all segments and hands off items at boundaries/junctions.
- **Resource nodes** replace "Loading Docks": static map features (Scrap/Brass/Aether
  deposits) the player must physically route golems/belts to — the direct spatial
  analog of Factorio ore patches.
- **Placement**: ghost-preview + grid-snap `BuildModeController`, checks `GridMap`
  occupancy, instantiates the golem prefab, opens the programming UI.
- **Assembly Bays** become a concrete placeable `AssemblyBayStructure` with N golem
  mount slots; upgrading with Scrap/Brass unlocks more simultaneous golems and bigger
  chassis tiers — a direct spatial translation of the tableau mechanic.

### Multiplayer-compatible seams (build clean now, no networking yet)
- Every `Blueprint`/patent/drafted card carries an explicit `OwnerId` from day one
  (hardcoded to a single `LocalPlayer` in v1).
- `PatentRegistry`/`Blueprint` is implemented as real single-player QoL (named/reusable
  saved programs) with a `TryUseBlueprint(blueprintId, userId)` API that already has
  the royalty-charge branch, no-op'd when `userId == OwnerId`.
- `AssemblyLineState` (drip-feeds new unlocks over time) exposes `ClaimSlot(userId)`
  from the start, even though only one user calls it in v1.
- The Artificer Focus meter is per-player from the start, so it can flip into
  competitive turn order later.
- No Netcode/Mirror packages, no client-authority split — just avoid hardcoding
  singleton "the player" access inside ownable entities. Purely global systems
  (`SimulationClock`, `GridMap`) can stay simple singletons.

### Project structure
```
Assets/_Project/Scripts/
  Simulation/   SimulationClock, ITickable, TickScheduler
  World/        GridMap, ResourceNode, MapGeneration
  Belts/        BeltSegment, ConveyorSystem, ItemStack
  Golems/       GolemEntity, GolemProgram, execution state machine
  PunchCards/   LogicCoreDefinition, AppendageActionDefinition, ChassisDefinition (SOs)
  Buildings/    AssemblyBayStructure, Extractor, PlaceableBuilding base
  Blueprints/   Blueprint, PatentRegistry, OwnerId
  AssemblyLine/ AssemblyLineState, DraftableCardDefinition
  Economy/      ResourceInventory, ItemType definitions
  Player/       ArtificerController, BuildModeController, ArtificerFocusMeter
  UI/           WorkbenchPanel (blueprint viewport + Card Vault + Engage Gears
                 lever), GolemStatusPanel, HUD, BuildMenu
  Events/       event bus for triggers
  Save/         (later) serialization
Assets/_Project/{Prefabs,ScriptableObjects,Scenes,Art,Tilemaps}/
Tests/{EditMode,PlayMode}/
```
Use asmdefs: `GolemFactory.Simulation` (UnityEngine-light), `GolemFactory.Runtime`,
`GolemFactory.Editor`, `GolemFactory.Tests.EditMode`, `GolemFactory.Tests.PlayMode`.

Key scene-resident managers: `SimulationClock`, `GridMap`, `ConveyorSystem`,
`ItemDatabase`, `EventBus`, `PatentRegistry`, `AssemblyLineState`.

### Milestones (each independently playable)
- **M0** — Project scaffolding: Unity 6 LTS 2D URP project, packages, folders/asmdefs,
  empty scene + camera/grid, Unity `.gitignore`, pan/zoom input.
- **M1** — Grid + placement: `GridMap`, click-to-place/remove a placeholder building,
  no simulation yet.
- **M2** — Tick clock + one hardcoded golem: `SimulationClock` with play/pause/speed,
  a single `GolemEntity` running a hardcoded 2-step program (Extract Scrap → deposit)
  on AlwaysOn trigger. *Smallest playable slice.*
- **M3** — Punch-card data model + minimal (list-based) programming UI: a few Logic
  Core/Appendage/Chassis SOs, assemble/assign `GolemProgram`, capacity enforcement.
  List-based UI only at this stage; the full Workbench/Card Vault visual treatment
  lands in M8.
- **M4** — Belts: `BeltSegment`/`ConveyorSystem`, connect golem→belt→golem/storage,
  visualize flow.
- **M5** — Multiple resource chains: Brass/Aether nodes, a Refine appendage (recipe
  over N ticks), generic `StorageBuffer`, inventory UI.
- **M6** — Stall handling + status UI: `Stalled` state, stall indicator,
  `GolemStalledEvent`, simple alerts panel.
- **M7** — First real Cog-style trigger / vertical slice: Threshold + Signal trigger
  types; demo scenario — Golem A hauls scrap until Brass hits a threshold → triggers
  Golem B to refine → triggers Golem C to load into a sell/ship building. *Demoable
  vertical-slice checkpoint.*
- **M8** — Artificer Focus meter + build UI polish: reprogramming/patenting resource
  cost, Assembly Bay structures with tiers/capacity, and the full Workbench UI —
  blueprint viewport, drag-and-drop Card Vault with teal (Logic Core) / copper
  (Appendage) card coloring, diagnostic tape ticker, "Engage Gears" activation lever.
- **M9 (stretch)** — Solo Assembly Line drafting loop, Blueprint/Patent Registry UI,
  save/load, polish.

Run M0–M2 first to validate feel quickly; treat M7 as the demoable checkpoint to show
the user.

## Critical Files (first to create)
- `Assets/_Project/Scripts/Simulation/SimulationClock.cs`
- `Assets/_Project/Scripts/Golems/GolemEntity.cs`
- `Assets/_Project/Scripts/Golems/GolemProgram.cs`
- `Assets/_Project/Scripts/PunchCards/LogicCoreDefinition.cs`,
  `AppendageActionDefinition.cs`, `ChassisDefinition.cs`
- `Assets/_Project/Scripts/Belts/ConveyorSystem.cs`
- `Assets/_Project/Scripts/World/GridMap.cs`

## Verification

- **EditMode tests** (highest-value, no scene needed) against the UnityEngine-light
  simulation assembly: `GolemProgram` state transitions (Idle→Running→Stalled→Idle,
  empty-source/full-destination/retrigger-while-stalled), trigger evaluation
  (Interval timing, Threshold edges, Signal chaining), belt item-advancement math,
  `GridMap` occupancy, `ArtificerFocusMeter` regen. E.g.
  `Tests/EditMode/Golems/GolemExecutionTests.cs`,
  `Tests/EditMode/Triggers/CogTriggerTests.cs`.
- **PlayMode tests**: `SimulationClock` driving registered `ITickable`s in order,
  belt-to-golem handoff across real GameObjects, build-mode placement through actual
  input flow.
- **Manual verification** for visual/spatial/UX: belt visuals at merges/turns,
  ghost-placement preview, stall-icon readability, camera feel, programming UI
  drag-and-drop. Define a rough perf budget early (e.g. "smooth at 500 belt items /
  100 golems") and profile against it starting around M4–M5.
- Run the game via Unity Editor Play mode at each milestone and manually exercise the
  new mechanic (e.g. at M7, let the demo scenario run and confirm Golem C receives
  refined Brass only after threshold-triggered Golem B completes).
- CI (Unity batch-mode `-runTests` in GitHub Actions) is a nice-to-have once
  EditMode/PlayMode asmdefs exist — not a v1 blocker.
