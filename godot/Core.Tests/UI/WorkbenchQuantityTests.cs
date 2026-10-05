using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.UI;
using NUnit.Framework;

namespace GolemFactory.Tests.UI
{
    /// <summary>
    /// Unity's PlayMode WorkbenchQuantityTests, ported onto <see cref="WorkbenchSession"/>: the
    /// per-socket batch size the player sets on a Haul card, and the rule that it reaches the
    /// golem only when the lever is pulled. VaultCards_HaveNoStepper is checked by the
    /// `workbench` scenario against the drawn screen -- a vault card in the session is a
    /// WorkbenchCardRef with no socket, so there is nothing for a unit test to find there.
    /// </summary>
    public class WorkbenchQuantityTests
    {
        private static (WorkbenchSession session, GolemEntity golem, ChassisDefinition chassis) Build(params AppendageActionDefinition[] roster)
        {
            var golem = new GolemEntity();
            golem.Configure("Golem", null);
            var chassis = new ChassisDefinition { maxAppendageSlots = 3 };
            var session = new WorkbenchSession(3);
            session.ConfigureGolem(golem);
            session.ConfigureRoster(new[] { chassis }, new LogicCoreDefinition[0], roster);
            return (session, golem, chassis);
        }

        private static AppendageActionDefinition MakeHaul(int authoredDefault) =>
            new AppendageActionDefinition { actionType = AppendageActionType.Haul, haulQuantity = authoredDefault };

        [Test]
        public void EngageGears_CommitsTheDraftedBatchSizeOntoTheGolem()
        {
            AppendageActionDefinition haul = MakeHaul(1);
            var (session, golem, chassis) = Build(haul);
            session.SelectChassis(chassis);
            session.HandleDrop(WorkbenchCardRef.FromVault(haul), WorkbenchZone.Socket(0));
            session.AdjustDraftQuantity(0, +3);
            Assert.AreEqual(4, session.DraftQuantityAt(0), "precondition: the draft moved");

            session.Engage();

            Assert.AreEqual(4, golem.Program.GetQuantityAt(0), "the batch size the player chose never reached the golem");
        }

        [Test]
        public void EngageGears_DoesNotResetBatchSizesBackToTheCardDefaults()
        {
            AppendageActionDefinition haul = MakeHaul(1);
            var (session, golem, chassis) = Build(haul);
            session.SelectChassis(chassis);
            session.HandleDrop(WorkbenchCardRef.FromVault(haul), WorkbenchZone.Socket(0));
            session.AdjustDraftQuantity(0, +7);

            session.Engage();
            Assert.AreEqual(8, golem.Program.GetQuantityAt(0));
            session.Engage();
            Assert.AreEqual(8, golem.Program.GetQuantityAt(0),
                "a second Engage reset the player's batch size to the card's authored default");
        }

        [Test]
        public void RetargetGolem_LoadsTheGolemsCurrentBatchSizesNotTheCardDefaults()
        {
            AppendageActionDefinition haul = MakeHaul(1);
            var (session, golem, _) = Build(haul);
            golem.Program.TryAssignChassis(new ChassisDefinition { maxAppendageSlots = 3 });
            golem.Program.TryAddAppendage(haul);
            golem.Program.SetQuantityAt(0, 6);

            session.RetargetGolem(golem);

            Assert.AreEqual(6, session.DraftQuantityAt(0));
        }

        [Test]
        public void ACardFromTheVault_StartsAtItsOwnAuthoredDefault()
        {
            AppendageActionDefinition haul = MakeHaul(5);
            var (session, _, chassis) = Build(haul);
            session.SelectChassis(chassis);

            session.HandleDrop(WorkbenchCardRef.FromVault(haul), WorkbenchZone.Socket(0));

            Assert.AreEqual(5, session.DraftQuantityAt(0),
                "a fresh card has no history, so the card's own default is the right starting point");
        }

