# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

`golem-factory` is a solo-play Unity prototype of "Golem Factory: The Clockwork Metropolis" — a
Factorio/Satisfactory-style automation game (cozy isometric pixel art) where the player places
"golems" programmed with punch-card-style Logic Core / Appendage / Chassis combinations that run
rigidly on a world tick clock. It began as a tabletop board-game design (`docs/game-design.md`)
and is being adapted into a digital prototype; read the docs below before making design calls, not
just the code.

- `docs/game-design.md` — original tabletop concept (spiral Time Track, Brass Cog triggers,
  physical punch-card tiles). Source of truth for *mechanics*.
- `docs/digital-design.md` — visual style, concrete golem roster, and the Workbench
  programming-UI spec for the digital version. Supersedes the tabletop's physical-logic-gate idea
  in favor of a menu-based card system.
- `docs/unity-implementation-plan.md` — the actual architecture/milestone log for this Unity
  project. This is the single most important doc in the repo: it records every milestone (M0–M9)
  with what was built, real bugs hit during live-Editor verification, and manual Editor setup
  steps that can't be reconstructed from git history alone. **Read the relevant milestone section
  before touching a system you haven't worked in** — it usually explains *why* something is built
  the way it is, including rejected alternatives.
- `docs/unity-mcp-setup-guide.md` — how to wire up the MCP-for-Unity bridge so an AI client can
  drive the Editor directly (scene/GameObject edits, Play mode, screenshots). Relevant background,
  not a task to redo.

There is no `.cursor/rules`, `.github/copilot-instructions.md`, or CI config in this repo.

## Engine & environment

- **Unity 6000.5.4f1** (Unity 6 LTS), 2D URP template. Version pinned in
  `ProjectSettings/ProjectVersion.txt`.
- New Input System (not legacy), Cinemachine v3 (installed but not yet wired into the camera — see
  M1 notes), 2D Tilemap + Extras (isometric grid layout), Test Framework (EditMode + PlayMode).
- No DOTS/ECS, no Addressables, no netcode package. Simulation is deliberately plain,
  data-oriented C# driven by a single fixed-tick loop, not per-object `Update()`.
- `ProjectSettings/` and `Packages/` are committed, so a fresh clone opens directly in Unity Hub
  (**Add**, not **New**).

## Commands

There is no CLI build/lint/test harness or CI in this repo — everything runs through the Unity
Editor (or a live MCP-for-Unity bridge, if connected):

- **Run all tests**: Unity Editor → **Window > General > Test Runner** → run the `EditMode` or
  `PlayMode` tab. There's no headless/CLI test runner wired up (Unity batch-mode `-runTests` is
  noted in the implementation plan as a "nice-to-have," not implemented).
- **Run a single test**: right-click it in the Test Runner window → Run, or filter by name there.
- **Play the game**: open `Assets/_Project/Scenes/Main.unity` (all milestone demos, hand-wired,
  running automatically) or `Assets/_Project/Scenes/Sandbox.unity` (the actual player-driven
  scenario — move around, harvest, build, construct+program golems) and hit Play.
- **Regenerate placeholder art**: `python Tools/Art/generate_placeholder_art.py` (needs Pillow).
  Writes PNGs to `Assets/_Project/Art/`; re-importing/rewiring them as Sprites still needs a manual
  Editor pass (texture import settings, Tile assets, `SpriteRenderer` assignment) — see the
  "Graphics demo implementation notes" section of the implementation plan for the exact steps.

As of the last recorded full run (economy/Management-HUD production-quality pass):
**494/494 tests passing** (393 EditMode + 101 PlayMode).

## Architecture

### Assembly layout (asmdefs)

- `GolemFactory.Simulation` (`Scripts/Simulation/`) — **`noEngineReferences: true`**. Plain C#
  only, no `UnityEngine` types allowed. This is deliberate: the tick clock and other core sim
  logic must be unit-testable without a scene.
- `GolemFactory.Runtime` (`Scripts/`, everything outside `Simulation/` and `Editor/`) — references
  `GolemFactory.Simulation` and `Unity.InputSystem`. Almost all gameplay code lives here.
