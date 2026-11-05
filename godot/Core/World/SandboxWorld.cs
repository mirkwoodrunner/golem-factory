using System;
using System.Collections.Generic;
using System.Linq;
using GolemFactory.Audio;
using GolemFactory.Belts;
using GolemFactory.Blueprints;
using GolemFactory.Buildings;
using GolemFactory.ClockTower;
using GolemFactory.Compat;
using GolemFactory.Data;
using GolemFactory.Diagnostics;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.AssemblyLine;
using GolemFactory.Progression;
using GolemFactory.Player;
using GolemFactory.PunchCards;
using GolemFactory.Save;
using GolemFactory.Simulation;
using GolemFactory.Steam;
using GolemFactory.UI;

namespace GolemFactory.World
{
    /// <summary>
    /// The whole Sandbox, composed: every registry Unity kept on a Holder, and the wiring
    /// SandboxBootstrap.Start did by sweeping the scene (milestone G5).
    ///
    /// <para>
    /// Unity needed four late-wiring seams because a prefab could not hold a scene reference, so
    /// a building placed after the one-shot sweep had to ask the bootstrap to wire it. Here the
    /// composition IS the bootstrap and outlives startup, so a station the player places is
    /// wired by the same <see cref="ConfigureStation"/> call the starter station was -- the seam
    /// survives only as the interface BuildModeController already asks through.
    /// </para>
    ///
    /// <para>
    /// Engine-free on purpose: the Godot scene wraps one of these (WorldNode), and a test or a
    /// scenario can build the same world with no scene at all. The registration ORDER below is
    /// SandboxBootstrap's, and it matters in two places -- the buffer policy runs before anything
    /// creates the stockpile (a buffer takes its capacity at creation), and belts register on the
    /// clock before any golem, so a golem sees this tick's belt state.
    /// </para>
    /// </summary>
    public sealed class SandboxWorld : IPlacedStationConfigurator
    {
        private static readonly GridCoordinateConverter UnitCells = new GridCoordinateConverter(new Vector2(1f, 1f));

        private readonly List<GolemEntity> _golems = new List<GolemEntity>();
        private readonly List<GolemConstructionStation> _stations = new List<GolemConstructionStation>();
        private GolemConstructionStation _stationTemplate;
        private bool _requireSteamPower;
        private string _stockpileBufferId = "FactoryStockpile";

        public SimulationClock Clock { get; } = new SimulationClock();
        public ConveyorSystem Conveyor { get; } = new ConveyorSystem();
        public SpatialEndpointRegistry Endpoints { get; } = new SpatialEndpointRegistry();
        public ResourceNodeRegistry Nodes { get; } = new ResourceNodeRegistry();
        public StorageBufferRegistry Buffers { get; } = new StorageBufferRegistry();
        public BeltNetwork Belts { get; } = new BeltNetwork();
        public GridMap Grid { get; } = new GridMap();
        public SteamNetwork Steam { get; } = new SteamNetwork();
        public FreightMastRegistry Masts { get; } = new FreightMastRegistry();
        public ClockTowerSiteHolder ClockTower { get; } = new ClockTowerSiteHolder();
        public NodeExtractorRegistry ExtractorCap { get; } = new NodeExtractorRegistry();
        public AssemblyBayStructure AssemblyBay { get; } = new AssemblyBayStructure();
        public BuildModeController Build { get; } = new BuildModeController();
        public FloorExpansionService FloorExpansion { get; } = new FloorExpansionService();

        /// <summary>The pack's cap ladder (docs/pack-upgrades-design.md): the Assembly Line tab's Pack row.</summary>
        public PackUpgradeService PackUpgrade { get; } = new PackUpgradeService();

        public FloorBounds Bounds { get; private set; } = new FloorBounds();
        public DefinitionSet Definitions { get; private set; }
        public SandboxSetup Setup { get; private set; }
        public TruckloadMarket Market { get; private set; }
        /// <summary>The build menu: every placeable the player may place.</summary>
        public IReadOnlyList<PlaceableEntry> Placeables { get; private set; } = new List<PlaceableEntry>();

        /// <summary>
        /// Every placeable the world knows how to draw: the menu's, and the fixtures (the Clock
        /// Tower), which stand on their own sites and are never in the menu (G10).
        /// </summary>
        public IReadOnlyList<PlaceableEntry> AllPlaceables { get; private set; } = new List<PlaceableEntry>();

        /// <summary>The placeables that are the world's, not the player's to place.</summary>
        public static readonly IReadOnlyCollection<string> FixtureKeys = new[] { ClockTowerKey };

        public const string ClockTowerKey = "ClockTowerPrefab";

        /// <summary>The Clock Tower, standing on its site in the town square; null in a world without one.</summary>
        public PlaceableBuilding ClockTowerBuilding { get; private set; }
        public List<ResourceNodeMarker> Markers { get; } = new List<ResourceNodeMarker>();
        public HandCrankBench StarterBench { get; private set; }
        public GolemConstructionStation StarterStation { get; private set; }

        /// <summary>
        /// Buildings the setup authored rather than the player placed: the starter bench and
        /// station. Unity's were scene objects carrying a PlaceableBuilding, which is how the
        /// player's [E] found them; here they are buildings for the same reason. They are NOT
        /// GridMap occupants and NOT in BuildModeController.Buildings, matching Unity -- a click
        /// with a placeable on an occupied tile demolishes, and these were never bought.
        /// </summary>
        public IReadOnlyList<PlaceableBuilding> AuthoredBuildings => _authoredBuildings;

        /// <summary>
        /// The Workbench's state and decisions (G7). Six sockets, as WorkbenchCanvas.prefab has.
        /// Ungated until the Assembly Line panel exists to claim cards from (G8) -- Sandbox.unity
        /// gates it (gateWorkbenchRoster: 1), but gating with no way to claim would leave the
        /// player with the two starting verbs and nothing else.
        /// </summary>
        public WorkbenchSession Workbench { get; } = new WorkbenchSession(WorkbenchSockets);

