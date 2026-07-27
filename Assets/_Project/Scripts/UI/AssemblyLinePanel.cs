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
                return;
            }

            AssemblyLineState line = lineHolder.State;
            for (int i = 0; i < line.SlotCount; i++)
            {
                DraftableCardDefinition card = line.GetCard(i);
                CreateSlotRow(i, card, line);
            }

            if (statusText != null)
            {
                statusText.text = _statusMessage;
            }
        }

        private void CreateSlotRow(int slotIndex, DraftableCardDefinition card, AssemblyLineState line)
        {
            var row = new GameObject("Slot", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(content, false);
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

            if (card == null)
            {
                CreateLabel(row.transform, "(empty)");
                return;
            }

            CreateLabel(row.transform, $"{card.DisplayName} ({line.GetCurrentCost(slotIndex)} Scrap)");

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
            buttonGo.GetComponent<Button>().onClick.AddListener(() => ClaimSlot(slotIndex, card));
            CreateLabel(buttonGo.transform, "Claim");
        }

        private void ClaimSlot(int slotIndex, DraftableCardDefinition card)
        {
            AssemblyLineState line = lineHolder.State;
            _statusMessage = line.TryClaimSlot(slotIndex, PlaceableBuilding.LocalPlayerOwnerId, bufferRegistryHolder.Registry, walletBufferId)
                ? $"Claimed {card.DisplayName}."
                : "Not enough Scrap to claim that card.";
            Refresh();
        }

        private static void CreateLabel(Transform parent, string text)
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
            label.color = Color.black;
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
