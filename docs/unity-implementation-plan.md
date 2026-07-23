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

This is a from-scratch architecture and milestone plan. **Status: the M0 project
scaffold is done** — see `docs/unity-mcp-setup-guide.md` for the setup steps
that were validated end-to-end (Unity 6.5.4f1, 2D URP template, all packages
below installed, `ProjectSettings`/`Packages`/`Assets` committed to `main`).

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
- **M0 (done)** — Project scaffolding: Unity 6 LTS 2D URP project, packages,
  folders/asmdefs, empty scene, Unity `.gitignore`. Remaining from the original
  scope: camera/grid setup and pan/zoom input aren't wired up yet — pick those
  up as part of M1 alongside placement, since both need the same camera/input
  groundwork.
- **M1 (done)** — Grid + placement:
  `GridMap`/`GridCoordinateConverter`/`BuildModeController`/
  `CameraRigController`/`PlaceableBuilding` and their EditMode/PlayMode tests
  are all written and committed; the "Manual Editor setup" checklist below has
  been run in-Editor and the resulting scene/prefab changes are on `main`.
- **M2 (done)** — Tick clock + one hardcoded golem:
  `SimulationClock`/`ITickable`/`TickScheduler`/`EventBus`/`GolemEntity`/
  `GolemProgram` and the `LogicCoreDefinition`/`AppendageActionDefinition`/
  `ChassisDefinition` SO shells landed alongside M1 (previously undocumented).
  This pass adds the missing pieces: `SimulationClockRunner` (the
  `MonoBehaviour` wrapper called for in `SimulationClock.cs`'s doc comment,
  exposing `Play`/`Pause`/`SetSpeed` and publishing `TickAdvancedEvent`) and
  `HardcodedDemoProgram` (builds the "Extract Scrap → deposit" 2-step
  `AlwaysOn` program from runtime `ScriptableObject` instances so M2 is
  demoable without pre-authored `.asset` files), plus expanded EditMode
  coverage (`SimulationClockTests`, `TickSchedulerTests`, extended
  `GolemExecutionTests`), and `GolemDemoBootstrap` (wires the hardcoded
  program onto a `GolemEntity` and calls `Play()`). The "M2 manual editor
  setup" checklist below has been run in-Editor; Play mode confirmed the
  golem ticks through its 2-step cycle. *Smallest playable slice.*
- **M3 (done)** — Punch-card data model + minimal (list-based) programming UI: an
  authored roster of Logic Core/Appendage/Chassis `.asset` instances, capacity-
  enforced `GolemProgram.TryAssignChassis`/`TryAddAppendage`/`RemoveAppendageAt`, and
  `UI/GolemProgrammingPanel` (`OnGUI`-based list UI wired to that roster). List-based
  UI only at this stage; the full Workbench/Card Vault visual treatment lands in M8.