        public const int WorkbenchSockets = 6;

        /// <summary>The named-program library the Workbench's Patent button stamps into.</summary>
        public PatentRegistry Patents { get; } = new PatentRegistry();

        /// <summary>The player's hands: [E], hold-to-crank, [R], [G], the prompt. Wired to this world.</summary>
        public PlayerInteractor Interactor { get; } = new PlayerInteractor();

        private readonly List<PlaceableBuilding> _authoredBuildings = new List<PlaceableBuilding>();

        public string StockpileBufferId => _stockpileBufferId;

        /// <summary>
        /// The Pneumatic Post's shared store (docs/crates-design.md §10): every Post crate
        /// publishes this one buffer. A registry buffer like any other, saved by its id.
        /// </summary>
        public const string PostBufferId = "PneumaticPost";

        /// <summary>
        /// The player's wallet (docs/crates-design.md §4.4): the pack, which keeps the stockpile's
        /// id because saves name it, then the Post store. Every cost is charged through it;
        /// golems never reach the pack.
        /// </summary>
        public PlayerWallet Wallet => _wallet ??= new PlayerWallet(Buffers, _stockpileBufferId, PostBufferId);

        private PlayerWallet _wallet;

        /// <summary>Every live golem, whichever station built it.</summary>
        public IReadOnlyList<GolemEntity> Golems => _golems;

        /// <summary>The step-by-step guide (G10), when sandbox.json turns it on; else null.</summary>
        public Tutorial.TutorialGuide Tutorial { get; private set; }

        /// <summary>A station built a golem. The scene hosts a node for it.</summary>
        public event Action<GolemEntity> GolemSpawned;

        /// <summary>The wrecking bar took a golem. The scene frees its node.</summary>
        public event Action<GolemEntity> GolemDismantled;

        /// <summary>
        /// A world with only the belts and clock -- what the spike's slice and the older test
        /// scenes ran on. <see cref="Compose"/> builds the full Sandbox.
        /// </summary>
        public SandboxWorld(DefinitionSet definitions, int beltSegmentLengthTicks = 4, float ticksPerSecond = 10f)
        {
            Definitions = definitions;
            Audio = new AudioDirector(definitions != null && definitions.Tables.ContainsKey(typeof(AudioSlotDefinition))
                ? definitions.AudioSlots
                : null);
            Audio.Attach();
            Belts.Configure(Conveyor, Endpoints, beltSegmentLengthTicks);
            Clock.TicksPerSecond = ticksPerSecond;
            Clock.Register(Conveyor);
        }

        /// <summary>The full Sandbox: <paramref name="setup"/> applied, the build menu stocked.</summary>
        public static SandboxWorld Compose(
            DefinitionSet definitions, SandboxSetup setup, IReadOnlyList<PlaceableEntry> placeables,
            int beltSegmentLengthTicks = 4, float ticksPerSecond = 10f)
        {
            var world = new SandboxWorld(definitions, beltSegmentLengthTicks, ticksPerSecond);
            world.Apply(setup, placeables);
            return world;
        }

        private void Apply(SandboxSetup setup, IReadOnlyList<PlaceableEntry> placeables)
        {
            Setup = setup;
            AllPlaceables = placeables ?? new List<PlaceableEntry>();
            Placeables = AllPlaceables.Where(p => !FixtureKeys.Contains(p.Key)).ToList();
            _stockpileBufferId = setup.stockpileBufferId;
            _requireSteamPower = setup.requireSteamPower;
            Bounds = new FloorBounds(FloorLayout.HalfExtent, setup.startingNorthExtent);
            if (setup.beltCellsPerSecond > 0f)
            {
                // A cell is segmentLengthTicks progress units; at TicksPerSecond ticks a second.
                Conveyor.StepPerTick = setup.beltCellsPerSecond * Belts.SegmentLengthTicks / Clock.TicksPerSecond;
            }

            // SandboxBootstrap.Start, in its order.
            setup.ApplyBufferPolicy(Buffers);
            setup.RegisterNodes(Nodes);
            foreach (SandboxSetup.NodeEntry entry in setup.nodes)
            {
                var marker = new ResourceNodeMarker { Position = new Vector3(entry.x, entry.y, 0f) };
                marker.Configure(Nodes, entry.id);
                marker.RegisterAsSpatialEndpoint(Endpoints, UnitCells);
                Markers.Add(marker);
            }

            // Steam ticks on the simulation clock: Coke burns per unit of work, so the burn has
            // to follow Play/Pause and the speed multiplier.
            _steamTick = new TickAdapter(Steam.Tick);
            Clock.Register(_steamTick);

            FloorExpansion.Configure(Bounds, Buffers, _stockpileBufferId, PostBufferId);
            if (setup.floorExpansion != null)
            {
                FloorExpansion.ConfigureCost(setup.floorExpansion.purchases, setup.floorExpansion.rowsPerPurchase);
            }
            PackUpgrade.Configure(Buffers, _stockpileBufferId, PostBufferId, setup.packCapacityPerType, setup.packUpgrades);

            // Ticked whether or not a tower is built: an unbuilt tower accrues nothing.
            // EMPTY, as ManagerHolders.prefab's site holder was (stages: []): the site stays
            // dormant until a tower is BUILT, and the placed tower hands it the stages it carries
            // (PlaceableClockTower.RegisterAsSpatialEndpoint). Configured with every stage at
            // startup, the HUD said "Clock Tower starved of FrameSection" over an empty workshop.
            ClockTower.Configure(new ClockTowerStageDefinition[0]);
            ClockTower.Attach();
            Clock.Register(ClockTower);

            if (setup.starterBench != null)
            {
                var bench = new PlaceableBuilding { name = "StarterHandCrankBench", Cell = SandboxSetup.CellOf(setup.starterBench) };
                StarterBench = bench.AddPart(new HandCrankBench());
                StarterBench.Configure(Buffers, _stockpileBufferId, Definitions.Recipes.Values.OrderBy(r => r.name), PostBufferId);
                Clock.Register(StarterBench);
                _authoredBuildings.Add(bench);
            }

            Market = setup.BuildMarket(Nodes);
            Clock.Register(Market);

            WireBuildMode();
            RaiseClockTower();

            if (setup.starterStation != null)
            {
                var stationBuilding = new PlaceableBuilding
                {
                    name = "StarterConstructionStation",
                    Cell = SandboxSetup.CellOf(setup.starterStation),
                    Facing = SandboxSetup.FacingOf(setup.starterStation),
                };
                StarterStation = stationBuilding.AddPart(new GolemConstructionStation());
                _authoredBuildings.Add(stationBuilding);
                StarterStation.SetPlacement(SandboxSetup.CellOf(setup.starterStation), SandboxSetup.FacingOf(setup.starterStation));
                StarterStation.ConfigureBuildRoster(RosterFor(setup.starterStation.roster), () => new GolemEntity());
                AddStation(StarterStation);
            }

            WireInteractor();
            Interactor.GolemProblem = GolemProblem;
            WireWorkbench(setup.workbench);
            Alerts = new AlertsStrip(() => _golems) { Names = TileNames };
            Alerts.Attach();
            WireProgression(setup.assemblyLine);
            WireAudio();
            EventBus.ClockTowerStageCompleted += OnTowerStageCompleted;
            if (setup.tutorial)
            {
                Tutorial = new Tutorial.TutorialGuide(this);
                // The Workbench's next-drag hint follows the guide's program plans.
                Workbench.HintSource = Tutorial.PlanFor;
                if (AssemblyLine != null)
                {
                    AssemblyLine.Wanted = card => card != null && GolemFactory.Tutorial.TutorialGuide.CardKey(card) == Tutorial.WantedCardAppendage;
                }
                if (setup.playtest)
                {
                    Tutorial.AttachPlaytest(new Tutorial.PlaytestSession(), () => _realSeconds);
                }
            }
        }

