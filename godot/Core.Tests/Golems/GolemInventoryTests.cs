using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.PunchCards;

namespace GolemFactory.Tests.EditMode
{
    // The golem's internal typed stock (docs/progression-design.md section 2). Plain C#, so
    // these run with no scene, no clock and no GameObject at all.
    public class GolemInventoryTests
    {
        [Test]
        public void AddInput_ClampsAtThePerTypeCapAndReportsWhatActuallyWentIn()
        {
            var inventory = new GolemInventory();

            Assert.AreEqual(GolemInventory.CapacityPerType,
                inventory.AddInput(ItemType.Scrap, GolemInventory.CapacityPerType + 5),
                "it accepted more than the cap");
            Assert.AreEqual(GolemInventory.CapacityPerType, inventory.GetInput(ItemType.Scrap));
            Assert.AreEqual(0, inventory.AddInput(ItemType.Scrap, 1), "a full stock kept accepting");
        }

        [Test]
        public void TheCapIsPerItemType_NotPerGolem()
        {
            // A per-golem cap would deadlock exactly the way a whole-buffer cap does: one type
            // backing up would lock out every other type, and no rigid golem can drain the
            // wrong one out.
            var inventory = new GolemInventory();
            inventory.AddInput(ItemType.Scrap, GolemInventory.CapacityPerType);

            Assert.AreEqual(GolemInventory.CapacityPerType, inventory.AddInput(ItemType.Brass, 99));
            Assert.AreEqual(GolemInventory.CapacityPerType, inventory.GetInput(ItemType.Scrap));
        }

        [Test]
        public void InputRoomFor_TracksTheRemainingHeadroomAndNeverGoesNegative()
        {
            var inventory = new GolemInventory();
            Assert.AreEqual(GolemInventory.CapacityPerType, inventory.InputRoomFor(ItemType.Scrap));

            inventory.AddInput(ItemType.Scrap, 5);
            Assert.AreEqual(GolemInventory.CapacityPerType - 5, inventory.InputRoomFor(ItemType.Scrap));

            inventory.AddInput(ItemType.Scrap, 99);
            Assert.AreEqual(0, inventory.InputRoomFor(ItemType.Scrap));
        }

        [Test]
        public void TryConsumeInput_IsAllOrNothing()
        {
            var inventory = new GolemInventory();
            inventory.AddInput(ItemType.Scrap, 2);

            Assert.IsFalse(inventory.TryConsumeInput(ItemType.Scrap, 3));
            Assert.AreEqual(2, inventory.GetInput(ItemType.Scrap),
                "a failed consume took a partial bite anyway");

            Assert.IsTrue(inventory.TryConsumeInput(ItemType.Scrap, 2));
            Assert.AreEqual(0, inventory.GetInput(ItemType.Scrap));
        }

        [Test]
        public void InputAndOutputAreSeparatePiles()
        {
            // Keeping them separate is what lets Push empty everything at once and makes
            // byproducts free -- see the pure-logistics rule in GolemEntity.PushStock.
            var inventory = new GolemInventory();
            inventory.AddInput(ItemType.Scrap, 4);

            Assert.AreEqual(0, inventory.GetOutput(ItemType.Scrap));
            Assert.AreEqual(GolemInventory.CapacityPerType, inventory.OutputRoomFor(ItemType.Scrap));
        }

        [Test]
        public void TotalUnits_CountsEveryTypeInAStock()
        {
            var inventory = new GolemInventory();
            inventory.AddOutput(ItemType.Scrap, 3);
            inventory.AddOutput(ItemType.Brass, 2);

            Assert.AreEqual(5, inventory.Output.TotalUnits);
        }

        [Test]
        public void EnumerationOrderIsInsertionOrder_NotWhateverTheDictionaryFeelsLike()
        {
            // Push drains in this order, so two identically-programmed golems facing a
            // destination with room for only part of a mixed hold must agree on what goes
            // first. Dictionary iteration order is not a contract; this list is.
            var stock = new GolemInventory().Input;
            stock.Add(ItemType.Aether, 1);
            stock.Add(ItemType.Scrap, 1);
            stock.Add(ItemType.Brass, 1);

            CollectionAssert.AreEqual(
                new[] { ItemType.Aether, ItemType.Scrap, ItemType.Brass }, stock.TypesInOrder);
        }

