using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GolemFactory.AssemblyLine;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.PunchCards;

namespace GolemFactory.UI
{
    // Browse-and-claim panel for the Assembly Line. UGUI-based (converted from the
    // original OnGUI panel as part of the Management HUD consolidation) -- lists each
    // slot's current card and its live-decaying cost, with a Claim button that withdraws
    // Scrap from the named wallet buffer. No ScrollRect: slot count is small and fixed,
    // matching the original panel's non-scrolling behavior.
    public sealed class AssemblyLinePanel : MonoBehaviour
    {
        [SerializeField] private AssemblyLineStateHolder lineHolder;
        [SerializeField] private StorageBufferRegistryHolder bufferRegistryHolder;
        [SerializeField] private string walletBufferId = "ScrapBuffer";
        [SerializeField] private RectTransform content;
        [SerializeField] private TextMeshProUGUI statusText;

        // Claim button skin, applied per-row since rows are rebuilt from scratch on every
        // Refresh() -- left unset, the button falls back to its default flat Image color.
        [SerializeField] private Sprite claimButtonSprite;

        // --- The Assembly Bay cap (progression-design 8) -----------------------------------
        // Wired at RUNTIME by SandboxBootstrap, never authored: the bay lives on
        // ManagerHolders.prefab and this panel on WorkbenchCanvas.prefab, and a prefab cannot
        // hold a reference into another prefab. Left unwired the row simply does not appear,
        // which is what every scene without a bay wants.
        //
        // Its own buffer id, NOT walletBufferId: the Assembly Line claims cards from a wallet
        // buffer, while a bay upgrade is paid out of the player's stockpile -- the same buffer
        // the construction station and the build menu spend from.
        [SerializeField] private AssemblyBayStructure assemblyBay;

        // §11 item 15's Floor Expansion. It shares this tab with the bay row because they are
        // the same kind of decision -- spend goods to raise a cap the factory is pressing
        // against -- and a player looking for "why can I not build more" finds both in one place.
        [SerializeField] private GolemFactory.World.FloorExpansionService floorExpansion;
        [SerializeField] private string stockpileBufferId = "FactoryStockpile";

        // Warm parchment, not Color.black: these rows sit on ManagementScreen's near-black
        // iron panel (0.08 grey), where the original black text was effectively invisible.
        // Button labels stay black, since those sit on a light brass button sprite.
        private static readonly Color RowTextColor = new Color(0.88f, 0.84f, 0.76f, 1f);
        private static readonly Color DimTextColor = new Color(0.55f, 0.52f, 0.47f, 1f);
        private static readonly Color AffordableCostColor = new Color(1f, 0.76f, 0.30f, 1f);
        private static readonly Color RowTint = new Color(1f, 1f, 1f, 0.035f);

        private string _statusMessage = "";

        public void Configure(AssemblyLineStateHolder line, StorageBufferRegistryHolder buffers, string walletBuffer)
        {
            lineHolder = line;
            bufferRegistryHolder = buffers;
            walletBufferId = walletBuffer;
        }

        public void ConfigureUI(RectTransform contentRoot, TextMeshProUGUI status)
        {
            content = contentRoot;
            statusText = status;
        }

        public void ConfigureSprites(Sprite claimButton) => claimButtonSprite = claimButton;

        /// <summary>
        /// Wires the bay row. Separate from <see cref="Configure"/> for the same reason every
        /// other Configure* split in this project is: a scene with no bay never calls it and
        /// this panel behaves exactly as it did.
        /// </summary>
        public void ConfigureBays(
            AssemblyBayStructure bay, StorageBufferRegistryHolder stockpile, string stockpileBuffer)
        {
            assemblyBay = bay;
            if (stockpile != null)
            {
                bufferRegistryHolder = bufferRegistryHolder != null ? bufferRegistryHolder : stockpile;
                _bayStockpileHolder = stockpile;
            }

            if (!string.IsNullOrEmpty(stockpileBuffer))
            {
                stockpileBufferId = stockpileBuffer;
            }
        }

        // The bay spends from the stockpile, which need not be the same registry holder the
        // Assembly Line's wallet lives in. Held separately rather than overwriting
        // bufferRegistryHolder, which would silently re-point every Claim button.
        private StorageBufferRegistryHolder _bayStockpileHolder;

        public void ConfigureFloorExpansion(GolemFactory.World.FloorExpansionService expansion) =>
            floorExpansion = expansion;

        /// <summary>Stock reader for the bay's cost, matching GolemConstructionStation.StockOf.</summary>
        private int BayStockOf(string itemType)
        {
            StorageBufferRegistryHolder holder = _bayStockpileHolder != null
                ? _bayStockpileHolder
                : bufferRegistryHolder;
            StorageBuffer buffer;
            if (holder == null || !holder.Registry.TryGetBuffer(stockpileBufferId, out buffer))
            {
                return 0;
            }

            return buffer.GetQuantity(itemType);
        }