        /// <summary>The tech tree's progress ledger, read by the Assembly Line's unlocks and the Ledger tab.</summary>
        public TechTreeProgressTracker TechTree { get; } = new TechTreeProgressTracker();

        /// <summary>The Assembly Line (§8): draft slots, claims, the cards waiting on prerequisites.</summary>
        public AssemblyLineState AssemblyLine { get; private set; }

        /// <summary>The Assembly Line tab's rows and actions.</summary>
        public AssemblyLineBoard AssemblyLineBoard { get; private set; }

        /// <summary>
        /// SandboxBootstrap.RegisterAssemblyLine and WireTechTree, in their order. THE UNLOCK
        /// CONTEXT IS WIRED BEFORE THE POOL IS SEEDED (root CLAUDE.md): seeding first makes every
        /// prerequisite unanswerable, which deliberately passes, and §8.3's gating goes inert. The
        /// context reads the tech tree's ledger, which only grows, so spending a good cannot
        /// re-lock a card.
        /// </summary>
        private void WireProgression(SandboxSetup.AssemblyLineSetup setup)
        {
            TechTree.Configure(Buffers, ClockTower.Site);
            TechTree.ConfigureWorld(() => _golems, InteractableBuildings);
            TechTree.ConfigureFloorBounds(Bounds);
            TechTree.Attach();
            Production.Attach();

            if (setup == null || string.IsNullOrEmpty(setup.deck) || !Definitions.Decks.TryGetValue(setup.deck, out DraftableCardCatalog deck))
            {
                return;
            }

            _deck = deck;
            _claimUserId = setup.claimUserId;
            AssemblyLine = new AssemblyLineState(setup.slots);
            AssemblyLine.ConfigureUnlockContext(itemType => TechTree.Ledger.HasItem(itemType));
            // The opening hand BEFORE the deck. The line skips a card its player already owns
            // only when it fills a slot, so seeding first filled all three slots with the
            // opening verbs -- free to claim, buying nothing -- and no real card appeared until
            // the player had cleared them. (Unity's SandboxBootstrap had the same order; fixed
            // in the port at the user's call.) The unlock context is still wired first.
            foreach (DraftableCardDefinition card in deck.OpeningHand)
            {
                AssemblyLine.GrantClaim(setup.claimUserId, card);
            }
            AssemblyLine.SeedCandidates(deck.Cards);
            if (setup.gateWorkbench)
            {
                Workbench.ConfigureCardGating(AssemblyLine, setup.claimUserId);
            }
            TechTree.ConfigureCardClaims(AssemblyLine, setup.claimUserId);

            // A Haul offers the goods the factory has made (G10): the ledger only grows, so a good
            // once offered stays offered.
            Workbench.HaulableGood = item => TechTree.Ledger.HasItem(item);

            AssemblyLineBoard = new AssemblyLineBoard(AssemblyLine, Buffers, _stockpileBufferId, setup.claimUserId, PostBufferId);
            AssemblyLineBoard.ConfigureBays(AssemblyBay, _stockpileBufferId);
            AssemblyLineBoard.ConfigureFloorExpansion(FloorExpansion);
            AssemblyLineBoard.ConfigurePackUpgrade(PackUpgrade);
            AssemblyLineBoard.ConfigureSeen(item => TechTree.Ledger.HasItem(item));
            AssemblyLineBoard.ConfigurePostShown(() => TechTree.Ledger.HasBuilding(TechTreeCatalog.BuildingPneumaticPost));
        }

        /// <summary>
        /// What the game sounds like (docs/audio-design.md): the director, and the player's
        /// moments it hears. The golems' work, red stalls and the loops it reads each frame in
        /// <see cref="Advance"/>; the Clock Tower's stages off the event bus.
        /// </summary>
        public AudioDirector Audio { get; }

