using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Economy;
using GolemFactory.Progression;

namespace GolemFactory.Tests.EditMode
{
    // The three rules that decide what a card on the chart says (TechTreeStatusRules), plus the
    // monotonic ledger under them. Run against small hand-built graphs where the expected answer
    // is obvious, and then once against the real catalog to prove a fresh save reads sensibly.
    public class TechTreeStatusRulesTests
    {
        private static TechTreeNode Node(
            string id, string[] prerequisites,
            TechTreeUnlockSignal signal = TechTreeUnlockSignal.Item,
            string signalId = "Widget",
            bool planned = false) =>
            new TechTreeNode(
                id, id, "", TechTreeNodeKind.Recipe, 0, 0, signal, signalId, prerequisites,
                isPlanned: planned);

        private static TechTreeNodeState[] Resolve(
            IReadOnlyList<TechTreeNode> nodes, TechTreeProgressLedger ledger)
        {
            var states = new TechTreeNodeState[nodes.Count];
            TechTreeStatusRules.Resolve(nodes, ledger, states);
            return states;
        }

        [Test]
        public void RootWithNoPrerequisitesIsAvailableOnAnEmptyLedger()
        {
            var nodes = new[] { Node("root", new string[0]) };

            Assert.AreEqual(TechTreeNodeState.Available, Resolve(nodes, new TechTreeProgressLedger())[0]);
        }

        [Test]
        public void ObservingTheSignalResearchesTheNode()
        {
            var nodes = new[] { Node("root", new string[0], signalId: ItemType.IronPlate) };
            var ledger = new TechTreeProgressLedger();
            ledger.RecordItem(ItemType.IronPlate);

            Assert.AreEqual(TechTreeNodeState.Researched, Resolve(nodes, ledger)[0]);
        }

        [Test]
        public void AnUnmetPrerequisiteKeepsANodeLocked()
        {
            var nodes = new[]
            {
                Node("root", new string[0], signalId: "A"),
                Node("leaf", new[] { "root" }, signalId: "B")
            };

            Assert.AreEqual(TechTreeNodeState.Locked, Resolve(nodes, new TechTreeProgressLedger())[1]);
        }

        [Test]
        public void ResearchingThePrerequisiteMakesTheLeafAvailable()
        {
            var nodes = new[]
            {
                Node("root", new string[0], signalId: "A"),
                Node("leaf", new[] { "root" }, signalId: "B")
            };
            var ledger = new TechTreeProgressLedger();
            ledger.RecordItem("A");

            TechTreeNodeState[] states = Resolve(nodes, ledger);
            Assert.AreEqual(TechTreeNodeState.Researched, states[0]);
            Assert.AreEqual(TechTreeNodeState.Available, states[1]);
        }

        [Test]
        public void AnObservedSignalOutranksAnUnmetPrerequisite()
        {
            // The world is the authority: a player who holds the good has made it, whatever the
            // chart thought about the order they would get there in.
            var nodes = new[]
            {
                Node("root", new string[0], signalId: "A"),
                Node("leaf", new[] { "root" }, signalId: "B")
            };
            var ledger = new TechTreeProgressLedger();
            ledger.RecordItem("B");

            TechTreeNodeState[] states = Resolve(nodes, ledger);
            Assert.AreEqual(TechTreeNodeState.Available, states[0], "root is still merely available");
            Assert.AreEqual(TechTreeNodeState.Researched, states[1]);
        }

        [Test]
        public void SignallessTechniqueResearchesFromItsPrerequisitesAlone()
        {
            var nodes = new[]
            {
                Node("root", new string[0], signalId: "A"),
                Node("technique", new[] { "root" }, TechTreeUnlockSignal.None, null)
            };
            var ledger = new TechTreeProgressLedger();
            ledger.RecordItem("A");

            Assert.AreEqual(TechTreeNodeState.Researched, Resolve(nodes, ledger)[1]);
        }

        [Test]
        public void PlannedNodeStopsAtAvailableEvenWithEveryPrerequisiteMet()
        {
            // The load-bearing one. A planned node describes something the build does not have,
            // so no signal for it can ever arrive; promoting it on prerequisites alone would have
            // the chart report an unlock that does not exist.
            var nodes = new[]
            {
                Node("root", new string[0], signalId: "A"),
                Node("planned", new[] { "root" }, TechTreeUnlockSignal.None, null, planned: true)
            };
            var ledger = new TechTreeProgressLedger();
            ledger.RecordItem("A");

            Assert.AreEqual(TechTreeNodeState.Available, Resolve(nodes, ledger)[1]);
        }

        [Test]
        public void PlannedNodeIsNotResearchedEvenIfItsSignalSomehowArrives()
        {
            var nodes = new[] { Node("planned", new string[0], signalId: "A", planned: true) };
            var ledger = new TechTreeProgressLedger();
            ledger.RecordItem("A");

            Assert.AreEqual(TechTreeNodeState.Available, Resolve(nodes, ledger)[0]);
        }

        [Test]
        public void ResolutionDoesNotDependOnTableOrder()
        {
            // The catalog happens to be authored in dependency order. The fixpoint is what makes
            // that a convenience rather than a load-bearing assumption.
            var forwards = new[]
            {
                Node("a", new string[0], signalId: "A"),
                Node("b", new[] { "a" }, TechTreeUnlockSignal.None, null),
                Node("c", new[] { "b" }, TechTreeUnlockSignal.None, null)
            };
            var backwards = new[] { forwards[2], forwards[1], forwards[0] };

            var ledger = new TechTreeProgressLedger();
            ledger.RecordItem("A");

            Assert.AreEqual(TechTreeNodeState.Researched, Resolve(forwards, ledger)[2]);
            Assert.AreEqual(TechTreeNodeState.Researched, Resolve(backwards, ledger)[0]);
        }

