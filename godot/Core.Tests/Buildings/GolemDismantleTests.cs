using System;
using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.Simulation;
using GolemFactory.PunchCards;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// The station's death sequence: the exact inverse of the birth sequence in
    /// <c>SpawnGolem</c>. PlayMode rather than EditMode because it turns on real destruction --
    /// <c>Object.Destroy</c> is deferred to end of frame, and <c>GolemEntity.OnDisable</c> (which
    /// releases the steam consumer and the node claim) does not run in EditMode at all.
    /// </summary>
    public class GolemDismantleTests
    {

        // Every golem a station builds in these tests, so TearDown can take it out of the world.
        // Unity swept leaked golems up with FindObjectsByType, because SpawnGolem instantiated
        // at the scene root; here a station-built golem is Attach()ed and so subscribed to the
        // static EventBus, and leaving it there would leak it into every later test.
        private readonly List<GolemEntity> _spawned = new List<GolemEntity>();

        [TearDown]
        public void TearDown()
        {
            foreach (GolemEntity golem in _spawned)
            {
                golem.Remove();
            }
            _spawned.Clear();
        }

        private static ChassisDefinition MakeChassis(int scrapCost, int brassCost)
        {
            var chassis = new ChassisDefinition();
            chassis.maxAppendageSlots = 3;
            chassis.cost = new List<RecipeIngredient>();
            if (scrapCost > 0)
            {
                chassis.cost.Add(new RecipeIngredient(ItemType.Scrap, scrapCost));
            }
            if (brassCost > 0)
            {
                chassis.cost.Add(new RecipeIngredient(ItemType.Brass, brassCost));
            }
            return chassis;
        }

        private (GolemConstructionStation station, StorageBufferRegistry buffers) Build(
            ChassisDefinition[] roster)
        {

            Func<GolemEntity> golemPrefab = () =>
            {
                var golem = new GolemEntity();
                _spawned.Add(golem);
                return golem;
            };

            var buffers = new StorageBufferRegistry();

            var station = new GolemConstructionStation();
            station.Configure(roster, golemPrefab, null, null, buffers, null, null, "FactoryStockpile");

            return (station, buffers);
        }

        private static int Stock(StorageBufferRegistry buffers, string itemType) =>
            buffers.GetOrCreate("FactoryStockpile").GetQuantity(itemType);

        [Test]
        public void Dismantle_PlayerBuiltGolem_RefundsTheChassisCostInFull()
        {
            ChassisDefinition chassis = MakeChassis(12, 4);
            var (station, buffers) = Build(new[] { chassis });
            buffers.Deposit("FactoryStockpile", ItemType.Scrap, 12);
            buffers.Deposit("FactoryStockpile", ItemType.Brass, 4);

            GolemEntity golem;
            Assert.IsTrue(station.TryConstructGolem(chassis, out golem));
            Assert.AreEqual(0, Stock(buffers, ItemType.Scrap), "building spends the bundle");

            IReadOnlyList<RecipeIngredient> refunded;
            string refusal;
            Assert.IsTrue(station.TryDismantleGolem(golem, out refunded, out refusal), refusal);

            // Full, not a fraction -- the same settled call that governs demolishing a building.
            Assert.AreEqual(12, Stock(buffers, ItemType.Scrap));
            Assert.AreEqual(4, Stock(buffers, ItemType.Brass));
            Assert.IsTrue(golem.IsRemoved, "the golem must actually leave the world");
        }

        [Test]
        public void Dismantle_GolemCarryingGoods_HandsThoseBackToo()
        {
            ChassisDefinition chassis = MakeChassis(12, 0);
            var (station, buffers) = Build(new[] { chassis });
            buffers.Deposit("FactoryStockpile", ItemType.Scrap, 12);

            GolemEntity golem;
            Assert.IsTrue(station.TryConstructGolem(chassis, out golem));
            golem.Inventory.AddInput(ItemType.Coal, 8);
            golem.Inventory.AddOutput(ItemType.Coke, 3);

            IReadOnlyList<RecipeIngredient> refunded;
            string refusal;
            Assert.IsTrue(station.TryDismantleGolem(golem, out refunded, out refusal), refusal);

            // The golem a player most wants to remove is usually the stalled one holding
            // something. Destroying its load would put the sting back into exactly that case.
            Assert.AreEqual(12, Stock(buffers, ItemType.Scrap));
            Assert.AreEqual(8, Stock(buffers, ItemType.Coal));
            Assert.AreEqual(3, Stock(buffers, ItemType.Coke));
        }

        [Test]
        public void Dismantle_SceneAuthoredGolem_RefundsCargoButNotAChassisNobodyBought()
        {
            ChassisDefinition chassis = MakeChassis(12, 4);
            var (station, buffers) = Build(new[] { chassis });

            // Built by hand rather than through the station, so IsRuntimeSpawned stays false --
            // this is what a golem authored into the scene looks like.
            var authored = new GolemEntity();
            authored.Configure("AuthoredGolem", null);
            authored.Program.TryAssignChassis(chassis);
            authored.Inventory.AddInput(ItemType.Coal, 5);

            IReadOnlyList<RecipeIngredient> refunded;
            string refusal;
            Assert.IsTrue(station.TryDismantleGolem(authored, out refunded, out refusal), refusal);

            Assert.AreEqual(0, Stock(buffers, ItemType.Scrap),
                "refunding scenery would mint goods out of the workshop's own furniture");
            Assert.AreEqual(5, Stock(buffers, ItemType.Coal),
                "what it was carrying is real goods whoever built it");
        }

        [Test]
        public void Dismantle_ReleasesItsAssemblyBaySlot()
        {
            ChassisDefinition chassis = MakeChassis(1, 0);
            var (station, buffers) = Build(new[] { chassis });
            buffers.Deposit("FactoryStockpile", ItemType.Scrap, 10);

            var bay = new AssemblyBayStructure();
            station.ConfigureAssemblyBay(bay);

            GolemEntity golem;
            Assert.IsTrue(station.TryConstructGolem(chassis, out golem));
            Assert.AreEqual(1, bay.OccupiedSlots);

            IReadOnlyList<RecipeIngredient> refunded;
            string refusal;
            Assert.IsTrue(station.TryDismantleGolem(golem, out refunded, out refusal), refusal);

            // "All bays are full. Upgrade the bays, or dismantle a golem." named this action
            // before it existed -- so the slot has to actually come back.
            Assert.AreEqual(0, bay.OccupiedSlots);
            Assert.IsTrue(bay.HasFreeSlot);
        }

        [Test]
        public void Dismantle_StopsTheGolemBeingTicked()
        {
            ChassisDefinition chassis = MakeChassis(1, 0);
            var (station, buffers) = Build(new[] { chassis });
            buffers.Deposit("FactoryStockpile", ItemType.Scrap, 10);

            var clock = new SimulationClock();
            station.ConfigureSceneServices(null, null, buffers, clock, null, "FactoryStockpile");

            clock.Play();

            GolemEntity golem;
            Assert.IsTrue(station.TryConstructGolem(chassis, out golem));

            IReadOnlyList<RecipeIngredient> refunded;
            string refusal;
            Assert.IsTrue(station.TryDismantleGolem(golem, out refunded, out refusal), refusal);

            // A destroyed MonoBehaviour left registered is ticked through a Unity-null
            // reference, which throws rather than quietly doing nothing.
            Assert.DoesNotThrow(() => clock.Advance(10f));
        }

        [Test]
        public void Dismantle_HeldGolem_IsRefusedRatherThanDestroyedUnderTheCarry()
        {
            ChassisDefinition chassis = MakeChassis(1, 0);
            var (station, buffers) = Build(new[] { chassis });
            buffers.Deposit("FactoryStockpile", ItemType.Scrap, 10);

            GolemEntity golem;
            Assert.IsTrue(station.TryConstructGolem(chassis, out golem));
            golem.SetHeld(true);

            int stockBefore = Stock(buffers, ItemType.Scrap);

            IReadOnlyList<RecipeIngredient> refunded;
            string refusal;
            Assert.IsFalse(station.TryDismantleGolem(golem, out refunded, out refusal));

            // PlayerInteractor is holding a reference to it: destroying it mid-carry leaves the
            // player walking around holding nothing, with nothing to put down.
            Assert.IsTrue(!golem.IsRemoved);
            Assert.IsNotEmpty(refusal);
            Assert.AreEqual(stockBefore, Stock(buffers, ItemType.Scrap), "a refusal must not pay out");
        }

        [Test]
        public void Dismantle_NoRoomForTheRefund_KeepsTheGolemStandingAndSaysWhy()
        {
            ChassisDefinition chassis = MakeChassis(12, 0);
            var (station, buffers) = Build(new[] { chassis });
            buffers.Deposit("FactoryStockpile", ItemType.Scrap, 12);

            GolemEntity golem;
            Assert.IsTrue(station.TryConstructGolem(chassis, out golem));

            // Room for 4 of the 12 the chassis would pay back.
            buffers.GetOrCreate("FactoryStockpile").SetCapacityPerType(4);

            IReadOnlyList<RecipeIngredient> refunded;
            string refusal;
            Assert.IsFalse(station.TryDismantleGolem(golem, out refunded, out refusal));

            // A dismantle that ate the overflow is precisely the punishment the full refund
            // exists to remove -- and here it would eat the cargo as well as the chassis.
            Assert.IsTrue(!golem.IsRemoved, "the golem stays standing rather than half-dismantled");
            Assert.IsNotEmpty(refusal);
            Assert.AreEqual(0, Stock(buffers, ItemType.Scrap));
        }

        [Test]
        public void Dismantle_TwiceOverTheSameGolem_DoesNotPayTwice()
        {
            ChassisDefinition chassis = MakeChassis(12, 0);
            var (station, buffers) = Build(new[] { chassis });
            buffers.Deposit("FactoryStockpile", ItemType.Scrap, 12);

            GolemEntity golem;
            Assert.IsTrue(station.TryConstructGolem(chassis, out golem));

            IReadOnlyList<RecipeIngredient> refunded;
            string refusal;
            Assert.IsTrue(station.TryDismantleGolem(golem, out refunded, out refusal), refusal);

            // The second call sees a destroyed golem, which compares equal to null through
            // Unity's overload -- the guard has to be the null check, not "was it already gone".
            Assert.IsFalse(station.TryDismantleGolem(golem, out refunded, out refusal));
            Assert.AreEqual(12, Stock(buffers, ItemType.Scrap), "a double click must not mint goods");
        }
    }
}
