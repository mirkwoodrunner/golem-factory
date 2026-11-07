using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.PunchCards;
using GolemFactory.Supply;

namespace GolemFactory.World
{
    /// <summary>
    /// The Sandbox scene's authored setup -- what Unity spread across Sandbox.unity's placed
    /// objects, ManagerHolders.prefab and SandboxBootstrap's code -- read from
    /// <c>godot/data/sandbox.json</c>, plus the rules that apply it.
    ///
    /// <para>
    /// PORTED FROM SandboxBootstrap (milestone G4): the data moved to JSON, and the parts of
    /// Start() that were rules rather than wiring live here, unchanged -- how starting nodes are
    /// stocked per game mode, the buffer capacity policy, the market's offers. The wiring itself
    /// (who talks to whom) is the scene's job.
    /// </para>
    /// </summary>
    public sealed class SandboxSetup
    {
        public sealed class NodeEntry
        {
            public string id;
            public string itemType;
            public string sprite;
            public int x;
            public int y;
        }

        public sealed class MarketEntry
        {
            public string nodeId;
            public List<RecipeIngredient> price = new List<RecipeIngredient>();
            public int truckloadSize;
            public int deliveryTicks;
        }

        /// <summary>
        /// The supply street (docs/supply-design.md): the spots carts can stand on, the carts the
        /// world starts with, and each good's ladder of prices, rates and capacities.
        /// </summary>
        public sealed class SupplySetup
        {
            public List<SpotEntry> spots = new List<SpotEntry>();
            public List<StartingCart> startingCarts = new List<StartingCart>();
            public List<CartLadderEntry> carts = new List<CartLadderEntry>();
        }

        public sealed class SpotEntry
        {
            public int x;
            public int y;
        }

        public sealed class StartingCart
        {
            public int x;
            public int y;
            public string itemType;
        }

        /// <summary>One good's cart: its price, and rung 0 then each rung's rate, capacity and price.</summary>
        public sealed class CartLadderEntry
        {
            public string itemType;
            public List<RecipeIngredient> add = new List<RecipeIngredient>();
            public List<int> rates = new List<int>();
            public List<int> capacities = new List<int>();
            public List<List<RecipeIngredient>> rateCosts = new List<List<RecipeIngredient>>();
            public List<List<RecipeIngredient>> capacityCosts = new List<List<RecipeIngredient>>();
        }

        public sealed class Placement
        {
            public int x;
            public int y;
            public string facing = "North";
            public string recipes;
            public string roster;
        }

        /// <summary>
        /// The Floor Expansion purchase: rows per purchase, and one price per purchase, priced up
        /// the tiers (docs/pack-upgrades-design.md §4).
        /// </summary>
        public sealed class Expansion
        {
            public int rowsPerPurchase;
            public List<List<RecipeIngredient>> purchases = new List<List<RecipeIngredient>>();
        }

        /// <summary>
        /// The Workbench's roster: WorkbenchCanvas.prefab's chassis and logic cores, and the 24
        /// cards Sandbox.unity overrode its vault to. Ungated, this is the whole vault; gated
        /// (§8.3), a card must also be claimed on the Assembly Line.
        /// </summary>
        public sealed class WorkbenchRoster
        {
            public List<string> chassis = new List<string>();
            public List<string> logicCores = new List<string>();
            public List<string> appendages = new List<string>();
        }

        /// <summary>
        /// Sandbox.unity's Assembly Line: the deck its bootstrap seeded (AssemblyLineDeck), the
        /// holder's three slots, the claim user, and gateWorkbenchRoster -- on, so the Workbench
        /// vault offers only claimed cards.
        /// </summary>
        public sealed class AssemblyLineSetup
        {
            public string deck;
            public int slots = 3;
            public string claimUserId = "LocalPlayer";
            public bool gateWorkbench;
        }

        public sealed class Point
        {
            public float x;
            public float y;
        }

        public bool creativeMode;
        public bool requireSteamPower;
        public string stockpileBufferId = "FactoryStockpile";

        /// <summary>
        /// A crate holds this many of each good (docs/crates-design.md §0.3). In the Sandbox every
        /// buffer without an override is a crate, so it is the registry's default.
        /// </summary>
        public int crateCapacityPerType = 50;

        /// <summary>The pack (the stockpile id): what the player carries, this many of each good.</summary>
        public int packCapacityPerType = 100;

        /// <summary>The pack's cap ladder above <see cref="packCapacityPerType"/> (docs/pack-upgrades-design.md §3).</summary>
        public List<PackUpgradeRung> packUpgrades = new List<PackUpgradeRung>();

        /// <summary>The Pneumatic Post's shared store (crates-design §10): this many of each good. A placeholder.</summary>
        public int postCapacityPerType = 200;
        public int startingNorthExtent = FloorLayout.DefaultNorthExtent;

        /// <summary>
        /// How fast cargo crosses a belt cell. 0 (absent) keeps Unity's rate: a cell in
        /// segmentLengthTicks ticks, which at 10 ticks a second was 2.5 cells a second -- too
        /// quick to watch in playtest, so the shipping Sandbox sets 1 (G10).
        /// </summary>
        public float beltCellsPerSecond;