- `GolemFactory.Editor` (`Scripts/Editor/`) — Editor-only, references both of the above. Currently
  just the asmdef shell; no editor tooling has been written yet.
- `GolemFactory.Tests.EditMode` / `GolemFactory.Tests.PlayMode` (`Assets/Tests/`) — mirror the
  runtime folder structure under `EditMode/<Category>/` and `PlayMode/<Category>/`.

### The tick simulation

Everything golem/belt-related runs off one fixed-tick loop, not `Update()`:

- `Simulation/SimulationClock.cs` — plain C# tick source; accumulates real time into a `long`
  tick counter at `TicksPerSecond`, calls `ITickable.Tick(currentTick)` on every registrant in
  registration order. Play/Pause/Speed controlled independently of tick advancement.
- `Simulation/ITickable.cs` / `TickScheduler.cs` — the tick contract and a one-off
  scheduled-callback helper.
- `SimulationClockRunner.cs` — the `MonoBehaviour` wrapper that owns a `SimulationClock` instance
  and calls `Advance()` from `Update()`; exposes `Play()`/`Pause()`/`SetSpeed()` and publishes
  `TickAdvancedEvent`.

### The "Holder" pattern

Every plain-C# manager class (that needs to live in a scene) gets a thin, single-purpose
`MonoBehaviour` wrapper suffixed `Holder` that just owns an instance and exposes it as a property
— e.g. `GridMapHolder` owns a `GridMap`, `ConveyorSystemHolder` owns a `ConveyorSystem`,
`StorageBufferRegistryHolder`, `ResourceNodeRegistryHolder`, `ArtificerFocusMeterHolder`,
`PatentRegistryHolder`. This keeps simulation logic engine-decoupled and unit-testable while still
giving it a scene presence other components can reference in the Inspector. When adding a new
manager-style system, follow this pattern rather than making the logic itself a `MonoBehaviour`.

### Golem execution model

This is the mechanical core of the game and the part most milestones touch:

- `PunchCards/LogicCoreDefinition.cs`, `AppendageActionDefinition.cs`, `ChassisDefinition.cs` —
  `ScriptableObject` **authored definitions** (trigger type, action type, slot capacity/cost).
  Authored `.asset` instances live under `Assets/_Project/ScriptableObjects/{Chassis,LogicCores,
  Appendages}/`. All five named chassis from the digital-design roster (Clockwork Scavenger, Brass
  Presser, Aether-Hauler, Mainspring Overclocker, Zeppelin Freight Loader) share the same
  `GolemEntity`/`GolemProgram` execution path — no per-golem subclassing.
- `Golems/GolemProgram.cs` — plain, per-instance/savable state: assigned chassis, logic core
  instance, ordered appendage list, plus assembly-time capacity enforcement
  (`TryAssignChassis`/`TryAddAppendage`/`RemoveAppendageAt`).
- `Golems/GolemEntity.cs` — the `MonoBehaviour`/`ITickable` that drives a `GolemProgram`:
  `Idle` → `Running` → `Stalled` state machine. **Execution is strictly linear and
  non-adaptive by design**: a precondition failure (empty source, full destination) doesn't
  skip/reorder/substitute — the golem stalls and retries the same step every tick until conditions
  clear, publishing `GolemStalledEvent`/`GolemResumedEvent`. There is no branching in the model;
  rigidity is structural.
  - Trigger types: `AlwaysOn`, `Interval` (evaluated generically), `Threshold` (edge-triggered
    poll of a `StorageBufferRegistry` quantity — fires once per crossing, not every tick above
    threshold), `Signal` (subscribes to `EventBus.GolemCompleted`, latches a pending fire if it
    arrives mid-cycle). Threshold and Signal are implemented directly on `GolemEntity`, **not** a
    separate `GolemTriggerSystem` as an early plan comment proposed — see the M7 notes in the
    implementation plan for why that was rejected.
  - **Gotcha**: `GolemEntity` has no `[ExecuteAlways]`, so `OnEnable`/`OnDisable` (and therefore
    Signal-trigger subscription) only run in Play Mode, not EditMode. Tests relying on that must
    be `PlayMode` tests, not `EditMode`.

### Spatial systems

