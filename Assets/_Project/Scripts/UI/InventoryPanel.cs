using UnityEngine;
using UnityEngine.UI;
using GolemFactory.Economy;

namespace GolemFactory.UI
{
    // Minimal inventory readout: lists every StorageBuffer's contents by item type.
    // UGUI-based (converted from the original OnGUI panel as part of the Management HUD
    // consolidation) -- content lives under a ScrollRect's Content RectTransform, owned
    // by ManagementPanel's InventoryTab. Rebuilds every row from scratch on Refresh(),
    // same "clear children, rebuild from data" idiom as WorkbenchController.RebuildUI().
    public sealed class InventoryPanel : MonoBehaviour
    {
        [SerializeField] private StorageBufferRegistryHolder bufferRegistryHolder;
        [SerializeField] private RectTransform content;

        public void Configure(StorageBufferRegistryHolder holder) => bufferRegistryHolder = holder;

        public void ConfigureUI(RectTransform contentRoot) => content = contentRoot;

        public void Refresh()
        {
            if (content == null)
            {
                return;
            }

            ClearChildren(content);

            if (bufferRegistryHolder == null)
            {
                return;
            }

            foreach (StorageBuffer buffer in bufferRegistryHolder.Registry.Buffers.Values)
            {
                CreateRow(buffer.BufferId, bold: true);
                foreach (var entry in buffer.Quantities)
                {
                    CreateRow($"  {entry.Key}: {entry.Value}", bold: false);
                }
            }
        }

        private void CreateRow(string text, bool bold)
        {
            var go = new GameObject("Row", typeof(RectTransform), typeof(LayoutElement), typeof(Text));
            go.transform.SetParent(content, false);
            go.GetComponent<LayoutElement>().preferredHeight = 20f;

            Text label = go.GetComponent<Text>();
            label.text = text;
            label.alignment = TextAnchor.MiddleLeft;
            label.color = Color.black;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 13;
            label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
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
