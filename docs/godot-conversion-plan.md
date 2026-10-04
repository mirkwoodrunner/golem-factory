# Full conversion: golem-factory from Unity to Godot

## Status

Updated by each milestone's PR. The first row that isn't **done** is the current milestone.

| Milestone | Status |
|---|---|
| G0: Land the spike | **done**: PR #28, merged 2026-10-04 |
| G1: Authored data as JSON | in review: PR #29 |
| G2: Gameplay services into Core | in progress, as stacked sub-PRs: **G2a** station + assembly bay (in review), **G2b** build mode, **G2c** interactions, **G2d** save + the rest |
| G3–G10 | not started |

## Context

The spike (`spike/godot`, `docs/godot-spike.md`) proved the port is cheap where it matters.
118 rule files moved into an engine-free `godot/Core`, 1,105 tests pass under `dotnet test`,
`GolemEntity` needed no logic changes, and a playable slice passes a headless end-to-end check.
You've played the slice and like how Godot plays, so this plan takes the game the rest of the
way. The outcome is a Godot build that passes the existing playtest script
(`testscript/phase-1-playtest.md`), with the Unity project then removed.

**Decisions already made (2026-10-03):**
- Authored data becomes **JSON** loaded into Core's plain classes.
- Unity stays as the reference until cutover, then it's **deleted from main under a
  `unity-final` tag**.
- The UI **recreates the current look** with the existing art.
- **No save compatibility** with Unity saves.

**What's left to port:** 85 Unity files, about 21k lines:
- UI: 6.6k
- Editor tooling: 5k, mostly made unnecessary by text scenes
- Player: 3.1k
- Buildings: 2.4k
- World: 2k
- The rest: 2.3k

**Tests not yet ported:**
- 163 EditMode tests: catalog, station, assembly bay, save, FloorExpansion, HandCrankBench,
  AssemblyLineGating.
- 205 PlayMode tests in 28 files.

## Strategy: three rules for the whole conversion

1. **Extract rules into Core before building Nodes.** It worked for `GolemEntity` and it's
   the plan's main lever. The three largest Unity-coupled files mix real gameplay rules with
   engine glue:
   - `Player/BuildModeController.cs` (1,407 lines)
   - `Player/PlayerInteractor.cs` (1,103 lines)
   - `Buildings/GolemConstructionStation.cs` (638 lines)

   Their rules move into engine-free Core services. Their tests, most of the PlayMode suite,
   then become `dotnet test` unit tests, and the Godot Nodes stay thin. The same pattern is
   already in place: `GridConversions`, `WorldNode` and Nodes that only own, convert and draw.
2. **Every Unity test is accounted for.** `docs/godot-test-ledger.md` lists all 1,473 Unity
   tests as *ported*, *replaced by* (a named Core test or scenario), or *retired* (with a
   reason, such as testing a Unity-only API). The spike's assert-count check stays the
   standard: a ported test keeps its asserts.
3. **The settled design calls in the root `CLAUDE.md` carry over verbatim**, and each is
   pinned by a test in Core:
   - Full refund on demolish, and the three rules that keep it honest
   - Drag is not a click
   - The two tools are exclusive in both directions
   - Golems are not GridMap occupants
   - A held golem is refused
   - A wrecking-bar drag never takes a golem
   - No Focus meter
   - A golem is named by its `GolemId`
   - The Assembly Line context is wired before seeding, and stays monotone
   - Labelled depots publish a filtered endpoint
   - `AppendageActionType` and `StallReason` are append-only

## Repo and branching

- **G0 lands the spike on `main`.** The two commits from `docs/backlog-drift` come with it.
- Each milestone below is its own branch and PR into `main`.
- `godot/` stays a subfolder permanently. A root-level `project.godot` would make Godot scan
  `Tools/`, `docs/` and `testscript/`.
- Unity keeps building on `main` until G10. Nothing under `Assets/` changes before then.

## Milestones (in dependency order)

### G0: Land the spike
Open a PR merging `spike/godot` into `main`.
**Exit:** `main` builds both engines, and `dotnet test` and `--spike-check` pass.

