using UnityEngine;
using UnityEngine.UI;
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
        [SerializeField] private Text statusText;

        private string _statusMessage = "";

        public void Configure(AssemblyLineStateHolder line, StorageBufferRegistryHolder buffers, string walletBuffer)
        {
            lineHolder = line;
            bufferRegistryHolder = buffers;
            walletBufferId = walletBuffer;
        }

        public void ConfigureUI(RectTransform contentRoot, Text status)
        {
            content = contentRoot;
            statusText = status;
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
            row.GetComponent<LayoutElement>().preferredHeight = 28f;

            if (card == null)
            {
                CreateLabel(row.transform, "(empty)");
                return;
            }

            CreateLabel(row.transform, $"{card.DisplayName} ({line.GetCurrentCost(slotIndex)} Scrap)");

            var buttonGo = new GameObject("Claim", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonGo.transform.SetParent(row.transform, false);
            buttonGo.GetComponent<LayoutElement>().preferredWidth = 60f;
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
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            Text label = go.GetComponent<Text>();
            label.text = text;
            label.alignment = TextAnchor.MiddleLeft;
            label.color = Color.black;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