        /// <summary>
        /// The camera's decisions (docs/game-feel-design.md §2–§3): zoom levels, the pan to a
        /// stopped golem, the Clock Tower's stage event. <c>CameraRigNode</c> follows it.
        /// </summary>
        public CameraDirector Camera { get; } = new CameraDirector();

        /// <summary>The alert strip was clicked: the camera goes to the next stopped golem. Its id, or null with none.</summary>
        public string FocusNextAlert()
        {
            Alerts?.Reconcile();
            return Camera.NextAlert(Alerts?.Tracker.StallsWorstFirst());
        }

        private void OnTowerStageCompleted(ClockTowerStageCompletedEvent e)
        {
            if (!Restoring)
            {
                Camera.BeginTowerEvent(e.StageNumber, e.StageName);
            }
        }

        private void WireAudio()
        {
            Workbench.CardSeated += () => Audio.Play(AudioRules.CardSnap);
            Workbench.LeverPulled += () => Audio.Play(AudioRules.Engage);
            Workbench.Patented += _ => Audio.Play(AudioRules.Patent);
            if (AssemblyLineBoard != null)
            {
                AssemblyLineBoard.Claimed += () => Audio.Play(AudioRules.Claim);
            }
            Build.BuildingPlaced += _ => Audio.Play(AudioRules.Place);
            Build.BuildingRemoved += _ => Audio.Play(AudioRules.Demolish);
            Interactor.Collected += () => Audio.Play(AudioRules.Collect);
            Inventory.Collected += () => Audio.Play(AudioRules.Collect);
        }

        private void WireWorkbench(SandboxSetup.WorkbenchRoster roster)
        {
            Workbench.ConfigurePatents(Patents);
            if (roster == null)
            {
                return;
            }
            Workbench.ConfigureRoster(
                roster.chassis.Select(n => Definitions.Chassis[n]).ToArray(),
                roster.logicCores.Select(n => Definitions.LogicCores[n]).ToArray(),
                roster.appendages.Select(n => Definitions.Appendages[n]).ToArray());
        }

        /// <summary>
        /// SandboxBootstrap's PlayerInteractor wiring. The screens (construction, Workbench,
        /// Management) are the scene's to hand over, through <see cref="PlayerInteractor.Configure"/>
        /// again once it has them; until then [E] at a station reports that no panel is available.
        /// </summary>
        private void WireInteractor()
        {
            Interactor.Configure(InteractRange, Buffers, _stockpileBufferId, null, null);
            Interactor.ConfigurePost(PostBufferId);
            Interactor.ConfigureAffordance(null);
            Interactor.ConfigureWorld(() => Markers, InteractableBuildings, () => _golems);
            Interactor.ConfigureMarket(Market, () => Clock.CurrentTick, Buffers, _stockpileBufferId);
            Interactor.ConfigureBuildMode(Build);
            Interactor.ConfigureGolemPlacement(Grid, new Vector2(1f, 1f));
            Interactor.ConfigureScreenCoordinator(Screens);
            Interactor.Attach();
        }

        /// <summary>
        /// Hands the player's [E] the screens it opens, once the scene has built them -- the
        /// construction panel for a station, the Workbench for a golem (G7) and the Management
        /// screen whose being open hides the prompt (G8). Null for one not built yet.
        /// </summary>
        // --- Save and load (G9) ---------------------------------------------------------------

        /// <summary>
        /// The SaveLoad tab's Save: the stockpile and every buffer, the patent library, every
        /// golem's program and placement, and every building the player placed. Returns the
        /// status line Unity's SaveLoadPanel printed.
        /// </summary>
        /// <summary>
        /// The Ledger's recipe pane (G10: on the world rather than the tab, so the guide can see a
        /// recipe was opened). The Godot Ledger tab draws this one.
        /// </summary>
        public TechTreeReadout LedgerReadout =>
            _ledgerReadout ??= new TechTreeReadout(Definitions.Recipes.Values.ToList(), Throughput, ProductionTally.SeriesId, Clock.TicksPerSecond);

        private TechTreeReadout _ledgerReadout;

        /// <summary>Saves and loads this session, for the guide's save/load steps.</summary>
        public int SavesMade { get; private set; }
        public int LoadsMade { get; private set; }

        /// <summary>
        /// Where the SaveLoad tab saves -- set by the Godot layer (user://save.json, globalized) --
        /// so the playtest kit's save step writes where the player's own Save does.
        /// </summary>
        public string DefaultSavePath { get; set; }

        public string SaveTo(string path)
        {
            SavesMade++;
            // The guide's Save step is done by saving, so it moves on BEFORE the save records its
            // place: otherwise a load lands back on Save, and saving again loops (the kit did).
            Tutorial?.Settle();
            List<GolemEntity> live = LiveGolems();
            SaveData data = SaveLoadService.CaptureState(Buffers, Patents, live, Build.Buildings);
            data.progress = CaptureProgress();
            data.market = CaptureMarket();
            SaveFileIO.WriteToFile(data, path);
            return $"Saved {live.Count} golems and {data.buildings.Count} buildings.";
        }

        /// <summary>
        /// The SaveLoad tab's Load: the built world first (cleared WITHOUT a refund -- a load
        /// replaces the factory, and refunding the swept buildings on top of the restored buffers
        /// would make save/load/save/load an infinite duplicator, root CLAUDE.md), then its
        /// inhabitants: a golem still standing gets its program back in place, a player-built one
        /// that is gone is rebuilt by the station, any other is reported as skipped. The status
        /// line reports what the load DID, not how many entries the file held.
        /// </summary>
        public string LoadFrom(string path)
        {
            LoadsMade++;
            SaveData data = SaveFileIO.ReadFromFile(path);
            if (data == null)
            {
                return "No save file found.";
            }

            // A load rebuilding the factory isn't the player placing and wrecking it: no sound,
            // and no landing or dust (Restoring).
            Audio.Suppressed = true;
            Restoring = true;
            try
            {
                return Restore(data);
            }
            finally
            {
                Audio.Suppressed = false;
                Restoring = false;
            }
        }