### G1: Authored data as JSON
- `Tools/Data/convert_unity_assets.py` (new, one-time, kept for re-runs until cutover) reads
  the 86 `.asset` YAML files under `Assets/_Project/ScriptableObjects/`. It resolves
  cross-references by GUID through the `.meta` files (an appendage's `recipe:` is a GUID), and
  writes `godot/data/{chassis,logic_cores,appendages,recipes,clock_tower,assembly_line}.json`.
  References are written by definition **name**, which is what `DefinitionCatalog` and the
  save file already key on.
- `godot/Core/Data/DefinitionLoader.cs` (new): uses `System.Text.Json` to fill the existing
  plain classes (`ChassisDefinition`, `RecipeDefinition`, ...) and builds a `DefinitionCatalog`.
  It fails loudly on a dangling name.
- Port the 4 held catalog test files (`TechTreeCatalogTests`, `RecipeCatalogTests`,
  `ChassisCostAcyclicityTests`, `ClockTowerStageCatalogTests`) against the JSON, plus
  `DraftableCardCatalog` and `SaveCatalogCoverageTests`.
- A **parity test** loads the JSON and checks every field against the converter's own dump of
  the YAML, so a bad conversion can't slip through.

**Exit:** all catalog tests pass, and `HardcodedDemoProgram` and the slice load definitions
from JSON.

**As built:**
- Seven JSON files, not six. The deck (`assembly_line_decks.json`) is its own type,
  `DraftableCardCatalog`, now a plain Core class.
- The parity check is a round trip: every loaded field must equal its JSON value. Together
  with the loader refusing unknown keys, that covers what a second YAML parse would, without
  a second parser.
- Two items moved to later milestones:
  - `SaveCatalogCoverageTests` opens the Sandbox scene's save panel, so it moved to **G9**.
  - `HardcodedDemoProgram` stays code-defined. It *is* the definition of the reference
    programs the regression suite pins, and pointing it at the data would make those
    programs change whenever the data changes.
- The slice loads its golems' chassis, core and cards from the JSON.
- The test ledger (`Tools/Godot/test_ledger.py` → `docs/godot-test-ledger.md`) started here.

### G2: Extract the gameplay services into Core (the big refactor)
New engine-free services, each fed the registries directly the way `GolemEntity` now is:

| Core service | Rules taken from | Tests that move to `dotnet test` |
|---|---|---|
| `Buildings/BuildService` | `BuildModeController`: place, rotate, demolish, refund (`RefundWouldFit`, `IsRuntimePlaced`, `refund` flag), drag runs (`BuildDragPath`, `TryPlaceDragged`, `DemolishDragged`), tool exclusivity, placement bounds, connected-shape refresh | `BuildModeControllerTests`, `BuildDragRunTests`, `BuildDemolitionRefundTests`, `BuildPlacementBoundsTests`, `BuildingRebuildTests`, `WreckingBarGolemTests` |
| `Buildings/StationService` | `GolemConstructionStation` + `AssemblyBayStructure`: construct, respawn, dismantle (`GolemDismantleRules`), id reservation, bay slots | `GolemConstructionStationTests`, `AssemblyBay*Tests`, `PlacedStation*Tests`, `GolemDismantleTests`, `GolemRespawnTests` |
| `Player/InteractionService` | `PlayerInteractor`: target selection, harvest, crank, refuel, relabel depot, market truckload, golem carry/rotate/place | `PlayerInteractorTests`, `BoilerHandRefuelTests`, `GolemRepositioningTests`, `GolemInteractableRefreshTests` |
| `Save/SaveLoadService` (finish) | Replace its 8 `GetComponent<…>()` calls with an `IPlacedBuilding` interface in Core; `SaveFileIO` moves to the Godot layer (`user://` + `System.Text.Json`) | `SaveLoadServiceTests`, `BuildingPersistenceTests` |
| `World/SandboxSetup` | `SandboxBootstrap`'s data (starting nodes, buffer capacity policy, freight masts, game mode), not its wiring | `FloorExpansionTests` (with `FloorExpansionService`'s rules) |
| Also ported | `HandCrankBench`, `TechTreeProgressTracker`, `AssemblyLineStateHolder`, `TruckloadMarketHolder` and `BufferThroughputMonitor` rules | `HandCrankBenchTests`, `AssemblyLineGatingTests`, `BuildingSignalCoverageTests` |

