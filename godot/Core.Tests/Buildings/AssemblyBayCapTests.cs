using System;
using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// The golem cap: one fixed number, where the frame budget holds, with nothing to buy
    /// (2026-11-19). The tests fill a small cap so they stay quick.
    /// </summary>
    public class AssemblyBayCapTests
    {
        private const int SmallCap = 10;

        private AssemblyBayStructure BuildBay()
        {
            var bay = new AssemblyBayStructure();
            bay.ConfigureCap(SmallCap);
            return bay;
        }

        private GolemEntity MakeGolem(string name)
        {
            return new GolemEntity();
        }

        [Test]
        public void TheCapIs112_WhereTheFrameBudgetHolds()
        {
            var bay = new AssemblyBayStructure();

            Assert.AreEqual(112, AssemblyBayStructure.DefaultCap);
            Assert.AreEqual(112, bay.MaxGolemSlots);
            Assert.AreEqual(112, bay.GolemCap);
            Assert.IsTrue(bay.HasFreeSlot);
        }

        [Test]
        public void TheWorldReadsItsCapFromTheData()
        {
            SandboxSetup setup = SandboxSetup.Parse(
                System.IO.File.ReadAllText(System.IO.Path.Combine(AuthoredData.DataDirectory, "sandbox.json")));
            Assert.AreEqual(112, setup.golemCap);
        }

        [Test]
        public void ACapOfZero_LeavesItUncapped_ForADiagnosticLoad()
        {
            var bay = new AssemblyBayStructure();
            bay.ConfigureCap(0);

            Assert.AreEqual(int.MaxValue, bay.MaxGolemSlots);
        }

        [Test]
        public void AnAssignedGolem_IsListed_AndCannotTakeASecondSlot()
        {
            AssemblyBayStructure bay = BuildBay();
            GolemEntity golem = MakeGolem("Golem");

            Assert.IsTrue(bay.TryAssignGolem(golem));
            Assert.Contains(golem, (System.Collections.ICollection)bay.AssignedGolems);
            Assert.IsFalse(bay.TryAssignGolem(golem));
            Assert.AreEqual(1, bay.OccupiedSlots);
        }

        [Test]
        public void ReleasingAGolem_FreesItsSlotInAFullBay()
        {
            AssemblyBayStructure bay = BuildBay();
            GolemEntity golem = MakeGolem("Golem");
            bay.TryAssignGolem(golem);
            for (int i = 1; i < SmallCap; i++)
            {
                bay.TryAssignGolem(MakeGolem("Filler" + i));
            }
            Assert.IsFalse(bay.HasFreeSlot);

            Assert.IsTrue(bay.ReleaseGolem(golem));
            Assert.IsTrue(bay.TryAssignGolem(MakeGolem("GolemB")));
        }

        [Test]
        public void ADestroyedGolem_GivesItsSlotBack()
        {
            // §10's recovery route from an over-built factory is "delete golems, freeing both
            // bay slots and upkeep instantly". A slot that never came back would break the
            // escape hatch, not just the accounting.
            AssemblyBayStructure bay = BuildBay();
            GolemEntity golem = MakeGolem("Doomed");
            bay.TryAssignGolem(golem);
            Assert.AreEqual(1, bay.OccupiedSlots);

            golem.Remove();

            Assert.AreEqual(0, bay.OccupiedSlots);
        }

        [Test]
        public void AFullBay_HasNoFreeSlot()
        {
            AssemblyBayStructure bay = BuildBay();
            for (int i = 0; i < SmallCap; i++)
            {
                Assert.IsTrue(bay.TryAssignGolem(MakeGolem("Golem" + i)));
            }

            Assert.IsFalse(bay.HasFreeSlot);
            Assert.IsFalse(bay.TryAssignGolem(MakeGolem("OneTooMany")));
        }

        [Test]
        public void ForceAssign_TakesAGolemOverTheCap_ForTheLoadPath()
        {
            // A save describes a factory that was legal when it was built. Refusing part of it
            // on load would delete golems the player owns because a cap moved.
            AssemblyBayStructure bay = BuildBay();
            for (int i = 0; i < SmallCap; i++)
            {
                bay.TryAssignGolem(MakeGolem("Golem" + i));
            }

            bay.ForceAssignGolem(MakeGolem("Restored"));

            Assert.AreEqual(11, bay.OccupiedSlots);
            Assert.IsFalse(bay.HasFreeSlot);
        }

        // --- In the loop ---------------------------------------------------------------------

        private GolemConstructionStation BuildStation(AssemblyBayStructure bay, out ChassisDefinition chassis)
        {
            Func<GolemEntity> golemPrefab = () => new GolemEntity();

            var buffers = new StorageBufferRegistry();

            chassis = new ChassisDefinition();
            chassis.cost = new System.Collections.Generic.List<RecipeIngredient>
            {
                new RecipeIngredient(ItemType.Scrap, 5),
            };
            buffers.Deposit("FactoryStockpile", ItemType.Scrap, 500);

            var station = new GolemConstructionStation();
            station.Configure(
                new[] { chassis }, golemPrefab, null, null, buffers, null, null, "FactoryStockpile");
            station.ConfigureAssemblyBay(bay);
            return station;
        }

        [Test]
        public void TheStationRefusesToBuildPastTheCap_AndChargesNothingForTheRefusal()
        {
            AssemblyBayStructure bay = BuildBay();
            GolemConstructionStation station = BuildStation(bay, out ChassisDefinition chassis);

            var built = new System.Collections.Generic.List<GolemEntity>();
            for (int i = 0; i < SmallCap; i++)
            {
                Assert.IsTrue(station.TryConstructGolem(chassis, out GolemEntity golem), "golem " + i);
                built.Add(golem);
            }

            station.TryGetStockpile(out int scrapBefore, out _);
            Assert.IsFalse(station.TryConstructGolem(chassis, out GolemEntity refused));
            Assert.IsNull(refused);
            station.TryGetStockpile(out int scrapAfter, out _);

            Assert.AreEqual(scrapBefore, scrapAfter,
                "The cap is checked BEFORE the cost, so a refusal never touches the stockpile.");
            Assert.IsNotEmpty(station.LastRefusalReason);
            StringAssert.Contains("holds " + SmallCap + " golems", station.LastRefusalReason);

            foreach (GolemEntity golem in built)
            {
                golem.Remove();
            }
        }

        [Test]
        public void AStationWithNoBay_BuildsWithoutALimit()
        {
            // Main.unity's demos and every pre-existing rig take this branch, exactly as they
            // do for steam and the extractor cap.
            GolemConstructionStation station = BuildStation(null, out ChassisDefinition chassis);

            var built = new System.Collections.Generic.List<GolemEntity>();
            for (int i = 0; i < SmallCap + 3; i++)
            {
                Assert.IsTrue(station.TryConstructGolem(chassis, out GolemEntity golem));
                built.Add(golem);
            }

            foreach (GolemEntity golem in built)
            {
                golem.Remove();
            }
        }
    }
}
