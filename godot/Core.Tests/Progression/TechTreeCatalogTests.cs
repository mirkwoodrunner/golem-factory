using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Progression;
using GolemFactory.PunchCards;

namespace GolemFactory.Tests.EditMode
{
    // The tech-tree catalog is a TRANSCRIPTION of docs/progression-design.md §5.2/§6/§9 -- the
    // phase ordering that lives nowhere in the .asset files, because a RecipeDefinition knows its
    // ingredients and not its place in the arc. A transcription drifts unless something holds it
    // to its source, so these tests hold it to two: the assets on disk (every recipe and chassis
    // node must name a real one, and every authored recipe must appear) and the graph's own
    // structural rules (acyclic, resolvable ids, one keystone per phase).
    //
    // Deliberately NOT asserted against a constant inside TechTreeCatalog itself, which would be a
    // tautology -- the same discipline RecipeCatalogTests uses.
    public class TechTreeCatalogTests
    {
        private const string RecipeRoot = "Assets/_Project/ScriptableObjects/Recipes";
        private const string ChassisRoot = "Assets/_Project/ScriptableObjects/Chassis";

        [Test]
        public void EveryPhaseHasNodes()
        {
            for (int phase = 0; phase < TechTreeCatalog.Phases.Count; phase++)
            {
                Assert.IsTrue(
                    CountInPhase(phase) > 0,
                    $"Phase {phase} ({TechTreeCatalog.Phases[phase].Title}) has no nodes.");
            }
        }

        [Test]
        public void EveryPhaseHasExactlyOneKeystone()
        {
            for (int phase = 0; phase < TechTreeCatalog.Phases.Count; phase++)
            {
                int keystones = 0;
                foreach (TechTreeNode node in TechTreeCatalog.Nodes)
                {
                    if (node.PhaseIndex == phase && node.IsKeystone)
                    {
                        keystones++;
                    }
                }

                // Phase V carries two chassis (Overclocker and Zeppelin), which §6 sequences
                // inside one phase, so it is the one column allowed a second keystone.
                int expected = phase == 4 ? 2 : 1;
                Assert.AreEqual(
                    expected, keystones,
                    $"Phase {phase} ({TechTreeCatalog.Phases[phase].Title}) keystone count.");
            }
        }

        [Test]
        public void NodeIdsAreUnique()
        {
            var seen = new HashSet<string>();
            foreach (TechTreeNode node in TechTreeCatalog.Nodes)
            {
                Assert.IsTrue(seen.Add(node.Id), $"Duplicate node id '{node.Id}'.");
            }
        }

        [Test]
        public void EveryPrerequisiteResolvesToAKnownNode()
        {
            foreach (TechTreeNode node in TechTreeCatalog.Nodes)
            {
                for (int i = 0; i < node.Prerequisites.Count; i++)
                {
                    Assert.IsTrue(
                        TechTreeCatalog.TryGetNode(node.Prerequisites[i], out _),
                        $"Node '{node.Id}' names unknown prerequisite '{node.Prerequisites[i]}'.");
                }
            }
        }

        [Test]
        public void EveryRowInAPhaseIsUnique()
        {
            // Two nodes on the same (phase, row) would draw exactly on top of each other, and the
            // chart would silently lose one -- the failure mode is invisible rather than loud.
            var occupied = new HashSet<(int, int)>();
            foreach (TechTreeNode node in TechTreeCatalog.Nodes)
            {
                Assert.IsTrue(
                    occupied.Add((node.PhaseIndex, node.Row)),
                    $"Node '{node.Id}' collides at phase {node.PhaseIndex}, row {node.Row}.");
            }
        }

        [Test]
        public void GraphIsAcyclic()
        {
            // A cycle would leave TechTreeStatusRules' fixpoint with a branch that can never
            // resolve past Locked -- a whole limb of the chart dead with nothing to point at.
            foreach (TechTreeNode node in TechTreeCatalog.Nodes)
            {
                Assert.IsFalse(
                    ReachesItself(node, node.Id, new HashSet<string>()),
                    $"Node '{node.Id}' is part of a prerequisite cycle.");
            }
        }

