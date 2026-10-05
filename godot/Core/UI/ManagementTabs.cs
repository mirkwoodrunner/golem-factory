using System.Collections.Generic;
using System.Linq;
using GolemFactory.Blueprints;

namespace GolemFactory.UI
{
    public enum ManagementTab
    {
        Inventory,
        AssemblyLine,
        Patents,
        SaveLoad,
        TechTree,
    }

    /// <summary>
    /// The Management screen's open state and tab: Unity's ManagementPanel minus its UGUI (G8).
    /// Exactly one tab's content shows and exactly one tab button is lit -- the only thing on the
    /// screen telling the player which tab they are on. The tab survives a close, so Tab twice
    /// returns the player where they were.
    /// </summary>
    public sealed class ManagementTabs
    {
        public bool IsOpen { get; private set; }

        public ManagementTab ActiveTab { get; private set; } = ManagementTab.Inventory;

        public void Open() => IsOpen = true;

        public void Close() => IsOpen = false;

        public void Toggle()
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        public void SelectTab(ManagementTab tab) => ActiveTab = tab;

        /// <summary>Whether a tab's content is drawn: only the active one.</summary>
        public bool IsContentShown(ManagementTab tab) => tab == ActiveTab;

        /// <summary>Whether a tab button wears the lit plate: only the active one.</summary>
        public bool IsHighlighted(ManagementTab tab) => tab == ActiveTab;

        /// <summary>
        /// The tab bar's labels, as Unity's prefab spelled them -- "AssemblyLine" and "SaveLoad"
        /// included, and the tech tree as "Ledger".
        /// </summary>
        public static string Label(ManagementTab tab) => tab == ManagementTab.TechTree ? "Ledger" : tab.ToString();
    }

    /// <summary>The Patents tab: the named-program library's rows, and loading one.</summary>
    public static class PatentBrowser
    {
        public const string NoRegistryMessage = "Patent registry unavailable: no registry wired.";
        public const string NoPatentsMessage = "No patents filed yet. Configure a golem, then press Patent on the Workbench.";

        /// <summary>The blueprints to list, in id order (BP-001, BP-002, ...).</summary>
        public static List<Blueprint> Rows(PatentRegistry registry) =>
            registry == null ? new List<Blueprint>() : registry.Blueprints.Values.OrderBy(b => b.BlueprintId).ToList();

        /// <summary>The line drawn when there is nothing to list, or null when there is.</summary>
        public static string EmptyMessage(PatentRegistry registry) =>
            registry == null ? NoRegistryMessage : registry.Blueprints.Count == 0 ? NoPatentsMessage : null;

        /// <summary>
        /// The Load button: open the Workbench, THEN load the blueprint into its draft.
        ///
        /// <para>
        /// Unity did these the other way round, and its Open re-reads the draft from the
        /// targeted golem -- so with a golem targeted, the blueprint the player clicked Load on
        /// was replaced by that golem's program before the screen drew. Its test only asserted
        /// that the Workbench opened. Found in the port; LoadingABlueprint_SurvivesOpeningTheWorkbench
        /// pins the order.
        /// </para>
        /// </summary>
        public static void Load(Blueprint blueprint, IWorkbenchScreen screen, WorkbenchSession session)
        {
            if (blueprint == null || session == null)
            {
                return;
            }
            screen?.Open();
            session.LoadBlueprintIntoDraft(blueprint);
        }
    }
}