        public void Refresh()
        {
            if (content == null)
            {
                return;
            }

            ClearChildren(content);

            if (lineHolder == null || lineHolder.State == null)
            {
                CreateLabel(CreateSlotRoot().transform, "Assembly line unavailable: no line state wired.", DimTextColor);
                return;
            }

            // The wallet balance drives every row's affordability colour, so read it once
            // rather than per row.
            int wallet = 0;
            StorageBuffer walletBuffer;
            if (bufferRegistryHolder != null
                && bufferRegistryHolder.Registry.TryGetBuffer(walletBufferId, out walletBuffer))
            {
                wallet = walletBuffer.GetQuantity(ItemType.Scrap);
            }

            CreateLabel(CreateSlotRoot().transform, "Wallet: " + wallet + " Scrap (" + walletBufferId + ")", AffordableCostColor);

            // FIRST, above the card rows. The cap governs whether a claimed card can ever be
            // put on a golem at all, so a player who cannot build is looking for this line
            // rather than scrolling past the draft to find it.
            CreateBayRow();
            CreateFloorExpansionRow();

            AssemblyLineState line = lineHolder.State;
            for (int i = 0; i < line.SlotCount; i++)
            {
                DraftableCardDefinition card = line.GetCard(i);
                CreateSlotRow(i, card, line, wallet);
            }

            if (statusText != null)
            {
                statusText.text = _statusMessage;
            }
        }

