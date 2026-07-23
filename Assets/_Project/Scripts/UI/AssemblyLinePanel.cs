using UnityEngine;
using GolemFactory.AssemblyLine;
using GolemFactory.Buildings;
using GolemFactory.Economy;

namespace GolemFactory.UI
{
    // Simple browse-and-claim panel for the Assembly Line, OnGUI-based like
    // AlertsPanel/InventoryPanel -- no Canvas/UGUI wiring required. Lists each slot's
    // current card and its live-decaying cost, with a Claim button that withdraws Scrap
    // from the named wallet buffer.
    public sealed class AssemblyLinePanel : MonoBehaviour
    {
        [SerializeField] private AssemblyLineStateHolder lineHolder;
        [SerializeField] private StorageBufferRegistryHolder bufferRegistryHolder;
        [SerializeField] private string walletBufferId = "ScrapBuffer";

        private string _statusMessage = "";

        private void OnGUI()
        {
            if (lineHolder == null || lineHolder.State == null)
            {
                return;
            }

            var boldLabel = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };

            GUILayout.BeginArea(new Rect(10, 240, 250, 150), GUI.skin.box);
            GUILayout.Label("Assembly Line", boldLabel);

            AssemblyLineState line = lineHolder.State;
            for (int i = 0; i < line.SlotCount; i++)
            {
                DraftableCardDefinition card = line.GetCard(i);
                GUILayout.BeginHorizontal();
                if (card == null)
                {
                    GUILayout.Label("(empty)");
                }
                else
                {
                    GUILayout.Label($"{card.DisplayName} ({line.GetCurrentCost(i)} Scrap)");
                    if (GUILayout.Button("Claim", GUILayout.Width(60)))
                    {
                        _statusMessage = line.TryClaimSlot(i, PlaceableBuilding.LocalPlayerOwnerId, bufferRegistryHolder.Registry, walletBufferId)
                            ? $"Claimed {card.DisplayName}."
                            : "Not enough Scrap to claim that card.";
                    }
                }
                GUILayout.EndHorizontal();
            }

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                GUILayout.Space(4);
                GUILayout.Label(_statusMessage);
            }

            GUILayout.EndArea();
        }
    }
}
