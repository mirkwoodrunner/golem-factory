using System;
using System.Collections.Generic;
using GolemFactory.Belts;
using GolemFactory.Buildings;
using GolemFactory.ClockTower;
using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.Steam;
using GolemFactory.World;

namespace GolemFactory.Player
{
    /// <summary>What a build-mode popup is saying, which decides its colour in the scene.</summary>
    public enum BuildPopupKind
    {
        /// <summary>A refusal: off the ground, unaffordable, nothing to remove, no room.</summary>
        Refused,

        /// <summary>Goods spent on a placement ("-15 Scrap").</summary>
        Spent,

        /// <summary>Goods coming back from a demolition or dismantle ("+15 Scrap").</summary>
        Refund,
    }

    /// <summary>A short message at a cell, for the scene to float above it.</summary>
    public readonly struct BuildPopup
    {
        public readonly Vector2Int Cell;
        public readonly string Text;
        public readonly BuildPopupKind Kind;

        public BuildPopup(Vector2Int cell, string text, BuildPopupKind kind)
        {
            Cell = cell;
            Text = text;
            Kind = kind;
        }
    }

    /// <summary>
    /// Click-to-place, click-to-remove, and click-and-drag runs against <see cref="GridMap"/>.
    ///
    /// <para>
    /// PORTED FROM Unity's MonoBehaviour of the same name (milestone G2b). Every rule is the
    /// Unity one, with its reasoning kept beside it. What changed is the engine glue:
    /// </para>
    /// <list type="bullet">
    ///   <item>Input became verbs -- <see cref="Click"/>, <see cref="Hover"/>,
    ///   <see cref="Release"/>, <see cref="RotateKey"/>, <see cref="CancelPlacement"/> -- so a
    ///   Godot node forwards events and decides nothing.</item>
    ///   <item>The ghost sprite became <see cref="GhostStateFor"/>, the rule it was coloured by.</item>
    ///   <item>Instantiate / Destroy / GetComponent became <see cref="PlaceableBuilding.Instantiate"/>,
    ///   <see cref="PlaceableBuilding.MarkRemoved"/> and <see cref="PlaceableBuilding.GetPart{T}"/>;
    ///   the scene learns of both through <see cref="BuildingPlaced"/> and
    ///   <see cref="BuildingRemoved"/>.</item>
    ///   <item>FloatingPopup.Spawn became the <see cref="PopupRaised"/> event.</item>
    ///   <item>FindObjectsByType became <see cref="Buildings"/> (every building this mode knows
    ///   of) and a golem roster the host supplies (<see cref="ConfigureGolemRoster"/>).</item>
    ///   <item>The static belt/pipe rosters behind the shape refresh became that same list.</item>
    /// </list>
    /// </summary>
    public sealed class BuildModeController
    {
        private GridMap _gridMap;
        private PlaceableBuilding _buildingPrefab;

        // Left null in a scene that never wires an economy -- placement there stays free.
        private StorageBufferRegistry _stockpile;
        private string _stockpileBufferId = "FactoryStockpile";
        private PlaceableBuilding[] _availablePrefabs;

        // Belt placement. Optional: with no network wired, a belt prefab still places as a
        // plain building (it just never registers a lane).
        private BeltNetwork _beltNetwork;
        private SpatialEndpointRegistry _spatialEndpoints;
        private ConveyorSystem _conveyor;

        // Steam power (docs/progression-design.md §3.1). Optional in exactly the same additive
        // way belts are: with no network wired, a Boiler or Steam Pipe still places as a plain
        // building -- it just never publishes itself into the steam grid.
        private SteamNetwork _steamNetwork;

        // §6's Freight Mast. Optional in the same additive way steam is.
        private FreightMastRegistry _mastRegistry;

        // A placed GolemConstructionStation needs SCENE services, so the scene hands over
        // something that can wire one, and this asks it as each station is built. Unwired, a
        // station places as a plain building.
        private IPlacedStationConfigurator _stationConfigurator;

        // The scene's Clock Tower site, for a tower placed without one of its own.
        private ClockTowerSite _clockTowerSite;

        // The scene's golem dismantler. Optional: unwired, the wrecking bar refuses a golem's
        // tile with "not a building", as it did before golems were removable.
        private IGolemDismantler _golemDismantler;

        // Every golem standing in the world, for TryFindGolemAt. Unity scanned the scene with
        // FindObjectsByType; Core has no scene, so whoever owns the golems supplies them.
        private Func<IEnumerable<GolemEntity>> _golemRoster;

        private readonly List<PlaceableBuilding> _buildings = new List<PlaceableBuilding>();
        private Vector2Int _hoveredCell;

        /// <summary>Raised for every popup Unity spawned; the scene floats the text over the cell.</summary>
        public event Action<BuildPopup> PopupRaised;

        /// <summary>Raised once a building is fully placed and registered.</summary>
        public event Action<PlaceableBuilding> BuildingPlaced;

        /// <summary>Raised once a building has been unregistered and removed.</summary>
        public event Action<PlaceableBuilding> BuildingRemoved;

        /// <summary>
        /// Raised after any change that can alter a belt's or pipe's picture -- the scene
        /// redraws <see cref="PlaceableBelt.Shape"/> and <see cref="PlaceableSteamPipe.Shape"/>.
        /// </summary>
        public event Action ConnectedShapesChanged;

        /// <summary>
        /// Every building this mode knows of: the ones it placed or rebuilt, plus any the scene
        /// registered with <see cref="RegisterExistingBuilding"/>. Removed buildings leave it.
        /// </summary>
        public IReadOnlyList<PlaceableBuilding> Buildings => _buildings;

        /// <summary>
        /// Direction the next placed building/belt will face. Cycled with R. Held here rather
        /// than on the prefab because it must persist between placements -- laying a run of
        /// belts means orienting once and clicking several times.
        /// </summary>
        public Facing PlacementFacing { get; private set; } = Facing.North;

        /// <summary>
        /// Whether the player is currently holding a placeable. Used to arbitrate the shared R
        /// key: in build mode R turns the ghost, otherwise the interactor uses it to turn the
        /// nearest golem. One binding, two meanings, decided by whether a tool is in hand.
        /// </summary>
        public bool IsPlacementActive => _buildingPrefab != null;