        /// <summary>
        /// The bay occupancy readout and its Upgrade button: "Bays 7/10 · +6 slots ·
        /// 40 Scrap + 20 Iron Plate · [Upgrade]".
        ///
        /// <para>
        /// Rendered only when a bay is wired. Before this row existed, §8's cap was a hard wall
        /// at ten golems: <c>AssemblyBayStructure.TryUpgrade</c> was implemented, tested, and
        /// called by nothing in the game.
        /// </para>
        /// </summary>
        private void CreateBayRow()
        {
            if (assemblyBay == null)
            {
                return;
            }

            GameObject row = CreateSlotRoot();
            IReadOnlyList<RecipeIngredient> cost = assemblyBay.UpgradeCost;
            bool affordable = AssemblyBayRowPolicy.CanAfford(BayStockOf, cost);

            CreateLabel(
                row.transform,
                AssemblyBayRowPolicy.FormatOccupancy(assemblyBay.OccupiedSlots, assemblyBay.MaxGolemSlots)
                + "  ·  " + AssemblyBayRowPolicy.FormatUpgradeEffect(AssemblyBayStructure.SlotsPerUpgrade),
                assemblyBay.HasFreeSlot ? RowTextColor : AffordableCostColor);

            CreateCostLabel(
                row.transform, AssemblyBayRowPolicy.FormatCost(cost),
                affordable ? AffordableCostColor : DimTextColor);

            var buttonGo = new GameObject("Upgrade", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonGo.transform.SetParent(row.transform, false);
            LayoutElement buttonLayout = buttonGo.GetComponent<LayoutElement>();
            buttonLayout.preferredWidth = 74f;
            buttonLayout.preferredHeight = 24f;
            if (claimButtonSprite != null)
            {
                Image buttonImage = buttonGo.GetComponent<Image>();
                buttonImage.sprite = claimButtonSprite;
                buttonImage.type = Image.Type.Sliced;
            }

            Button upgradeButton = buttonGo.GetComponent<Button>();
            // Wrapped, not passed directly: UpgradeBays returns a bool so a test can assert
            // on the outcome, and UnityAction takes none.
            upgradeButton.onClick.AddListener(() => UpgradeBays());
            upgradeButton.interactable = affordable;
            CreateButtonLabel(buttonGo.transform, "Upgrade");
        }

        /// <summary>
        /// Buys one bay upgrade. Public so a test can drive the decision without clicking a
        /// Button, exactly as <c>ClaimSlot</c> and <c>BuildModeController.PlaceOrRemove</c> are.
        /// </summary>
        public bool UpgradeBays()
        {
            if (assemblyBay == null)
            {
                _statusMessage = "No assembly bays in this scene.";
                return false;
            }

            StorageBufferRegistryHolder holder = _bayStockpileHolder != null
                ? _bayStockpileHolder
                : bufferRegistryHolder;
            IReadOnlyList<RecipeIngredient> cost = assemblyBay.UpgradeCost;

            bool upgraded = holder != null
                && assemblyBay.TryUpgrade(holder.Registry, stockpileBufferId);

            _statusMessage = AssemblyBayRowPolicy.DescribeResult(
                upgraded, BayStockOf, cost, AssemblyBayStructure.SlotsPerUpgrade);

            // Re-render from data rather than patching the row in place -- the same idiom the
            // rest of this panel and WorkbenchController.RebuildUI follow.
            Refresh();
            return upgraded;
        }

        /// <summary>
        /// "Workshop 25x27 · +2 rows north · 80 Scrap + 40 Iron Plate · [Extend]".
        ///
        /// <para>
        /// The room's CURRENT size is stated, not just the purchase, because the number the
        /// player is deciding against is how much floor they already have.
        /// </para>
        /// </summary>
        private void CreateFloorExpansionRow()
        {
            if (floorExpansion == null || floorExpansion.Bounds == null)
            {
                return;
            }

            GolemFactory.World.FloorBounds bounds = floorExpansion.Bounds;
            GameObject row = CreateSlotRoot();

            int width = bounds.HalfExtent * 2 + 1;
            int depth = bounds.NorthExtent + bounds.HalfExtent + 1;
            string label = "Workshop " + width + "x" + depth;

            if (!bounds.CanExpand)
            {
                // Land is finite (§11): when it runs out the row says so rather than offering a
                // button that always refuses.
                CreateLabel(row.transform, label + "  ·  fully extended", DimTextColor);
                return;
            }

            IReadOnlyList<RecipeIngredient> cost = floorExpansion.NextCost();
            bool affordable = floorExpansion.CanAfford();

            CreateLabel(
                row.transform,
                label + "  ·  +" + floorExpansion.RowsPerPurchase + " rows north",
                RowTextColor);
            CreateCostLabel(
                row.transform, ConstructionCostPolicy.FormatCost(cost),
                affordable ? AffordableCostColor : DimTextColor);

            var buttonGo = new GameObject("Extend", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonGo.transform.SetParent(row.transform, false);
            LayoutElement buttonLayout = buttonGo.GetComponent<LayoutElement>();
            buttonLayout.preferredWidth = 74f;
            buttonLayout.preferredHeight = 24f;
            if (claimButtonSprite != null)
            {
                Image buttonImage = buttonGo.GetComponent<Image>();
                buttonImage.sprite = claimButtonSprite;
                buttonImage.type = Image.Type.Sliced;
            }

            Button extendButton = buttonGo.GetComponent<Button>();
            extendButton.onClick.AddListener(() => ExtendFloor());
            extendButton.interactable = affordable;
            CreateButtonLabel(buttonGo.transform, "Extend");
        }

        /// <summary>Buys one expansion. Public so a test drives the same path the button does.</summary>
        public bool ExtendFloor()
        {
            if (floorExpansion == null)
            {
                _statusMessage = "No expandable floor in this scene.";
                return false;
            }

            bool expanded = floorExpansion.TryPurchaseExpansion();
            _statusMessage = expanded ? "" : floorExpansion.LastStatusMessage;
            Refresh();
            return expanded;
        }

        private GameObject CreateSlotRoot()
        {
            var row = new GameObject("Slot", typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(content, false);
            Image rowBackground = row.GetComponent<Image>();
            rowBackground.color = RowTint;
            rowBackground.raycastTarget = false;
            LayoutElement e = row.GetComponent<LayoutElement>();
            e.preferredHeight = 28f;
            e.flexibleHeight = 0f;
            HorizontalLayoutGroup l = row.GetComponent<HorizontalLayoutGroup>();
            l.childControlWidth = true;
            l.childControlHeight = true;
            l.childForceExpandWidth = false;
            l.childForceExpandHeight = false;
            l.childAlignment = TextAnchor.MiddleLeft;
            l.spacing = 6f;
            l.padding = new RectOffset(6, 6, 0, 0);
            return row;
        }

        private void CreateSlotRow(int slotIndex, DraftableCardDefinition card, AssemblyLineState line, int wallet)
        {
            var row = new GameObject("Slot", typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(content, false);
            Image rowBackground = row.GetComponent<Image>();
            rowBackground.color = RowTint;
            rowBackground.raycastTarget = false;
            LayoutElement rowElement = row.GetComponent<LayoutElement>();
            rowElement.preferredHeight = 28f;
            // flexibleHeight must be explicit 0, not just left at LayoutElement's default
            // -1 ("unspecified") -- otherwise the parent VerticalLayoutGroup still hands
            // out leftover space to these rows even with childForceExpandHeight false,
            // stretching each row to fill the whole list instead of its own 28f.
            rowElement.flexibleHeight = 0f;
            // childControlWidth/Height default to false on a freshly-added LayoutGroup, so
            // without this the Claim button below keeps its own native (100x100) rect
            // instead of respecting its LayoutElement -- most visible once it has a real
            // sprite drawing its (wrong) bounds instead of an invisible flat color.
            HorizontalLayoutGroup rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            // Without this, childForceExpandWidth's true default stretches the Claim
            // button (60f preferredWidth) to eat all leftover row width instead of sitting
            // at its intended compact size.
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            // Spacing was left at its 0 default, so the right-aligned cost column butted
            // straight against the Claim button with no visible gap.
            rowLayout.spacing = 12f;
            rowLayout.padding = new RectOffset(6, 6, 0, 0);

            if (card == null)
            {
                CreateLabel(row.transform, "Slot " + (slotIndex + 1) + " -- empty", DimTextColor);
                return;
            }

            int cost = line.GetCurrentCost(slotIndex);
            bool affordable = wallet >= cost;

            CreateLabel(row.transform, card.DisplayName, RowTextColor);
            // Cost gets its own fixed-width right-aligned column so the numbers line up
            // vertically -- a cost buried at the end of a variable-length name is not
            // scannable. Affordability is carried by brightness (bright brass vs dim),
            // which is the channel that survives this palette, and the Claim button's own
            // interactable state carries it a second time.
            CreateCostLabel(row.transform, cost + " Scrap", affordable ? AffordableCostColor : DimTextColor);

            var buttonGo = new GameObject("Claim", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonGo.transform.SetParent(row.transform, false);
            LayoutElement buttonLayout = buttonGo.GetComponent<LayoutElement>();
            buttonLayout.preferredWidth = 60f;
            // Explicit preferredHeight: once claimButtonSprite is set, Image reports its
            // own preferred size to the layout system, which otherwise overrides this
            // row's intended 28f height (LayoutElement.preferredHeight left at -1 doesn't
            // win over a sprited Image's computed size the way an unset flat-color Image
            // -- effectively size-less to layout -- did).
            buttonLayout.preferredHeight = 24f;
            if (claimButtonSprite != null)
            {
                Image buttonImage = buttonGo.GetComponent<Image>();
                buttonImage.sprite = claimButtonSprite;
                buttonImage.type = Image.Type.Sliced;
            }
            Button claimButton = buttonGo.GetComponent<Button>();
            claimButton.onClick.AddListener(() => ClaimSlot(slotIndex, card));
            // Non-interactable when unaffordable rather than "clickable but always fails":
            // TryClaimSlot already refuses, so the click was only ever a way to produce an
            // error message the player could have been shown up front.
            claimButton.interactable = affordable;
            CreateButtonLabel(buttonGo.transform, "Claim");
        }

        // A button's caption is NOT a layout child: the button has no LayoutGroup, so the
        // caption created by CreateLabel kept a zero-size rect anchored at the button's
        // centre and its MidlineLeft text spilled left across the cost column. Stretching
        // it to the button and centring it keeps "Claim" inside its own button.
        private static void CreateButtonLabel(Transform parent, string text)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.black;
            label.fontSize = 13;
            label.raycastTarget = false;
        }

        private static void CreateCostLabel(Transform parent, string text, Color color)
        {
            var go = new GameObject("Cost", typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            LayoutElement element = go.GetComponent<LayoutElement>();
            // Wide enough for the longest cost string ("999 Scrap") at this font size --
            // a right-aligned label narrower than its own text overflows leftward and,
            // at 82f, ran straight into the Claim button beside it.
            element.preferredWidth = 112f;
            element.minWidth = 112f;
            element.flexibleWidth = 0f;
            element.flexibleHeight = 0f;

            TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.alignment = TextAlignmentOptions.MidlineRight;
            label.color = color;
            label.fontSize = 13;
            label.fontStyle = FontStyles.Bold;
            label.raycastTarget = false;
        }

        private void ClaimSlot(int slotIndex, DraftableCardDefinition card)
        {
            AssemblyLineState line = lineHolder.State;
            _statusMessage = line.TryClaimSlot(slotIndex, PlaceableBuilding.LocalPlayerOwnerId, bufferRegistryHolder.Registry, walletBufferId)
                ? $"Claimed {card.DisplayName}."
                : "Not enough Scrap to claim that card.";
            Refresh();
        }

        private static void CreateLabel(Transform parent, string text, Color color)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            // flexibleWidth so a row's HorizontalLayoutGroup gives this label the leftover
            // space instead of the Claim button's own fixed 60f overlapping it -- inert
            // (harmless) when this label is nested inside the button itself instead, since
            // nothing there reads LayoutElement.
            go.GetComponent<LayoutElement>().flexibleWidth = 1f;

            TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.color = color;
            label.fontSize = 13;
            label.raycastTarget = false;
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
