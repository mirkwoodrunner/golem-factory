using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GolemFactory.Buildings;
using GolemFactory.Player;

namespace GolemFactory.UI
{
    // UGUI replacement for the original OnGUI build menu: lists BuildModeController's
    // available placeable prefabs with cost as buttons; picking one just calls
    // SetActivePrefab, so BuildModeController.PlaceOrRemove's existing click-to-place flow
    // (and its cost-gating) is unchanged by this panel's existence. Rows are rebuilt from
    // data on Start (AvailablePrefabs is set once via ConfigureEconomy and never changes at
    // runtime), matching the "always re-render from data" idiom used by WorkbenchController.
    public sealed class BuildMenuPanel : MonoBehaviour
    {
        [SerializeField] private BuildModeController _buildModeController;
        [SerializeField] private RectTransform _rowContainer;
        [SerializeField] private Sprite _rowSprite;
        [SerializeField] private Sprite _rowActiveSprite;

        // The panel body this hides when a full screen is open (as opposed to this whole
        // GameObject, which WorkbenchController's own hideWhileOpen list already toggles --
        // driving the same object from two mechanisms is how they end up fighting).
        [SerializeField] private GameObject _panelRoot;
        [SerializeField] private WorkbenchController _workbenchController;
        [SerializeField] private ManagementPanel _managementPanel;
        [SerializeField] private GolemConstructionPanel _constructionPanel;

        private readonly List<(PlaceableBuilding prefab, Image image)> _rows = new();

        private const float RowHeight = 30f;

        // Lit brass for the tool in hand, dim iron for the rest; captions invert with the
        // plate so both stay legible.
        private static readonly Color SelectedRowColor = new Color(0.88f, 0.68f, 0.35f, 1f);
        private static readonly Color UnselectedRowColor = new Color(0.36f, 0.32f, 0.27f, 1f);
        private static readonly Color SelectedLabelColor = new Color(0.10f, 0.08f, 0.06f, 1f);
        private static readonly Color UnselectedLabelColor = new Color(0.82f, 0.78f, 0.71f, 1f);

        public void Configure(BuildModeController buildModeController) => _buildModeController = buildModeController;

        /// <summary>
        /// Wires the screens this panel must not draw over. Separate from Configure so a scene
        /// that has no full screens (or a test) simply never calls it and the panel stays
        /// always-visible exactly as before.
        /// </summary>
        public void ConfigureScreens(
            GameObject panelRoot, WorkbenchController workbench, ManagementPanel management, GolemConstructionPanel construction)
        {
            _panelRoot = panelRoot;
            _workbenchController = workbench;
            _managementPanel = management;
            _constructionPanel = construction;
        }

        /// <summary>Whether the panel body is currently drawn. Exposed for tests.</summary>
        public bool IsBodyVisible => _panelRoot == null || _panelRoot.activeSelf;

        private void Start()
        {
            if (_panelRoot == null && _rowContainer != null)
            {
                // Default to the row container's own panel parent, so a scene that never calls
                // ConfigureScreens still gets the right object hidden rather than nothing.
                Transform panel = _rowContainer.parent;
                if (panel != null)
                {
                    _panelRoot = panel.gameObject;
                }
            }

            RebuildUI();
            ApplyScreenVisibility();
        }

        // This panel lives on its own Canvas (it is not part of the shared
        // WorkbenchCanvas.prefab, which both scenes instantiate), and two root Canvases with
        // the same sortingOrder resolve by hierarchy order -- which is why it drew straight
        // over the Management screen. Rather than fight sorting across separate canvases, it
        // asks HudScreenPolicy the same question every other screen asks.
        private void Update() => ApplyScreenVisibility();

        private void ApplyScreenVisibility()
        {
            if (_panelRoot == null)
            {
                return;
            }

            bool show = HudScreenPolicy.ShouldShowWorldHud(
                _workbenchController != null && _workbenchController.IsOpen,
                _managementPanel != null && _managementPanel.IsOpen,
                _constructionPanel != null && _constructionPanel.IsOpen);

            if (_panelRoot.activeSelf != show)
            {
                _panelRoot.SetActive(show);
            }
        }