        [Test]
        public void ACardMovedBetweenSockets_CarriesItsBatchSizeWithIt()
        {
            AppendageActionDefinition haul = MakeHaul(1);
            var (session, _, chassis) = Build(haul);
            session.SelectChassis(chassis);
            session.HandleDrop(WorkbenchCardRef.FromVault(haul), WorkbenchZone.Socket(0));
            session.AdjustDraftQuantity(0, +5);

            session.HandleDrop(WorkbenchCardRef.FromSocket(haul, 0), WorkbenchZone.Socket(2));

            Assert.AreEqual(6, session.DraftQuantityAt(2), "the batch size did not follow the card");
            Assert.AreEqual(WorkbenchQuantityPolicy.MinQuantity, session.DraftQuantityAt(0),
                "the vacated socket must not keep the old size for whatever lands there next");
        }

        [Test]
        public void RemovingACard_ClearsThatSlotsBatchSize()
        {
            AppendageActionDefinition haul = MakeHaul(1);
            var (session, _, chassis) = Build(haul);
            session.SelectChassis(chassis);
            session.HandleDrop(WorkbenchCardRef.FromVault(haul), WorkbenchZone.Socket(0));
            session.AdjustDraftQuantity(0, +4);

            session.RemoveFromSlot(WorkbenchCardRef.FromSocket(haul, 0));

            Assert.AreEqual(WorkbenchQuantityPolicy.MinQuantity, session.DraftQuantityAt(0));
        }

        [Test]
        public void AdjustingTheDial_DoesNotTouchTheGolemUntilTheLeverIsPulled()
        {
            AppendageActionDefinition haul = MakeHaul(1);
            var (session, golem, chassis) = Build(haul);
            session.SelectChassis(chassis);
            golem.Program.TryAssignChassis(new ChassisDefinition { maxAppendageSlots = 3 });
            golem.Program.TryAddAppendage(haul);
            golem.Program.SetQuantityAt(0, 2);
            session.RetargetGolem(golem);

            session.AdjustDraftQuantity(0, +6);

            Assert.AreEqual(2, golem.Program.GetQuantityAt(0), "the dial edited the live program instead of the draft");
        }

        [Test]
        public void AHaulCardInASlot_GetsAStepperAndAnAssembleCardDoesNot()
        {
            AppendageActionDefinition haul = MakeHaul(2);
            var assemble = new AppendageActionDefinition { actionType = AppendageActionType.Assemble };
            var (session, _, chassis) = Build(haul, assemble);
            session.SelectChassis(chassis);

            session.HandleDrop(WorkbenchCardRef.FromVault(haul), WorkbenchZone.Socket(0));
            session.HandleDrop(WorkbenchCardRef.FromVault(assemble), WorkbenchZone.Socket(1));

            Assert.IsTrue(session.SlotHasStepper(0), "a Haul slot must carry a batch-size control");
            Assert.IsFalse(session.SlotHasStepper(1),
                "an Assemble slot must not -- its quantity changes nothing, so a dial there is a lie");
        }

        // A program packs its appendages, so a gap in the sockets must not misalign the sizes.
        [Test]
        public void WithAnEmptySocketBetweenTwoCards_EachBatchSizeLandsOnItsOwnStep()
        {
            AppendageActionDefinition first = MakeHaul(1);
            AppendageActionDefinition second = MakeHaul(1);
            var (session, golem, chassis) = Build(first, second);
            session.SelectChassis(chassis);

            session.HandleDrop(WorkbenchCardRef.FromVault(first), WorkbenchZone.Socket(0));
            session.AdjustDraftQuantity(0, +2);   // 3
            session.HandleDrop(WorkbenchCardRef.FromVault(second), WorkbenchZone.Socket(2));
            session.AdjustDraftQuantity(2, +8);   // 9

            session.Engage();

            Assert.AreEqual(2, golem.Program.appendages.Count);
            Assert.AreEqual(3, golem.Program.GetQuantityAt(0));
            Assert.AreEqual(9, golem.Program.GetQuantityAt(1),
                "socket 2's size landed on the wrong step -- the program packs, the sockets do not");
        }
    }
}
