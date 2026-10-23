using System.Collections.Generic;
using System.Linq;
using GolemFactory.Buildings;
using GolemFactory.ClockTower;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Steam;
using GolemFactory.World;
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
            List<RecipeDefinition> recipes = AuthoredData.All<RecipeDefinition>();

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
            foreach (RecipeDefinition recipe in AuthoredData.All<RecipeDefinition>())
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
                AuthoredData.All<RecipeDefinition>().Count, recipeNodes,
                "Every authored RecipeDefinition should have exactly one node on the track.");
        }

        [Test]
        public void EveryChassisNodeNamesARealChassisAsset()
        {
            var chassisNames = new HashSet<string>();
            foreach (ChassisDefinition chassis in AuthoredData.All<ChassisDefinition>())
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


        [Test]
        public void EveryRecipeNode_QuotesItsRecipe_AsTheDataHasIt()
        {
            // docs/tuning-data-spec.md invariant 12: the Lens went to 3 Glass, and the Ledger's
            // node still said 2. A recipe node's detail is its recipe's inputs and output.
            // By number: node "r10.lensgrinding" is recipe "R10_LensGrinding".
            var recipes = new Dictionary<string, RecipeDefinition>();
            foreach (RecipeDefinition recipe in AuthoredData.All<RecipeDefinition>())
            {
                recipes[recipe.name.Substring(0, recipe.name.IndexOf('_')).ToLowerInvariant()] = recipe;
            }
            int checkedNodes = 0;
            foreach (TechTreeNode node in TechTreeCatalog.Nodes)
            {
                if (node.Kind != TechTreeNodeKind.Recipe)
                {
                    continue;
                }
                string key = node.Id.Substring(0, node.Id.IndexOf('.'));
                Assert.IsTrue(recipes.TryGetValue(key, out RecipeDefinition recipe), $"{node.Id} names no recipe");
                // The details abbreviate goods ("2 Copper + 1 Zinc -> 3 Brass"), so the numbers
                // are what is checked: each input's quantity in order, then the output's.
                string[] sides = node.Detail.Split(new[] { " -> " }, System.StringSplitOptions.None);
                var given = new List<int>();
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(sides[0], @"(\d+) "))
                {
                    given.Add(int.Parse(m.Groups[1].Value));
                }
                var expected = new List<int>();
                foreach (RecipeIngredient input in recipe.inputs)
                {
                    expected.Add(input.quantity);
                }
                CollectionAssert.AreEqual(expected, given, $"{node.Id}'s inputs: '{node.Detail}'");
                if (sides.Length > 1) // the tier-5 nodes list their inputs alone
                {
                    StringAssert.StartsWith(recipe.outputQuantity + " ", sides[1], $"{node.Id}'s output: '{node.Detail}'");
                }
                checkedNodes++;
            }
            Assert.Greater(checkedNodes, 10);
        }
            // --- The Ledger's quoted numbers, held to the data (Phase 3 critique) ----------------
        //
        // The Ledger's texts are hand-written. Phase 3 retuned the Presser, the Hauler, the
        // Overclocker, the Zeppelin, the Mast, the Post, stage 3's Lens and the bench's speed,
        // and the texts kept the old numbers until a critique read them. These read every number
        // the texts quote that a definition owns, so the next retune fails here instead.

        // The Ledger abbreviates some goods; everything else is ItemTiers' display name.
        private static readonly Dictionary<string, string> Abbreviated = new Dictionary<string, string>
        {
            { "Plate", ItemType.IronPlate }, { "Wire", ItemType.CopperWire }, { "Frame", ItemType.FrameSection },
            { "Cog", ItemType.GreatCog }, { "Conduit", ItemType.AetherConduit }, { "Core", ItemType.ChronometerCore },
        };

        private static string GoodNamed(string name)
        {
            if (Abbreviated.TryGetValue(name, out string item))
            {
                return item;
            }
            foreach (string good in KnownGoods)
            {
                if (ItemTiers.DisplayName(good) == name)
                {
                    return good;
                }
            }
            Assert.Fail($"the Ledger names a good '{name}' no item type displays as");
            return null;
        }

        private static IEnumerable<string> KnownGoods =>
            typeof(ItemType).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => (string)f.GetRawConstantValue());

        /// <summary>"60 Scrap + 10 Plate + 5 Gear" as (good, quantity).</summary>
        private static Dictionary<string, int> PriceIn(string text)
        {
            var price = new Dictionary<string, int>();
            foreach (string part in text.Split(new[] { " + " }, System.StringSplitOptions.None))
            {
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(part.Trim(), @"^(\d+) (.+)$");
                Assert.IsTrue(m.Success, $"'{part}' in '{text}' is not 'N Good'");
                string good = GoodNamed(m.Groups[2].Value);
                price[good] = (price.TryGetValue(good, out int q) ? q : 0) + int.Parse(m.Groups[1].Value);
            }
            return price;
        }

        private static Dictionary<string, int> Summed(IEnumerable<RecipeIngredient> cost)
        {
            var price = new Dictionary<string, int>();
            foreach (RecipeIngredient line in cost)
            {
                price[line.itemType] = (price.TryGetValue(line.itemType, out int q) ? q : 0) + line.quantity;
            }
            return price;
        }

        private static TechTreeNode Node(string id)
        {
            Assert.IsTrue(TechTreeCatalog.TryGetNode(id, out TechTreeNode node), id);
            return node;
        }

        private static string[] Segments(TechTreeNode node) =>
            node.Detail.Split(new[] { " · " }, System.StringSplitOptions.None);

        [Test]
        public void EveryChassisNode_QuotesItsChassis_AsTheDataHasIt()
        {
            // "3 slots · 1 input · 60 Scrap + 10 Plate + 5 Gear": slots first, the price last.
            var chassis = new Dictionary<string, ChassisDefinition>();
            foreach (ChassisDefinition c in AuthoredData.All<ChassisDefinition>())
            {
                chassis[c.name] = c;
            }
            int checkedNodes = 0;
            foreach (TechTreeNode node in TechTreeCatalog.Nodes)
            {
                if (node.Kind != TechTreeNodeKind.Chassis)
                {
                    continue;
                }
                Assert.IsTrue(chassis.TryGetValue(node.SignalId, out ChassisDefinition definition), node.Id);
                string[] segments = Segments(node);
                StringAssert.StartsWith(definition.maxAppendageSlots + " slots", segments[0], $"{node.Id}: '{node.Detail}'");
                CollectionAssert.AreEquivalent(Summed(definition.cost), PriceIn(segments[segments.Length - 1]),
                    $"{node.Id}'s price: '{node.Detail}'");
                checkedNodes++;
            }
            Assert.AreEqual(5, checkedNodes, "all five chassis");
        }

        [Test]
        public void TheBoilerNode_QuotesTheSteamRules()
        {
            Assert.AreEqual(
                $"Powers {SteamNetwork.MaxGolemsPerBoiler} golems · {SteamGaugeUtility.CokePerMinutePerGolem} Coke/min each",
                Node("bldg.boiler").Detail);
        }

        [Test]
        public void EveryPricedBuildingNode_QuotesThePlaceablesPrice()
        {
            var placeables = new Dictionary<string, PlaceableBuilding>();
            foreach (PlaceableEntry entry in Data.PlaceableCatalogTests.LoadReal())
            {
                placeables[entry.Key] = entry.Prefab;
            }
            // The node, its prefab: the price is the first " · " segment.
            var priced = new (string node, string prefab)[]
            {
                ("bldg.steampipe", "SteamPipePrefab"),
                ("bldg.belt", "BeltPrefab"),
                ("bldg.depot", "DepotPrefab"),
                ("bldg.freightmast", "FreightMastPrefab"),
            };
            foreach ((string id, string prefab) in priced)
            {
                CollectionAssert.AreEquivalent(Summed(placeables[prefab].Cost), PriceIn(Segments(Node(id))[0]),
                    $"{id}: '{Node(id).Detail}'");
            }

            // The Post names its goods, not their counts: every good it costs, and no other.
            string post = Segments(Node("bldg.pneumaticpost"))[0];
            CollectionAssert.AreEquivalent(
                Summed(placeables["PneumaticPostPrefab"].Cost).Keys,
                post.Split(new[] { " + " }, System.StringSplitOptions.None).Select(GoodNamed),
                $"bldg.pneumaticpost: '{post}'");
        }

        [Test]
        public void TheTowerStageNodes_QuoteEachStagesDemand_InOrder()
        {
            var stages = new Dictionary<int, ClockTowerStageDefinition>();
            foreach (ClockTowerStageDefinition stage in AuthoredData.All<ClockTowerStageDefinition>())
            {
                stages[stage.stageNumber] = stage;
            }
            var full = new Dictionary<string, string>
            {
                { "Frame Section", ItemType.FrameSection }, { "Great Cog", ItemType.GreatCog },
            };
            for (int number = 1; number <= 4; number++)
            {
                TechTreeNode node = Node("tower.stage" + number);
                var quoted = new List<(string, int)>();
                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(node.Detail, @"([A-Z][A-Za-z ]*?) @ (\d+)"))
                {
                    string name = m.Groups[1].Value;
                    quoted.Add((full.TryGetValue(name, out string good) ? good : GoodNamed(name), int.Parse(m.Groups[2].Value)));
                }
                CollectionAssert.AreEqual(stages[number].demands.Select(d => (d.itemType, d.ratePerMinute)).ToList(), quoted,
                    $"{node.Id}: '{node.Detail}'");
            }
        }

        [Test]
        public void NoLedgerText_EstimatesAGolemCount()
        {
            // The user's call (tuning-data-spec §19.3): a hand-written "~96 golems" was a guess
            // the data never checked, so the Ledger names no factory size at all.
            var estimate = new System.Text.RegularExpressions.Regex(@"~\s*\d+\s+golems|factory of");
            foreach (TechTreePhase phase in TechTreeCatalog.Phases)
            {
                Assert.IsFalse(estimate.IsMatch(phase.Goal), $"{phase.Title}: '{phase.Goal}'");
            }
            foreach (TechTreeNode node in TechTreeCatalog.Nodes)
            {
                Assert.IsFalse(estimate.IsMatch(node.Detail), $"{node.Id}: '{node.Detail}'");
                Assert.IsFalse(node.Detail.TrimEnd().EndsWith("·"), $"{node.Id}: dangling separator in '{node.Detail}'");
            }
        }

        [Test]
        public void TheBenchBaysPackAndFloorNodes_QuoteTheirRules()
        {
            StringAssert.Contains($"at {HandCrankRules.SpeedPercent}% speed", Node("bench.handcrank").Detail);

            var bay = new AssemblyBayStructure();
            Assert.AreEqual(
                $"Golem cap {AssemblyBayStructure.DefaultSlots} · +{AssemblyBayStructure.SlotsPerUpgrade} for "
                + string.Join(" + ", bay.UpgradeCost.Select(c => c.quantity + " " + (c.itemType == ItemType.IronPlate ? "Plate" : ItemTiers.DisplayName(c.itemType)))),
                Node("bays.assembly").Detail);

            SandboxSetup setup = World.SandboxSetupTests.LoadReal();
            string[] counts = { "zero", "one", "two", "three", "four", "five", "six", "seven" };
            Assert.AreEqual(
                $"Carries {setup.packCapacityPerType} · up to {setup.packUpgrades.Last().capacityPerType} in "
                + $"{counts[setup.packUpgrades.Count]} upgrades",
                Node("pack.upgrades").Detail);
            StringAssert.StartsWith(
                $"+{setup.floorExpansion.rowsPerPurchase} rows · {counts[setup.floorExpansion.purchases.Count]} purchases",
                Node("bldg.floorexpansion").Detail);
        }
    }
}
