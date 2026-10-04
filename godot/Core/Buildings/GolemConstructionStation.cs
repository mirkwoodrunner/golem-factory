using System;
using System.Collections.Generic;
using GolemFactory.Belts;
using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.Simulation;
using GolemFactory.Steam;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Buildings
{
    // Spends ChassisDefinition.cost -- an item bundle since §1.5 (docs/progression-design.md §6,
    // §11 item 8), replacing the Scrap/Brass pair that could not express a single §6 cost from
    // the Presser on -- to spawn a bare-chassis GolemEntity and hand it straight to the
    // Workbench so the player programs it exactly like any other golem.
    //
    // PORTED FROM Unity's MonoBehaviour of the same name (milestone G2 of
    // docs/godot-conversion-plan.md). The rules are unchanged; the engine glue became:
    //   * the golem PREFAB  -> a golem FACTORY (Func<GolemEntity>). Its presence still means
    //     "this station can build" (HasBuildRoster); the scene a golem lives in is the host's
    //     business, which it learns from GolemSpawned.
    //   * transform / GetComponent<PlaceableBuilding>() -> Cell and StationFacing, set by
    //     whoever places the station (SetPlacement).
    //   * Destroy(golem.gameObject) -> golem.Remove(), then GolemDismantled for the host.
    //   * SimulationClockRunner / WorkbenchController / the Holders -> the plain objects they
    //     owned, and IWorkbenchTarget.
    public sealed class GolemConstructionStation : IGolemDismantler, IBuildingPart
    {
        private ChassisDefinition[] chassisRoster = new ChassisDefinition[0];
        private Func<GolemEntity> golemSource;
        private ConveyorSystem conveyor;
        private ResourceNodeRegistry nodeRegistry;
        private StorageBufferRegistry bufferRegistry;
        private SimulationClock clock;
        private IWorkbenchTarget workbench;
        private string stockpileBufferId = "FactoryStockpile";

        // Facing-based spatial routing. Optional in exactly the same way GolemEntity's own
        // spatial registry is: leave these unassigned and constructed golems route purely by
        // the ids on their appendage cards, as before.
        private SpatialEndpointRegistry spatialEndpoints;
        private GridMap gridMap;

        // Steam power (docs/progression-design.md §3.1). Optional in exactly the same way the
        // spatial registry above is: a station with no steam network builds golems that are
        // EXEMPT from the NoSteam precondition.
        private SteamNetwork steamNetwork;

        // The 2-extractor-per-node cap (docs/progression-design.md §3.2). Optional in the same
        // way: a station with no cap registry builds golems that may work any node they face.
        private NodeExtractorRegistry nodeExtractors;

        // §8's concurrent-golem cap. Optional in exactly the same additive way: a station with
        // no bay builds without a limit.
        private AssemblyBayStructure assemblyBay;

        // §6's Freight Link. Optional like every registry above it.
        private FreightMastRegistry mastRegistry;

        private int _nextGolemNumber = 1;

        /// <summary>
        /// Raised once a golem is fully wired -- configured, registered with the clock, and (on
        /// a build) assigned its bay slot. The host gives it a scene presence here; in Unity
        /// that presence was the instantiated prefab itself.
        /// </summary>
        public event Action<GolemEntity> GolemSpawned;

        /// <summary>Raised after a golem is dismantled and removed; the host frees its Node.</summary>
        public event Action<GolemEntity> GolemDismantled;

        /// <summary>
        /// Why the last <see cref="TryConstructGolem"/> refused, when the reason was not the
        /// cost. Empty otherwise. Exists because "you are out of bay slots" and "you cannot
        /// afford this" need different actions from the player, and the panel previously read
        /// every refusal as a shortfall -- which would have printed "needs 0 more Scrap" at a
        /// player whose stockpile was full.
        /// </summary>
        public string LastRefusalReason { get; private set; } = "";

        /// <summary>The bay this station's golems occupy, or null when uncapped.</summary>
        public AssemblyBayStructure AssemblyBay => assemblyBay;

        /// <summary>
        /// Subjects every golem this station builds to §8's bay cap. Split out for the same
        /// reason ConfigureSteam is -- a station that never gets one keeps building without a
        /// limit.
        /// </summary>
        public void ConfigureAssemblyBay(AssemblyBayStructure bay) => assemblyBay = bay;

        public void ConfigureFreight(FreightMastRegistry masts) => mastRegistry = masts;

        public ChassisDefinition[] ChassisRoster => chassisRoster;

        /// <summary>What this station builds golems with. Exposed so a station the player just
        /// placed can be handed the same factory the authored one uses.</summary>
        public Func<GolemEntity> GolemSource => golemSource;

        /// <summary>The cell this station stands on. Was its transform's position in Unity.</summary>
        public Vector2Int Cell { get; private set; }

        /// <summary>
        /// Which way this station points, and therefore the tile its golem steps out onto and
        /// the direction that golem starts facing. The player orients it with R at placement.
        /// </summary>
        public Facing StationFacing { get; private set; } = Facing.North;

        /// <summary>
        /// A station for a newly placed building -- what Unity's Instantiate did to the
        /// station component on a prefab. Everything the prefab was authored or wired with
        /// carries over (roster, golem source, services); runtime state does not: the golem
        /// counter restarts, nobody is subscribed yet, and placement is set by build mode.
        /// </summary>
        public IBuildingPart CloneForInstance()
        {
            var clone = (GolemConstructionStation)MemberwiseClone();
            clone.GolemSpawned = null;
            clone.GolemDismantled = null;
            clone._nextGolemNumber = 1;
            clone.LastRefusalReason = "";
            clone.Cell = Vector2Int.zero;
            clone.StationFacing = Facing.North;
            return clone;
        }

        /// <summary>Where the station stands and which way it points. Unity read both off the
        /// sibling PlaceableBuilding and the transform.</summary>
        public void SetPlacement(Vector2Int cell, Facing facing)
        {
            Cell = cell;
            StationFacing = facing;
        }

        // Test/bootstrap-friendly setup mirroring GolemEntity.Configure/ConfigureEconomy.
        public void Configure(
            ChassisDefinition[] roster, Func<GolemEntity> source, ConveyorSystem conveyorSystem,
            ResourceNodeRegistry nodes, StorageBufferRegistry buffers,
            SimulationClock simulationClock, IWorkbenchTarget workbenchTarget, string bufferId)
        {
            chassisRoster = roster ?? new ChassisDefinition[0];
            golemSource = source;
            conveyor = conveyorSystem;
            nodeRegistry = nodes;
            bufferRegistry = buffers;
            clock = simulationClock;
            workbench = workbenchTarget;
            stockpileBufferId = bufferId;
        }

        /// <summary>
        /// The asset half of a station's wiring: what it can build, and what it builds them
        /// from. Split out of <see cref="Configure"/> so a station that came into the world
        /// after the scene's setup ran (one the player placed, one a save rebuilt) can be given
        /// a roster without also being handed a fresh set of scene services.
        /// </summary>
        public void ConfigureBuildRoster(ChassisDefinition[] roster, Func<GolemEntity> source)
        {
            chassisRoster = roster ?? new ChassisDefinition[0];
            golemSource = source;
        }

        /// <summary>
        /// The scene half: the registries, clock and Workbench a station needs, and nothing
        /// else. DELIBERATELY DOES NOT TOUCH the roster or the golem factory -- an authored
        /// station already carries those, and overwriting them from a sweep would let one
        /// unconfigured caller blank a station that was working.
        /// </summary>
        public void ConfigureSceneServices(
            ConveyorSystem conveyorSystem, ResourceNodeRegistry nodes,
            StorageBufferRegistry buffers, SimulationClock simulationClock,
            IWorkbenchTarget workbenchTarget, string bufferId)
        {
            conveyor = conveyorSystem;
            nodeRegistry = nodes;
            bufferRegistry = buffers;
            clock = simulationClock;
            workbench = workbenchTarget;
            if (!string.IsNullOrEmpty(bufferId))
            {
                stockpileBufferId = bufferId;
            }
        }

        /// <summary>
        /// Whether this station could build anything if asked. False on a freshly placed
        /// station, whose roster and golem factory are both empty -- which is exactly the state
        /// that made a player-built station a decorative box.
        /// </summary>
        public bool HasBuildRoster =>
            golemSource != null && chassisRoster != null && chassisRoster.Length > 0;

        /// <summary>
        /// Turns on facing-based routing for every golem this station builds. Split out from
        /// Configure for the same reason ConfigureEconomy is -- a station with no spatial
        /// registry keeps producing purely id-routed golems.
        /// </summary>
        public void ConfigureSpatial(SpatialEndpointRegistry endpoints, GridMap map)
        {
            spatialEndpoints = endpoints;
            gridMap = map;
        }

        /// <summary>
        /// Steam-gates every golem this station builds from here on. Split out for the same
        /// reason ConfigureSpatial is.
        /// </summary>
        public void ConfigureSteam(SteamNetwork steam) => steamNetwork = steam;

        /// <summary>
        /// Subjects every golem this station builds to §3.2's two-extractor-per-node cap.
        ///
        /// <para>
        /// UNLIKE steam, this is NOT behind a scene switch. The cap needs no consumable and
        /// cannot soft-lock: a refused golem stalls naming the seam and is fixed by walking it
        /// somewhere else.
        /// </para>
        /// </summary>
        public void ConfigureNodeExtractorCap(NodeExtractorRegistry cap) => nodeExtractors = cap;

        /// <summary>
        /// Current Scrap/Brass in the buffer this station spends from. Returns false when no
        /// buffer registry is wired, so the panel can say "stockpile unavailable" rather than
        /// silently printing a confident zero.
        ///
        /// <para>
        /// Kept on the Scrap/Brass pair even though costs are now bundles: this feeds the
        /// panel's one-line "what's in the vault" readout, which is a summary of the two goods
        /// the player hand-harvests, not a cost preview. Affordability goes through
        /// <see cref="StockOf"/> instead.
        /// </para>
        /// </summary>
        public bool TryGetStockpile(out int scrapStock, out int brassStock)
        {
            scrapStock = 0;
            brassStock = 0;
            if (bufferRegistry == null ||
                !bufferRegistry.TryGetBuffer(stockpileBufferId, out StorageBuffer buffer))
            {
                return false;
            }

            scrapStock = buffer.GetQuantity(ItemType.Scrap);
            brassStock = buffer.GetQuantity(ItemType.Brass);
            return true;
        }

        /// <summary>
        /// How much of one good the station's stockpile holds -- the stock reader
        /// ConstructionCostPolicy's bundle arithmetic takes. Zero when nothing is wired, which
        /// makes an unwired station read as "you have none of anything", the same answer the
        /// withdrawal would give.
        /// </summary>
        public int StockOf(string itemType) =>
            bufferRegistry == null ? 0 : bufferRegistry.GetQuantity(stockpileBufferId, itemType);

        /// <summary>
        /// Whether <see cref="TryConstructGolem"/> would currently succeed on cost grounds.
        /// Routed through ConstructionCostPolicy -- the same arithmetic the panel prints -- so
        /// the preview and the actual withdrawal can never disagree. A missing buffer registry
        /// reads as unaffordable, matching TryConstructGolem's own early-out.
        /// </summary>
        public bool CanAfford(ChassisDefinition chassis)
        {
            if (chassis == null || bufferRegistry == null)
            {
                return false;
            }

            // A buffer that has never been deposited into doesn't exist yet, but a genuinely
            // free chassis is still affordable against it -- the same zero-cost case
            // StorageBufferRegistry.TryWithdrawBundle guards, and StockOf answers 0 for it
            // rather than failing.
            return ConstructionCostPolicy.CanAfford(StockOf, chassis.cost);
        }

        // Withdraws the chassis's cost, creates a bare-chassis golem (no logic core or
        // appendages yet -- the player fits those via the Workbench, matching "feed resources
        // to build golem parts"), registers it with the clock so it sits Idle until
        // programmed, and retargets the Workbench onto it immediately.
        public bool TryConstructGolem(ChassisDefinition chassis, out GolemEntity golem)
        {
            golem = null;
            LastRefusalReason = "";
            if (chassis == null || golemSource == null || bufferRegistry == null)
            {
                return false;
            }

            // §8: Assembly Bays cap concurrent golems. CHECKED BEFORE THE COST, so a refused
            // build never touches the stockpile -- charging four goods and then discovering
            // there is nowhere to put the golem would be the partial-charge bug the bundle
            // withdrawal exists to prevent, one level up.
            if (assemblyBay != null && !assemblyBay.HasFreeSlot)
            {
                LastRefusalReason =
                    $"All {assemblyBay.MaxGolemSlots} assembly bays are full. " +
                    "Upgrade the bays, or dismantle a golem.";
                return false;
            }

            // Atomic with a full refund on shortfall -- a chassis is the most expensive thing
            // the player buys and a partial charge would take four goods and hand back nothing.
            if (!bufferRegistry.TryWithdrawBundle(stockpileBufferId, chassis.cost))
            {
                return false;
            }

            Vector2Int spawnCell = ResolveConstructedGolemCell(out Facing spawnFacing);
            golem = SpawnGolem(chassis, NextGolemId(), spawnCell, spawnFacing);

            // Takes the slot the check above reserved. Assigned after the golem exists rather
            // than before, so a spawn that somehow failed cannot leave a bay slot held by
            // nothing -- the bay counts golems, and there was no golem to count.
            if (assemblyBay != null)
            {
                assemblyBay.TryAssignGolem(golem);
            }

            workbench?.RetargetGolem(golem);
            GolemSpawned?.Invoke(golem);
            return true;
        }

        /// <summary>
        /// Rebuilds a golem a save file describes but the world no longer contains, standing on
        /// the cell it was saved on and facing the way it was saved facing.
        ///
        /// <para>
        /// TWO THINGS THIS DELIBERATELY DOES NOT DO, both of which <see cref="TryConstructGolem"/>
        /// does. It does not charge the chassis cost -- the player paid for this golem in the
        /// session that built it, and charging again would make loading a game a tax, or fail
        /// outright for a player who has since spent their stockpile. And it does not retarget
        /// the Workbench: loading a factory of nine golems would otherwise leave the programming
        /// screen pointed at whichever one happened to be last in the file.
        /// </para>
        ///
        /// <para>
        /// Everything else is the same wiring in the same order, because it goes through the same
        /// <c>SpawnGolem</c>. The order is load-bearing and documented there.
        /// </para>
        /// </summary>
        public bool TryRespawnGolem(
            string golemId, ChassisDefinition chassis, Vector2Int savedCell, Facing savedFacing,
            out GolemEntity golem)
        {
            golem = null;
            if (chassis == null || golemSource == null || string.IsNullOrEmpty(golemId))
            {
                return false;
            }

            // Its own cell, not a resolved spawn tile: a loaded factory has to come back the
            // shape it was saved in. The walk past blocked neighbours is for a NEW golem stepping
            // out of a station door, and applying it here would shuffle a restored factory's
            // golems off their tiles -- which under spatial routing means quietly rerouting them.
            golem = SpawnGolem(chassis, golemId, savedCell, savedFacing);

            // FORCED, not checked. A save describes a factory that was legal when it was built,
            // and refusing part of it on load would silently delete golems the player owns
            // because a cap moved -- the same reasoning that stops a load re-charging costs.
            if (assemblyBay != null)
            {
                assemblyBay.ForceAssignGolem(golem);
            }

            // Keep the counter ahead of every id restored from the file, or the next golem the
            // player builds is handed a name a loaded golem already answers to -- and golem ids
            // are the key for save entries, stall events and the spatial/steam registries, so a
            // duplicate is not cosmetic. Parsed rather than tracked as a count because the file
            // is the only thing that knows which numbers were used.
            ReserveGolemNumber(golemId);
            GolemSpawned?.Invoke(golem);
            return true;
        }

        // The whole birth sequence, shared so a respawned golem cannot drift from a built one.
        private GolemEntity SpawnGolem(
            ChassisDefinition chassis, string golemId, Vector2Int spawnCell, Facing spawnFacing)
        {
            GolemEntity golem = golemSource();
            golem.Configure(golemId, conveyor);
            golem.ConfigureEconomy(nodeRegistry, bufferRegistry);
            golem.Program.TryAssignChassis(chassis);

            // What tells the save system this golem is reconstructible. Set here rather than at
            // either call site so it is impossible to add a third way to build a golem that
            // forgets it -- a golem that forgot would simply vanish on the next load, silently,
            // which is the bug this whole change exists to fix.
            golem.MarkRuntimeSpawned();

            if (spatialEndpoints != null)
            {
                golem.ConfigureSpatial(spatialEndpoints, spawnCell, spawnFacing);
            }

            // After ConfigureSpatial, deliberately: the steam grid is keyed by cell, and
            // ConfigureSteam registers the golem at whatever cell it is standing on. Wiring it
            // first would register the golem at (0,0) and leave it there.
            if (steamNetwork != null)
            {
                golem.ConfigureSteam(steamNetwork);
            }

            // After ConfigureSpatial for the same reason: the cap claim is filed against the
            // node behind the golem, which is only meaningful once it is standing somewhere.
            if (nodeExtractors != null)
            {
                golem.ConfigureNodeExtractorCap(nodeExtractors);
            }

            // §6's Freight Link, and it must come after ConfigureSpatial too -- the binding is
            // "the mast nearest THIS golem", so it is meaningless until the golem has a cell.
            // This is the placement §6 binds at: one mast, chosen once, kept.
            if (mastRegistry != null)
            {
                golem.ConfigureFreight(mastRegistry);
            }

            // Unity ran OnEnable here (Instantiate enables the object); the Signal-trigger
            // subscription and the steam re-registration live in Attach now.
            golem.Attach();

            clock?.Register(golem);

            // A golem that exists but that the player cannot walk up to is not in the game.
            // The interactor caches its interactables, so without this every golem the player
            // ever built was unreachable for the rest of the session. Published from SpawnGolem
            // rather than from its two callers so a build and a save-restore cannot diverge.
            EventBus.Publish(new WorldInteractablesChangedEvent("golem spawned"));

            return golem;
        }

        /// <summary>
        /// The inverse of <see cref="SpawnGolem"/>: takes a golem out of the world and pays back
        /// what it is owed. The wrecking bar's golem branch, reached through
        /// <see cref="IGolemDismantler"/>.
        ///
        /// <para>
        /// <b>It undoes the birth sequence in reverse, and the order matters as much there as it
        /// does going forward.</b> The bay slot and the clock registration are released while the
        /// golem still exists to be identified by; the steam consumer and the node-extractor
        /// claim are released by <see cref="GolemEntity.Remove"/> (Unity's Destroy, which ran
        /// OnDisable) -- so they are deliberately NOT repeated here. Duplicating them would be a
        /// second writer for state that already has exactly one.
        /// </para>
        ///
        /// <para>
        /// <b>Room for the payout is checked before a single thing is torn down</b>, so a full
        /// stockpile costs the player nothing at all. This is the build mode's RefundWouldFit
        /// rule, and it matters more here: a building's refund is its cost, while a golem's is
        /// its cost <em>plus its cargo</em>, so a partial payout would destroy goods that were
        /// never the price of anything.
        /// </para>
        /// </summary>
        public bool TryDismantleGolem(
            GolemEntity golem,
            out IReadOnlyList<RecipeIngredient> refunded,
            out string refusalReason)
        {
            refunded = Array.Empty<RecipeIngredient>();
            refusalReason = "";

            if (golem == null || golem.IsRemoved)
            {
                refusalReason = "Nothing to dismantle there.";
                return false;
            }

            // A golem in the player's hands is mid-reposition, and the interactor is holding a
            // reference to it. Removing it underneath the carry would leave the player walking
            // around holding nothing, with no way to put it down. Refusing is one sentence; the
            // alternative is teaching the carry about deletion.
            if (golem.IsHeld)
            {
                refusalReason = $"Put {golem.GolemId} down before dismantling it.";
                return false;
            }

            // Only what the player paid for. A scene-authored golem still gives its cargo back
            // (real goods, wherever the golem came from) but not a chassis nobody bought --
            // refunding that would mint goods out of the scenery, exactly as refunding authored
            // furniture would. See GolemDismantleRules.
            IReadOnlyList<RecipeIngredient> chassisCost =
                golem.IsRuntimeSpawned && golem.Program != null && golem.Program.chassis != null
                    ? golem.Program.chassis.cost
                    : null;

            List<RecipeIngredient> payout = GolemDismantleRules.ComposeRefund(
                chassisCost,
                StockAsBundle(golem.Inventory != null ? golem.Inventory.Input : null),
                StockAsBundle(golem.Inventory != null ? golem.Inventory.Output : null));

            if (!PayoutWouldFit(payout))
            {
                refusalReason =
                    $"Not dismantled: the stockpile has no room for {golem.GolemId}'s refund. " +
                    "Make space first.";
                return false;
            }

            // Released before the golem goes, while there is still a golem to match on.
            if (assemblyBay != null)
            {
                assemblyBay.ReleaseGolem(golem);
            }

            // A removed golem left registered would keep being ticked.
            clock?.Unregister(golem);

            // The Workbench holds its target. Left pointing at a removed golem it would show
            // that golem's program and let the lever try to commit onto it.
            if (workbench != null && workbench.TargetGolem == golem)
            {
                workbench.RetargetGolem(null);
            }

            golem.Remove();

            // Paid after the teardown, matching the order a demolition uses, and safe because
            // the bundle was composed from the golem's own stock before anything was touched.
            if (bufferRegistry != null)
            {
                for (int i = 0; i < payout.Count; i++)
                {
                    bufferRegistry.Deposit(stockpileBufferId, payout[i].itemType, payout[i].quantity);
                }
            }

            // Same reason SpawnGolem publishes it: the interactor caches its interactables, so
            // without this the player keeps being offered a golem that is gone.
            EventBus.Publish(new WorldInteractablesChangedEvent("golem dismantled"));
            GolemDismantled?.Invoke(golem);

            refunded = payout;
            return true;
        }

        // One Stock flattened into an ingredient bundle, in TypesInOrder rather than dictionary
        // order -- the same determinism Push relies on, so two identically-loaded golems quote
        // the same refund.
        private static IReadOnlyList<RecipeIngredient> StockAsBundle(GolemInventory.Stock stock)
        {
            if (stock == null)
            {
                return null;
            }

            var bundle = new List<RecipeIngredient>(stock.TypesInOrder.Count);
            for (int i = 0; i < stock.TypesInOrder.Count; i++)
            {
                string itemType = stock.TypesInOrder[i];
                bundle.Add(new RecipeIngredient(itemType, stock.Get(itemType)));
            }

            return bundle;
        }

        // Answers true when there is nothing to pay or nowhere to pay it, so the check never
        // blocks a removal it has no stake in. It exists for capped buffers, exactly like the
        // build mode's RefundWouldFit.
        private bool PayoutWouldFit(IReadOnlyList<RecipeIngredient> payout)
        {
            if (bufferRegistry == null || payout == null || payout.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < payout.Count; i++)
            {
                if (bufferRegistry.RoomFor(stockpileBufferId, payout[i].itemType) < payout[i].quantity)
                {
                    return false;
                }
            }

            return true;
        }

        // "PlayerGolem-007" -> reserve 7, so the next build is 008. Anything that does not match
        // the pattern (a golem the player renamed, an id from another scene's setup) is ignored
        // rather than guessed at: an unparseable id cannot collide with a generated one.
        private void ReserveGolemNumber(string golemId)
        {
            const string prefix = "PlayerGolem-";
            if (golemId == null || !golemId.StartsWith(prefix, StringComparison.Ordinal))
            {
                return;
            }

            if (int.TryParse(golemId.Substring(prefix.Length), out int number) &&
                number >= _nextGolemNumber)
            {
                _nextGolemNumber = number + 1;
            }
        }

        private string NextGolemId()
        {
            string id = $"PlayerGolem-{_nextGolemNumber:D3}";
            _nextGolemNumber++;
            return id;
        }

        // Stands the new golem on a real tile and points it the same way the station points, so
        // "where you built it" IS its routing. With no spatial registry wired this degrades to
        // the pre-existing behaviour exactly: the station's own cell, no routing.
        private Vector2Int ResolveConstructedGolemCell(out Facing spawnFacing)
        {
            spawnFacing = StationFacing;

            if (spatialEndpoints == null)
            {
                return Vector2Int.zero;
            }

            return GolemSpawnPlacement.ResolveSpawnCell(
                Cell, spawnFacing, gridMap != null ? (Func<Vector2Int, bool>)gridMap.IsOccupied : null);
        }
    }
}
