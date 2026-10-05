using System.Collections.Generic;
using System.Text.Json;
using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.PunchCards;

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

        public sealed class Placement
        {
            public int x;
            public int y;
            public string facing = "North";
            public string recipes;
            public string roster;
        }

        /// <summary>The Floor Expansion purchase, as Sandbox.unity's FloorExpansionService authored it.</summary>
        public sealed class Expansion
        {
            public int scrapPerRow;
            public int ironPlatePerRow;
            public int rowsPerPurchase;
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
        public int productionBufferCapacityPerType = 100;
        public int startingNorthExtent = FloorLayout.DefaultNorthExtent;
        public string freeStallSeedNode;
        public int freeStallSeedQuantity;
        public Point playerStart = new Point();
        public Expansion floorExpansion;
        public WorkbenchRoster workbench;
        public AssemblyLineSetup assemblyLine;
        public List<NodeEntry> nodes = new List<NodeEntry>();
        public List<MarketEntry> market = new List<MarketEntry>();
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
        /// Production buffers are capped per type; the factory stockpile is not
        /// (SandboxBootstrap.ApplyBufferCapacityPolicy, unchanged).
        /// </summary>
        public void ApplyBufferPolicy(StorageBufferRegistry buffers)
        {
            buffers.DefaultCapacityPerType = productionBufferCapacityPerType;
            if (!string.IsNullOrEmpty(stockpileBufferId))
            {
                buffers.SetCapacity(stockpileBufferId, StorageBuffer.Unlimited);
            }
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

        public static Vector2Int CellOf(Placement placement) => new Vector2Int(placement.x, placement.y);

        public static Facing FacingOf(Placement placement) =>
            System.Enum.TryParse(placement.facing, out Facing facing) ? facing : Facing.North;
    }
}