        /// <summary>
        /// Whether the wrecking bar is in hand: clicks remove buildings and place nothing.
        ///
        /// <para>
        /// <b>Why this had to exist.</b> Removal was only ever reachable by clicking an occupied
        /// cell <em>while holding a placeable</em> -- every click is gated on
        /// <c>BuildClickPolicy.ShouldPlace</c>. That was invisible while there was no way out of
        /// build mode, because a placeable was then always in hand. The moment a way out
        /// shipped, "click a depot to take it back" quietly stopped working for anyone who had
        /// put their tool down -- found in playtest.
        /// </para>
        ///
        /// <para>
        /// A mode rather than a modifier key, and rather than simply letting any click demolish:
        /// demolition is destructive and the cursor is already overloaded (harvest, interact,
        /// place). It sits in the build menu beside the things it undoes.
        /// </para>
        /// </summary>
        public bool IsDemolishActive { get; private set; }

        /// <summary>
        /// Whether a click belongs to build mode at all -- either tool counts. Distinct from
        /// <see cref="IsPlacementActive"/>, which arbitrates R: there is nothing to rotate while
        /// demolishing, so R must still reach a bench or a golem.
        /// </summary>
        public bool IsBuildToolActive => IsPlacementActive || IsDemolishActive;

        // Set by PlaceOrRemove on a failed cost check (and the other refusals), for the build
        // menu (or a test) to surface.
        public string LastStatusMessage { get; private set; } = "";

        public PlaceableBuilding ActivePrefab => _buildingPrefab;
        public IReadOnlyList<PlaceableBuilding> AvailablePrefabs => _availablePrefabs;

        /// <summary>The cell the pointer is over, as of the last <see cref="Hover"/>.</summary>
        public Vector2Int HoveredCell => _hoveredCell;

        // --- Wiring --------------------------------------------------------------------------

        // Programmatic setup used by tests and the scene.
        public void Configure(GridMap gridMap, PlaceableBuilding buildingPrefab)
        {
            _gridMap = gridMap;
            _buildingPrefab = buildingPrefab;
        }

        // Wires the economy separately from Configure so a scene that never calls this keeps
        // placement free.
        public void ConfigureEconomy(StorageBufferRegistry stockpile, string stockpileBufferId, PlaceableBuilding[] availablePrefabs)
        {
            _stockpile = stockpile;
            _stockpileBufferId = stockpileBufferId;
            _availablePrefabs = availablePrefabs;
        }

        /// <summary>
        /// Wires belt placement. Separate again for the same reason ConfigureEconomy is: a scene
        /// with no belts never calls it and nothing changes there.
        /// </summary>
        public void ConfigureBelts(BeltNetwork beltNetwork, SpatialEndpointRegistry spatialEndpoints, ConveyorSystem conveyor)
        {
            _beltNetwork = beltNetwork;
            _spatialEndpoints = spatialEndpoints;
            _conveyor = conveyor;
        }

        /// <summary>Wires steam placement. Separate again, for the same reason ConfigureBelts is.</summary>
        public void ConfigureSteam(SteamNetwork steamNetwork) => _steamNetwork = steamNetwork;

        public void ConfigureFreight(FreightMastRegistry mastRegistry) => _mastRegistry = mastRegistry;

        public void ConfigureStationWiring(IPlacedStationConfigurator configurator) =>
            _stationConfigurator = configurator;

        public void ConfigureClockTower(ClockTowerSite site) => _clockTowerSite = site;

        /// <summary>
        /// Late-wired by the scene, for the same reason <see cref="ConfigureStationWiring"/> is.
        /// </summary>
        public void ConfigureGolemDismantling(IGolemDismantler dismantler) => _golemDismantler = dismantler;

        /// <summary>The golems standing in the world -- what Unity found with FindObjectsByType.</summary>
        public void ConfigureGolemRoster(Func<IEnumerable<GolemEntity>> golems) => _golemRoster = golems;

        /// <summary>
        /// Puts a building the scene authored onto the grid and into <see cref="Buildings"/>,
        /// without charging or marking it runtime-placed -- scene furniture, which the refund
        /// and the save system both leave alone. Its parts are NOT registered: an authored
        /// building is wired by whoever authored it.
        /// </summary>
        public void RegisterExistingBuilding(PlaceableBuilding building, Vector2Int cell)
        {
            if (building == null || _gridMap == null)
            {
                return;
            }

            building.Cell = cell;
            if (_gridMap.TryOccupy(cell, building))
            {
                _buildings.Add(building);
            }
        }

        // --- The buildable area (docs/progression-design.md §3.3) -------------------------
        // -1 means UNBOUNDED, following ResourceNode.Infinite and StorageBuffer.Unlimited's
        // sentinel idiom, and it is the default: a rig that never calls ConfigurePlacementBounds
        // places exactly where it always could.
        private int _placementHalfExtent = -1;
        private int _placementStreetDepth;

        /// <summary>
        /// Bounds placement to the ground that is actually drawn.
        ///
        /// <para>
        /// The bound is the WORLD (workshop + street), not the workshop: the five traders stand
        /// out on the street, so bounding to the room would forbid the belts and depots that
        /// reach them. See <see cref="FloorLayout.IsInsideWorld(Vector2Int, int, int)"/>.
        /// </para>
        /// </summary>
        public void ConfigurePlacementBounds(int halfExtent, int streetDepth)
        {
            _placementHalfExtent = halfExtent;
            _placementStreetDepth = streetDepth;
        }

        // The live room, for Floor Expansion (§11 item 15). Held rather than copied: new floor
        // has to be buildable the moment it is paid for, without a second call to re-bound.
        private FloorBounds _placementBounds;

        public void ConfigurePlacementBounds(FloorBounds bounds, int streetDepth)
        {
            _placementBounds = bounds;
            if (bounds != null)
            {
                _placementHalfExtent = bounds.HalfExtent;
                _placementStreetDepth = streetDepth;
            }
        }

        /// <summary>
        /// Whether a building may stand on <paramref name="cell"/> at all. Public so the ghost,
        /// the click and a test all read one answer -- a ghost that promises a tile placement
        /// then refuses is the bug this replaced, not an improvement on it.
        /// </summary>
        public bool IsCellBuildable(Vector2Int cell)
        {
            if (_placementHalfExtent < 0)
            {
                return true;
            }

            if (_placementBounds != null)
            {
                return FloorLayout.IsInsideWorld(
                    cell, _placementBounds.HalfExtent, _placementStreetDepth,
                    FloorLayout.StreetHalfExtent, _placementBounds.NorthExtent);
            }

            return FloorLayout.IsInsideWorld(cell, _placementHalfExtent, _placementStreetDepth);
        }