        [Test]
        public void UnknownPrerequisiteIdLocksTheBranchRatherThanUnlockingIt()
        {
            var nodes = new[] { Node("leaf", new[] { "typo" }) };

            Assert.AreEqual(TechTreeNodeState.Locked, Resolve(nodes, new TechTreeProgressLedger())[0]);
        }

        [Test]
        public void LedgerOnlyEverGrows()
        {
            var ledger = new TechTreeProgressLedger();
            Assert.IsTrue(ledger.RecordItem(ItemType.Brass));
            Assert.IsFalse(ledger.RecordItem(ItemType.Brass), "second record is not a change");
            Assert.IsTrue(ledger.HasItem(ItemType.Brass));

            int versionBefore = ledger.Version;
            ledger.RecordItem(ItemType.Brass);
            Assert.AreEqual(versionBefore, ledger.Version, "version only moves on a real change");
        }

        [Test]
        public void TowerStageSignalNeedsThatManyStagesCompleted()
        {
            var stage3 = new TechTreeNode(
                "stage3", "Stage 3", "", TechTreeNodeKind.Milestone, 0, 0,
                TechTreeUnlockSignal.TowerStage, "3", new string[0]);

            var ledger = new TechTreeProgressLedger();
            ledger.RecordCompletedTowerStages(2);
            Assert.IsFalse(ledger.SignalObserved(stage3));

            ledger.RecordCompletedTowerStages(3);
            Assert.IsTrue(ledger.SignalObserved(stage3));

            ledger.RecordCompletedTowerStages(1);
            Assert.IsTrue(ledger.SignalObserved(stage3), "a lower report never walks progress back");
        }

        [Test]
        public void FreshSaveOffersTheColdWorkshopAndNothingDeeper()
        {
            // The whole chart against an empty ledger: phase I's two rootless nodes are the only
            // thing offered, and nothing from phase VI has leaked forward.
            TechTreeNodeState[] states = Resolve(TechTreeCatalog.Nodes, new TechTreeProgressLedger());

            Assert.AreEqual(0, TechTreeStatusRules.CountResearched(states));

            for (int i = 0; i < TechTreeCatalog.Nodes.Count; i++)
            {
                TechTreeNode node = TechTreeCatalog.Nodes[i];
                if (states[i] != TechTreeNodeState.Available)
                {
                    continue;
                }

                Assert.AreEqual(
                    0, node.PhaseIndex,
                    $"'{node.Id}' is available on a fresh save but sits in phase {node.PhaseIndex}.");
            }
        }

        [Test]
        public void NextObjectiveNamesTheFirstAvailableNodeInReadingOrder()
        {
            TechTreeNodeState[] states = Resolve(TechTreeCatalog.Nodes, new TechTreeProgressLedger());
            TechTreeNode next = TechTreeStatusRules.FindNextObjective(TechTreeCatalog.Nodes, states);

            Assert.IsNotNull(next);
            Assert.AreEqual("bench.handcrank", next.Id, "the arc opens at the Hand-Crank Bench");
        }

        [Test]
        public void NextObjectiveSkipsPlannedNodes()
        {
            var nodes = new[]
            {
                Node("planned", new string[0], TechTreeUnlockSignal.None, null, planned: true),
                Node("real", new string[0], signalId: "A")
            };
            TechTreeNodeState[] states = Resolve(nodes, new TechTreeProgressLedger());

            Assert.AreEqual("real", TechTreeStatusRules.FindNextObjective(nodes, states).Id);
        }

        [Test]
        public void BuildingTheFirstScavengerOpensTheBrassPresserBranch()
        {
            // An end-to-end read of the real track: §9's phase I into phase II. The Presser needs
            // the Scavenger plus hand-cranked Plate and Gears, and nothing else on the chart
            // should move until all three are in.
            var ledger = new TechTreeProgressLedger();
            ledger.RecordBuilding(TechTreeCatalog.BuildingHandCrankBench);
            ledger.RecordChassis(TechTreeCatalog.ChassisScavenger);

            Assert.AreEqual(TechTreeNodeState.Locked, StateOf(ledger, "chassis.presser"));

            ledger.RecordItem(ItemType.IronPlate);
            Assert.AreEqual(TechTreeNodeState.Locked, StateOf(ledger, "chassis.presser"));

            ledger.RecordItem(ItemType.Gear);
            Assert.AreEqual(TechTreeNodeState.Available, StateOf(ledger, "chassis.presser"));

            // And the phase after it is still shut: the Hauler needs the Presser itself.
            Assert.AreEqual(TechTreeNodeState.Locked, StateOf(ledger, "chassis.hauler"));
        }

        private static TechTreeNodeState StateOf(TechTreeProgressLedger ledger, string nodeId)
        {
            TechTreeNodeState[] states = Resolve(TechTreeCatalog.Nodes, ledger);
            for (int i = 0; i < TechTreeCatalog.Nodes.Count; i++)
            {
                if (TechTreeCatalog.Nodes[i].Id == nodeId)
                {
                    return states[i];
                }
            }

            Assert.Fail($"No node '{nodeId}'.");
            return TechTreeNodeState.Locked;
        }
    }
}
