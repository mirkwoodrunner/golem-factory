using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// §6's Freight Link: <c>FreightLaunch</c> empties a Zeppelin's output stock onto its bound
    /// mast's tile, regardless of distance -- one-way, fixed pair, 24 ticks, no pathfinding and
    /// no adaptivity. It is what turns a distant node into a self-contained outpost.
    /// </summary>
    public class FreightLinkTests
    {

        [TearDown]
        public void TearDown()
        {
        }

        private static ChassisDefinition MakeChassis(bool allowsFreight)
        {
            var chassis = new ChassisDefinition();
            chassis.maxAppendageSlots = 6;
            chassis.allowsFreightLaunch = allowsFreight;
            return chassis;
        }

        // --- the registry -------------------------------------------------------------------

        [Test]
        public void NearestMast_IsChebyshevAndTiesBreakByCellOrder()
        {
            var registry = new FreightMastRegistry();
            registry.Register(new Vector2Int(5, 0), "east");
            registry.Register(new Vector2Int(-5, 0), "west");

            // Equidistant: the tie must be settled by cell order, not by dictionary iteration,
            // or two identically-built factories bind their Zeppelins differently and diverge.
            Assert.IsTrue(registry.TryFindNearest(Vector2Int.zero, out Vector2Int chosen));
            Assert.AreEqual(new Vector2Int(-5, 0), chosen);

            registry.Register(new Vector2Int(1, 1), "close");
            Assert.IsTrue(registry.TryFindNearest(Vector2Int.zero, out chosen));
            Assert.AreEqual(new Vector2Int(1, 1), chosen);
        }

        [Test]
        public void NoMasts_MeansNoBinding()
        {
            var registry = new FreightMastRegistry();

            Assert.IsFalse(registry.TryFindNearest(Vector2Int.zero, out _));
        }

        // --- the chassis gate ---------------------------------------------------------------

        [Test]
        public void OnlyTheZeppelinMayHoldFreightLaunch()
        {
            var card = new AppendageActionDefinition();
            card.actionType = AppendageActionType.FreightLaunch;

            var refused = new GolemProgram();
            refused.TryAssignChassis(MakeChassis(allowsFreight: false));
            Assert.IsFalse(refused.TryAddAppendage(card));

            var allowed = new GolemProgram();
            allowed.TryAssignChassis(MakeChassis(allowsFreight: true));
            Assert.IsTrue(allowed.TryAddAppendage(card));
        }

        // --- execution ------------------------------------------------------------------------

        private GolemEntity BuildZeppelin(
            Vector2Int golemCell, Vector2Int mastCell, out StorageBuffer mastBuffer,
            out GolemProgram program)
        {

            var endpoints = new SpatialEndpointRegistry();

            var masts = new FreightMastRegistry();
            masts.Register(mastCell, "Mast");

            mastBuffer = new StorageBuffer("MastDepot");
            endpoints.Register(mastCell, new StorageBufferEndpoint(mastBuffer));

            var golem = new GolemEntity();

            program = golem.Program;
            program.TryAssignChassis(MakeChassis(allowsFreight: true));

            var launch = new AppendageActionDefinition();
            launch.actionType = AppendageActionType.FreightLaunch;
            program.TryAddAppendage(launch);

            var logicCore = new LogicCoreDefinition();
            logicCore.triggerType = TriggerType.AlwaysOn;
            program.logicCore = logicCore;

            golem.ConfigureSpatial(endpoints, golemCell, Facing.North);
            golem.ConfigureFreight(masts);
            return golem;
        }

        [Test]
        public void ALaunchEmptiesTheHoldOntoTheMast_HoweverFarAway()
        {
            // Thirty cells away and across the map: §6 is explicit that the link works
            // "regardless of distance", which is what makes a distant outpost self-contained.
            GolemEntity zeppelin = BuildZeppelin(
                new Vector2Int(0, 0), new Vector2Int(30, -18), out StorageBuffer mast, out _);
            // INPUT stock, because the pure-logistics rule applies to a launch exactly as it
            // does to a Push: this program has no Assemble step, so its input IS its push stock.
            // A Haul -> FreightLaunch Zeppelin is the canonical shape of §6's outpost run.
            zeppelin.Inventory.AddInput(ItemType.Coke, 5);
            zeppelin.Inventory.AddInput(ItemType.IronPlate, 3);

            for (long tick = 0; tick < GolemEntity.FreightLaunchTicks; tick++)
            {
                zeppelin.Tick(tick);
            }

            Assert.AreEqual(5, mast.GetQuantity(ItemType.Coke));
            Assert.AreEqual(3, mast.GetQuantity(ItemType.IronPlate));
            Assert.AreEqual(0, zeppelin.Inventory.GetInput(ItemType.Coke));
        }

        [Test]
        public void ALaunchCostsAFlatTwentyFourTicks_WhateverItCarries()
        {
            // Not 2 + unitCount: a flight is a flight. Charging per unit would make the link
            // cheaper for a light hold, which is a throughput dial §6 never gave it.
            GolemEntity zeppelin = BuildZeppelin(
                Vector2Int.zero, new Vector2Int(9, 9), out StorageBuffer mast, out GolemProgram program);
            zeppelin.Inventory.AddInput(ItemType.Coke, 6);

            zeppelin.Tick(0);

            // THE GOODS MOVE AT BEGIN AND THE GOLEM IS BUSY AFTERWARDS, exactly as Haul, Extract
            // and Push do -- the duration is committed work, not a delivery delay. The rejected
            // alternative was holding the load "in flight" until the step completes, which would
            // create a third place goods can exist: not in the golem and not on the mast. The
            // consume-after-give ordering in PushStockInto exists precisely so no such window
            // is ever open, and a golem destroyed mid-flight would take the cargo with it.
            Assert.AreEqual(6, mast.GetQuantity(ItemType.Coke), "the hold leaves at the launch");
            Assert.AreEqual(0, zeppelin.Inventory.GetInput(ItemType.Coke));

            // ...and the Zeppelin is occupied for the full flight regardless of what it carried.
            // Ticks 0..23 INCLUSIVE are the 24: the launch began on tick 0, so the last tick of
            // the flight is the one that completes it.
            for (long tick = 1; tick < GolemEntity.FreightLaunchTicks - 1; tick++)
            {
                zeppelin.Tick(tick);
                Assert.AreEqual(GolemState.Running, program.State, "still flying at tick " + tick);
            }

            zeppelin.Tick(GolemEntity.FreightLaunchTicks - 1);
            Assert.AreEqual(0, program.CurrentStepIndex, "the cycle comes round after 24 ticks");
            Assert.AreEqual(24, GolemEntity.FreightLaunchTicks);
        }

        [Test]
        public void TheBindingIsFixed_ANearerMastBuiltLaterDoesNotStealIt()
        {
            // §6 binds "at placement". A per-tick nearest search would re-route a working
            // factory the moment the player built a mast somewhere else.
            var endpoints = new SpatialEndpointRegistry();
            var masts = new FreightMastRegistry();

            var far = new Vector2Int(20, 0);
            masts.Register(far, "far");

            var golem = new GolemEntity();
            golem.ConfigureSpatial(endpoints, Vector2Int.zero, Facing.North);
            golem.ConfigureFreight(masts);

            Assert.AreEqual(far, golem.BoundMastCell);

            masts.Register(new Vector2Int(1, 0), "near");

            Assert.AreEqual(far, golem.BoundMastCell, "the pair is fixed once chosen");
        }

        [Test]
        public void AZeppelinWithNoMast_StallsNamingTheFact()
        {
            var endpoints = new SpatialEndpointRegistry();
            var masts = new FreightMastRegistry();

            var golem = new GolemEntity();
            GolemProgram program = golem.Program;
            program.TryAssignChassis(MakeChassis(allowsFreight: true));

            var launch = new AppendageActionDefinition();
            launch.actionType = AppendageActionType.FreightLaunch;
            program.TryAddAppendage(launch);
            var logicCore = new LogicCoreDefinition();
            logicCore.triggerType = TriggerType.AlwaysOn;
            program.logicCore = logicCore;

            golem.ConfigureSpatial(endpoints, Vector2Int.zero, Facing.North);
            golem.ConfigureFreight(masts);
            golem.Inventory.AddInput(ItemType.Coke, 1);

            golem.Tick(0);

            Assert.AreEqual(GolemState.Stalled, program.State);
            Assert.AreEqual(StallReason.NoTargetAtTile, golem.StallReason,
                "a missing mast is something the player can go and build, not a broken program");

            // ...and it clears the moment one exists, without reprogramming.
            var mastBuffer = new StorageBuffer("MastDepot");
            var cell = new Vector2Int(4, 4);
            masts.Register(cell, "Mast");
            endpoints.Register(cell, new StorageBufferEndpoint(mastBuffer));

            for (long tick = 1; tick <= GolemEntity.FreightLaunchTicks; tick++)
            {
                golem.Tick(tick);
            }

            Assert.AreEqual(1, mastBuffer.GetQuantity(ItemType.Coke));
        }

        [Test]
        public void AZeppelinThatAssembles_LaunchesFromItsOutputStock()
        {
            // The other side of the pure-logistics fork, and the reason FreightLaunch had to
            // reuse PushStock rather than naming a stock of its own: with an Assemble in the
            // program, the hold that goes on the cart is the OUTPUT stock, and the inputs stay
            // where they are.
            GolemEntity zeppelin = BuildZeppelin(
                Vector2Int.zero, new Vector2Int(7, 7), out StorageBuffer mast, out GolemProgram program);

            var assemble = new AppendageActionDefinition();
            assemble.actionType = AppendageActionType.Assemble;
            var recipe = new RecipeDefinition();
            recipe.inputs = new List<RecipeIngredient> { new RecipeIngredient(ItemType.Coal, 1) };
            recipe.outputItemType = ItemType.Coke;
            recipe.outputQuantity = 1;
            recipe.durationTicks = 2;
            assemble.recipe = recipe;

            // Assemble FIRST, then the launch, so the cycle makes a good and then flies it out.
            program.RemoveAppendageAt(0);
            program.TryAddAppendage(assemble);
            var launch = new AppendageActionDefinition();
            launch.actionType = AppendageActionType.FreightLaunch;
            program.TryAddAppendage(launch);

            zeppelin.Inventory.AddInput(ItemType.Coal, 1);

            for (long tick = 0; tick < 2 + GolemEntity.FreightLaunchTicks; tick++)
            {
                zeppelin.Tick(tick);
            }

            Assert.AreEqual(1, mast.GetQuantity(ItemType.Coke));
            Assert.AreEqual(0, zeppelin.Inventory.GetOutput(ItemType.Coke));
        }

        [Test]
        public void AFullMast_SkipsTheRefusedTypeRatherThanAbandoningTheLaunch()
        {
            // The launch shares Push's loop precisely so §10's deadlock fix cannot drift: a full
            // Slag slot must never block Iron Plate.
            GolemEntity zeppelin = BuildZeppelin(
                Vector2Int.zero, new Vector2Int(6, 6), out StorageBuffer mast, out _);
            mast.SetCapacityPerType(10);
            mast.Deposit(ItemType.Slag, 10);

            zeppelin.Inventory.AddInput(ItemType.Slag, 2);
            zeppelin.Inventory.AddInput(ItemType.IronPlate, 4);

            for (long tick = 0; tick < GolemEntity.FreightLaunchTicks; tick++)
            {
                zeppelin.Tick(tick);
            }

            Assert.AreEqual(4, mast.GetQuantity(ItemType.IronPlate), "the open type still moves");
            Assert.AreEqual(2, zeppelin.Inventory.GetInput(ItemType.Slag),
                "the refused type stays in the hold for the next cycle");
        }
    }
}
