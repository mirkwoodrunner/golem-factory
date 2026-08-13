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
- `docs/progression-design.md` — the gameplay progression: a 4+ tier tech tree, steam-power
  scarcity, chassis unlock sequencing, and the Clock Tower endgame. Passed a three-round review
  against a 9-point rubric. **Entirely unimplemented** — it is a spec, not a record of what exists.
- `docs/open-items.md` — **read this before planning any work.** Consolidated backlog: what the
  progression pass still needs (in dependency order), the decisions still awaiting a human call,
  known functional gaps, and deliberate scope cuts. Distinguishes what is built from what is only
  designed, which the two docs above do not.

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
- **Regenerate art**: there are now **two** generators, and which one owns a file matters.
  - `python Tools/Art/generate_topdown_environment.py` — the floor, walls, props, belt and
    cursor overlays. Square, top-down, PPU 64. **This is the one that owns the environment.**
  - `python Tools/Art/generate_placeholder_art.py` — everything else (chassis, items, player,
    UI). Still contains the old 2:1 isometric versions of the environment sprites, so it
    refuses to write any filename the top-down generator owns (see `TOP_DOWN_OWNED`); running
    it used to silently revert the whole projection switch.
  - Both write to `Assets/_Project/Art/`. Importing them is **no longer a manual Editor pass**:
    run **Tools > Golem Factory > Rebuild Environment (All Scenes)**, or headless via
    `-executeMethod GolemFactory.Editor.SandboxFloorGenerator.RebuildEnvironmentAllScenes`,
    which applies PPU/pivots, builds the Tile assets, and repaints and re-walls both scenes.

As of the last full run (progression pass, Editor passes, the Hand-Crank Bench, and the
isometric→top-down projection switch): **934/934 tests passing** (824 EditMode + 110 PlayMode).

**Two ways to run the tests, and which one depends on whether the Editor is open.**

- **Editor open, MCP-for-Unity bridge connected** → use the bridge: `run_tests(mode="EditMode")`
  then poll `get_test_job(job_id, wait_timeout=120)`. Both suites run in ~15 s total and, unlike
  batch mode, this works *while the Editor is open*. Use `init_timeout=120000` for PlayMode.
- **Editor closed** → batch mode, below.

**Unity batch mode does run the tests.** The implementation plan records this as a "nice-to-have,
not implemented" — that is out of date. It works, and it is the cheapest way to verify a pass
without driving the Editor by hand:

```sh
"G:/Unity/Hub/Editor/6000.5.4f1/Editor/Unity.exe" -runTests -batchmode \
  -projectPath "G:/GitHub/golem-factory" \
  -testPlatform EditMode \
  -testResults "<somewhere>/editmode-results.xml" \
  -logFile "<somewhere>/editmode.log"
```

Swap `-testPlatform PlayMode` for the other suite. Exit code 0 means everything passed (2 means
failures); the result counts and any failure detail are in the `-testResults` XML, not stdout.
**The Editor must be closed** — it fails on the project lock otherwise. Do not add `-quit`, which
kills the run before the tests report.

While the Editor is open, `dotnet build GolemFactory.<Assembly>.csproj` still works as a fast
**compile-only gate**. Caveats: the `.csproj` files are Unity-generated and git-ignored, and they
use explicit `<Compile Include>` lists, so a newly added `.cs` file must be added to one by hand or
Unity has to regenerate them. It type-checks only — it runs nothing.

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
- `Golems/GolemInventory.cs` — **the machine model** (`docs/progression-design.md` §2, implemented
  as open-items §1.1). Each golem holds an input and an output `Stock`, capped at 12 **per item
  type**. `Haul(itemType, qty)`/`ExtractFromNode(qty)` fill input from the tile behind; `Push`
  empties stock onto the tile in front, mixed types and all; `Assemble` converts input to output
  **without ever touching a tile**. Durations for those three are derived, not authored.
  - **The pure-logistics rule**: a program with **no `Assemble` step treats its input stock as its
    output stock** (`GolemProgram.HasAssembleStep` → `GolemEntity.PushStock`). Without it every
    logistics golem in the game fills input to the cap and stalls forever. It is an explicit named
    special case, deliberately *not* a merging of the two stocks — keeping them separate is what
    lets one `Push` empty everything at once and makes byproducts free.
  - **It rides the `IsSpatiallyPlaced` fork** (below), it does not replace it. Id-routed golems
    keep the pre-machine-model semantics byte for byte, including `step.durationTicks`.
  - Per-slot `Haul` batch size lives on `GolemProgram.appendageQuantities`, **never** on
    `AppendageActionDefinition` — that is a shared asset, so writing a player's quantity there
    would retune every golem holding the card.
  - `AppendageActionType` and `StallReason` are both **append-only**: they are serialized by
    integer index into authored `.asset` files and into `GolemStalledEvent` respectively.
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
- `World/BeltNetwork.cs` (+ `BeltNetworkHolder`) — player-placeable belts. One placed belt is one
  cell: it registers a `BeltSegment` with the `ConveyorSystem`, publishes a `BeltSegmentEndpoint`
  on its cell, and auto-chains into the belt it points at (`BeltPlacementRules.ShouldLink`).
  Wraps `BeltSegment` strictly from the outside, so `Belts/` still references nothing above it.
  **A belt can only hand off to another belt** — getting items into a buffer needs a golem doing
  `LoadIntoBuffer` at the end of the run.
