# Full conversion: golem-factory from Unity to Godot

## Status

Updated by each milestone's PR. The first row that isn't **done** is the current milestone.

| Milestone | Status |
|---|---|
| G0: Land the spike | **done**: PR #28, merged 2026-10-04 |
| G1: Authored data as JSON | **done**: PR #29 |
| G2: Gameplay services into Core | **done**: PRs #30–#33 (G2a–G2d) |
| G3: Art and fonts pipeline | **done**: PR #34 |
| G4: The world scene | **done**: PR #35 |
| G5: Buildings and build mode | **done**: PR #36 |
| G5b: The player's hands (pulled forward from G6) | **done**: PR #37 |
| G6: Golems in full | **done**: PR #38 |
| G7: The Workbench | **done**: PR #39 |
| G8: Management HUD and remaining screens | **done**: PR #40 |
| G9: Save and load end to end | **done**: PR #41 (all of #30–#42 landed on `main` through #43, 2026-10-08) |
| G10: Parity playtest and cutover | **done**: playtest fixes #42-#52, then the cutover PR #53 (tag `unity-final`) |

## Next session: start here

**The conversion is finished.** The cutover PR removed the Unity project; the tag `unity-final`
holds its last state, and `docs/history/` holds the Unity-era docs. The game is `godot/`, and the
root `CLAUDE.md` is its guide. What's left is in `docs/open-items.md`:
1. Fix what the playtest report finds.
2. Decide round 3 of the marked-tile plan from the playtest's answer on marked tiles.
3. The data clean-ups the cutover unblocked (the JSON is hand-edited now): rename "Extract
   Scrap", delete the unused `godot/art/clock_tower.png`.

Standing rules: tests and scenarios never write the player's real `save.json`.

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
2. **Every Unity test is accounted for.** `docs/history/godot-test-ledger.md` lists all 1,473 Unity
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
- The test ledger (`Tools/Godot/test_ledger.py` → `docs/history/godot-test-ledger.md`) started here.

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

**As built (G2b):**
- **`BuildModeController` and `PlaceableBuilding` are plain Core classes under their Unity
  names.** A building's kind is a list of `IBuildingPart`s (depot, belt, splitter, boiler,
  pipe, mast, recycler, slag heap, clock tower, and the construction station itself).
  `GetPart<T>()` replaces `GetComponent<T>()`, and `prefab.Instantiate()` clones authored
  settings but not runtime state, so the place and demolish chains ported almost line for
  line.
- **Input became verbs** (`Click`, `Hover`, `Release`, `RotateKey`, `CancelPlacement`). The
  ghost became `GhostStateFor(cell)`, the rule it was coloured by, so the ghost and the click
  can't disagree.
- **Popups, placements, removals and shape changes are events** (`PopupRaised`,
  `BuildingPlaced`, `BuildingRemoved`, `ConnectedShapesChanged`). The belt and pipe parts
  hold their `Shape` as state for the scene to draw.
- **`FindObjectsByType` became explicit lists:**
  - `Buildings`, the buildings build mode placed, plus scene furniture registered through
    `RegisterExistingBuilding`.
  - A golem roster the host supplies (`ConfigureGolemRoster`).
  - Unity's **static** belt and pipe rosters are gone, because they would leak between tests.
- **Two test-porting hazards were caught,** and they're worth knowing for the rest of G2:
  - **Fake-null asserts.** `x != null` meant "not destroyed" and becomes `!x.IsRemoved`.
  - **Implicit scene membership.** A golem or building the Unity test merely created was
    findable by scene scan. Ported as-is, "the sweep made zero dismantle calls" passes
    because nothing is *findable*. Those tests now register their golems and furniture
    explicitly.

**As built (G2c):**
- **`PlayerInteractor`, `HandCrankBench` and `ResourceNodeMarker` are plain Core classes.**
  The bench is a building part that ticks. The marker keeps its harvest, depletion and
  endpoint logic and exposes `Visual`/`HarvestCount` for the scene to draw.
  `HandCrankBenchTests` and `ResourceNodeMarkerTests` moved up from G2d, because the
  interactor needs both.
- **The world is handed over** (`ConfigureWorld`: markers, buildings, golems). Stations,
  boilers, depots and benches are reached as parts of the buildings.
- **Positions:** Unity read every interactable's *transform*, which isn't always its cell (a
  carried golem rides with the player). `ConfigurePositions` keeps that separation. The
  scene answers with node positions, and the default is the cell centre. Tests answer with
  where their GameObjects stood.
- **Lifecycle and input:** `OnEnable`/`OnDisable` became `Attach()`/`Detach()`, input became
  verbs plus `SetInteractHeld` for the crank, and `Update` became `Poll()`. The screens
  became `IScreen`, `IConstructionScreen` and `IWorkbenchScreen`.
- **A third porting hazard: fixture reuse.** NUnit reuses one fixture instance per class, so
  a world list in a test rig must be cleared in TearDown. Unity's TearDown destroyed every
  GameObject, and without the clear, golems leak between tests.
- **Done in G4:** the slice's `Nodes/ResourceNodeMarker`, which shared a name with Core's, is
  now `NodeMarkerNode`.

**As built (G2d), which completes G2:**
- **Save and load in Core.** `SaveLoadService` reads building state through `GetPart<T>()`.
  `IBuildingRebuilder` and `IGolemRespawner` and their two adapters are ported, without the
  `FindInScene` fallbacks, because the scene constructs them with the real objects.
- **Four more classes ported:**
  - `ClockTowerSiteHolder`, which ticks the site and relays `ItemAssembled`
    (`Attach`/`Detach`).
  - `TechTreeProgressTracker`, whose sweeps are unchanged; `Exists<T>` became "any building
    has that part".
  - `FloorExpansionService`'s rules. Painting and walls go to the scene, through
    `RowsAdded`.
  - `FloorBoundsHolder` and `AssemblyLineStateHolder` were pure holders, so callers use the
    object.
