# Full conversion: golem-factory from Unity to Godot

## Status

Updated by each milestone's PR. The first row that isn't **done** is the current milestone.

| Milestone | Status |
|---|---|
| G0: Land the spike | **done**: PR #28, merged 2026-10-04 |
| G1: Authored data as JSON | in review: PR #29 |
| G2: Gameplay services into Core | **built**, in review as stacked sub-PRs: G2a station + assembly bay, G2b build mode, G2c interactions, G2d save + the rest |
| G3: Art and fonts pipeline | in review |
| G4: The world scene | in review |
| G5: Buildings and build mode | in review; **waiting on your hands-on check** |
| G5b: The player's hands (pulled forward from G6) | in review |
| G6–G10 | not started |

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

### G6: Golems in full

(G5b already did the `[G]` carry, the interactor and the construction panel. What remains is
the golem's own presentation and the assembly bay.)
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