        /// <summary>True while a load rebuilds the world: buildings appear and go without their landing or dust.</summary>
        public bool Restoring { get; private set; }

        private string Restore(SaveData data)
        {

            RestoreProgress(data.progress);
            RestoreMarket(data.market);

            IGolemRespawner respawner = _stationTemplate != null ? new StationGolemRespawner(_stationTemplate) : null;
            SaveLoadService.RestoreReport report = SaveLoadService.RestoreState(
                data, Buffers, Patents, LiveGolems(), Definitions.ToCatalog(), respawner,
                new BuildModeBuildingRebuilder(Build), Wallet.PackId);
            Interactor.RefreshInteractables();

            // The guide last, once its golems are back: entering a step notes which golems were
            // already standing, and a role names a golem by id, so both want the loaded world.
            if (data.progress != null && Tutorial != null)
            {
                Tutorial.RestoreRoles(data.progress.tutorialRoles);
                Tutorial.Restore(data.progress.tutorialStepId, data.progress.tutorialStep, data.progress.tutorialDismissed);
            }

            string golems = report.Skipped > 0
                ? $"Loaded {report.Restored} golems, rebuilt {report.Respawned}, skipped {report.Skipped}"
                : $"Loaded {report.Restored} golems, rebuilt {report.Respawned}";
            string buildings = report.BuildingsSkipped > 0
                ? $"{report.BuildingsRebuilt} buildings, {report.BuildingsSkipped} skipped"
                : $"{report.BuildingsRebuilt} buildings";
            string status = golems + "; " + buildings + ".";

            // A save from before crates (crates-design §7): its stockpile is the pack now, kept
            // whole even above the cap, and its depots come back as empty crates. Said once, here
            // and at the player, so the empty crates read as a change rather than a loss.
            if (data.version < 1)
            {
                Interactor.Announce(CratesMigrationNotice);
                status += " " + CratesMigrationNotice;
            }
            return status;
        }

        public const string CratesMigrationNotice =
            "Crates are local now. Your depots start empty; everything you had is in your pack.";

        private DraftableCardCatalog _deck;
        private string _claimUserId;

        /// <summary>
        /// The stalls' stock and the carts on the road. A load restores them with the pack, so
        /// a truckload paid for before a save arrives after loading it, even on a fresh launch,
        /// and one ordered after the save does not arrive on top of its refunded price.
        /// </summary>
        private MarketEntry CaptureMarket()
        {
            var market = new MarketEntry();
            foreach (ResourceNode node in Nodes.Nodes)
            {
                if (node.RemainingQuantity != ResourceNode.Infinite)
                {
                    market.stallIds.Add(node.NodeId);
                    market.stallStocks.Add(node.RemainingQuantity);
                }
            }
            if (Market != null)
            {
                foreach ((string nodeId, int quantity, long ticksLeft) in Market.CartsOnTheRoad(Clock.CurrentTick))
                {
                    market.cartStallIds.Add(nodeId);
                    market.cartQuantities.Add(quantity);
                    market.cartTicksLeft.Add(ticksLeft);
                }
            }
            return market;
        }

        private void RestoreMarket(MarketEntry market)
        {
            if (market == null)
            {
                return; // a save from before the street was saved: leave it as it is
            }
            for (int i = 0; i < market.stallIds.Count && i < market.stallStocks.Count; i++)
            {
                if (Nodes.TryGetNode(market.stallIds[i], out ResourceNode node))
                {
                    node.RestoreQuantity(market.stallStocks[i]);
                }
            }
            int carts = System.Math.Min(market.cartStallIds.Count,
                System.Math.Min(market.cartQuantities.Count, market.cartTicksLeft.Count));
            Market?.RestoreCarts(Enumerable.Range(0, carts)
                .Select(i => (market.cartStallIds[i], market.cartQuantities[i], market.cartTicksLeft[i])), Clock.CurrentTick);
        }

        private ProgressEntry CaptureProgress()
        {
            var progress = new ProgressEntry
            {
                ledgerTowerStages = TechTree.Ledger.CompletedTowerStages,
                floorNorthExtent = Bounds.NorthExtent,
                assemblyBayTier = AssemblyBay.Tier,
                packUpgradeTier = PackUpgrade.Tier,
                clockSpeed = Clock.Speed,
                clockPaused = Clock.State == ClockState.Paused,
                tutorialStep = Tutorial?.Index ?? 0,
                tutorialStepId = Tutorial?.CurrentStepId,
                tutorialDismissed = Tutorial?.Dismissed ?? false,
                clockTowerStageIndex = ClockTower.Site.StageIndex,
                clockTowerProgressUnits = ClockTower.Site.ProgressUnits,
                clockTowerComplete = ClockTower.Site.IsComplete,
            };
            progress.ledgerItems.AddRange(TechTree.Ledger.Items.OrderBy(x => x));
            progress.ledgerChassis.AddRange(TechTree.Ledger.Chassis.OrderBy(x => x));
            progress.ledgerBuildings.AddRange(TechTree.Ledger.Buildings.OrderBy(x => x));
            progress.ledgerCards.AddRange(TechTree.Ledger.ClaimedCards.OrderBy(x => x));
            if (AssemblyLine != null)
            {
                progress.claimedCards.AddRange(AssemblyLine.GetClaimedCards(_claimUserId).Select(c => c.name));
            }
            if (Tutorial != null)
            {
                progress.tutorialRoles.AddRange(Tutorial.RoleEntries);
            }
            return progress;
        }