        // --- The two tools ---------------------------------------------------------------------

        /// <summary>
        /// Picks up the wrecking bar, putting down any placeable first -- the two are one
        /// cursor and cannot both be held.
        /// </summary>
        public void EnterDemolishMode()
        {
            _buildingPrefab = null;
            IsDemolishActive = true;
            LastStatusMessage = "";
            EndDrag();
        }

        /// <summary>
        /// Called by the build menu when the player picks a placeable type. Takes the wrecking
        /// bar out of the player's hand, because <b>the two are one cursor and cannot both be
        /// held</b> -- the exact mirror of what <see cref="EnterDemolishMode"/> does to a
        /// placeable.
        ///
        /// <para>
        /// <b>That mirror was missing once, and it did far more than leave a row highlighted.</b>
        /// Every mode question here is asked as <c>IsDemolishActive</c> FIRST, so a player who
        /// picked Demolish and then Depot got: no placement at all, an inverted ghost, no facing
        /// arrow -- so R went silently invisible -- and any drag turned into a demolition sweep.
        /// Fixed HERE rather than in the menu that reported it: the invariant belongs to the
        /// state, or the next caller reintroduces it.
        /// </para>
        /// </summary>
        public void SetActivePrefab(PlaceableBuilding prefab)
        {
            _buildingPrefab = prefab;
            IsDemolishActive = false;
            LastStatusMessage = "";
            // A run in progress belonged to the tool being put down, exactly as it does when the
            // bar is picked up.
            EndDrag();
        }

        /// <summary>
        /// Puts the held placeable (or the wrecking bar) down -- leaves build mode. The build
        /// menu, a test and the key binding all take this path.
        ///
        /// <para>
        /// <b>Why this had to exist.</b> There was once no way out of build mode at all, so once
        /// a row was clicked <see cref="IsPlacementActive"/> stayed true for the rest of the
        /// session, and -- because R is arbitrated on exactly that flag -- <b>R could never again
        /// reach the Hand-Crank Bench or a golem</b>.
        /// </para>
        /// </summary>
        public bool CancelPlacement()
        {
            if (_buildingPrefab == null && !IsDemolishActive)
            {
                return false;
            }

            _buildingPrefab = null;
            EndDrag();
            // The wrecking bar leaves by the same doors the placeables do.
            IsDemolishActive = false;
            LastStatusMessage = "";
            return true;
        }

        /// <summary>Turns the ghost one step clockwise.</summary>
        public void RotatePlacement() => PlacementFacing = FacingUtility.RotateClockwise(PlacementFacing);

        /// <summary>
        /// The R key. Turns the ghost only when a placeable is actually in hand; otherwise R
        /// belongs to the interactor, which turns the nearest golem. Returns whether build mode
        /// took the key.
        /// </summary>
        public bool RotateKey()
        {
            if (!IsPlacementActive)
            {
                return false;
            }
            RotatePlacement();
            return true;
        }

        // --- Pointer -----------------------------------------------------------------------------

        /// <summary>
        /// The pointer moved over <paramref name="cell"/>. Carries a live drag there -- what
        /// Unity's Update did with the hovered cell.
        /// </summary>
        public void Hover(Vector2Int cell)
        {
            _hoveredCell = cell;
            ExtendDrag(cell);
        }

        /// <summary>
        /// A click at <paramref name="cell"/>. A click over UI belongs to that UI, not to the
        /// world underneath it: without this, closing a panel also tried to build on whatever
        /// tile the panel was covering, and *selecting* a placeable in the menu immediately
        /// tried to place one.
        /// </summary>
        public void Click(Vector2Int cell, bool pointerOverUi)
        {
            _hoveredCell = cell;
            if (!BuildClickPolicy.ShouldPlace(IsBuildToolActive, pointerOverUi))
            {
                return;
            }

            PlaceOrRemove(cell);
            BeginDrag(cell);
        }

        /// <summary>
        /// The button came up: the run ends. The host must also call this if a drag ever
        /// outlives its button -- a drag that kept laying belts wherever the cursor went is the
        /// one failure mode of this feature a player could not undo in a single gesture.
        /// </summary>
        public void Release() => EndDrag();

        /// <summary>
        /// What the ghost should show over <paramref name="cell"/> -- the rule the Unity ghost
        /// was coloured by, so the ghost and the click can never disagree.
        /// </summary>
        public BuildGhostState GhostStateFor(Vector2Int cell)
        {
            if (IsDemolishActive)
            {
                // The wrecking bar inverts the ghost: an OCCUPIED tile is the good one.
                return BuildGhostVisuals.ClassifyRemoval(HasRemovableThing(cell));
            }

            // Off the ground reads as Blocked rather than as a fourth state: the player's move
            // is the same one an occupied tile asks for -- put the cursor somewhere else.
            bool occupied = _gridMap != null && _gridMap.IsOccupied(cell);
            bool refused = occupied || !IsCellBuildable(cell);
            return BuildGhostVisuals.Classify(refused, CanAffordActivePrefab());
        }

        // --- What is on a cell ----------------------------------------------------------------------

        /// <summary>
        /// Whether this cell holds something the wrecking bar can actually take. A golem may
        /// share the tile and is not a building, so "occupied" is the wrong question.
        /// </summary>
        public bool HasRemovableBuilding(Vector2Int cell) =>
            _gridMap != null
            && _gridMap.TryGetOccupant(cell, out object occupant)
            && occupant is PlaceableBuilding building
            && !building.IsFixture;

