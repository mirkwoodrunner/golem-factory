using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using GolemFactory.Belts;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    // Facing-based spatial routing: a golem pulls from the tile behind it and pushes to the
    // tile in front (docs/digital-design.md, "Grid & Movement Mechanics").
    //
    // The load-bearing property under test is the *fallback*: Main.unity's seven hand-wired
    // demo golems and every pre-existing test route by the bare-string sourceId/destinationId
    // baked into their appendage assets, and must be completely unaffected. A golem only
    // routes spatially once ConfigureSpatial has handed it a registry.
    public class GolemSpatialRoutingTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
            {
                Object.DestroyImmediate(go);
            }
            _spawned.Clear();
        }

        private T AddHolder<T>() where T : Component
        {
            var go = new GameObject(typeof(T).Name);
            _spawned.Add(go);
            return go.AddComponent<T>();
        }

        private GolemEntity CreateGolem(AppendageActionDefinition step)
        {
            var go = new GameObject("SpatialGolem");
            _spawned.Add(go);
            GolemEntity entity = go.AddComponent<GolemEntity>();

            var logicCore = ScriptableObject.CreateInstance<LogicCoreDefinition>();
            logicCore.triggerType = TriggerType.AlwaysOn;
            entity.Program.logicCore = logicCore;
            entity.Program.appendages.Add(step);
            return entity;
        }

        private static AppendageActionDefinition Step(
            AppendageActionType type, string sourceId = null, string destinationId = null)
        {
            var step = ScriptableObject.CreateInstance<AppendageActionDefinition>();
            step.actionType = type;
            step.sourceId = sourceId;
            step.destinationId = destinationId;
            return step;
        }

        // --- Fallback: id routing when nothing spatial is configured -----------------------

        [Test]
        public void WithoutASpatialRegistry_ExtractStillRoutesByTheAuthoredIds()
        {
            ConveyorSystemHolder conveyor = AddHolder<ConveyorSystemHolder>();
            ResourceNodeRegistryHolder nodes = AddHolder<ResourceNodeRegistryHolder>();
            var belt = new BeltSegment("ScrapBeltA", 4);
            conveyor.System.Register(belt);
            nodes.Registry.Register(new ResourceNode("ScrapNode", ItemType.Scrap, 3));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.ExtractFromNode, "ScrapNode", "ScrapBeltA"));
            golem.Configure("Demo", conveyor);
            golem.ConfigureEconomy(nodes, null);
            // Deliberately no ConfigureSpatial -- this is the Main.unity demo-golem shape.

            golem.Tick(0);

            Assert.AreEqual(1, belt.Items.Count, "id-routed extract stopped working");
            Assert.AreEqual(StallReason.None, golem.StallReason);
        }

        [Test]
        public void WithoutASpatialRegistry_LoadIntoBufferStillRoutesByTheAuthoredIds()
        {
            ConveyorSystemHolder conveyor = AddHolder<ConveyorSystemHolder>();
            StorageBufferRegistryHolder buffers = AddHolder<StorageBufferRegistryHolder>();
            var belt = new BeltSegment("ScrapBeltA", 1);
            conveyor.System.Register(belt);
            belt.TryEnqueue(new ItemStack { ItemType = ItemType.Scrap });
            belt.Advance(belt.Length);

            GolemEntity golem = CreateGolem(Step(AppendageActionType.LoadIntoBuffer, "ScrapBeltA", "ScrapBuffer"));
            golem.Configure("Demo", conveyor);
            golem.ConfigureEconomy(null, buffers);

            golem.Tick(0);

            StorageBuffer buffer;
            Assert.IsTrue(buffers.Registry.TryGetBuffer("ScrapBuffer", out buffer));
            Assert.AreEqual(1, buffer.GetQuantity(ItemType.Scrap));
        }

        [Test]
        public void WithoutASpatialRegistry_HaulStaysTheHistoricalNoOpSuccess()
        {
            // Haul's locomotion was never built, so it was a no-op success stub. Golems in
            // Main.unity carrying a Haul card must keep cycling rather than start stalling.
            GolemEntity golem = CreateGolem(Step(AppendageActionType.Haul));
            golem.Tick(0);

            Assert.AreNotEqual(GolemState.Stalled, golem.Program.State);
            Assert.AreEqual(StallReason.None, golem.StallReason);
        }

        // --- Strictness: the branch is on the GOLEM, not on the individual tile -------------
        // These are the tests that pin the actual fix. The first version of spatial routing
        // decided the fallback per-endpoint, so a spatially placed golem with an empty source
        // tile silently reverted to its authored sourceId and kept working -- which meant
        // rotating a golem away from its node did nothing, and facing was decorative for
        // exactly the two actions players use most.

        [Test]
        public void SpatiallyPlaced_WithAnEmptySourceTile_StallsInsteadOfFallingBackToTheAuthoredId()
        {
            // Both authored ids are live and would happily carry the step. A spatially placed
            // golem must ignore them entirely and report the tile.
            ConveyorSystemHolder conveyor = AddHolder<ConveyorSystemHolder>();
            ResourceNodeRegistryHolder nodes = AddHolder<ResourceNodeRegistryHolder>();
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            var belt = new BeltSegment("ScrapBeltA", 4);
            conveyor.System.Register(belt);
            nodes.Registry.Register(new ResourceNode("ScrapNode", ItemType.Scrap, 3));

            // Give it a valid *target* tile so the stall can only be about the source.
            var tileBelt = new BeltSegment("TileBelt", 4);
            endpoints.Registry.Register(new Vector2Int(9, 10), new BeltSegmentEndpoint(tileBelt));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.ExtractFromNode, "ScrapNode", "ScrapBeltA"));
            golem.Configure("Spatial", conveyor);
            golem.ConfigureEconomy(nodes, null);
            golem.ConfigureSpatial(endpoints, new Vector2Int(9, 9), Facing.North);

            golem.Tick(0);

            Assert.AreEqual(GolemState.Stalled, golem.Program.State);
            Assert.AreEqual(StallReason.NoSourceAtTile, golem.StallReason);
            Assert.AreEqual(new Vector2Int(9, 8).ToString(), golem.StallResourceId);
            Assert.AreEqual(0, belt.Items.Count, "the authored destinationId was used anyway");
            ResourceNode idNode;
            Assert.IsTrue(nodes.Registry.TryGetNode("ScrapNode", out idNode));
            Assert.AreEqual(3, idNode.RemainingQuantity, "the authored sourceId was drained anyway");
        }

        [Test]
        public void SpatiallyPlaced_WithAnEmptyTargetTile_StallsInsteadOfFallingBackToTheAuthoredId()
        {
            ConveyorSystemHolder conveyor = AddHolder<ConveyorSystemHolder>();
            ResourceNodeRegistryHolder nodes = AddHolder<ResourceNodeRegistryHolder>();
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            var belt = new BeltSegment("ScrapBeltA", 4);
            conveyor.System.Register(belt);
            nodes.Registry.Register(new ResourceNode("ScrapNode", ItemType.Scrap, 3));

            // Valid source tile, nothing in front.
            var tileNode = new ResourceNode("TileNode", ItemType.Scrap, 5);
            endpoints.Registry.Register(new Vector2Int(9, 8), new ResourceNodeEndpoint(tileNode));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.ExtractFromNode, "ScrapNode", "ScrapBeltA"));
            golem.Configure("Spatial", conveyor);
            golem.ConfigureEconomy(nodes, null);
            golem.ConfigureSpatial(endpoints, new Vector2Int(9, 9), Facing.North);

            golem.Tick(0);

            Assert.AreEqual(StallReason.NoTargetAtTile, golem.StallReason);
            Assert.AreEqual(new Vector2Int(9, 10).ToString(), golem.StallResourceId);
            Assert.AreEqual(5, tileNode.RemainingQuantity, "a blocked extract consumed from the tile node");
            Assert.AreEqual(0, belt.Items.Count, "the authored destinationId was used anyway");
        }

        [Test]
        public void SpatiallyPlaced_LoadIntoBuffer_WithAnEmptySourceTile_StallsInsteadOfUsingTheAuthoredBelt()
        {
            // The other half of the pair players actually use. The id-named belt is loaded and
            // ready; strictness means it must be ignored.
            ConveyorSystemHolder conveyor = AddHolder<ConveyorSystemHolder>();
            StorageBufferRegistryHolder buffers = AddHolder<StorageBufferRegistryHolder>();
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();

            var idBelt = new BeltSegment("ScrapBeltA", 1);
            conveyor.System.Register(idBelt);
            idBelt.TryEnqueue(new ItemStack { ItemType = ItemType.Scrap });
            idBelt.Advance(idBelt.Length);

            var tileBuffer = new StorageBuffer("TileBuffer");
            endpoints.Registry.Register(new Vector2Int(0, 1), new StorageBufferEndpoint(tileBuffer));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.LoadIntoBuffer, "ScrapBeltA", "ScrapBuffer"));
            golem.Configure("Spatial", conveyor);
            golem.ConfigureEconomy(null, buffers);
            golem.ConfigureSpatial(endpoints, Vector2Int.zero, Facing.North);

            golem.Tick(0);

            Assert.AreEqual(StallReason.NoSourceAtTile, golem.StallReason);
            Assert.AreEqual(new Vector2Int(0, -1).ToString(), golem.StallResourceId);
            Assert.AreEqual(1, idBelt.Items.Count, "the authored sourceId belt was drained anyway");
            Assert.AreEqual(0, tileBuffer.GetQuantity(ItemType.Scrap));
            Assert.IsFalse(buffers.Registry.TryGetBuffer("ScrapBuffer", out _),
                "the authored destinationId buffer was written to anyway");
        }

        [Test]
        public void RotatingAPlacedGolemAwayFromItsNode_StallsItsExtractStep()
        {
            // The player-facing statement of the whole feature, on ExtractFromNode rather than
            // Haul: same golem, same program, one rotation, and the behaviour must change.
            ConveyorSystemHolder conveyor = AddHolder<ConveyorSystemHolder>();
            ResourceNodeRegistryHolder nodes = AddHolder<ResourceNodeRegistryHolder>();
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            nodes.Registry.Register(new ResourceNode("ScrapNode", ItemType.Scrap, 9));
            var idBelt = new BeltSegment("ScrapBeltA", 4);
            conveyor.System.Register(idBelt);

            var tileNode = new ResourceNode("TileNode", ItemType.Scrap, 9);
            var tileBelt = new BeltSegment("TileBelt", 4);
            endpoints.Registry.Register(new Vector2Int(0, 0), new ResourceNodeEndpoint(tileNode));
            endpoints.Registry.Register(new Vector2Int(0, 2), new BeltSegmentEndpoint(tileBelt));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.ExtractFromNode, "ScrapNode", "ScrapBeltA"));
            golem.Configure("Spatial", conveyor);
            golem.ConfigureEconomy(nodes, null);
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);

            golem.Tick(0);
            Assert.AreEqual(StallReason.None, golem.StallReason, "precondition: it should work while aimed correctly");
            Assert.AreEqual(1, tileBelt.Items.Count);

            golem.SetPlacement(new Vector2Int(0, 1), Facing.West);
            golem.Tick(1);

            Assert.AreEqual(GolemState.Stalled, golem.Program.State,
                "rotating away from the node did not stall it -- facing is decorative again");
            Assert.AreEqual(StallReason.NoSourceAtTile, golem.StallReason);
            Assert.AreEqual(0, idBelt.Items.Count, "it quietly kept working via the authored ids");
        }

        [Test]
        public void Refine_StaysIdRoutedEvenWhenSpatiallyPlaced()
        {
            // Deliberate exemption: a recipe is defined by item types, and IItemEndpoint is
            // type-agnostic, so a spatial take could grab the wrong input off a mixed buffer
            // and silently transmute it.
            StorageBufferRegistryHolder buffers = AddHolder<StorageBufferRegistryHolder>();
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            buffers.Registry.Deposit("ScrapBuffer", ItemType.Scrap);

            AppendageActionDefinition step = Step(AppendageActionType.Refine, "ScrapBuffer", "PlateBuffer");
            step.inputItemType = ItemType.Scrap;
            step.outputItemType = ItemType.Brass;

            GolemEntity golem = CreateGolem(step);
            golem.ConfigureEconomy(null, buffers);
            // Spatially placed with two completely empty tiles: a strict spatial Refine would
            // stall here. It must not -- it routes by the ids its recipe names.
            golem.ConfigureSpatial(endpoints, new Vector2Int(20, 20), Facing.North);

            golem.Tick(0);

            Assert.AreEqual(StallReason.None, golem.StallReason, "Refine was routed spatially");
            StorageBuffer output;
            Assert.IsTrue(buffers.Registry.TryGetBuffer("PlateBuffer", out output));
            Assert.AreEqual(1, output.GetQuantity(ItemType.Brass));
        }

        // --- Spatial wins when a tile is occupied -----------------------------------------

        [Test]
        public void SpatialEndpoints_TakePrecedenceOverTheAuthoredIds()
        {
            ConveyorSystemHolder conveyor = AddHolder<ConveyorSystemHolder>();
            ResourceNodeRegistryHolder nodes = AddHolder<ResourceNodeRegistryHolder>();
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();

            // The id-named belt exists and has room, so if routing were still id-based the
            // item would land here.
            var idBelt = new BeltSegment("ScrapBeltA", 4);
            conveyor.System.Register(idBelt);
            nodes.Registry.Register(new ResourceNode("ScrapNode", ItemType.Scrap, 3));

            // The spatially adjacent pair the golem should actually use.
            var tileNode = new ResourceNode("TileNode", ItemType.Brass, 3);
            var tileBelt = new BeltSegment("TileBelt", 4);
            endpoints.Registry.Register(new Vector2Int(0, 0), new ResourceNodeEndpoint(tileNode));
            endpoints.Registry.Register(new Vector2Int(0, 2), new BeltSegmentEndpoint(tileBelt));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.ExtractFromNode, "ScrapNode", "ScrapBeltA"));
            golem.Configure("Spatial", conveyor);
            golem.ConfigureEconomy(nodes, null);
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);

            golem.Tick(0);

            Assert.AreEqual(1, tileBelt.Items.Count, "the item did not go to the tile in front");
            Assert.AreEqual(ItemType.Brass, tileBelt.Items[0].ItemType,
                "the item came from the id-named node, not the tile behind");
            Assert.AreEqual(2, tileNode.RemainingQuantity);
            Assert.AreEqual(0, idBelt.Items.Count, "the id-named belt was used despite spatial endpoints");
        }

        [Test]
        public void FacingAwayFromTheSource_StallsWithNoSourceAtTile()
        {
            // The contrast that proves facing is real: same golem, same program, rotated.
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            var node = new ResourceNode("TileNode", ItemType.Scrap, 5);
            var belt = new BeltSegment("TileBelt", 4);
            endpoints.Registry.Register(new Vector2Int(0, 0), new ResourceNodeEndpoint(node));
            endpoints.Registry.Register(new Vector2Int(0, 2), new BeltSegmentEndpoint(belt));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.Haul));
            golem.Configure("Hauler", null);
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);

            golem.Tick(0);
            Assert.AreEqual(1, belt.Items.Count, "facing the node should have moved an item");
            Assert.AreEqual(4, node.RemainingQuantity);

            golem.SetPlacement(new Vector2Int(0, 1), Facing.East);
            golem.Tick(1);

            Assert.AreEqual(GolemState.Stalled, golem.Program.State);
            Assert.AreEqual(StallReason.NoSourceAtTile, golem.StallReason);
            Assert.AreEqual(new Vector2Int(-1, 1).ToString(), golem.StallResourceId,
                "the stall should name the empty tile behind the golem");
            Assert.AreEqual(1, belt.Items.Count, "a rotated golem still moved an item");
            Assert.AreEqual(4, node.RemainingQuantity, "a rotated golem still drained the node");
        }

        [Test]
        public void NothingInFront_StallsWithNoTargetAtTile()
        {
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            var node = new ResourceNode("TileNode", ItemType.Scrap, 5);
            endpoints.Registry.Register(new Vector2Int(0, 0), new ResourceNodeEndpoint(node));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.Haul));
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);

            golem.Tick(0);

            Assert.AreEqual(StallReason.NoTargetAtTile, golem.StallReason);
            Assert.AreEqual(new Vector2Int(0, 2).ToString(), golem.StallResourceId);
            Assert.AreEqual(5, node.RemainingQuantity, "a blocked haul consumed from the node");
        }

        // --- Haul, made real ---------------------------------------------------------------

        [Test]
        public void Haul_MovesOneItemFromTheTileBehindToTheTileInFront()
        {
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            var source = new StorageBuffer("SourceChest");
            source.Deposit(ItemType.Scrap, 3);
            var destination = new StorageBuffer("DestChest");
            endpoints.Registry.Register(new Vector2Int(5, 5), new StorageBufferEndpoint(source));
            endpoints.Registry.Register(new Vector2Int(7, 5), new StorageBufferEndpoint(destination));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.Haul));
            golem.ConfigureSpatial(endpoints, new Vector2Int(6, 5), Facing.East);

            golem.Tick(0);

            Assert.AreEqual(2, source.GetQuantity(ItemType.Scrap));
            Assert.AreEqual(1, destination.GetQuantity(ItemType.Scrap));
        }

        [Test]
        public void Haul_OntoAFullBelt_DoesNotConsumeFromTheFiniteSource()
        {
            // The same check-before-consume ordering hazard that caused the original item-loss
            // bug, on the spatial path: CanGive must be consulted before TryTake.
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            var node = new ResourceNode("TileNode", ItemType.Scrap, 5);
            var belt = new BeltSegment("TileBelt", 1);
            while (belt.TryEnqueue(new ItemStack { ItemType = ItemType.Scrap }))
            {
            }
            Assert.IsFalse(belt.CanEnqueue(), "precondition: belt must be full");

            endpoints.Registry.Register(new Vector2Int(0, 0), new ResourceNodeEndpoint(node));
            endpoints.Registry.Register(new Vector2Int(0, 2), new BeltSegmentEndpoint(belt));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.Haul));
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);

            for (int tick = 0; tick < 10; tick++)
            {
                golem.Tick(tick);
            }

            Assert.AreEqual(5, node.RemainingQuantity,
                "a blocked haul consumed from the node and dropped the item");
            Assert.AreEqual(StallReason.BeltFull, golem.StallReason);
        }

        [Test]
        public void Haul_DrainingSourceStallsWithoutInventingItems()
        {
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            var node = new ResourceNode("TileNode", ItemType.Scrap, 2);
            var destination = new StorageBuffer("DestChest");
            endpoints.Registry.Register(new Vector2Int(0, 0), new ResourceNodeEndpoint(node));
            endpoints.Registry.Register(new Vector2Int(0, 2), new StorageBufferEndpoint(destination));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.Haul));
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);

            for (int tick = 0; tick < 6; tick++)
            {
                golem.Tick(tick);
            }

            Assert.AreEqual(2, destination.GetQuantity(ItemType.Scrap), "the node yielded more than it held");
            Assert.AreEqual(StallReason.NodeEmpty, golem.StallReason);
            Assert.AreEqual("TileNode", golem.StallResourceId);
        }

        // --- Spatial LoadIntoBuffer --------------------------------------------------------

        [Test]
        public void LoadIntoBuffer_PullsFromTheBeltOnTheTileBehind()
        {
            SpatialEndpointRegistryHolder endpoints = AddHolder<SpatialEndpointRegistryHolder>();
            var belt = new BeltSegment("TileBelt", 1);
            belt.TryEnqueue(new ItemStack { ItemType = ItemType.Brass });
            belt.Advance(belt.Length);
            var buffer = new StorageBuffer("TileBuffer");
            endpoints.Registry.Register(new Vector2Int(0, 1), new BeltSegmentEndpoint(belt));
            endpoints.Registry.Register(new Vector2Int(0, -1), new StorageBufferEndpoint(buffer));

            GolemEntity golem = CreateGolem(Step(AppendageActionType.LoadIntoBuffer, "ScrapBeltA", "ScrapBuffer"));
            golem.ConfigureSpatial(endpoints, Vector2Int.zero, Facing.South);

            golem.Tick(0);

            Assert.AreEqual(1, buffer.GetQuantity(ItemType.Brass));
            Assert.AreEqual(0, belt.Items.Count);
        }

        // --- Placement ---------------------------------------------------------------------

        [Test]
        public void SourceAndTargetCells_FollowThePlacement()
        {
            GolemEntity golem = CreateGolem(Step(AppendageActionType.Haul));
            golem.SetPlacement(new Vector2Int(4, 4), Facing.West);

            Assert.AreEqual(new Vector2Int(4, 4), golem.Cell);
            Assert.AreEqual(Facing.West, golem.Facing);
            Assert.AreEqual(new Vector2Int(3, 4), golem.TargetCell);
            Assert.AreEqual(new Vector2Int(5, 4), golem.SourceCell);
        }
    }
}