        /// <summary>
        /// Restores progress BEFORE the world: the ledger first (the Assembly Line's unlock
        /// context reads it), then the line, the bay tier (respawned golems take bay slots) and
        /// the floor (a building beyond the original back wall needs its row to rebuild on).
        /// </summary>
        private void RestoreProgress(ProgressEntry progress)
        {
            if (progress == null)
            {
                return; // a save from before progress was saved: leave it as it is
            }

            TechTreeProgressLedger ledger = TechTree.Ledger;
            ledger.Clear();
            progress.ledgerItems.ForEach(x => ledger.RecordItem(x));
            progress.ledgerChassis.ForEach(x => ledger.RecordChassis(x));
            progress.ledgerBuildings.ForEach(x => ledger.RecordBuilding(x));
            progress.ledgerCards.ForEach(x => ledger.RecordClaimedCard(x));
            ledger.RecordCompletedTowerStages(progress.ledgerTowerStages);

            if (AssemblyLine != null)
            {
                var byName = _deck.Cards.Concat(_deck.OpeningHand).Where(c => c != null)
                    .GroupBy(c => c.name).ToDictionary(g => g.Key, g => g.First());
                AssemblyLine.Restore(
                    _claimUserId,
                    progress.claimedCards.Where(byName.ContainsKey).Select(n => byName[n]),
                    _deck.Cards);
            }

            AssemblyBay.RestoreTier(progress.assemblyBayTier);
            FloorExpansion.Restore(progress.floorNorthExtent);
            PackUpgrade.Restore(progress.packUpgradeTier);

            // The tower on its site (G10), a fixture the building list never holds.
            if (ClockTowerBuilding != null)
            {
                // From a clean site: the windows, meters and armed alarms are the session being
                // abandoned, and kept, they credited the loaded tower with goods never delivered
                // in its timeline (from review).
                ClockTower.Site.Reset();
                ClockTower.Site.RestoreProgress(
                    progress.clockTowerStageIndex, progress.clockTowerProgressUnits, progress.clockTowerComplete);
            }


            Clock.Speed = progress.clockSpeed > 0f ? progress.clockSpeed : 1f;
            if (progress.clockPaused)
            {
                Clock.Pause();
            }
            else
            {
                Clock.Play();
            }
        }

        private List<GolemEntity> LiveGolems() => _golems.Where(g => g != null && !g.IsRemoved).ToList();

        /// <summary>
        /// Adds a golem no station built -- a scene-authored one, as Unity's Main.unity demos and
        /// LoopSlice's hand-placed golems are -- to the world's roster, so it is saved, loaded,
        /// reachable by [E] and reported by the alerts strip like any other.
        /// </summary>
        public void AdoptGolem(GolemEntity golem)
        {
            if (golem != null && !_golems.Contains(golem))
            {
                _golems.Add(golem);
                Interactor.RefreshInteractables();
            }
        }

        public void ConfigureScreens(IConstructionScreen construction, IWorkbenchScreen workbench, IScreen management)
        {
            ConstructionScreen = construction ?? ConstructionScreen;
            WorkbenchScreen = workbench ?? WorkbenchScreen;
            ManagementScreen = management ?? ManagementScreen;
            Interactor.Configure(InteractRange, Buffers, _stockpileBufferId, ConstructionScreen, WorkbenchScreen);
            Interactor.ConfigureAffordance(ManagementScreen);
        }

        /// <summary>
        /// Hands [I] the Pack and Crate screens (docs/inventory-screens-design.md §2), as
        /// <see cref="ConfigureScreens"/> does the others. Null keeps one already handed over.
        /// </summary>
        public void ConfigureInventoryScreens(IPackScreen pack, ICrateScreen crate) =>
            Interactor.ConfigureInventory(pack, crate);

        /// <summary>
        /// What the Pack and Crate screens show and what their clicks do: rows, transfers through
        /// <see cref="CrateTransfer"/>, and the labels a crate is offered (every good the ledger has seen).
        /// </summary>
        public CrateScreenReadout Inventory =>
            _inventory ??= new CrateScreenReadout(Buffers, _stockpileBufferId, () => TechTree.Ledger.Items, PostBufferId);

        private CrateScreenReadout _inventory;

        /// <summary>
        /// The screens handed over so far. Each screen registers itself (passing null for the
        /// others keeps theirs), so they can be built in any order and still find each other --
        /// the construction panel opens the Workbench on a golem it just built.
        /// </summary>
        public IConstructionScreen ConstructionScreen { get; private set; }
        public IWorkbenchScreen WorkbenchScreen { get; private set; }
        public IScreen ManagementScreen { get; private set; }

        /// <summary>Unity PlayerInteractor's _interactRange in Sandbox.unity, in cells.</summary>
        public const float InteractRange = 1.5f;

        /// <summary>Everything [E] may reach: what the player built, and what the setup authored.</summary>
        public IEnumerable<PlaceableBuilding> InteractableBuildings() => Build.Buildings.Concat(_authoredBuildings);

        /// <summary>
        /// The Clock Tower on its fixed site in the town square (G10). A fixture: not in the
        /// build menu, not the wrecking bar's, and never in a save's building list (it is not
        /// runtime-placed), so a load finds it where it always stands. Every footprint cell takes
        /// deliveries, so a golem can feed it from any side; until the factory has built a
        /// Zeppelin, none of them will.
        /// </summary>
        private void RaiseClockTower()
        {
            PlaceableEntry entry = AllPlaceables.FirstOrDefault(p => p.Key == ClockTowerKey);
            if (entry == null)
            {
                return;
            }

            PlaceableBuilding tower = entry.Prefab.Instantiate();
            Vector2Int centre = TownSquare.TowerCentre;
            Build.RegisterFixture(tower, centre, TownSquare.TowerCells());
            PlaceableClockTower part = tower.GetPart<PlaceableClockTower>();
            if (part != null && part.RegisterAsSpatialEndpoint(Endpoints, ClockTower.Site, centre))
            {
                foreach (Vector2Int cell in TownSquare.TowerCells().Where(c => c != centre))
                {
                    Endpoints.Register(cell, part.Endpoint);
                }
            }

            ClockTower.Site.OpenWhen = () => TechTree.Ledger.HasChassis(ZeppelinChassis);
            ClockTower.Site.ClosedReason = "roped off until you build a Zeppelin";
            ClockTowerBuilding = tower;
            _authoredBuildings.Add(tower);
        }

