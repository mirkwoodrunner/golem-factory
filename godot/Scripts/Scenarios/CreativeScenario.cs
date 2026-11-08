using System.IO;
using System.Linq;
using Godot;
using GolemFactory.Economy;
using GolemFactory.Menus;
using GolemFactory.Save;
using GolemFactory.UI;
using GolemFactory.World;
using CoreVector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// Creative Mode (docs/menus-design.md §9), with real keys and clicks, across the reloads
    /// the title makes:
    /// <list type="number">
    ///   <item>The title offers Creative; the keys reach it and Enter starts a Creative world:
    ///   no guide, the HUD says what world it is, the pack is full of every good, and every card
    ///   and chassis is on the Workbench.</item>
    ///   <item>A depot placed with the build keys and a click costs nothing.</item>
    ///   <item>The Save / Load tab is Creative's own: a save lands in Creative's slots, never the
    ///   campaign's. Quit to title autosaves there too.</item>
    ///   <item>The title's Continue names the Creative world; Load opens on Creative's slots,
    ///   Left shows the campaign's (none), Right comes back, and Enter loads the world.</item>
    ///   <item>A campaign game greys a Creative save found among its slots: never loaded as the
    ///   other kind.</item>
    /// </list>
    /// Writes only <c>user://scenario-saves</c> and <c>user://scenario-settings.cfg</c>.
    /// </summary>
    public sealed class CreativeScenario : StepScenario
    {
        private WorldNode World => WorldNode.Find(Runner);
        private SandboxWorld Sandbox => World.Sandbox;
        private TitleScreen Title => TitleScreen.Find(Runner);
        private ManagementScreen Management => Tree.Root.FindChild("Management", true, false) as ManagementScreen;
        private HudOverlay Hud => Tree.Root.FindChild("Hud", true, false) as HudOverlay;
        private TutorialPanel Guide => Tree.Root.FindChild("Tutorial", true, false) as TutorialPanel;

        private CoreVector2Int _depot;
        private int _scrap;

        protected override void Plan()
        {
            GameSettingsStore.ResetForScenario(Tree.Root);
            Do("no saves", () => { ScenarioSaves.Clear(); return null; });
            Reload("to the title", LaunchKind.Title);
            Until("staged", 5, () => Title?.Showcase != null ? null : "no showcase yet");
            PlanStart();
            PlanBuild();
            PlanSave();
            PlanTitle();
            PlanCampaignRefuses();
        }

        protected override void Cleanup() => ScenarioSaves.Clear();

        // --- 1. Starting a Creative world --------------------------------------------------------

        private void PlanStart()
        {
            Do("the title offers Creative", () =>
            {
                Button creative = Title.Entries[TitleChoice.Creative];
                string detail = creative.GetNodeOrNull<Label>("Detail")?.Text ?? "";
                if (creative.Disabled || detail != TitleMenu.CreativeDetail || Title.Menu.Selected != TitleChoice.NewGame)
                {
                    return $"Creative disabled {creative.Disabled}, '{detail}', keyboard on {Title.Menu.Selected}";
                }
                Tap(Key.Down);
                return null;
            });
            Reloading("Enter on Creative", 1, () =>
            {
                if (Title.Menu.Selected != TitleChoice.Creative)
                {
                    throw new System.InvalidOperationException("Down went to " + Title.Menu.Selected);
                }
                Tap(Key.Enter);
            });
            Wait(0.5);
            Do("a Creative world", () =>
            {
                if (World.Launch != LaunchKind.NewGame || !Sandbox.Creative || !GameLaunch.Creative)
                {
                    return $"launch {World.Launch}, creative {Sandbox.Creative}";
                }
                if (Sandbox.Tutorial != null || Guide.Plate.IsVisibleInTree())
                {
                    return "the guide is here";
                }
                if (Hud.TowerHeadline != HudOverlay.CreativeHeadline)
                {
                    return "the HUD's panel reads " + Hud.TowerHeadline;
                }
                int cap = Sandbox.Wallet.PackCapacity;
                string short1 = ItemTiers.CanonicalOrder.FirstOrDefault(i => Sandbox.Wallet.Pack.GetQuantity(i) != cap);
                if (short1 != null)
                {
                    return $"the pack holds {Sandbox.Wallet.Pack.GetQuantity(short1)} {short1}, not {cap}";
                }
                if (!Sandbox.Definitions.Chassis.Values.All(Sandbox.Workbench.IsChassisAvailable) || Sandbox.Workbench.VaultAppendages.Count() < Sandbox.Definitions.Appendages.Count / 2)
                {
                    return "the Workbench is missing cards or chassis";
                }
                if (Sandbox.SaveFolder != SavePaths.CreativeFolder)
                {
                    return "it saves to " + Sandbox.SaveFolder;
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "creative");
                return null;
            });
        }

        // --- 2. Free building ------------------------------------------------------------------

        private void PlanBuild()
        {
            Do("1 for a depot", () =>
            {
                _scrap = Sandbox.Wallet.Quantity(ItemType.Scrap);
                _depot = Sandbox.Interactor.PlayerCell + new CoreVector2Int(2, 1);
                Tap(Key.Key1);
                return null;
            });
            Do("click a cell", () =>
            {
                if (!Sandbox.Build.IsBuildToolActive)
                {
                    return "1 didn't pick up the depot";
                }
                Click(Runner.GetViewport().GetCanvasTransform() * GridConversions.CellToWorld(_depot));
                return null;
            });
            Do("it stands, and cost nothing", () =>
            {
                if (!Sandbox.Build.Buildings.Any(b => b.Cell == _depot && !b.IsRemoved))
                {
                    return "no depot: " + Sandbox.Build.LastStatusMessage;
                }
                int now = Sandbox.Wallet.Quantity(ItemType.Scrap);
                if (now != _scrap)
                {
                    return $"Scrap {_scrap} -> {now}";
                }
                Tap(Key.Escape);
                Log("Creative: no guide, the pack full, a depot placed for nothing");
                return null;
            });
        }

        // --- 3. Creative's own slots -----------------------------------------------------------

        private void PlanSave()
        {
            Do("Tab", () => { Tap(Key.Tab); return null; });
            Do("click Save / Load", () => { Click(Management.TabButtons[ManagementTab.SaveLoad]); return null; });
            Do("click Save on Slot 1", () =>
            {
                string heading = Management.TabContent[ManagementTab.SaveLoad].FindChild("SideHeading", true, false) is Label l ? l.Text : "";
                if (heading != "Saving a Creative world")
                {
                    return "the tab says " + heading;
                }
                Click(Management.Slots.Button("slot-1", SlotAction.Save));
                return null;
            });
            Do("it is in Creative's slots", () =>
            {
                string creative = SaveSlots.PathIn(SavePaths.CreativeFolder, "slot-1");
                if (!File.Exists(creative) || SaveFileIO.ReadFromFile(creative)?.creative != true)
                {
                    return "no Creative save in Creative's Slot 1";
                }
                return File.Exists(SaveSlots.PathIn(SavePaths.Folder, "slot-1")) ? "it was written to the campaign's slots too" : null;
            });
            Reloading("click Quit to title", 1, () => Click(Management.QuitToTitleButton));
            Until("staged", 5, () => Title?.Showcase != null ? null : "no showcase yet");
            Do("the autosave went to Creative's slots", () =>
                File.Exists(SaveSlots.PathIn(SavePaths.CreativeFolder, SaveSlots.AutosaveKey)) && !File.Exists(SaveSlots.PathIn(SavePaths.Folder, SaveSlots.AutosaveKey))
                    ? null : "the autosave isn't (only) Creative's");
        }

        // --- 4. Back from the title ------------------------------------------------------------

        private void PlanTitle()
        {
            Do("Continue names the Creative world", () =>
            {
                string detail = Title.Entries[TitleChoice.Continue].GetNodeOrNull<Label>("Detail")?.Text ?? "";
                if (!detail.StartsWith("Creative, today at"))
                {
                    return "Continue says " + detail;
                }
                Click(Title.Entries[TitleChoice.Load]);
                return null;
            });
            Do("Load opens on Creative's slots", () =>
            {
                if (!Title.LoadOpen || !Title.LoadingCreative)
                {
                    return $"Load open {Title.LoadOpen}, Creative {Title.LoadingCreative}";
                }
                if (!Title.SlotBoard.Can("slot-1", SlotAction.Load) || !Title.SlotBoard.Can(SaveSlots.AutosaveKey, SlotAction.Load))
                {
                    return "Creative's slots aren't offered";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "creative-load");
                Tap(Key.Left);
                return null;
            });
            Do("Left shows the campaign's: none", () =>
            {
                if (Title.LoadingCreative || Title.SlotBoard.Rows.Any(r => !r.Empty))
                {
                    return $"Creative {Title.LoadingCreative}, {Title.SlotBoard.Rows.Count(r => !r.Empty)} campaign saves";
                }
                Tap(Key.Right);
                return null;
            });
            Do("Right comes back", () => Title.LoadingCreative && Title.SlotBoard.Selected != null ? null : "still on the campaign's");
            Reloading("Enter loads it", 1, () => Tap(Key.Enter));
            Until("loaded", 2, () => Title.LoadStatus != null ? null : "nothing loaded yet");
            Do("the Creative world, with its depot", () =>
            {
                if (!Sandbox.Creative || World.Launch != LaunchKind.Continue)
                {
                    return $"creative {Sandbox.Creative}, launch {World.Launch}: {Title.LoadStatus}";
                }
                if (!Sandbox.Build.Buildings.Any(b => b.Cell == _depot && !b.IsRemoved))
                {
                    return "the depot didn't come back: " + Title.LoadStatus;
                }
                Log("its saves are Creative's own; the title's Continue and Load (Left, Right, Enter) brought it back");
                return null;
            });
        }

        // --- 5. Never the other kind -----------------------------------------------------------

        private void PlanCampaignRefuses()
        {
            Reload("a campaign game", LaunchKind.NewGame);
            Do("a Creative save among the campaign's slots", () =>
            {
                if (Sandbox.Creative)
                {
                    return "the new game is Creative";
                }
                Directory.CreateDirectory(SavePaths.Folder);
                File.Copy(SaveSlots.PathIn(SavePaths.CreativeFolder, "slot-1"), SaveSlots.PathIn(SavePaths.Folder, "slot-2"), true);
                Tap(Key.Tab);
                return null;
            });
            Do("click Save / Load", () => { Click(Management.TabButtons[ManagementTab.SaveLoad]); return null; });
            Do("it is greyed, never loaded", () =>
            {
                SlotRow row = Management.SlotBoard.Row("slot-2");
                if (row.Readable || !Management.Slots.Button("slot-2", SlotAction.Load).Disabled || row.Where != SaveSlotBoard.UnreadableText)
                {
                    return $"Slot 2: readable {row.Readable}, '{row.Where}'";
                }
                string refused = Sandbox.LoadFrom(SaveSlots.PathIn(SavePaths.Folder, "slot-2"));
                if (refused != SandboxWorld.WrongKind(saveIsCreative: true))
                {
                    return "a direct load said " + refused;
                }
                Log("a campaign game greys a Creative save and refuses to load it");
                Tap(Key.Escape);
                return null;
            });
        }
    }
}