- `World/GridMap.cs` — simulation truth for occupancy, `Vector2Int`-indexed. **Decoupled from
  rendering** — the Tilemap is purely visual. Isometric presentation only affects the
  `Grid`/`Tilemap` components and camera; grid math stays as if it were a top-down grid.
- `World/GridCoordinateConverter.cs` — pure C# isometric world↔cell math, independent of Unity's
  `Tilemap` component so it's unit-testable without a scene.
- `Belts/BeltSegment.cs` / `ConveyorSystem.cs` — **performance-critical: no GameObject per belt
  item.** Items are `ItemStack{ItemType, Progress}` structs in a `List<ItemStack>` per segment.
  `ConveyorSystem.Tick` runs two full passes (advance-all, then handoff-all) specifically so a
  handed-off item can never be double-advanced in the same tick regardless of dictionary iteration
  order. `Belts/` has no reverse reference to `Golems/` — golem code pulls from belts via
  `TryEnqueue`/`TryPeekHead`/`TryDequeueHead` by segment id.
- `Belts/BeltSegmentVisual.cs` — pools a fixed number of `SpriteRenderer`s sized to segment
  capacity (never grows/shrinks) rather than instantiating one per item. Not currently wired to
  any scene instance (belts render invisibly today) — the visual-only idiom to follow if that
  changes.
- `World/ResourceNode.cs` / `ResourceNodeRegistry.cs` — finite or infinite (`ResourceNode.Infinite`)
  map resource sources, separate from a node's id.
- `Economy/StorageBuffer.cs` / `StorageBufferRegistry.cs` — per-item-type quantities
  (`Dictionary<string,int>`), created on first deposit rather than requiring pre-registration.

### Player-facing systems (Sandbox scene)

`Main.unity` has no player — every demo golem is hand-wired and self-running. `Sandbox.unity` adds
an actual playable front door, reusing `Main.unity`'s systems unchanged via two prefabs
(`WorkbenchCanvas.prefab`, `ManagerHolders.prefab`) plus `GolemPrefab.prefab`:

- `Player/PlayerController.cs`/`PlayerMovement.cs` — analog movement (not grid-locked; only golems
  are grid-locked), same "extract the math into a pure function" idiom as
  `GridCoordinateConverter`.
- `Player/PlayerInteractor.cs` — finds the nearest interactable (resource node, golem construction
  station, existing golem) and dispatches to harvest / open construction panel /
  `WorkbenchController.RetargetGolem`.
- `World/ResourceNodeMarker.cs` — spatial proxy that forwards to the same
  `ResourceNodeRegistry.TryExtract` a golem's `ExtractFromNode` step calls, so player harvesting
  and golem extraction genuinely compete for the same `RemainingQuantity`.
- `Buildings/GolemConstructionStation.cs` — spends a chassis's Scrap/Brass cost via
  `StorageBufferRegistry.TryWithdrawScrapAndBrass`, instantiates `GolemPrefab`, registers it with
  the clock, retargets the Workbench onto it.
- **Known gap** (see implementation plan, "Deliberate scope cuts"): a `SaveLoadService` concept is
  referenced there for restoring golem programs on load, but no such save/load code exists yet —
  `Scripts/Save/` is currently empty (`.gitkeep` only). Don't assume it's implemented.

### UI: two eras coexist

- Most panels (`GolemProgrammingPanel` [disabled, superseded], `InventoryPanel`, `AlertsPanel`,
  `GolemConstructionPanel`, `BuildMenuPanel`) are **immediate-mode `OnGUI`** — no Canvas/scene
  wiring required, but note **OnGUI always draws over Canvas UGUI regardless of sort order** (hit
  as a real bug during M8 — `InventoryPanel` had to be capped to a fixed-size box in a free
  corner to avoid visually colliding with the Workbench).
- The Workbench (`UI/WorkbenchController.cs` + `WorkbenchCard.cs`/`WorkbenchDropZone.cs`) is the
  one real **UGUI** system (Canvas + EventSystem + `InputSystemUIInputModule` — the project's
  Input System setting is New-Input-System-only, so the legacy `StandaloneInputModule` won't
  work). Dragging cards edits a **local draft** copy of the program only; nothing commits to the
  real `GolemEntity.Program` until `EngageGears()` is called. `RebuildUI()` always destroys and
  recreates card GameObjects from data rather than choreographing incremental reparenting —
  follow that "always re-render from data" idiom for new UGUI work here, matching how
  `BeltSegmentVisual` redraws from `BeltSegment.Items`.