- **One replacement and one partial port,** both recorded in the ledger
  (`Tools/Godot/test_ledger.py`'s `PARTIAL`):
  - `FloorExpansionTests.PaintingWritesATileOnEveryNewCell` exercised Unity's Tilemap. It's
    replaced by a test of what Core now owes the scene: exactly the rows a purchase added.
  - Two `AssemblyLineGatingTests` drive the Workbench's card gating and are held for **G7**.
- **G2 exit criterion met.** Every non-UI Unity test is ported or replaced. What remains is
  held for the scene and UI milestones (G4, G5, G7–G9).


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

**As built (G3):**
- **One art root.** Every generator and the trim tool take their output folder from
  `Tools/Art/art_paths.py` (`godot/art/`), and `--out-root` redirects any of them. All 147
  sprites are in `godot/art/`, with `UI/…` folders mirrored.
- **Exit met.** `Tools/Art/verify_art.py` regenerates everything into a scratch folder: all
  68 generated sprites are byte-identical to the committed art. The other 79 are authored
  (walk frames, item and chassis art, the Steampunk pack) and are listed by name. The trim
  tool reports every standing sprite already trimmed.
- **No import pass.** Unity's PPU, pivot and 9-slice settings are applied where each sprite
  is drawn: `StandOnCell` for standing sprites, centred pipe tiles (G5), `StyleBoxTexture`
  margins (G7). Nearest filtering is already project-wide.
- **Font.** `godot/fonts/LiberationSans.ttf` (OFL licence alongside) is the project font. It
  covers printable Latin-1 and all four allowlisted glyphs, so no fallback font is needed. A
  probe confirmed `HasChar` returns false for glyphs the font lacks, so the check has teeth.
- **The scenario runner.** The spike's `SpikeCheck` became `Scripts/Scenarios/ScenarioRunner`
  with `-- --scenario <name>`: `loop` (alias `--spike-check`) and `font-glyphs`. An unknown
  name fails, and `--demo` replaces `--spike-demo`.

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

**As built (G4):**
- **The layout is a Core function, not a baked scene.** Unity's `SandboxFloorGenerator` wrote
  about 300 GameObjects into `Sandbox.unity`. `Core/World/SandboxLayout` holds the same
  placement rules, and `Scripts/World/ShellNode` builds the walls, kerbs, posts, furniture,
  clutter, contact shadows and seven sconces from it at load. `SandboxLayoutTests` checks
  every count and a dozen positions against the numbers read from `Sandbox.unity`; all of
  them matched on the first run.
- **The setup is data.** `godot/data/sandbox.json` holds what was split across
  `Sandbox.unity`, `ManagerHolders.prefab` and `SandboxBootstrap`: the nine stalls, the
  market, the game mode, `requireSteamPower`, the buffer policy, the free scrap seed, and the
  starter bench and station. `Core/World/SandboxSetup` applies `SandboxBootstrap`'s rules.
  `WorldNode.ApplySandboxSetup` turns it on.
- **The scenes split.** `Sandbox.tscn` is the real world: floor and street, shell, stalls,
  the starter Hand-Crank Bench (a real ticking `HandCrankBench`), the starter station (every
  chassis), and the player. `LoopSlice.tscn` keeps the spike's hand-built slice for the
  `loop` scenario, behind `ConstructionStationNode.SliceAutoProgram`; the slice's
  auto-programming stand-ins never run in the real Sandbox.
- **Pivots** are Unity's import pivots, value for value, in `Scripts/World/SpritePivots`.
- **Lighting:** a `CanvasModulate` at 0.95 plus a `PointLight2D` per sconce, in Unity's lamp
  colour, sitting a quarter cell inside the room.
- **Camera and walk rules moved into Core:** `CameraRigRules` (follow lerp 5/s, zoom clamp
  3..15, starting size 10) and `PlayerWalker` (`PlayerController.MoveBy` plus
  `ArtificerWalkAnimator`). The last two G4-held suites are ported onto `PlayerWalker`. Each
  dropped one engine-only test, recorded in the ledger.
- **Scenario `world`:** checks that the shell matches the layout, that every stall publishes
  its endpoint, that the player reaches the street's far edge and is stopped there, that they
  walk back inside, and that the camera settles on them. Frames reviewed: the workshop, and
  the street with all nine carts.
- **Floor expansion:** the hooks are in (`FloorLayer.PaintRows`, `ShellNode.RebuildWalls`,
  `WorldNode.Bounds` read by the walk clamp), but nothing in the scene triggers a purchase
  yet. The purchase action arrives with the build menu in G5.
- **Not needed:** freight masts and extra benches have no starting placements in Unity's
  Sandbox; they are placeables (G5). The four late-wiring seams are still Core interfaces,
  as G2 ported them.
- No Unity reference screenshot exists (the MCP bridge was down), so "frames match" was
  checked against the scene's data instead.

### G5: Buildings and build mode
- One `.tscn` per placeable (11), each a thin Node over its Core building plus `BuildService`.
- Ghost, R to rotate, click and drag runs, demolish mode, connected belt and pipe shapes
  (`BeltShapeRules`/`PipeShapeRules`, already in Core).
- Steam visuals, boiler refuel, recycler, slag heap, Clock Tower site.
- Build menu panel (UI, but needed to place anything).

**Exit:** scenarios place, drag, rotate, demolish and refund every placeable; frames are
reviewed.

**As built (G5):**
- **The build menu is data.** `godot/data/placeables.json` holds the ten prefabs Unity's
  Sandbox build menu offered, in its order. `convert_unity_assets.py` gained a prefab pass that
  reads them through `Sandbox.unity`'s `_availablePrefabs`, and its `--check` covers them.
  `Core/Data/PlaceableCatalog` loads them into `PlaceableBuilding` prefabs with their parts,
  as strictly as `DefinitionLoader` does. There is no `.tscn` per placeable: a building is a
  Core object, and one `BuildingView` draws any of them from its catalog entry.
- **The plan said 11 placeables; Unity's Sandbox offers 10.** The belt splitter exists in
  Core (`PlaceableBeltSplitter`, with tests), but no Unity prefab was ever authored for it, so
  it isn't in the build menu. Adding it is a design call, not a port, and is left for you.
- **`SandboxBootstrap` is now Core.** `Core/World/SandboxWorld` owns:
  - every registry
  - steam, masts, the clock tower site
  - the extractor and bay caps
  - floor expansion and the market
  - the starter bench and station
  - `BuildModeController`, wired the way the bootstrap wired it

  `ConfigureStation` is the one station-wiring path, for the starter station and for a placed
  one. `WorldNode` just owns a `SandboxWorld` and forwards to it. The late-wiring seams remain
  as the interfaces `BuildModeController` asks through; the world implements them directly.
- **Godot side:**
  - `BuildingsLayer` draws every placed building: belts and pipes as rotated floor tiles
    shaped by Core's shape rules, and belt cargo drawn per item rather than as a node each.
  - `BuildCursorNode` turns the mouse into `Click`/`Hover`/`Release` and handles R, Escape and
    right-click. It draws the ghost from `GhostStateFor`.
  - `BuildMenuNode` is Unity's panel with the same art and measurements: rows toggle, the
    highlight is polled, and Demolish is the last row.
- **Z-order:** the floor moved to z −10. Z-index is global within a canvas layer, so at 0 the
  floor drew over everything floor-level (belts, pipes, the ghost, prop shadows), which G4's
  frames hadn't revealed.
- **Scenario `build`:** drives the real menu and the world with synthetic mouse and key
  events. It checks that the menu has 11 rows, Demolish is last, the Demolish row toggles, and
  the two tools are exclusive. It places 8 placeables by row and click, turning the depot with
  R first, checks the ghost, and checks that selecting a row builds nothing underneath. It
  drags a belt run and a pipe run off a boiler (checking the network and the pieces), then
  Escape, then demolishes by drag and by click. The stockpile ends exactly where it started.
  `BuildMenuDemolishRowTests` is replaced by this scenario in the ledger's new `REPLACED`
  table.
- **Headless window:** headless runs got a 64×64 window, which put the menu off-screen.
  `ScenarioRunner` now gives them the project's 1280×720.
- **Frames reviewed:** the menu with a tool lit, every building standing, the pipe and belt
  runs, and the ghost with its arrow. The first frame caught dark text on dark plates (the
  row tint also darkened the caption). It was fixed by making the caption a child Label, as
  Unity's was.
- **Not in G5, deliberately:**
  - Player interactions with placed buildings (refuelling a boiler by hand, relabelling a
    depot, cranking a placed bench). They go through Core's `PlayerInteractor`, which is wired
    in G6 together with the `[G]` carry.
  - The steam gauge and the floor-expansion purchase button are UI, and come in G8.
- **Unity parity, noted:** the starter bench and station aren't `GridMap` occupants, so a
  building can be placed on top of them, as in Unity. Registering them would make a click
  with a placeable demolish them, since a click on an occupied tile demolishes, and with no
  refund. Fixing this is a design call.

### G5b: The player's hands (pulled forward from G6, after the G5 hands-on check)

Your first look at G5 found that the carts said nothing and the bench had no interface. The
Godot player still ran the spike's own `IInteractable` code, so only the station answered
`[E]`. That made the build look broken, so the interaction layer moved ahead of the rest of G6.

**As built (G5b):**
- **Core's `PlayerInteractor` drives the Sandbox player.** `SandboxWorld` wires it the way
  `SandboxBootstrap` did. `PlayerNode` feeds it the position and the held state of `[E]`,
  polls it, and passes on `[E]`, `[R]` (after build mode has had its turn) and `[G]`. The
  starter bench and station are now authored `PlaceableBuilding`s, so `[E]` finds them. They
  still aren't grid occupants and aren't in `BuildModeController.Buildings`, which matches
  Unity. `LoopSlice` keeps the spike's interactables.
- **Views, all Unity's look:**
  - `InteractionPromptNode`: the breathing ring under the target, and Core's prompt caption
    above it ("[E] Harvest Scrap - 59 left"). It also draws the rising popups from the
    interactor and from build mode: gains, refusals, spends and refunds. Captions and popups
    are drawn in screen space, so they stay readable at every zoom.
  - `HandCrankPanelNode`: the bench readout, shown while you stand at a bench. Hold `[E]` to
    crank; `[R]` changes the recipe.
  - `ConstructionPanelNode`: "Construct Golem". Each chassis row shows its portrait, tier and
    slots or the shortfall, and its cost; an unaffordable row is disabled. A build closes the
    panel, because the Workbench (G7) would open there. Escape or `[E]` closes it too.
  - `ModalScreens`: an open screen holds the player still and hides the world prompt.
- **A real bug found by the new scenario:** the station publishes "interactables changed"
  before it raises `GolemSpawned`, so the interactor re-snapshotted a golem roster one short.
  `[G]` next to a fresh golem then said "no golem in range". Unity's snapshot was a scene
  scan, which had already seen the new object. `SandboxWorld` now refreshes again once its
  roster has changed (`AFreshlyBuiltGolem_IsWithinReach_AndGCarriesIt`).
- **Scenario `interact`,** with real key and mouse events:
  - Scrap stall: the caption shows, `[E]` harvests one Scrap, and a popup rises.
  - Empty Coal stall: `[E]` orders a truckload and pays for it.
  - Bench: the panel shows, and holding `[E]` cranks.
  - Station: `[E]` opens the panel with every chassis, the player is held while it's open,
    and a row click builds a Scavenger.
  - `[G]` carries the new golem and sets it down.
  - Out of reach of everything: no prompt.
- **Frames reviewed:**
  - The stall prompt with "+1 Scrap", the crank readout, and the construction panel.
  - The panel's first frame showed a bright brown window, because Unity's dark window tint
    was missing, and costs clipped at the front. Both are fixed.

- **Depth fix, also from your check ("background objects show up in front of the player"):**
  the spike's `StandOnCell` put a standing sprite's feet half a cell below its node, and
  y-sorting compares nodes. So the player, the golems and every building sorted by a point
  above their own feet, while the G4 props sorted at theirs: just south of a crate, the crate
  drew over the player. `StandOnCell` now puts the feet on the origin, as Unity's BottomCenter
  pivot did, which also moves those sprites half a cell up to where Unity drew them. The
  `world` scenario checks all 96 standing sprites, with each one's feet within 4px of its sort
  point. Before the fix it failed with "Player draws its feet 32px from its sort point".

### G6: Golems in full

(G5b already did the `[G]` carry, the interactor and the construction panel. What remains is
the golem's own presentation and the assembly bay.)
- The golem scene: mood tint and badge with `GolemMoodRules` dwell, facing indicator, stall
  indicator, ground shadow.
- `[G]` carry and place, rotate.
- `GolemConstructionPanel`.
- Assembly bay.

**Exit:** a scenario builds every chassis and runs a program through each `AppendageActionType`.

**As built (G6):**
- **`GolemNode` now has Unity's golem presentation, in one node.** It combines four Unity
  components:
  - `GolemVisual`: the chassis art (a generic copper golem until a chassis is fitted), a mood
    tint, an idle bob paced by the mood, and a short shake when a stall is published. Only the
    body sprite moves, so the bob never changes the golem's depth.
  - `GroundShadow`: a contact shadow under the feet.
  - `GolemFacingIndicator`: the gold facing arrow, plus the teal source tile and gold target
    tile.
  - `GolemStallIndicator`: the mood badge, shown only after `GolemMoodRules`' dwell. A stopped
    golem's badge names the reason ("[!] PlayerGolem-007 / no steam at (9, 6)").

  The spike's "Working · 3" debug label is gone.
- **`RoutingFocusNode`** (Unity's `RoutingFocusController`): only the golem nearest the player,
  within 3.5 cells, lights its routing tiles.
- **`WorldNode` calls `WorldHudRegistry.Solve` first thing each frame** (Unity's
  `WorldHudSolver`), so mood badges are laid out without stacking.
- **The assembly bay** is wired in Core (`SandboxWorld`, since G5) and caps construction. Its
  rows in the Management screen are UI, and come in G8.
- **Scenario `golems`:** every chassis is built through the construction panel's own build, and
  each runs on steam in the live Sandbox. Three boilers are placed by the build controller and
  refuelled with the player's own refuel action. Programs:
  - Scavenger: Haul and Push onto a belt.
  - Brass Presser: Haul off the belt, then LoadIntoBuffer.
  - Scavenger: ExtractFromNode at the Scrap stall.
  - Overclocker: Extract Coal from a truckload the player ordered, Assemble R1, Repeat,
    then Push. That gives +3 Coke a cycle.
  - Aether-Hauler: Refine.
  - Zeppelin: Haul, then FreightLaunch to a mast.

  A seventh golem placed out of steam must stall on `NoSteam` and wear the badge saying so.
  Every golem must draw its own chassis art. It passed on the first run. Frames reviewed: the
  row of working golems on the pipe main, and both stall badges.
- `[G]` carry, rotate and the construction panel landed early, in G5b.: The Workbench (largest UI piece)
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

**As built (G7):**
- **`Core/UI/WorkbenchSession`** holds the logic of Unity's `WorkbenchController`, with the
  UGUI taken out:
  - The draft: chassis, trigger, six sockets and per-socket batch sizes. Dragging edits only
    the draft.
  - The target: Open and Retarget re-read the committed program.
  - Card gating, and socket highlights while a card is held.
  - Engage: refused, with a status, when there's no target or when the program has more steps
    than there are sockets.
  - Patent, loaded blueprints, chassis selection with the too-small refusal, and the status
    line that retires itself.
  - Every readout.

  The lever's throw and the chassis flash are events, because they're presentation.
  `GolemEntity` gained `name` (Unity's `Object.name`), used only as the header's fallback.
- **`SandboxWorld`** owns the session and a `PatentRegistry`, and gives stations the session
  as their Workbench target, so a new golem becomes the target, as in Unity. The roster is
  `sandbox.json`'s `workbench`, read from the prefab and `Sandbox.unity`: 5 chassis, 2 cores
  and 24 cards. Screens register through `ConfigureScreens`, each passing null for the others,
  so the construction panel and the Workbench find each other. After a build, the panel opens
  the Workbench on the new golem, as Unity's did.
- **`Scripts/UI/WorkbenchScreen`** is the prefab rebuilt from its own numbers. `Ugui.Place`
  converts a RectTransform's anchors, position, size and pivot into Godot anchors and offsets.
  At the 1280×720 reference a UGUI pixel is a Godot pixel. Cards use Godot's drag and drop. A
  drag with no taker is the session's "released over nothing". The screen re-renders from the
  session whenever its `Version` moves, so a failed drag can't orphan a card.
- **Card gating is off in the Sandbox for now.** `Sandbox.unity` gates the vault
  (`gateWorkbenchRoster: 1`), but claiming happens in the Assembly Line panel, which is G8.
  Gating before then would leave only the two starting verbs. The session supports gating
  (both gating tests are ported), and it gets turned on with the panel in G8.
- **Tests:** 29 of `WorkbenchControllerTests`, 9 of `WorkbenchQuantityTests` and the 2
  Assembly Line gating tests are ported onto the session, plus 2 `SandboxWorld` wiring tests.
  The other 10 Unity tests were about UGUI plumbing, and are checked by the new scenario.
- **Scenario `workbench`,** with real mouse and keys:
  - A Scavenger is built, and the Workbench opens on it with the panel closed.
  - The vault shows 26 cards, and none of them carries a dial.
  - Holding a card lights exactly the sockets that would take it.
  - Real drags fill TRIGGER, STEP 1 and STEP 2, and the dial's + raises the batch size. The
    golem is untouched until Engage.
  - A vault card dropped on nothing, twice, leaves no orphan and no ghost. A socketed card
    dropped on nothing leaves its socket.
  - ENGAGE GEARS commits the core and both steps with batch size 2. PATENT stamps BP-001.
  - CLOSE hides the screen. `[E]` at the golem reopens it, re-read from the program.
  - With no target the lever goes dead.
- **Frames reviewed.** One fix came from them: the socket captions were truncated to
  "STEP 1 · t…". TMP had wrapped them onto two lines; they wrap now.
- `interact` and `golems` now expect the Workbench to open after a build, and close it.

### G8: Management HUD and remaining screens

(Also turns on the Workbench's card gating, together with the Assembly Line panel that grants
claims. See G7's As built.)
- Inventory, alerts, the Artificer's Ledger (`TechTreeChartLayout` in Core, `Art/UI/TechTree`
  chrome), Assembly Line panel, Hand-Crank, Clock Tower and steam gauge views, save/load panel,
  simulation control bar, floating popups.
- `HudScreenPolicy` exclusivity, already in Core.
- Port the UI PlayMode tests that test policy, not pixels.

**Exit:** every screen opens, and the exclusivity scenario passes.

**As built (G8):**
- **Core models**, one per panel, each the decisions of a Unity panel with its UGUI taken out:
  - `InventoryReadout`: the rows, with rates from a `BufferRateTracker` that `SandboxWorld`
    now samples in real time (Unity's `BufferThroughputMonitor`).
  - `AlertsStrip`: reconciles against the roster every 0.5 s.
  - `ManagementTabs` and `PatentBrowser`.
  - `AssemblyLineBoard`: the wallet, bay, floor and slot rows, with Claim, Upgrade and Extend.
  - `TechTreeReadout`: the Ledger's selection and recipe pane.
  - `ScreenCoordinator`: opening any full screen closes the others. In Unity that was spread
    over three classes and once let screens stack.
- **The Assembly Line is live.** `SandboxWorld` wires `SandboxBootstrap.RegisterAssemblyLine`
  in its order: the unlock context from the tech-tree ledger *before* seeding, then the
  opening hand, then claims. The Workbench vault is now gated (`gateWorkbenchRoster: 1`, as in
  `Sandbox.unity`). The tech-tree tracker polls and promotes cards as the factory produces new
  goods.
- **Godot screens, from WorkbenchCanvas.prefab's numbers:**
  - `ManagementScreen` (Tab): Inventory, AssemblyLine, Patents, SaveLoad and Ledger tabs.
  - `AssemblyLineTab`.
  - `LedgerTab`: the full chart over the tt_* chrome, drag to pan, with the recipe pane
    outside the chart.
  - `HudOverlay`, replacing the spike's debug readout in the Sandbox: steam gauge, alerts
    strip, Clock Tower panel, and the simulation control bar (pause/play, 0.5x–4x).

  Floor Expansion now works end to end: Extend buys rows, which are planked and walled, and
  the walk and build bounds follow.
- **Unity fixes found in the port** (each has a test or scenario):
  - **The Patents Load button lost the blueprint.** It loaded the blueprint, then opened the
    Workbench, whose Open re-reads the target golem's program. Godot opens first, then loads.
  - **The Assembly Line wallet was `ScrapBuffer`**, an M9 setting never revisited after the
    economy moved to `FactoryStockpile`. A Scrap-priced card read its price against a buffer
    the player can't fill. Claims now pay from the stockpile, like the bay and floor rows.
- **Port bugs caught by the frames and the scenario, all fixed:**
  - The Clock Tower panel said "starved of FrameSection" with no tower built, because the site
    was given every stage at startup. Unity's site starts empty, and a built tower brings its
    stages.
  - Rebuilt walls lost their names (a queued node stays in the tree).
  - A Ledger rebuild flashed unstyled plaques for one frame.
- **The SaveLoad tab** is drawn as the prefab has it, with its buttons disabled and a line
  saying save/load arrives in G9.
- The Assembly Line opened with the three opening-hand verbs in its slots, cards the player
  already owns, because Unity seeded the slots before granting the hand and only a refill
  skips owned cards. **Fixed in G10 at the user's call:** the hand is granted first, so the
  line opens on cards the player can use (`SandboxWorldTests
  .TheAssemblyLineOpens_OnCardsThePlayerDoesNotOwn`, and the `management` scenario).
- **Tests:** 7 held suites ported onto the Core models, plus regression tests for the fixes;
  4 tests were replaced by the new `management` scenario. Ledger 1465/1473; the 8 left are
  G9's save tests.

### G9: Save and load end to end
`SaveLoadService` plus Godot `SaveFileIO`, respawning player-built golems and rebuilding
buildings. Includes the "no refund on load" duplicator regression.

**Exit:** a save/load/save/load scenario leaves the stockpile and world identical.

**As built (G9):**
- **`Core/Save/SaveFileIO`** stays engine-free, contrary to the plan's "moves to the Godot
  layer". It's plain System.Text.Json over a path the caller passes; Godot hands it
  `user://save.json`, globalized. Tests point it at a temp file.
- **`SandboxWorld.SaveTo` and `LoadFrom`** do what Unity's SaveLoadPanel did, with its exact
  status lines. A load works in this order:
  1. Rebuild the placed buildings, clearing the old ones **with no refund**, so save/load can't
     duplicate goods.
  2. Restore golems still standing in place.
  3. Rebuild player-built golems that are gone through the station, with the same id, program
     and place.
  4. Report golems no station built as skipped.

  `AdoptGolem` adds scene-authored golems to the roster.
- **The SaveLoad tab works.** Save and Load go through the world, and the file is
  `ManagementScreen.SavePath`. Scenarios use a scratch file, never the player's save.
- **Tests:** `SaveFileIOTests` (2), `SaveCatalogCoverageTests` (2) and `SaveLoadPanelTests` (4)
  are ported, plus regression tests that a gone player-built golem is rebuilt and that
  save/load/save/load doesn't duplicate goods. **The ledger is complete: 1473/1473 Unity tests
  accounted for, none held.**
- **Scenario `save`,** through the tab's own buttons:
  - Build two depots, a belt and a programmed Scavenger, then save.
  - Wreck everything: demolish, dismantle, spend.
  - Load: the buildings and their views come back, the golem is rebuilt with its program and
    place and its node is hosted, and the stockpile is exactly as saved.
  - Two more rounds change nothing.
- **Not saved, matching Unity's SaveData (yours to decide in G10):**
  - Assembly Line claims and the tech-tree ledger.
  - The floor's expanded extent. In a fresh session, a building past the starting north wall
    would be skipped.
  - Clock speed and the paused state.

  None of these were in Unity's save either. **All four are saved since G10, at your call**
  (see below).

### G10: Parity playtest and cutover
- Run `testscript/phase-1-playtest.md` against the Godot build. You play it too, since
  automation can't judge feel.
- Fix what the playtest finds.
- Then, in one cutover PR (**done**, #53):
  - Tag `unity-final`.
  - Delete `Assets/`, `Packages/`, `ProjectSettings/` and the Unity-specific `.gitignore`
    rules.
  - Retire the Unity Editor tooling.
  - Move `docs/history/unity-implementation-plan.md` to `docs/history/`.
  - Rewrite the root `CLAUDE.md` from `godot/CLAUDE.md`.
  - Update `docs/open-items.md`.

**As built (G10, playtest fixes so far):**
- **The Assembly Line opens on cards the player doesn't own.** The opening hand is granted
  before the deck is seeded, so the first fill skips it.
- **A save holds the factory's progress** (`SaveData.progress`, a `ProgressEntry`):
  - Assembly Line claims
  - the tech-tree ledger
  - the floor's north wall
  - the assembly-bay tier (not on the list, but bought progress lost the same way)
  - clock speed and pause

  A load restores progress before the world. The ledger goes first, because the line's
  unlock context reads it. The floor goes before the buildings, so one on a bought row has
  ground to stand on.

  A load **replaces** progress rather than merging it. Otherwise a card bought after the save
  would survive a load that also hands back its cost. The line is rebuilt as a fresh session
  holding those claims would build it, not in the old queue order. A load can also shrink
  the room (`FloorExpansionService.RowsRemoved`): the planks and back wall follow. A G9 save
  with no `progress` leaves progress alone.
- **The belt splitter is in the build menu,** right after the belt, for 4 Scrap.
  - Core's `PlaceableBeltSplitter` and its rules already existed; Unity never authored a
    prefab.
  - The converter adds it as a Godot-only entry (`belt_splitter_entry`), so a regeneration
    keeps it.
  - Its tile, `belt_splitter.png`, is generated by `generate_topdown_environment.py`: a
    turntable with an outward chevron on each side, never rotated, and no facing arrow on the
    ghost.
  - It isn't drag-placeable, is fully refunded, and saves and loads as a splitter.
  - The `build` scenario clicks one onto a run's end and checks that it feeds two branches.
- **An empty market stall's caption names what [E] does there.** It used to say "Harvest
  CopperOre - depleted" while the key ordered a truckload. The cases are now:
  - empty: `[E]  Order a truckload of Copper Ore  -  20 Scrap`
  - cart on the road: `Copper Ore  -  cart on its way`, with no key
  - stocked: `[E]  Harvest Copper Ore  -  5 left`

  Item names use `ItemTiers.DisplayName`. `InteractionTargeting.BuildPrompt` takes an optional
  verb override. Tests are in `SandboxInteractionTests`, and the `interact` scenario checks
  the real caption before and after ordering.
- **Belt cargo, from playtest ("looks weird", "too fast").** Unity drew cargo on a straight
  back-to-front line, which went wrong in three places:
  - corners drew cargo arriving from the wrong side
  - the splitter's cargo ran along a facing it doesn't have
  - a dead end's front item hung half off the belt

  `World/BeltCargoPath` now draws entry edge → centre → exit edge. The entry is the side that
  feeds the belt. The exit is the front, or for a splitter the branch its round-robin takes
  next, and a dead end stops short of the edge. Cargo draws above all belt tiles (tile z −2,
  cargo −1).

  Belts ran at Unity's 2.5 cells a second. They now run at **1**: `sandbox.json`
  `beltCellsPerSecond`, applied as `ConveyorSystem.StepPerTick`, which keeps item spacing and
  capacity the same. `BeltCargoPathTests` covers this, and the `build` scenario now runs
  Scrap through a splitter into two dead-end branches.
- **A no-steam stall says why.** It was always "no steam at (x, y)", even when the pipe ran
  to the golem and the boiler was simply empty. `SteamNetwork.Diagnose` tells apart:
  - no pipe reaching the tile
  - every reaching boiler out of Coke
  - a fuelled boiler at its 8-golem limit

  The cause is `GolemEntity.SteamShortage`, read by the badge ("my boiler is out of Coke")
  and by the alerts strip, which also names the fix. A stall badge fades to 25% while the
  player stands within two cells, so it stops covering the golem's tile and pipe.
  `SteamShortageTests` covers this.
- **A step-by-step guide** (you chose it over a checklist or contextual hints).
  `Core/Tutorial/TutorialGuide` has eleven steps, following testscript Part E's cold start:
  1. Scrap
  2. a Coal truckload
  3. Coke
  4. Iron Plate at the bench
  5. a boiler
  6. fuelling it
  7. a Scavenger
  8. programming it
  9. a depot
  10. setting the golem to work
  11. the Tab screens

  **Every step is detected from the world, never ticked off by hand**, so doing things early
  skips ahead, and advancing is one-way. It's on in `sandbox.json` (`"tutorial": true`) and
  saved with progress.

  `Scripts/UI/TutorialPanel` draws it:
  - a plate under the Clock Tower panel showing the step and its count ("Coal 3 / 5")
  - a bobbing arrow over the target, or pinned to the screen edge pointing at it when off
    screen
  - a pulsing outline on the build-menu row a step needs
  - **Skip guide**; F1 hides it and brings it back

  Over a full screen it shows only during "Program it", in the Workbench's empty viewport
  space. Bottom right, it covered the ENGAGE lever, caught in frames. Tests are in
  `TutorialGuideTests`, which play a cold start through to a working golem, and the new
  `tutorial` scenario.

  **The first golem has a marked layout** (from playtest), beside the free Scrap stall: the
  golem one tile north of the stall facing north, the depot in front of it, and the boiler
  beside it. The Boiler, Depot and Put it to work steps each mark their tile with a pulsing
  floor outline, plus a facing arrow for the golem's.
  - The depot must stand on its tile.
  - The boiler step also accepts any boiler whose steam reaches the golem's tile, so a working
    layout of the player's own still counts.
- **Demolishing refunds what a building holds, not only what it cost** (from playtest: "the
  coke is lost if you demolish the boiler"). `BuildModeController.RefundFor` adds a boiler's,
  slag heap's or recycler's Coke and the recycler's Scrap to the price.
  - Contents come back whoever built the building, like a golem's cargo; only the price is
    gated on `IsRuntimePlaced`.
  - The bundle is read before teardown, because unregistering a boiler drops its stock.
- **How long Coke lasts is visible**:
  - the boiler's [E] caption reads "18 Coke · 2 golems · 1:30 left"
  - loading Coke reports how long it lasts one golem
  - the HUD gauge is labelled "Boiler fuel"
- **The guide's second chapter: the first Brass Presser** (from playtest: "what should the user
  do after automating scrap extraction?"). It follows progression-design §9: Phase 1's goal is
  the Presser, the first golem that crafts, and Phase 2's lesson is that it needs steam piped
  to it. Seven steps:
  1. Cut 10 Gears at the bench (R8).
  2. Claim Scrap Reclamation on the Assembly Line.
  3. Build the Presser (60 Scrap + 20 Iron Plate + 10 Gear).
  4. Lay two Steam Pipes on marked tiles from the boiler.
  5. Build a second depot on its marked tile.
  6. Program Haul Scrap → Assemble Scrap Reclamation → Push Output.
  7. Put it on its marked tile, between the two depots. It hauls the Scavenger's Scrap
     through the first depot (every depot opens onto the one stockpile).

  A closing step points at the Coke line and the Ledger. A step can now mark several tiles
  (the pipe run), and the Workbench placement covers both programming steps.
  `TutorialGuideTests` plays both chapters through to the Presser's first Iron Plate, and the
  `tutorial` scenario checks the pipe step's markers.
- **Guide chapter 3: the Coke line** (you asked for it). It builds a boiler that feeds itself
  beside the Coal stall. Seven steps:
  1. Claim Assemble Coking.
  2. Build a second Brass Presser.
  3. Build a second boiler on its marked tile, two north of the Coal stall.
  4. Lay a marked pipe run: from the first boiler east along the stalls' row, then up one to
     the second.
  5. Order a Coal truckload at the stall.
  6. Program Extract Scrap → Assemble Coking → Push Output.
  7. Put it between the stall and the new boiler, facing the boiler. Done when its first Coke
     lands in that firebox.

  **The pipe run is load-bearing.** The new boiler starts empty, and a Presser beside only an
  empty boiler could never make the first Coke. The pipes power it from the first boiler and
  join the two into one network.

  "Extract Scrap" takes whatever the stall behind holds, which here is Coal; the step says so.
  The name is misleading and worth fixing when the card data is hand-edited after cutover.

  `TutorialGuideTests` plays all three chapters through to Coke in the new firebox, and the
  `tutorial` scenario checks the chapter-3 pipe markers.
- **Playtest mode, part 1: questions, timings and the report** (your call: "integrate the
  playtest script into the tutorial").
  - `Core/Tutorial/PlaytestSession` asks the script's judgement questions in the guide, each at
    the step that follows what it asks about: the light, the Workbench, the manual era and the
    walking, the market, Coke upkeep, and the guide itself.
  - It times every step in real seconds and composes a Markdown report: answers with notes,
    step timings, playtest-kit uses, and the errors the game logged.
  - `Scripts/UI/PlaytestNode` shows the "your call" card in the guide plate's place, with
    answer buttons, a note field and Skip. It hooks Godot's `Logger` for errors and writes
    `user://playtest-report.md` as the session changes. **Scenario runs write
    `scenario-playtest-report.md` instead**, so a test run can't overwrite a real playtest's
    report.
  - Typing a note doesn't walk or crank: `TextEntry.IsTyping` guards the player's polled keys.
  - On in `sandbox.json` (`"playtest": true`). Covered by `PlaytestSessionTests`, and by the
    `tutorial` scenario, which answers with a typed note and checks the report on disk.
  - **The playtest kit (F9, playtest mode only)** fast-forwards the current chapter by
    PERFORMING its steps (`TutorialGuide.Kit.cs`). It grants each step's goods, places
    buildings on their marked tiles through build mode, builds and programs golems, and waits
    on the world's clock for steps that take time. The world afterwards is the one a player
    who did the chapter would have. Every use goes in the report. `PlaytestKitTests` carries
    a fresh game through all three chapters, and so does the `playtest-kit` scenario, by
    pressing F9.
  - **Guide chapter 4: copies, turning and stalls** (the script's Part C and the badge half
    of I1). Seven steps:
    1. Patent the first Scavenger's program.
    2. Build a second Scavenger.
    3. Stamp the patent onto it (Patents → Load, then ENGAGE).
    4. Build a third depot on its marked tile.
    5. Stand the golem on the Scrap stall's east side, facing EAST: the first lesson in R.
       Boiler 1 is right above it, so it needs no pipe.
    6. Turn it to face up, so it stalls with "nothing behind me".
    7. Turn it back; it resumes.

    New questions: did stamping feel better than reprogramming, and did the badge say what
    was wrong. Facing south was the first try, but it backs onto the boiler and stalls as
    "waiting on Boiler(-7,-15)", true but confusing, so the step asks for up.
  - **Guide chapter 5: metal, and its Slag** (progression Phase 4; the script's F and I3).
    Ten steps:
    1. Claim Iron Smelting.
    2. Build an Aether-Hauler.
    3. Build two depots on marked tiles: one behind the smelter, one in front.
    4. Lay three marked pipes straight off the coal line's boiler.
    5. Program Haul (Scrap ×2), Haul (Coke, set with the picker), Assemble Iron Smelting and
       Push.
    6. Set the smelter to work: it pushes Iron Plate and Slag.
    7. Build a Slag Heap on its marked tile.
    8. Build a carrier Presser.
    9. Program it Haul (Slag ×4), Haul (Coke ×1), Push: the four Slag and the one Coke the
       heap burns them with.
    10. Set it to work. Done when the heap burns its first Slag.

    New question: was Slag a real problem or a chore. The playtest kit now fast-forwards five
    chapters to a 6-golem factory.
  - **Guide chapter 6: belts, and a label** (the script's F and I2), at the Copper stall.
    Eight steps:
    1. Buy a Copper truckload and take one by hand, so the label can offer it.
    2. Drag a belt run north on the marked tiles.
    3. Drag a long pipe run up beside it and across to chapter 5's pipes.
    4. Build and program an extractor (Extract → Push).
    5. Set it to push ore onto the belt.
    6. Build a depot past the belt's end and label it Copper Ore with E.
    7. Build an unloader (Haul ‹Copper Ore› → Push).
    8. Set it between the belt's end and the depot.

    New questions: is E-cycling a decent way to label, and did dragging feel good. F9 now
    fast-forwards six chapters to an 8-golem factory.

    **Noticed in the frames, left for the playtest:** golems waiting for input wear the red
    stall badge too: a carrier with "no Slag" yet, an unloader "waiting on Belt(0,-12)#3".
    Waiting is normal in a running factory, so the badge may be too loud, and that belt label
    is an internal id.
  - **Guide chapter 7: room to grow, and keeping it** (the script's F, G, I4 and H): Extend
    the floor, upgrade the assembly bays, open a recipe in the Ledger, then Save and Load. The
    Ledger's recipe pane moved onto `SandboxWorld.LedgerReadout` so the guide can see a recipe
    was opened. A save settles the guide first (`TutorialGuide.Settle`, never `Update`, which
    runs the kit), so a load lands past the Save step. New question: did the load bring
    everything back.
  - **Claims, from playtest** ("the claims still appear random... it should be ones I am
    capable of claiming"). Two rules on the Assembly Line:
    - A card is offered only once the factory has made every good its price asks for, read
      from the same ever-growing ledger as the prerequisites, so it never flickers.
    - The card the guide is asking for is always on show (`AssemblyLineState.Wanted`, from
      `TutorialGuide.WantedCardAppendage`), taking a cycling verb's slot first. A one-off it
      displaces goes to the front of the queue, so it is not lost.
    `PlaytestKitTests.EveryClaimStep_ShowsTheCardItAsksFor_AndOnlyPayableCards` pins both; with
    the second rule off, it fails at the Iron Smelting step.
  - **Scenario runs never touch the player's save.** `PlaytestNode.UnderScenario` now reads the
    command line itself. It used to be a flag the ScenarioRunner set, after `ManagementScreen`
    had already chosen the player's real `save.json`, and a kit run overwrote one.
  - **Guide chapter 8: Brass, and the goods beyond.** Eight **goal steps**, one for each good
    the Zeppelin costs: Copper Ingot, Zinc Ingot, Brass, Casing, Glass, Lens, Mainspring and
    Aether Cell.
    - Each step names its card (a new `TutorialStep.Card`, which also replaced the guide's
      switch for the three earlier claim steps) and its recipe.
    - A step is done once the tech-tree ledger has seen the good, the same question the
      line asks of a card's price.
    - No marked tiles: by now the player has built every shape of line these need.
    - In this order every card's price is payable when its step comes.
    - The kit claims each card and grants the good, as the gears step already did.
    - New question: did you know what to build next?
  - **Guide chapter 9: the Zeppelin** (the script's Freight Link, Part G). Six steps:
    1. Claim its chassis card. Chassis cards are now matched too, through `TutorialGuide.CardKey`.
    2. Build the Zeppelin.
    3. Raise a Freight Mast.
    4. Lay one pipe for steam.
    5. Program it: Haul ‹Copper Ore›, then Freight Launch.
    6. Set it past chapter 6's labelled Copper depot, **with the depot behind it**: a golem hauls
       from behind. The first draft had it facing the depot, and the guide test caught it
       stalling on `NoSourceAtTile`.

    The guide test plays it for real: the Zeppelin flies and the ore lands in the stockpile
    through the mast. New question: did flying goods to a mast feel worth it?
  - **The guide stays on show over the Workbench for every step done there.** This is now a
    `TutorialStep.Workbench` flag, not a list in the panel. The list had stopped at chapter 3, so
    the guide stepped aside exactly when programming the smelter's two Hauls, the hardest
    Workbench step in the guide.
  - **`testscript/phase-1-playtest.md` is a how-to now**, down from 518 lines. The guide runs
    what Parts A-H walked through. The page keeps:
    - how to start, and where the report lands
    - the keys
    - a chapter map back to the old parts
    - the checks the guide never reaches: moods, the Recycler, the splitter, the workshop and
      Demolish
    - the known gaps
    - the tuning table, updated to the Godot numbers (3 Coke/min per working golem, no Focus)
  - **Playtest mode is complete** (PR #46).
- **The Clock Tower rework** (branch `conversion/g10-clock-tower`), at the user's call: the
  tower "is supposed to be the final project", but it was available from the start and drawn
  like a grandfather clock. Steps:
  1. **The town square (done).** `Core/World/TownSquare`: 10 rows by 17 cells south of the
     street, opening off its middle. The world is now a cross: workshop, street, square.
     - `IsInsideWorld`, `GetWorldCells` and `ClampToFloor` include it. The clamp's square
       rectangle overlaps the street's last row, as the road overlaps the room's front row,
       so walking across the seam never snags.
     - The street's kerb stops across the square's mouth. The square gets side walls and a
       far kerb (187 wall pieces, up from 167).
     - The tower's site is a fixed 3×3 footprint at the square's centre, with a free ring of
       cells around it to stand and deliver from.
  2. **The site (done).** The tower stands from the start on the 3×3 footprint and is
     never in the build menu (`SandboxWorld.Placeables` versus `AllPlaceables`).
     - It's a **fixture** (`PlaceableBuilding.IsFixture`, `BuildModeController.RegisterFixture`):
       it occupies all nine cells, and the wrecking bar refuses it by click or drag.
     - It stays out of `Build.Buildings`, which is the factory, read by the save, the refund and
       every count. It joins the authored buildings, as the starter bench and station do.
     - Every footprint cell is its input, so it can be fed from any side.
     - It's **roped off** (`ClockTowerSite.OpenWhen`) until the tech-tree ledger has seen a
       Zeppelin. While closed it refuses deliveries, accrues nothing, and the HUD reads
       "Clock Tower site roped off until you build a Zeppelin".
  3. **Staged art (done).** `Tools/Art/generate_clock_tower_art.py` draws six pictures:
     - `clock_tower_site` (roped off: brass posts, a sagging rope, a sign)
     - `stage0` (open: the plinth chalked out)
     - `stage1` (Foundation: a stone plinth under scaffolding)
     - `stage2` (The Movement: the brick shaft, its great cog showing)
     - `stage3` (Aether Illumination: a band of glowing lenses)
     - `stage4` (The Chronometer: belfry, clock face, slate spire)

     All six share one untrimmed 192×448 canvas with the bottom row on the footprint's south
     edge, so the stages swap in place. `verify_art.py` runs the generator.
     `ClockTowerArt.SpriteFor` picks the picture (Core, tested). The view puts the tower's node
     at the footprint's south edge, because a standing sprite's feet go on its node's origin;
     the `world` scenario caught the first draft offsetting them. The old single-cell
     `clock_tower.png` is now unused.
  4. **Save, and guide chapter 10 (done).**
     - **Save:** the tower's stage and progress ride in the save's `ProgressEntry`. A
       fixture is never in the building list, where a placed tower's progress used to go, so
       without this a load reset the endgame.
     - **Chapter 10, the Clock Tower:**
       1. Walk into the town square.
       2. Make Frame Sections (a goal step).
       3. Feed them to the tower.
       4. Lay the Foundation.

       The guide test feeds it for real, 6 a minute of freshly assembled Frame Sections
       through a footprint cell's input, until stage 1 completes and the picture changes.
       The guide says plainly that a stockpile builds nothing, which is the tower's
       fresh-production rule. The kit teleports the player through
       `PlayerInteractor.Teleport`, which the Godot player node consumes; a bare Position
       write was overwritten by the node on its next frame.
     - **Found in the kit's frames:** the instant the rope came down, the HUD raised
       "starved of FrameSection, progress frozen" over a site nobody had fed. A site is now
       starved only once it has `HasStarted`: the alarm waits for the first delivery, though
       progress still freezes.
- **One Haul card; the player picks the good** (your call). Found while planning the smelting
  chapter: the deck's only Haul was typed to Scrap. No card could load Coke, ore or plate into
  a golem, so every two-input recipe (the Aether-Hauler's reason to exist) could not be fed,
  in Unity too.
  - Each Haul slot now has a good on the program (`GolemProgram.appendageItemTypes`, parallel
    to the batch sizes and for the same reason: the card is a shared asset). It defaults to
    the card's own type.
  - The Workbench shows `Haul < good >` on a socketed Haul, offering the goods the factory has
    made plus Scrap. ENGAGE commits the pick.
  - Patents keep each slot's good and batch size, and the save carries both for golems and
    patents. An older save keeps the cards' defaults.
  - `HaulItemTypeTests` covers it, including an Aether-Hauler smelting Iron Plate from Scrap
    and Coke hauled from one depot. The `workbench` scenario clicks the picker and checks
    ENGAGE commits it.
- **Newly unlocked Assembly Line cards jump the queue**, found writing that chapter. A card
  unlocks when the factory first makes what it needs, and it used to join the *back* of the
  queue, in reverse deck order. So R2 Scrap Reclamation, the first recipe a Presser runs, came
  up only after claiming through about six cards, two of them unaffordable that early.
  Promoted cards now go to the front, in deck order.

  That wasn't enough, as the playtest showed ("it says to claim scrap reclamation, but I don't
  see it"): the slots held only cycling verbs, Repeat Assembly twice, and a slot refills only
  on a claim. So **a slot holding a cycling verb now gives way to any unowned one-off card
  waiting in the queue**, checked on promotion and every tick, and refills prefer one-off
  cards. The verbs matter only to the Overclocker and Zeppelin, and they come back once
  nothing else is waiting. R2 is on show the moment Scrap exists.

  Tests: `AnUnlockedCard_TakesAVerbsSlot_AtOnce_InDeckOrder`,
  `ALineAlreadyFullOfVerbs_ShowsAWaitingOneOff_OnTheNextTick`, and the guide test, which now
  claims nothing but R2. This changes Unity's gating behaviour; yours to overrule.
- **Coke burns only for WORKING golems, at half the rate** (your call, from playtest: "it seems
  to burn very fast"). It was 6 Coke/min for every golem in a boiler's reach, working or not;
  it's now 3 Coke/min per golem actually running a step.
  - Idle, unprogrammed and stalled golems stay powered, so they start the moment they have
    work, but cost nothing.
  - A golem reports work each tick (`SteamNetwork.ReportWorking`), and a boiler pays only for
    its powered golems that reported.
  - `TicksPerCokePerPoweredGolem` is 200, and the gauge and captions derive 3/min from it.
  - The ported burn tests now run their golems as working and assert the new numbers. The
    hand-load bound moves from 30 s to 60 s, and `progression-design.md` §3.1 records the
    change.
  - Covered by `WorkingGolemBurnTests` through the real Sandbox, and by
    `APoweredGolemThatIsNotWorking_BurnsNothing_AndStaysPowered`.
- **The alerts strip lost the steam cause between reconciles.** Stall events carry no cause
  and overwrote the snapshot, so it is now read from the golem when the strip composes its
  text. Caught in the frames.
 (every milestone)

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