        /// <summary>What opens the tower's site: the factory has built one of these.</summary>
        public const string ZeppelinChassis = Progression.TechTreeCatalog.ChassisZeppelin;

        private void WireBuildMode()
        {
            Build.Configure(Grid, null);
            Build.ConfigureEconomy(Buffers, _stockpileBufferId, Placeables.Select(p => p.Prefab).ToArray(), PostBufferId);
            Build.ConfigureBelts(Belts, Endpoints, Conveyor);
            // "I can walk there" and "I can build there" are one boundary (§3.3): the same live
            // bounds object the player's clamp reads, so new floor is buildable the moment it is
            // walkable.
            Build.ConfigurePlacementBounds(Bounds, FloorLayout.StreetDepth);
            Build.ConfigureSteam(Steam);
            Build.ConfigureFreight(Masts);
            Build.ConfigureClockTower(ClockTower.Site);
            Build.ConfigureStationWiring(this);
            Build.ConfigureGolemRoster(() => _golems);
            Build.BuildingPlaced += OnBuildingPlaced;
            Build.BuildingRemoved += OnBuildingRemoved;
        }

        /// <summary>A roster field: chassis names, comma-separated, or "all".</summary>
        private ChassisDefinition[] RosterFor(string roster)
        {
            List<string> list = (roster ?? "").Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
            return list.Contains("all")
                ? Definitions.Chassis.Values.OrderBy(c => c.maxAppendageSlots).ThenBy(c => c.name).ToArray()
                : list.Select(n => Definitions.Chassis[n]).ToArray();
        }

        /// <summary>Wires <paramref name="station"/> into the world and starts hosting its golems.</summary>
        public void AddStation(GolemConstructionStation station)
        {
            if (_stations.Contains(station))
            {
                return;
            }
            _stations.Add(station);
            station.GolemSpawned += OnGolemSpawned;
            station.GolemDismantled += OnGolemDismantled;
            ConfigureStation(station);

            // The first station that can build anything is the template a placed one copies,
            // and the dismantler the wrecking bar asks: birth and death in one place.
            if (_stationTemplate == null && station.HasBuildRoster)
            {
                _stationTemplate = station;
                Build.ConfigureGolemDismantling(station);
            }
        }

        /// <summary>
        /// The ONE definition of "a construction station, fully wired" -- SandboxBootstrap's, for
        /// the starter station and for every one the player places. Only ever FILLS IN a roster,
        /// never overwrites one.
        /// </summary>
        public bool ConfigureStation(GolemConstructionStation station)
        {
            if (station == null)
            {
                return false;
            }

            if (!station.HasBuildRoster && _stationTemplate != null && _stationTemplate != station)
            {
                station.ConfigureBuildRoster(_stationTemplate.ChassisRoster, _stationTemplate.GolemSource);
            }

            // The Workbench session is the target a new golem is handed to, as Unity's station
            // retargeted the WorkbenchController on spawn.
            station.ConfigureSceneServices(Conveyor, Nodes, Buffers, Clock, Workbench, _stockpileBufferId, PostBufferId);
            station.ConfigureSpatial(Endpoints, Grid);

            // THE ONE LINE THE SWITCH GATES: handing a station the steam network is what makes
            // its golems steam-gated.
            if (_requireSteamPower)
            {
                station.ConfigureSteam(Steam);
            }

            // Not behind a switch: neither cap consumes anything or can soft-lock.
            station.ConfigureNodeExtractorCap(ExtractorCap);
            station.ConfigureAssemblyBay(AssemblyBay);
            station.ConfigureFreight(Masts);
            return station.HasBuildRoster;
        }

        private void OnBuildingPlaced(PlaceableBuilding building)
        {
            InvalidateStallFacts();
            // BuildModeController already asked ConfigureStation through the seam; hosting the
            // station's golems is the part only the world can do.
            GolemConstructionStation station = building.GetPart<GolemConstructionStation>();
            if (station != null)
            {
                AddStation(station);
            }

            // A bench from the build menu is a clone of the catalog's prefab, which has no
            // registry: wired here to the wallet and the clock, as the starter bench is in
            // Compose, or it never crafts (found in the Phase 1 critique; older than crates).
            HandCrankBench bench = building.GetPart<HandCrankBench>();
            if (bench != null)
            {
                bench.ConfigureStockpile(Buffers, _stockpileBufferId, PostBufferId);
                Clock.Register(bench);
            }
        }

        private void OnBuildingRemoved(PlaceableBuilding building)
        {
            InvalidateStallFacts();
            HandCrankBench bench = building.GetPart<HandCrankBench>();
            if (bench != null)
            {
                bench.IsCranking = false;
                Clock.Unregister(bench);
            }
        }

        /// <summary>
        /// What a stalled golem's neighbours look like: the facts that tell waiting (grey) from
        /// stuck (red) (docs/onboarding-feedback-design.md §3.2).
        /// </summary>
        public StallFactsProbe StallFacts =>
            _stallFacts ??= new StallFactsProbe(() => _golems, Endpoints, Belts, () => Market) { Frame = () => PresentationFrame };

        private StallFactsProbe _stallFacts;

        /// <summary>
        /// Moves on once a frame, after the frame's ticks, and whenever a building or a golem
        /// comes or goes: the stall-facts probe keeps its indices until it moves, or until a
        /// golem moves, turns, is carried or changes whether it runs
        /// (docs/performance-report-template.md §5.2 step 2).
        /// </summary>
        public long PresentationFrame { get; private set; }

        /// <summary>Says the world changed outside a frame's ticks, so this frame's stall facts are stale.</summary>
        public void InvalidateStallFacts() => PresentationFrame++;