- **M4 (done)** — Belts: `BeltSegment`/`ConveyorSystem`, connect golem→belt→golem/storage,
  visualize flow. `GolemEntity.TryExecute`'s `ExtractFromNode`/`LoadIntoBuffer` stubs
  became real (belt-backed) behavior; `Haul`/`Refine` stay no-op stubs (locomotion and
  the M5 recipe system, respectively, don't exist yet). Only straight-line segment
  chaining and a hand-wired demo scene — no junctions/splitters and no belt placement
  via `BuildModeController` yet (that's M8/M9's build-UI polish).
- **M5 (done)** — Multiple resource chains: a real `ResourceNode`/`ResourceNodeRegistry`
  (replacing M4's infinite-placeholder hack) and `StorageBuffer`/`StorageBufferRegistry`
  (replacing M4's `DemoBuffer`), a Refine appendage with genuine recipe-over-N-ticks
  processing (`GolemProgram.StepProgressTicks`), and an `InventoryPanel` UI. The node
  roster wasn't an Aether-node-and-Brass-node pair as the milestone summary literally
  reads -- Brass stayed a Refine output (`ScrapBuffer` → `BrassBuffer`, per the
  M3-authored `RefineBrass` asset) and Aether became the second raw node, since that
  matches `digital-design.md`'s Aether-Hauler fluff and gives a genuinely independent
  second chain rather than two node types feeding the same appendage.
- **M6 (done)** — Stall handling + status UI: `Stalled` state and `GolemStalledEvent`
  already existed since M2; this milestone added the missing counterpart
  `GolemResumedEvent`, a world-space `GolemStallIndicator` per golem, and a simple
  `AlertsPanel` listing every currently-stalled golem.
- **M7 (done)** — First real Cog-style trigger / vertical slice: Threshold + Signal
  trigger types, implemented directly in `GolemEntity` (Threshold as an edge-triggered
  poll of the already-held `StorageBufferRegistry`; Signal via a `GolemCompleted`
  subscription) rather than the standalone `GolemTriggerSystem` an M2-era code comment
  had proposed -- see the implementation notes below for why. Demo scenario adapted to
  this project's buffer economy: Golem E hauls Scrap until a buffer hits a threshold →
  triggers Golem F to refine into Brass → Golem F completing triggers (Signal) Golem G
  to ship it into a final buffer. *Demoable vertical-slice checkpoint.*
- **M8 (done)** — Artificer Focus meter + build UI polish: reprogramming/patenting
  resource cost, `AssemblyBayStructure` with tiers/capacity, and the full Workbench UI
  — real UGUI drag-and-drop (first Canvas/EventSystem work in the project; every prior
  milestone's UI was OnGUI), blueprint viewport, teal/copper Card Vault, diagnostic tape
  ticker, "Engage Gears" lever. Supersedes M3's `GolemProgrammingPanel` (now disabled).
  Headless `Blueprint`/`PatentRegistry` exist and are Focus-gated via the Workbench's
  "Patent" button, but there's no browse/reuse UI for saved blueprints yet -- that
  remains M9's explicit scope ("Blueprint/Patent Registry UI").
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

## M1 implementation notes (grid + placement)

### Camera & input
- New Input System asset `Assets/_Project/Input/GolemFactoryInputActions.inputactions`,
  one `Gameplay` action map: `Pan` (Vector2, WASD composite), `Zoom` (Axis, mouse
  scroll), `Click` (Button, left mouse). Components read it via
  `InputActionAsset.FindActionMap`/`FindAction` rather than generating a C# wrapper
  class, so no Editor-generated code is required to check in.
- `Player/CameraRigController.cs` — plain `MonoBehaviour` that reads `Pan`/`Zoom`
  from the asset each frame and drives the `Camera` transform/`orthographicSize`
  directly (clamped to a min/max zoom).
- **Scope trim from the original Unity-setup section**: Cinemachine v3 is
  installed (`Packages/manifest.json`) but *not* wired into the camera for M1 —
  driving the plain `Camera` directly is enough to satisfy M1's "pan/zoom works"
  requirement, and it avoids hand-authoring Cinemachine component YAML/asmdef
  references that can't be verified without the Unity Editor open. Swapping to a
  Cinemachine-driven follow rig later is a camera-only change; it doesn't touch
  `GridMap`, `GridCoordinateConverter`, the input asset, or `BuildModeController`.

### Grid & placement
- `World/GridCoordinateConverter.cs` — plain C# isometric world↔cell math
  (`WorldToCell`/`CellToWorldCenter`), parameterized by cell size and decoupled
  from Unity's `Tilemap` component so it's unit-testable without a scene. The
  Tilemap's cell size (set in the Editor per the manual setup steps below) must
  match the value passed into this converter.
- `World/GridMapHolder.cs` — thin scene-resident `MonoBehaviour` that owns a
  `GridMap` instance, mirroring how `SimulationClock` is owned by a wrapper
  (`Simulation/SimulationClock.cs`'s doc comment).
- `Buildings/PlaceableBuilding.cs` — minimal `MonoBehaviour` placeholder:
  `Cell`, `OwnerId` (hardcoded `LocalPlayer`, matching the multiplayer-seam
  convention used elsewhere in this plan). Not `ITickable` — no simulation in M1.
- `Player/BuildModeController.cs` — each frame, converts the pointer position to
  a cell via `GridCoordinateConverter`, moves a ghost `SpriteRenderer` to that
  cell's center, and tints it green/red based on `GridMap.IsOccupied`. On
  `Click`: empty cell → instantiate the placeholder prefab and
  `GridMap.TryOccupy`; occupied cell → look up the occupant via
  `GridMap.TryGetOccupant`, destroy it, `GridMap.Free`. One click does double
  duty (place/remove) — no separate mode toggle for M1. The place/remove logic
  itself is exposed as `PlaceOrRemove(Vector2Int cell)`, callable directly from
  tests without simulating Input System events.

### Manual Editor setup (can't be authored from git alone)
Scene composition, prefabs, and cross-object references need the Unity Editor —
hand-editing `Main.unity`'s YAML for these blindly (no Editor available to
verify) risks a broken scene, so this is a checklist to run once in-Editor:
1. In `Main.unity`, add a `Grid` GameObject (Isometric cell layout, cell size
   matching whatever's passed to `GridCoordinateConverter`, e.g. `1 × 0.5`) with
   a child `Tilemap` + `Tilemap Renderer` for the visual grid.
2. Create an empty `PlaceholderBuilding` GameObject with a `SpriteRenderer` +
   `PlaceableBuilding` component, save it as a prefab under
   `Assets/_Project/Prefabs/`.
3. Create a `BuildMode` GameObject, add `BuildModeController`, assign: the main
   `Camera`, a `GridMapHolder` (add that component to a `GridMap` manager
   GameObject in-scene), the placeholder prefab, a ghost `SpriteRenderer` (a
   separate semi-transparent sprite object), and the
   `GolemFactoryInputActions` asset.
4. Create a `CameraRig` GameObject, add `CameraRigController`, assign the main
   `Camera` and the input actions asset.
5. Save the scene and commit the resulting `.unity`/`.meta`/prefab changes.

### Testing
- EditMode: `GridMap` occupancy edge cases (double-occupy rejected, free-then-
  reoccupy, empty-cell lookup) and `GridCoordinateConverter` cell↔world
  round-trips — `Tests/EditMode/World/GridMapTests.cs`,
  `Tests/EditMode/World/GridCoordinateConverterTests.cs`.
- PlayMode: `BuildModeController.PlaceOrRemove` place/remove flow against a real
  `GridMapHolder` and instantiated `PlaceableBuilding` —
  `Tests/PlayMode/Player/BuildModeControllerTests.cs`.
- Manual: pan/zoom feel, ghost-preview readability — verified in-Editor per the
  setup checklist above (not automatable from this environment).

## M2 implementation notes (tick clock + one hardcoded golem)

### Code (done)
- `Simulation/SimulationClock.cs`, `Simulation/ITickable.cs`,
  `Simulation/TickScheduler.cs` — plain C# tick source and one-off scheduled
  callbacks, unit-testable without a scene (`GolemFactory.Simulation` asmdef
  has `noEngineReferences: true`).
- `Events/EventBus.cs` — static event bus (`TickAdvanced`, `ThresholdCrossed`,
  `GolemCompleted`, `GolemStalled`).
- `Golems/GolemProgram.cs`, `Golems/GolemEntity.cs` — `Idle`/`Running`/
  `Stalled` state machine driven by `ITickable.Tick`; trigger evaluation
  (`AlwaysOn`/`Interval` live, `Threshold`/`Signal` deferred to M7); appendage
  execution is currently a stub that always succeeds (real
  Haul/ExtractFromNode/Refine/LoadIntoBuffer behavior lands M3–M5).
- `PunchCards/LogicCoreDefinition.cs`, `AppendageActionDefinition.cs`,
  `ChassisDefinition.cs` — SO shells for trigger/action/chassis data (full
  authored `.asset` roster is an M3 task).
- `SimulationClockRunner.cs` — scene-resident `MonoBehaviour` wrapper around
  `SimulationClock` (mirrors `GridMapHolder`'s ownership of `GridMap`),
  exposing `Play()`/`Pause()`/`SetSpeed(float)` and publishing
  `TickAdvancedEvent` from `Update()`.
- `Golems/HardcodedDemoProgram.cs` — builds the M2 demo `GolemProgram`
  (`AlwaysOn` trigger, `ExtractFromNode` → `LoadIntoBuffer`) from runtime
  `ScriptableObject` instances, so the milestone is demoable without
  pre-authored `.asset` files.

### M2 manual editor setup (done)
Ran alongside M1's checklist, using the bootstrap-`MonoBehaviour` option:
1. `Main.unity` has a `SimulationClockRunner` GameObject with the
   `SimulationClockRunner` component.
2. A `Golem` GameObject has `GolemEntity`.
3. A `GolemDemoBootstrap` GameObject (`Golems/GolemDemoBootstrap.cs`) holds
   references to both; on `Start()` it assigns
   `HardcodedDemoProgram.ExtractAndDeposit()` onto the golem's program,
   calls `SimulationClockRunner.Register(golemEntity)`, then
   `SimulationClockRunner.Play()` — so the clock advances and the golem ticks
   automatically once Play mode starts (no separate play/pause/speed HUD
   needed yet; that's still an M8 concern).
4. Scene/prefab changes are committed to `main`.

### Testing
- EditMode: `SimulationClock` play/pause gating, tick-accumulation math,
  tickable registration order — `Tests/EditMode/Simulation/
  SimulationClockTests.cs`. `TickScheduler` due-tick firing/removal —
  `Tests/EditMode/Simulation/TickSchedulerTests.cs`. `GolemEntity` trigger
  evaluation (`AlwaysOn` every tick, `Interval` on multiples), step
  advancement, and `GolemCompletedEvent` publication on cycle wrap —
  `Tests/EditMode/Golems/GolemExecutionTests.cs`.
- Manual: verified in-Editor — Play mode runs clean and the hardcoded golem
  ticks through its 2-step cycle via `GolemDemoBootstrap`.

## M3 implementation notes (punch-card data model + list-based programming UI)

### Code (done)
- `Golems/GolemProgram.cs` gains the assembly API the milestone calls for:
  `TryAssignChassis` (rejects a chassis whose `maxAppendageSlots` is smaller
  than the program's current appendage count, leaving the old chassis in
  place), `TryAddAppendage` (rejects once `appendages.Count` reaches the
  assigned chassis's `maxAppendageSlots`, and rejects with no chassis
  assigned at all), and `RemoveAppendageAt` (bounds-checked no-op on an
  invalid index). This is the "capacity enforcement" called for in the
  milestone description; per the design doc it's assembly-time only —
  `GolemEntity.Tick`/execution never re-checks slot counts.
- An authored roster of `.asset` instances now backs the SO shells added in
  M2, under `Assets/_Project/ScriptableObjects/`:
  - `LogicCores/`: `AlwaysOnCore`, `IntervalCore10` (10-tick interval).
  - `Appendages/`: `ExtractScrap`, `HaulScrap`, `RefineBrass`,
    `LoadIntoScrapBuffer` — one per `AppendageActionType`, reusing the
    `ScrapNode`/`ScrapBuffer` ids from `HardcodedDemoProgram` plus a
    `BrassBuffer` id for the refine step.
  - `Chassis/`: all five named in `docs/digital-design.md`'s roster —
    `ClockworkScavenger` (2 slots, tier 1), `BrassPresser` (3, tier 1),
    `AetherHauler` (3, tier 2), `MainspringOverclocker` (4, tier 2),
    `ZeppelinFreightLoader` (5, tier 3) — with placeholder Scrap/Brass costs
    since no economy balancing pass has happened yet.
  These are plain-data `.asset` YAML files (no cross-object scene
  references), so — unlike scene/prefab composition — they were safe to
  author directly rather than deferring to an in-Editor checklist.
- `UI/GolemProgrammingPanel.cs` — the "minimal (list-based) programming UI":
  an `OnGUI` panel (no Canvas/UGUI scene wiring required) that lists
  Inspector-assigned `availableChassis`/`availableLogicCores`/
  `availableAppendages` arrays as toggle/button rows, calls the
  `GolemProgram` assembly API above, and surfaces a status message when an
  action is rejected (chassis-swap-too-small, appendage-add-at-capacity).
  Full drag-and-drop Card Vault styling is explicitly deferred to M8.

### M3 manual editor setup (done)
1. `Main.unity` has a `GolemProgrammingPanel` GameObject with the
   `GolemProgrammingPanel` component.
2. Its `Target Golem` field is assigned to the scene's `Golem` GameObject
   (from the M2 setup).
3. `Available Chassis`/`Available Logic Cores`/`Available Appendages` are
   populated with the `.asset` files under
   `Assets/_Project/ScriptableObjects/{Chassis,LogicCores,Appendages}/`.
4. Play mode confirmed the panel renders in the top-left, chassis/logic-core
   swaps and appendage add/remove buttons work, and capacity rejections show
   the status message.
5. Scene changes are committed to `main`.

### Testing
- EditMode: chassis/appendage capacity enforcement —
  `Tests/EditMode/Golems/GolemProgramAssemblyTests.cs` (assign succeeds/fails
  on slot count, add succeeds up to capacity and fails beyond it, add fails
  with no chassis, remove frees a slot, out-of-range remove is a no-op).
- Manual: verified in-Editor — `GolemProgrammingPanel` layout/readability and
  drag-and-drop of roster assets confirmed per the setup checklist above.

## M4 implementation notes (belts)

### Code (done)
- `Belts/ItemStack.cs` — mutable struct (`ItemType` string id, `Progress` float).
  Held in `BeltSegment`'s `List<ItemStack>` and mutated via read-copy/write-back
  through the indexer, since `List<T>`'s indexer isn't addressable and `foreach`
  yields readonly copies.
- `Belts/BeltSegment.cs` — fixed-capacity lane (`Capacity = Length + 1`,
  `MinSpacing = 1`), items ordered head-first. `Advance(step)` walks head→tail so
  each item's cap comes from the already-updated item ahead of it, enforcing
  no-overlap/no-passing every tick. `TryEnqueue`/`TryPeekHead`/`TryRemoveHead`
  gate on capacity/spacing and on the head having reached `Length`. `Next` is a
  plain reference for chaining two segments.
- `Belts/ConveyorSystem.cs` — plain C# `ITickable`, segments keyed by string id.
  `Tick` runs two full passes: (1) `Advance(1f)` every segment, (2) hand off any
  head that reached `Length` onto `Next` (or leave it parked as backpressure if
  `Next` is full). Splitting into two passes means a handed-off item — reset to
  `Progress = 0` in its new segment — can never be advanced twice in the same
  tick, so dictionary iteration order doesn't affect correctness. Exposes
  `TryEnqueue`/`TryPeekHead`/`TryDequeueHead` by segment id for golem code to
  call directly (pull-based; `Belts/` has no reverse reference to `Golems/`).
  `TryGetSegment` guards against a `null` id (an unset `sourceId`/`destinationId`)
  so callers get `false` instead of the `ArgumentNullException` a raw
  `Dictionary<string,_>` lookup would throw. Only 1:1 `Next` chaining is
  implemented — junctions/splitters/mergers are not.
- `Belts/ConveyorSystemHolder.cs` — thin scene-resident owner for one
  `ConveyorSystem`, mirroring `GridMapHolder`/`SimulationClockRunner`.
- `Belts/DemoBuffer.cs` — static in-memory counter keyed by buffer id. An M4
  placeholder sink for `LoadIntoBuffer`, explicitly **not** the real
  `StorageBuffer` (M5) — not serialized, not shown in any UI.
- `Belts/BeltSegmentVisual.cs` — "visualize flow" without a GameObject per item:
  pools a fixed number of `SpriteRenderer`s sized to `BeltSegment.Capacity`
  (never grows/shrinks at runtime) and each `LateUpdate` positions/enables up to
  `Items.Count` of them via `Lerp(startPoint, endPoint, progress/Length)`.
- `Golems/GolemEntity.cs` — gains a `[SerializeField] ConveyorSystemHolder
  conveyorHolder` field and a `Configure(id, holder)` method (mirrors
  `BuildModeController.Configure`, used by tests and available for runtime
  bootstrapping). `TryExecute`'s unconditional `return true;` stub is replaced
  with a switch on `actionType`: `ExtractFromNode` builds an `ItemStack` from
  `sourceId` and pushes it onto the belt named by `destinationId` (every node is
  treated as an infinite M4 placeholder source — no `ResourceNode` exists yet);
  `LoadIntoBuffer` pulls the head item off the belt named by `sourceId` and
  calls `DemoBuffer.Deposit(destinationId, item.ItemType)`. Both fail (→
  `Stalled`) on a full/not-yet-arrived belt, or if `conveyorHolder` is unassigned.
  `Haul` and `Refine` fall through to the same no-op-success stub as before —
  Haul needs a locomotion system that doesn't exist, Refine is explicitly M5's
  recipe-over-N-ticks appendage. This also preserves every pre-M4 test's
  behavior unchanged, since `AppendageActionType.Haul` is the enum default (0)
  and every existing test constructs `AppendageActionDefinition` instances
  without setting `actionType`.
- `Golems/HardcodedDemoProgram.cs` gains `ExtractOntoBelt(beltSegmentId)` and
  `LoadFromBelt(beltSegmentId, bufferId)` alongside the existing
  `ExtractAndDeposit()` (left untouched).
- `Golems/BeltDemoBootstrap.cs` — the M4 playable demo, additive alongside (not
  replacing) M2/M3's `GolemDemoBootstrap`: builds two chained `BeltSegment`s in
  code, assigns Golem A `ExtractOntoBelt("ScrapBeltA")` and Golem B
  `LoadFromBelt("ScrapBeltB", "ScrapBuffer")`, registers the `ConveyorSystem`
  and both golems with the clock, calls `Play()`. Two golems are required
  because a single golem doing extract-then-load never needs a belt at all.

### M4 manual editor setup (was documented as done; actually landed at M5)
This checklist was written and marked done when M4's code was authored, but no
Unity Editor was attached to verify it at the time. When a live Unity MCP
connection became available during M5, `Main.unity`'s hierarchy was inspected
directly and none of the steps below had actually been applied — `GolemB`,
`ConveyorSystem`, and `BeltDemoBootstrap` didn't exist, and the M2/M3
`GolemDemoBootstrap` GameObject was still active. The scene wiring (for both M4
and M5) was done for real during the M5 session, via live `manage_gameobject`/
`manage_components` MCP calls followed by an actual Play-mode run, not by
authoring YAML blind:
1. Disabled the existing M2/M3 `GolemDemoBootstrap` GameObject.
2. Created `Conveyor` (`ConveyorSystemHolder`), `Nodes`
   (`ResourceNodeRegistryHolder`, M5), and `Buffers`
   (`StorageBufferRegistryHolder`, M5).
3. Created `GolemB` (plus M5's `GolemC`/`GolemD`), each with `GolemEntity`.
4. Assigned `Conveyor Holder`/`Node Registry Holder`/`Buffer Registry Holder`
   on `Golem`, `GolemB`, `GolemC`, `GolemD`.
5. Created `BeltDemoBootstrap`, assigned Golem A–D, the conveyor/node/buffer
   holders, and the existing `SimulationClockRunner`.
6. Play mode confirmed Scrap flows Golem A → belt → Golem B → `ScrapBuffer`,
   Golem C refines it into `BrassBuffer`, and Golem D independently drains the
   finite `AetherNode` into `AetherBuffer` — all with zero console errors.
7. Scene changes saved to `main`.

Skipped, deliberately, as out of scope for either milestone's mechanic: the
`BeltSegmentVisual`/endpoint-transform sprite setup from the original M4
checklist. Belt flow correctness is already covered by
`Tests/PlayMode/Golems/BeltGolemHandoffTests.cs`; wiring cosmetic sprites for a
demo bootstrap that M8/M9 will eventually replace wasn't worth the manual
Editor time.

### Testing
- EditMode: belt item-advancement/capacity/spacing math and head peek/remove
  gating — `Tests/EditMode/Belts/BeltSegmentTests.cs`. Multi-segment tick
  ordering (advance-then-handoff, no double-advance in one tick, backpressure
  when `Next` is full) — `Tests/EditMode/Belts/ConveyorSystemTests.cs`.
- PlayMode: golem↔belt handoff across real GameObjects (extract stalls on a
  full belt, load stalls before the head arrives, an end-to-end run across two
  chained segments reaches the destination `StorageBuffer` -- updated at M5 when
  `DemoBuffer` was retired) — `Tests/PlayMode/Golems/BeltGolemHandoffTests.cs`.
- Manual: verified in-Editor at M5 (see the corrected manual-setup note above) —
  belt flow works end-to-end with zero console errors. Stall-on-full-belt and a
  stall UI are still M6 scope. Full perf profiling against the "500 belt items /
  100 golems" budget starts in earnest once M5's economy is in place, per the
  Verification section above.

## M5 implementation notes (multiple resource chains)

### Code (done)
- `Economy/ItemType.cs` — canonical item type id constants (`Scrap`, `Brass`,
  `Aether`), matching the bare-string-id convention used for node/buffer/belt
  ids elsewhere (`Belts/ItemStack.cs`), so recipes don't restate raw literals.
- `Economy/StorageBuffer.cs`/`StorageBufferRegistry.cs`/
  `StorageBufferRegistryHolder.cs` — the real replacement for M4's
  `Belts/DemoBuffer.cs` (deleted this milestone): a buffer now holds
  per-item-type quantities (`Dictionary<string,int>`), not one opaque count, so
  the inventory UI can list what's actually inside. `StorageBufferRegistry`
  mirrors `ConveyorSystem`'s segment dictionary (null-id guard included);
  buffers are created on first deposit rather than requiring pre-registration.
- `World/ResourceNode.cs`/`ResourceNodeRegistry.cs`/
  `ResourceNodeRegistryHolder.cs` — the real replacement for M4's "every
  `ExtractFromNode.sourceId` is treated as an infinite source, and doubles as
  the extracted item's type" hack. A node now has a real `ItemType` (separate
  from its node id) and a finite `RemainingQuantity` (`ResourceNode.Infinite`
  opts out, e.g. for the demo's `ScrapNode`).
- `PunchCards/AppendageActionDefinition.cs` gains `inputItemType`/
  `outputItemType`, used only by Refine: the item type withdrawn from the
  `sourceId` buffer and the item type deposited into the `destinationId`
  buffer. `ExtractFromNode`/`LoadIntoBuffer` don't need these — their item type
  now flows through `ItemStack.ItemType`, sourced from the real `ResourceNode`.
- `Golems/GolemProgram.cs` gains `StepProgressTicks` (reset inside
  `AdvanceStep`) — the recipe-over-N-ticks counter.
- `Golems/GolemEntity.cs`'s execution loop is restructured around
  `durationTicks` generically (not just for Refine): `TryBeginStep` runs once,
  when `StepProgressTicks == 0` (this is where a step's precondition is
  checked and its side effect happens); once it succeeds, `Tick` just counts
  `StepProgressTicks` up to `Max(1, step.durationTicks)` without re-checking
  anything, then calls `CompleteStep` (a no-op except for Refine, where the
  recipe output is deposited only now, not at Begin) and advances. Since
  `ExtractFromNode`/`LoadIntoBuffer` still default to `durationTicks = 1`, they
  complete in the same single tick as before — M4's behavior for those two is
  unchanged. Refine's `TryBeginRefine` withdraws the recipe input up front
  (mirrors a real refinery: once started, nothing can drain the input back out
  from under it), so a multi-tick refine can't be interrupted mid-cycle by the
  source buffer running dry. Also fixes a latent M4 gap: recovering from
  `Stalled` (or finishing a step mid-program) never explicitly reset `State`
  back to `Running`/`Idle` in the old single-tick-only code — harmless when
  every step resolved in one tick, but would have left a resumed multi-tick
  step reading "Stalled" forever. `GolemEntity` also gains
  `nodeRegistryHolder`/`bufferRegistryHolder` fields and a `ConfigureEconomy`
  method (separate from M4's `Configure`, so existing two-arg call sites are
  untouched) for wiring them programmatically.
- `Golems/HardcodedDemoProgram.cs` gains `Refine(...)` and
  `ExtractThenLoad(...)` builders alongside the M2–M4 ones.
- `Golems/BeltDemoBootstrap.cs` extends the same class/file from M4 (not a new
  additive bootstrap, since M5's chain is a direct continuation of M4's Scrap
  flow, not an independent demo) with Golem C (Refine: `ScrapBuffer` →
  `BrassBuffer`, 3 ticks) and Golem D (a single-golem `ExtractThenLoad` chain:
  `AetherNode` → `AetherBelt` → `AetherBuffer`, demonstrating a second,
  independent, *finite* resource chain). Registers `ScrapNode` (infinite) and
  `AetherNode` (`aetherNodeQuantity`, default 20) at `Start()`.
- `UI/InventoryPanel.cs` — minimal `OnGUI` readout (mirrors
  `GolemProgrammingPanel`'s style) listing every registered `StorageBuffer`'s
  contents by item type. No per-resource icons/visual treatment — that's a
  later UI pass.
- `ScriptableObjects/Appendages/RefineBrass.asset` (M3-authored) gains
  `inputItemType: Scrap`/`outputItemType: Brass` now that those fields exist
  and are load-bearing.

### M5 manual editor setup (done, via live Unity MCP)
A live Unity Editor connection was available this session, so rather than
writing a checklist for a human to run later, the scene was wired directly
through `manage_gameobject`/`manage_components` MCP calls and verified with an
actual Play-mode run (see the corrected M4 manual-setup note above for why
this also had to cover M4's never-applied steps):
1. Created `Nodes` (`ResourceNodeRegistryHolder`) and `Buffers`
   (`StorageBufferRegistryHolder`).
2. Created `GolemC`/`GolemD` (`GolemEntity`), set their `Golem Id`s, and
   assigned `Conveyor Holder`/`Node Registry Holder`/`Buffer Registry Holder`
   on all four golems (`Golem`, `GolemB`, `GolemC`, `GolemD`).
3. Created `BeltDemoBootstrap` (see the M4 note) with `golemA`–`golemD` and the
   conveyor/node/buffer holders assigned.
4. Created `InventoryPanel`, assigned its `Buffer Registry Holder` to
   `Buffers`.
5. Saved the scene, entered Play mode, and read the live component state back
   via the `mcpforunity://scene/gameobject/{id}/components` resource: after a
   few seconds, `Buffers` held `ScrapBuffer{Scrap: 58}`,
   `BrassBuffer{Brass: 29}`, `AetherBuffer{Aether: 16}` — confirming both
   chains run correctly end-to-end — with zero console errors or warnings.
6. Exited Play mode and re-saved.

### Testing
- EditMode: `StorageBuffer`/`StorageBufferRegistry` deposit/withdraw/
  independent-item-type-tracking — `Tests/EditMode/Economy/
  StorageBufferTests.cs`. `ResourceNode`/`ResourceNodeRegistry` infinite vs.
  finite depletion, null-id handling — `Tests/EditMode/World/
  ResourceNodeTests.cs`.
- PlayMode: Refine's multi-tick progress (no stall while processing, input
  withdrawn at Begin, output deposited only at completion, `StepProgressTicks`
  resets, stall-then-resume) — `Tests/PlayMode/Golems/GolemRefineTests.cs`.
  `Tests/PlayMode/Golems/BeltGolemHandoffTests.cs` (M4) was updated to route
  through the real `ResourceNodeRegistry`/`StorageBufferRegistry` instead of
  the retired sourceId-as-itemType hack and `DemoBuffer`, plus a new
  unknown-node-id-stalls case.
- Manual: verified in-Editor via live MCP calls, described above — not just a
  written checklist this time.

## M6 implementation notes (stall handling + status UI)

`GolemState.Stalled` and `Events/EventBus.cs`'s `GolemStalledEvent` have existed since
M2/M4 and needed no changes; M6's actual gap was that nothing *consumed* them yet.

### Code (done)
- `Events/EventBus.cs` gains `GolemResumedEvent` -- the counterpart
  `GolemStalledEvent` never had. Published exactly once, from
  `Golems/GolemEntity.cs`'s `Tick`, at the specific transition where a step's
  `TryBeginStep` succeeds after the golem was `Stalled` (captured via a
  `wasStalled` flag read at the top of `Tick`, before the Idle-trigger check
  can overwrite `State`). Without this, a UI element would have to poll
  `Program.State` every frame to know when to turn itself off; with it,
  listeners are purely event-driven.
- `UI/StallTracker.cs` — plain C# (no `MonoBehaviour`) set of currently-stalled
  golem ids, add-on-`GolemStalled`/remove-on-`GolemResumed`. Factored out so
  the bookkeeping is unit-testable without a GameObject or `OnGUI`.
- `UI/GolemStallIndicator.cs` — one per golem, world-space: projects an `OnGUI`
  label above the golem's transform (via `Camera.main.WorldToScreenPoint`)
  while stalled. Event-filtered by `golemId` rather than driving off
  `StallTracker`, since each instance only cares about one golem. No
  sprite/art asset -- that's later visual polish, not M6's job.
- `UI/AlertsPanel.cs` — one global `OnGUI` panel (mirrors
  `GolemProgrammingPanel`/`InventoryPanel`'s style) owning a `StallTracker`
  and listing every currently-stalled golem id. A live "current status" view,
  not a history log — a full alert log/timestamps is UI polish for a later
  milestone, not M6's "simple" scope.

### M6 manual editor setup (done, via live Unity MCP)
Same live-wiring approach as M5 (see its note above for why this is real
Editor state, not a checklist):
1. Created `AlertsPanel` (`AlertsPanel` component) — no references to wire,
   it's purely event-driven.
2. Created `StallIndicator_Golem`/`_GolemB`/`_GolemC`/`_GolemD`
   (`GolemStallIndicator` component each), assigned each one's `Golem` field
   to the matching `GolemEntity`.
3. Saved, entered Play mode, and let the demo run ~15s -- the finite
   `AetherNode` (20 units, from M5) depleted naturally and stalled Golem D on
   its `ExtractFromNode` step with zero rigging required. Confirmed via both
   `mcpforunity://scene/gameobject/{id}/components` (`GolemD.Program.State`
   read back as `2`/Stalled) and a `manage_camera` screenshot showing "⚠
   GolemD is stalled" in the alerts panel and a floating "⚠ GolemD" label at
   the world-space indicator position, with zero console errors/warnings.
4. Exited Play mode and re-saved.

### Testing
- EditMode: `StallTracker` add-on-stall/remove-on-resume, repeated-stall
  dedup, resume-for-untracked-golem no-op, multiple golems tracked
  independently, unsubscribe stops reacting — `Tests/EditMode/UI/
  StallTrackerTests.cs`.
- PlayMode: extended `Tests/PlayMode/Golems/GolemRefineTests.cs` --
  `GolemResumedEvent` fires exactly once at the stalled→running transition
  (not again on the tick that completes an already-resumed cycle), and never
  fires for a golem that was never stalled.
- Manual: verified in-Editor via live MCP calls and a real screenshot,
  described above.

## M7 implementation notes (Threshold + Signal triggers, vertical slice)

### Design deviation from the M2-era plan
`GolemEntity.ShouldTrigger`'s M2/M4 comment said Threshold/Signal evaluation would
"move into a standalone GolemTriggerSystem at M7." That didn't happen -- both are
implemented directly in `GolemEntity` instead, for different reasons each:
- **Threshold** just polls `bufferRegistryHolder` (already held since M5) each tick --
  there's no state to watch that GolemEntity doesn't already have a reference to, so a
  separate polling system would only add indirection.
- **Signal** is genuinely event-driven, but subscribing directly to
  `EventBus.GolemCompleted` on `GolemEntity`'s own `OnEnable`/`OnDisable` (the idiom M6
  established for UI listeners) is simpler than a separate system that would need its
  own golem-id → GolemEntity registry just to dispatch to the right instance.

### Code (done)
- `PunchCards/LogicCoreDefinition.cs` gains `thresholdBufferId`/`thresholdItemType` --
  the M2-era `thresholdQuantity` field had no way to say *which* buffer/item to watch.
- `Golems/GolemProgram.cs` gains `ThresholdArmed` (bool, starts `true`) and
  `PendingSignal` (bool, starts `false`) -- the latched state each trigger type needs.
- `Golems/GolemEntity.cs`:
  - `ShouldTriggerThreshold` -- edge-triggered, not level-triggered: fires once when the
    watched quantity reaches/crosses `thresholdQuantity`, publishes
    `ThresholdCrossedEvent` (declared since M2, never published until now), then stays
    disarmed until the quantity dips back below and re-crosses. A level-triggered
    version (fire every tick while at/above threshold) was considered and rejected --
    it would just degenerate into `AlwaysOn` once supply exceeds consumption.
  - `OnEnable`/`OnDisable`/`OnGolemCompletedForSignal` -- subscribes to
    `EventBus.GolemCompleted`, and when the event's `GolemId` matches this golem's
    `logicCore.signalGolemId`, latches `PendingSignal = true`. `ShouldTrigger`'s Signal
    case consumes and resets it. A signal arriving while this golem is mid-cycle (not
    Idle) is queued rather than dropped -- but multiple signals arriving while busy
    coalesce into a single pending fire, they don't queue individually.
  - **Important gotcha this uncovered**: `GolemEntity` has no `[ExecuteAlways]`, so
    Unity does not invoke `OnEnable`/`OnDisable` for it in EditMode (only in Play
    Mode) -- meaning Signal-trigger tests must run as PlayMode tests, not EditMode.
    Threshold has no such requirement since it doesn't depend on a lifecycle callback.
    See Testing below.
- `Golems/HardcodedDemoProgram.cs` gains `ThresholdRefine(...)` and `SignalShip(...)`.
  `SignalShip`'s step is a same-item-type `Refine` (a degenerate 1:1 recipe) rather than
  a new appendage type -- there's no dedicated buffer-to-buffer "move" action, and
  inventing one just for a "ship into storage" demo step wasn't worth it.
- `Golems/TriggerDemoBootstrap.cs` -- new, additive alongside `BeltDemoBootstrap` (this
  is a new mechanic, not a continuation of the M4/M5 Scrap/Aether chains, so it gets its
  own bootstrap the way M4's did relative to M2/M3's). Golem E continuously hauls Scrap
  (`ExtractThenLoad`) into a dedicated `TriggerScrapBuffer` (kept separate from M4/M5's
  shared `ScrapBuffer` so this demo's threshold-crossing pace isn't drowned out by that
  chain's much larger throughput); reuses the shared `Conveyor`/`Nodes`/`Buffers`
  GameObjects (in particular the infinite `ScrapNode` M5's bootstrap already registers)
  rather than duplicating that infrastructure. In practice the threshold fires
  repeatedly, not just once: Golem F's refine always consumes exactly 1 Scrap per
  firing, which reliably dips `TriggerScrapBuffer` one unit below the threshold every
  time, guaranteeing re-arming regardless of Golem E's supply rate -- a live run showed
  300+ full Extract→Threshold→Refine→Signal→Ship cycles with zero errors.

### M7 manual editor setup (done, via live Unity MCP)
1. Created `GolemE`/`GolemF`/`GolemG` (`GolemEntity`) and `TriggerDemoBootstrap`,
   wired `golemE`/`golemF`/`golemG` and the shared `Conveyor`/`Nodes`/`Buffers`/
   `SimulationClockRunner` references.
2. First live run revealed a real bug: `TriggerDemoBootstrap` called
   `golemE.ConfigureEconomy(...)` but never `golemE.Configure(...)`, so Golem E's
   `conveyorHolder` stayed null and it stalled forever on step 0 (`ExtractFromNode`
   silently fails without a conveyor holder). Fixed by adding the missing `Configure`
   call; re-verified live afterward.
3. Also hit a genuine Unity Editor hang mid-session: entering Play mode got stuck with
   `play_mode.is_changing: true` for 100+ seconds (nothing ticking, `SimulationClock`
   frozen at tick 0). Exiting Play mode (`manage_editor` stop) and re-entering cleared
   it. Not a code issue -- flagged here in case it recurs.
4. Saved, entered Play mode, and confirmed via
   `mcpforunity://scene/gameobject/{id}/components` that after ~15s,
   `TriggerScrapBuffer`/`TriggerBrassBuffer`/`ShippedBuffer` all existed with sane,
   internally-consistent values (e.g. `ShippedBuffer` growing continuously), plus a
   `manage_camera` screenshot confirming the inventory/alerts panels rendered correctly
   -- including the M6 `AlertsPanel` picking up Golem E's stalls automatically, with no
   changes needed on the M6 side, since it's driven purely by `EventBus`.
5. Exited Play mode and re-saved.

### Testing
- EditMode (`Tests/EditMode/Golems/GolemTriggerTests.cs`, Threshold only): below-
  threshold doesn't fire; at/above fires once and publishes `ThresholdCrossedEvent`;
  staying above doesn't refire every tick; dipping below then re-crossing fires again.
- PlayMode (`Tests/PlayMode/Golems/GolemSignalTriggerTests.cs`, Signal only -- needs
  Play Mode for `OnEnable` to actually subscribe, see the gotcha above): an unrelated
  golem completing doesn't fire; the watched golem completing does; the pending signal
  is consumed and doesn't refire without a new event; a signal arriving mid-cycle is
  queued and fires on the next Idle check.
- Manual: verified in-Editor via live MCP calls and a screenshot, described above --
  a genuine end-to-end run of the full trigger chain, not a simulated/rigged one.

## M8 implementation notes (Artificer Focus meter + full Workbench UI)

This is the largest milestone so far and the first to touch real UGUI (Canvas +
EventSystem + drag-and-drop) -- every prior milestone's UI was `OnGUI` immediate mode.

### Code (done)
- `Player/ArtificerFocusMeter.cs`/`ArtificerFocusMeterHolder.cs` -- a resource
  distinct from `SimulationClock`, regenerating on wall-clock time (`Update`, not
  ticks) per the design doc. `TryConsume`/`Refund` (the latter added after a real bug:
  the first draft tried to "refund" via `TryConsume(-amount)`, which `TryConsume`'s own
  non-negative guard silently rejects -- caught by a dedicated `Refund` method plus
  tests, not by manual inspection).
- `Blueprints/Blueprint.cs`/`PatentRegistry.cs`/`PatentRegistryHolder.cs` -- headless,
  per the "multiplayer-compatible seams" section: `Blueprint` carries `OwnerId` from
  day one, `TryUseBlueprint` already has the royalty-charge branch (a documented no-op
  in solo v1, since there's no other player's wallet to pay into). No browse/reuse UI
  -- that's M9's explicit scope.
- `Buildings/AssemblyBayStructure.cs` -- `TryAssignGolem`/`ReleaseGolem` capacity
  bookkeeping, `TryUpgrade` (withdraws Scrap then Brass from a `StorageBufferRegistry`
  buffer, refunding the Scrap if the Brass withdrawal fails so a failed upgrade never
  partially charges). Capacity/upgrade data model only, not the Assembly Line drafting
  loop (M9 stretch scope).
- `UI/WorkbenchDropZone.cs` -- marks a slot GameObject (the Logic Core slot, or one of
  N appendage slots) as a valid drop target.
- `UI/WorkbenchCard.cs` -- `IBeginDragHandler`/`IDragHandler`/`IEndDragHandler`. Purely
  reports "this card, dropped on this zone (or null)" to the controller; doesn't touch
  program state itself.
- `UI/WorkbenchController.cs` -- the orchestrator, and the biggest design decision in
  this milestone: dragging cards only edits a local **draft** copy of the program
  (`_draftChassis`/`_draftLogicCore`/`_draftAppendages`), never the real
  `GolemEntity.Program`, until `EngageGears()` commits it -- matching the design doc's
  "pulling it locks in the current card configuration and boots the golem into the
  game world." `HandleDrop` only ever mutates draft data, then calls `RebuildUI()`,
  which destroys and recreates every card GameObject from that data -- deliberately
  choosing "always re-render from data" over choreographing GameObject reparenting
  per-drag, matching the plain-data-first idiom the rest of the project already
  follows (`BeltSegmentVisual` redrawing from `BeltSegment.Items` rather than
  persistent per-item GameObjects). Chassis selection stays button-based (not a
  draggable card), since the design doc's card color coding only covers Logic
  Cores/Appendages. `ConfigureGolem`/`ConfigureSystems`/`ConfigureRoster`/`ConfigureUI`
  are test/bootstrap-friendly setters mirroring `GolemEntity.Configure` -- necessary
  here specifically because the component has far too many `[SerializeField]`s to wire
  any other way from a test or a bootstrap script.
- `UI/InventoryPanel.cs` (M5) relocated from the top-right to the top-left corner and
  capped to a fixed height (was full-screen) -- see "Bug found live" below.
- `docs/digital-design.md`'s "sell/ship building" and "reprogramming"/"patenting"
  language doesn't map onto a dedicated appendage type for buffer-to-buffer transfer
  (M7's `SignalShip` degenerate-Refine trick handled that one already); nothing new
  needed here.

### Bugs found via live verification (not caught by unit tests alone)
1. **OnGUI always draws over Canvas UGUI, regardless of sort order.** M5's
   `InventoryPanel` (top-right, full height) visually collided with M8's new Card
   Vault (also right-anchored). Moving it to the top-left (freed up by
   `GolemProgrammingPanel` being disabled) just exposed the *same* problem against the
   Blueprint Viewport's left column instead, since the new Workbench's three columns
   are all full-height. Final fix: cap `InventoryPanel` to a small fixed-height box
   (250x220) in the top-left, accepting a small remaining corner overlap as a known
   cosmetic trim rather than a full HUD layout redesign -- OnGUI and UGUI use
   completely separate input pipelines, so this is purely visual, not a functional
   blocker (dragging/clicking still works underneath).
2. `ArtificerFocusMeter.Refund` via `TryConsume(-amount)` silently no-ops (see Code
   above) -- caught before it ever reached the scene, while writing
   `WorkbenchController.EngageGears`'s defensive chassis-rejection path.

### M8 manual editor setup (done, via live Unity MCP + `execute_code`)
Given the sheer number of `RectTransform`-positioned GameObjects a real UGUI layout
needs (Canvas, EventSystem, ~20 child elements with anchors/sizes), building this one
`manage_gameobject`/`manage_components` call at a time would have been slow and
error-prone with no visual feedback until the end. Instead, the whole hierarchy was
built in a single `execute_code` call -- a C# script run directly in the Editor
(Canvas + `CanvasScaler` + `GraphicRaycaster`, `EventSystem` +
`InputSystemUIInputModule` -- the project's `activeInputHandler` is New-Input-System-
only, so the legacy `StandaloneInputModule` would not have worked -- three anchored
columns, `VerticalLayoutGroup`s for the vault/chassis row, a `DragLayer` added last so
dragged cards render on top). Notably, `execute_code` fell back to the CodeDom (C# 6)
compiler rather than Roslyn, so the script avoided local functions/lambda-heavy
patterns that wouldn't compile under C# 6.
1. Loaded the M3-authored Chassis/LogicCore/Appendage roster assets, found the
   existing `Golem` GameObject (M3's `GolemProgrammingPanel` target) to reuse as the
   Workbench's target -- same reasoning as disabling `GolemDemoBootstrap` for
   `BeltDemoBootstrap` at M4: don't run two systems that both drive the same golem.
2. Built the Canvas hierarchy and `WorkbenchController`, wired everything via the
   `Configure*` methods (called directly in code, not via `manage_components`'
   property-setting, which is far more reliable for arrays/object references).
   Created `AssemblyBay`, `FocusMeter`, `Patents` holders; disabled
   `GolemProgrammingPanel`.
3. Saved, entered Play mode, screenshotted -- confirmed the panel renders and found
   bug #1 above.
4. Used a second `execute_code` call to *drive the actual UI live*: clicked a chassis
   button, called `WorkbenchController.HandleDrop` directly on the real vault cards
   found by name, clicked Engage Gears and Patent, then read back `GolemEntity.Program`
   and `PatentRegistry.Blueprints` -- confirming a genuine end-to-end commit
   (`chassis=ClockworkScavenger logicCore=AlwaysOnCore appendages=ExtractScrap,
   blueprintCount=1`), not just that the UI renders. Re-verified after the
   `InventoryPanel` fix with a final screenshot and a clean console.
5. Exited Play mode and re-saved.

### Testing
- EditMode: `ArtificerFocusMeterTests.cs` (consume/refund/regen, including the
  negative-amount edge cases from bug #2), `PatentRegistryTests.cs` (patent/duplicate-
  id/unknown-id/royalty-no-op), `AssemblyBayStructureTests.cs` (assign/release/
  capacity, upgrade success/insufficient-Scrap/insufficient-Brass-with-refund).
- PlayMode (`Tests/PlayMode/UI/WorkbenchControllerTests.cs` -- needs Play Mode since
  `WorkbenchController.Start()`, like `GolemEntity.OnEnable` in M7, doesn't run in
  EditMode): exercises `HandleDrop`/`EngageGears`/`Patent`/`SelectChassis` directly
  with constructed `WorkbenchCard`/`WorkbenchDropZone` instances rather than
  simulating real pointer drags through the `EventSystem`/`GraphicRaycaster` -- that
  plumbing is thin, low-risk Unity event wiring; the logic worth testing is what
  `HandleDrop` decides to do with a `(card, zone)` pair, which doesn't require an
  actual drag. Covers: commit-on-engage, insufficient-Focus rejection (both for
  reprogramming and patenting), moving a card between appendage slots, clearing a
  slot by dropping in empty space, rejecting a drop onto a slot beyond the current
  chassis's capacity, and rejecting a chassis swap that wouldn't fit the current
  draft's appendage count.
- Manual: verified in-Editor via live MCP calls (including a scripted live drive of
  the actual UI, not just the underlying logic) and screenshots, described above.

## Graphics demo implementation notes

Through M8 the simulation, golem programming, belts, economy, triggers, and Workbench
UI all work, but almost none of it is visible: `GolemEntity` has no renderer,
`PlaceableBuilding`/`AssemblyBayStructure` use Unity's built-in default sprite, and
`Assets/_Project/Art`/`Tilemaps` are empty except for `.gitkeep`. This pass gives the
already-working M2/M4/M5/M7 demo scenarios (all of which run simultaneously in
`Main.unity` via `GolemDemoBootstrap`/`BeltDemoBootstrap`/`TriggerDemoBootstrap`) an
actual visual presentation, without touching any simulation code.

Authored from a session with **no Unity Editor and no image-generation tool
available** (unlike M8, there was no live MCP bridge this time) — see the two
constraints below, which shaped the split between what's committed as code/assets vs.
what needs one manual pass in the Editor.

- **No source of bespoke pixel art.** `ConceptArt/`'s `golem lineup.png`/
  `workshop.png` are polished reference illustrations, not usable game assets — no
  transparent backgrounds, not tile-aligned, not isolated per-unit. Instead,
  `Tools/Art/generate_placeholder_art.py` (Python + Pillow) generates simple,
  intentional placeholder sprites in the warm brass/copper palette from
  `docs/digital-design.md`, committed to `Assets/_Project/Art/`: `floor_tile.png` /
  `floor_tile_accent.png` (128×64 isometric diamonds matching
  `GridCoordinateConverter`'s `1 × 0.5` cell size), `golem_generic_{copper,brass,
  steel}.png` (generic robot silhouette, palette-swapped so six simultaneous golem
  instances don't look identical), `building_block.png`, `item_scrap.png` /
  `item_brass.png`, and `ghost_placeholder.png`. These are explicitly placeholders —
  swapping in real pixel art later is a pure asset replacement; no script here
  references a specific file by anything other than its role.
- **No Unity Editor in this session**, so no way to generate the `.meta`/GUID a PNG
  needs before anything can reference it as a `Sprite`. This is the same constraint
  M1 hit first (see "Manual Editor setup" above) and the same fix applies: pure C#
  is committed directly, all sprite-reference wiring becomes a one-time manual
  checklist.

### Code (committed, testable without Unity where possible)
- `World/YSortUtility.cs` — pure static `ComputeSortingOrder(float worldY)`,
  extracted the same way `GridCoordinateConverter` separates math from the
  MonoBehaviour that applies it, so it's covered by
  `Tests/EditMode/World/YSortUtilityTests.cs` without needing a scene.
- `World/YSortSpriteRenderer.cs` — sets `SpriteRenderer.sortingOrder` from
  `YSortUtility` every `LateUpdate`; same visual-only, simulation-untouched idiom
  `Belts/BeltSegmentVisual.cs` already uses. Drop onto any golem/building/item
  sprite so isometric depth looks right without hand-tuning sort order per object.
- `Golems/GolemVisual.cs` — assigns a golem's placeholder sprite once and tints it
  red while `Stalled`/back to white on resume, via the same
  `EventBus.GolemStalled`/`GolemResumed` subscription `UI/GolemStallIndicator.cs`
  already uses. Reads `GolemEntity` only for its id; never writes to it.

### Manual Editor setup (can't be authored from git alone)
1. Pull the branch, open `Main.unity` in Unity 6, let the new
   `Assets/_Project/Art/*.png` import; confirm no Console errors.
2. For each new PNG: Texture Type = Sprite (2D and UI), Pixels Per Unit = 64, Filter
   Mode = Point, Compression = None; Apply.
3. `Window > 2D > Tile Palette`: create Tile assets from `floor_tile.png`/
   `floor_tile_accent.png`, paint the scene's existing `Grid`/`Tilemap` GameObject
   under the golems/buildings.
4. On GolemB/C/D/E/F/G: add `SpriteRenderer` + `GolemVisual` (assign a
   `golem_generic_*` variant + the `GolemEntity` reference) + `YSortSpriteRenderer`.
5. On `PlaceholderBuilding.prefab` and any `AssemblyBayStructure` instances: assign
   `building_block.png` to the existing `SpriteRenderer`; add `YSortSpriteRenderer`.
6. On each `BeltSegmentVisual` instance (M4/M5/M7 belts): assign `item_scrap.png`/
   `item_brass.png` to `Item Sprite`.
7. On the `BuildMode` ghost object: assign `ghost_placeholder.png`.
8. Frame `Main Camera`/`CameraRig`'s starting position + `orthographicSize` so the
   painted floor and golems are in view on load.
9. Save scene + prefabs; commit `Assets/_Project/Art/**` (now with generated
   `.meta`s), `Main.unity`, and any changed prefabs.

### Testing
- EditMode: `Tests/EditMode/World/YSortUtilityTests.cs` — sign/zero/ordering of
  `ComputeSortingOrder`.
- Manual: everything in the checklist above, run once in-Editor by whoever picks
  this branch up next — not automatable from a session with no Unity install.