### Events

`Events/EventBus.cs` is a static pub/sub bus of `readonly struct` event types
(`TickAdvancedEvent`, `ThresholdCrossedEvent`, `GolemCompletedEvent`, `GolemStalledEvent`,
`GolemResumedEvent`). Add new event types here rather than inventing a second bus or wiring direct
component references across systems that shouldn't know about each other (e.g. UI listening to
golem state).

### Multiplayer-compatible seams (build clean now, no networking yet)

The design is solo-only for v1 but is intentionally architected to grow into the original
multiplayer board game later without a rewrite:

- Ownable entities (`Blueprint`, etc.) carry an explicit `OwnerId` from day one, hardcoded to a
  single `LocalPlayer`.
- `PatentRegistry.TryUseBlueprint(blueprintId, userId)` already has the royalty-charge branch,
  no-op'd when `userId == OwnerId`.
- `ArtificerFocusMeter` is per-player from the start (the seam for later competitive turn order).
- Purely global systems (`SimulationClock`, `GridMap`) are allowed to stay simple singletons —
  don't over-engineer those into per-player state.

Keep this pattern in mind when adding new player-owned data: avoid hardcoding "the" player where a
future second owner would need a rewrite instead of a parameter.

## Conventions specific to this codebase

- **Extract math into pure, engine-free functions** callable from tests without a scene, then have
  a thin `MonoBehaviour` apply the result. Established by `GridCoordinateConverter`,
  `YSortUtility`, `PlayerMovement.ComputeDisplacement` — follow it for new spatial/simulation math.
- **`Configure(...)` methods, not just `[SerializeField]`**, on components with references too
  numerous or too test/bootstrap-unfriendly to wire purely via the Inspector (`GolemEntity
  .Configure`/`.ConfigureEconomy`, `WorkbenchController.Configure*`, `BuildModeController
  .SetActivePrefab`, `CameraRigController.SetFollowTarget`). Lets tests and bootstrap scripts wire
  components directly instead of only through serialized scene state.
- **A prefab cannot hold a field reference into a different prefab** — cross-prefab references
  resolve to `null` on instantiation into a new scene and must be re-wired per-scene explicitly
  (hit converting `WorkbenchCanvas.prefab`'s references into `Sandbox.unity`).
- **`[ExecuteAlways]` is not on most gameplay `MonoBehaviour`s**, so `Awake()`/`OnEnable()` logic
  (sprite assignment, event subscriptions) does not run in EditMode — only in Play Mode. This has
  caused real bugs (golem sprites invisible until Play mode; Signal-trigger tests needing to be
  PlayMode not EditMode). If something works in Play mode but not when just viewing the scene,
  check this first.
- **Bare-string ids** (not enums or object references) identify belts, buffers, and nodes across
  systems (e.g. `"ScrapBuffer"`, `"ScrapBeltA"`) — `Economy/ItemType.cs` holds canonical item-type
  id constants so recipes don't restate raw literals, but node/buffer/belt *instance* ids are
  still plain strings assigned per bootstrap/scene.
- **Registries guard against `null` ids** (an unset `sourceId`/`destinationId`) by returning
  `false` from `TryGet*` rather than letting a raw `Dictionary<string,_>` lookup throw — keep this
  when adding new registries.
- Manual Editor/scene work (composing GameObjects, wiring cross-object references, importing
  textures) generally can't be done blind from a text diff — it's tracked as an explicit checklist
  per milestone in `docs/unity-implementation-plan.md` and, when a live MCP-for-Unity bridge is
  available, executed via `execute_code` (a C# script run directly in the Editor) rather than many
  individual tool calls, since bulk hierarchy/import-setting work is far more reliable that way.
  If you make source changes that require scene/prefab/asset wiring to actually take effect,
  say so explicitly rather than assuming the change is live.
