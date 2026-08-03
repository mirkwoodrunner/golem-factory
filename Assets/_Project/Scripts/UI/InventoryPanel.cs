using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GolemFactory.Economy;

namespace GolemFactory.UI
{
    // Stockpile readout: every StorageBuffer's contents, one row per item type, with an
    // icon, the quantity, a relative-magnitude bar and a filling/draining rate.
    //
    // UGUI-based (converted from the original OnGUI panel as part of the Management HUD
    // consolidation) -- content lives under a ScrollRect's Content RectTransform, owned by
    // ManagementPanel's InventoryTab. Rebuilds every row from scratch on Refresh(), same
    // "clear children, rebuild from data" idiom as WorkbenchController.RebuildUI().
    //
    // The rate column is derived entirely here, from BufferThroughputMonitor's sampled
    // history -- StorageBufferRegistry stores current quantities and nothing else, and
    // deliberately stays that way.
    public sealed class InventoryPanel : MonoBehaviour
    {
        // Per-item icon binding. A list of (id, sprite) pairs rather than three named
        // fields because item type ids are bare strings by convention here -- a new item
        // type should need an entry, not a new serialized field and a code change.
        [System.Serializable]
        public sealed class ItemIconBinding
        {
            public string itemType;
            public Sprite icon;
        }

        [SerializeField] private StorageBufferRegistryHolder bufferRegistryHolder;
        [SerializeField] private BufferThroughputMonitor throughputMonitor;
        [SerializeField] private RectTransform content;
        [SerializeField] private ItemIconBinding[] itemIcons = new ItemIconBinding[0];
        [SerializeField] private Sprite headerPlateSprite;

        // Warm brass/parchment against ManagementScreen's near-black iron panel. Brightness
        // is the channel that reads here, so the hierarchy is: quantity brightest, item
        // name mid, section header brass, secondary text dim.
        private static readonly Color HeaderColor = new Color(0.85f, 0.66f, 0.32f, 1f);
        private static readonly Color ItemNameColor = new Color(0.88f, 0.84f, 0.76f, 1f);
        private static readonly Color QuantityColor = new Color(1f, 0.97f, 0.90f, 1f);
        private static readonly Color DimColor = new Color(0.55f, 0.52f, 0.47f, 1f);

        // Direction coding reuses the Workbench's existing copper/teal vocabulary rather
        // than inventing a green/red channel: copper = accumulating, teal = draining. The
        // glyph carries the same information independently, so the readout never depends
        // on color alone.
        private static readonly Color RisingColor = new Color(1f, 0.76f, 0.30f, 1f);
        private static readonly Color FallingColor = new Color(0.45f, 0.80f, 0.82f, 1f);

        private static readonly Color RowTint = new Color(1f, 1f, 1f, 0.035f);
        private static readonly Color HeaderPlateTint = new Color(0.75f, 0.55f, 0.26f, 0.22f);
        private static readonly Color BarTrackColor = new Color(1f, 1f, 1f, 0.09f);
        private static readonly Color BarFillColor = new Color(0.85f, 0.66f, 0.32f, 0.55f);

        private const float RowHeight = 26f;
        private const float HeaderHeight = 24f;
        private const float IconSize = 22f;
        private const float BarWidth = 56f;
        private const float QuantityWidth = 52f;
        private const float RateWidth = 74f;

        public void Configure(StorageBufferRegistryHolder holder) => bufferRegistryHolder = holder;

        public void ConfigureUI(RectTransform contentRoot) => content = contentRoot;

        public void ConfigureThroughput(BufferThroughputMonitor monitor) => throughputMonitor = monitor;

        public void ConfigureIcons(ItemIconBinding[] icons) => itemIcons = icons ?? new ItemIconBinding[0];

        private void Awake()
        {
            // The monitor lives on the same GameObject as the registry holder, so a scene
            // that wires the holder gets the rate readout for free -- no second per-scene
            // reference to forget. An explicitly-assigned monitor still wins.
            if (throughputMonitor == null && bufferRegistryHolder != null)
            {
                throughputMonitor = bufferRegistryHolder.GetComponent<BufferThroughputMonitor>();
            }
        }