        /// <summary>
        /// The golem standing on <paramref name="cell"/>, if any.
        ///
        /// <para>
        /// <b>Golems are NOT <c>GridMap</c> occupants</b> -- only buildings are (the TryOccupy
        /// calls in this file are the only ones in the project). A golem knows its own cell
        /// instead, so finding one is a scan rather than a lookup. Affordable because this runs
        /// on a click and on the hovered cell, never per golem per frame.
        /// </para>
        /// </summary>
        public bool TryFindGolemAt(Vector2Int cell, out GolemEntity golem)
        {
            golem = null;
            if (_golemRoster == null)
            {
                return false;
            }

            foreach (GolemEntity candidate in _golemRoster())
            {
                if (candidate != null && !candidate.IsRemoved && candidate.Cell == cell)
                {
                    golem = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>What a refusal calls a fixture: its own display name, else the building's.</summary>
        private static string FixtureName(PlaceableBuilding building) =>
            building.GetPart<PlaceableClockTower>()?.DisplayName ?? building.name;

        /// <summary>
        /// Puts a fixture on the world across a whole footprint (G10: the Clock Tower is three
        /// cells square). Every cell is occupied by it, so nothing can be built over the
        /// footprint, and a click on any of them finds it -- and is refused.
        /// </summary>
        public void RegisterFixture(PlaceableBuilding building, Vector2Int cell, IEnumerable<Vector2Int> footprint)
        {
            if (building == null || _gridMap == null)
            {
                return;
            }

            // NOT in Buildings: that list is the factory, which the save, the refund and every
            // count of "what the player built" read. A fixture is the town's.
            building.IsFixture = true;
            building.Cell = cell;
            _gridMap.TryOccupy(cell, building);
            foreach (Vector2Int other in footprint)
            {
                if (other != cell)
                {
                    _gridMap.TryOccupy(other, building);
                }
            }
        }

        /// <summary>Whether the wrecking bar has something to take back at this cell.</summary>
        public bool HasRemovableThing(Vector2Int cell) =>
            HasRemovableBuilding(cell) || (_golemDismantler != null && TryFindGolemAt(cell, out _));

        /// <summary>
        /// Whether the active prefab's cost is currently payable. With no stockpile wired
        /// placement is free, so this is always true there -- exactly matching PlaceOrRemove's
        /// own cost check, so the ghost can never promise something placement will then refuse.
        /// </summary>
        public bool CanAffordActivePrefab()
        {
            if (_buildingPrefab == null || _stockpile == null)
            {
                return true;
            }

            return UI.ConstructionCostPolicy.CanAfford(ReadStock, _buildingPrefab.Cost);
        }

        private int ReadStock(string itemType)
        {
            if (_stockpile == null || !_stockpile.TryGetBuffer(_stockpileBufferId, out StorageBuffer buffer))
            {
                return 0;
            }

            return buffer.GetQuantity(itemType);
        }

        private void Popup(Vector2Int cell, string text, BuildPopupKind kind)
        {
            if (!string.IsNullOrEmpty(text))
            {
                PopupRaised?.Invoke(new BuildPopup(cell, text, kind));
            }
        }

        // ===================================================================================
        // Click-and-drag: laying a RUN.
        // ===================================================================================
        //
        // Belts and steam pipes are the two things a player lays fifteen of in a row. A drag
        // lays the run and POINTS IT ALONG ITSELF, which is the half that matters: the facing of
        // every belt but the last is only knowable once the drag has reached the next cell, so
        // each cell is laid facing the cursor and then turned as the run goes on (Reface /
        // BeltNetwork.TrySetFacing, neither of which disturbs the lane or its cargo).
        //
        // THE WRECKING BAR DRAGS TOO, and its run is deliberately not the mirror image of a
        // placement run: it never stops, and it never takes a golem. See DemolishDragged.
        private bool _dragActive;

        /// <summary>Whether this run is the wrecking bar's rather than a placeable's.</summary>
        private bool _dragDemolishing;

        private Vector2Int _dragLastCell;
        private readonly List<Vector2Int> _dragStepScratch = new List<Vector2Int>();

        // Cells this drag laid. Purely so the player can wiggle back over their own run without
        // it counting as "blocked": every OTHER occupied cell ends the drag, because a run that
        // silently skipped a wall would leave the belt before the gap pointing into it.
        private readonly HashSet<Vector2Int> _dragPlacedCells = new HashSet<Vector2Int>();

        /// <summary>Whether a run is being laid right now.</summary>
        public bool IsDragging => _dragActive;

        /// <summary>Starts a run at the cell the click just landed on.</summary>
        public void BeginDrag(Vector2Int anchor)
        {
            if (IsDemolishActive)
            {
                // The wrecking bar drags unconditionally -- the tool IS the flag. Safe to make
                // destructive-by-the-gesture for the same reason removal is a full refund: a
                // swept building comes back for nothing.
                _dragActive = true;
                _dragDemolishing = true;
                _dragLastCell = anchor;
                _dragPlacedCells.Clear();
                return;
            }

            if (_buildingPrefab == null || !_buildingPrefab.IsDragPlaceable)
            {
                return;
            }

            _dragActive = true;
            _dragDemolishing = false;
            _dragLastCell = anchor;
            _dragPlacedCells.Clear();
            // The anchor counts as ours whether or not the click actually placed anything: if it
            // did, the run may need to turn it; if it did not, the very first step will find it
            // occupied or refused and end the drag there.
            _dragPlacedCells.Add(anchor);
        }

        public void EndDrag()
        {
            _dragActive = false;
            _dragDemolishing = false;
            _dragPlacedCells.Clear();
        }

        /// <summary>Carries a live drag up to <paramref name="cell"/>, laying every cell on the way.</summary>
        public void ExtendDrag(Vector2Int cell)
        {
            if (!_dragActive || cell == _dragLastCell || _gridMap == null)
            {
                return;
            }

            _dragStepScratch.Clear();
            BuildDragPath.AppendCells(_dragLastCell, cell, _dragStepScratch);

            for (int i = 0; i < _dragStepScratch.Count; i++)
            {
                Vector2Int next = _dragStepScratch[i];
                if (_dragDemolishing)
                {
                    DemolishDragged(next);
                    _dragLastCell = next;
                    continue;
                }

                Facing stepFacing = BuildDragPath.StepFacing(_dragLastCell, next, PlacementFacing);

                if (_dragPlacedCells.Contains(next))
                {
                    // Wiggled back over our own run. Re-anchor and carry on, laying nothing --
                    // and pointedly NOT re-facing the cell we came from, which would turn the
                    // run backwards into itself. The next forward step re-faces it correctly.
                    _dragLastCell = next;
                    continue;
                }

                if (!TryPlaceDragged(next, stepFacing))
                {
                    // Blocked, off the ground, or unaffordable. The run stops where it stopped
                    // rather than skipping the obstruction, because a run with a hole in it
                    // leaves the belt before the hole pointing at nothing.
                    EndDrag();
                    return;
                }

                // Only now does the cell we came from know which way the run leaves it -- after
                // the next cell is really there, so a refusal never leaves a belt aimed at a wall.
                RefaceDragged(_dragLastCell, stepFacing);

                _dragPlacedCells.Add(next);
                _dragLastCell = next;

                // The ghost keeps the direction the run is going, so letting go and clicking
                // again continues the line rather than restarting it at whatever R last chose.
                PlacementFacing = stepFacing;
            }
        }

        /// <summary>
        /// One cell of a wrecking-bar run: takes back the building standing here, if any.
        ///
        /// <para>
        /// <b>It does NOT stop at an empty cell, and that is the deliberate asymmetry with the
        /// placement run.</b> A placed run has to be CONTINUOUS to mean anything; a demolition
        /// has no such requirement. Sweeping the bar across a corner of the factory is meant to
        /// clear what is there and pass over what is not.
        /// </para>
        ///
        /// <para>
        /// <b>It does not take GOLEMS, and a click still does.</b> The full refund does not make
        /// a golem whole: a demolished building re-placed is identical, while a dismantled golem
        /// hands back its chassis and cargo and loses <em>the program</em>. Sweeping up a
        /// building is an undo away; sweeping up a golem is not, so a golem still costs one
        /// deliberate click.
        /// </para>
        ///
        /// <para>
        /// Every other rule is the click's, unchanged, because this calls the same
        /// <see cref="DemolishBuilding"/>: full refund, runtime-placed only, and refused outright
        /// if the stockpile has no room. A refusal leaves that one building standing and the
        /// sweep carries on.
        /// </para>
        /// </summary>
        private void DemolishDragged(Vector2Int cell)
        {
            if (_gridMap.TryGetOccupant(cell, out object occupant) && occupant is PlaceableBuilding building)
            {
                DemolishBuilding(building, cell, refund: true);
            }
        }

        // One cell of a run. Deliberately NOT PlaceOrRemove: that method is the player's CLICK,
        // and a click on an occupied cell demolishes what is there. Dragging a belt run across
        // your own depot must never eat the depot.
        private bool TryPlaceDragged(Vector2Int cell, Facing facing)
        {
            if (_gridMap.IsOccupied(cell) || !IsCellBuildable(cell) || _buildingPrefab == null)
            {
                return false;
            }

            Facing previous = PlacementFacing;
            PlacementFacing = facing;
            PlaceInternal(cell, _gridMap);
            PlacementFacing = previous;

            // PlaceInternal reports a refusal by leaving the cell unoccupied (it withdraws the
            // cost atomically and returns early on a shortfall), so occupancy is the honest
            // answer to "did that work".
            return _gridMap.IsOccupied(cell);
        }

        // Turns a cell the current drag already laid, so the run points along itself.
        private void RefaceDragged(Vector2Int cell, Facing facing)
        {
            if (!_dragPlacedCells.Contains(cell) || _gridMap == null
                || !_gridMap.TryGetOccupant(cell, out object occupant))
            {
                return;
            }

            var building = occupant as PlaceableBuilding;
            if (building == null || building.Facing == facing)
            {
                return;
            }

            building.Facing = facing;

            // A belt's facing IS its routing, so the lane graph has to be told before the
            // picture is. Both are no-ops for a pipe, whose Facing is read only as the
            // orientation of an ISOLATED stub.
            PlaceableBelt belt = building.GetPart<PlaceableBelt>();
            if (belt != null && _beltNetwork != null)
            {
                _beltNetwork.TrySetFacing(cell, facing);
                belt.Reface(facing);
            }

            RefreshConnectedShapes();
        }

        /// <summary>
        /// Re-derives every belt and pipe picture from the networks that own them. Called
        /// wherever the built world changes. Recomputed wholesale rather than patched per cell,
        /// for the reason BeltNetwork.Relink is.
        /// </summary>
        private void RefreshConnectedShapes()
        {
            for (int i = 0; i < _buildings.Count; i++)
            {
                PlaceableBuilding building = _buildings[i];
                if (_steamNetwork != null)
                {
                    building.GetPart<PlaceableSteamPipe>()?.RefreshShape(_steamNetwork, building.Facing);
                }
                if (_beltNetwork != null)
                {
                    building.GetPart<PlaceableBelt>()?.RefreshShape(_beltNetwork, building.Cell);
                }
            }

            ConnectedShapesChanged?.Invoke();
        }

        // ===================================================================================
        // The click.
        // ===================================================================================

        public void PlaceOrRemove(Vector2Int cell)
        {
            if (_gridMap == null)
            {
                return;
            }

            GridMap map = _gridMap;
            if (map.IsOccupied(cell))
            {
                if (map.TryGetOccupant(cell, out object occupant) && occupant is PlaceableBuilding building)
                {
                    // The player's own click at a cell: a sale being reversed, so it pays back.
                    DemolishBuilding(building, cell, refund: true);
                }
                else if (IsDemolishActive && !TryDismantleGolemAt(cell))
                {
                    // Not a building, and not a golem either. Says so rather than doing nothing,
                    // because a silent click on the wrecking bar reads as the tool being broken.
                    LastStatusMessage = "Nothing to remove there -- that tile is not a building.";
                    Popup(cell, "not a building", BuildPopupKind.Refused);
                }

                return;
            }

            // A golem standing on an UNoccupied tile, which is the normal case: golems are not
            // GridMap occupants. Checked before the empty-tile refusal below, or the wrecking
            // bar would say "nothing here" while the player is looking straight at a golem.
            if (IsDemolishActive && TryDismantleGolemAt(cell))
            {
                return;
            }

            if (IsDemolishActive)
            {
                Popup(cell, "nothing here", BuildPopupKind.Refused);
                return;
            }

            if (_buildingPrefab == null)
            {
                return;
            }

            // REMOVAL is checked above and is deliberately NOT bounded: a building standing off
            // the ground (an old save, a bound that moved) must always be removable, or it is
            // litter the player cannot clear.
            if (!IsCellBuildable(cell))
            {
                LastStatusMessage = "Can't build there -- that is outside the workshop and the street.";
                Popup(cell, "off the ground", BuildPopupKind.Refused);
                return;
            }

            PlaceInternal(cell, map);
        }

        /// <summary>
        /// Rebuilds a building a save file describes, at the cell and facing it was saved with.
        ///
        /// <para>
        /// DOES NOT CHARGE THE COST, for the same reason
        /// <c>GolemConstructionStation.TryRespawnGolem</c> does not: the player paid for this
        /// building in the session that placed it, and charging on load would be a tax that
        /// simply fails for anyone who has since spent their stockpile.
        /// </para>
        ///
        /// <para>
        /// Everything else goes through the same occupancy and endpoint registration a placement
        /// does, because a restored belt that never reached BeltNetwork would be a lane the
        /// player can see and items cannot use.
        /// </para>
        /// </summary>
        public bool TryRebuildSavedBuilding(
            string prefabKey, Vector2Int cell, Facing facing, out PlaceableBuilding instance)
        {
            instance = null;
            PlaceableBuilding prefab = FindPrefab(prefabKey);
            if (prefab == null || _gridMap == null)
            {
                return false;
            }

            if (_gridMap.IsOccupied(cell))
            {
                return false;
            }

            instance = prefab.Instantiate();
            instance.Cell = cell;
            instance.Facing = facing;
            instance.MarkRuntimePlaced(prefabKey);
            _gridMap.TryOccupy(cell, instance);
            _buildings.Add(instance);
            RegisterPlacedEndpoints(instance, cell, facing);
            BuildingPlaced?.Invoke(instance);

            // Its own publish: a save restore builds the instance here rather than through
            // PlaceInternal, so it would otherwise be the one path that left a loaded factory
            // full of buildings the player could not interact with.
            EventBus.Publish(new WorldInteractablesChangedEvent("building restored"));
            return true;
        }

        /// <summary>
        /// Removes every building the player placed, leaving the ones authored into the scene
        /// alone. The counterpart of <c>StorageBufferRegistry.Clear()</c>: a load must REPLACE
        /// the built world, not merge into it, or loading twice stacks two factories on one
        /// floor.
        /// </summary>
        public int ClearRuntimePlacedBuildings()
        {
            var placed = new List<PlaceableBuilding>();
            foreach (PlaceableBuilding building in _buildings)
            {
                if (building != null && building.IsRuntimePlaced)
                {
                    placed.Add(building);
                }
            }

            foreach (PlaceableBuilding building in placed)
            {
                // refund: false, and this is NOT a detail. A load REPLACES the built world, and
                // it sweeps exactly the buildings a refund pays out on -- the runtime-placed
                // ones. Paying them back here would hand the player their whole factory's cost
                // in goods on every single load, on top of the buffers the save is about to
                // restore: save, load, save, load is an infinite resource duplicator. Only a
                // player's own click at a cell is a sale being reversed.
                DemolishBuilding(building, building.Cell, refund: false);
            }

            return placed.Count;
        }

        private PlaceableBuilding FindPrefab(string prefabKey)
        {
            if (string.IsNullOrEmpty(prefabKey) || _availablePrefabs == null)
            {
                return null;
            }

            foreach (PlaceableBuilding prefab in _availablePrefabs)
            {
                if (prefab != null && prefab.name == prefabKey)
                {
                    return prefab;
                }
            }

            return null;
        }

        /// <summary>
        /// The wrecking bar's golem branch. Returns whether this click was <em>about</em> a golem
        /// at all -- true even when the dismantle was refused, because a refusal that reported
        /// "that tile is not a building" over a visible golem would be a lie.
        ///
        /// <para>
        /// Every rule here lives on the other side of <c>IGolemDismantler</c>: this method owns
        /// the status line and the popups, and nothing else. That split is why the refund cannot
        /// diverge between a golem removed with the wrecking bar and one removed by anything
        /// added later.
        /// </para>
        /// </summary>
        private bool TryDismantleGolemAt(Vector2Int cell)
        {
            if (_golemDismantler == null || !TryFindGolemAt(cell, out GolemEntity golem))
            {
                return false;
            }

            if (!_golemDismantler.TryDismantleGolem(golem, out IReadOnlyList<RecipeIngredient> refunded, out string refusalReason))
            {
                LastStatusMessage = refusalReason;
                Popup(cell, "kept", BuildPopupKind.Refused);
                return true;
            }

            LastStatusMessage = "";
            // A golem that cost nothing and carried nothing still has to say something happened,
            // or the click reads as the tool failing. "dismantled" is that floor.
            Popup(cell,
                refunded != null && refunded.Count > 0
                    ? "+" + UI.ConstructionCostPolicy.FormatCost(refunded)
                    : "dismantled",
                BuildPopupKind.Refund);
            return true;
        }

        // Every registration a placement made, undone in one place, so removal and a save's
        // "replace the built world" sweep can never drift apart.
        private void DemolishBuilding(PlaceableBuilding building, Vector2Int cell, bool refund)
        {
            // A landmark is part of the world, not the factory (G10): the Clock Tower stands on
            // its site whatever the wrecking bar thinks, and a load's sweep never reaches it
            // either, since it is not runtime-placed.
            if (building.IsFixture)
            {
                LastStatusMessage = $"The {FixtureName(building)} is part of the town. It stays.";
                return;
            }

            // COZY RULE: never take goods away as the price of tidying up. If the refund will
            // not fit, the building stays standing and says so -- a demolition that destroyed
            // what it could not hand back would be exactly the punishment a full refund exists
            // to remove. Checked before anything is torn down, so the refusal leaves the world
            // untouched rather than half-dismantled.
            if (refund && !RefundWouldFit(building))
            {
                LastStatusMessage =
                    "Not demolished: the stockpile has no room for the refund. Make space first.";
                Popup(cell, "no room for refund", BuildPopupKind.Refused);
                return;
            }

            // Read what goes back NOW, before anything is torn down: unregistering a boiler from
            // the steam grid drops its SteamBoiler, and the Coke in it with it.
            List<RecipeIngredient> refundBundle = refund ? RefundFor(building) : null;

            // Tear the lane down BEFORE removing the building. BeltNetwork.TryRemove is what
            // clears any upstream belt's Next pointer; skipping it would leave a live belt
            // handing items to an unregistered segment that never ticks.
            if (_beltNetwork != null && building.GetPart<PlaceableBelt>() != null)
            {
                _beltNetwork.TryRemove(cell);
            }

            // A removed depot must stop being an endpoint too, or golems keep pushing into a
            // building that is no longer there. The same is true of everything else that
            // publishes an input tile, so the test is "did this building publish an endpoint",
            // not "was it a depot".
            if (_spatialEndpoints != null &&
                (building.GetPart<PlaceableDepot>() != null ||
                 building.GetPart<PlaceableClockTower>() != null ||
                 building.GetPart<PlaceableBoiler>() != null ||
                 building.GetPart<PlaceableFreightMast>() != null ||
                 building.GetPart<PlaceableSlagHeap>() != null ||
                 building.GetPart<PlaceableScrapRecycler>() != null))
            {
                _spatialEndpoints.Unregister(cell);
            }

            // A demolished mast must leave the registry too, or a Zeppelin stays bound to a cell
            // with nothing on it.
            building.GetPart<PlaceableFreightMast>()?.UnregisterFromMastNetwork(_mastRegistry, cell);

            // Steam has to come out of the grid before the building goes, for the same reason a
            // belt does: a paved-over pipe that stayed registered would keep carrying steam
            // through a gap the player can see.
            if (_steamNetwork != null)
            {
                building.GetPart<PlaceableSteamPipe>()?.UnregisterFromSteamNetwork(_steamNetwork);
                building.GetPart<PlaceableBoiler>()?.UnregisterFromSteamNetwork(_steamNetwork);
            }

            // Pay back, in full, what was read before the teardown began.
            if (refundBundle != null)
            {
                RefundBuilding(refundBundle, cell);
            }

            _gridMap?.Free(cell);
            _buildings.Remove(building);
            building.MarkRemoved();
            BuildingRemoved?.Invoke(building);

            // Same sweep as after a placement, and it matters MORE here: a tee whose third arm
            // has just been lifted has to stop drawing an arm into an empty cell.
            RefreshConnectedShapes();

            // Removal matters as much as placement: the interactor's cached lists would
            // otherwise keep offering a demolished depot.
            EventBus.Publish(new WorldInteractablesChangedEvent("building demolished"));
        }

        /// <summary>
        /// Pays a demolished building's cost back into the stockpile, in full.
        ///
        /// <para>
        /// <b>Full, not a percentage, and that is a settled design call rather than a starting
        /// value.</b> The game is cozy, so placing and reorganising must not be punitive. A
        /// salvage fraction is a tax on changing your mind, in a game whose whole loop is laying
        /// something down, watching it be wrong, and moving it. Do not reintroduce one as
        /// "balance". It also makes "move a building" something the existing tools already do --
        /// remove, then place -- at no cost.
        /// </para>
        ///
        /// <para>
        /// <b>Only what the player actually paid for.</b> <see cref="PlaceableBuilding.IsRuntimePlaced"/>
        /// is false for anything authored into the scene, and refunding those would mint goods
        /// out of the furniture.
        /// </para>
        /// </summary>
        private void RefundBuilding(List<RecipeIngredient> refund, Vector2Int cell)
        {
            if (_stockpile == null || refund.Count == 0)
            {
                return;
            }

            // Room was checked before a single registration was torn down (RefundWouldFit), so
            // every unit lands.
            for (int i = 0; i < refund.Count; i++)
            {
                _stockpile.Deposit(_stockpileBufferId, refund[i].itemType, refund[i].quantity);
            }

            LastStatusMessage = "";
            Popup(cell, "+" + UI.ConstructionCostPolicy.FormatCost(refund), BuildPopupKind.Refund);
        }

        /// <summary>
        /// What demolishing <paramref name="building"/> hands back: its cost, if the player paid
        /// for it, plus everything it holds -- whoever built it.
        ///
        /// <para>
        /// <b>The contents are the G10 addition</b> (from playtest: "the coke is lost if you
        /// demolish the boiler"). A boiler's firebox, a slag heap's and a recycler's Coke, and the
        /// recycler's finished Scrap are real goods the player put in or the factory made, and the
        /// same argument that refunds a dismantled golem's cargo applies: the building most worth
        /// moving is often the one full of fuel, and burning that fuel as the price of moving it
        /// puts back the sting the full refund exists to remove. Contents are refunded whether or
        /// not the building was runtime-placed, exactly as golem cargo is; only the PRICE is gated
        /// on <see cref="PlaceableBuilding.IsRuntimePlaced"/>, because only the price could be
        /// minted out of scene furniture.
        /// </para>
        /// </summary>
        public static List<RecipeIngredient> RefundFor(PlaceableBuilding building)
        {
            var refund = new List<RecipeIngredient>();
            if (building == null)
            {
                return refund;
            }

            if (building.IsRuntimePlaced && building.Cost != null)
            {
                foreach (RecipeIngredient c in building.Cost)
                {
                    Add(refund, c.itemType, c.quantity);
                }
            }

            Add(refund, ItemType.Coke, building.GetPart<PlaceableBoiler>()?.Boiler?.CokeStock ?? 0);
            Add(refund, ItemType.Coke, building.GetPart<PlaceableSlagHeap>()?.Heap?.CokeStock ?? 0);
            ScrapRecycler recycler = building.GetPart<PlaceableScrapRecycler>()?.Recycler;
            Add(refund, ItemType.Coke, recycler?.CokeStock ?? 0);
            Add(refund, ItemType.Scrap, recycler?.ScrapStock ?? 0);
            return refund;
        }

        private static void Add(List<RecipeIngredient> bundle, string itemType, int quantity)
        {
            if (quantity <= 0)
            {
                return;
            }
            for (int i = 0; i < bundle.Count; i++)
            {
                if (bundle[i].itemType == itemType)
                {
                    bundle[i] = new RecipeIngredient(itemType, bundle[i].quantity + quantity);
                    return;
                }
            }
            bundle.Add(new RecipeIngredient(itemType, quantity));
        }

        /// <summary>
        /// Whether the stockpile can take back everything <see cref="RefundFor"/> would hand
        /// back. Asked BEFORE the demolition so a refusal is a no-op rather than a
        /// half-dismantled building.
        ///
        /// <para>
        /// Answers <c>true</c> for anything that would not be refunded anyway -- no stockpile
        /// wired, scene-authored furniture holding nothing, a free building -- so the check never
        /// blocks a removal it has no stake in.
        /// </para>
        /// </summary>
        private bool RefundWouldFit(PlaceableBuilding building)
        {
            if (_stockpile == null)
            {
                return true;
            }

            foreach (RecipeIngredient item in RefundFor(building))
            {
                if (_stockpile.RoomFor(_stockpileBufferId, item.itemType) < item.quantity)
                {
                    return false;
                }
            }

            return true;
        }

        private void PlaceInternal(Vector2Int cell, GridMap map)
        {
            // Atomic with a full refund on shortfall (StorageBufferRegistry.TryWithdrawBundle):
            // a Boiler is 30 Scrap + 10 Iron Plate, and taking the Scrap for a placement that
            // then refuses would be a straight theft at the cursor.
            if (_stockpile != null && !_stockpile.TryWithdrawBundle(_stockpileBufferId, _buildingPrefab.Cost))
            {
                LastStatusMessage = $"Not enough resources to build {_buildingPrefab.name} " +
                                     $"(needs {UI.ConstructionCostPolicy.FormatCost(_buildingPrefab.Cost)}).";
                // The refusal has to appear at the cursor, or a click that could not be paid for
                // is indistinguishable from a click that did not register.
                Popup(cell, UI.ConstructionCostPolicy.FormatShortfall(ReadStock, _buildingPrefab.Cost),
                    BuildPopupKind.Refused);
                return;
            }

            LastStatusMessage = "";
            PlaceableBuilding instance = _buildingPrefab.Instantiate();
            instance.Cell = cell;
            instance.Facing = PlacementFacing;
            // What tells the save system this building is rebuildable, and which prefab to
            // rebuild it from. Recorded here, at the one place a building is ever placed.
            instance.MarkRuntimePlaced(_buildingPrefab.name);
            map.TryOccupy(cell, instance);
            _buildings.Add(instance);
            RegisterPlacedEndpoints(instance, cell, PlacementFacing);
            BuildingPlaced?.Invoke(instance);
            if (_stockpile != null && _buildingPrefab.Cost != null && _buildingPrefab.Cost.Count > 0)
            {
                Popup(cell, "-" + UI.ConstructionCostPolicy.FormatCost(_buildingPrefab.Cost), BuildPopupKind.Spent);
            }
        }

        // Every placeable that must also exist in the *simulation*, not just the scene, is
        // published here, so one place owns "this building is now really in the world".
        //
        // Takes the facing explicitly rather than reading PlacementFacing, so the save system can
        // rebuild a belt pointing the way it pointed when it was placed instead of the way the
        // cursor happens to point now. A belt's facing IS its routing, so inheriting the
        // cursor's would silently re-plumb a restored factory.
        private void RegisterPlacedEndpoints(PlaceableBuilding instance, Vector2Int cell, Facing facing)
        {
            //   * a belt: BeltNetwork creates the BeltSegment, registers it with the
            //     ConveyorSystem so it ticks, publishes a spatial endpoint on the cell, and
            //     auto-chains it to whatever it points into. The part is only told the result.
            PlaceableBelt belt = instance.GetPart<PlaceableBelt>();
            if (belt != null && _beltNetwork != null)
            {
                // A splitter is placed through the identical call with one flag.
                bool isSplitter = instance.GetPart<PlaceableBeltSplitter>() != null;
                if (_beltNetwork.TryPlace(cell, facing, isSplitter, out PlacedBelt placed))
                {
                    belt.BindSegment(placed.Segment, facing);
                }
            }

            //   * a depot: publishes its StorageBuffer on the cell, giving a routed chain
            //     somewhere to actually end.
            instance.GetPart<PlaceableDepot>()?.RegisterAsSpatialEndpoint(_spatialEndpoints, _stockpile, cell);

            //   * a construction station: stands where it was placed, facing where it was turned,
            //     and gets the scene services that let it actually build a golem. Without this a
            //     station the player PAID for was inert -- the money went and the building did
            //     not work.
            GolemConstructionStation station = instance.GetPart<GolemConstructionStation>();
            if (station != null)
            {
                station.SetPlacement(cell, facing);
                _stationConfigurator?.ConfigureStation(station);
            }

            //   * a clock tower: publishes its input tile, giving the megaproject somewhere for a
            //     golem to Push into.
            PlaceableClockTower tower = instance.GetPart<PlaceableClockTower>();
            tower?.RegisterAsSpatialEndpoint(_spatialEndpoints, tower.Site ?? _clockTowerSite, cell);

            //   * a slag heap: publishes its tile -- §5.3(c)'s costed sink.
            instance.GetPart<PlaceableSlagHeap>()?.RegisterAsSpatialEndpoint(_spatialEndpoints, cell);

            //   * a scrap recycler: publishes its tile, in BOTH directions.
            instance.GetPart<PlaceableScrapRecycler>()?.RegisterAsSpatialEndpoint(_spatialEndpoints, cell);

            //   * a freight mast: publishes its tile like a depot AND joins the mast registry.
            PlaceableFreightMast mast = instance.GetPart<PlaceableFreightMast>();
            if (mast != null)
            {
                mast.RegisterAsSpatialEndpoint(_spatialEndpoints, _stockpile, cell);
                mast.RegisterWithMastNetwork(_mastRegistry, cell);
            }

            //   * a boiler or a steam pipe: publishes itself into the SteamNetwork, which
            //     re-derives every boiler's reach on the spot. A PIPE is not an IItemEndpoint --
            //     steam is not a good that travels on tiles -- but a BOILER is, on its fuel side.
            if (_steamNetwork != null)
            {
                PlaceableBoiler boiler = instance.GetPart<PlaceableBoiler>();
                if (boiler != null)
                {
                    boiler.RegisterWithSteamNetwork(_steamNetwork, cell);
                    boiler.RegisterAsSpatialEndpoint(_spatialEndpoints, cell);
                }

                instance.GetPart<PlaceableSteamPipe>()?.RegisterWithSteamNetwork(_steamNetwork, cell);
            }

            // Neighbours change shape when something is laid beside them: a straight pipe
            // becomes a tee, a belt fed from its flank becomes a corner.
            RefreshConnectedShapes();

            // LAST, after every registration above. A listener re-scans on this, and the thing it
            // will find must already be fully wired -- a depot found before its endpoint was
            // published would be interactable and routing-invisible at once.
            EventBus.Publish(new WorldInteractablesChangedEvent("building placed"));
        }
    }
}
