using UnityEngine;
using GolemFactory.Economy;

namespace GolemFactory.UI
{
    // Minimal M5 inventory readout: lists every StorageBuffer's contents by item type.
    // OnGUI-based like GolemProgrammingPanel -- no Canvas/UGUI scene wiring required.
    // Full visual treatment (per-resource icons, etc.) is a later UI pass, not M5's job.
    // M8 note: relocated from the top-right to the top-left corner. OnGUI (IMGUI) always
    // renders on top of Canvas-based UGUI regardless of sort order, so the original
    // top-right position collided with M8's new Card Vault panel, which also anchors to
    // the right side. The top-left corner was freed up by M3's GolemProgrammingPanel
    // being disabled (superseded by the Workbench) this same milestone.
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

            // Capped height (not full screen) so it doesn't blanket the Workbench's
            // Blueprint Viewport slots below it -- see the class comment. A scroll view
            // still handles overflow if the buffer list grows past this box.
            GUILayout.BeginArea(new Rect(10, 10, 250, 220), GUI.skin.box);
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