        [Test]
        public void ATypeThatEmptiesLeavesTheOrderingRatherThanLingeringAtZero()
        {
            var stock = new GolemInventory().Input;
            stock.Add(ItemType.Scrap, 1);
            stock.Add(ItemType.Brass, 1);
            stock.TryConsume(ItemType.Scrap, 1);

            CollectionAssert.AreEqual(new[] { ItemType.Brass }, stock.TypesInOrder);
            Assert.AreEqual(1, stock.TotalUnits);
        }

        [Test]
        public void NullOrEmptyItemTypes_AreRefusedRatherThanStored()
        {
            var inventory = new GolemInventory();

            Assert.AreEqual(0, inventory.AddInput(null, 3));
            Assert.AreEqual(0, inventory.AddInput(string.Empty, 3));
            Assert.AreEqual(0, inventory.GetInput(null));
            Assert.AreEqual(0, inventory.InputRoomFor(null));
            Assert.IsFalse(inventory.TryConsumeInput(null, 1));
            Assert.AreEqual(0, inventory.Input.TotalUnits);
        }

        [Test]
        public void Clear_EmptiesBothStocks()
        {
            var inventory = new GolemInventory();
            inventory.AddInput(ItemType.Scrap, 3);
            inventory.AddOutput(ItemType.Brass, 3);

            inventory.Clear();

            Assert.AreEqual(0, inventory.Input.TotalUnits);
            Assert.AreEqual(0, inventory.Output.TotalUnits);
            Assert.AreEqual(0, inventory.Output.TypesInOrder.Count);
        }
    }

    // The per-slot Haul quantity lives on GolemProgram, not on the shared
    // AppendageActionDefinition asset -- one card is referenced by every golem that slots it.
    public class GolemProgramQuantityTests
    {
        private static ChassisDefinition Chassis(int slots)
        {
            var chassis = new ChassisDefinition();
            chassis.maxAppendageSlots = slots;
            return chassis;
        }

        private static AppendageActionDefinition Card(
            AppendageActionType type = AppendageActionType.Haul, int defaultQuantity = 1)
        {
            var card = new AppendageActionDefinition();
            card.actionType = type;
            card.haulQuantity = defaultQuantity;
            return card;
        }

        [Test]
        public void AddingAnAppendage_SeedsTheSlotWithTheCardsAuthoredDefault()
        {
            var program = new GolemProgram();
            program.TryAssignChassis(Chassis(2));
            program.TryAddAppendage(Card(defaultQuantity: 6));

            Assert.AreEqual(6, program.GetQuantityAt(0));
        }

        [Test]
        public void SettingAQuantity_DoesNotWriteBackToTheSharedCard()
        {
            // The whole reason this state is on the program: two golems sharing one asset must
            // be able to disagree about batch size.
            AppendageActionDefinition shared = Card(defaultQuantity: 2);

            var a = new GolemProgram();
            a.TryAssignChassis(Chassis(2));
            a.TryAddAppendage(shared);
            var b = new GolemProgram();
            b.TryAssignChassis(Chassis(2));
            b.TryAddAppendage(shared);

            a.SetQuantityAt(0, 9);

            Assert.AreEqual(9, a.GetQuantityAt(0));
            Assert.AreEqual(2, b.GetQuantityAt(0), "the other golem's batch size changed too");
            Assert.AreEqual(2, shared.haulQuantity, "the shared asset was mutated");
        }

        [Test]
        public void QuantityIsClampedToOneThroughTheStockCap()
        {
            var program = new GolemProgram();
            program.TryAssignChassis(Chassis(2));
            program.TryAddAppendage(Card());

            program.SetQuantityAt(0, 0);
            Assert.AreEqual(1, program.GetQuantityAt(0), "a zero-batch Haul would never finish");

            program.SetQuantityAt(0, GolemInventory.CapacityPerType + 40);
            Assert.AreEqual(GolemInventory.CapacityPerType, program.GetQuantityAt(0),
                "a batch bigger than the per-type cap can never complete");
        }

