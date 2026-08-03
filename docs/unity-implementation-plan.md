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
- **M9 (stretch, done)** — Solo Assembly Line drafting loop (`AssemblyLineState`:
  slots with decaying Scrap cost, claim, drip-feed refill from a cycling pool),
  Blueprint/Patent Registry UI (browse + load a patented blueprint back into the
  Workbench's draft), and JSON save/load (buffers, Focus, patents, every golem's
  program). The Assembly Line doesn't gate the Workbench's card roster yet -- see its
  implementation notes below for why that integration was deliberately deferred.

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

### Manual Editor setup (done, via live Unity MCP + `execute_code`)
Run for real in a later session that had a live Editor connection (unlike the one
that authored the code/art above), the same way M8's Workbench UI hierarchy was
built -- via `execute_code` rather than dozens of individual `manage_gameobject`/
`manage_components` calls, since precise `TextureImporter`/`Tilemap`/`SpriteRenderer`
wiring is much more reliable as one C# script than as many small tool round-trips.
1. Pulled the branch; Unity imported the 9 new PNGs automatically (confirmed via
   `.meta` files appearing and a clean console).
2. Set import settings (`TextureImporterType.Sprite`, PPU 64, `FilterMode.Point`,
   `TextureImporterCompression.Uncompressed`, mipmaps off, alpha-is-transparency)
   on all 9 via a script driving `TextureImporter` directly -- the
   `manage_texture(set_import_settings)` MCP tool itself didn't accept its own
   `import_settings` parameter correctly (silently dropped to an empty object),
   so this used `execute_code` instead rather than fighting that tool further.
3. Created `Tile` assets (`Assets/_Project/Tilemaps/FloorTile.asset`/
   `FloorTileAccent.asset`) from the two floor sprites and painted a 13×13 diamond
   onto the existing `Grid/Tilemap` (accent tiles scattered on an `(x+y) % 4 == 0`
   pattern for visual variety) -- done via `Tilemap.SetTile` in code rather than the
   Tile Palette window, which has no MCP equivalent.
