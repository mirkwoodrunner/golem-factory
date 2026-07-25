using UnityEngine;
using GolemFactory.Blueprints;

namespace GolemFactory.UI
{
    // Simple browse UI for patented blueprints -- M8 built the headless PatentRegistry
    // and a way to create blueprints (the Workbench's Patent button); this is the other
    // half M9 adds: a way to see what's been patented and load one back into the
    // Workbench's draft for reuse/editing. OnGUI-based like the rest of the HUD panels.
    public sealed class PatentBrowserPanel : MonoBehaviour
    {
        [SerializeField] private PatentRegistryHolder patentRegistryHolder;
        [SerializeField] private WorkbenchController workbenchController;

        private Vector2 _scroll;

        private void OnGUI()
        {
            if (patentRegistryHolder == null || workbenchController == null)
            {
                return;
            }

            var boldLabel = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };

            GUILayout.BeginArea(new Rect(10, 400, 250, 150), GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll);
            GUILayout.Label("Patented Blueprints", boldLabel);

            foreach (Blueprint blueprint in patentRegistryHolder.Registry.Blueprints.Values)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(blueprint.BlueprintId);
                if (GUILayout.Button("Load", GUILayout.Width(60)))
                {
                    workbenchController.LoadBlueprintIntoDraft(blueprint);
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