        [Test]
        public void PrerequisitesNeverPointForward()
        {
            // A prerequisite in a LATER phase would be unreachable in play: the player would have
            // to finish phase V to unlock a phase III node. Same column is fine (that is a
            // within-phase ordering); a later column never is.
            foreach (TechTreeNode node in TechTreeCatalog.Nodes)
            {
                for (int i = 0; i < node.Prerequisites.Count; i++)
                {
                    TechTreeCatalog.TryGetNode(node.Prerequisites[i], out TechTreeNode source);
                    Assert.LessOrEqual(
                        source.PhaseIndex, node.PhaseIndex,
                        $"Node '{node.Id}' (phase {node.PhaseIndex}) requires '{source.Id}' from " +
                        $"later phase {source.PhaseIndex}.");
                }
            }
        }

        [Test]
        public void EveryStringTheChartDrawsIsLatin1()
        {
            // FOURTEEN NODE DETAILS CARRIED U+2192 FROM THE DAY THE CHART WAS WRITTEN, plus one
            // em dash. TMP's default LiberationSans SDF atlas has no entry for either, so every
            // recipe node on the Ledger drew a missing-glyph box where its arrow should be. The
            // constraint is recorded in three other files already (StallDiagnostics,
            // GolemStallIndicator, WorkbenchLoopLabels); this catalog simply never heard about
            // it, and nothing was checking. The middle dot (U+00B7) is in the atlas and stays.
            foreach (TechTreePhase phase in TechTreeCatalog.Phases)
            {
                AssertLatin1(phase.Title, "phase title");
                AssertLatin1(phase.Goal, "phase goal");
            }

            foreach (TechTreeNode node in TechTreeCatalog.Nodes)
            {
                AssertLatin1(node.DisplayName, node.Id + " name");
                AssertLatin1(node.Detail, node.Id + " detail");
            }
        }

        private static void AssertLatin1(string text, string what)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            foreach (char c in text)
            {
                Assert.LessOrEqual(
                    (int)c, 0xFF,
                    $"{what} has a glyph the TMP atlas cannot draw (U+{(int)c:X4}) in \"{text}\"");
            }
        }

        [Test]
        public void EveryRecipeNodeResolvesToARecipeAssetByItsNumber()
        {
            // How the Ledger's readout finds the recipe a node names, and the reason it is done
            // by NUMBER: r4.ironsmelting signals on Slag rather than on Iron Plate -- R4 is the
            // only recipe that makes Slag, which makes it the sharper detector -- so resolving
            // by output found nothing for it and the readout silently fell back to the
            // catalog's hand-written line.
            List<RecipeDefinition> recipes = LoadAll<RecipeDefinition>(RecipeRoot);

            foreach (TechTreeNode node in TechTreeCatalog.Nodes)
            {
                if (node.Kind != TechTreeNodeKind.Recipe)
                {
                    continue;
                }

                Assert.IsNotNull(
                    RecipeLedger.FindByNodeName(recipes, node.DisplayName),
                    $"'{node.DisplayName}' ({node.Id}) resolves to no recipe asset -- a recipe " +
                    "node's display name must lead with its number, e.g. \"R4 Iron Smelting\"");
            }
        }

        [Test]
        public void ARecipeNumberPrefixDoesNotMatchALongerNumber()
        {
            // The underscore in the prefix is what stops "R1" matching R19_WireDrawing.
            Assert.AreEqual("R1_", RecipeLedger.RecipeAssetPrefix("R1 Coking"));
            Assert.AreEqual("R19_", RecipeLedger.RecipeAssetPrefix("R19 Wire Drawing"));
            Assert.IsNull(RecipeLedger.RecipeAssetPrefix("Regulator"));
            Assert.IsNull(RecipeLedger.RecipeAssetPrefix("Clockwork Scavenger"));
            Assert.IsNull(RecipeLedger.RecipeAssetPrefix(""));
        }