        public void Refresh()
        {
            if (content == null)
            {
                return;
            }

            ClearChildren(content);

            if (bufferRegistryHolder == null)
            {
                CreateMessageRow("Inventory unavailable: no storage registry wired.");
                return;
            }

            // Awake doesn't run in EditMode, and a Configure() call can arrive after it, so
            // resolve here too rather than trusting a single early lookup.
            if (throughputMonitor == null)
            {
                throughputMonitor = bufferRegistryHolder.GetComponent<BufferThroughputMonitor>();
            }

            IReadOnlyDictionary<string, StorageBuffer> buffers = bufferRegistryHolder.Registry.Buffers;
            if (buffers.Count == 0)
            {
                CreateMessageRow("No stockpiles yet. Harvest a resource node to start one.");
                return;
            }

            // Buffers are keyed by a Dictionary, whose iteration order is not contractual;
            // sorting keeps a section in the same place between frames, which matters a lot
            // for a list that re-renders every frame while the tab is open.
            var bufferIds = new List<string>(buffers.Keys);
            bufferIds.Sort(string.CompareOrdinal);

            // The magnitude bars are normalized against the largest single stock on screen,
            // because a StorageBuffer has no capacity concept in the simulation -- there is
            // no "full" to draw a percentage against, so the honest comparison is relative.
            int largestQuantity = 0;
            foreach (string bufferId in bufferIds)
            {
                foreach (KeyValuePair<string, int> entry in buffers[bufferId].Quantities)
                {
                    if (entry.Value > largestQuantity)
                    {
                        largestQuantity = entry.Value;
                    }
                }
            }

            foreach (string bufferId in bufferIds)
            {
                StorageBuffer buffer = buffers[bufferId];

                var itemTypes = new List<string>(buffer.Quantities.Keys);
                BufferTrendUtility.SortItemTypes(itemTypes);

                CreateHeaderRow(bufferId, itemTypes.Count);

                if (itemTypes.Count == 0)
                {
                    CreateMessageRow("   empty");
                    continue;
                }

                foreach (string itemType in itemTypes)
                {
                    CreateItemRow(bufferId, itemType, buffer.GetQuantity(itemType), largestQuantity);
                }
            }
        }

        private void CreateHeaderRow(string bufferId, int itemTypeCount)
        {
            GameObject row = CreateRowRoot("Header", HeaderHeight, HeaderPlateTint, headerPlateSprite);

            CreateLabel(row.transform, "Name", bufferId, HeaderColor, 14, FontStyles.Bold,
                TextAlignmentOptions.MidlineLeft, flexibleWidth: 1f, fixedWidth: 0f);
            CreateLabel(row.transform, "Count",
                itemTypeCount == 1 ? "1 type" : itemTypeCount + " types",
                DimColor, 11, FontStyles.Normal,
                TextAlignmentOptions.MidlineRight, flexibleWidth: 0f, fixedWidth: RateWidth);
        }

        private void CreateItemRow(string bufferId, string itemType, int quantity, int largestQuantity)
        {
            GameObject row = CreateRowRoot("Item", RowHeight, RowTint, null);

            CreateIcon(row.transform, itemType);
            CreateLabel(row.transform, "Name", itemType, ItemNameColor, 13, FontStyles.Normal,
                TextAlignmentOptions.MidlineLeft, flexibleWidth: 1f, fixedWidth: 0f);
            CreateMagnitudeBar(row.transform, quantity, largestQuantity);
            CreateLabel(row.transform, "Quantity", quantity.ToString(), QuantityColor, 15, FontStyles.Bold,
                TextAlignmentOptions.MidlineRight, flexibleWidth: 0f, fixedWidth: QuantityWidth);
            CreateRateLabel(row.transform, bufferId, itemType);
        }

        private void CreateRateLabel(Transform parent, string bufferId, string itemType)
        {
            float rate = 0f;
            bool hasReading = throughputMonitor != null
                && throughputMonitor.TryGetRatePerMinute(bufferId, itemType, out rate);
            if (!hasReading)
            {
                // Not "0/min": no reading yet and a genuinely flat stock are different
                // claims, and printing the second when we only know the first is a lie the
                // player would plan around.
                CreateLabel(parent, "Rate", "--", DimColor, 12, FontStyles.Normal,
                    TextAlignmentOptions.MidlineRight, flexibleWidth: 0f, fixedWidth: RateWidth);
                return;
            }

            StockTrend trend = BufferTrendUtility.Classify(rate);
            Color color = trend == StockTrend.Rising ? RisingColor
                : trend == StockTrend.Falling ? FallingColor
                : DimColor;

            CreateLabel(parent, "Rate",
                BufferTrendUtility.TrendGlyph(trend) + " " + BufferTrendUtility.FormatRate(rate),
                color, 12, trend == StockTrend.Steady ? FontStyles.Normal : FontStyles.Bold,
                TextAlignmentOptions.MidlineRight, flexibleWidth: 0f, fixedWidth: RateWidth);
        }

