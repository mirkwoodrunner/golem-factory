using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GolemFactory.AssemblyLine;
using GolemFactory.Buildings;
using GolemFactory.Economy;

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
