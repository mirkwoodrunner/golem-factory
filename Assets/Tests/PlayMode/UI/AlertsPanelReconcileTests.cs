using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.UI;

namespace GolemFactory.Tests.PlayMode
{
    // Regression cover for the alerts strip's reconciliation, which exists specifically so the
    // strip cannot drift from real golem state. An earlier version cached the golem list at
    // OnEnable and only rebuilt it when an entry turned out to be *destroyed* -- it could not
    // see additions at all. In Sandbox, which starts with zero golems and spawns them at
    // runtime, the cache stayed empty forever and the wholesale clear inside
    // StallTracker.Reconcile actively erased stalls the event path had correctly recorded, so
    // the strip permanently claimed every golem was running.
    public class AlertsPanelReconcileTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
            {
                Object.Destroy(go);
            }
            _spawned.Clear();
        }

        private AlertsPanel CreatePanel()
        {
            var go = new GameObject("AlertsPanel");
            _spawned.Add(go);
            return go.AddComponent<AlertsPanel>();
        }

        // A golem with an ExtractFromNode step and no holders wired stalls as Unconfigured on
        // its first tick -- the cheapest reliable stall that needs no registries.
        private GolemEntity CreateStallingGolem(string id)
        {
            var go = new GameObject(id);
            _spawned.Add(go);
            GolemEntity entity = go.AddComponent<GolemEntity>();
            entity.Configure(id, null);

            var logicCore = ScriptableObject.CreateInstance<LogicCoreDefinition>();
            logicCore.triggerType = TriggerType.AlwaysOn;
            entity.Program.logicCore = logicCore;

            var step = ScriptableObject.CreateInstance<AppendageActionDefinition>();
            step.actionType = AppendageActionType.ExtractFromNode;
            entity.Program.appendages.Add(step);

            return entity;
        }

        [UnityTest]
        public IEnumerator GolemConstructedAfterTheStripEnabled_IsStillReported()
        {
            AlertsPanel panel = CreatePanel();
            yield return null; // let OnEnable run with zero golems in the scene

            Assert.AreEqual(0, panel.Tracker.Count, "precondition: nothing stalled yet");

            // The Sandbox case: the golem does not exist until well after the strip enabled.
            GolemEntity golem = CreateStallingGolem("RuntimeGolem");
            yield return null;
            golem.Tick(0);

            Assert.AreEqual(GolemState.Stalled, golem.Program.State, "precondition: golem stalled");

            // Wait past the reconcile interval. This is the exact window in which the old
            // implementation erased the entry rather than confirming it.
            yield return new WaitForSecondsRealtime(0.85f);

            Assert.AreEqual(1, panel.Tracker.Count,
                "reconcile dropped a runtime-constructed golem's stall");
            Assert.IsTrue(panel.Tracker.IsStalled("RuntimeGolem"));
        }

        [UnityTest]
        public IEnumerator ReconcileKeepsReportingAcrossRepeatedPasses()
        {
            AlertsPanel panel = CreatePanel();
            yield return null;

            GolemEntity golem = CreateStallingGolem("PersistentGolem");
            yield return null;
            golem.Tick(0);

            // Several reconcile windows -- a stall must not flicker in and out of the strip.
            for (int i = 0; i < 3; i++)
            {
                yield return new WaitForSecondsRealtime(0.6f);
                Assert.AreEqual(1, panel.Tracker.Count, "stall dropped on reconcile pass " + i);
            }
        }

        [UnityTest]
        public IEnumerator GolemThatRecovers_IsDroppedFromTheStrip()
        {
            AlertsPanel panel = CreatePanel();
            yield return null;

            GolemEntity golem = CreateStallingGolem("RecoveringGolem");
            yield return null;
            golem.Tick(0);
            yield return new WaitForSecondsRealtime(0.85f);
            Assert.AreEqual(1, panel.Tracker.Count, "precondition: stall registered");

            // Clear the fault at the source, without publishing a GolemResumedEvent -- the
            // strip must re-derive this from state rather than wait to be told.
            golem.Program.State = GolemState.Idle;
            yield return new WaitForSecondsRealtime(0.85f);

            Assert.AreEqual(0, panel.Tracker.Count, "recovered golem stayed on the strip");
        }
    }
}
