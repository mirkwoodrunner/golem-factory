using System;
using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Blueprints;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.UI;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// The construction station's "From a patent" rows: a golem built from a patent arrives with
    /// the patent's program engaged, at the bare chassis's cost. Patents used to be loaded from
    /// Management into a bare golem's Workbench, and Management cannot open over the Workbench,
    /// so the only route to a patent was a playtester's three-screen round trip.
    /// </summary>
    public class ConstructFromBlueprintTests
    {
        private const string Stockpile = "FactoryStockpile";

        private readonly ChassisDefinition _scavenger = new ChassisDefinition
        {
            name = "ClockworkScavenger",
            maxAppendageSlots = 2,
            cost = new List<RecipeIngredient> { new RecipeIngredient(ItemType.Scrap, 12) },
        };

        private readonly ChassisDefinition _presser = new ChassisDefinition
        {
            name = "BrassPresser",
            maxAppendageSlots = 3,
            cost = new List<RecipeIngredient> { new RecipeIngredient(ItemType.Scrap, 60) },
        };

        private readonly LogicCoreDefinition _alwaysOn = new LogicCoreDefinition { name = "AlwaysOnCore" };

        private readonly AppendageActionDefinition _haul = new AppendageActionDefinition
        {
            name = "HaulScrap", actionType = AppendageActionType.Haul, inputItemType = ItemType.Scrap, haulQuantity = 2,
        };

        private readonly AppendageActionDefinition _push = new AppendageActionDefinition
        {
            name = "PushOutput", actionType = AppendageActionType.Push,
        };

        /// <summary>Records what the golem's program held at the moment the Workbench was pointed at it.</summary>
        private sealed class RecordingWorkbench : IWorkbenchTarget
        {
            public GolemEntity TargetGolem { get; private set; }
            public int StepsAtRetarget = -1;

            public void RetargetGolem(GolemEntity golem)
            {
                TargetGolem = golem;
                StepsAtRetarget = golem?.Program.appendages.Count ?? -1;
            }
        }

        private (GolemConstructionStation station, StorageBufferRegistry buffers, RecordingWorkbench workbench) Build(params ChassisDefinition[] roster)
        {
            var buffers = new StorageBufferRegistry();
            var workbench = new RecordingWorkbench();
            var station = new GolemConstructionStation();
            station.Configure(roster, () => new GolemEntity(), null, null, buffers, null, workbench, Stockpile);
            return (station, buffers, workbench);
        }

        private Blueprint Patent(ChassisDefinition chassis, IReadOnlyList<AppendageActionDefinition> cards,
            IReadOnlyList<string> itemTypes = null, IReadOnlyList<int> quantities = null) =>
            new Blueprint("BP-001", "LocalPlayer", chassis, _alwaysOn, cards, itemTypes, quantities);

        [Test]
        public void ABuildFromAPatent_ArrivesWithTheWholeProgramEngaged()
        {
            var (station, buffers, _) = Build(_scavenger);
            buffers.Deposit(Stockpile, ItemType.Scrap, 12);
            Blueprint patent = Patent(_scavenger, new[] { _haul, _push },
                new[] { ItemType.Coke, "" }, new[] { 5, 1 });

            Assert.IsTrue(station.TryConstructFromBlueprint(patent, out GolemEntity golem));

            GolemProgram program = golem.Program;
            Assert.AreSame(_scavenger, program.chassis);
            Assert.AreSame(_alwaysOn, program.logicCore);
            CollectionAssert.AreEqual(new[] { _haul, _push }, program.appendages);
            Assert.AreEqual(ItemType.Coke, program.GetItemTypeAt(0), "the slot's picked good, not the card's");
            Assert.AreEqual(5, program.GetQuantityAt(0), "the slot's batch size");
            Assert.AreEqual(GolemState.Idle, program.State);
            Assert.AreEqual(0, program.CurrentStepIndex);
        }

        [Test]
        public void ABuildFromAPatent_CostsExactlyTheBareChassis()
        {
            var (station, buffers, _) = Build(_scavenger);
            buffers.Deposit(Stockpile, ItemType.Scrap, 15);

            Assert.IsTrue(station.TryConstructFromBlueprint(Patent(_scavenger, new[] { _push }), out _));

            Assert.AreEqual(3, buffers.GetQuantity(Stockpile, ItemType.Scrap));
        }

        [Test]
        public void AnOlderPatent_WithoutGoodsOrBatches_TakesEachCardsOwn()
        {
            var (station, buffers, _) = Build(_scavenger);
            buffers.Deposit(Stockpile, ItemType.Scrap, 12);

            Assert.IsTrue(station.TryConstructFromBlueprint(Patent(_scavenger, new[] { _haul, _push }), out GolemEntity golem));

            Assert.AreEqual(ItemType.Scrap, golem.Program.GetItemTypeAt(0));
            Assert.AreEqual(2, golem.Program.GetQuantityAt(0));
        }

        [Test]
        public void TheWorkbenchAndTheHost_MeetTheGolemAlreadyProgrammed()
        {
            // RetargetGolem re-reads the draft from the golem: pointed at it before the program
            // went on, the Workbench would show a bare golem that is actually running.
            var (station, buffers, workbench) = Build(_scavenger);
            buffers.Deposit(Stockpile, ItemType.Scrap, 12);
            int stepsAtSpawn = -1;
            station.GolemSpawned += g => stepsAtSpawn = g.Program.appendages.Count;

            Assert.IsTrue(station.TryConstructFromBlueprint(Patent(_scavenger, new[] { _haul, _push }), out GolemEntity golem));

            Assert.AreSame(golem, workbench.TargetGolem);
            Assert.AreEqual(2, workbench.StepsAtRetarget);
            Assert.AreEqual(2, stepsAtSpawn);
        }

        [Test]
        public void APatentForAChassisThisStationCannotBuild_IsRefusedWithoutCharging()
        {
            var (station, buffers, _) = Build(_scavenger);
            buffers.Deposit(Stockpile, ItemType.Scrap, 100);

            Assert.IsFalse(station.TryConstructFromBlueprint(Patent(_presser, new[] { _push }), out GolemEntity golem));

            Assert.IsNull(golem);
            StringAssert.Contains("cannot build a Brass Presser", station.LastRefusalReason);
            Assert.AreEqual(100, buffers.GetQuantity(Stockpile, ItemType.Scrap));
        }

        [Test]
        public void APatentWithMoreStepsThanItsChassisHolds_IsRefusedWithoutCharging()
        {
            var (station, buffers, _) = Build(_scavenger);
            buffers.Deposit(Stockpile, ItemType.Scrap, 100);

            Assert.IsFalse(station.TryConstructFromBlueprint(Patent(_scavenger, new[] { _haul, _push, _push }), out _));

            StringAssert.Contains("more steps", station.LastRefusalReason);
            Assert.AreEqual(100, buffers.GetQuantity(Stockpile, ItemType.Scrap));
        }

        [Test]
        public void APatentWithNoChassis_IsRefused()
        {
            var (station, _, _) = Build(_scavenger);

            Assert.IsFalse(station.TryConstructFromBlueprint(Patent(null, new[] { _push }), out _));

            StringAssert.Contains("names no chassis", station.LastRefusalReason);
        }

        [Test]
        public void AnUnaffordablePatent_FailsAsTheBareChassisWould_WithNoReason()
        {
            // An empty reason is how the panel knows to print the shortfall instead.
            var (station, buffers, _) = Build(_scavenger);
            buffers.Deposit(Stockpile, ItemType.Scrap, 5);

            Assert.IsFalse(station.TryConstructFromBlueprint(Patent(_scavenger, new[] { _push }), out GolemEntity golem));

            Assert.IsNull(golem);
            Assert.AreEqual("", station.LastRefusalReason);
            Assert.AreEqual(5, buffers.GetQuantity(Stockpile, ItemType.Scrap));
        }

        [Test]
        public void ThePatentSummary_ReadsTheProgramInOrder_NamingAChangedGood()
        {
            Blueprint patent = Patent(_scavenger, new[] { _haul, _push }, new[] { ItemType.Coke, "" }, new[] { 2, 1 });

            Assert.AreEqual("Always On Core · Haul Scrap (Coke) · Push Output", PatentBrowser.Summary(patent));
        }

        [Test]
        public void ThePatentSummary_LeavesAnUnchangedGoodUnsaid()
        {
            Assert.AreEqual("Always On Core · Haul Scrap · Push Output",
                PatentBrowser.Summary(Patent(_scavenger, new[] { _haul, _push })));
        }
    }
}
