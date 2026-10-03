using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;
using GolemFactory.Buildings;
using GolemFactory.Player;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Tests.PlayMode
{
    /// <summary>
    /// The build menu's Demolish row: it exists, it toggles the wrecking bar the same way a
    /// placeable row toggles a placeable, and adding it does not push the menu off the bottom
    /// of the screen.
    ///
    /// <para>
    /// PlayMode because <c>RebuildUI</c> runs from <c>Start</c> and builds real UGUI
    /// GameObjects -- the same reason <c>HudScreenExclusivityTests</c> lives here.
    /// </para>
    /// </summary>
    public class BuildMenuDemolishRowTests
    {
        private const float RowHeight = 30f;

        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        [UnityTest]
        public IEnumerator TheMenu_OffersDemolish_BelowEveryPlaceable()
        {
            (BuildMenuPanel _, RectTransform container) = Build(placeables: 9);
            yield return null;

            Assert.AreEqual(10, container.childCount, "nine placeables plus the wrecking bar");
            Assert.AreEqual("Demolish", container.GetChild(container.childCount - 1).name,
                "it sits under the things it undoes");
        }

        [UnityTest]
        public IEnumerator TheDemolishRow_TogglesTheWreckingBar()
        {
            (BuildMenuPanel panel, RectTransform container) = Build(placeables: 2);
            yield return null;

            BuildModeController controller = _root.GetComponentInChildren<BuildModeController>();
            Button demolish = container.GetChild(container.childCount - 1).GetComponent<Button>();

            demolish.onClick.Invoke();
            Assert.IsTrue(controller.IsDemolishActive);
            Assert.IsNull(controller.ActivePrefab, "picking up the bar puts the placeable down");

            // Clicking the row you already hold puts it down -- the same toggle every other
            // row has, because a tool with only one way out is a tool players get stuck in.
            demolish.onClick.Invoke();
            Assert.IsFalse(controller.IsDemolishActive);
        }

        [UnityTest]
        public IEnumerator PickingAPlaceable_PutsTheWreckingBarDown()
        {
            // The MIRROR of the assertion above, and it was missing -- which is exactly how the
            // bug shipped. The test pinned "the bar puts the placeable down" and nothing pinned
            // the other direction, so selecting Demolish and then a placeable left BOTH in hand.
            (BuildMenuPanel panel, RectTransform container) = Build(placeables: 2);
            yield return null;

            BuildModeController controller = _root.GetComponentInChildren<BuildModeController>();
            Button demolish = container.GetChild(container.childCount - 1).GetComponent<Button>();
            Button firstPlaceable = container.GetChild(0).GetComponent<Button>();

            demolish.onClick.Invoke();
            Assert.IsTrue(controller.IsDemolishActive, "precondition: the bar is in hand");

            firstPlaceable.onClick.Invoke();

            Assert.IsFalse(controller.IsDemolishActive,
                "picking a placeable must put the wrecking bar down");
            Assert.IsNotNull(controller.ActivePrefab);
        }

        [UnityTest]
        public IEnumerator TheMenu_GrowsToFitTheExtraRow()
        {
            // The authored panel had exactly enough room for nine rows. The tenth would have
            // hung off the bottom of a panel with no ScrollRect and no ContentSizeFitter.
            (BuildMenuPanel _, RectTransform container) = Build(placeables: 9);
            yield return null;

            var panelRoot = (RectTransform)container.parent;
            var layout = container.GetComponent<VerticalLayoutGroup>();
            float needed = container.childCount * RowHeight
                + (container.childCount - 1) * layout.spacing
                + layout.padding.top + layout.padding.bottom;
            float available = panelRoot.rect.height - Mathf.Max(0f, -container.sizeDelta.y);

            Assert.GreaterOrEqual(available, needed,
                "every row has to fit inside the panel that draws them");
        }

        private (BuildMenuPanel panel, RectTransform container) Build(int placeables)
        {
            _root = new GameObject("Root");

            var grid = new GameObject("GridMap").AddComponent<GridMapHolder>();
            grid.transform.SetParent(_root.transform);

            var prefabs = new PlaceableBuilding[placeables];
            for (int i = 0; i < placeables; i++)
            {
                var prefab = new GameObject("Placeable" + i + "Prefab").AddComponent<PlaceableBuilding>();
                prefab.transform.SetParent(_root.transform);
                prefabs[i] = prefab;
            }

            var controller = new GameObject("BuildMode").AddComponent<BuildModeController>();
            controller.transform.SetParent(_root.transform);
            controller.Configure(null, grid, prefabs[0], Vector2.one);
            controller.ConfigureEconomy(null, "FactoryStockpile", prefabs);

            // Mirrors the authored panel: a fixed-height body with a stretched row container
            // inset 48px vertically, which is the geometry FitPanelHeightToRows reads.
            var panelRoot = new GameObject("Panel", typeof(RectTransform)).GetComponent<RectTransform>();
            panelRoot.SetParent(_root.transform, false);
            panelRoot.sizeDelta = new Vector2(280f, 356f);

            var container = new GameObject("RowContainer", typeof(RectTransform), typeof(VerticalLayoutGroup))
                .GetComponent<RectTransform>();
            container.SetParent(panelRoot, false);
            container.anchorMin = Vector2.zero;
            container.anchorMax = Vector2.one;
            container.sizeDelta = new Vector2(-20f, -48f);
            container.GetComponent<VerticalLayoutGroup>().spacing = 4f;

            var panel = _root.AddComponent<BuildMenuPanel>();
            panel.Configure(controller);
            typeof(BuildMenuPanel)
                .GetField("_rowContainer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(panel, container);
            typeof(BuildMenuPanel)
                .GetField("_panelRoot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(panel, panelRoot.gameObject);

            return (panel, container);
        }
    }
}