        private void RebuildUI()
        {
            if (_rowContainer == null)
            {
                return;
            }

            NormalizeContainerLayout();

            foreach (Transform child in _rowContainer)
            {
                Destroy(child.gameObject);
            }
            _rows.Clear();

            if (_buildModeController == null || _buildModeController.AvailablePrefabs == null)
            {
                return;
            }

            foreach (PlaceableBuilding prefab in _buildModeController.AvailablePrefabs)
            {
                if (prefab != null)
                {
                    CreateRow(prefab);
                }
            }

            RefreshHighlights();
        }

        // The layout landmine, hit for real here: the container was authored with
        // childControlHeight = false, which makes the layout system ignore every row's
        // LayoutElement.preferredHeight of 28 and keep whatever height the row's RectTransform
        // happened to have. That stayed invisible while rows were flat-coloured Images (which
        // report no useful size) and only appeared once the steampunk row sprites were
        // assigned -- rows rendered at 100px instead of 28px and the menu overflowed off the
        // bottom of the screen. Enforced from code, not just fixed in the scene, so a future
        // Inspector edit cannot silently reintroduce it.
        private void NormalizeContainerLayout()
        {
            var layout = _rowContainer.GetComponent<VerticalLayoutGroup>();
            if (layout == null)
            {
                return;
            }

            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperCenter;
        }

        private void CreateRow(PlaceableBuilding prefab)
        {
            var go = new GameObject(prefab.name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(_rowContainer, false);

            var layoutElement = go.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = 240f;
            layoutElement.preferredHeight = RowHeight;
            layoutElement.minHeight = RowHeight;
            layoutElement.flexibleWidth = 0f;
            // Explicit zero, not an unset -1: an unset flexibleHeight still lets the parent
            // VerticalLayoutGroup hand out whatever slack it has.
            layoutElement.flexibleHeight = 0f;

            Image image = go.GetComponent<Image>();
            if (_rowSprite != null)
            {
                image.sprite = _rowSprite;
                image.type = Image.Type.Sliced;
            }

            PlaceableBuilding captured = prefab;
            go.GetComponent<Button>().onClick.AddListener(() =>
            {
                _buildModeController.SetActivePrefab(captured);
                RefreshHighlights();
            });

            // "GolemConstructionStationPrefab (Scrap 25, Brass 5)" wrapped onto a second line
            // and overflowed the row. The "Prefab" suffix is an asset-naming artifact the
            // player should never see, and ConstructionCostPolicy already words a cost more
            // compactly than the raw fields did.
            CreateLabel(go.transform,
                StripPrefabSuffix(prefab.name) + "   (" + ConstructionCostPolicy.FormatCost(prefab.ScrapCost, prefab.BrassCost) + ")");

            _rows.Add((prefab, image));
        }

        private void RefreshHighlights()
        {
            if (_buildModeController == null)
            {
                return;
            }

            foreach (var (prefab, image) in _rows)
            {
                bool isActive = _buildModeController.ActivePrefab == prefab;
                Sprite target = isActive ? _rowActiveSprite : _rowSprite;
                if (target != null)
                {
                    image.sprite = target;
                }

                // The two row sprites are near-identical brass plates, so swapping them alone
                // left "which tool am I holding" almost unreadable. Brightness is the channel
                // that survives this warm palette -- the same lit/dim split ManagementPanel
                // uses for its selected tab.
                image.color = isActive ? SelectedRowColor : UnselectedRowColor;

                TMPro.TextMeshProUGUI label = image.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
                if (label != null)
                {
                    label.color = isActive ? SelectedLabelColor : UnselectedLabelColor;
                    label.fontStyle = isActive ? TMPro.FontStyles.Bold : TMPro.FontStyles.Normal;
                }
            }
        }

        private static string StripPrefabSuffix(string name) =>
            name != null && name.EndsWith("Prefab") && name.Length > "Prefab".Length
                ? name.Substring(0, name.Length - "Prefab".Length)
                : name;

        private static void CreateLabel(Transform parent, string text)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.alignment = TextAlignmentOptions.Center;
            label.color = UnselectedLabelColor;
            label.fontSize = 11;
            // Single line, always: a wrapped caption is taller than the row that contains it,
            // which is what made the second entry spill past the panel.
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Truncate;
            label.raycastTarget = false;
        }
    }
}
