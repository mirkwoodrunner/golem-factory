using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.Menus;
using GolemFactory.PunchCards;
using GolemFactory.UI;
using GolemFactory.World;
using Stop = GolemFactory.Nodes.WorkbenchScreen.PadStopKind;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// The screens by pad (docs/steam-deck-design.md D6), with joypad events only once the
    /// playtest card is answered. So far the Workbench (D6.W, the owner's answers):
    /// <list type="number">
    ///   <item>The Workbench opens on a new Scavenger with the ring on the hinted card.</item>
    ///   <item>A sends Always On, Extract and Push Output to the next socket each, the ring
    ///   following the hint to the lever; LB and the d-pad do nothing behind the screen.</item>
    ///   <item>The d-pad walks the ring to STEP 1; RT and LT change its batch size, and the dial
    ///   names them; X takes the card out and A sends it back.</item>
    ///   <item>With every step full, A moves nothing and the status says why.</item>
    ///   <item>A Haul in STEP 2: RB and LB change its good.</item>
    ///   <item>A on the lever engages the program; B closes the screen.</item>
    /// </list>
    /// </summary>
    public sealed class GamepadUiScenario : StepScenario
    {
        private WorldNode World => WorldNode.Find(Runner);
        private SandboxWorld Sandbox => World.Sandbox;
        private WorkbenchSession Session => Sandbox.Workbench;
        private WorkbenchScreen Bench => Tree.Root.FindChild("Workbench", true, false) as WorkbenchScreen;
        private ConstructionPanelNode Panel => Tree.Root.FindChild("ConstructionPanel", true, false) as ConstructionPanelNode;
        private CrateScreen CrateUi => Tree.Root.FindChildren("*", "CanvasLayer", true, false).OfType<CrateScreen>().First();
        private PackScreen PackUi => Tree.Root.FindChildren("*", "CanvasLayer", true, false).OfType<PackScreen>().First();
        private ChassisDefinition Scavenger => Sandbox.Definitions.Chassis["ClockworkScavenger"];
        private CameraRigNode Camera => Tree.Root.FindChildren("*", "Camera2D", true, false).OfType<CameraRigNode>().First();

        private static readonly Compat.Vector3 OpenFloor = new Compat.Vector3(-5f, 8f, 0f);

        private GolemEntity _golem;
        private float _zoom;
        private string _good;
        private bool _dpadDown;
        private JoyButton _dpadHeld;

        protected override void Plan()
        {
            GameSettingsStore.ResetForScenario(Tree.Root);
            InputDevices.Reset();
            Wait(0.5);
            PlanStart();
            PlanOpen();
            PlanSend();
            PlanDial();
            PlanFull();
            PlanHaul();
            PlanEngage();
            PlanVaultScroll();
            PlanCrate();
            PlanPack();
            PlanManagement();
            PlanSupply();
            PlanContract();
            PlanTitle();
        }

        // --- 11. The cart panel -----------------------------------------------------------------

        private static readonly Compat.Vector2Int SpotCell = new Compat.Vector2Int(-20, -16);
        private SupplyScreen Carts => SupplyScreen.Find(Runner);

        private void PlanSupply()
        {
            Do("the Coal cart's price in the pack; stand at the empty spot", () =>
            {
                foreach ((string item, int n) in new[] { (ItemType.Scrap, 100), (ItemType.IronPlate, 40), (ItemType.Gear, 10) })
                {
                    Sandbox.Buffers.Deposit(Sandbox.StockpileBufferId, item, n);
                }
                Sandbox.Interactor.Teleport(new Compat.Vector3(SpotCell.x, SpotCell.y + 1f, 0f));
                return null;
            });
            Wait(0.3);
            PressButton("A at the spot", JoyButton.A);
            Wait(0.1);
            Do("the panel lists the carts, the ring on the first", () =>
            {
                if (!Carts.IsOpen)
                {
                    return "A didn't open the cart panel: " + Sandbox.Interactor.CurrentPrompt;
                }
                return Carts.PadRingVisible && Carts.PadFocusIndex == 0 ? null : $"the ring ({Carts.PadRingVisible}) on row {Carts.PadFocusIndex}";
            });
            Walk("the Coal cart", () => SupplyScreen.StopRow, () => Carts.PadFocusIndex, SupplyScreen.StopRow,
                () => Carts.Rows.ToList().FindIndex(r => r.row.Action == Supply.SupplyAction.Add && r.row.ItemType == ItemType.Coal),
                () => Carts.Button(Supply.SupplyAction.Add, ItemType.Coal), () => Carts.PadRingRect, list: true);
            Do("capture; A", () =>
            {
                ScenarioCapture.Shot(Runner.GetViewport(), "pad-supply");
                Button(JoyButton.A, true);
                return null;
            });
            Do("let go of A: a Coal cart at the spot", () =>
            {
                Button(JoyButton.A, false);
                return Sandbox.Supply.SpotAt(SpotCell).Cart?.ItemType == ItemType.Coal ? null : "no Coal cart: " + Carts.Status;
            });
            PressButton("B", JoyButton.B);
            Do("B closed the panel", () =>
            {
                if (Carts.IsOpen)
                {
                    return "still open";
                }
                Log("the cart panel by pad: A at the spot, the ring to the Coal cart, A added it, B closed");
                return null;
            });
        }

        // --- 12. The City Office's notice -------------------------------------------------------

        private ContractScreen Notice => ContractScreen.Find(Runner);

        private void PlanContract()
        {
            Do("the factory smelts iron, so the first contract posts; stand at the office's door", () =>
            {
                foreach (string good in Sandbox.Contracts.Sequence[0].postWhenSeen)
                {
                    Sandbox.TechTree.Ledger.RecordItem(good);
                }
                Compat.Vector2Int front = TownSquare.OfficeCell + new Compat.Vector2Int(0, -1);
                Sandbox.Interactor.Teleport(new Compat.Vector3(front.x, front.y, 0f));
                return null;
            });
            Until("the first contract posts", 2, () =>
                Sandbox.Contracts.Current == Sandbox.Contracts.Sequence[0] && Sandbox.Contracts.State == Contracts.ContractState.Posted
                    ? null : $"current {Sandbox.Contracts.Current?.name}");
            Do("tap the left stick up, to face it", () => { Axis(JoyAxis.LeftY, -1f); return null; });
            Do("let go", () => { Axis(JoyAxis.LeftY, 0f); return null; });
            Wait(0.3);
            PressButton("A at the office", JoyButton.A);
            Wait(0.1);
            Do("the notice is open, the ring on Take, not taken by the A that opened it", () =>
            {
                if (!Notice.IsOpen)
                {
                    return "A didn't open the notice: " + Sandbox.Interactor.CurrentPrompt;
                }
                if (Sandbox.Contracts.State != Contracts.ContractState.Posted)
                {
                    return "the A that opened the notice also took the contract";
                }
                Button take = Notice.Button(Contracts.ContractAction.Accept);
                if (take == null || !Notice.PadRingVisible || !Notice.PadRingRect.Encloses(take.GetGlobalRect()))
                {
                    return $"the ring ({Notice.PadRingVisible}) on row {Notice.PadFocusIndex}, Take {(take == null ? "missing" : "elsewhere")}";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "pad-contract");
                return null;
            });
            PressButton("A on Take", JoyButton.A);
            Do("taken", () => Sandbox.Contracts.State == Contracts.ContractState.Accepted ? null : $"{Sandbox.Contracts.State}: {Notice.Status}");
            PressButton("Y", JoyButton.Y);
            Do("Y closed the notice", () =>
            {
                if (Notice.IsOpen)
                {
                    return "still open";
                }
                Log("the City Office by pad: A at the door, the ring on Take, A took it, Y closed");
                return null;
            });
        }

        // --- 13. The title ----------------------------------------------------------------------

        private TitleScreen Title => TitleScreen.Find(Runner);
        private Menus.TitleChoice _choice;
        private double _titleVolume;

        private void WalkTitle(Menus.TitleChoice choice)
        {
            Until("the d-pad walks the menu to " + choice, 6, () =>
            {
                if (_dpadDown)
                {
                    Button(_dpadHeld, false);
                    _dpadDown = false;
                    return "letting go";
                }
                if (Title.Menu.Selected == choice)
                {
                    return null;
                }
                int from = Menus.TitleMenu.Order.ToList().IndexOf(Title.Menu.Selected), to = Menus.TitleMenu.Order.ToList().IndexOf(choice);
                _dpadHeld = to > from ? JoyButton.DpadDown : JoyButton.DpadUp;
                Button(_dpadHeld, true);
                _dpadDown = true;
                return "on " + Title.Menu.Selected;
            });
        }

        private void PlanTitle()
        {
            Do("a save, so the title has one to load", () => { Sandbox.SaveTo(SavePaths.Legacy); return null; });
            Reload("to the title", LaunchKind.Title);
            Wait(0.3);
            Do("the title, its hint naming A", () =>
            {
                if (Title?.IsOpen != true)
                {
                    return "no title";
                }
                if (Title.MainHintText != TitleScreen.PadMainHint)
                {
                    return $"the hint reads '{Title.MainHintText}'";
                }
                _choice = Title.Menu.Selected;
                _zoom = Camera.OrthographicSize;
                return null;
            });
            Do("the stick down, in five motion events at once", () =>
            {
                foreach (float v in new[] { 0.55f, 0.7f, 0.85f, 0.95f, 1f })
                {
                    Axis(JoyAxis.LeftY, v);
                }
                return null;
            });
            // Let go inside the repeat's first wait (0.3 s): held longer, it steps again, as it should.
            Do("let go: one step down, not five; the camera unmoved", () =>
            {
                Axis(JoyAxis.LeftY, 0f);
                List<Menus.TitleChoice> order = Menus.TitleMenu.Order.ToList();
                Menus.TitleChoice next = order.Skip(order.IndexOf(_choice) + 1).FirstOrDefault(c => Title.Menu.IsEnabled(c));
                if (Title.Menu.Selected != next)
                {
                    return $"on {Title.Menu.Selected}, from {_choice}, expected {next} (enabled: "
                        + string.Join(", ", order.Where(c => Title.Menu.IsEnabled(c))) + ")";
                }
                return Mathf.Abs(Camera.OrthographicSize - _zoom) > 0.01f ? "the stick zoomed the showcase" : null;
            });
            WalkTitle(Menus.TitleChoice.Settings);
            PressButton("A on Settings", JoyButton.A);
            Wait(0.1);
            Do("Settings, the ring on Master", () =>
                !Title.SettingsOpen ? "A didn't open Settings"
                : Title.PadFocusControl == Title.Settings.Master && Title.PadRingVisible ? null : $"the ring is on {Title.PadFocusControl?.Name}");
            Do("LT on Master", () => { _titleVolume = Title.Settings.Master.Value; Axis(JoyAxis.TriggerLeft, 1f); return null; });
            Do("LT up: a step down", () =>
            {
                Axis(JoyAxis.TriggerLeft, 0f);
                return Title.Settings.Master.Value < _titleVolume ? null : $"Master {Title.Settings.Master.Value}";
            });
            PressAxis("RT on Master", JoyAxis.TriggerRight);
            PressButton("B", JoyButton.B);
            Do("B is back on the menu", () => Title.SettingsOpen ? "still in Settings" : null);
            WalkTitle(Menus.TitleChoice.Load);
            PressButton("A on Load", JoyButton.A);
            Wait(0.1);
            Do("Load, its hint naming A and B", () =>
            {
                if (!Title.LoadOpen)
                {
                    return "A didn't open Load";
                }
                if (Title.LoadHintText != TitleScreen.PadLoadHint)
                {
                    return $"the hint reads '{Title.LoadHintText}'";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "pad-title-load");
                return null;
            });
            PressButton("d-pad right", JoyButton.DpadRight);
            Do("Creative's slots", () => Title.LoadingCreative ? null : "still the campaign's");
            PressButton("d-pad left", JoyButton.DpadLeft);
            Do("the campaign's again, a slot chosen", () =>
                Title.LoadingCreative ? "still Creative's" : Title.SlotBoard.Selected != null ? null : "no slot chosen");
            Reloading("A loads it", 1, () => Button(JoyButton.A, true));
            Do("let go of A: the game, loaded", () =>
            {
                Button(JoyButton.A, false);
                if (World.Launch != LaunchKind.Continue || Title?.IsOpen == true)
                {
                    return $"launch {World.Launch}, title open {Title?.IsOpen}";
                }
                Log("the title by pad: a held stick stepped once, Settings by the ring, Load's kinds by the d-pad, A loaded the slot");
                return null;
            });
        }

        // --- 10. The Management screen ----------------------------------------------------------

        private ManagementScreen Menu => Tree.Root.FindChild("Management", true, false) as ManagementScreen;
        private ManagementTab _tabBefore;
        private double _volumeBefore;
        private bool _muteBefore;
        private int _ledgerScroll;

        private ScrollContainer LedgerScroll =>
            Menu.TabContent[ManagementTab.TechTree].FindChildren("*", "ScrollContainer", true, false).OfType<ScrollContainer>().First();

        private void PlanManagement()
        {
            PressButton("Start", JoyButton.Start);
            Wait(0.1);
            Do("the menu is open with the ring in it", () =>
            {
                if (!Menu.IsOpen)
                {
                    return "Start didn't open the menu";
                }
                _tabBefore = Menu.Tabs.ActiveTab;
                return Menu.PadRingVisible ? null : "no ring";
            });
            PressButton("RB", JoyButton.RightShoulder);
            Do("the next tab", () =>
            {
                List<ManagementTab> bar = ManagementTabs.BarOrder.Where(t => Menu.TabButtons[t].Visible).ToList();
                ManagementTab expected = bar[(bar.IndexOf(_tabBefore) + 1) % bar.Count];
                return Menu.Tabs.ActiveTab == expected ? null : $"on {Menu.Tabs.ActiveTab}, expected {expected}";
            });
            PressButton("LB", JoyButton.LeftShoulder);
            Do("back, and nothing behind the menu heard LB", () =>
                Sandbox.Build.IsBuildToolActive ? "LB picked up a build tool behind the menu"
                : Menu.Tabs.ActiveTab == _tabBefore ? null : $"on {Menu.Tabs.ActiveTab}");
            Do("d-pad up behind the menu", () => { _zoom = Camera.OrthographicSize; Button(JoyButton.DpadUp, true); return null; });
            Do("d-pad up let go", () => { Button(JoyButton.DpadUp, false); return null; });
            Wait(0.3);
            Do("the camera didn't zoom behind the menu", () =>
                Mathf.Abs(Camera.OrthographicSize - _zoom) > 0.01f ? "the d-pad zoomed the camera behind the menu" : null);
            Until("RB to Settings", 4, () =>
            {
                if (_dpadDown)
                {
                    Button(JoyButton.RightShoulder, false);
                    _dpadDown = false;
                    return "letting go";
                }
                if (Menu.Tabs.ActiveTab == ManagementTab.Settings)
                {
                    return null;
                }
                Button(JoyButton.RightShoulder, true);
                _dpadDown = true;
                return "on " + Menu.Tabs.ActiveTab;
            });
            Do("let go of RB; the ring on the first slider, and never on a key button", () =>
            {
                if (_dpadDown)
                {
                    Button(JoyButton.RightShoulder, false);
                    _dpadDown = false;
                }
                if (Menu.PadStopControls.Any(c => Menu.Settings.KeyButtons.Values.Contains(c as Button)))
                {
                    return "a key button is a stop";
                }
                return Menu.PadFocusControl == Menu.Settings.Master ? null : $"the ring is on {Menu.PadFocusControl?.Name}";
            });
            Do("LT on Master", () => { _volumeBefore = Menu.Settings.Master.Value; Axis(JoyAxis.TriggerLeft, 1f); return null; });
            Do("LT up: the volume fell a step", () =>
            {
                Axis(JoyAxis.TriggerLeft, 0f);
                return Menu.Settings.Master.Value < _volumeBefore ? null : $"Master {Menu.Settings.Master.Value} from {_volumeBefore}";
            });
            PressAxis("RT on Master", JoyAxis.TriggerRight);
            Do("back up", () => Mathf.IsEqualApprox((float)Menu.Settings.Master.Value, (float)_volumeBefore) ? null : $"Master {Menu.Settings.Master.Value}");
            Walk("Mute", () => Menu.PadFocusKind, () => Menu.PadFocusControl == Menu.Settings.Mute ? 1 : 0, ManagementScreen.StopItem, () => 1,
                () => Menu.Settings.Mute, () => Menu.PadRingRect, list: false);
            Do("A on Mute", () =>
            {
                _muteBefore = Menu.Settings.Mute.ButtonPressed;
                ScenarioCapture.Shot(Runner.GetViewport(), "pad-management-settings");
                Button(JoyButton.A, true);
                return null;
            });
            Do("A up: Mute ticked", () =>
            {
                Button(JoyButton.A, false);
                return Menu.Settings.Mute.ButtonPressed != _muteBefore ? null : "A didn't tick Mute";
            });
            PressButton("A on Mute again", JoyButton.A);
            Do("untick", () => Menu.Settings.Mute.ButtonPressed == _muteBefore ? null : "A didn't untick Mute");
            Do("to the Tech tree", () => { Menu.SelectTab(ManagementTab.TechTree); return null; });
            Wait(0.2);
            Do("right stick right", () => { _ledgerScroll = LedgerScroll.ScrollHorizontal; Axis(JoyAxis.RightX, 1f); return null; });
            Wait(0.3);
            Do("let go: the chart panned", () =>
            {
                Axis(JoyAxis.RightX, 0f);
                return LedgerScroll.ScrollHorizontal > _ledgerScroll ? null : $"scroll {LedgerScroll.ScrollHorizontal} from {_ledgerScroll}";
            });
            PressButton("B", JoyButton.B);
            Do("B closed the menu", () =>
            {
                if (Menu.IsOpen)
                {
                    return "still open";
                }
                Log("the menu by pad: LB and RB changed tab, RT and LT stepped a slider, A ticked a checkbox, the right stick panned the Ledger, B closed");
                return null;
            });
        }

        // --- 8. The Crate screen ----------------------------------------------------------------

        private static readonly Compat.Vector2Int DepotCell = new Compat.Vector2Int(-4, 4);
        private Buildings.ICrate _depot;
        private int _packScrap;

        private int PackOf(string good) => Sandbox.Buffers.GetQuantity(Sandbox.StockpileBufferId, good);

        private void WalkCrate(string name, string kind, System.Func<int> index, System.Func<Control> target, bool list) =>
            Walk(name, () => CrateUi.PadFocusKind, () => CrateUi.PadFocusIndex, kind, index, target, () => CrateUi.PadRingRect, list);

        private void PlanCrate()
        {
            Do("a depot holding 10 Scrap; stand north of it", () =>
            {
                Buildings.PlaceableBuilding prefab = Sandbox.Placeables.Single(p => p.Key == "DepotPrefab").Prefab;
                foreach (RecipeIngredient c in prefab.Cost)
                {
                    Sandbox.Buffers.Deposit(Sandbox.StockpileBufferId, c.itemType, c.quantity);
                }
                Sandbox.Build.SetActivePrefab(prefab);
                Sandbox.Build.PlaceOrRemove(DepotCell);
                Sandbox.Build.CancelPlacement();
                _depot = Sandbox.Build.Buildings.SingleOrDefault(b => b.Cell == DepotCell && !b.IsRemoved)?.GetPart<Buildings.ICrate>();
                if (_depot == null)
                {
                    return $"no depot placed at {DepotCell}: {Sandbox.Build.LastStatusMessage}";
                }
                _depot.Buffer.Deposit(ItemType.Scrap, 10);
                Sandbox.Interactor.Teleport(new Compat.Vector3(DepotCell.x, DepotCell.y + 1, 0f));
                return null;
            });
            Wait(0.2);
            PressButton("Y at the depot", JoyButton.Y);
            Wait(0.1);
            Do("the Crate screen, the ring on the crate's Scrap, the hint naming A and X", () =>
            {
                if (!CrateUi.IsOpen || CrateUi.Crate != _depot)
                {
                    return "Y didn't open the depot's crate: " + Sandbox.Interactor.LastStatusMessage;
                }
                if (CrateUi.PadFocusKind != CrateScreen.StopCrate || CrateUi.CrateRowsInOrder[CrateUi.PadFocusIndex].good != ItemType.Scrap)
                {
                    return $"the ring is on {CrateUi.PadFocusKind} {CrateUi.PadFocusIndex}";
                }
                if (!CrateUi.PadRingVisible || !CrateUi.PadRingRect.Encloses(CrateUi.CrateRows[ItemType.Scrap].GetGlobalRect()))
                {
                    return "the ring doesn't surround the crate's Scrap row";
                }
                if (CrateUi.HintText != CrateScreen.PadHint)
                {
                    return $"the hint reads '{CrateUi.HintText}'";
                }
                _packScrap = PackOf(ItemType.Scrap);
                ScenarioCapture.Shot(Runner.GetViewport(), "pad-crate");
                return null;
            });
            PressButton("A", JoyButton.A);
            Do("one Scrap taken", () =>
                _depot.Buffer.GetQuantity(ItemType.Scrap) == 9 && PackOf(ItemType.Scrap) == _packScrap + 1
                    ? null : $"crate {_depot.Buffer.GetQuantity(ItemType.Scrap)}, pack {PackOf(ItemType.Scrap)} (was {_packScrap})");
            PressButton("X", JoyButton.X);
            Do("the rest taken: what fits", () =>
                _depot.Buffer.GetQuantity(ItemType.Scrap) == 0 && PackOf(ItemType.Scrap) == _packScrap + 10
                    ? null : $"crate {_depot.Buffer.GetQuantity(ItemType.Scrap)}, pack {PackOf(ItemType.Scrap)} (was {_packScrap})");
            WalkCrate("the pack's Scrap", CrateScreen.StopPack, () => CrateUi.PackRowsInOrder.ToList().FindIndex(r => r.good == ItemType.Scrap),
                () => CrateUi.PackRows[ItemType.Scrap], list: true);
            PressButton("A on the pack's Scrap", JoyButton.A);
            Do("one Scrap put back", () =>
                _depot.Buffer.GetQuantity(ItemType.Scrap) == 1 ? null : $"crate {_depot.Buffer.GetQuantity(ItemType.Scrap)}");
            WalkCrate("the label button", CrateScreen.StopLabel, () => 0, () => CrateUi.LabelButton, list: false);
            PressButton("A on the label button", JoyButton.A);
            Do("the picker is open, the ring on the crate's label (Any)", () =>
            {
                if (!CrateUi.PickerOpen)
                {
                    return "A didn't open the picker";
                }
                if (CrateUi.PadFocusKind != CrateScreen.StopOption || CrateUi.PickerOptions[CrateUi.PadFocusIndex].good != null)
                {
                    return $"the ring is on {CrateUi.PadFocusKind} {CrateUi.PadFocusIndex}";
                }
                return null;
            });
            PressButton("d-pad right", JoyButton.DpadRight);
            Do("the ring on the next option", () =>
            {
                if (CrateUi.PadFocusKind != CrateScreen.StopOption || CrateUi.PadFocusIndex != 1)
                {
                    return $"the ring is on {CrateUi.PadFocusKind} {CrateUi.PadFocusIndex}";
                }
                string named = CrateScreenReadout.OptionName(CrateUi.PickerOptions[1].good);
                if (CrateUi.Status != named)
                {
                    return $"the status reads '{CrateUi.Status}', not '{named}'";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "pad-crate-picker");
                return null;
            });
            PressButton("A on it", JoyButton.A);
            Do("the crate is labelled; the picker closed onto the label button", () =>
            {
                string good = CrateUi.PickerOptions.Count > 1 ? CrateUi.PickerOptions[1].good : null;
                if (CrateUi.PickerOpen)
                {
                    return "the picker stayed open";
                }
                if (_depot.FilterItemType == null)
                {
                    return "the crate has no label: " + CrateUi.Status;
                }
                return CrateUi.PadFocusKind == CrateScreen.StopLabel ? null : $"the ring is on {CrateUi.PadFocusKind} {CrateUi.PadFocusIndex}";
            });
            PressButton("A on the label button again", JoyButton.A);
            PressButton("B", JoyButton.B);
            Do("B closed the picker, not the screen", () =>
                CrateUi.PickerOpen || !CrateUi.IsOpen ? $"picker {CrateUi.PickerOpen}, screen {CrateUi.IsOpen}" : null);
            PressButton("B", JoyButton.B);
            Do("B closed the screen", () => CrateUi.IsOpen ? "still open" : null);
            PressButton("Y at the depot", JoyButton.Y);
            Wait(0.1);
            WalkCrate("Take all", CrateScreen.StopTakeAll, () => 0, () => CrateUi.TakeAllButton, list: false);
            PressButton("A on Take all", JoyButton.A);
            Do("the crate is empty", () => _depot.Buffer.GetQuantity(ItemType.Scrap) == 0 ? null : $"crate {_depot.Buffer.GetQuantity(ItemType.Scrap)}");
            PressButton("Y", JoyButton.Y);
            Do("Y closed the screen", () =>
            {
                if (CrateUi.IsOpen)
                {
                    return "still open";
                }
                Log("the Crate screen by pad: A took one, X what fits, A put one back, the picker labelled it, Take all, B and Y closed");
                return null;
            });
        }

        // --- 9. The Pack screen -----------------------------------------------------------------

        private void PlanPack()
        {
            Do("one of every good in the pack; stand in the open", () =>
            {
                foreach (string good in ItemTiers.CanonicalOrder)
                {
                    Sandbox.Buffers.Deposit(Sandbox.StockpileBufferId, good, 1);
                }
                Sandbox.Interactor.Teleport(OpenFloor + new Compat.Vector3(3f, 0f, 0f));
                return null;
            });
            Wait(0.2);
            PressButton("Y", JoyButton.Y);
            Wait(0.1);
            Do("the pack, the ring on its first row, the list longer than its window", () =>
            {
                if (!PackUi.IsOpen)
                {
                    return "Y didn't open the pack";
                }
                if (!PackUi.PadRingVisible || PackUi.PadFocusKind != PackScreen.StopRow || PackUi.PadFocusIndex != 0)
                {
                    return $"the ring ({PackUi.PadRingVisible}) is on {PackUi.PadFocusKind} {PackUi.PadFocusIndex}";
                }
                Button last = PackUi.RowsInOrder[PackUi.RowsInOrder.Count - 1].row;
                if (PackUi.HintText != PackScreen.PadHint)
                {
                    return $"the hint reads '{PackUi.HintText}'";
                }
                return last.GetGlobalRect().End.Y > PackUi.RowsView.GetGlobalRect().End.Y + 1f ? null : "the whole pack fits its window";
            });
            Walk("the last row", () => PackUi.PadFocusKind, () => PackUi.PadFocusIndex, PackScreen.StopRow,
                () => PackUi.RowsInOrder.Count - 1, () => PackUi.RowsInOrder[PackUi.RowsInOrder.Count - 1].row, () => default, list: true);
            Wait(0.1);
            Do("the last row is scrolled into view", () =>
            {
                Button last = PackUi.RowsInOrder[PackUi.RowsInOrder.Count - 1].row;
                if (!PackUi.RowsView.GetGlobalRect().Grow(1f).Encloses(last.GetGlobalRect()))
                {
                    return "the last row is outside the window";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "pad-pack");
                return null;
            });
            PressButton("B", JoyButton.B);
            Do("B closed the pack", () =>
            {
                if (PackUi.IsOpen)
                {
                    return "still open";
                }
                Log("the pack's ring walked a long list to its last row; B closed it");
                return null;
            });
        }

        // --- 7. A long vault scrolls to the ring ------------------------------------------------

        private void PlanVaultScroll()
        {
            PlanReopen();
            Do("every card offered, so the vault is longer than its window", () =>
            {
                Session.ConfigureCardGating(null, null);
                return null;
            });
            Until("the vault overflows", 1, () =>
            {
                WorkbenchCardControl last = Bench.VaultList.GetChildren().OfType<WorkbenchCardControl>().LastOrDefault(c => !c.IsQueuedForDeletion());
                return last != null && last.GetGlobalRect().End.Y > Bench.VaultView.GetGlobalRect().End.Y + 1f ? null : "the whole vault fits its window";
            });
            NavigateTo("the vault", Stop.Vault, () => 0);
            NavigateTo("the last card", Stop.Vault, () => Bench.VaultList.GetChildren().OfType<WorkbenchCardControl>().Count(c => !c.IsQueuedForDeletion()) - 1);
            Wait(0.1);
            Do("the last card is scrolled into the window, the ring around it", () =>
            {
                WorkbenchCardControl last = Bench.VaultList.GetChildren().OfType<WorkbenchCardControl>().Last(c => !c.IsQueuedForDeletion());
                string name = last.CardRef.Appendage?.name ?? last.CardRef.LogicCore?.name;
                string ring = RingOn(name);
                if (ring != null)
                {
                    return ring;
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "pad-workbench-scroll");
                return null;
            });
            PressButton("B", JoyButton.B);
            Do("closed again", () =>
            {
                if (Bench.IsOpen)
                {
                    return "B didn't close it";
                }
                Log("the d-pad walked a long vault to its last card, scrolled into view");
                return null;
            });
        }

        protected override void Cleanup()
        {
            foreach (JoyAxis axis in new[] { JoyAxis.LeftX, JoyAxis.LeftY, JoyAxis.TriggerLeft, JoyAxis.TriggerRight })
            {
                Axis(axis, 0f);
            }
            InputDevices.Reset();
            ScenarioSaves.Clear();
        }

        // --- 0.A fresh game, the playtest card answered (a click), the pad picked up ----------

        private void PlanStart()
        {
            Do("stand at the starting Scrap cart; nudge the left stick", () =>
            {
                Compat.Vector2Int scrap = World.Setup.HomeSpot(ItemType.Scrap).Value;
                Sandbox.Interactor.Teleport(new Compat.Vector3(scrap.x, scrap.y + 1f, 0f));
                Axis(JoyAxis.LeftX, 0.6f);
                return null;
            });
            Do("let go: the pad is in use", () =>
            {
                Axis(JoyAxis.LeftX, 0f);
                return InputDevices.PadInUse ? null : "the stick didn't count as the pad";
            });
            PlanCard();
            Do("the clock running", () => { Sandbox.Clock.Play(); return null; });
        }

        // --- 0b. The playtest card by pad -------------------------------------------------------

        private PlaytestNode Card => Tree.Root.FindChild("Playtest", true, false) as PlaytestNode;
        private Tutorial.PlaytestQuestion _question;
        private Compat.Vector3 _standing;
        private int _scrapBefore;

        private void PlanCard()
        {
            Wait(0.2);
            Do("the card is up, saying the d-pad's left answers it", () =>
            {
                if (Card?.Card.Visible != true)
                {
                    return "no playtest card";
                }
                _question = Sandbox.Tutorial.Playtest.Current;
                return Card.CounterLine == PlaytestNode.PadCounterText ? null : $"the counter reads '{Card.CounterLine}'";
            });
            PressButton("d-pad left", JoyButton.DpadLeft);
            Do("the card holds the pad, the ring on the first answer", () =>
            {
                if (!Card.PadHeld || !InputDevices.OverlayHoldsPad)
                {
                    return "the d-pad's left didn't take the pad into the card";
                }
                if (Card.PadFocusKind != PlaytestNode.StopOption || Card.PadFocusIndex != 0 || !Card.PadRingVisible)
                {
                    return $"the ring ({Card.PadRingVisible}) is on {Card.PadFocusKind} {Card.PadFocusIndex}";
                }
                return Card.CounterLine == PlaytestNode.CounterText ? null : $"the counter still reads '{Card.CounterLine}'";
            });
            Do("hold the left stick", () => { _standing = Sandbox.Interactor.Position; Axis(JoyAxis.LeftX, 1f); return null; });
            Wait(0.3);
            Do("let go: the Artificer stood still", () =>
            {
                Axis(JoyAxis.LeftX, 0f);
                Compat.Vector3 now = Sandbox.Interactor.Position;
                return Mathf.Abs(now.x - _standing.x) < 0.01f ? null : $"walked {now.x - _standing.x:0.00} cells while the card held the pad";
            });
            Do("d-pad up while the card holds the pad", () => { _zoom = Camera.OrthographicSize; Button(JoyButton.DpadUp, true); return null; });
            Do("d-pad up let go", () => { Button(JoyButton.DpadUp, false); return null; });
            Wait(0.3);
            Do("the camera didn't zoom behind the card", () =>
                Mathf.Abs(Camera.OrthographicSize - _zoom) > 0.01f ? "the d-pad zoomed the camera while the card held the pad" : null);
            PressButton("B", JoyButton.B);
            Do("B let go of the card; the question still waits", () =>
                Card.PadHeld || InputDevices.OverlayHoldsPad ? "B didn't let go" : Sandbox.Tutorial.Playtest.Current == _question ? null : "B answered it");
            PressButton("d-pad left again", JoyButton.DpadLeft);
            Walk("the note", () => Card.PadFocusKind, () => Card.PadFocusIndex, PlaytestNode.StopNote, () => 0, () => Card.Note, () => Card.PadRingRect, list: false);
            PressButton("A on the note", JoyButton.A);
            Do("the note takes typing", () => Card.Note.HasFocus() ? null : "A didn't open the note");
            PressButton("B", JoyButton.B);
            Do("B is back on the ring, still holding the card", () =>
                !Card.Note.HasFocus() && Card.PadHeld ? null : $"note focused {Card.Note.HasFocus()}, held {Card.PadHeld}");
            Walk("the second answer", () => Card.PadFocusKind, () => Card.PadFocusIndex, PlaytestNode.StopOption, () => 1,
                () => Card.Options.GetChildren().OfType<Button>().ElementAt(1), () => Card.PadRingRect, list: false);
            Do("capture; A", () =>
            {
                ScenarioCapture.Shot(Runner.GetViewport(), "pad-card");
                _scrapBefore = Sandbox.Buffers.GetQuantity(Sandbox.StockpileBufferId, ItemType.Scrap);
                Button(JoyButton.A, true);
                return null;
            });
            Do("let go of A", () => { Button(JoyButton.A, false); return null; });
            Wait(0.1);
            Do("answered with the second option; the pad back in the world, and no Scrap harvested by that A", () =>
            {
                if (Sandbox.Tutorial.Playtest.Current == _question)
                {
                    return "still on the same question";
                }
                if (Card.PadHeld || InputDevices.OverlayHoldsPad)
                {
                    return "the card kept the pad";
                }
                int scrap = Sandbox.Buffers.GetQuantity(Sandbox.StockpileBufferId, ItemType.Scrap);
                if (scrap != _scrapBefore)
                {
                    return $"the A that answered also harvested ({_scrapBefore} -> {scrap})";
                }
                Log("the playtest card by pad: the d-pad's left took it, the stick didn't walk, B let go, A opened the note and B closed it, A answered");
                return null;
            });
        }

        // --- 1. The Workbench opens with the ring on the hinted card ----------------------------

        private void PlanOpen()
        {
            Do("a Scavenger's cost in the pack, and only that; stand south of the station", () =>
            {
                foreach (RecipeIngredient c in Scavenger.cost)
                {
                    Sandbox.Buffers.Deposit(Sandbox.StockpileBufferId, c.itemType, c.quantity);
                }
                Compat.Vector2Int station = Sandbox.StarterStation.Cell;
                Sandbox.Interactor.Teleport(new Compat.Vector3(station.x, station.y - 1, 0f));
                return null;
            });
            Wait(0.2);
            Do("tap the left stick up, to face it", () => { Axis(JoyAxis.LeftY, -1f); return null; });
            Do("let go", () => { Axis(JoyAxis.LeftY, 0f); return null; });
            Wait(0.2);
            PressButton("A at the station", JoyButton.A);
            Wait(0.1);
            Do("the panel is open, the ring on the Scavenger's row, the one the pack pays for", () =>
            {
                if (!Panel.IsOpen)
                {
                    return "A didn't open the station's panel: " + Sandbox.Interactor.LastStatusMessage;
                }
                int scavenger = Panel.Rows.ToList().FindIndex(r => r.chassis == Scavenger);
                if (Panel.PadFocusKind != ConstructionPanelNode.StopChassis || Panel.PadFocusIndex != scavenger)
                {
                    return $"the ring is on {Panel.PadFocusKind} {Panel.PadFocusIndex}, the Scavenger is row {scavenger}";
                }
                if (!Panel.PadRingVisible || !Panel.PadRingRect.Encloses(Panel.Rows[scavenger].row.GetGlobalRect()))
                {
                    return $"the ring ({Panel.PadRingVisible}) {Panel.PadRingRect} doesn't surround the row";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "pad-station");
                return null;
            });
            PressButton("d-pad down, to a row the pack can't pay for", JoyButton.DpadDown);
            Do("the ring on a greyed row", () =>
                Panel.PadFocusKind == ConstructionPanelNode.StopChassis && Panel.Rows[Panel.PadFocusIndex].row.Disabled
                    ? null : $"the ring is on {Panel.PadFocusKind} {Panel.PadFocusIndex}");
            PressButton("A on it", JoyButton.A);
            Do("nothing built; the panel says what's short", () =>
            {
                if (!Panel.IsOpen || Bench.IsOpen)
                {
                    return $"panel open {Panel.IsOpen}, Workbench open {Bench.IsOpen}";
                }
                return Panel.Status.Contains(" to build ") ? null : $"status '{Panel.Status}'";
            });
            PressButton("d-pad up, back to the Scavenger", JoyButton.DpadUp);
            Do("press A on the Scavenger", () =>
            {
                _zoom = Camera.OrthographicSize;
                Button(JoyButton.A, true);
                return null;
            });
            Do("let go of A", () => { Button(JoyButton.A, false); return null; });
            Wait(0.1);
            Do("built; the panel closed onto the Workbench", () =>
            {
                _golem = Panel.LastBuilt;
                if (_golem == null || _golem.Program.chassis != Scavenger)
                {
                    return "no Scavenger built: " + Panel.Status;
                }
                if (Panel.IsOpen)
                {
                    return "the panel stayed open";
                }
                Log("the station's panel by pad: A at the station, the ring on the row the pack pays for, A on a greyed row said what's short, A built");
                return null;
            });
            Wait(0.1);
            Do("the Workbench is open on it, nothing sent by the A that built it, the ring on Always On, the card the guide lights", () =>
            {
                if (!Bench.IsOpen || Bench.TargetGolem != _golem)
                {
                    return $"open {Bench.IsOpen} on {Bench.TargetGolem?.GolemId}: {Sandbox.Interactor.LastStatusMessage}";
                }
                if (Session.DraftLogicCore != null || Session.AssignedAppendageCount != 0)
                {
                    return "the A that built the golem also sent a card";
                }
                string ring = RingOn("AlwaysOnCore");
                if (ring != null)
                {
                    return ring;
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "pad-workbench-open");
                return null;
            });
        }

        /// <summary>Reopens the Workbench on the golem by A at it, after X carries it off the station, where A builds.</summary>
        private void PlanReopen()
        {
            Do("stand south of it", () =>
            {
                Sandbox.Interactor.Teleport(new Compat.Vector3(_golem.Cell.x, _golem.Cell.y - 1, 0f));
                return null;
            });
            Wait(0.2);
            PressButton("X picks it up", JoyButton.X);
            Do("carrying it; walk to open floor", () =>
            {
                if (Sandbox.Interactor.CarriedGolem != _golem)
                {
                    return "X picked nothing up: " + Sandbox.Interactor.LastStatusMessage;
                }
                Sandbox.Interactor.Teleport(OpenFloor);
                return null;
            });
            Wait(0.2);
            PressButton("X sets it down", JoyButton.X);
            Do("set down; step south of it", () =>
            {
                if (Sandbox.Interactor.CarriedGolem != null)
                {
                    return "still carrying: " + Sandbox.Interactor.LastStatusMessage;
                }
                Sandbox.Interactor.Teleport(new Compat.Vector3(_golem.Cell.x, _golem.Cell.y - 1, 0f));
                return null;
            });
            Wait(0.2);
            Do("tap the left stick up, to face it", () => { Axis(JoyAxis.LeftY, -1f); return null; });
            Do("let go", () => { Axis(JoyAxis.LeftY, 0f); return null; });
            Wait(0.2);
            PressButton("A at the golem", JoyButton.A);
            Wait(0.1);
            Do("the Workbench is open on it again, its program kept", () =>
            {
                if (!Bench.IsOpen || Bench.TargetGolem != _golem)
                {
                    return $"open {Bench.IsOpen} on {Bench.TargetGolem?.GolemId}: {Sandbox.Interactor.LastStatusMessage}";
                }
                return Session.AssignedAppendageCount == 2 ? null : $"the draft holds {Session.AssignedAppendageCount} steps";
            });
        }

        // --- 2. A sends each card; the ring follows the hint ------------------------------------

        private void PlanSend()
        {
            PressButton("A on Always On", JoyButton.A);
            Do("Always On in TRIGGER, landing; the ring on Extract", () =>
            {
                if (Session.DraftLogicCore?.name != "AlwaysOnCore")
                {
                    return "the trigger holds " + (Session.DraftLogicCore?.name ?? "nothing");
                }
                return Bench.CardLanding ? RingOn("ExtractScrap") : "no landing";
            });
            PressButton("A on Extract", JoyButton.A);
            Do("Extract in STEP 1; the ring on Push Output", () => Draft(0) != "ExtractScrap" ? $"STEP 1 holds {Draft(0)}" : RingOn("PushOutput"));
            PressButton("A on Push Output", JoyButton.A);
            Do("Push Output in STEP 2; the ring on the lever; the golem untouched", () =>
            {
                if (Draft(1) != "PushOutput")
                {
                    return $"STEP 2 holds {Draft(1)}";
                }
                if (_golem.Program.appendages.Count != 0)
                {
                    return "a send wrote to the golem before the lever";
                }
                if (Bench.PadFocusKind != Stop.Lever || !Encloses(Bench.PadRingRect, Bench.Lever))
                {
                    return $"the ring is on {Bench.PadFocusKind} {Bench.PadFocusIndex}";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "pad-workbench-lever");
                return null;
            });
            PressButton("LB behind the screen", JoyButton.LeftShoulder);
            PressButton("d-pad up behind the screen", JoyButton.DpadUp);
            Do("the build menu and the camera didn't hear them", () =>
            {
                if (Sandbox.Build.IsBuildToolActive)
                {
                    return "LB picked up a build tool behind the Workbench";
                }
                return Mathf.Abs(Camera.OrthographicSize - _zoom) > 0.01f ? "the d-pad zoomed the camera behind the Workbench" : null;
            });
            Do("log", () => { Log("A sent Always On, Extract and Push Output, the ring following the hint to the lever"); return null; });
        }

        // --- 3. The d-pad to STEP 1; the triggers on its dial; X and A --------------------------

        private void PlanDial()
        {
            NavigateTo("STEP 1", Stop.Step, () => 0);
            PressAxis("RT", JoyAxis.TriggerRight);
            Do("the batch size rose, and the dial names the triggers", () =>
            {
                if (Session.DraftQuantityAt(0) != 2)
                {
                    return $"batch {Session.DraftQuantityAt(0)}";
                }
                string minus = Glyph(0, "Decrease"), plus = Glyph(0, "Increase");
                if (minus != "LT" || plus != "RT")
                {
                    return $"the dial reads '{minus}' and '{plus}'";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "pad-workbench-dial");
                return null;
            });
            PressAxis("LT", JoyAxis.TriggerLeft);
            Do("back to one", () => Session.DraftQuantityAt(0) == 1 ? null : $"batch {Session.DraftQuantityAt(0)}");
            PressButton("X on STEP 1", JoyButton.X);
            Do("STEP 1 empty, STEP 2 kept; the ring on Extract again", () =>
            {
                if (Draft(0) != null || Draft(1) != "PushOutput")
                {
                    return $"steps {Draft(0)}, {Draft(1)}";
                }
                return RingOn("ExtractScrap");
            });
            PressButton("A on Extract", JoyButton.A);
            Do("Extract back in STEP 1, the first empty step", () => Draft(0) == "ExtractScrap" ? null : $"STEP 1 holds {Draft(0)}");
            Do("log", () => { Log("the d-pad reached STEP 1; RT and LT stepped its dial; X took it out and A sent it back"); return null; });
        }

        // --- 4. Every step full -----------------------------------------------------------------

        private void PlanFull()
        {
            NavigateTo("Haul Scrap in the vault", Stop.Vault, () => VaultIndex("HaulScrap"));
            PressButton("A on Haul Scrap", JoyButton.A);
            Do("nothing moved, and the status says why", () =>
            {
                if (Draft(0) != "ExtractScrap" || Draft(1) != "PushOutput")
                {
                    return $"steps {Draft(0)}, {Draft(1)}";
                }
                return Bench.StatusText == WorkbenchSession.StepsFullMessage ? null : $"status '{Bench.StatusText}'";
            });
        }

        // --- 5. A Haul's good by the shoulders --------------------------------------------------

        private void PlanHaul()
        {
            Do("the factory has made Coke, so a Haul can take it", () =>
            {
                Sandbox.Buffers.Deposit(Sandbox.StockpileBufferId, ItemType.Coke, 5);
                Sandbox.TechTree.Poll(); // the ledger sweeps once a second; don't depend on where the steps fall
                return null;
            });
            NavigateTo("STEP 2", Stop.Step, () => 1);
            PressButton("X on STEP 2", JoyButton.X);
            NavigateTo("Haul Scrap", Stop.Vault, () => VaultIndex("HaulScrap"));
            PressButton("A on Haul Scrap", JoyButton.A);
            Do("Haul in STEP 2", () => Draft(1) == "HaulScrap" ? null : $"STEP 2 holds {Draft(1)}");
            NavigateTo("STEP 2", Stop.Step, () => 1);
            Do("the picker names the shoulders", () =>
            {
                _good = Session.DraftItemTypeAt(1);
                string left = Glyph(1, "PreviousGood"), right = Glyph(1, "NextGood");
                return left == "LB" && right == "RB" ? null : $"the picker reads '{left}' and '{right}'";
            });
            PressButton("RB", JoyButton.RightShoulder);
            Do("the good changed", () =>
            {
                if (Session.DraftItemTypeAt(1) == _good)
                {
                    return $"still {_good}";
                }
                ScenarioCapture.Shot(Runner.GetViewport(), "pad-workbench-haul");
                return null;
            });
            PressButton("LB", JoyButton.LeftShoulder);
            Do("and back", () => Session.DraftItemTypeAt(1) == _good ? null : $"{Session.DraftItemTypeAt(1)}, not {_good}");
            PressButton("X on the Haul", JoyButton.X);
            Do("the ring follows the hint to Push Output", () => Draft(1) != null ? $"STEP 2 holds {Draft(1)}" : RingOn("PushOutput"));
            PressButton("A on Push Output", JoyButton.A);
            Do("Push Output back in STEP 2", () => Draft(1) == "PushOutput" ? null : $"STEP 2 holds {Draft(1)}");
            Do("log", () => { Log("RB and LB stepped a Haul's good"); return null; });
        }

        // --- 6. The lever and B -----------------------------------------------------------------

        private void PlanEngage()
        {
            Do("the ring on the lever", () => Bench.PadFocusKind == Stop.Lever ? null : $"the ring is on {Bench.PadFocusKind} {Bench.PadFocusIndex}");
            PressButton("A on the lever", JoyButton.A);
            Do("the program is the golem's", () =>
            {
                List<string> steps = _golem.Program.appendages.Select(a => a.name).ToList();
                return _golem.Program.logicCore?.name == "AlwaysOnCore" && steps.SequenceEqual(new[] { "ExtractScrap", "PushOutput" })
                    ? null : $"the golem runs {_golem.Program.logicCore?.name}: {string.Join(", ", steps)}";
            });
            PressButton("B", JoyButton.B);
            Do("the Workbench is closed", () =>
            {
                if (Bench.IsOpen)
                {
                    return "B didn't close it";
                }
                Log("A on the lever engaged it; B closed the Workbench");
                return null;
            });
        }

        // --- Helpers ----------------------------------------------------------------------------

        private string Draft(int slot) => Session.DraftAppendageAt(slot)?.name;

        private int VaultIndex(string card) =>
            Bench.VaultList.GetChildren().OfType<WorkbenchCardControl>().Where(c => !c.IsQueuedForDeletion())
                .Select((c, i) => (c, i)).FirstOrDefault(p => p.c.CardRef.Appendage?.name == card || p.c.CardRef.LogicCore?.name == card).i;

        private WorkbenchCardControl VaultCard(string card) =>
            Bench.VaultList.GetChildren().OfType<WorkbenchCardControl>().FirstOrDefault(c => !c.IsQueuedForDeletion()
                && (c.CardRef.Appendage?.name == card || c.CardRef.LogicCore?.name == card));

        /// <summary>Null if the ring rests on <paramref name="card"/> in the vault, drawn around it, in the vault's window.</summary>
        private string RingOn(string card)
        {
            WorkbenchCardControl control = VaultCard(card);
            if (control == null)
            {
                return card + " isn't in the vault";
            }
            if (!Bench.PadRingVisible || Bench.PadFocusControl != control)
            {
                return $"the ring ({Bench.PadRingVisible}) is on {Bench.PadFocusKind} {Bench.PadFocusIndex}, not {card}";
            }
            if (!Encloses(Bench.PadRingRect, control))
            {
                return $"the ring {Bench.PadRingRect} doesn't surround {card} {control.GetGlobalRect()}";
            }
            Rect2 view = Bench.VaultView.GetGlobalRect().Grow(1f);
            return view.Encloses(control.GetGlobalRect()) ? null : $"{card} is outside the vault's window";
        }

        private static bool Encloses(Rect2 ring, Control control) => ring.Encloses(control.GetGlobalRect());

        private string Glyph(int slot, string button) =>
            ((Bench.StepRows[slot].Card?.FindChild(button, true, false) as Control)?.FindChild("Label", false, false) as Label)?.Text;

        private void PressButton(string name, JoyButton button)
        {
            Do(name, () => { Button(button, true); return null; });
            Do(name + " up", () => { Button(button, false); return null; });
        }

        private void PressAxis(string name, JoyAxis axis)
        {
            Do(name, () => { Axis(axis, 1f); return null; });
            Do(name + " up", () => { Axis(axis, 0f); return null; });
        }

        /// <summary>The d-pad, a press and a release at a time, toward the stop until the ring is on it.</summary>
        private void NavigateTo(string name, Stop kind, System.Func<int> index) =>
            Walk(name, () => Bench.PadFocusKind.ToString(), () => Bench.PadFocusIndex, kind.ToString(), index,
                () => kind switch
                {
                    Stop.Step => Bench.StepRows[index()].Socket,
                    Stop.Vault => Bench.VaultList.GetChildren().OfType<WorkbenchCardControl>().Where(c => !c.IsQueuedForDeletion()).ElementAt(index()),
                    Stop.Lever => Bench.Lever,
                    _ => null,
                },
                () => Bench.PadRingRect, kind == Stop.Vault);

        /// <summary>
        /// Any screen's ring: the d-pad, a press and a release at a time, toward the stop until the
        /// ring is on it. Within a list (<paramref name="list"/>), up or down by index; else toward
        /// the target on screen.
        /// </summary>
        private void Walk(string name, System.Func<string> atKind, System.Func<int> atIndex, string kind, System.Func<int> index,
            System.Func<Control> target, System.Func<Rect2> ring, bool list)
        {
            Until("the d-pad walks the ring to " + name, 6, () =>
            {
                if (_dpadDown)
                {
                    Button(_dpadHeld, false);
                    _dpadDown = false;
                    return "letting go";
                }
                if (atKind() == kind && atIndex() == index())
                {
                    return null;
                }
                Vector2 d = target().GetGlobalRect().GetCenter() - ring().GetCenter();
                _dpadHeld = list && atKind() == kind
                    ? (index() > atIndex() ? JoyButton.DpadDown : JoyButton.DpadUp)
                    : Mathf.Abs(d.X) > Mathf.Abs(d.Y) ? (d.X > 0 ? JoyButton.DpadRight : JoyButton.DpadLeft) : (d.Y > 0 ? JoyButton.DpadDown : JoyButton.DpadUp);
                Button(_dpadHeld, true);
                _dpadDown = true;
                return $"the ring on {atKind()} {atIndex()}";
            });
            Do("let go of the d-pad", () =>
            {
                if (_dpadDown)
                {
                    Button(_dpadHeld, false);
                    _dpadDown = false;
                }
                return null;
            });
        }

        private static void Button(JoyButton button, bool down) =>
            Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = button, Pressed = down, Device = 0 });

        private static void Axis(JoyAxis axis, float value) =>
            Input.ParseInputEvent(new InputEventJoypadMotion { Axis = axis, AxisValue = value, Device = 0 });
    }
}
