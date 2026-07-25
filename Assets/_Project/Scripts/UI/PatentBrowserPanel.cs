using UnityEngine;
using UnityEngine.UI;
using GolemFactory.Blueprints;

namespace GolemFactory.UI
{
    // Browse UI for patented blueprints -- lets an Artificer load a previously-patented
    // configuration back into the Workbench's draft for reuse/editing. UGUI-based
    // (converted from the original OnGUI panel as part of the Management HUD
    // consolidation); content lives under a ScrollRect's Content RectTransform, owned by
    // ManagementPanel's PatentsTab.
    public sealed class PatentBrowserPanel : MonoBehaviour
    {
        [SerializeField] private PatentRegistryHolder patentRegistryHolder;
        [SerializeField] private WorkbenchController workbenchController;
        [SerializeField] private ManagementPanel managementPanel;
        [SerializeField] private RectTransform content;

        public void Configure(PatentRegistryHolder patents, WorkbenchController workbench, ManagementPanel management)
        {
            patentRegistryHolder = patents;
            workbenchController = workbench;
            managementPanel = management;
        }

        public void ConfigureUI(RectTransform contentRoot) => content = contentRoot;

        public void Refresh()
        {
            if (content == null)
            {
                return;
            }

            ClearChildren(content);

            if (patentRegistryHolder == null)
            {
                return;
            }

            foreach (Blueprint blueprint in patentRegistryHolder.Registry.Blueprints.Values)
            {
                CreateRow(blueprint);
            }
        }

        private void CreateRow(Blueprint blueprint)
        {
            var row = new GameObject("Blueprint", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(content, false);
            row.GetComponent<LayoutElement>().preferredHeight = 28f;

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(row.transform, false);
            Text label = labelGo.GetComponent<Text>();
            label.text = blueprint.BlueprintId;
            label.alignment = TextAnchor.MiddleLeft;
            label.color = Color.black;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 13;
            label.raycastTarget = false;

            var buttonGo = new GameObject("Load", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonGo.transform.SetParent(row.transform, false);
            buttonGo.GetComponent<LayoutElement>().preferredWidth = 60f;
            buttonGo.GetComponent<Button>().onClick.AddListener(() => LoadBlueprint(blueprint));

            var buttonLabelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            buttonLabelGo.transform.SetParent(buttonGo.transform, false);
            Text buttonLabel = buttonLabelGo.GetComponent<Text>();
            buttonLabel.text = "Load";
            buttonLabel.alignment = TextAnchor.MiddleCenter;
            buttonLabel.color = Color.black;
            buttonLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            buttonLabel.fontSize = 13;
            buttonLabel.raycastTarget = false;
        }

        private void LoadBlueprint(Blueprint blueprint)
        {
            if (workbenchController == null)
            {
                return;
            }

            workbenchController.LoadBlueprintIntoDraft(blueprint);
            workbenchController.Open();
            managementPanel?.Close();
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
