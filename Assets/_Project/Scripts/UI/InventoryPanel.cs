using UnityEngine;
using GolemFactory.Economy;

namespace GolemFactory.UI
{
    // Minimal M5 inventory readout: lists every StorageBuffer's contents by item type.
    // OnGUI-based like GolemProgrammingPanel -- no Canvas/UGUI scene wiring required.
    // Full visual treatment (per-resource icons, etc.) is a later UI pass, not M5's job.
    public sealed class InventoryPanel : MonoBehaviour
    {
        [SerializeField] private StorageBufferRegistryHolder bufferRegistryHolder;

        private Vector2 _scroll;

        private void OnGUI()
        {
            if (bufferRegistryHolder == null)
            {
                return;
            }

            var boldLabel = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };

            GUILayout.BeginArea(new Rect(Screen.width - 260, 10, 250, Screen.height - 20), GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Label("Inventory", boldLabel);

            foreach (StorageBuffer buffer in bufferRegistryHolder.Registry.Buffers.Values)
            {
                GUILayout.Space(6);
                GUILayout.Label(buffer.BufferId, boldLabel);
                foreach (var entry in buffer.Quantities)
                {
                    GUILayout.Label($"  {entry.Key}: {entry.Value}");
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
