using UnityEngine;
using GolemFactory.Buildings;
using GolemFactory.Player;

namespace GolemFactory.UI
{
    // Tiny OnGUI panel (same style as GolemProgrammingPanel/GolemConstructionPanel) listing
    // BuildModeController's available placeable prefabs with cost; picking one just calls
    // SetActivePrefab, so BuildModeController.PlaceOrRemove's existing click-to-place flow
    // (and its cost-gating) is unchanged by this panel's existence.
    public sealed class BuildMenuPanel : MonoBehaviour
    {
        [SerializeField] private BuildModeController _buildModeController;

        public void Configure(BuildModeController buildModeController) => _buildModeController = buildModeController;

        private void OnGUI()
        {
            if (_buildModeController == null || _buildModeController.AvailablePrefabs == null)
            {
                return;
            }

            var boldLabel = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };

            GUILayout.BeginArea(new Rect(10, Screen.height - 150, 260, 140), GUI.skin.box);
            GUILayout.Label("Build", boldLabel);

            foreach (PlaceableBuilding prefab in _buildModeController.AvailablePrefabs)
            {
                if (prefab == null)
                {
                    continue;
                }

                bool isActive = _buildModeController.ActivePrefab == prefab;
                string label = $"{prefab.name} (Scrap {prefab.ScrapCost}, Brass {prefab.BrassCost})";
                if (GUILayout.Toggle(isActive, label, GUI.skin.button) && !isActive)
                {
                    _buildModeController.SetActivePrefab(prefab);
                }
            }

            GUILayout.EndArea();
        }
    }
}