4. Wired all seven golems (`Golem`/`GolemB`–`GolemG`, not just B–G as originally
   scoped -- the original `Golem` needed the same treatment): added
   `SpriteRenderer`/`GolemVisual`/`YSortSpriteRenderer`, assigned a palette-swapped
   `golem_generic_*` sprite + the `GolemEntity` reference (via `SerializedObject`,
   since those are private `[SerializeField]`s), and moved each to a distinct
   `GridCoordinateConverter` cell so they don't all render stacked at the origin
   (their bootstraps never assigned world positions -- purely a simulation-logic
   demo through M8, positions didn't matter until now).
5. Same treatment for the single live `AssemblyBay` instance (no
   `PlaceholderBuilding.prefab` exists in this project -- M1's placeholder is a
   plain `PlaceableBuilding` GameObject, not a prefab -- so this wired the scene
   instance directly instead).
6. **Skipped deliberately**: no `BeltSegmentVisual` instances exist anywhere in this
   project -- M4 and M5 both explicitly deferred creating them (see their own
   implementation notes above), so this step in the original checklist assumed
   objects that were never built. Belts remain invisible; only golems/buildings/
   floor/ghost got sprites this pass. A future pass can add `BeltSegmentVisual`
   GameObjects with `Item Sprite` set to `item_scrap.png`/`item_brass.png` once
   someone actually wants belt items to render.
7. Assigned `ghost_placeholder.png` to the `BuildMode` ghost's existing
   `SpriteRenderer`.
8. Framed `Main Camera`/`CameraRig` on the golem cluster's centroid at
   `orthographicSize = 5`.
9. Saved the scene. A real gotcha hit here: the first Edit-mode screenshot showed
   the tilemap, ghost, and building sprite rendering correctly but **no golem
   sprites at all** -- `GolemVisual.Awake()` (which copies its serialized `sprite`
   field onto the `SpriteRenderer`) doesn't run outside Play Mode, same
   `[ExecuteAlways]` gotcha M7 hit with `GolemEntity.OnEnable`. Confirmed by
   entering Play mode and re-screenshotting: all seven golems render correctly,
   "Alerts: All golems running" with zero stalls, zero console errors.

### Testing
- EditMode: `Tests/EditMode/World/YSortUtilityTests.cs` — sign/zero/ordering of
  `ComputeSortingOrder`.
- Manual: verified in-Editor via live MCP calls and Play-mode screenshots,
  described above — confirmed the golems actually render (not just that the scene
  saves without errors), catching the `Awake()`-in-EditMode gotcha along the way.
- Full regression: 132/132 tests still pass (105 EditMode + 27 PlayMode) after this
  pass, confirming the wiring didn't disturb any simulation logic.

## M9 implementation notes (Assembly Line, Patent Registry UI, save/load)

Stretch scope per the plan, so trimmed more freely than M5-M8: the Assembly Line ships
as a genuine, tested, playable economy loop, but doesn't (yet) gate what the Workbench
shows -- see the deferred-integration note below.

### Code (done)
- `AssemblyLine/DraftableCardDefinition.cs` (SO) -- wraps exactly one of Chassis/
  LogicCore/Appendage (extends `UI/WorkbenchCard`'s one-of-two pattern to three,
  since the Assembly Line drafts chassis too, unlike the Workbench's cards) plus a
  `baseCost`/`decayPerSecond`/`minCost`.
- `AssemblyLine/AssemblyLineState.cs` -- a fixed number of slots, each holding a card
  whose Scrap cost decays the longer it sits (`GetCurrentCost`), `TryClaimSlot`
  (keyed by `userId` from day one, same convention as `PatentRegistry`) withdraws the
  *current* decayed cost, then refills that slot from a candidate pool that cycles
  forever rather than depleting -- matches the sandbox's "no forced end condition"
  design (same infinite-resource precedent as the demo's `ScrapNode`).
- `AssemblyLine/AssemblyLineStateHolder.cs` -- built its `State` via a field
  initializer, not `Awake()`, on purpose: `Awake` doesn't run outside Play Mode for a
  plain `MonoBehaviour` (the same gotcha M7's `GolemEntity.OnEnable` and the
  graphics-wiring pass's `GolemVisual.Awake` both hit), and EditMode tests need a
  working `State` immediately after `AddComponent`.
- `AssemblyLine/AssemblyLineDemoBootstrap.cs` -- seeds the line with a
  `DraftableCardDefinition` for every M3-authored roster asset, built at runtime the
  same way `HardcodedDemoProgram` builds its programs (no pre-authored `.asset`
  files needed for wrappers this mechanical).
- `UI/AssemblyLinePanel.cs` -- `OnGUI` browse-and-claim panel, same style as
  `AlertsPanel`/`InventoryPanel`.
- `WorkbenchController.LoadBlueprintIntoDraft(Blueprint)` -- the other half of M8's
  `Patent()`: loads a patented blueprint's chassis/logic core/appendages into the
  *draft*, not the real `GolemEntity.Program` -- still has to go through Engage Gears
  (and its Focus cost) to take effect, same as a manually-dragged configuration.
- `UI/PatentBrowserPanel.cs` -- `OnGUI` list of patented blueprints with a Load
  button per entry, calling the method above.
- `Save/SaveData.cs`/`DefinitionCatalog.cs`/`SaveLoadService.cs`/`SaveFileIO.cs` --
  `JsonUtility` can't serialize `Dictionary` or a polymorphic `ScriptableObject`
  reference, so buffer contents are parallel lists and every asset reference is
  stored as its name, resolved back via `DefinitionCatalog` (built from whatever
  roster the caller already has, not an `AssetDatabase` search, so it works
  identically in a build). `SaveLoadService.CaptureState`/`RestoreState` are pure
  logic with no file I/O and no `MonoBehaviour` dependency, so they're fully
  EditMode-testable; `SaveFileIO` is a thin `JsonUtility` + `File` wrapper kept
  separate specifically so the logic above stays disk-free and testable.
  Deliberately excludes belt contents/positions and tick count -- a "continue where
  you left off" save of the economy/golem-programs, not a byte-for-byte simulation
  snapshot. Golems no longer present in the scene at load time are silently skipped
  (there's no "spawn a new one" concept for a save file to invent).
- `UI/SaveLoadPanel.cs` -- `OnGUI` Save/Load buttons, bottom-right.
- `ArtificerFocusMeter.SetCurrent(float)` -- added because restoring an exact saved
  Focus value needs it: `Refund` only ever *increases* `CurrentFocus` (by design, for
  the M8 chassis-rejection rollback path), which can't express "the save says 10 but
  we're currently at 100."
- `Economy/StorageBufferRegistry.Clear()` -- added mid-verification; see the bug
  below.

### Deferred: Assembly Line doesn't gate the Workbench (yet)
The tabletop mechanic (`docs/game-design.md`) is a competitive draft; "claim a card"
should plausibly mean "now it's available in your Workbench roster," not just "now
it's available" (everything already is, via the M3-authored roster passed straight
into `WorkbenchController.ConfigureRoster`). Wiring that properly -- the vault
showing only claimed cards, refreshing live as new ones are claimed -- would touch
already-tested M8 behavior for a stretch-scope milestone, so this pass keeps the
Assembly Line as a standalone, fully-functional economy loop (real cost decay, real
claiming, real drip-feed) without yet rewiring what it unlocks. A natural follow-up,
not done here.

### Bugs found via live verification
1. **The Assembly Line/save-load itself uncovered a real Unity Editor hang**, not a
   code bug: entering Play mode got stuck with `play_mode.is_changing: true` for 50+
   seconds multiple times in a row (same class of hang M7 hit once). Stop/re-enter
   didn't reliably clear it this time; needed the user to bring the Editor window to
   OS focus before a subsequent Play attempt actually ran. `PlayerSettings
   .runInBackground` was also enabled while investigating (a reasonable permanent
   setting for a project that gets driven headlessly like this, even though it
   turned out not to be the actual fix for this particular hang).
2. **`SaveLoadService.RestoreState` merged instead of replaced buffer state** --
   found by mutating a buffer between Save and Load and confirming Load didn't
   actually restore the pre-mutation value (`scrapAfterLoad` came out as
   `scrapAfterMutate + scrapBeforeSave`, not `scrapBeforeSave`). `StorageBufferRegistry
   .Deposit` is additive by design (matches golems continuously depositing during
   normal play), so replaying every saved deposit on top of live state double-counts
   anything already there. Fixed by adding `StorageBufferRegistry.Clear()` and
   calling it before restoring buffer entries; both the unit test
   (`RestoreState_ReplacesExistingBufferState_DoesNotMergeWithIt`) and a live
   before/mutate/load/after check confirm the fix.
3. Two tests (`TryClaimSlot_RefillsSlot_FromCyclingPool`,
   `TryClaimSlot_RefilledSlot_DecayTimerResets`) never funded the test wallet buffer,
   so `TryClaimSlot` silently failed and the assertions passed for the wrong reason
   (checking state that would've looked identical whether the claim succeeded or
   not) -- caught by an actual EditMode test run flagging a *third*, unrelated test
   with the same root cause, not by inspection.

### M9 manual editor setup (done, via live Unity MCP + `execute_code`)
1. Loaded the same M3-authored roster assets M8's script did, created
   `AssemblyLine` (`AssemblyLineStateHolder`) and `AssemblyLineDemoBootstrap` (wired
   to the roster), `AssemblyLinePanel` (wired to the line + `Buffers`, wallet =
   `ScrapBuffer`), `PatentBrowserPanel` (wired to `Patents` + `WorkbenchController`),
   and `SaveLoadPanel` (wired via its `Configure(...)` method, mirroring
   `GolemEntity.Configure`/`ConfigureEconomy` and M8's `WorkbenchController
   .Configure*` methods -- necessary here too since the panel has several
   `[SerializeField]`s no test or bootstrap can reach any other way).
2. Saved, entered Play mode, and live-drove the actual UI via `execute_code` end to
   end: claimed a card off the Assembly Line (confirmed Scrap withdrawn at the
   correctly-decayed cost), dropped cards onto the Workbench and clicked Patent
   (confirmed a blueprint appeared in `PatentRegistry`), called
   `LoadBlueprintIntoDraft` on it and clicked Engage Gears (confirmed the golem's
   `Program` matched the patented config), then Saved, mutated a buffer, and Loaded
   (confirmed the mutation was discarded and the saved value restored exactly) --
   see the bugs found above, both caught this way rather than by inspection.
3. Exited Play mode, re-saved.

### Testing
- EditMode: `Tests/EditMode/AssemblyLine/AssemblyLineStateTests.cs` (seed/decay/
  claim success+insufficient-funds/refill-cycles/decay-timer-resets/unknown-user),
  `Tests/EditMode/Save/SaveLoadServiceTests.cs` (capture, restore, the
  merge-vs-replace regression, Focus restoring both up and down, blueprint and
  golem-program round-trips via `DefinitionCatalog`, a since-removed golem being
  skipped without error), `Tests/EditMode/Save/SaveFileIOTests.cs` (file round-trip,
  missing-file returns null), plus a new `Clear()` case in
  `Tests/EditMode/Economy/StorageBufferTests.cs`.
- PlayMode: extended `Tests/PlayMode/UI/WorkbenchControllerTests.cs` with
  `LoadBlueprintIntoDraft` commit and null-blueprint-is-a-no-op cases.
- Manual: verified in-Editor via live MCP calls and a screenshot, described above --
  a genuine scripted drive of the real UI (button clicks, `HandleDrop` calls,
  reflection-invoked `SaveLoadPanel.Save`/`Load`), not just the underlying logic in
  isolation.
- Full regression: 152/152 tests pass (123 EditMode + 29 PlayMode).

## Player-driven starting scenario implementation notes

Through M8/the graphics pass, `Main.unity` has a fully working simulation but **no
player** — every demo golem is hand-wired and runs automatically the instant Play mode
starts. This pass adds an actual playable front door: a character that walks around,
harvests resources by hand, spends them on buildings, and constructs+programs its own
golems, in a new `Sandbox.unity` scene that reuses `Main.unity`'s systems unchanged.

### Code (done)
- `Player/PlayerMovement.cs`/`PlayerController.cs` — pure-math `ComputeDisplacement`
  (same "extract the math" idiom as `GridCoordinateConverter`) plus a thin
  MonoBehaviour that applies it directly to `transform.position` — analog movement,
  not grid-locked (only golems are grid-locked per `game-design.md`).
- `World/ResourceNodeMarker.cs` — spatial proxy for one `ResourceNode`; `TryHarvest`
  forwards to the same `ResourceNodeRegistry.TryExtract` a golem's `ExtractFromNode`
  step already calls, so a player harvesting and a golem extracting from the same node
  genuinely compete for `RemainingQuantity` — an emergent property, not a conflict to
  resolve.
- `Player/PlayerInteractor.cs` — finds the nearest interactable (node marker, golem
  construction station, or existing golem) within range and acts on it via a new
  `Interact` action: harvest-and-deposit, open the construction panel, or
  `WorkbenchController.RetargetGolem`. The three kinds don't share an interface, so
  they're three separately-cached arrays rather than an artificial abstraction.
- `Buildings/GolemConstructionStation.cs` — a sibling component (`PlaceableBuilding`
  is `sealed`) that spends a chassis's `scrapCost`/`brassCost` — populated on every
  chassis asset since M3 but read by zero code until now — via a new
  `StorageBufferRegistry.TryWithdrawScrapAndBrass` (centralizes the withdraw-then-
  refund pattern `AssemblyBayStructure.TryUpgrade` already had once), instantiates a
  bare-chassis `GolemEntity`, registers it with the clock, and retargets the Workbench
  onto it. **Real bug caught while writing the shared withdraw method**: a literal
  zero-cost withdrawal (`TryWithdraw(id, Scrap, 0)`) against a buffer that never
  received a Scrap deposit incorrectly failed, because `StorageBuffer.TryWithdraw`
  rejects on a dictionary miss regardless of the requested amount — fixed by skipping
  the withdraw call entirely when a cost is `<= 0`, locked in with a dedicated test.
- `UI/GolemConstructionPanel.cs`/`UI/BuildMenuPanel.cs` — `OnGUI` panels (same style as
  `GolemProgrammingPanel`, no UGUI needed) listing chassis-with-cost and
  placeable-prefabs-with-cost respectively; the latter just calls the new
  `BuildModeController.SetActivePrefab`.
- `World/SandboxBootstrap.cs` — the scene's only front-door script: registers the
  starting `ResourceNode`s (ids matched by hand-placed markers) and starts the clock.
  Also wires `CameraRigController.SetFollowTarget(player)` once at `Start()`, since
  that method isn't a `[SerializeField]` (same `Configure(...)`-not-Inspector idiom as
  everywhere else) and something has to call it.
- `WorkbenchController.RetargetGolem` — reloads the draft from a different golem and
  rebuilds the UI, without re-running `Start()`'s one-time button/listener wiring.
- `BuildModeController` — gained an optional `StorageBufferRegistryHolder` +
  `SetActivePrefab`; when unconfigured (as in `Main.unity` today) placement stays
  exactly as free as it always was — zero regression risk, confirmed live (below).
- `CameraRigController` — gained `SetFollowTarget(Transform)`; when unset (as in
  `Main.unity`) it reads `Pan` exactly as before.
- `Tools/Art/generate_placeholder_art.py` — added `make_player()`, a human silhouette
  (round head, hat brim, cloak) deliberately distinct from `make_golem()`'s boxy
  tripod-and-eye shape, producing `Assets/_Project/Art/player.png`.

### Manual Editor setup (done, via live Unity MCP + `execute_code`)
1. Extracted `Main.unity`'s Workbench UI (Canvas/EventSystem/`WorkbenchController`)
   and manager holders (`StorageBufferRegistryHolder`, `ResourceNodeRegistryHolder`,
   `ConveyorSystemHolder`, `ArtificerFocusMeterHolder`, `PatentRegistryHolder`,
   `GridMapHolder`, `SimulationClockRunner`) into two prefabs
   (`WorkbenchCanvas.prefab`/`ManagerHolders.prefab`) by reparenting the existing
   scene objects under two new wrapper GameObjects and converting each to a prefab —
   `Main.unity`'s own GameObjects/components keep their identity throughout, so every
   existing serialized reference into them stays valid; confirmed via a full test run
   and a live Play-mode screenshot immediately after (Workbench, inventory, and all
   seven golems rendering identically to before).
2. Created `GolemPrefab.prefab` (`GolemEntity`/`SpriteRenderer`/`GolemVisual`/
   `YSortSpriteRenderer`) by duplicating `Main.unity`'s existing `Golem` GameObject,
   clearing its scene-specific `golemId`/holder references (a fresh prefab shouldn't
   carry a stale link to `Main.unity`'s specific `Nodes`/`Buffers`/`Conveyor`), saving
   it as a prefab, then deleting the staging duplicate from `Main.unity`.
3. **Gotcha confirmed**: a prefab asset cannot hold a field reference to an object in
   a *different* prefab — so `WorkbenchController.focusMeterHolder`/
   `patentRegistryHolder`, which pointed at `Main.unity`'s specific `FocusMeter`/
   `Patents` GameObjects when `WorkbenchCanvas.prefab` was created (before those
   objects were themselves wrapped into `ManagerHolders.prefab`), came back `null` on
   a fresh instantiation into `Sandbox.unity` and had to be re-wired there explicitly.
   `Main.unity` itself was unaffected, since Unity preserves same-scene cross-prefab
   references as an instance-level override, not something baked into either asset.
4. Assembled `Sandbox.unity`: instantiated both prefabs, hand-painted a 13×13 isometric
   floor (`Tilemap.SetTile` in a loop, reusing M8/graphics-pass's `FloorTile.asset`),
   built `Player`/`CameraRig`/`BuildMode`/`Ghost`, created two more small template
   prefabs (`DepotPrefab.prefab` — a paid placeholder building — and
   `GolemConstructionStationPrefab.prefab`, one instance of which is the scene's free
   starter station), three `ResourceNodeMarker`s (Scrap/Brass/Aether — Brass is
   directly harvestable here rather than requiring the refining chain first, so a
   fresh save can afford every chassis without an extra bootstrapping step), and
   wired every cross-reference (chassis roster, golem prefab, stockpile buffer,
   Workbench, camera follow target) via `execute_code` rather than dozens of
   individual property-set calls, then read every wired component back to confirm
   nothing silently resolved to `null`.
5. Live-verified the entire loop in Play mode via `execute_code`, driving the real
   public methods (not simulated input): walked the player to each node and called
   `PlayerInteractor.Interact()` in a loop — confirmed the shared stockpile
   incremented per harvest; interacted with the starter station, opened the
   construction panel, and called `TryConstructGolem` — confirmed the exact chassis
   cost was withdrawn and a real `GolemEntity` was spawned; called
   `BuildModeController.PlaceOrRemove` with insufficient funds (correctly rejected,
   no withdrawal) and then with sufficient funds (correctly withdrew the exact Depot
   cost and occupied the cell); retargeted the Workbench onto the new golem, assigned
   a logic core and appendage directly, advanced the `SimulationClock`, and confirmed
   the golem actually extracted Scrap onto a belt — the same `ResourceNode` the player
   themselves had just harvested from. One benign console message ("PlayerLoop called
   recursively") appeared, traced to invoking gameplay code via `execute_code` while
   Play Mode's own loop was concurrently running — an artifact of that testing
   shortcut, not a bug in the shipped code (it does not appear during normal Play
   Mode entry/exit or anywhere in the automated test suite).
6. Re-ran the full suite and a live Play-mode pass against `Main.unity` unchanged
   afterward (see regression count below).

### Testing
- EditMode: `PlayerMovementTests.cs`, `ResourceNodeMarkerTests.cs`,
  `GolemConstructionStationTests.cs` (construct success/insufficient-Scrap/
  insufficient-Brass-refunds-Scrap — runs fine in EditMode, unlike the
  `Awake`/`OnEnable`-dependent components elsewhere in this project, since
  `Instantiate`/`SimulationClockRunner.Register` don't depend on either), extended
  `StorageBufferTests.cs` (`TryWithdrawScrapAndBrass`, including the zero-cost bug
  above) and `AssemblyBayStructureTests.cs` (unchanged after its `TryUpgrade` refactor
  — doubles as a regression check).
- PlayMode: `PlayerControllerTests.cs`, `PlayerInteractorTests.cs`, extended
  `BuildModeControllerTests.cs` (insufficient/sufficient funds, and an explicit
  unconfigured-stockpile-stays-free regression guard) and `WorkbenchControllerTests.cs`
  (`RetargetGolem` switches the commit target and reloads the draft from the new
  golem's existing program).
- Full regression: 162/162 tests pass (122 EditMode + 40 PlayMode) after this pass,
  and a live Play-mode pass against `Main.unity` (unchanged: same Workbench, inventory,
  alerts, and seven golems rendering and running identically, zero console errors).

### Deliberate scope cuts
1. No player collision/grid-snapping — the player can walk through buildings/golems.
2. No refund on removing a placed building.
3. `AssemblyBayStructure`'s capacity/tier upgrade isn't wired into the Sandbox loop.
4. `BuildMenuPanel` ships with exactly two placeable types (a paid Depot placeholder
   and the Golem Construction Station) — no general building catalog, and no
   "auto-extracting machine" building, since in this game's fiction a programmed golem
   *is* automated collection.
5. No node-depletion visual feedback on `ResourceNodeMarker` — harvesting a depleted
   node just fails with a status message.
6. **Known gap, not fixed here**: `SaveLoadService.RestoreState` can only restore
   programs onto `GolemEntity`s already present in the scene at Load time — it can't
   re-spawn a player's dynamically-constructed golem roster from scratch. Low-stakes
   in `Main.unity` (golems are always scene-resident there) but more visible now that
   `Sandbox.unity` spawns golems at runtime.

## Walkable Main.unity demo + UGUI HUD redesign implementation notes

`Main.unity` (the 7-golem assembly-line demo) had no player or movement at all — only
free camera pan/zoom — and its screen was badly cluttered: `WorkbenchCanvas` was a
full-screen UGUI canvas with no show/hide concept, always on top of five overlapping
`OnGUI` corner panels (`InventoryPanel`/`AssemblyLinePanel`/`PatentBrowserPanel`/
`AlertsPanel`/`SaveLoadPanel`) — the exact clutter the "Bugs found via live
verification" note under M8 already flagged as deferred "cosmetic trim." This pass
makes the demo walkable and gives it a real, uncluttered HUD.

### Code (done)
- `World/MainSceneBootstrap.cs` — `Main.unity`'s only player-facing bootstrap, mirrors
  `SandboxBootstrap.cs` trimmed to its one actual job here: wiring
  `CameraRigController.SetFollowTarget(player)` once at `Start()` (everything else the
  scene needs is already seeded by the existing `BeltDemoBootstrap`/
  `TriggerDemoBootstrap`/`AssemblyLineDemoBootstrap`).
- `UI/WorkbenchController.cs` — gained `IsOpen`/`Open()`/`Close()` plus
  `canvasRoot`/`closeButton`/`managementPanel`/`constructionPanel` fields
  (`ConfigureVisibility(...)` sets them programmatically, same idiom as every other
  `Configure*` method on this class). `Open()` closes the other two HUD screens for
  mutual exclusion; `Start()` now always calls `Close()` — a no-op on `IsOpen`
  bookkeeping only when `canvasRoot` is unset, so every pre-existing caller/test that
  never wires it (including `WorkbenchControllerTests.Build()`) is unaffected.
- `UI/GolemConstructionPanel.cs` — gained the same `workbenchController`/
  `managementPanel` cross-refs so its own `Open()` closes them too; stays `OnGUI` (it
  was already correctly `IsOpen`-gated, so there's no overlap risk to solve by
  converting it).
- `Player/PlayerInteractor.cs` — `TryProgram` now calls `_workbenchController.Open()`
  immediately before `RetargetGolem`, so walking up to a golem actually reveals the
  Workbench instead of silently retargeting an already-visible one.
- `UI/ManagementPanel.cs` (new) — consolidates the four small always-on panels into
  one tabbed, toggleable UGUI screen (`Tab` key, new `ToggleMenu` input action).
  Lives on its own always-active `ManagementController` GameObject separate from the
  `ManagementScreen` it shows/hides, for the same reason `WorkbenchController` lives
  off its own toggled canvas root: the key listener has to keep running while the
  screen is hidden. Refreshes only the currently active tab, once per frame while
  open — same "clear children, rebuild from data" idiom as
  `WorkbenchController.RebuildUI()`.
- `UI/InventoryPanel.cs`/`AssemblyLinePanel.cs`/`PatentBrowserPanel.cs`/
  `SaveLoadPanel.cs` — converted from self-drawing `OnGUI` panels to `Refresh()`-driven
  UGUI components living inside `ManagementPanel`'s tabs. `PatentBrowserPanel`'s Load
  button now also calls `workbenchController.Open()` (so a loaded blueprint visibly
  lands in the Workbench, now that the Workbench isn't always on-screen already).
- `UI/AlertsPanel.cs` — converted to `AlertsStrip`, a small always-active top-center
  UGUI strip (was the one panel that never overlapped anything as `OnGUI`, converted
  anyway for a single consistent rendering technology rather than one lingering IMGUI
  seam).
- `Input/GolemFactoryInputActions.inputactions` — added `ToggleMenu` (`<Keyboard>/tab`,
  previously unbound) to the `Gameplay` map.

### Manual Editor setup (done, via live Unity MCP + `execute_code`)
1. Restructured the shared `WorkbenchCanvas.prefab` (via
   `PrefabUtility.LoadPrefabContents`/`SaveAsPrefabAsset`, in staged `execute_code`
   passes rather than one large script, verifying the hierarchy after each stage):
   wrapped the existing four Workbench columns under a new `WorkbenchScreen` (capturing
   and reapplying each column's exact anchors/position/size across the reparent, since
   both old and new parents are full-stretch anyway but nothing was left to chance),
   added a `CloseButton`, then built `ManagementScreen` (`Background`/`TabBar` with
   four tab buttons + its own `CloseButton`/`TabContent` with the four tab
   GameObjects — `InventoryTab`/`PatentsTab` use a real `ScrollRect`+`Viewport`+
   `Content`+`VerticalLayoutGroup`+`ContentSizeFitter`, `AssemblyLineTab`/`SaveLoadTab`
   don't need scrolling) and the always-active `AlertsStrip`, all as one shared
   `Canvas`/`EventSystem` rather than a second prefab (Unity expects exactly one active
   `EventSystem`; a second would risk real input-focus conflicts). `SaveLoadPanel`'s
   `chassisRoster`/`logicCoreRoster`/`appendageRoster` were baked directly into the
   prefab using the same asset GUIDs already baked into
   `WorkbenchController.availableChassis/availableLogicCores/availableAppendages` —
   safe since these are asset refs, not scene refs.
2. **Real bug caught via a Play-mode read-back, not by inspection**: reflection
   (`FieldInfo.SetValue`) on a private `[SerializeField]` of a component that's part of
   a scene's *prefab instance* takes effect immediately in memory, but silently reverts
   to the prefab's default on the next scene save/domain reload unless the change is
   also registered as an instance override via
   `PrefabUtility.RecordPrefabInstancePropertyModifications(component)` (plus
   `EditorUtility.SetDirty(component)`). First pass wired `InventoryPanel`/
   `AssemblyLinePanel`/`PatentBrowserPanel`/`SaveLoadPanel`'s per-scene data-source
   holders and `WorkbenchController`/`ManagementPanel`'s `constructionPanel` refs this
   way, saved, and they read back fine *in the same Editor session* — but came back
   `null` after actually entering Play mode (which the running scene doesn't reload
   from disk by default, so this was doubly surprising until re-checking in Edit mode
   confirmed the save itself hadn't captured them). Re-wired with
   `RecordPrefabInstancePropertyModifications`, confirmed present in the raw scene
   YAML (`m_Modifications`/`propertyPath` entries) this time, then re-verified in Play
   mode.
3. Added a plain (non-prefab) `Player` GameObject to `Main.unity`, mirroring
   `Sandbox.unity`'s `Player` exactly: `SpriteRenderer` (`player.png`, already built for
   `Sandbox.unity`), `PlayerController`, `PlayerInteractor` (wired to the scene's
   `ManagerHolders` stockpile, the Workbench's `WorkbenchController`, and a new bare
   `GolemConstructionPanel` added purely for parity — `Main.unity` has no
   `GolemConstructionStation` yet, so that path is a harmless no-op today but needs no
   extra wiring the moment one is added). Added `MainSceneBootstrap`, wired to
   `CameraRig` and the new `Player`.
4. Spread the 7 golems + `AssemblyBay` from their original ~4×2-unit cluster near the
   origin out across the 13×13 floor's full ~12×6-unit footprint (no belt visuals
   exist in the scene, so repositioning is purely cosmetic — zero effect on the
   assembly-line simulation logic).
5. Deleted the five old standalone OnGUI panel GameObjects (superseded by the
   Workbench instance's new `ManagementScreen` tabs) and wired the tabs' per-scene data
   sources (`StorageBufferRegistryHolder`, `AssemblyLineStateHolder`,
   `PatentRegistryHolder`, `ArtificerFocusMeterHolder` — the latter two reused directly
   off `WorkbenchController`'s own already-correct references rather than re-resolved
   independently, to guarantee the same instances).
6. Live-verified end to end in Play mode via `execute_code`: drove
   `PlayerController.MoveBy` directly and confirmed the camera followed; walked next to
   a golem and called `PlayerInteractor.Interact()` — confirmed `WorkbenchController
   .IsOpen` flipped true and it targeted the right golem; toggled `ManagementPanel` and
   confirmed it force-closed the Workbench and vice versa; exercised every
   `ManagementPanel` tab (`Refresh()` populated real inventory/assembly-line rows) and
   the Patents tab's full round-trip (`Patent()` → tab lists it → clicking its real
   `Load` button called `LoadBlueprintIntoDraft` *and* opened the Workbench *and*
   closed Management); clicked the real Save/Load buttons and confirmed the status
   text updated correctly. Took screenshots confirming the default (nothing open),
   Workbench-open, and Management-open states never overlap. Re-verified
   `Sandbox.unity` afterward (harvest → open construction panel → construct attempt),
   confirming the shared prefab's now-hidden-by-default Workbench doesn't regress its
   existing flow there.

### Testing
- New `Tests/PlayMode/UI/ManagementPanelTests.cs`, `InventoryPanelTests.cs`,
  `AssemblyLinePanelTests.cs` (seeds a `DraftableCardDefinition` via
  `AssemblyLineState.SeedCandidates` since the state starts with empty slots by
  default — production seeds it via `AssemblyLineDemoBootstrap`, which the test
  doesn't otherwise need), `PatentBrowserPanelTests.cs`, `SaveLoadPanelTests.cs`
  (writes/cleans up `SaveFileIO.DefaultPath`, same as `SaveFileIOTests`, since the
  panel has no path-injection hook).
- Extended `WorkbenchControllerTests.cs` (`Open`/`Close`/`IsOpen`, mutual exclusion
  with `ManagementPanel`/`GolemConstructionPanel`) and `PlayerInteractorTests.cs`
  (`Interact_GolemInRange_RetargetsWorkbench` now also asserts `workbench.IsOpen`).
- **Gotcha hit writing the new UGUI tests**: `ClearChildren`'s `Destroy()` defers
  actual removal to end-of-frame, so a test that calls `Refresh()` twice back-to-back
  without a `yield return null` in between sees a doubled `childCount` (both the
  about-to-be-destroyed old rows and the new ones coexist for that instant) — fixed by
  yielding once after the second `Refresh()` before asserting, matching how
  `ManagementPanel.Update()` actually calls `Refresh()` once per frame in real usage
  rather than twice synchronously.
- Full regression: 199/199 tests pass (140 EditMode + 59 PlayMode).

### Deliberate scope cuts
1. `Sandbox.unity`'s `ManagementScreen` tabs inherit the shared prefab's shape but
   aren't re-wired with Sandbox-specific data sources here (it has no
   `AssemblyLineStateHolder` at all, and the Inventory/Patents/SaveLoad tabs would need
   their own per-instance holder wiring, same as `Main.unity` just got) — only the
   Workbench's now-hidden-by-default behavior was confirmed to carry over cleanly;
   fully wiring Sandbox's Management HUD is a natural follow-up, not done here.
2. `GolemConstructionPanel` stays `OnGUI` rather than converting to UGUI — no clutter
   problem to fix there today, since it's already interaction-gated and mutually
   exclusive with the UGUI screens.
3. No changes to player movement bounds/collision, camera zoom limits, or the floor's
   painted tile count — "room to move" came entirely from spreading out existing
   objects across the already-painted 13×13 floor, not from enlarging the world.

## Steampunk & Fantasy UI Pack reskin implementation notes

The user dropped a "Steampunk & Fantasy UI Pack" (free-use, by Scott Jenkins) into a
`temp/` folder for review. Renamed that folder to `_incoming/` first — `temp/` collides
with Unity's own auto-generated `Temp/` engine scratch folder (Burst cache, lockfiles)
under Windows' case-insensitive filesystem, and is covered by `.gitignore`'s `[Tt]emp/`
pattern, so anything dropped there was both gitignored locally and at risk of being
clobbered by the Editor. Then reskinned the flat-colored HUD built in the previous pass
with curated sprites from the pack.

### Code (done)
- Cherry-picked ~21 sprites into `Assets/_Project/Art/UI/Steampunk/` (steampunk button
  accept/cancel/exit/blank in iron/gold/normal finishes with pressed/highlight states,
  iron/bolted panel textures, gears, gauge) — imported fresh with this project's own
  Sprite (2D and UI) conventions rather than the pack's own `.unitypackage` metadata,
  with a 9-slice `spriteBorder` on panel/button textures. Skipped the pack's parallel
  non-steampunk "fantasy" set and its Tilemap-oriented `Tile` assets/prefab (built for a
  workflow this project doesn't use). The `.psd` source and `ReadMe.txt` moved to
  `docs/source-assets/SteampunkFantasyUIPack/` for future recoloring reference, kept out
  of `Assets/` since Unity would otherwise import the layered PSD as a flat texture.
- `WorkbenchController.cs` — new `chassisButtonSprite`/`vaultCardSprite` fields (via a
  `ConfigureSprites(...)` setter, same idiom as every other `Configure*` method),
  applied in `BuildChassisButtons()`/`CreateCard()` under the existing
  Selected/Unselected and Teal/Copper tints (so the color-coding semantics are
  unchanged, just textured now).
- `AssemblyLinePanel.cs`/`PatentBrowserPanel.cs` — same pattern for their per-row Claim/
  Load buttons (`claimButtonSprite`/`loadButtonSprite`).
- Both Close buttons, `EngageGearsButton`/`PatentButton`, the three Workbench column
  backgrounds, `AlertsStrip`, `TabBar`'s 4 buttons, and `SaveLoadTab`'s Save/Load buttons
  were reskinned directly in the shared `WorkbenchCanvas.prefab` (no C# needed — they're
  baked into the prefab, not runtime-instantiated).

### Bug found via live verification (not by inspection)
Assigning a sprite to a dynamically-instantiated row's button (`AssemblyLinePanel`'s
Claim, `PatentBrowserPanel`'s Load) made each list row balloon to ~230px tall instead of
its intended 28px, once actually screenshotted in Play mode — invisible before because a
flat-color `Image` with no sprite reports no meaningful size to Unity's layout system,
so `LayoutElement.preferredHeight` being otherwise-inert (the parent
`VerticalLayoutGroup`'s `childControlHeight`/`childControlWidth` default to `false` on a
freshly `AddComponent`'d group) never mattered until the `Image` had a real sprite
competing for layout space. Root-cause fix, at both the outer row-stacking level (prefab
`Content` groups) and the inner per-row `HorizontalLayoutGroup` (label + button): enable
`childControlWidth`/`childControlHeight`, and — the actual missing piece — set each
row's `LayoutElement.flexibleHeight = 0` explicitly (`-1`/unset still lets a
`VerticalLayoutGroup` hand out leftover space even with `childForceExpandHeight`
false). A secondary, purely cosmetic follow-up: `childForceExpandWidth`'s `true` default
was stretching the 60f-wide Claim/Load buttons across the whole row; disabled that and
gave the row's label an explicit `flexibleWidth = 1` so it absorbs the leftover width
instead. Also applied the same `flexibleHeight = 0` fix to `InventoryPanel.cs`'s rows
(same latent issue, just not visually obvious without a sprite revealing the true
bounds — rows were quietly 100px tall instead of 20px, over-spacing the list).

### Testing
No new tests — this is a purely visual change; existing components only gained new
`Sprite`/layout-flag fields that default harmlessly (a `null` sprite keeps the original
flat-color look, and every existing test's hand-built rigs never wire these fields).
Full regression: 199/199 tests pass (140 EditMode + 59 PlayMode), unchanged from before
this pass. Verified live in Play mode via screenshots: default/Workbench-open/
Management-open (all 4 tabs) in `Main.unity`, and the Workbench in `Sandbox.unity`
(confirming the shared-prefab reskin and hide-until-opened behavior both carry over
cleanly there too).

## Graphics & presentation quality pass implementation notes

Four-phase engine/art/UI polish pass (PR #23, merged to `main`), run per an approved plan.

### Phase 1: engine polish (done)
- The project had **no URP render pipeline asset at all** — `GraphicsSettings.asset`/every
  `QualitySettings.asset` tier's `customRenderPipeline` referenced a guid
  (`681886c5eb7344803b6206f758bf0b1c`) that didn't exist anywhere in the repo, so rendering
  had silently been running on whatever fallback Unity picks rather than an intentional
  pipeline. Created a real `UniversalRenderPipelineAsset`
  (`Assets/Settings/URP-GolemFactory.asset`) with a 2D Renderer
  (`Assets/Settings/Renderer2D.asset`) and repointed `GraphicsSettings` and all six quality
  tiers at its new guid.
- Added `Light2D` ambient (warm-white) + accent lighting to `Main.unity`/`Sandbox.unity`, and
  a `GlobalVolume` GameObject referencing a new
  `Assets/Settings/PostProcessing/GolemFactoryVolumeProfile.asset` with conservative Bloom/
  Color Adjustments/Vignette. Added `Assets/Settings/SpriteLit2D.mat`, the lit material
  `BeltSegmentVisual`'s new optional `itemMaterial` field opts pooled item slots into (falls
  back to Unity's default unlit sprite material when left unset, same fallback idiom as
  `WorkbenchController`'s reskin sprite fields).
- Installed `com.unity.2d.pixel-perfect` but did **not** enable a `PixelPerfectCamera`
  component — it conflicts with the existing free-zoom `CameraRigController`; left available
  for a future deliberate camera-mode decision rather than wired in now.
- Fixed Steampunk UI Pack import settings left over from the previous reskin pass (Bilinear→
  Point filtering, a few sprites missing their 9-slice `spriteBorder`) plus a mismatch where
  the Engage Gears/Patent buttons had icon sprites stretched into full button backgrounds.

### Phase 2: UI completion (done)
- Converted the last two `OnGUI` panels to UGUI, closing out the "two eras coexist" gap:
  `BuildMenuPanel` (button-per-placeable row rebuilt from
  `BuildModeController.AvailablePrefabs` once in `Start()`, since that list is set once via
  `ConfigureEconomy` and never changes at runtime) and `GolemStallIndicator` (now a
  runtime-built World Space `Canvas` child that tracks the golem's transform in
  `LateUpdate()`, replacing `OnGUI` + manual `WorldToScreenPoint`). Both follow
  `WorkbenchController`'s established "always re-render from data" idiom.
- `GolemConstructionPanel` remains `OnGUI` (unchanged from the Steampunk pass' call — see its
  scope cuts above).

### Phase 3: real art content (done)
- `ChassisDefinition` gained a `chassisSprite` field so golem art is data-driven instead of
  hand-wired per scene instance. `GolemVisual.RefreshSpriteFromChassis()` — called by
  `GolemConstructionStation` right after `TryAssignChassis`, since chassis assignment happens
  after `Instantiate` and `GolemVisual.Awake()` already ran with no chassis yet — prefers
  `Program.chassis.chassisSprite` over the Inspector-set fallback `sprite` field, so
  pre-existing hand-wired demo golems are unaffected.
- Replaced the "3 palette-swapped generic silhouettes standing in for 5 named chassis"
  placeholder-art gap: extended `Tools/Art/generate_placeholder_art.py` with 5 distinct
  hand-coded pixel silhouettes (`make_clockwork_scavenger`, `make_brass_presser`,
  `make_aether_hauler`, `make_mainspring_overclocker`, `make_zeppelin_freight_loader`) keyed
  to each chassis's stated role — rickety tripod laborer, bolted stationary press, treaded
  armored hauler, tall clockwork butler, bulky late-game behemoth — rather than paid AI image
  generation; the cost tradeoff was surfaced mid-session and the call was to stay free.

### Phase 4: stretch polish (done)
- New `GolemAnimationUtility` (engine-free static class in `Golems/`, same "extract math into
  pure functions" idiom as `GridCoordinateConverter`/`YSortUtility`): `ComputeIdleBobOffset`
  (sine bob) and `ComputeShakeOffset` (sine shake, linearly decaying to zero over
  `shakeDuration` so a stall reads as one jolt, not a sustained wobble). `GolemVisual.Update()`
  applies the idle bob while `Running`, fires the shake on the frame a stall lands (reusing
  the same `GolemStalled`/`GolemResumed` subscriptions already driving the stall tint), then
  holds the base position while `Stalled`.
- **Bug found via live verification, not by inspection**: `BeltSegmentVisual` had never
  actually worked in any scene — its `Awake()` resolved its segment via
  `conveyorHolder.System.TryGetSegment` before the relevant demo bootstrap script registered
  that segment in its own `Start()` (Unity runs all `Awake()`s before any `Start()`, but
  registration order *across* independent bootstrap scripts was never guaranteed), so the
  sprite pool silently never got built and belts had rendered invisibly since the belt system
  was first written. Fixed by retrying resolution from `LateUpdate()` until it succeeds once,
  making the wiring order-independent instead of requiring bootstrap scripts to register
  earlier than `Start()`.
- Added a one-shot `ParticleSystem` handoff sparkle per belt segment, triggered by the head
  item's `Progress` crossing the segment's end between frames
  (`_previousHeadProgress`/`_previousItemCount`) rather than tracking per-pool-slot, since
  pool slot assignment reshuffles as other items advance/leave.
- Migrated legacy uGUI `Text` to TextMeshPro across every UI script (`AlertsPanel`,
  `AssemblyLinePanel`, `PatentBrowserPanel`, `SaveLoadPanel`, `WorkbenchController`, plus the
  two newly-UGUI'd panels above) and `WorkbenchCanvas.prefab`; added `Unity.TextMeshPro` to
  `GolemFactory.Runtime.asmdef` and imported the TMP Essentials package
  (`Assets/TextMesh Pro/`), which wasn't previously in the repo.

### Testing
- New `Assets/Tests/EditMode/Golems/GolemAnimationUtilityTests.cs` (6 tests: idle bob at zero
  time, amplitude scaling, shake at full/zero/half remaining duration, zero-duration shake).
- Minor mechanical updates to `AssemblyLinePanelTests.cs`, `SaveLoadPanelTests.cs`,
  `WorkbenchControllerTests.cs` for the `Text`→`TextMeshProUGUI` component-type migration.
- Full regression: 205/205 tests pass (146 EditMode, incl. the 6 new, + 59 PlayMode).
- Verified live in the Editor via the Unity MCP bridge at each phase: URP pipeline renders
  correctly, Light2D warmth visible in both `Main.unity` and `Sandbox.unity`, Workbench/
  Management UI text renders crisp with TMP, belt items visibly flow with the handoff
  sparkle, and golems constructed via `GolemConstructionStation` pick up their correct
  distinct chassis sprite.

### Deliberate scope cuts
1. Pixel Perfect Camera package installed but not enabled — conflicts with the existing
   free-zoom `CameraRigController`; a future camera-mode decision, not made here.
2. `GolemConstructionPanel` still not converted to UGUI — carried over from the Steampunk
   pass' scope cut, still true: already interaction-gated and mutually exclusive with the
   UGUI screens, no clutter problem to justify it.
3. `Sandbox.unity`'s Management HUD tabs (Inventory/Patents/SaveLoad) still aren't wired with
   their own per-instance data-source holders — flagged as a follow-up in the Steampunk pass
   and still open after this pass; only `Main.unity` has that wiring.

## Workbench production-quality pass implementation notes

Presentation-only pass over the M8 Workbench screen, bringing it to the
`docs/digital-design.md` spec ("mahogany-and-brass blueprint viewport… Card Vault…
diagnostic tape ticker… Engage Gears lever"). No simulation or drag/commit semantics
changed: dragging still edits only the local draft, and nothing reaches
`GolemEntity.Program` until `EngageGears()` spends Focus.

### Root cause: the "transparent Workbench panels" bug

The three column backgrounds looked like they had *no* panel behind them — the game world
showed straight through the whole screen. The obvious hypotheses were all wrong: the
`Image`s did have their `iron_panel_nobolt` sprite, `fillCenter` was `true`, `type` was
`Sliced` with a valid 9-slice border, `CanvasRenderer.cull` was `false`, and neither
`Main.unity` nor `Sandbox.unity` carried an `m_Sprite`/`m_Color` prefab-instance override.

The actual cause is **`m_ActiveColorSpace: 1` (Linear) plus a very dark tint at
`alpha 0.85`**. Confirmed by an A/B in Play mode, measured off the captured PNGs rather
than eyeballed: a flat red `Image` at alpha 0.85 rendered as a strong wash (`250,86,74`),
while the same panel sprite tinted `0.35/0.25/0.15` at the same alpha landed on
`51,43,37` against a `67,59,52` world — i.e. ~30% effective coverage, not 85%. In linear
blending the surviving 15% of a mid-bright, high-contrast world carries far more
perceived luminance than 15% does in gamma space, so an almost-black panel reads as a
faint dirty film and the stone texture underneath stays legible. Nothing was "missing";
the panels were authored translucent-and-dark, which is unusable over a lit world.

Fix: the Workbench is a *dedicated management screen*, so it now has a fully opaque tiled
mahogany backdrop and opaque panels. Nothing in the screen relies on fractional alpha
over the world any more.

### Code (done)
- `UI/WorkbenchDiagnostics.cs` — new engine-free static class (GridCoordinateConverter /
  GolemAnimationUtility idiom): `ComputeCycleTicks`, `ComputeSteamDraw`,
  `ComputeCyclesPerMinute`, `Humanize` (CamelCase asset id → prose, keeping acronym and
  digit runs intact), `DisplayName`, and `ComposeTicker`, which returns the exact tape
  string so it is asserted in tests rather than assembled ad hoc in `Update()`.
  **Steam is deliberately not a simulated resource** — the psi figure is a derived
  presentation number over data that already exists (slotted steps × chassis tier), which
  is what the design doc's "immediate feedback before activation" asks for.
- `UI/WorkbenchLeverMotion.cs` + `UI/WorkbenchLever.cs` — the "Engage Gears" lever. The
  throw/hold/spring-back curve is engine-free and testable; the `MonoBehaviour` is a thin
  applier that only moves a handle `RectTransform`. It never touches program state — the
  `Button` on the same GameObject still raises `EngageGears` through the normal `onClick`
  path.
- `UI/WorkbenchController.cs` — chassis buttons and vault cards rebuilt with a name +
  subtitle (trigger/action detail read straight off the definitions), `LayoutElement`
  with `flexibleHeight = 0` pinned explicitly (the Steampunk pass' row-ballooning bug,
  pre-empted), cream lettering on the chassis rack in both states so no per-state text
  recolor is needed, and `RefreshBlueprintPane()` driving the viewport portrait from
  `ChassisDefinition.chassisSprite` — the same data-driven source `GolemVisual` uses.
  `ClearChildren` split into `ClearCards` for slots so a rebuild sheds only the card and
  keeps the slot's caption/socket/hint chrome. New `hideWhileOpen` list hides always-on
  HUD chrome that has no `Close()` of its own (Sandbox's `BuildMenuPanel`) — the modality
  is the Workbench's concern, not each panel's.
- **Unnamed definitions**: demo bootstraps build some `LogicCoreDefinition`s /
  `AppendageActionDefinition`s via `ScriptableObject.CreateInstance`, so `name` is empty
  and those slot cards had always rendered as a blank strip (and the tape read
  "TRIGGER -- none --" while a core visibly sat in the slot). `DisplayName` now falls
  back to the trigger/action type.
- `Tools/Art/generate_workbench_ui_art.py` — new companion to
  `generate_placeholder_art.py` (free/Pillow only, no paid image generation): mahogany /
  iron / blueprint-field 9-slice panels whose centres tile seamlessly for
  `Image.Type.Tiled`, a brass title plate, a near-white punch-card face (so the teal/copper
  `Image.color` coding stays the thing carrying the semantics), a slot socket, punched
  paper tape, and the lever track/handle/gauge pip. Output to
  `Assets/_Project/Art/UI/Workbench/`.

### Manual Editor setup (done, via live Unity MCP + `execute_code`)
`WorkbenchScreen`'s children were deleted and rebuilt in one `PrefabUtility
.LoadPrefabContents` → edit → `SaveAsPrefabAsset` pass (headless, so the
prefab-instance-override-reverts gotcha never applies), then the controller's serialized
references re-pointed via `SerializedObject`. Only `WorkbenchScreen` was touched;
`ManagementScreen`, `AlertsStrip`, `EventSystem` and the controller GameObjects were left
alone so the scenes' existing overrides (`targetGolem`, `focusMeterHolder`,
`bufferRegistryHolder`, …) survived. Layout: header plate → blueprint viewport (chassis
portrait pane + captioned trigger/step sockets) → chassis rack → card vault (now a real
`ScrollRect` + `RectMask2D`) → bottom bar (tape ticker, status line, lever, patent) →
`DragLayer` last. The Sandbox `BuildMenuPanel` wiring is a scene-level reference and was
written with `PrefabUtility.RecordPrefabInstancePropertyModifications`.

### Testing
- New `Assets/Tests/EditMode/UI/WorkbenchDiagnosticsTests.cs` (16) and
  `WorkbenchLeverMotionTests.cs` (6).
- Full regression: **246/246 pass (185 EditMode + 61 PlayMode)**, up from 205; zero
  failures, console clean.
- Verified live in Play mode in both `Main.unity` and `Sandbox.unity` (shared prefab),
  including a scripted end-to-end drive: select a chassis, drop cards into slots, pull the
  lever, read back `GolemEntity.Program` (`chassis=ZeppelinFreightLoader logicCore=set
  appendages=3`).

### Deliberate scope cuts
1. No hover/press states on vault cards or chassis buttons (`Button` colour tint only) —
   the cards are runtime-instantiated and would need per-instance `SpriteState` wiring.
2. The lever animates on click but there is no audio and no gear-turn animation on the
   header ornament; both would need new assets/systems this pass didn't take on.
3. `AlertsStrip` still overlaps the very top of the screen; the header bar was moved down
   to clear it rather than relocating shared HUD chrome another pass owns.

## World/environment production-quality pass implementation notes

Presentation-only pass over the isometric floor, perimeter walls, lighting and camera
framing of both `Main.unity` and `Sandbox.unity`. No simulation logic changed; `GridMap`
remains the truth and the Tilemap remains purely visual.

### The floor tiles were never the size of their own cell
Before anything else: `floor_tile.png` (128x64) imported at **PPU 64**, which made every
tile **2 x 1 world units** — exactly twice the `1 x 0.5` cell the isometric `Grid` uses. The
painted diamonds therefore never lined up with the grid at all; the floor was a 2x-oversized
overlapping mat that only *looked* like a tiled floor. Every environment sprite now imports
at **PPU 128**, which is what makes sprite size == cell size.

That fixed the scale, and it fixed the pixel budget too. The whole environment is now
authored at one art-pixel scale — **32 art pixels per world unit**, native canvas upscaled
x4, imported at PPU 128 — chosen so that at the default `orthographicSize` of 5 an art pixel
is 3-4 screen pixels. Authoring finer produced exactly the high-frequency visual noise the
old grey stone floor was criticised for.

### Root cause of the "staircase" walls
The in-flight wall art was 88x109 px at PPU 64 (**1.375 x 1.703 world units**) with a
**flat** bottom edge, placed one instance per *perimeter ring cell* via
`FloorLayout.GetNorthEastEdgeCells`. Consecutive perimeter cells are only **0.5 x 0.25**
world units apart (the 2:1 isometric run), so each sprite was 2.75x wider than its own
spacing *and* its silhouette slope never matched the run it was supposed to trace. The
result is a row of flat-bottomed blocks each stepped up a quarter unit from the last — a
literal staircase. No offset or scale nudge can fix that; the footprint geometry is wrong.

The fix is geometric, not cosmetic:
- A wall segment covers exactly **one cell edge**: 0.5 world wide with a base line that
  rises 0.25 world across that width (`_wall_base_row`'s `//2` staircase in the generator
  script is the exact 2:1 step, so neighbours butt together seamlessly).
- Its sprite pivot is a **custom pivot at the midpoint of that base line**, not the sprite
  centre or bottom-centre.
- It is anchored on the boundary **line** at `halfExtent + 0.5`, not a ring cell at
  `halfExtent + 1` — `FloorLayout.GetEdgeAnchor(Edge, index)`, which replaces
  `GetNorthEastEdgeCells`/`GetNorthWestEdgeCells`.

### Code
- `World/FloorLayout.cs` — new `Edge` enum plus `GetEdgeAnchor` / `GetEdgeIndices` /
  `GetWallPostAnchors`, all pure cell-fraction math (the `GridCoordinateConverter` idiom).
  Replaces the two ring-cell edge enumerations, which encoded the wrong placement model.
- `World/FloorTileVariant.cs` — pure deterministic tile selection (4 plank variants + two
  rare accent tiles). Deterministic on purpose: a random scatter would rewrite the scene
  file on every regeneration.
- `World/GroundShadow.cs` — creates its own child `SpriteRenderer` and drives its
  `sortingOrder` from the **owner's** Y minus one. A `YSortSpriteRenderer` on the child
  could not do this: the shadow's own Y is lower, so it would compute a higher order and
  draw *in front of* the thing casting it. `anchorToSpriteBottom` derives the drop point
  from the owner sprite's pivot every frame, because this project's pivots are inconsistent
  (player = bottom-centre, chassis = centre) and `GolemVisual` assigns the chassis sprite
  after `Awake`.
- `Scripts/Editor/SandboxFloorGenerator.cs` — rewritten. Paints the floor from
  `FloorTileVariant`, builds both wall runs (with sconce variants + child `Light2D`s), both
  near-edge slab skirtings, three corner posts, and deterministic crate/barrel clutter along
  all four edges. Also gained a `Tools > Golem Factory > Reimport Environment Art` menu item
  that applies PPU 128 + the custom pivots, so the pivots live in source rather than in a
  throwaway console script. `GolemFactory.Editor.asmdef` now references
  `Unity.RenderPipelines.Universal.2D.Runtime` for `Light2D`.
- Sorting order on walls/props is **baked at generation** rather than adding a
  `YSortSpriteRenderer` to ~130 static objects: the value is identical to what that
  component computes, it costs no per-frame work, and it also sorts correctly in EditMode
  (no `[ExecuteAlways]`).

### Art (`Tools/Art/generate_placeholder_art.py`, Pillow only — no paid generation)
Warm wood plank floor with 4 variants, a riveted brass inspection plate and a steam grate as
sparse accents; panelled-dado/brass-rail/warm-brick wall segments plus lit sconce variants;
mirrored near-edge slab skirting; a brass-banded corner post; crate and barrel props; and a
soft contact shadow. `main()` now runs **only** the environment set — the character/item
sprites have since been replaced with better art at different resolutions, and re-running
the script used to silently clobber them. `--legacy` opts back in.

### Lighting / framing
- `Assets/Settings/PostProcessing/GolemFactoryVolumeProfile.asset` was **empty**. The
  Bloom/ColorAdjustments/Vignette recorded in the graphics pass had been created but never
  added as sub-assets, so they vanished on the next domain reload and no post-processing was
  ever running. Rebuilt with `AssetDatabase.AddObjectToAsset` and verified across a reload.
- `Sandbox.unity` had no `GlobalVolume` at all, and its camera had no
  `UniversalAdditionalCameraData` (so no post-processing). Both added.
- Both cameras: `Skybox` (default blue void) -> `SolidColor` warm gloom, so the workshop
  reads as a lit platform in a dark room instead of a diamond floating in nothing.
- Global `Light2D` warmed and raised to 1.15; wall sconces every 4th segment at 0.85 /
  radius 3.6 (every 6th left long stretches of wall in darkness).

### Gotcha worth recording
`manage_camera(screenshot)`'s inline preview is noticeably **brighter** than the PNG it
writes to disk. Two rounds of lighting were graded against the preview and were far too dark
in reality. Always verify the file, not the inline image.

### Testing
- `Tests/EditMode/World/FloorTileVariantTests.cs` (6) and 5 new `FloorLayoutTests` cases,
  including the staircase regression test itself: consecutive edge anchors must be exactly
  one cell edge (0.5 x 0.25) apart on all four edges, back walls must sort behind the cell
  they border and front skirtings in front of it, and each run must end exactly on its
  corner post.
- Full regression: **308/308 pass (229 EditMode + 79 PlayMode)**, up from 296; zero
  failures, console clean.
- Verified live in Play mode in both scenes at 1920x1080, including the Y-sort case: the
  player parked at the far corner is correctly occluded from the knees down by the wall
  segments nearer the camera while the corner post draws behind her.

### Deliberate scope cuts
1. `WallSegmentNE.prefab`/`WallSegmentNW.prefab` were deleted. Seven distinct piece types
   would have meant seven prefabs to keep in sync with the one generator that is the actual
   source of truth; the generator is idempotent, so nothing is lost.
2. Golem chassis sprites are pivoted **centre**, so a golem's art sits half a sprite-height
   below its cell. `GroundShadow` compensates for the shadow, but the sprites themselves
   still render low. Fixing the pivots shifts every hand-placed golem in `Main.unity` and
   belongs to whoever owns golem presentation, not this pass.
3. The interior of the room is still empty floor — clutter is confined to the perimeter so
   it never competes with build placement. Filling the middle is a level-design decision.

## Belt readability production-quality pass implementation notes

Belts previously rendered as nothing but a row of grey `item_scrap` sprites strung along a bare
diagonal. Reviewed live, that line read as scattered debris or a staircase, not a conveyor. Three
defects, all fixed here.

### 1. Pooled belt items were never Y-sorted

Every pooled `ItemSlot` sat at `sortingOrder = 0` while the golems around them were at -174 and
+166, so belt cargo punched through the isometric depth order everywhere on the map. The project
already had `World/YSortUtility.ComputeSortingOrder(worldY)`; the pool simply never called it.

**One sorting order for the whole segment would also have been wrong** — a diagonal lane spans a
range of world Y, so the order has to be per item, recomputed each frame from that item's own
point *on the lane* (not from the raised sprite position, so the cosmetic "sits on top of the
belt" offset can't reorder anything).

Measured in Play mode on `Main.unity`'s `ScrapBeltA` (lane from `(4.50, 0.00)` to `(3.65, 0.85)`).
**Corrected against a live re-measurement during the follow-up fix pass** — the numbers first
recorded here (`-3` for the feeder golem, `-173` for `GolemB`) were wrong; the conclusions and the
size of the margins were not. The real anchors, read straight off the `SpriteRenderer`s:

| renderer                          | world Y | sortingOrder |
|-----------------------------------|---------|--------------|
| feeder golem `Golem` (chassis)    | -0.037  | **4**        |
| feeder golem `Golem` GroundShadow | -0.787  | 3            |
| `GolemB` (chassis)                | 1.663   | **-166**     |
| `GolemB` GroundShadow             | 0.913   | -167         |

Note the feeder golem's Y **oscillates** (it has an idle bob), so its order drifts around 0-4
frame to frame; do not expect a single fixed number when re-measuring.

`ScrapBeltA`'s six cargo slots, by progress, are `1, -16, -33, -50, -67, -84` (the `+1` is the
cargo tiebreak added in the fix pass, below). All are below the feeder golem's 4, i.e. cargo at
the mouth correctly draws *behind* the golem standing in front of it. `ScrapBeltB`
(`(3.65, 0.85) -> (2.80, 1.70)`, ending at `GolemB`) shows the opposite half: its slots run
`-84 ... -169`, and its first five are all greater than `GolemB`'s `-166`, so cargo correctly
draws **over** the golem behind it. Same component, same frame, both directions — see
`Assets/Screenshots/belt_main_occlusion_feeder.png` and `belt_main_occlusion_receiver.png`.

Under the old code all six slots were `0`, i.e. permanently in front of everything.

The lane/arrow/roller decals sort from the lane's **furthest-back** end minus a small bias, not
from its centre: anything standing on or beside a flat ground decal has `Y <= thatMaximum` and
therefore a strictly larger sorting order, so it always draws on top. Sorting the decal from its
centre instead put the lane in front of the cargo riding its far half.

### 2. There was no belt to look at

`Belts/BeltSegmentVisual.cs` now draws the lane itself, not just its cargo, and everything it
draws stays inside the "no GameObject per belt item" rule — every renderer is pooled at resolve
time and never grows:

- one stretched `belt_lane` sprite rotated onto the start-to-end vector (the art is uniform along
  its length precisely so an arbitrary X stretch is invisible — no rivets to smear),
- two `belt_roller` end drums,
- N `belt_arrow` chevrons, N fixed by lane length, scrolled along the lane every frame and faded
  out at both mouths so pooled arrows recycle without popping,
- the existing `Capacity`-sized item-slot pool, now also picking its sprite per
  `ItemStack.ItemType` from a serialized `string -> Sprite` table (so a mixed belt is readable),
  and scaled to fit the segment's own item spacing.

Arrow scroll speed comes from the clock (`laneLength / segmentLength * TicksPerSecond * Speed`),
so it matches the speed an unobstructed item actually travels and stops dead when the sim is
paused. That is the "how fast" readout, and it works on an empty belt.

### 3. Items teleported once per tick

`BeltSegment.Progress` is integer-stepped, so rendering straight from it made cargo blink from
cell to cell. `BeltFlowUtility.PredictProgressAfterAdvance` replays `BeltSegment.Advance`'s
head-first cap propagation **without writing anything back**, and the visual interpolates between
current and predicted progress using a new read-only `SimulationClock.TickFraction`.

That is also what makes backpressure legible for free: a blocked item predicts to its own current
progress, so it visibly *stops* while its unobstructed neighbours keep gliding. On top of that,
`ComputeCongestion` counts items queued *behind another item* — deliberately **not** counting a
head parked at the end of a terminal segment, which is normal operation and would otherwise leave
the jam signal permanently on — and drives the arrows from amber to red while slowing them to a
halt. Queued items also take a gentle warm flush (a hard red tint made a jammed Brass ingot read
as a different item).

`SimulationClock.TickFraction` is the only simulation-side change: a read-only property over the
existing accumulator. `Advance()` is untouched, and nothing in the simulation may read it — doing
so would make behaviour frame-rate dependent.

### `Sandbox.unity` had no belts at all

Sandbox registered a `ConveyorSystem` but never a single `BeltSegment`, and the two authored
belt-facing appendage cards had empty ids (`ExtractScrap.destinationId`,
`LoadIntoScrapBuffer.sourceId`), so every belt program a player could build was guaranteed to
stall on `TryEnqueue`/`TryDequeueHead` returning `false` for an unknown segment.
`SandboxBootstrap` now registers a chained `ScrapBeltA -> ScrapBeltB` pair, both with visuals in
the scene, and the two cards point at them. The ids intentionally match the ones
`BeltDemoBootstrap` registers in `Main.unity`, so one set of authored `AppendageActionDefinition`
assets drives both scenes.

`Main.unity` also gained the missing `TriggerScrapBeltVisual` — `TriggerDemoBootstrap` had always
registered that segment, but nothing ever drew it. The `AetherBelt`/`TriggerScrapBelt` anchors
were nudged off their golems, since a lane rendered *under* a golem's feet reads as a bug.

### Art (`Tools/Art/generate_placeholder_art.py belts`, Pillow only — no paid generation)

New `generate_belts()` section; `main()` now takes an optional section name so belt art can be
regenerated without touching the environment. Same convention as the environment pass: 32 art px
per world unit, x4 upscale, PPU 128 (`belt_lane` is 1.0 x 0.4375 world, plus `belt_arrow` and
`belt_roller`). The arrow is authored **white with only a dark rim**, because the runtime tints it
(amber when flowing, flashing hot when jammed); tinting a pre-coloured sprite would multiply the
two hues and mud both ends of the readout. No belt art needed regenerating in the follow-up fix
pass below — the whole fix is layering and runtime tint/scale.

`item_scrap` / `item_brass` / `item_aether` were reskinned from cold grey into the warm workshop
palette with three distinct silhouettes (stepped rust offcuts / clean brass trapezoid / tall teal
shard), so they separate by shape before colour is even read. They keep their 32x32 file size and
PPU 64 — same GUIDs, same world size, no rewiring — but are authored at 16 art px upscaled x2, so
their pixel density finally matches the floor's. Their generation moved OUT of
`generate_legacy_placeholders()`, which would otherwise silently clobber the reskin the same way
`--legacy` clobbers the chassis art.

`Assets/_Project/Scripts/Editor/BeltArtImporter.cs`
(`Tools > Golem Factory > Reimport Belt Art`) mirrors
`SandboxFloorGenerator.ReimportEnvironmentArt`, for the same reason it exists there:
`manage_texture`'s import-settings path silently drops its payload, and freshly written PNGs
otherwise land at PPU 100, bilinear, compressed.

### Testing

- New `Assets/Tests/EditMode/Belts/BeltFlowUtilityTests.cs` (26 tests). The load-bearing one is
  `PredictProgressAfterAdvance_MatchesBeltSegmentAdvance`, which drives a real `BeltSegment`,
  predicts every item's next progress, then calls `Advance` and asserts the prediction was exact —
  if those two ever diverge, interpolated cargo drifts away from the simulation.
- 4 new `SimulationClockTests` for `TickFraction` (zero before any time accumulates, mid-tick
  value, wrap back after a tick fires, zero at `TicksPerSecond == 0`).
- Full regression: **341/341 pass (262 EditMode + 79 PlayMode), zero failures.**

### Deliberate scope cuts

1. `Main.unity`'s golems are placed on a ring, not on the isometric grid, so `ScrapBeltA/B` run at
   a 45-degree screen angle rather than along a `1 x 0.5` cell axis. The lane renders correctly at
   any angle, but a belt on a true iso axis would look more at home. Fixing it means moving
   golems, which belongs to whoever owns golem placement.
2. Belt junctions still render as two coincident end rollers rather than a purpose-built junction
   piece. `ConveyorSystem` only supports 1:1 `Next` chaining, so there are no real splitters or
   mergers to draw yet.
3. No belt *sound*, and no per-item squash/motion blur on handoff. The one-shot handoff sparkle
   from the previous pass is unchanged and still fires.

## Belt readability follow-up fix pass

The pass above was reviewed live in Play mode and returned **FAIL** on three counts. The
foundation (per-item Y-sort, clock-matched arrow speed, sub-tick interpolation, the item art, the
Sandbox belt registration, the no-GameObject-per-item pool) all verified TRUE and is untouched
here. Everything below is presentation-only; `ConveyorSystem.Tick` and `BeltSegment` are not
touched, and `Belts/` still has no reference to `Golems/`.

All three fixes are expressed as pure functions in new
`Assets/_Project/Scripts/Belts/BeltSignalUtility.cs`, unit-tested by
`Assets/Tests/EditMode/Belts/BeltSignalUtilityTests.cs` (21 tests), following the
`BeltFlowUtility` / `YSortUtility` / `WorkbenchDropRules` idiom.

### 1. The direction arrows were drawn UNDER the cargo

Not a tuning issue — a logic flaw. Cargo sorted at `YSort(groundY)`; arrows sorted at
`laneSortingOrder + 1`, i.e. `YSort(BACKMOST laneY) - 3`. Since every cargo Y on a lane is at most
the backmost Y, and `ComputeSortingOrder` decreases in Y, **the cargo order was strictly larger on
every belt in the project, always.** On `ScrapBeltA` that was arrows at `-88` against cargo at
`0 ... -84`. Direction was therefore absent in a belt's normal *loaded* state, and the failure
compounds: a jam means the belt is full, a full belt is wall-to-wall cargo, so the jam readout was
hidden exactly when it fired.

The bug was invisible from either constant alone, so the rule is now one function with one
property a test can assert. `ComputeFlowSignalSortingOrder` anchors on the lane's **frontmost**
(smallest-Y, therefore largest-order) end rather than the lane decal, and
`FlowSignal_OutranksCargoAtEveryPointOnEveryLane` walks 200 samples along five lane geometries
(including both real `Main` lanes and a degenerate horizontal one) asserting the signal wins at
every one. A companion test pins the old `laneSortingOrder + 1` rule as losing, so nobody re-derives
the arrows from the lane decal again.

Measured live on `Main.unity`, fully jammed `ScrapBeltA`: arrows `3`, max enabled cargo slot `1`.
On `ScrapBeltB`: arrows `-82`, max cargo `-84`. Lane decal still behind everything at `-89` / `-174`.

**Feed-point tie.** An item at progress 0 tied `sortingOrder` with the feeder golem standing at
the same Y, and that golem's idle bob makes its order drift through the tie every second or so —
i.e. real flicker. Cargo now takes `+1` (`CargoSortingBias`), 0.01 world units of Y, enough to
settle exact ties and far too small to reorder anything real. Clearing the cargo then puts the
flow signal on `3`, which can itself collide exactly with a character at the mouth. No integer
bias can rule that out in general, so the remainder is broken in the only other channel available:
with an orthographic camera and `Default` transparency sorting, equal `sortingOrder` resolves by
view-axis distance, and `ComputeFlowSignalPosition` pushes the chevrons `+0.01` in Z so they
**lose** those ties — correct, since anything standing level with the lane mouth should occlude a
decal painted on the lane.

### 2. The jam state was LESS visible than the healthy state

The workshop floor is warm brown planks (`PLANK_TONES[3]`, relative luminance **0.360**). The
alarm shifted the arrows from amber (luminance 0.765, contrast **0.405**) to a dark signal red
(luminance 0.435, contrast **0.075**). The alarm state had **5.4x less contrast against its own
background than the healthy state** — it read as "the belt dimmed", not "something is wrong".
Red-on-brown is simply a bad alarm channel here.

The deeper problem: against warm brown, *no* red out-luminates that amber, so hue and brightness
alone can never make the alarm louder at all times. **Area is the channel that wins.** The jam
signal now:

- keeps a red base (`jamBaseColor` 1.00/0.34/0.28) for semantics, but **pulses toward hot white**
  (`jamPulseColor` 1.00/0.95/0.90) at 2.4 Hz, with a floor of 0.5 so even a still frame caught at
  the bottom of the pulse is well clear of the floor;
- **swells the chevrons** with the pulse (`JamSignalScaleGain` 0.6, so 1.30x at the trough and
  1.60x at the peak — 1.7x and 2.6x the pixels);
- still freezes the scroll as congestion rises, and now also tints the two end rollers, which sit
  outside the cargo's footprint.

`ComputeSignalSalience` = `scale² × luminanceContrast`, and
`JamSignal_IsLouderThanTheHealthySignalAtEVERYPhaseOfThePulse` asserts the quietest phase still
beats the healthy state (and the loudest beats it 3x). The old red is pinned by
`OldJamSignal_WasFiveTimesQUIETERThanTheHealthyState`.

Verified on rendered pixels, not on theory. Belt-region crops at gameplay framing (ortho 5),
counting chevron pixels and summing their luminance contrast against the plank floor:

| state (Sandbox / Main)     | chevron px       | contrast energy   |
|----------------------------|------------------|-------------------|
| flowing, fully loaded      | 1.00x / 1.00x    | 1.00x / 1.00x     |
| jammed, dimmest pulse phase| 1.33x / 1.21x    | 1.26x / 1.14x     |
| jammed, peak pulse phase   | 1.65x / 1.54x    | 2.58x / 2.42x     |

See `Assets/Screenshots/belt_fix_sandbox_compare_crop.png` and `belt_fix_main_compare_crop.png`
(flowing / jam-trough / jam-peak at identical framing).

`arrowSpacing` also dropped 0.5 -> 0.36 and `arrowEndFade` 0.2 -> 0.14 on all six
`BeltSegmentVisual` instances across both scenes: pooled chevrons went 4 -> 5 on `Main`'s
1.202-unit lanes and 5 -> 6 on `Sandbox`'s 1.600-unit lanes, and the narrower end fade keeps more
of them at full alpha (3 visible on `Main`, 5 on `Sandbox`). 0.36 is the tightest spacing a
1.6x-swollen chevron (0.30 world wide) still fits into without touching its neighbour.

### 3. The cargo "warm flush" was imperceptible

`itemJamTint` (1.00/0.80/0.72) multiplied into already brown/orange scrap. Measured on the three
authored item colours it moved luminance by **13.8% / 14.9% / 18.4%** — and in the same warm
direction the art already occupies, which is why it produced no detectable difference in
side-by-side crops. Replaced (field renamed to `itemQueuedTint`, so the stale value baked into
every scene is dropped rather than overriding the new default) with a **cold dim**
(0.58/0.62/0.78): **38.7% / 38.5% / 37.2%** luminance drop with the hue swung off the warm axis.
Queued cargo now reads as cold dead metal under bright flashing chevrons, without becoming a
different item. Both the old failure and the new threshold are unit-tested.

### Testing

- New `BeltSignalUtilityTests.cs` (21). The load-bearing one is
  `FlowSignal_OutranksCargoAtEveryPointOnEveryLane` — a standing regression test that cargo can
  never occlude the flow signal.
- Full regression: **362/362 pass (283 EditMode + 79 PlayMode), zero failures**, up from 341;
  console clean.
- Verified in Play mode at gameplay framing (`orthographicSize` 5) in **both** scenes, on a
  **fully loaded** belt, flowing vs. jammed at identical framing.

### Deliberate scope cuts (still open)

1. `ComputeItemScale` clamps at `maxItemScale = 1.0`, so on Sandbox's 0.32-world item spacing the
   auto-fit never engages and cargo overflows the lane silhouette. Raising the clamp is a one-line
   change but re-tunes every belt's cargo size, which wants its own verification pass.
2. Arrow scroll speed is scaled by `(1 - congestion)`, which under-reports throughput at partial
   congestion (the head is still moving). Correct fix is to derive it from actual head throughput.
3. The lane still reads as a flat girder with no isometric thickness on `Main`'s 45-degree
   diagonal, and belt junctions still draw two coincident rollers at identical position *and*
   `sortingOrder`.
## Economy / storage readout + Management HUD production-quality pass

`ManagementPanel`'s four tabs shipped functional but unreadable and, in `Sandbox.unity`,
inert. This pass fixes the wiring gap the M9/HUD notes recorded as an open scope cut,
makes the inventory scannable, and adds the one economic signal the game had no way to
show: whether a buffer is filling or draining.

### What was actually wired vs. what the docs claimed

Verified live in both scenes before changing anything (`execute_code` reflection dump of
every serialized `UnityEngine.Object` field on all four panels). The "Deliberate scope
cuts" note under *Walkable Main.unity demo + UGUI HUD redesign* was accurate and still
open:

- `Sandbox.unity` had **no `AssemblyLineStateHolder` at all**, and all four tabs' per-scene
  data sources were `null` (`InventoryPanel.bufferRegistryHolder`,
  `PatentBrowserPanel.patentRegistryHolder`, `AssemblyLinePanel.lineHolder`/
  `bufferRegistryHolder`, all three of `SaveLoadPanel`'s holders,
  `ManagementPanel.constructionPanel`). Every tab there rendered empty, and
  `SaveLoadPanel.Save()` would have thrown a `NullReferenceException` on the first click.
- **Undocumented, present in BOTH scenes**: `AssemblyLinePanel.statusText` and
  `SaveLoadPanel.statusTextMeshProUGUI` were `null` even though the `StatusText`
  GameObjects existed in the prefab. Every status message this pass's predecessors
  "verified" ("Claimed X", "Saved N golems") was being written to a field nobody had
  connected -- the M9/HUD verification had read `_statusMessage` back by reflection, not
  looked at the screen.
- **Undocumented**: every dynamically-created row in `InventoryPanel`/`AssemblyLinePanel`/
  `PatentBrowserPanel` used `Color.black` text on `ManagementScreen`'s 0.08-grey iron
  panel. Effectively invisible.

### Code (done)

- `Economy/BufferTrendUtility.cs` (new) -- engine-free static math: least-squares slope of
  quantity against time scaled to per-minute, a three-valued `StockTrend` with a deadband,
  signed rate formatting, ASCII trend glyphs, and the canonical item-type display order.
  Least squares rather than a first-to-last delta **because buffer quantities are integers
  that step at tick boundaries** -- an endpoint delta over a short window quantizes hard
  (`TryComputeRatePerMinute_IsALeastSquaresFit_NotAnEndpointDelta` pins the difference).
  Glyphs are `^`/`v`/`-`, not Unicode triangles: TMP renders a missing-glyph box for
  anything outside its default atlas, which would put a literal box next to every number.
- `Economy/BufferRateTracker.cs` (new) -- plain-C# rolling sample history per
  (bufferId, itemType) over an 8s window. Pruned **strictly** by age with no "keep the last
  two anyway" floor: retaining a point from outside the window anchors the fit to a stale
  quantity, which `Sample_StaleWindowDoesNotAnchorTheRate_AfterATrendReverses` guards.
- `Economy/BufferThroughputMonitor.cs` (new) -- the Holder-pattern `MonoBehaviour` that owns
  the tracker and feeds it `Time.time` every 0.25s. Lives on the **same GameObject as
  `StorageBufferRegistryHolder`** and resolves it via `GetComponent` when unset, so (a) it
  needs zero cross-object scene wiring in either scene and (b) it keeps sampling while the
  HUD is closed or on another tab -- a rate that only starts accumulating when you open the
  panel is useless. `Tracker` is built lazily, not in `Awake()`, for the same reason
  `AssemblyLineStateHolder` uses a field initializer.
- **Nothing was added to the simulation.** `StorageBuffer`/`StorageBufferRegistry` are
  untouched; the rate is derived entirely presentation-side from sampled quantities.
- `UI/InventoryPanel.cs` -- rebuilt rows: item icon, name, relative-magnitude bar, quantity,
  and a signed rate with a trend glyph. Grouped under a brass per-buffer header plate with
  buffers sorted (a `Dictionary` iteration order is not contractual, and a list that
  reorders itself between frames is unscannable) and item types in production order
  (Scrap -> Brass -> Aether). Bars are normalized against the **largest stock on screen**,
  not a capacity -- a `StorageBuffer` has no capacity concept in the simulation, so there is
  no "full" to draw a percentage against and inventing one would be a lie. Rows with no
  rate history print `--`, not `0/min`: "no reading yet" and "genuinely flat" are different
  claims. Real empty/unwired states instead of a silently blank panel.
- `UI/AssemblyLinePanel.cs` -- wallet-balance header row, cost in its own fixed-width
  right-aligned column, affordability carried by brightness *and* by disabling the Claim
  button (`TryClaimSlot` already refused; the click was only a way to produce an error
  message the player could have been shown up front).
- `UI/PatentBrowserPanel.cs` -- readable row colour, row plates, and a real empty state
  naming the action that fills the list.
- `UI/SaveLoadPanel.cs` -- `HasDataSources` guard so an unwired scene reports itself in the
  status line instead of throwing.
- `UI/ManagementPanel.cs` -- `ApplyTabHighlight()`. Every tab button shares the same brass
  sprite, so before this **nothing on screen indicated which tab was open**. Plate and
  caption invert together (dark ink on lit brass, parchment on dim).

### Layout landmine, hit exactly as predicted

Adding sprited item icons re-triggered the "a flat-color `Image` reports no useful size to
the layout system" bug. Fixed once, in `InventoryPanel.CreateRowRoot`, with the recorded
recipe: `childControlWidth`/`childControlHeight` **true**, `childForceExpandWidth` **false**,
and an explicit `LayoutElement.flexibleHeight = 0` (`-1`/unset still lets the parent
`VerticalLayoutGroup` hand out leftover space). Icons additionally need explicit
`flexibleWidth = 0` **and** `preferredWidth`/`preferredHeight`, or a 32x32 sprite reports its
native size and drags the row's height with it. Verified at 54 rows: rows held 28px.

### Manual Editor setup (done, via live Unity MCP + `execute_code`)

1. `ManagerHolders.prefab` -- added `BufferThroughputMonitor` to `Buffers`. Shared by both
   scenes, so one edit covers both with no per-scene wiring.
2. `WorkbenchCanvas.prefab` (shared, headless `LoadPrefabContents`/`SaveAsPrefabAsset`) --
   wired the two never-connected `StatusText` refs; baked the three item-icon sprites and a
   header plate onto `InventoryPanel` (asset refs, safe in a prefab); added an **opaque
   `Backdrop`** under `ManagementScreen` (the iron-panel sprite is largely translucent, so
   the screen read as a faint overlay on the world -- unusable for a data screen); sized the
   tab buttons (they kept a native 100x100 rect and spilled into the first rows of content)
   and moved `TabBar`/`TabContent` down clear of the always-on `AlertsStrip`, which is a
   sibling that draws over this screen; sized and re-skinned `SaveLoadTab`'s buttons.
3. `Sandbox.unity` -- created `AssemblyLine` (`AssemblyLineStateHolder`) +
   `AssemblyLineDemoBootstrap` (same roster assets `Main.unity` uses), then wired all four
   tabs' data sources and `ManagementPanel.constructionPanel`, each followed by
   `PrefabUtility.RecordPrefabInstancePropertyModifications` + `EditorUtility.SetDirty`.
   **Confirmed the overrides survived** a full save -> Play-mode entry -> exit -> disk
   reload, which is the exact failure mode recorded under the HUD redesign notes.

### Proving the rate readout is correct (measurement, not "looks right")

- Unit tests assert the **exact** slope on hand-computed series (2 items/s over 4s => exactly
  120/min; -1/s => exactly -60/min; a flat series => exactly 0).
- Live in Play mode on `Main.unity`, against an independent endpoint measurement taken over
  15.2s of real simulation: Scrap ground truth **401.4/min** vs. displayed **+400/min**;
  Brass ground truth **200.7/min** vs. displayed **+200/min** (0.2% and 0.5% error). A
  genuinely static buffer (`TriggerBrassBuffer`, quantity 0) read exactly `0/min`/Steady, not
  noise. Draining a buffer flipped it to teal `v -1750/min` within the window and it
  recovered to `+400/min` once the drain aged out.

### Testing

- New `Tests/EditMode/Economy/BufferTrendUtilityTests.cs` (13) and
  `BufferRateTrackerTests.cs` (9).
- Extended `Tests/PlayMode/UI/InventoryPanelTests.cs` (empty states, icon slot, quantity,
  `--` vs. a signed rate), `AssemblyLinePanelTests.cs` (wallet row, affordability gating),
  `PatentBrowserPanelTests.cs` (empty state), `ManagementPanelTests.cs` (exactly one tab
  highlighted, caption inverts).
- **Gotcha**: a PlayMode test that seeds `BufferRateTracker` with a synthetic series must
  disable `BufferThroughputMonitor` first. The test-runner clock is already many seconds in,
  so one real `Time.time` sample ages every synthetic point straight out of the window and
  the readout correctly reports "no reading" -- which looked like a bug and was not.
- Full regression: **410/410 pass (324 EditMode + 86 PlayMode), zero failures**, up from
  381; console clean.
- Verified in Play mode with screenshots in **both** scenes, every tab, including empty
  states, plus a 54-row scroll test. In `Sandbox.unity` the Assembly Line's Claim button was
  driven for real: wallet 40 -> 38 Scrap with "Claimed ClockworkScavenger." on the status
  line -- an economy loop that could not run in that scene at all before this pass.

### Deliberate scope cuts (still open)

1. There is still **no capacity concept** for a `StorageBuffer`, so the magnitude bars are
   relative-to-largest-on-screen only. A real fill gauge needs a simulation change.
2. The rate is **net stock change**, not gross throughput -- a buffer being filled at 60/min
   and drained at 60/min reads Steady, which is true but hides the traffic. Separating
   in/out needs deposit/withdraw instrumentation the registry does not expose.
3. `Sandbox.unity`'s `BuildMenuPanel` and `GolemConstructionPanel` are still `OnGUI`, and
   OnGUI always draws over UGUI -- the Build menu overlaps the bottom-left of the open
   Management screen. That belongs to the Sandbox/build-system pass.
4. `GolemConstructionPanel.workbenchController`/`.managementPanel` and
   `WorkbenchController.constructionPanel` are `null` in `Sandbox.unity`, so the
   construction panel does not force-close the other two HUD screens there. Noted, not
   fixed -- it is the player-interaction pass's half of the mutual-exclusion wiring.
5. The Assembly Line tab still has no `ScrollRect` (3 slots + a wallet row fit), and the
   Patents tab's rows carry no chassis/appendage summary, only the blueprint id.