        [Test]
        public void RemovingAnAppendage_RemovesItsQuantityToo_KeepingTheListsAligned()
        {
            var program = new GolemProgram();
            program.TryAssignChassis(Chassis(3));
            program.TryAddAppendage(Card(defaultQuantity: 1));
            program.TryAddAppendage(Card(defaultQuantity: 2));
            program.TryAddAppendage(Card(defaultQuantity: 3));

            program.RemoveAppendageAt(0);

            Assert.AreEqual(2, program.appendages.Count);
            Assert.AreEqual(2, program.GetQuantityAt(0), "the quantities shifted out of step");
            Assert.AreEqual(3, program.GetQuantityAt(1));
        }

        // --- Self-healing --------------------------------------------------------------------
        // The parallel list can legitimately be the wrong length: an older save, a hand-edited
        // scene, or one of the demo bootstraps that appends straight to `appendages` past
        // TryAddAppendage (HardcodedDemoProgram does exactly that, on purpose). None of those
        // may be able to break a golem's tick.

        [Test]
        public void AShortQuantityList_SelfHealsToTheAuthoredDefaults()
        {
            var program = new GolemProgram();
            program.appendages.Add(Card(defaultQuantity: 5));
            program.appendages.Add(Card(defaultQuantity: 7));
            Assert.AreEqual(0, program.appendageQuantities.Count, "precondition: desynced");

            Assert.AreEqual(5, program.GetQuantityAt(0));
            Assert.AreEqual(7, program.GetQuantityAt(1));
            Assert.AreEqual(2, program.appendageQuantities.Count);
        }

        [Test]
        public void ALongQuantityList_IsTruncatedRatherThanThrowing()
        {
            var program = new GolemProgram();
            program.appendages.Add(Card(defaultQuantity: 4));
            program.appendageQuantities.AddRange(new List<int> { 4, 8, 8, 8 });

            Assert.AreEqual(4, program.GetQuantityAt(0));
            Assert.AreEqual(1, program.appendageQuantities.Count);
        }

        [Test]
        public void AddingToADesyncedProgram_RealignsInsteadOfCompoundingTheDrift()
        {
            var program = new GolemProgram();
            program.TryAssignChassis(Chassis(3));
            program.appendages.Add(Card(defaultQuantity: 5)); // added past TryAddAppendage
            program.TryAddAppendage(Card(defaultQuantity: 9));

            Assert.AreEqual(2, program.appendages.Count);
            Assert.AreEqual(5, program.GetQuantityAt(0));
            Assert.AreEqual(9, program.GetQuantityAt(1));
        }

        [Test]
        public void GetQuantityAt_OutOfRange_ReturnsOneRatherThanThrowing()
        {
            var program = new GolemProgram();
            Assert.AreEqual(1, program.GetQuantityAt(0));
            Assert.AreEqual(1, program.GetQuantityAt(-1));
            Assert.DoesNotThrow(() => program.SetQuantityAt(4, 3));
        }

        [Test]
        public void ANullAppendageInTheList_DoesNotBreakTheSelfHeal()
        {
            var program = new GolemProgram();
            program.appendages.Add(null);

            Assert.AreEqual(1, program.GetQuantityAt(0));
        }

        // --- HasAssembleStep, the pure-logistics rule's test ---------------------------------

        [Test]
        public void HasAssembleStep_IsFalseForAPureLogisticsProgram()
        {
            var program = new GolemProgram();
            program.TryAssignChassis(Chassis(2));
            program.TryAddAppendage(Card(AppendageActionType.Haul));
            program.TryAddAppendage(Card(AppendageActionType.Push));

            Assert.IsFalse(program.HasAssembleStep);
        }

        [Test]
        public void HasAssembleStep_IsTrueAssoonAsAnySlotHoldsOne()
        {
            var program = new GolemProgram();
            program.TryAssignChassis(Chassis(3));
            program.TryAddAppendage(Card(AppendageActionType.Haul));
            program.TryAddAppendage(Card(AppendageActionType.Assemble));
            program.TryAddAppendage(Card(AppendageActionType.Push));

            Assert.IsTrue(program.HasAssembleStep);
        }

        [Test]
        public void HasAssembleStep_DoesNotCountLegacyRefine()
        {
            // Refine is the pre-machine-model verb: it is id-routed buffer-to-buffer and never
            // touches the golem's internal stocks, so a Refine golem is still pure logistics as
            // far as Push is concerned.
            var program = new GolemProgram();
            program.TryAssignChassis(Chassis(2));
            program.TryAddAppendage(Card(AppendageActionType.Refine));

            Assert.IsFalse(program.HasAssembleStep);
        }
    }
}