        private void CreateMessageRow(string message)
        {
            GameObject row = CreateRowRoot("Message", RowHeight, Color.clear, null);
            CreateLabel(row.transform, "Label", message, DimColor, 12, FontStyles.Italic,
                TextAlignmentOptions.MidlineLeft, flexibleWidth: 1f, fixedWidth: 0f);
        }

        // Every row goes through here so the layout landmine is fixed in exactly one place:
        // a flat-color Image reports no useful size to the layout system, so a
        // mis-configured LayoutGroup stays invisible until a child gets a real sprite (the
        // item icons below) and then rows balloon to the sprite's native size. The three
        // settings that matter are childControlWidth/Height true, childForceExpandWidth
        // false, and an explicit LayoutElement.flexibleHeight of 0 (-1/"unset" still lets
        // the parent VerticalLayoutGroup hand out leftover space).
        private GameObject CreateRowRoot(string name, float height, Color tint, Sprite plateSprite)
        {
            var row = new GameObject(name, typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(content, false);

            LayoutElement rowElement = row.GetComponent<LayoutElement>();
            rowElement.preferredHeight = height;
            rowElement.minHeight = height;
            rowElement.flexibleHeight = 0f;

            Image background = row.GetComponent<Image>();
            background.color = tint;
            background.raycastTarget = false;
            if (plateSprite != null)
            {
                background.sprite = plateSprite;
                background.type = Image.Type.Sliced;
            }

            HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.spacing = 6f;
            layout.padding = new RectOffset(6, 6, 0, 0);
            return row;
        }

        private void CreateIcon(Transform parent, string itemType)
        {
            // The slot is created whether or not a sprite resolves, so every row's text
            // starts at the same x -- a ragged left edge is exactly what makes a list
            // unscannable, and a missing icon is a content gap, not a layout change.
            var go = new GameObject("Icon", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
            go.transform.SetParent(parent, false);

            LayoutElement element = go.GetComponent<LayoutElement>();
            element.preferredWidth = IconSize;
            element.minWidth = IconSize;
            element.preferredHeight = IconSize;
            // A sprited Image reports its own native size (32x32 here) as its preferred
            // size; without these explicit zeros the row's HorizontalLayoutGroup lets it
            // grow past IconSize and drags the whole row's height with it.
            element.flexibleWidth = 0f;
            element.flexibleHeight = 0f;

            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;

            Sprite icon = ResolveIcon(itemType);
            if (icon == null)
            {
                image.color = Color.clear;
                return;
            }

            image.sprite = icon;
            image.color = Color.white;
        }

        private void CreateMagnitudeBar(Transform parent, int quantity, int largestQuantity)
        {
            var track = new GameObject("Bar", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
            track.transform.SetParent(parent, false);

            LayoutElement element = track.GetComponent<LayoutElement>();
            element.preferredWidth = BarWidth;
            element.minWidth = BarWidth;
            element.preferredHeight = 8f;
            element.flexibleWidth = 0f;
            element.flexibleHeight = 0f;

            Image trackImage = track.GetComponent<Image>();
            trackImage.color = BarTrackColor;
            trackImage.raycastTarget = false;

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(track.transform, false);

            // Anchored fraction rather than a LayoutElement: this child is inside a
            // container with no LayoutGroup of its own, so it stretches with the track and
            // needs no layout participation at all.
            float fraction = largestQuantity > 0 ? Mathf.Clamp01((float)quantity / largestQuantity) : 0f;
            var fillRect = (RectTransform)fill.transform;
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(fraction, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            Image fillImage = fill.GetComponent<Image>();
            fillImage.color = BarFillColor;
            fillImage.raycastTarget = false;
        }

        private static void CreateLabel(
            Transform parent, string name, string text, Color color, float fontSize, FontStyles style,
            TextAlignmentOptions alignment, float flexibleWidth, float fixedWidth)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            LayoutElement element = go.GetComponent<LayoutElement>();
            element.flexibleWidth = flexibleWidth;
            element.flexibleHeight = 0f;
            if (fixedWidth > 0f)
            {
                element.preferredWidth = fixedWidth;
                element.minWidth = fixedWidth;
            }

            TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.alignment = alignment;
            label.color = color;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
        }

        private Sprite ResolveIcon(string itemType)
        {
            if (itemIcons == null || itemType == null)
            {
                return null;
            }

            foreach (ItemIconBinding binding in itemIcons)
            {
                if (binding != null && binding.itemType == itemType)
                {
                    return binding.icon;
                }
            }

            return null;
        }

        private static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Destroy(parent.GetChild(i).gameObject);
            }
        }
    }
}