        /// <summary>Whether the step-by-step guide runs (G10). Off unless the setup asks.</summary>
        public bool tutorial;

        /// <summary>
        /// Playtest mode (G10): the guide asks the playtest script's judgement questions, times
        /// every step, and keeps a report. Needs <see cref="tutorial"/>.
        /// </summary>
        public bool playtest;
        public string freeStallSeedNode;
        public int freeStallSeedQuantity;
        public Point playerStart = new Point();
        public Expansion floorExpansion;
        public WorkbenchRoster workbench;
        public AssemblyLineSetup assemblyLine;
        public List<NodeEntry> nodes = new List<NodeEntry>();
        public List<MarketEntry> market = new List<MarketEntry>();
        public SupplySetup supply;
        public Placement starterBench;
        public Placement starterStation;

        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };

        public static SandboxSetup Parse(string json) => JsonSerializer.Deserialize<SandboxSetup>(json, Options);

        public GameMode Mode => new GameMode(creativeMode);

        /// <summary>
        /// Registers every starting node. In Creative Mode a stall never runs dry; otherwise
        /// every stall starts EMPTY and is stocked by ordering truckloads -- except the free scrap
        /// stall, seeded once so the player's first minutes are not spent waiting on a cart they
        /// cannot yet afford (SandboxBootstrap.RegisterStartingNodes, unchanged).
        /// </summary>
        public void RegisterNodes(ResourceNodeRegistry registry)
        {
            int startingStock = creativeMode ? ResourceNode.Infinite : 0;
            foreach (NodeEntry node in nodes)
            {
                registry.Register(new ResourceNode(node.id, node.itemType, startingStock));
            }

            if (!creativeMode && !string.IsNullOrEmpty(freeStallSeedNode)
                && registry.TryGetNode(freeStallSeedNode, out ResourceNode seeded))
            {
                seeded.Deliver(freeStallSeedQuantity);
            }
        }

        /// <summary>
        /// Crates are the default, capped per type; the pack and the Post store are overrides,
        /// capped at their own numbers (docs/crates-design.md §4.3). The pack was unlimited while
        /// every depot opened onto it. The Post store exists from the start, empty, so the wallet
        /// never has to ask whether it does (§10).
        /// </summary>
        public void ApplyBufferPolicy(StorageBufferRegistry buffers)
        {
            buffers.DefaultCapacityPerType = crateCapacityPerType;
            if (!string.IsNullOrEmpty(stockpileBufferId))
            {
                buffers.SetCapacity(stockpileBufferId, packCapacityPerType);
            }
            buffers.SetCapacity(SandboxWorld.PostBufferId, postCapacityPerType);
        }

        /// <summary>The truckload market with every stall's offer -- ManagerHolders' TruckloadMarketHolder.</summary>
        public TruckloadMarket BuildMarket(ResourceNodeRegistry nodeRegistry)
        {
            var built = new TruckloadMarket(nodeRegistry, Mode);
            foreach (MarketEntry entry in market)
            {
                built.AddOffer(new MarketOffer(entry.nodeId, entry.price, entry.truckloadSize, entry.deliveryTicks));
            }
            return built;
        }

        /// <summary>The supply street with its spots, ladders and starting carts.</summary>
        public SupplyStreet BuildSupply(
            ResourceNodeRegistry nodes, SpatialEndpointRegistry endpoints, GridCoordinateConverter cells,
            List<ResourceNodeMarker> markers, Func<PlayerWallet> wallet, Func<string, bool> seen,
            int ticksPerMinute, Func<Vector2Int, bool> occupied)
        {
            var street = new SupplyStreet(nodes, endpoints, cells, markers, wallet, seen, creativeMode, ticksPerMinute, occupied);
            if (supply == null)
            {
                return street;
            }
            foreach (SpotEntry spot in supply.spots)
            {
                street.AddSpot(new Vector2Int(spot.x, spot.y));
            }
            foreach (CartLadderEntry cart in supply.carts)
            {
                street.AddLadder(new CartLadder(
                    cart.itemType, cart.add, cart.rates, cart.capacities,
                    cart.rateCosts.Select(c => (IReadOnlyList<RecipeIngredient>)c).ToList(),
                    cart.capacityCosts.Select(c => (IReadOnlyList<RecipeIngredient>)c).ToList()));
            }
            PlaceStartingCarts(street);
            return street;
        }

        /// <summary>The carts the world starts with, on their spots: a new game, or a load of a
        /// save from before carts.</summary>
        public void PlaceStartingCarts(SupplyStreet street)
        {
            foreach (StartingCart cart in supply?.startingCarts ?? new List<StartingCart>())
            {
                street.PlaceStartingCart(new Vector2Int(cart.x, cart.y), cart.itemType);
            }
        }

        public static Vector2Int CellOf(Placement placement) => new Vector2Int(placement.x, placement.y);

        public static Facing FacingOf(Placement placement) =>
            System.Enum.TryParse(placement.facing, out Facing facing) ? facing : Facing.North;
    }
}
