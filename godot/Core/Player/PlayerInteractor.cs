using System;
using System.Collections.Generic;
using GolemFactory.Belts;
using GolemFactory.Buildings;
using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.Steam;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Player
{
    /// <summary>What an interaction popup is saying, which decides its colour in the scene.</summary>
    public enum InteractionPopupKind
    {
        /// <summary>Something good happened: a harvest, an order, a craft, a placement.</summary>
        Gain,

        /// <summary>Nothing happened, said quietly: depleted, can't pay, no Coke.</summary>
        Refused,
    }

    /// <summary>A caption rising from a point in the world.</summary>
    public readonly struct InteractionPopup
    {
        public readonly Vector3 Position;
        public readonly string Text;
        public readonly InteractionPopupKind Kind;

        /// <summary>How high above <see cref="Position"/> it starts, in world units.</summary>
        public readonly float Height;

        public InteractionPopup(Vector3 position, string text, InteractionPopupKind kind, float height)
        {
            Position = position;
            Text = text;
            Kind = kind;
            Height = height;
        }
    }

    /// <summary>
    /// Finds the nearest interactable -- a node to harvest, a station to build at, a golem to
    /// program, a boiler to fuel, a depot to relabel -- and acts on it with [E]. Also owns [G]
    /// (carry a golem), the context-shared [R], and the Hand-Crank Bench's held [E].
    ///
    /// <para>
    /// PORTED FROM Unity's MonoBehaviour of the same name (milestone G2c). The selection itself
    /// was already pure (<see cref="InteractionTargeting.SelectNearest"/>); every other rule is
    /// the Unity one, with its reasoning kept. The engine glue became:
    /// </para>
    /// <list type="bullet">
    ///   <item>transform.position -> <see cref="Position"/>, in world units (one per cell at the
    ///   default cell size). A golem stands at its cell's centre, a building at its cell's, a
    ///   node marker at its own <see cref="ResourceNodeMarker.Position"/>.</item>
    ///   <item>FindObjectsByType -> providers the scene hands over
    ///   (<see cref="ConfigureWorld"/>). Stations, benches, boilers and depots are reached as
    ///   parts of the buildings provided.</item>
    ///   <item>OnEnable/OnDisable -> <see cref="Attach"/>/<see cref="Detach"/>.</item>
    ///   <item>Input -> verbs (<see cref="Interact"/>, <see cref="RotateKey"/>,
    ///   <see cref="ToggleCarryGolem"/>, <see cref="SetInteractHeld"/>), Update ->
    ///   <see cref="Poll"/>.</item>
    ///   <item>The prompt view -> <see cref="CurrentPrompt"/>/<see cref="CurrentAffordance"/>/
    ///   <see cref="PromptPosition"/>; FloatingPopup -> <see cref="PopupRaised"/>; the panels ->
    ///   <see cref="IConstructionScreen"/>, <see cref="IWorkbenchScreen"/>, <see cref="IScreen"/>.</item>
    /// </list>
    /// </summary>
    public sealed class PlayerInteractor
    {
        // How high above a target a popup starts. The default clears the interaction caption,
        // which anchors to the same point. The second height exists for the one case that
        // spawns two captions at once (a recipe with a byproduct): at one height they rise as a
        // single smudge, a line apart they read as two goods.
        public const float PopupHeight = 1.5f;
        public const float SecondaryPopupHeight = 0.95f;

        // A carried golem rides slightly above the player so it is obvious it is in hand and
        // not working -- and is selected from there, as Unity's transform was.
        private static readonly Vector3 CarryOffset = new Vector3(0f, 0.55f, 0f);

        private static readonly Vector3 Parked =
            new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);

        private float _interactRange = 1.5f;
        private StorageBufferRegistry _stockpile;
        private string _stockpileBufferId = "FactoryStockpile";
        private IConstructionScreen _constructionScreen;
        private IWorkbenchScreen _workbench;
        private IScreen _managementScreen;
        private string _interactKeyLabel = "E";

        // Rotating an already-placed golem. "Golems cannot pivot" is a rule about *runtime
        // execution* -- nothing in a program may turn the golem mid-cycle -- not about the
        // player repositioning one between runs, which is the core spatial puzzle.
        private BuildModeController _buildMode;

        // Moving a placed golem: the player walks to the tile they want and carries the golem
        // there. GridMap is the simulation truth for occupancy.
        private GridMap _gridMap;
        private GridCoordinateConverter _converter = new GridCoordinateConverter(new Vector2(1f, 1f));

        // The market (progression-design §13.2). Optional: with no market wired, a stall is
        // harvested and never ordered from.
        private TruckloadMarket _market;
        private Func<long> _currentTick = () => 0;
        private StorageBufferRegistry _marketWallet;
        private string _marketWalletBufferId = "FactoryStockpile";

        // Where things stand on screen, when the scene knows better than the grid. Unity read
        // every interactable's transform.position, which is NOT always its cell -- a carried
        // golem rides with the player -- so the scene can answer for any object; null means
        // "use its cell".
        private Func<object, Vector3?> _positionOf;

        // What the scene says exists. Unity found these with FindObjectsByType.
        private Func<IEnumerable<ResourceNodeMarker>> _markerSource;
        private Func<IEnumerable<PlaceableBuilding>> _buildingSource;
        private Func<IEnumerable<GolemEntity>> _golemSource;

        // The snapshot RefreshInteractables takes, and the per-kind positions refilled from it.
        private ResourceNodeMarker[] _nodeMarkers = new ResourceNodeMarker[0];
        private GolemConstructionStation[] _stations = new GolemConstructionStation[0];
        private GolemEntity[] _golems = new GolemEntity[0];
        private PlaceableBoiler[] _boilers = new PlaceableBoiler[0];
        private PlaceableDepot[] _depots = new PlaceableDepot[0];
        private HandCrankBench[] _benches = new HandCrankBench[0];
        private Vector2Int[] _boilerCells = new Vector2Int[0];
        private Vector2Int[] _depotCells = new Vector2Int[0];
        private Vector2Int[] _benchCells = new Vector2Int[0];
        private Vector3[] _nodePositions = new Vector3[0];
        private Vector3[] _stationPositions = new Vector3[0];
        private Vector3[] _golemPositions = new Vector3[0];
        private Vector3[] _boilerPositions = new Vector3[0];
        private Vector3[] _depotPositions = new Vector3[0];

        // The bench whose completed-craft tally is mirrored in _watchedCraftCount, held
        // alongside it so walking from one bench to another cannot make the second bench's
        // standing total look like a craft that just finished under the player's hand.
        private HandCrankBench _watchedBench;
        private int _watchedCraftCount;
        private bool _interactHeld;
        private bool _attached;

        /// <summary>Where the player stands, in world units. Unity's transform.position.</summary>
        public Vector3 Position { get; set; }

        /// <summary>Raised for every popup Unity spawned.</summary>
        public event Action<InteractionPopup> PopupRaised;

        // Set by Interact()/the Try* methods on failure, for the prompt or a test to surface.
        public string LastStatusMessage { get; private set; } = "";

        /// <summary>The pick the affordance is currently advertising.</summary>
        public InteractionPick CurrentPick { get; private set; } = InteractionPick.None;

        public InteractionAffordance CurrentAffordance { get; private set; } = InteractionAffordance.Hidden;

        /// <summary>The prompt text currently shown (empty when nothing is advertised).</summary>
        public string CurrentPrompt { get; private set; } = "";

        /// <summary>Where the prompt is anchored -- the advertised target -- when one is shown.</summary>
        public Vector3 PromptPosition { get; private set; }

        /// <summary>The golem currently in the player's hands, or null.</summary>
        public GolemEntity CarriedGolem { get; private set; }

        /// <summary>
        /// The bench the player is standing at, or null. Exposed so the HUD readout renders
        /// exactly the bench the crank would turn, rather than searching for one itself.
        /// </summary>
        public HandCrankBench NearestBench { get; private set; }

        // --- Wiring ----------------------------------------------------------------------------

        public void Configure(
            float interactRange, StorageBufferRegistry stockpile, string stockpileBufferId,
            IConstructionScreen constructionScreen, IWorkbenchScreen workbench)
        {
            _interactRange = interactRange;
            _stockpile = stockpile;
            _stockpileBufferId = stockpileBufferId;
            _constructionScreen = constructionScreen;
            _workbench = workbench;
        }

        /// <summary>
        /// Wires the affordance separately from Configure, so a scene that wants no prompt
        /// simply never calls it.
        /// </summary>
        public void ConfigureAffordance(IScreen managementScreen, string interactKeyLabel)
        {
            _managementScreen = managementScreen;
            _interactKeyLabel = interactKeyLabel;
        }

        /// <summary>What exists to interact with. Unity found all of this with FindObjectsByType.</summary>
        public void ConfigureWorld(
            Func<IEnumerable<ResourceNodeMarker>> markers,
            Func<IEnumerable<PlaceableBuilding>> buildings,
            Func<IEnumerable<GolemEntity>> golems)
        {
            _markerSource = markers;
            _buildingSource = buildings;
            _golemSource = golems;
        }

        /// <summary>
        /// Where an interactable stands on screen, when that differs from its cell's centre --
        /// what Unity read from each transform. Return null to use the cell. Optional: without
        /// it a golem stands at its cell (or, carried, just above the player) and a building at
        /// its cell.
        /// </summary>
        public void ConfigurePositions(Func<object, Vector3?> positionOf) => _positionOf = positionOf;

        public void ConfigureMarket(
            TruckloadMarket market, Func<long> currentTick, StorageBufferRegistry wallet, string walletBufferId)
        {
            _market = market;
            _currentTick = currentTick ?? (() => 0);
            _marketWallet = wallet;
            if (!string.IsNullOrEmpty(walletBufferId))
            {
                _marketWalletBufferId = walletBufferId;
            }
        }

        /// <summary>
        /// The build mode this interactor defers the shared R key to. Optional: with none
        /// wired, R always reaches a bench or a golem.
        /// </summary>
        public void ConfigureBuildMode(BuildModeController buildMode) => _buildMode = buildMode;

        /// <summary>Wires golem repositioning. Optional, like every other Configure* here.</summary>
        public void ConfigureGolemPlacement(GridMap gridMap, Vector2 cellSize)
        {
            _gridMap = gridMap;
            _converter = new GridCoordinateConverter(cellSize);
        }

        /// <summary>
        /// Takes the snapshot and starts listening for the world changing -- Unity's OnEnable.
        /// </summary>
        public void Attach()
        {
            RefreshInteractables();
            if (!_attached)
            {
                // The snapshot is of a world that keeps changing. Without this subscription it
                // was never taken again, so nothing the player built was ever interactable.
                EventBus.WorldInteractablesChanged += OnWorldInteractablesChanged;
                _attached = true;
            }
        }

        /// <summary>Stops listening -- Unity's OnDisable.</summary>
        public void Detach()
        {
            if (_attached)
            {
                EventBus.WorldInteractablesChanged -= OnWorldInteractablesChanged;
                _attached = false;
            }
            CurrentPrompt = "";
            CurrentAffordance = InteractionAffordance.Hidden;
        }

        // Re-scan wholesale rather than apply a delta: the lists are small, this fires only
        // when something is built or demolished, and a delta protocol would be a second source
        // of truth about what exists.
        private void OnWorldInteractablesChanged(WorldInteractablesChangedEvent e) => RefreshInteractables();

        /// <summary>Re-reads what exists from the providers.</summary>
        public void RefreshInteractables()
        {
            _nodeMarkers = Snapshot(_markerSource);
            _golems = Snapshot(_golemSource);

            var stations = new List<GolemConstructionStation>();
            var boilers = new List<PlaceableBoiler>();
            var boilerCells = new List<Vector2Int>();
            var depots = new List<PlaceableDepot>();
            var depotCells = new List<Vector2Int>();
            var benches = new List<HandCrankBench>();
            var benchCells = new List<Vector2Int>();
            foreach (PlaceableBuilding building in Snapshot(_buildingSource))
            {
                if (building == null || building.IsRemoved)
                {
                    continue;
                }

                GolemConstructionStation station = building.GetPart<GolemConstructionStation>();
                if (station != null)
                {
                    stations.Add(station);
                }

                PlaceableBoiler boiler = building.GetPart<PlaceableBoiler>();
                if (boiler != null)
                {
                    boilers.Add(boiler);
                    boilerCells.Add(building.Cell);
                }

                PlaceableDepot depot = building.GetPart<PlaceableDepot>();
                if (depot != null)
                {
                    depots.Add(depot);
                    depotCells.Add(building.Cell);
                }

                HandCrankBench bench = building.GetPart<HandCrankBench>();
                if (bench != null)
                {
                    benches.Add(bench);
                    benchCells.Add(building.Cell);
                }
            }

            _stations = stations.ToArray();
            _boilers = boilers.ToArray();
            _boilerCells = boilerCells.ToArray();
            _depots = depots.ToArray();
            _depotCells = depotCells.ToArray();
            _benches = benches.ToArray();
            _benchCells = benchCells.ToArray();

            _nodePositions = Resize(_nodePositions, _nodeMarkers.Length);
            _stationPositions = Resize(_stationPositions, _stations.Length);
            _golemPositions = Resize(_golemPositions, _golems.Length);
            _boilerPositions = Resize(_boilerPositions, _boilers.Length);
            _depotPositions = Resize(_depotPositions, _depots.Length);
        }

        private static T[] Snapshot<T>(Func<IEnumerable<T>> source) =>
            source == null ? new T[0] : new List<T>(source()).ToArray();

        private static Vector3[] Resize(Vector3[] array, int length) =>
            array.Length == length ? array : new Vector3[length];

        // --- Positions ---------------------------------------------------------------------------

        private Vector3 CellCentre(Vector2Int cell) => _converter.CellToWorldCenter(cell);

        // The scene's answer first, then the grid's.
        private Vector3 Resolve(object thing, Vector2Int cell) =>
            _positionOf?.Invoke(thing) ?? CellCentre(cell);

        /// <summary>Where a golem is, as the player sees it: its cell, or riding above the player.</summary>
        public Vector3 PositionOf(GolemEntity golem) =>
            _positionOf?.Invoke(golem)
            ?? (golem == CarriedGolem ? Position + CarryOffset : CellCentre(golem.Cell));

        // Removed things are parked at +infinity, so a stale snapshot is never *wrong* -- it just
        // keeps an entry one refresh longer than it should.
        private void FillPositions()
        {
            for (int i = 0; i < _nodeMarkers.Length; i++)
            {
                _nodePositions[i] = _nodeMarkers[i] != null ? _nodeMarkers[i].Position : Parked;
            }
            for (int i = 0; i < _stations.Length; i++)
            {
                _stationPositions[i] = _stations[i] != null ? Resolve(_stations[i], _stations[i].Cell) : Parked;
            }
            FillGolemPositions();
            for (int i = 0; i < _boilers.Length; i++)
            {
                _boilerPositions[i] = _boilers[i] != null ? Resolve(_boilers[i], _boilerCells[i]) : Parked;
            }
            for (int i = 0; i < _depots.Length; i++)
            {
                _depotPositions[i] = _depots[i] != null ? Resolve(_depots[i], _depotCells[i]) : Parked;
            }
        }

        private void FillGolemPositions()
        {
            for (int i = 0; i < _golems.Length; i++)
            {
                GolemEntity golem = _golems[i];
                _golemPositions[i] = golem != null && !golem.IsRemoved ? PositionOf(golem) : Parked;
            }
        }

        // --- The market ----------------------------------------------------------------------------

        /// <summary>
        /// Orders one truckload from the stall a marker trades for. Reached only when the stall
        /// is EMPTY, which makes harvest and order share one key without ever being ambiguous: a
        /// stall with stock is something you take from, a stall without is something you buy from.
        /// </summary>
        public bool TryOrderTruckload(ResourceNodeMarker marker)
        {
            if (marker == null || _market == null)
            {
                LastStatusMessage = "Nothing to order here.";
                return false;
            }

            if (!_market.TryGetOffer(marker.NodeId, out MarketOffer offer))
            {
                LastStatusMessage = "This stall is not trading.";
                return false;
            }

            MarketOrderResult result = _market.TryOrder(
                marker.NodeId, _marketWallet, _marketWalletBufferId, _currentTick());

            switch (result)
            {
                case MarketOrderResult.Ordered:
                    LastStatusMessage = $"Ordered {offer.TruckloadSize} {marker.ItemType}; the cart is on its way.";
                    Popup(marker.Position, "Ordered", InteractionPopupKind.Gain);
                    return true;

                case MarketOrderResult.AlreadyInTransit:
                    LastStatusMessage = "That cart is already on the road.";
                    Popup(marker.Position, "En route", InteractionPopupKind.Refused);
                    return false;

                case MarketOrderResult.CannotAfford:
                    // Names the shortfall, exactly as a refused chassis or building does.
                    LastStatusMessage = ConstructionCostPolicy.FormatShortfall(MarketStockOf, offer.Price);
                    Popup(marker.Position, "Can't pay", InteractionPopupKind.Refused);
                    return false;

                default:
                    LastStatusMessage = "This stall never runs dry.";
                    return false;
            }
        }

        private int MarketStockOf(string itemType) =>
            _marketWallet == null ? 0 : _marketWallet.GetQuantity(_marketWalletBufferId, itemType);

        /// <summary>
        /// Whether this stall would take an order right now: a market is wired, it trades this
        /// node, and it has no cart already on the road.
        /// </summary>
        public bool CanOrderFrom(ResourceNodeMarker marker) =>
            marker != null
            && _market != null
            && _market.TryGetOffer(marker.NodeId, out _)
            && !_market.IsInTransit(marker.NodeId);

        // --- R and G -------------------------------------------------------------------------------

        /// <summary>
        /// The R key, shared three ways and decided by context rather than by a mode: with a
        /// placeable in hand it turns the ghost (build mode takes it), standing at a bench it
        /// changes what the bench makes, and otherwise it turns the golem you are next to.
        ///
        /// <para>
        /// A bench wins over a golem because you can only be at one of them, and if you are
        /// standing at a bench you are cranking, not rotating something behind you.
        /// </para>
        /// </summary>
        public bool RotateKey()
        {
            if (_buildMode != null && _buildMode.IsPlacementActive)
            {
                return false;
            }

            HandCrankBench bench = SelectNearestBench(_interactRange);
            if (bench != null)
            {
                bench.CycleRecipe();
                LastStatusMessage = bench.SelectedRecipe != null
                    ? $"Bench set to {bench.SelectedRecipe.name}."
                    : "This bench has nothing it can make.";
                return true;
            }

            return RotateNearestGolem();
        }

        /// <summary>
        /// The [G] key: pick up the golem you are standing next to, or -- if already carrying
        /// one -- put it down on the tile you are standing on.
        /// </summary>
        /// <remarks>
        /// Carry/drop rather than a single "summon the nearest golem here" action, which is what
        /// this originally was and which turned out to be genuinely ambiguous: with two golems in
        /// play, the golem already placed nearby was usually nearer than the new one at the
        /// station, so the wrong golem moved. Picking up explicitly removes the guess.
        /// </remarks>
        public bool ToggleCarryGolem() => CarriedGolem != null ? TryDropCarriedGolem() : TryPickUpNearestGolem();

        public bool TryPickUpNearestGolem()
        {
            // Arm's reach, like every other interaction -- you pick up the golem you are
            // standing next to, which is the disambiguation.
            GolemEntity golem = SelectNearestGolem(_interactRange);
            if (golem == null)
            {
                LastStatusMessage = "No golem in range to pick up.";
                return false;
            }

            Vector3 at = PositionOf(golem);
            CarriedGolem = golem;
            golem.SetHeld(true);
            LastStatusMessage = $"Carrying {golem.GolemId}. [G] to set it down.";
            Popup(at, "Carrying " + golem.GolemId, InteractionPopupKind.Gain);
            return true;
        }

        public bool TryDropCarriedGolem()
        {
            GolemEntity golem = CarriedGolem;
            if (golem == null)
            {
                return false;
            }

            Vector2Int cell = _converter.WorldToCell(Position);

            // GridMap is the simulation truth for occupancy. Dropping a golem inside a depot
            // would give it that depot's tile as its own, so its source/target would read the
            // depot's neighbours instead of the ones the player was aiming at.
            if (_gridMap != null && _gridMap.IsOccupied(cell))
            {
                LastStatusMessage = "Something is already built on this tile.";
                Popup(Position, "Tile occupied", InteractionPopupKind.Refused);
                return false;
            }

            golem.SetPlacement(cell, golem.Facing);
            golem.SetHeld(false);
            CarriedGolem = null;

            LastStatusMessage = $"{golem.GolemId} placed at {cell}.";
            Popup(Position, "Placed " + golem.GolemId, InteractionPopupKind.Gain);
            return true;
        }

        // Nearest golem within a radius, reusing the same pure selector the routing highlight
        // uses so "the golem the game is talking about" is decided one way, not two.
        private GolemEntity SelectNearestGolem(float range)
        {
            FillGolemPositions();
            int index = RoutingFocus.SelectNearestIndex(Position, _golemPositions, range);
            return index >= 0 && index < _golems.Length ? _golems[index] : null;
        }

        /// <summary>
        /// Turns the nearest in-range golem one step clockwise. This is the move that makes
        /// facing a puzzle rather than a fact about where you happened to build.
        /// </summary>
        public bool RotateNearestGolem()
        {
            // The nearest GOLEM, not the winner of the multi-kind [E] pick. Those differ
            // constantly and the difference is fatal here: a golem is almost always placed right
            // next to the node it pulls from, so the node usually wins the combined pick and
            // rotation would refuse with "no golem in range" while the player stands next to one.
            GolemEntity golem = SelectNearestGolem(_interactRange);
            if (golem == null)
            {
                LastStatusMessage = "No golem in range to rotate.";
                return false;
            }

            Facing rotated = FacingUtility.RotateClockwise(golem.Facing);
            golem.SetPlacement(golem.Cell, rotated);
            LastStatusMessage = $"{golem.GolemId} now faces {FacingVisuals.Describe(rotated)}.";
            Popup(PositionOf(golem), "Facing " + FacingVisuals.Describe(rotated), InteractionPopupKind.Gain);
            return true;
        }

        // --- Per frame -----------------------------------------------------------------------------

        /// <summary>
        /// Whether [E] is held right now -- the Hand-Crank Bench's input. Held, not tapped: §9's
        /// manual era has to cost the player's attention, or it is just a slower golem that
        /// needs no supervision.
        /// </summary>
        public void SetInteractHeld(bool held) => _interactHeld = held;

        /// <summary>
        /// Unity's Update: drops a carried golem that was removed some other way, re-selects the
        /// affordance, and drives the crank.
        /// </summary>
        public void Poll()
        {
            if (CarriedGolem != null && CarriedGolem.IsRemoved)
            {
                CarriedGolem = null;
            }

            RefreshAffordance();
            DriveHandCrank();
        }

        /// <summary>
        /// Sets the flag the bench reads. Polled rather than event-driven because "is the key
        /// down right now" is a state, and the bench accrues on SIMULATION ticks -- this only
        /// sets the flag, and <c>HandCrankBench.Tick</c> decides what a tick of cranking is worth.
        /// Every other bench is cleared, so walking away from a half-turned crank stops it.
        /// </summary>
        private void DriveHandCrank()
        {
            HandCrankBench nearest = SelectNearestBench(_interactRange);
            NearestBench = nearest;
            ReportFinishedCrafts(nearest);

            for (int i = 0; i < _benches.Length; i++)
            {
                if (_benches[i] != null)
                {
                    _benches[i].IsCranking = _benches[i] == nearest && _interactHeld;
                }
            }
        }

        /// <summary>
        /// The "+1 Coke" confirmation a finished hand-crank gives, matching the one harvesting a
        /// node gives. Watched from here rather than announced by the bench: the bench is a
        /// simulation object, and the popup belongs to whoever is standing at the handle.
        /// </summary>
        /// <remarks>
        /// A batch COUNT, not a bool: at 4x speed several ticks land between two polls, so a short
        /// recipe can finish more than once; "+2 Coke" is truthful and quieter than a stack.
        /// Only the NEAREST bench is watched, because only the nearest bench is ever cranking.
        /// </remarks>
        private void ReportFinishedCrafts(HandCrankBench bench)
        {
            // Arriving at a bench (or leaving one) re-seeds the tally instead of reporting it.
            // Whatever this bench made before the player walked up is history, not news.
            if (bench != _watchedBench)
            {
                _watchedBench = bench;
                _watchedCraftCount = bench != null ? bench.CompletedCrafts : 0;
                return;
            }

            if (bench == null)
            {
                return;
            }

            int batches = bench.CompletedCrafts - _watchedCraftCount;
            _watchedCraftCount = bench.CompletedCrafts;

            // LastCompletedRecipe, not SelectedRecipe: [R] can cycle the dial in the same frame
            // the craft lands, and the caption has to name what was actually made.
            RecipeDefinition recipe = bench.LastCompletedRecipe;
            if (batches <= 0 || recipe == null)
            {
                return;
            }

            Vector3 at = BenchPosition(bench);
            Popup(at, YieldPopupText.Gain(recipe.outputItemType, recipe.outputQuantity * batches),
                InteractionPopupKind.Gain);

            // The byproduct gets its own line rather than sharing one: a different good arriving
            // in the same stockpile, and R4's Slag is the case the player most needs told plainly.
            if (recipe.HasByproduct)
            {
                Popup(at, YieldPopupText.Gain(recipe.byproductItemType, recipe.byproductQuantity * batches),
                    InteractionPopupKind.Refused, SecondaryPopupHeight);
            }
        }

        private Vector3 BenchPosition(HandCrankBench bench)
        {
            for (int i = 0; i < _benches.Length; i++)
            {
                if (_benches[i] == bench)
                {
                    return Resolve(bench, _benchCells[i]);
                }
            }
            return Position;
        }

        /// <summary>Nearest bench within arm's reach, or null. Same rule as the golem pick.</summary>
        public HandCrankBench SelectNearestBench(float range)
        {
            HandCrankBench nearest = null;
            float bestSqr = range * range;
            for (int i = 0; i < _benches.Length; i++)
            {
                if (_benches[i] == null)
                {
                    continue;
                }

                float sqr = (Resolve(_benches[i], _benchCells[i]) - Position).sqrMagnitude;
                if (sqr <= bestSqr)
                {
                    bestSqr = sqr;
                    nearest = _benches[i];
                }
            }

            return nearest;
        }

        // --- The [E] affordance --------------------------------------------------------------------

        /// <summary>Re-selects the nearest interactable and updates the prompt.</summary>
        public void RefreshAffordance()
        {
            InteractionPick pick = SelectNearestPick();
            CurrentPick = pick;
            CurrentAffordance = InteractionTargeting.ClassifyAffordance(pick, _interactRange);

            // A full screen owns the player's attention and dims the world behind it; a
            // world-space prompt drawn under that dim is exactly the kind of leftover HUD the
            // presentation pass removed.
            if (!HudScreenPolicy.ShouldShowWorldHud(
                    _workbench != null && _workbench.IsOpen,
                    _managementScreen != null && _managementScreen.IsOpen,
                    _constructionScreen != null && _constructionScreen.IsOpen))
            {
                CurrentAffordance = InteractionAffordance.Hidden;
            }

            object target = ResolveTarget(pick, out Vector3 targetPosition);
            if (target == null)
            {
                CurrentAffordance = InteractionAffordance.Hidden;
            }

            if (CurrentAffordance == InteractionAffordance.Hidden)
            {
                CurrentPrompt = "";
                return;
            }

            // A depleted node is in range but cannot be harvested. Saying "[E] Harvest Aether -
            // depleted" told the player two contradictory things at once.
            if (CurrentAffordance == InteractionAffordance.Ready && IsUnavailable(pick, target))
            {
                CurrentAffordance = InteractionAffordance.Unavailable;
            }

            DescribeTarget(pick, target, CarriedGolem != null, out string targetName, out string detail);
            string verb = DescribeMarketStall(pick, target, ref detail);

            // [G] AND [R] ACT ON THE NEAREST GOLEM, NOT ON THE WINNER OF THE [E] PICK, and the two
            // differ at exactly the moment the player most needs to be told the keys exist. A
            // freshly built golem is emitted onto the tile the station faces, so standing where
            // you were when you built it, the STATION is nearest -- and the golem's own caption,
            // the only line in the game that mentions [G], is not the one drawn. So it is appended.
            if (pick.Kind != InteractionKind.Program)
            {
                GolemEntity handled = SelectNearestGolem(_interactRange);
                if (handled != null)
                {
                    detail = InteractionTargeting.AppendDetail(
                        detail, InteractionTargeting.GolemHandlingHint(handled.GolemId, CarriedGolem != null));
                }
            }

            CurrentPrompt = InteractionTargeting.BuildPrompt(
                pick.Kind, targetName, detail, CurrentAffordance, _interactKeyLabel, verb);
            PromptPosition = targetPosition;
        }

        private InteractionPick SelectNearestPick()
        {
            FillPositions();
            return InteractionTargeting.SelectNearest(
                Position, _nodePositions, _stationPositions, _golemPositions, _boilerPositions, _depotPositions);
        }

        private object ResolveTarget(InteractionPick pick, out Vector3 position)
        {
            position = default;
            int i = pick.Index;
            switch (pick.Kind)
            {
                case InteractionKind.Harvest when i >= 0 && i < _nodeMarkers.Length:
                    position = _nodePositions[i];
                    return _nodeMarkers[i];
                case InteractionKind.Construct when i >= 0 && i < _stations.Length:
                    position = _stationPositions[i];
                    return _stations[i];
                case InteractionKind.Program when i >= 0 && i < _golems.Length:
                    position = _golemPositions[i];
                    return _golems[i];
                case InteractionKind.Refuel when i >= 0 && i < _boilers.Length:
                    position = _boilerPositions[i];
                    return _boilers[i];
                case InteractionKind.Sort when i >= 0 && i < _depots.Length:
                    position = _depotPositions[i];
                    return _depots[i];
                default:
                    return null;
            }
        }

        // A boiler with no Coke to give it reads Unavailable rather than Ready, for the same
        // reason a depleted node does: "[E] Fuel Boiler" on a player holding nothing promises an
        // action that will refuse.
        private bool IsUnavailable(InteractionPick pick, object target)
        {
            switch (pick.Kind)
            {
                case InteractionKind.Harvest:
                {
                    var marker = (ResourceNodeMarker)target;
                    // An empty stall with a cart already on the road is genuinely unavailable; an
                    // empty stall you can order from is not, it is the other half of the market.
                    if (marker.IsDepleted && CanOrderFrom(marker))
                    {
                        return false;
                    }

                    return marker.IsDepleted;
                }
                case InteractionKind.Refuel:
                    return BoilerRefuelPolicy.AmountToLoad(StockpileCoke) <= 0;
                default:
                    return false;
            }
        }

        /// <summary>
        /// An EMPTY market stall's caption (G10, at the user's call). [E] there orders a
        /// truckload, so the line names that and its price -- "Harvest Copper Ore - depleted"
        /// described an action the key would not take. With a cart already on the road the key
        /// does nothing, and the line says why instead of "depleted". Returns the verb to use,
        /// or null to keep the kind's own; a stall with stock is untouched.
        /// </summary>
        private string DescribeMarketStall(InteractionPick pick, object target, ref string detail)
        {
            if (pick.Kind != InteractionKind.Harvest || !(target is ResourceNodeMarker marker) || !marker.IsDepleted
                || _market == null || !_market.TryGetOffer(marker.NodeId, out MarketOffer offer))
            {
                return null;
            }

            if (_market.IsInTransit(marker.NodeId))
            {
                detail = "cart on its way";
                return null;
            }

            detail = ConstructionCostPolicy.FormatCost(offer.Price);
            return "Order a truckload of";
        }

        private int StockpileCoke =>
            _stockpile == null ? 0 : _stockpile.GetQuantity(_stockpileBufferId, ItemType.Coke);

        private static void DescribeTarget(
            InteractionPick pick, object target, bool isCarrying, out string targetName, out string detail)
        {
            targetName = "";
            detail = "";
            switch (pick.Kind)
            {
                case InteractionKind.Harvest:
                {
                    var marker = (ResourceNodeMarker)target;
                    string itemType = marker.ItemType;
                    // "Copper Ore", not "CopperOre": a caption is English, not an id.
                    targetName = string.IsNullOrEmpty(itemType) ? marker.NodeId : ItemTiers.DisplayName(itemType);
                    detail = ResourceNodeVisualState.DescribeRemaining(marker.RemainingQuantity);
                    break;
                }
                case InteractionKind.Refuel:
                {
                    var boiler = (PlaceableBoiler)target;
                    targetName = "Boiler";
                    // The boiler's own stock, not the player's: what the player wants to know
                    // standing here is whether this firebox needs feeding.
                    detail = boiler.Boiler != null
                        ? SteamGaugeUtility.FormatBoiler(boiler.Boiler.CokeStock, boiler.Boiler.WorkingGolemCount)
                        : "cold";
                    break;
                }
                case InteractionKind.Sort:
                {
                    var depot = (PlaceableDepot)target;
                    // The CURRENT label, not the next one: with a cycle that grows as the factory
                    // does, "what is it now" is the harder question.
                    detail = "holds " + depot.FilterLabel + " · [E] relabel";
                    break;
                }
                case InteractionKind.Program:
                {
                    var golem = (GolemEntity)target;
                    targetName = golem.GolemId;
                    // Facing is in the caption because it is routing, not decoration, and this is
                    // the only place the player is told [R] and [G] exist at all.
                    detail = InteractionTargeting.AppendDetail(
                        "faces " + FacingVisuals.Describe(golem.Facing) + " · [R] turn",
                        // No id: the golem is already this caption's subject.
                        InteractionTargeting.GolemHandlingHint(null, isCarrying));
                    break;
                }
            }
        }

        // --- [E] -----------------------------------------------------------------------------------

        // Finds the single nearest interactable of any kind within range and acts on it.
        // Returns false (with LastStatusMessage explaining why) if nothing was in range or the
        // action itself failed.
        public bool Interact()
        {
            InteractionPick pick = SelectNearestPick();
            if (!pick.IsInRange(_interactRange))
            {
                LastStatusMessage = "Nothing in range to interact with.";
                return false;
            }

            object target = ResolveTarget(pick, out _);
            switch (pick.Kind)
            {
                case InteractionKind.Harvest:
                {
                    // ONE KEY, TWO ACTIONS, decided by the stall rather than by a mode: full
                    // stalls are harvested, empty ones are ordered from.
                    var marker = target as ResourceNodeMarker;
                    if (marker != null && marker.IsDepleted && CanOrderFrom(marker))
                    {
                        return TryOrderTruckload(marker);
                    }

                    return TryHarvest(marker);
                }
                case InteractionKind.Construct:
                    return TryOpenConstruction(target as GolemConstructionStation);
                case InteractionKind.Program:
                    return TryProgram(target as GolemEntity);
                case InteractionKind.Refuel:
                    return TryRefuelBoiler(target as PlaceableBoiler);
                case InteractionKind.Sort:
                    return TryRelabelDepot(target as PlaceableDepot);
                default:
                    LastStatusMessage = "Nothing in range to interact with.";
                    return false;
            }
        }

        /// <summary>
        /// Chalks the next label onto a depot (docs/cozy-automation-design.md §1): the crate
        /// cycles through "any goods" and every item type the stockpile has handled, and the
        /// tile it publishes is re-registered on the spot.
        /// </summary>
        public bool TryRelabelDepot(PlaceableDepot depot)
        {
            if (depot == null)
            {
                LastStatusMessage = "No depot in range to label.";
                return false;
            }

            depot.CycleFilter();
            LastStatusMessage = "Depot now holds " + depot.FilterLabel + ".";
            Popup(DepotPosition(depot), depot.FilterLabel, InteractionPopupKind.Gain);
            return true;
        }

        private Vector3 DepotPosition(PlaceableDepot depot)
        {
            for (int i = 0; i < _depots.Length; i++)
            {
                if (_depots[i] == depot)
                {
                    return Resolve(depot, _depotCells[i]);
                }
            }
            return Position;
        }

        private Vector3 BoilerPosition(PlaceableBoiler boiler)
        {
            for (int i = 0; i < _boilers.Length; i++)
            {
                if (_boilers[i] == boiler)
                {
                    return Resolve(boiler, _boilerCells[i]);
                }
            }
            return Position;
        }

        /// <summary>
        /// Hand-loads Coke from the stockpile into a boiler -- §10's blackout backstop.
        ///
        /// <para>
        /// THE LOOP THIS BREAKS: a boiler's only other Coke writer is <c>BoilerFuelEndpoint</c>,
        /// which a golem Pushes into, and a golem needs a powered boiler to move at all. A
        /// player-built boiler starts at zero, so without this the first boiler could never be
        /// lit and a total blackout was permanent.
        /// </para>
        /// </summary>
        public bool TryRefuelBoiler(PlaceableBoiler boiler)
        {
            if (boiler == null || boiler.Boiler == null)
            {
                LastStatusMessage = "No boiler in range.";
                return false;
            }

            Vector3 at = BoilerPosition(boiler);
            int amount = BoilerRefuelPolicy.AmountToLoad(StockpileCoke);
            if (amount <= 0)
            {
                LastStatusMessage = "No Coke in the stockpile.";
                Popup(at, "No Coke", InteractionPopupKind.Refused);
                return false;
            }

            // Withdraw first, then add. The reverse order would mint Coke if the withdrawal
            // failed -- and the withdrawal is the operation that can fail.
            if (!_stockpile.TryWithdraw(_stockpileBufferId, ItemType.Coke, amount))
            {
                LastStatusMessage = "No Coke in the stockpile.";
                Popup(at, "No Coke", InteractionPopupKind.Refused);
                return false;
            }

            boiler.Boiler.AddCoke(amount);
            // How long it lasts, said at the moment the player pays for it (G10).
            LastStatusMessage = $"Loaded {amount} Coke: {SteamGaugeUtility.FormatLastsOneGolem(amount)} for one golem, "
                + $"{SteamGaugeUtility.CokePerMinutePerGolem} Coke a minute per working golem.";
            Popup(at, YieldPopupText.Gain(ItemType.Coke, amount), InteractionPopupKind.Gain);
            return true;
        }

        public bool TryHarvest(ResourceNodeMarker marker)
        {
            if (marker == null)
            {
                LastStatusMessage = "Nothing left to harvest here.";
                return false;
            }

            if (!marker.TryHarvest(out ItemStack item))
            {
                LastStatusMessage = "Nothing left to harvest here.";
                // A refusal needs to say so where the player is looking.
                Popup(marker.Position, "Depleted", InteractionPopupKind.Refused);
                return false;
            }

            _stockpile?.Deposit(_stockpileBufferId, item.ItemType);

            LastStatusMessage = $"Harvested {item.ItemType}.";
            Popup(marker.Position, YieldPopupText.Gain(item.ItemType, 1), InteractionPopupKind.Gain);
            return true;
        }

        public bool TryOpenConstruction(GolemConstructionStation station)
        {
            if (station == null || _constructionScreen == null)
            {
                LastStatusMessage = "No construction panel available.";
                return false;
            }

            _constructionScreen.Open(station);
            LastStatusMessage = "";
            return true;
        }

        public bool TryProgram(GolemEntity golem)
        {
            if (golem == null || _workbench == null)
            {
                LastStatusMessage = "No Workbench available.";
                return false;
            }

            _workbench.Open();
            _workbench.RetargetGolem(golem);
            LastStatusMessage = $"Programming {golem.GolemId}.";
            return true;
        }

        private void Popup(Vector3 position, string text, InteractionPopupKind kind, float height = PopupHeight)
        {
            if (!string.IsNullOrEmpty(text))
            {
                PopupRaised?.Invoke(new InteractionPopup(position, text, kind, height));
            }
        }
    }
}