        /// <summary>
        /// The endgame profile's sampler (docs/performance-report-template.md §3): null, the
        /// default, unless a perf run sets it, so the game pays a null check. Setting it reaches
        /// the clock (each tick, by registrant), the probe, every golem's mood, the alerts strip
        /// and the hover card.
        /// </summary>
        public PerfSampler Perf
        {
            get => _perf;
            set
            {
                _perf = value;
                Clock.Perf = value;
                Clock.PerfGroup = value != null ? PerfGroupOf : (Func<Simulation.ITickable, string>)null;
                StallFacts.Perf = value;
                if (Alerts != null)
                {
                    Alerts.Perf = value;
                }
            }
        }

        private PerfSampler _perf;

        private string PerfGroupOf(Simulation.ITickable tickable)
        {
            switch (tickable)
            {
                case GolemEntity _: return PerfSampler.TickGolems;
                case ConveyorSystem _: return PerfSampler.TickBelts;
                default: return tickable == _steamTick ? PerfSampler.TickSteam : PerfSampler.TickOther;
            }
        }

        /// <summary>Which golem the mouse is over (onboarding-feedback-design §4.1).</summary>
        public GolemInspector Inspector => _inspector ??= new GolemInspector(() => _golems);

        private GolemInspector _inspector;

        /// <summary>What the player calls the thing on a tile: "Crate (3,4)", "Scrap stall".</summary>
        public TileNames TileNames => _tileNames ??= new TileNames(Endpoints, Masts);

        private TileNames _tileNames;

        /// <summary>
        /// The hover card for <paramref name="golem"/> drawn in <paramref name="mood"/>
        /// (onboarding-feedback-design §4.3).
        /// </summary>
        /// <remarks>
        /// A waiting golem whose source is fed names its feeders (§17.4); they are found here, for
        /// the one hovered golem, never per frame for every golem (§17.2 invariant 3).
        /// </remarks>
        public HoverCard HoverCard(GolemEntity golem, GolemMood mood, bool strainingHeld = false)
        {
            using (Perf.Measure(PerfSampler.Hover))
            {
                GolemFactory.Golems.StallFacts facts = golem?.StallFacts ?? default;
                IReadOnlyList<GolemEntity> feeders = GolemHoverText.NamesFeeders(mood, facts) ? StallFacts.FeedersOf(golem) : null;
                return GolemHoverText.Compose(golem, mood, facts, TileNames, strainingHeld, feeders);
            }
        }

        /// <summary>
        /// The problem a stopped golem's [E] prompt carries (§11 question 5): the hover card's
        /// state line, or null for a golem that hasn't stopped.
        /// </summary>
        public string GolemProblem(GolemEntity golem) =>
            golem != null && GolemMoodRules.IsStopped(golem.Mood) ? HoverCard(golem, golem.Mood).State : null;

        private void OnGolemSpawned(GolemEntity golem)
        {
            if (!_golems.Contains(golem))
            {
                _golems.Add(golem);
            }

            // Spawned and respawned alike (the respawner builds through the station), so a
            // golem's birth stays in one file and every one the world hosts reads grey or red.
            InvalidateStallFacts();
            golem.ConfigureStallFacts(StallFacts.Read);
            golem.ConfigurePerf(() => _perf);

            // The station publishes WorldInteractablesChanged from inside SpawnGolem, BEFORE it
            // raises GolemSpawned -- so the interactor's re-snapshot ran against a roster one
            // golem short. Unity's snapshot was a scene scan, which already saw the new object;
            // a roster kept by events has to refresh again once it has actually changed. Found
            // by the `interact` scenario: [G] next to a fresh golem said "no golem in range".
            Interactor.RefreshInteractables();
            GolemSpawned?.Invoke(golem);
        }

        private void OnGolemDismantled(GolemEntity golem)
        {
            InvalidateStallFacts();
            _golems.Remove(golem);
            Interactor.RefreshInteractables(); // the same ordering, the other way round
            GolemDismantled?.Invoke(golem);
        }

        /// <summary>Advances the simulation by real seconds (WorldNode calls this every frame).</summary>
        public void Advance(float seconds)
        {
            Clock.Advance(seconds);
            InvalidateStallFacts();

            // Unity's BufferThroughputMonitor and AlertsPanel ran on Update in real time, not on
            // simulation ticks: the rate column reads "per minute of the player's time", and
            // the strip must keep reconciling while the clock is paused.
            _realSeconds += seconds;
            if (_realSeconds >= _nextThroughputSample)
            {
                _nextThroughputSample = _realSeconds + BufferRateTracker.DefaultSampleIntervalSeconds;
                Throughput.Sample(_realSeconds, Buffers);
                Production.Sample(_realSeconds, Throughput);
            }
            Alerts?.Update(seconds);
            AssemblyLine?.Tick(seconds);
            TechTree.Update(_realSeconds);
            Tutorial?.Update();
            Audio.Update(seconds, Clock, _golems, Steam.LastEvaluatedWorkingCount);
        }

        private float _realSeconds;
        private float _nextThroughputSample;
        private TickAdapter _steamTick;

        /// <summary>The Inventory tab's rate history (Unity's BufferThroughputMonitor), sampled in real time.</summary>
        public BufferRateTracker Throughput { get; } = new BufferRateTracker();

        /// <summary>
        /// Every good golems have assembled, sampled into <see cref="Throughput"/> beside the
        /// buffers: the Ledger's live rate (docs/crates-design.md §4.5).
        /// </summary>
        public ProductionTally Production { get; } = new ProductionTally();

        /// <summary>The HUD's alerts strip. Null on the bare (slice) world.</summary>
        public AlertsStrip Alerts { get; private set; }

        /// <summary>The full screens, one up at a time.</summary>
        public ScreenCoordinator Screens { get; } = new ScreenCoordinator();

        private sealed class TickAdapter : ITickable
        {
            private readonly Action<long> _tick;
            public TickAdapter(Action<long> tick) => _tick = tick;
            public void Tick(long currentTick) => _tick(currentTick);
        }
    }
}