Buildings in Core are described by an `IPlacedBuilding` (cell, facing, prefab key,
`IsRuntimePlaced`, endpoints it publishes). That's the "building abstraction" the spike
found missing.

**Exit:** the ledger shows every non-UI Unity test ported or replaced, and the suite passes.

**As built (G2a):**
- **The Unity classes keep their names as plain Core classes**, the way `GolemEntity` did
  (`GolemConstructionStation`, `AssemblyBayStructure`), rather than being renamed to
  `StationService`. Tests and docs keep reading the same names, and a Node owns the object.
- **The golem prefab became a golem factory** (`Func<GolemEntity>`). The station raises
  `GolemSpawned` once a golem is fully wired, and the host gives it a Node
  (`GolemNode.Host`). A test's factory records what it builds, which replaces the
  `FindObjectsByType` sweep.
- **`GolemEntity.Remove()` is Unity's `Destroy`.** It runs `Detach()` (`OnDisable`) and sets
  `IsRemoved`, which the assembly bay prunes on. Unity's fake-null asserts (`golem == null`
  after a destroy) translate to `golem.IsRemoved`. That is the same fact, not a weaker one.
- **`IWorkbenchTarget`** replaces the concrete Workbench reference. G7's Workbench will
  implement it.
- **Held back:**
  - `GolemRespawnTests` goes through `SaveLoadService`, so it moved to G2d.
  - `PlacedStationConfigurationTests` is a build-mode test, so it moved to G2b.
- **In the slice,** the station is Core's real one. It charges the Scavenger's 12 Scrap
  from a seeded stockpile, and the spike check asserts that charge.

### G3: Art and fonts pipeline
- Point the five generators' `OUT_DIR` (`Tools/Art/generate_*.py`) at `godot/art/`, keeping
  their file-ownership guard (`TOP_DOWN_OWNED`). `trim_character_alpha.py` and
  `slice_artificer_walk.py` follow.
- Replace Unity's import passes (PPU 64, BottomCenter pivots, 9-slice) with:
  - default import settings (nearest filter, already project-wide)
  - `GridConversions.StandOnCell` for standing sprites
  - centred pivots for the five pipe tiles
  - `StyleBoxTexture` margins for 9-slice UI
- Font: bundle `Assets/TextMesh Pro/Fonts/LiberationSans.ttf` (OFL) as the project font. A
  headless check asserts `Font.HasChar` for the allowlisted glyphs → ≥ █ ░ ·, and adds a
  fallback font only if one is missing. Keep `TechTreeCatalogTests`' ≤ U+00FF guard as is.

**Exit:** a regenerated `godot/art/` matches the Unity art byte for byte, and the font check
passes.

### G4: The world scene
`Sandbox.tscn` at full size:
- Floor, walls and the market street from `FloorLayout` (`GetWorldCells`, edge anchors,
  street cells)