- **Known gap**: `Scripts/Save/` now exists (`SaveLoadService`, `SaveData`, `SaveFileIO`,
  `DefinitionCatalog`) and persists buffers, blueprints, focus, and golem programs (including
  each golem's cell/facing). But it only ever restores a program onto an **already-existing**
  `GolemEntity` — there is no concept of respawning a player-built golem, so golems the player
  constructed do not survive a fresh session.

### UI: all UGUI now (the OnGUI era is over)

- Every live panel is **UGUI**. `GolemProgrammingPanel` is the only remaining `OnGUI` script and
  it has been **disabled since M8**, superseded by the Workbench. `InventoryPanel`/`AlertsPanel`
  converted during the HUD consolidation; `BuildMenuPanel`/`GolemStallIndicator` during the
  graphics pass; `GolemConstructionPanel` during the production-quality pass.
- The reason this mattered is worth keeping: **OnGUI always draws over Canvas UGUI regardless of
  sort order**, so an IMGUI panel could never be made to respect the other screens. That was the
  root cause of the Sandbox HUD overlap, not a cosmetic leftover — mutual exclusion is now
  centralised in `UI/HudScreenPolicy.cs` and covered by a PlayMode exclusivity suite.
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
- **Editor/scene work is scripted, not hand-wired.** Composing GameObjects, adding components and
  wiring serialized references all work headless via `-executeMethod`: see
  `Scripts/Editor/ProgressionAssetAuthoring.cs` (`.asset` files) and
  `ProgressionSceneAuthoring.cs` (prefabs and scenes, via `PrefabUtility.LoadPrefabContents`/
  `SaveAsPrefabAsset`, `EditorSceneManager.OpenScene`/`SaveScene` and `SerializedObject`). Write
  these **idempotent** — find-or-create, update in place — so a tuning pass re-runs them without
  duplicating a GameObject. Note that `Configure(...)` calls do **not** survive to disk: a private
  `[SerializeField]` must be written through `SerializedObject`.
- **`ResourceNodeMarker` owns its `SpriteRenderer.color` at runtime.** `RefreshVisualState()`
  drives it from `ResourceNodeVisualState` as the depletion readout, and since §5.1 made every
  node infinite that pins every marker to `FullTint` (white) on the first frame. **A tint authored
  on a node marker is correct on disk, correct in the Editor, and gone the moment you press Play** —
  so two nodes sharing a sprite are indistinguishable in game no matter how they are coloured.
  Node identity must be the *sprite*. This had already bitten once before it was noticed: the
  Aether marker wore the brass ingot under a teal tint and rendered as plain brass in every play
  session, while `item_aether.png` sat unused.
- **Verify a UI change by looking at the game view, not just its serialized properties.** Both §8
  HUD readouts were once parented to `WorkbenchCanvas.prefab`'s *root* — which is a plain
  `Transform` that happens to share its name with the `Canvas` child — so they were present,
  active, correctly positioned, holding the right text, and invisible, because a `RectTransform`
  with no `Canvas` ancestor never renders. Find a canvas by **component**, never by name.
- **Verify scene changes by reading the scene back, not by trusting the authoring log.**
  `Sandbox.unity` carries prefab **overrides**, so a value written successfully to a prefab can be
  silently replaced by the scene's own copy (this bit `hideWhileOpen` for real — the authoring pass
  reported success and the HUD still drew over the Workbench modal). `SceneProbe.Verify` is the
  read-back pass. If you make source changes that require scene/prefab/asset wiring to take effect,
  say so explicitly rather than assuming the change is live.
