using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.UI;
using NUnit.Framework;

namespace GolemFactory.Tests.UI
{
    // The Workbench's read head: on the card the golem is on, and only while the bench shows
    // the cards it runs.
    public class WorkbenchReadHeadTests
    {
        private static WorkbenchSession Engaged(out GolemEntity golem, out AppendageActionDefinition a, out AppendageActionDefinition b)
        {
            golem = new GolemEntity();
            golem.Configure("Golem", null);
            var chassis = new ChassisDefinition { maxAppendageSlots = 3 };
            var core = new LogicCoreDefinition { name = "Core" };
            a = new AppendageActionDefinition { name = "A" };
            b = new AppendageActionDefinition { name = "B" };
            var session = new WorkbenchSession(3);
            session.ConfigureGolem(golem);
            session.ConfigureRoster(new[] { chassis }, new[] { core }, new[] { a, b });
            session.SelectChassis(chassis);
            session.SendToNextSocket(WorkbenchCardRef.FromVault(a));
            session.SendToNextSocket(WorkbenchCardRef.FromVault(b));
            Assert.IsTrue(session.Engage(), session.StatusText);
            return session;
        }

        [Test]
        public void NoGolem_NoHead()
        {
            Assert.AreEqual(WorkbenchSession.NoReadHead, new WorkbenchSession(3).ReadHeadRow);
        }

        [Test]
        public void AnUnprogrammedGolem_HasNoHead()
        {
            var golem = new GolemEntity();
            golem.Configure("Golem", null);
            var session = new WorkbenchSession(3);
            session.ConfigureGolem(golem);
            Assert.AreEqual(WorkbenchSession.NoReadHead, session.ReadHeadRow);
        }

        [Test]
        public void TheHead_IsOnTheStepTheGolemIsOn_AndOnStepOneBetweenCycles()
        {
            WorkbenchSession session = Engaged(out GolemEntity golem, out _, out _);

            golem.Program.State = GolemState.Idle;
            golem.Program.CurrentStepIndex = 0;
            Assert.AreEqual(0, session.ReadHeadRow, "no trigger row: between cycles the head waits on step 1");

            golem.Program.State = GolemState.Running;
            golem.Program.CurrentStepIndex = 0;
            Assert.AreEqual(0, session.ReadHeadRow);
            golem.Program.CurrentStepIndex = 1;
            Assert.AreEqual(1, session.ReadHeadRow);

            golem.Program.State = GolemState.Stalled;
            Assert.AreEqual(1, session.ReadHeadRow, "stopped on a step, the head stays on it");
        }

        [Test]
        public void ADraftThatHasMovedOn_HasNoHead_UntilItIsEngaged()
        {
            WorkbenchSession session = Engaged(out GolemEntity golem, out AppendageActionDefinition a, out AppendageActionDefinition b);
            golem.Program.State = GolemState.Running;

            session.RemoveFromSlot(WorkbenchCardRef.FromSocket(b, 1));
            Assert.AreEqual(WorkbenchSession.NoReadHead, session.ReadHeadRow, "the golem still runs A then B; the bench shows A");

            session.SendToNextSocket(WorkbenchCardRef.FromVault(b));
            Assert.AreEqual(0, session.ReadHeadRow, "the same cards in the same order again");

            session.RemoveFromSlot(WorkbenchCardRef.FromSocket(a, 0));
            session.RemoveFromSlot(WorkbenchCardRef.FromSocket(b, 1));
            session.SendToNextSocket(WorkbenchCardRef.FromVault(b));
            session.SendToNextSocket(WorkbenchCardRef.FromVault(a));
            Assert.AreEqual(WorkbenchSession.NoReadHead, session.ReadHeadRow, "the same cards, the other order");
        }

        [Test]
        public void AGapInTheDraft_PutsTheHeadOnTheSocketTheCardSitsIn()
        {
            // A chassis of three steps, the program's two cards in sockets 0 and 2.
            var golem = new GolemEntity();
            golem.Configure("Golem", null);
            var chassis = new ChassisDefinition { maxAppendageSlots = 3 };
            var core = new LogicCoreDefinition { name = "Core" };
            var a = new AppendageActionDefinition { name = "A" };
            var b = new AppendageActionDefinition { name = "B" };
            var filler = new AppendageActionDefinition { name = "Filler" };
            var session = new WorkbenchSession(3);
            session.ConfigureGolem(golem);
            session.ConfigureRoster(new[] { chassis }, new[] { core }, new[] { a, b, filler });
            session.SelectChassis(chassis);
            session.SendToNextSocket(WorkbenchCardRef.FromVault(a));
            session.SendToNextSocket(WorkbenchCardRef.FromVault(b));
            Assert.IsTrue(session.Engage(), session.StatusText);
            session.RemoveFromSlot(WorkbenchCardRef.FromSocket(b, 1));
            session.SendToNextSocket(WorkbenchCardRef.FromVault(filler));
            session.SendToNextSocket(WorkbenchCardRef.FromVault(b));
            session.RemoveFromSlot(WorkbenchCardRef.FromSocket(filler, 1));
            Assume.That(session.DraftAppendageAt(1), Is.Null);
            Assume.That(session.DraftAppendageAt(2), Is.EqualTo(b));

            golem.Program.State = GolemState.Running;
            golem.Program.CurrentStepIndex = 1;
            Assert.AreEqual(2, session.ReadHeadRow);
        }

        [Test]
        public void TheSweep_CrossesTheCard_AndIsGone()
        {
            Assert.AreEqual(0f, WorkbenchReadHead.Sweep(0f));
            Assert.AreEqual(1f, WorkbenchReadHead.Sweep(WorkbenchReadHead.SweepSeconds));
            Assert.AreEqual(1f, WorkbenchReadHead.Sweep(-1f));
            Assert.AreEqual(1f, WorkbenchReadHead.Sweep(0f, motion: false), "Reduced motion: no sweep");
            float last = 0f;
            for (float age = 0f; age < WorkbenchReadHead.SweepSeconds; age += 1f / 240f)
            {
                float at = WorkbenchReadHead.Sweep(age);
                Assert.GreaterOrEqual(at, last, "it only goes across");
                last = at;
            }
            Assert.Greater(last, 0.97f, "no jump at the end");
        }
    }
}