- Props
- All starting nodes, freight masts and Hand-Crank benches from `SandboxSetup`
- Floor expansion
- `Camera2D` follow and zoom (`CameraRigController`'s rules)
- Y-sorting through `y_sort_enabled`

`SandboxBootstrap` becomes a `SandboxNode` that instantiates from `SandboxSetup`. Late
wiring (`IGolemRespawner`, `IBuildingRebuilder`, ...) becomes direct calls on `WorldNode`'s
services. The four seams existed because a Unity prefab couldn't hold scene references.

**Exit:** a scenario run walks the street and back; frames match the Unity Sandbox layout.

### G5: Buildings and build mode
- One `.tscn` per placeable (11), each a thin Node over its Core building plus `BuildService`.
- Ghost, R to rotate, click and drag runs, demolish mode, connected belt and pipe shapes
  (`BeltShapeRules`/`PipeShapeRules`, already in Core).
- Steam visuals, boiler refuel, recycler, slag heap, Clock Tower site.
- Build menu panel (UI, but needed to place anything).

**Exit:** scenarios place, drag, rotate, demolish and refund every placeable; frames are
reviewed.

### G6: Golems in full
- The golem scene: mood tint and badge with `GolemMoodRules` dwell, facing indicator, stall
  indicator, ground shadow.
- `[G]` carry and place, rotate.
- `GolemConstructionPanel`.
- Assembly bay.

**Exit:** a scenario builds every chassis and runs a program through each `AppendageActionType`.

### G7: The Workbench (largest UI piece)
- Control-node rebuild with the mahogany-and-brass theme from `Art/UI/Workbench`.
- Card drag and drop (Godot's `_GetDragData`/`_DropData`), sockets, quantity steppers, loop
  labels, the lever, chassis buttons and the Patents tab.
- The draft/engage state in `WorkbenchController` moves to a Core `WorkbenchSession`, joining
  the already-ported `WorkbenchDropRules`, `WorkbenchStatusPolicy` and
  `WorkbenchQuantityPolicy`. The draft-only-until-Engage rule and `GolemDisplayName` are
  pinned there.
- Port `WorkbenchControllerTests`/`WorkbenchQuantityTests` against `WorkbenchSession`.

**Exit:** program a golem end to end through the UI in a scenario. You review screenshots
next to Unity's.

### G8: Management HUD and remaining screens
- Inventory, alerts, the Artificer's Ledger (`TechTreeChartLayout` in Core, `Art/UI/TechTree`
  chrome), Assembly Line panel, Hand-Crank, Clock Tower and steam gauge views, save/load panel,
  simulation control bar, floating popups.
- `HudScreenPolicy` exclusivity, already in Core.
- Port the UI PlayMode tests that test policy, not pixels.

**Exit:** every screen opens, and the exclusivity scenario passes.

### G9: Save and load end to end
`SaveLoadService` plus Godot `SaveFileIO`, respawning player-built golems and rebuilding
buildings. Includes the "no refund on load" duplicator regression.

**Exit:** a save/load/save/load scenario leaves the stockpile and world identical.

### G10: Parity playtest and cutover
- Run `testscript/phase-1-playtest.md` against the Godot build. You play it too, since
  automation can't judge feel.
- Fix what the playtest finds.
- Then, in one cutover PR:
  - Tag `unity-final`.
  - Delete `Assets/`, `Packages/`, `ProjectSettings/` and the Unity-specific `.gitignore`
    rules.
  - Retire the Unity Editor tooling.
  - Move `docs/unity-implementation-plan.md` to `docs/history/`.
  - Rewrite the root `CLAUDE.md` from `godot/CLAUDE.md`.
  - Update `docs/open-items.md`.

## Verification (every milestone)

- **`dotnet test godot/GolemFactory.sln`**: Core tests pass. The count only grows, and the
  ledger is updated in the same PR.
- **Scenario runner:** the spike's `SpikeCheck` grows into `-- --scenario <name>`. Each
  scenario drives the real scene headless with `--fixed-fps 60`, asserts its outcome, and
  exits 0 or 1. There's no third-party test framework in the game project.
- **Look at it:** `--write-movie` frames for any visual change, reviewed before the PR. This
  is the root `CLAUDE.md`'s "verify a UI change by looking at the game view" rule.
- **Godot compile:** `--headless --build-solutions` is clean.
- **You:** a short hands-on check at G5, G7 and G10, because input feel and UI layout are
  judgement calls.

## Risks

- **G2 is a refactor of rules that hold the settled design calls.** Each rule moves with its
  tests in the same commit, never the rule first and the tests later.
- **The Workbench (G7) is the schedule risk.** It's 1,504 lines of UGUI plus drag and drop.
  Doing it after G2 means its logic is already in Core, so G7 is layout and feel only.
- **Godot 3 vs. 4 API mix-ups.** Check the 4.7 docs when unsure, and keep `godot/CLAUDE.md`'s
  gotcha list current.
- **Intermittent `git add` failures** on this machine: use the retry loop (see memory).
