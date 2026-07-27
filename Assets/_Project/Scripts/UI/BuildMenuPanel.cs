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

        private readonly List<(PlaceableBuilding prefab, Image image)> _rows = new();

        public void Configure(BuildModeController buildModeController) => _buildModeController = buildModeController;

        private void Start() => RebuildUI();

        private void RebuildUI()
        {
            if (_rowContainer == null)
            {
                return;
            }

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

        private void CreateRow(PlaceableBuilding prefab)
        {
            var go = new GameObject(prefab.name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(_rowContainer, false);

            var layoutElement = go.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = 240f;
            layoutElement.preferredHeight = 28f;
            layoutElement.flexibleWidth = 0f;
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

            CreateLabel(go.transform, $"{prefab.name} (Scrap {prefab.ScrapCost}, Brass {prefab.BrassCost})");

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
            }
        }

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
            label.color = Color.white;
            label.fontSize = 12;
            label.raycastTarget = false;
        }
    }
}
