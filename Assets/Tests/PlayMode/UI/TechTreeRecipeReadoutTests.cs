using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using TMPro;
using GolemFactory.Economy;
using GolemFactory.Progression;
using GolemFactory.PunchCards;
using GolemFactory.UI;

namespace GolemFactory.Tests.PlayMode
{
    /// <summary>
    /// The Ledger's recipe readout (docs/cozy-automation-design.md §4a) as it actually reaches
    /// the screen. <c>RecipeLedgerTests</c> pins the strings; this pins the binding.
    ///
    /// <para>
    /// Worth its own suite because this project has twice been bitten by a UI change that was
    /// present, active and correctly configured and still drew nothing -- a <c>RectTransform</c>
    /// with no <c>Canvas</c> ancestor, and a prefab value a scene override silently replaced. A
    /// serialized field says nothing about whether the player can read it.
    /// </para>
    /// </summary>
    public class TechTreeRecipeReadoutTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        private TechTreePanel BuildPanel(out TechTreeProgressTracker tracker)
        {
            _root = new GameObject("Root", typeof(RectTransform), typeof(Canvas));

            // Viewport then Chart, mirroring TechTreeAuthoring's real hierarchy -- the readout
            // parents itself to the chart's PARENT on purpose (RebuildChart destroys the chart's
            // children, and the chart is the scrolling content), so a flat harness would test a
            // different arrangement from the one that ships.
            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(_root.transform, false);
            var chart = new GameObject("Chart", typeof(RectTransform));
            chart.transform.SetParent(viewport.transform, false);
            var legend = new GameObject("Legend", typeof(RectTransform));
            legend.transform.SetParent(_root.transform, false);
            var header = new GameObject("Header", typeof(RectTransform), typeof(TextMeshProUGUI));
            header.transform.SetParent(_root.transform, false);

            var trackerGo = new GameObject("Tracker");
            trackerGo.transform.SetParent(_root.transform, false);
            tracker = trackerGo.AddComponent<TechTreeProgressTracker>();

            var panelGo = new GameObject("Panel", typeof(RectTransform));
            panelGo.transform.SetParent(_root.transform, false);
            TechTreePanel panel = panelGo.AddComponent<TechTreePanel>();
            panel.Configure(tracker);
            panel.ConfigureUI(
                (RectTransform)chart.transform,
                (RectTransform)legend.transform,
                header.GetComponent<TextMeshProUGUI>());
            return panel;
        }

        // R4 Iron Smelting, built in code rather than loaded, so the test does not also depend on
        // whatever the .asset happens to be tuned to this week.
        private static RecipeDefinition IronSmelting()
        {
            var recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
            recipe.inputs.Add(new RecipeIngredient(ItemType.Scrap, 2));
            recipe.inputs.Add(new RecipeIngredient(ItemType.Coke, 1));
            recipe.outputItemType = ItemType.IronPlate;
            recipe.outputQuantity = 2;
            recipe.byproductItemType = ItemType.Slag;
            recipe.byproductQuantity = 1;
            recipe.durationTicks = 24;
            // THE ASSET NAME IS THE IDENTITY the readout resolves by -- RecipeLedger matches a
            // node's leading recipe number against it. CreateInstance leaves it blank, so a
            // harness that skipped this would silently test the catalog's fallback line instead
            // of the recipe lookup, which is exactly what it did the first time.
            recipe.name = "R4_IronSmelting";
            return recipe;
        }

        [UnityTest]
        public IEnumerator ClickingARecipeNodeShowsItsRatioAndByproduct()
        {
            TechTreePanel panel = BuildPanel(out TechTreeProgressTracker _);
            panel.ConfigureRecipeReadout(
                new[] { IronSmelting() }, null, "FactoryStockpile", 10f);
            yield return null;

            panel.RebuildChart();
            panel.SelectNode("r4.ironsmelting");

            string body = panel.DetailBodyText;
            StringAssert.Contains("2 Scrap + 1 Coke -> 2 Iron Plate + 1 Slag", body);
            StringAssert.Contains("1 Slag per 2 Iron Plate", body);
            StringAssert.Contains("24 ticks", body);
        }

