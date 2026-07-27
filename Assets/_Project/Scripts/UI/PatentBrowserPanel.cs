using UnityEngine;
using UnityEngine.UI;
using TMPro;
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

        // Load button skin, applied per-row since rows are rebuilt from scratch on every
        // Refresh() -- left unset, the button falls back to its default flat Image color.
        [SerializeField] private Sprite loadButtonSprite;

        public void Configure(PatentRegistryHolder patents, WorkbenchController workbench, ManagementPanel management)
        {
            patentRegistryHolder = patents;
            workbenchController = workbench;
            managementPanel = management;
        }

        public void ConfigureUI(RectTransform contentRoot) => content = contentRoot;

        public void ConfigureSprites(Sprite loadButton) => loadButtonSprite = loadButton;

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
            LayoutElement rowElement = row.GetComponent<LayoutElement>();
            rowElement.preferredHeight = 28f;
            // flexibleHeight must be explicit 0 -- same fix as AssemblyLinePanel's row,
            // otherwise the parent VerticalLayoutGroup still stretches this row to fill
            // leftover space even with childForceExpandHeight false.
            rowElement.flexibleHeight = 0f;
            // childControlWidth/Height default to false on a freshly-added LayoutGroup, so
            // without this the Load button below keeps its own native (100x100) rect
            // instead of respecting its LayoutElement -- same fix as AssemblyLinePanel.
            HorizontalLayoutGroup rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            // Without this, childForceExpandWidth's true default stretches the Load button
            // (60f preferredWidth) to eat all leftover row width instead of sitting at its
            // intended compact size.
            rowLayout.childForceExpandWidth = false;

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(LayoutElement), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(row.transform, false);
            // flexibleWidth so the row's HorizontalLayoutGroup gives this label the
            // leftover space instead of the Load button's own fixed 60f overlapping it.
            labelGo.GetComponent<LayoutElement>().flexibleWidth = 1f;
            TextMeshProUGUI label = labelGo.GetComponent<TextMeshProUGUI>();
            label.text = blueprint.BlueprintId;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.color = Color.black;
            label.fontSize = 13;
            label.raycastTarget = false;

            var buttonGo = new GameObject("Load", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonGo.transform.SetParent(row.transform, false);
            LayoutElement buttonLayout = buttonGo.GetComponent<LayoutElement>();
            buttonLayout.preferredWidth = 60f;
            // Explicit preferredHeight: once loadButtonSprite is set, Image reports its own
            // preferred size to the layout system, which otherwise overrides this row's
            // intended 28f height -- same fix as AssemblyLinePanel's Claim button.
            buttonLayout.preferredHeight = 24f;
            if (loadButtonSprite != null)
            {
                Image buttonImage = buttonGo.GetComponent<Image>();
                buttonImage.sprite = loadButtonSprite;
                buttonImage.type = Image.Type.Sliced;
            }
            buttonGo.GetComponent<Button>().onClick.AddListener(() => LoadBlueprint(blueprint));

            var buttonLabelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            buttonLabelGo.transform.SetParent(buttonGo.transform, false);
            TextMeshProUGUI buttonLabel = buttonLabelGo.GetComponent<TextMeshProUGUI>();
            buttonLabel.text = "Load";
            buttonLabel.alignment = TextAlignmentOptions.Center;
            buttonLabel.color = Color.black;
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
