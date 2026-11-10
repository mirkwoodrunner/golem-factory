using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.UI;
using NUnit.Framework;

namespace GolemFactory.Tests.UI
{
    /// <summary>
    /// The pad's send (docs/steam-deck-design.md D6.W): A on a vault card seats it in the next
    /// socket that takes it, through the mouse's own drag verbs.
    /// </summary>
    public class WorkbenchPadSendTests
    {
        private const int Sockets = 3;

        private static WorkbenchSession Session(int chassisSlots, out ChassisDefinition chassis)
        {
            var golem = new GolemEntity();
            golem.Configure("Golem", null);
            var session = new WorkbenchSession(Sockets);
            session.ConfigureGolem(golem);
            chassis = new ChassisDefinition { maxAppendageSlots = chassisSlots };
            session.ConfigureRoster(new[] { chassis }, new LogicCoreDefinition[0], new AppendageActionDefinition[0]);
            if (chassisSlots > 0)
            {
                session.SelectChassis(chassis);
            }
            return session;
        }

        private static AppendageActionDefinition Card(string name, int haul = 1) =>
            new AppendageActionDefinition { name = name, haulQuantity = haul };

        [Test]
        public void ALogicCore_GoesToTheTrigger_ReplacingTheOneThere()
        {
            WorkbenchSession session = Session(3, out _);
            var first = new LogicCoreDefinition { name = "First" };
            var second = new LogicCoreDefinition { name = "Second" };

            Assert.AreEqual(WorkbenchZone.Trigger.Kind, session.SendToNextSocket(WorkbenchCardRef.FromVault(first))?.Kind);
            session.SendToNextSocket(WorkbenchCardRef.FromVault(second));

            Assert.AreEqual(second, session.DraftLogicCore);
        }

        [Test]
        public void Appendages_FillTheStepsInOrder_AndAGapIsFilledFirst()
        {
            WorkbenchSession session = Session(3, out _);
            AppendageActionDefinition a = Card("A"), b = Card("B"), c = Card("C"), d = Card("D");

            session.SendToNextSocket(WorkbenchCardRef.FromVault(a));
            session.SendToNextSocket(WorkbenchCardRef.FromVault(b));
            session.SendToNextSocket(WorkbenchCardRef.FromVault(c));
            session.RemoveFromSlot(WorkbenchCardRef.FromSocket(b, 1));
            WorkbenchZone? zone = session.SendToNextSocket(WorkbenchCardRef.FromVault(d));

            Assert.AreEqual(1, zone?.AppendageIndex);
            CollectionAssert.AreEqual(new[] { a, d, c }, new[] { session.DraftAppendageAt(0), session.DraftAppendageAt(1), session.DraftAppendageAt(2) });
        }

        [Test]
        public void EveryStepFull_MovesNothing_AndSaysWhy()
        {
            WorkbenchSession session = Session(2, out _);
            AppendageActionDefinition a = Card("A"), b = Card("B"), c = Card("C");
            session.SendToNextSocket(WorkbenchCardRef.FromVault(a));
            session.SendToNextSocket(WorkbenchCardRef.FromVault(b));
            int version = session.Version;

            WorkbenchZone? zone = session.SendToNextSocket(WorkbenchCardRef.FromVault(c));

            Assert.IsNull(zone, "the chassis has two steps; the Workbench's third socket is outside it");
            Assert.IsNull(session.DraftAppendageAt(2));
            Assert.AreEqual(2, session.AssignedAppendageCount);
            Assert.AreEqual(WorkbenchSession.StepsFullMessage, session.StatusText);
            Assert.Greater(session.Version, version, "the status line is redrawn");
        }

        [Test]
        public void NoChassis_SaysToFitOne()
        {
            WorkbenchSession session = Session(0, out _);

            Assert.IsNull(session.SendToNextSocket(WorkbenchCardRef.FromVault(Card("A"))));
            Assert.AreEqual(WorkbenchSession.NoChassisMessage, session.StatusText);
        }

        [Test]
        public void ASend_IsTheMouseDrop_SeatedWithTheCardsDefault_AndTheSocketsUnlit()
        {
            WorkbenchSession sent = Session(3, out _);
            WorkbenchSession dropped = Session(3, out _);
            AppendageActionDefinition haul = Card("Haul", haul: 3);
            int seated = 0;
            sent.CardSeated += () => seated++;

            sent.SendToNextSocket(WorkbenchCardRef.FromVault(haul));
            dropped.BeginCardDrag(WorkbenchCardRef.FromVault(haul));
            dropped.HandleDrop(WorkbenchCardRef.FromVault(haul), WorkbenchZone.Socket(0));

            Assert.AreEqual(1, seated, "the card's snap plays");
            Assert.AreEqual(dropped.DraftQuantityAt(0), sent.DraftQuantityAt(0));
            Assert.AreEqual(dropped.DraftItemTypeAt(0), sent.DraftItemTypeAt(0));
            Assert.AreEqual(DropZoneHighlight.Neutral, sent.AppendageHighlights[0]);
            Assert.AreEqual(DropZoneHighlight.Neutral, sent.LogicHighlight);
        }
    }
}