        [Test]
        public void EveryRecipeNodeNamesARealRecipeOutput()
        {
            var outputs = new HashSet<string>();
            foreach (RecipeDefinition recipe in LoadAll<RecipeDefinition>(RecipeRoot))
            {
                outputs.Add(recipe.outputItemType);
                if (!string.IsNullOrEmpty(recipe.byproductItemType))
                {
                    outputs.Add(recipe.byproductItemType);
                }
            }

            foreach (TechTreeNode node in TechTreeCatalog.Nodes)
            {
                if (node.Kind != TechTreeNodeKind.Recipe)
                {
                    continue;
                }

                Assert.AreEqual(
                    TechTreeUnlockSignal.Item, node.Signal,
                    $"Recipe node '{node.Id}' must unlock on an item it produces.");
                Assert.IsTrue(
                    outputs.Contains(node.SignalId),
                    $"Recipe node '{node.Id}' unlocks on '{node.SignalId}', which no authored " +
                    "recipe produces.");
            }
        }

        [Test]
        public void EveryAuthoredRecipeAppearsOnTheTrack()
        {
            // The point of the chart is that it is the whole track. A recipe authored and never
            // placed on it is a line of the game the player is never shown.
            int recipeNodes = 0;
            foreach (TechTreeNode node in TechTreeCatalog.Nodes)
            {
                if (node.Kind == TechTreeNodeKind.Recipe)
                {
                    recipeNodes++;
                }
            }

            Assert.AreEqual(
                LoadAll<RecipeDefinition>(RecipeRoot).Count, recipeNodes,
                "Every authored RecipeDefinition should have exactly one node on the track.");
        }

        [Test]
        public void EveryChassisNodeNamesARealChassisAsset()
        {
            var chassisNames = new HashSet<string>();
            foreach (ChassisDefinition chassis in LoadAll<ChassisDefinition>(ChassisRoot))
            {
                chassisNames.Add(chassis.name);
            }

            int chassisNodes = 0;
            foreach (TechTreeNode node in TechTreeCatalog.Nodes)
            {
                if (node.Kind != TechTreeNodeKind.Chassis)
                {
                    continue;
                }

                chassisNodes++;
                Assert.IsTrue(
                    chassisNames.Contains(node.SignalId),
                    $"Chassis node '{node.Id}' names '{node.SignalId}', which is not a chassis " +
                    "asset. Chassis identity is the .asset filename -- a rename is a data change.");
            }

            Assert.AreEqual(chassisNames.Count, chassisNodes, "All five chassis should be on the track.");
        }

        [Test]
        public void PlannedNodesAreTheOnesTheBuildDoesNotHave()
        {
            // docs/open-items.md: the Freight Link and its mast, the Slag Heap, Floor Expansion
            // and the Assembly Bay cap are specified and unbuilt. Pinned by name so that
            // BUILDING one of them fails this test -- which is the reminder to clear the flag,
            // rather than leaving the chart quietly calling a shipped feature "planned".
            //
            // FOUR NAMES HAVE LEFT THIS LIST -- "verb.repeat", "bays.assembly",
            // "verb.freightlink" and "bldg.freightmast" -- and each time this test failed first,
            // which is exactly what it is for: it is the reminder to clear the flag rather than
            // leave the chart quietly calling a shipped feature "planned".
            // EMPTY, and that is the milestone: every node on the chart is now a shipped
            // feature. The flag and this test stay, because the next designed-but-unbuilt thing
            // should be marked the same way rather than quietly drawn as though it existed.
            var expected = new HashSet<string>();

            var actual = new HashSet<string>();
            foreach (TechTreeNode node in TechTreeCatalog.Nodes)
            {
                if (node.IsPlanned)
                {
                    actual.Add(node.Id);
                }
            }

            CollectionAssert.AreEquivalent(expected, actual);
        }

        private static bool ReachesItself(TechTreeNode node, string targetId, HashSet<string> visiting)
        {
            if (!visiting.Add(node.Id))
            {
                return false;
            }

            for (int i = 0; i < node.Prerequisites.Count; i++)
            {
                if (node.Prerequisites[i] == targetId)
                {
                    return true;
                }

                if (TechTreeCatalog.TryGetNode(node.Prerequisites[i], out TechTreeNode source)
                    && ReachesItself(source, targetId, visiting))
                {
                    return true;
                }
            }

            return false;
        }

        private static int CountInPhase(int phase)
        {
            int count = 0;
            foreach (TechTreeNode node in TechTreeCatalog.Nodes)
            {
                if (node.PhaseIndex == phase)
                {
                    count++;
                }
            }
            return count;
        }

        private static List<T> LoadAll<T>(string folder) 
        {
            return AuthoredData.All<T>();
        }
    }
}
