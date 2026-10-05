using System.Collections.Generic;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.UI;
using NUnit.Framework;

namespace GolemFactory.Tests.UI
{
    /// <summary>
    /// Unity's PlayMode AlertsPanelReconcileTests, ported onto Core's <see cref="AlertsStrip"/>
    /// (G8). Unity waited real seconds for its reconcile interval; here the strip is advanced by
    /// the same seconds directly.
    ///
    /// <para>
    /// The bug these pin: the strip cached the golems it could see when it enabled, so in the
    /// Sandbox -- which starts with no golems -- the cache stayed empty, and the reconcile pass
    /// erased stalls the event path had correctly recorded. The strip claimed every golem was
    /// running.
    /// </para>
    /// </summary>
    public class AlertsPanelReconcileTests
    {
        private readonly List<GolemEntity> _roster = new List<GolemEntity>();
        private AlertsStrip _strip;

        [SetUp]
        public void SetUp()
        {
            _roster.Clear();
            _strip = new AlertsStrip(() => _roster);
            _strip.Attach();
        }

        [TearDown]
        public void TearDown() => _strip.Detach();

        // A golem with an ExtractFromNode step and no registries wired stalls as Unconfigured on
        // its first tick -- the cheapest reliable stall.
        private GolemEntity CreateStallingGolem(string id)
        {
            var golem = new GolemEntity();
            golem.Configure(id, null);
            golem.Program.logicCore = new LogicCoreDefinition { triggerType = TriggerType.AlwaysOn };
            golem.Program.appendages.Add(new AppendageActionDefinition { actionType = AppendageActionType.ExtractFromNode });
            _roster.Add(golem);
            return golem;
        }

        [Test]
        public void GolemConstructedAfterTheStripEnabled_IsStillReported()
        {
            _strip.Update(0.016f);
            Assert.AreEqual(0, _strip.Tracker.Count, "precondition: nothing stalled yet");

            // The Sandbox case: the golem does not exist until well after the strip enabled.
            GolemEntity golem = CreateStallingGolem("RuntimeGolem");
            golem.Tick(0);
            Assert.AreEqual(GolemState.Stalled, golem.Program.State, "precondition: golem stalled");

            _strip.Update(0.85f);

            Assert.AreEqual(1, _strip.Tracker.Count, "reconcile dropped a runtime-constructed golem's stall");
            Assert.IsTrue(_strip.Tracker.IsStalled("RuntimeGolem"));
        }

        [Test]
        public void ReconcileKeepsReportingAcrossRepeatedPasses()
        {
            GolemEntity golem = CreateStallingGolem("PersistentGolem");
            golem.Tick(0);

            // Several reconcile windows -- a stall must not flicker in and out of the strip.
            for (int i = 0; i < 3; i++)
            {
                _strip.Update(0.6f);
                Assert.AreEqual(1, _strip.Tracker.Count, "stall dropped on reconcile pass " + i);
            }
        }

        [Test]
        public void GolemThatRecovers_IsDroppedFromTheStrip()
        {
            GolemEntity golem = CreateStallingGolem("RecoveringGolem");
            golem.Tick(0);
            _strip.Update(0.85f);
            Assert.AreEqual(1, _strip.Tracker.Count, "precondition: stall registered");

            // Clear the fault at the source, without publishing a GolemResumedEvent -- the strip
            // must re-derive this from state rather than wait to be told.
            golem.Program.State = GolemState.Idle;
            _strip.Update(0.85f);

            Assert.AreEqual(0, _strip.Tracker.Count, "recovered golem stayed on the strip");
        }
    }
}