        [UnityTest]
        public IEnumerator TheReadoutIsParentedOutsideTheChart_SoARebuildDoesNotDestroyIt()
        {
            // RebuildChart clears every child of chartRoot. A readout living there would vanish
            // on the next state change, which is the sort of thing that only shows up in play.
            TechTreePanel panel = BuildPanel(out TechTreeProgressTracker _);
            panel.ConfigureRecipeReadout(new[] { IronSmelting() }, null, "FactoryStockpile", 10f);
            yield return null;

            panel.RebuildChart();
            panel.SelectNode("r4.ironsmelting");
            Assert.IsNotEmpty(panel.DetailBodyText);

            panel.RebuildChart();

            Assert.AreEqual("r4.ironsmelting", panel.SelectedNodeId, "selection survives by id");
            StringAssert.Contains("2 Scrap + 1 Coke", panel.DetailBodyText, "and so does the pane");
        }

        [UnityTest]
        public IEnumerator ANonRecipeNodeKeepsTheCatalogsOwnLine()
        {
            // Chassis, buildings, techniques and milestones have no recipe to resolve. An empty
            // pane would read as a bug; the authored line is the only thing there is to say.
            TechTreePanel panel = BuildPanel(out TechTreeProgressTracker _);
            panel.ConfigureRecipeReadout(new[] { IronSmelting() }, null, "FactoryStockpile", 10f);
            yield return null;

            panel.RebuildChart();
            panel.SelectNode("bldg.slagheap");

            TechTreeCatalog.TryGetNode("bldg.slagheap", out TechTreeNode node);
            Assert.AreEqual(node.Detail, panel.DetailBodyText);
        }

        [UnityTest]
        public IEnumerator AnUnmeasuredLineShowsNoLiveRate()
        {
            // With no monitor wired there is nothing measured, and "now: 0.0/min" for a line the
            // player has not built would make an unstarted branch look broken.
            TechTreePanel panel = BuildPanel(out TechTreeProgressTracker _);
            panel.ConfigureRecipeReadout(new[] { IronSmelting() }, null, "FactoryStockpile", 10f);
            yield return null;

            panel.RebuildChart();
            panel.SelectNode("r4.ironsmelting");

            StringAssert.DoesNotContain("now:", panel.DetailBodyText);
        }

        [UnityTest]
        public IEnumerator NothingIsSelectedUntilSomethingIsClicked()
        {
            TechTreePanel panel = BuildPanel(out TechTreeProgressTracker _);
            panel.ConfigureRecipeReadout(new[] { IronSmelting() }, null, "FactoryStockpile", 10f);
            yield return null;

            panel.RebuildChart();

            Assert.IsNull(panel.SelectedNodeId);
            Assert.AreEqual("", panel.DetailBodyText);
        }

        [UnityTest]
        public IEnumerator EveryNodePlaqueIsClickable()
        {
            // The plaque's raycastTarget was OFF before the readout existed. A Button on a
            // graphic that does not take raycasts is a button nothing can press, and it would
            // look perfectly correct in the Inspector.
            TechTreePanel panel = BuildPanel(out TechTreeProgressTracker _);
            panel.ConfigureRecipeReadout(new[] { IronSmelting() }, null, "FactoryStockpile", 10f);
            yield return null;

            panel.RebuildChart();

            var chart = _root.transform.Find("Viewport/Chart");
            int clickable = 0;
            foreach (Button button in chart.GetComponentsInChildren<Button>(true))
            {
                Assert.IsNotNull(button.targetGraphic, button.name + " has no target graphic");
                Assert.IsTrue(button.targetGraphic.raycastTarget, button.name + " takes no raycasts");
                clickable++;
            }

            Assert.AreEqual(TechTreeCatalog.Nodes.Count, clickable, "one clickable plaque per node");
        }
    }
}
