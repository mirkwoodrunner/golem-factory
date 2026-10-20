using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.Player;
using GolemFactory.PunchCards;
using GolemFactory.World;
using CoreVector3 = GolemFactory.Compat.Vector3;
using CoreVector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// The Pack and Crate screens (docs/inventory-screens-design.md §6), with real keys and clicks:
    ///
    /// <list type="number">
    ///   <item>I away from a crate opens the Pack screen over a hidden HUD; I, E and Escape close it.</item>
    ///   <item>E at a crate collects what fits; a full pack greys the crate and the Scrap stall.</item>
    ///   <item>I at the crate opens the Crate screen alone, with both panels, the prompt hidden.</item>
    ///   <item>A click moves one, Shift-click all that fits, a short move says why; crate +
    ///   pack per good never changes.</item>
    ///   <item>The label picker offers "Any" and the ledger's goods; Copper Ore relabels the depot
    ///   and its tile; Scrap is then refused on the way in and still comes out.</item>
    ///   <item>Take all is E's collect.</item>
    ///   <item>A pack above its cap shows it, gives, and takes nothing back until under it.</item>
    ///   <item>A Freight Mast's label button is disabled. At a Post crate the left panel is the
    ///   Pneumatic Post, a Shift-click moves into the store every Post crate shares, and the Pack
    ///   screen then lists the store under the pack.</item>
    ///   <item>On a guide step done in the crate screen, the guide's plate clears both panels.</item>
    ///   <item>The wrecking bar refuses a crate whose Scrap won't fit in the pack, whatever room
    ///   the Post has, then refunds it exactly.</item>
    /// </list>
    ///
    /// Writes nothing to disk. The frames worth a look are logged ("frame N: ...") for a
    /// <c>--write-movie</c> run.
    /// </summary>
    public sealed class InventoryScenario : IScenario
    {
        private const int FramesBetweenSteps = 4;

        private readonly Queue<(string name, Func<string> step)> _steps = new Queue<(string, Func<string>)>();
        private readonly List<string> _log = new List<string>();
        private SceneTree _tree;
        private Viewport _viewport;
        private SandboxWorld _world;
        private PlayerNode _player;
        private InteractionPromptNode _prompt;
        private HudOverlay _hud;
        private PackScreen _pack;
        private CrateScreen _crate;
        private WorkbenchScreen _workbench;
        private ManagementScreen _management;
        private TutorialPanel _guide;
        private int _wait;
        private Key? _heldTap;

        private static readonly CoreVector2Int DepotCell = new CoreVector2Int(-4, 6);
        private static readonly CoreVector2Int MastCell = new CoreVector2Int(-6, 6);
        private static readonly CoreVector2Int PostCell = new CoreVector2Int(2, 6);
        private static readonly CoreVector2Int OtherPostCell = new CoreVector2Int(6, 6);
        private ICrate _depot;
        private Dictionary<string, int> _totals;

        public void Begin(ScenarioRunner runner)
        {
            _tree = runner.GetTree();
            _viewport = _tree.Root;
            _world = runner.World.Sandbox;
            _player = _tree.Root.FindChild("Player", true, false) as PlayerNode;
            _prompt = _tree.Root.FindChild("InteractionPrompt", true, false) as InteractionPromptNode;
            _hud = _tree.Root.FindChild("Hud", true, false) as HudOverlay;
            _pack = _tree.Root.FindChild("PackScreen", true, false) as PackScreen;
            _crate = _tree.Root.FindChild("CrateScreen", true, false) as CrateScreen;
            _workbench = _tree.Root.FindChild("Workbench", true, false) as WorkbenchScreen;
            _management = _tree.Root.FindChild("Management", true, false) as ManagementScreen;
            _guide = _tree.Root.FindChild("Tutorial", true, false) as TutorialPanel;
            if (_player == null || _prompt == null || _hud == null || _pack == null || _crate == null
                || _workbench == null || _management == null || _guide == null)
            {
                Do("scene", () => "Sandbox.tscn is missing the player, prompt, HUD, a screen or the guide");
                return;
            }

            PlanPackScreen();
            PlanCollect();
            PlanCrateScreen();
            PlanPutAndTake();
            PlanLabel();
            PlanTakeAll();
            PlanAboveTheCap();
            PlanGuideDock();
            PlanMast();
            PlanPost();
            PlanDemolition();
        }

        public ScenarioResult? Step(double delta)
        {
            if (_wait-- > 0)
            {
                return null;
            }
            if (_heldTap is Key held)
            {
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = held, Keycode = held, Pressed = false });
                _heldTap = null;
            }
            if (_steps.Count == 0)
            {
                return new ScenarioResult(true, string.Join("; ", _log));
            }
            (string name, Func<string> step) = _steps.Dequeue();
            string failure = step();
            if (failure != null)
            {
                return new ScenarioResult(false, $"{name}: {failure}");
            }
            _wait = FramesBetweenSteps;
            return null;
        }

        // --- 1. The Pack screen -----------------------------------------------------------------

        private void PlanPackScreen()
        {
            Do("stand away from every crate, carrying three goods", () =>
            {
                Give(ItemType.Scrap, 30);
                Give(ItemType.Coke, 5);
                Give(ItemType.IronPlate, 2);
                Stand(-9f, 9f);
                return null;
            });
            Do("press I", () => { Tap(Key.I); return null; });
            Do("the Pack screen is up alone, over a hidden HUD", () =>
            {
                if (!_pack.IsOpen || _crate.IsOpen)
                {
                    return $"pack open={_pack.IsOpen}, crate open={_crate.IsOpen}";
                }
                if (_hud.Showing || _world.Screens.OpenCount != 1)
                {
                    return $"HUD showing={_hud.Showing}, {_world.Screens.OpenCount} screens up";
                }
                return null;
            });
            Do("one row per good, each with an icon slot and x/100", () =>
            {
                List<string> held = _world.Wallet.Pack.HeldGoods();
                if (_pack.Rows.Count != held.Count || held.Any(g => !_pack.Rows.ContainsKey(g)))
                {
                    return $"{_pack.Rows.Count} rows for {held.Count} goods";
                }
                foreach ((string good, Button row) in _pack.Rows)
                {
                    string text = (row.FindChild("Quantity", true, false) as Label)?.Text;
                    if (row.FindChild("Icon", true, false) == null || text != Pack(good) + "/100")
                    {
                        return $"{good}'s row reads '{text}'";
                    }
                }
                Shot("pack screen");
                return null;
            });
            foreach (Key closer in new[] { Key.I, Key.E, Key.Escape })
            {
                Key captured = closer;
                if (captured != Key.I)
                {
                    Do("press I again", () => { Tap(Key.I); return null; });
                    Do("it is up", () => _pack.IsOpen ? null : "I did not reopen it");
                }
                Do($"press {captured}", () => { Tap(captured); return null; });
                Do($"{captured} closed it, the HUD is back", () =>
                    !_pack.IsOpen && _hud.Showing && _world.Screens.OpenCount == 0 ? null : $"open={_pack.IsOpen}, HUD={_hud.Showing}");
            }
            Do("pack", () => { _log.Add($"pack: I opened it ({_pack.Rows.Count} rows, x/100), I, E and Escape closed it"); return null; });
        }

        // --- 2. Collect -----------------------------------------------------------------------

        private void PlanCollect()
        {
            Do("a depot with 20 Scrap in its crate, the pack at 95", () =>
            {
                Give(ItemType.Scrap, 15);
                _world.Build.SetActivePrefab(_world.Placeables.Single(p => p.Key == "DepotPrefab").Prefab);
                _world.Build.PlaceOrRemove(DepotCell);
                _world.Build.CancelPlacement();
                _depot = _world.Build.Buildings.SingleOrDefault(b => b.Cell == DepotCell && !b.IsRemoved)?.GetPart<ICrate>();
                if (_depot == null)
                {
                    return "no depot placed: " + _world.Build.LastStatusMessage;
                }
                _depot.Buffer.Deposit(ItemType.Scrap, 20);
                SetPack(ItemType.Scrap, 95);
                Stand(DepotCell.x, DepotCell.y + 1f);
                return null;
            });
            Do("press E", () => { Tap(Key.E); return null; });
            Do("the pack filled to 100, the crate kept 15, and the prompt greys", () =>
            {
                if (Pack(ItemType.Scrap) != 100 || _depot.Buffer.GetQuantity(ItemType.Scrap) != 15)
                {
                    return $"pack {Pack(ItemType.Scrap)}, crate {_depot.Buffer.GetQuantity(ItemType.Scrap)}";
                }
                if (_world.Interactor.CurrentAffordance != InteractionAffordance.Unavailable || !_prompt.Caption.Contains("pack full"))
                {
                    return $"prompt '{_prompt.Caption}' ({_world.Interactor.CurrentAffordance})";
                }
                return null;
            });
            int stall = 0;
            SandboxSetup.NodeEntry scrap = _world.Setup.nodes.Single(n => n.id == "ScrapNode");
            Do("walk to the Scrap stall", () =>
            {
                stall = Stall();
                Stand(scrap.x, scrap.y + 1f);
                return null;
            });
            Do("press E", () => { Tap(Key.E); return null; });
            Do("the harvest is refused and the stall keeps its unit", () =>
            {
                if (Stall() != stall || Pack(ItemType.Scrap) != 100)
                {
                    return $"stall {stall} -> {Stall()}, pack {Pack(ItemType.Scrap)}";
                }
                _log.Add($"collect: E filled the pack to 100 and left 15; '{_prompt.Caption}' at the stall");
                return null;
            });
        }

        // --- 3. The Crate screen ----------------------------------------------------------------

        private void PlanCrateScreen()
        {
            Do("stand at the depot, the Workbench open", () =>
            {
                Stand(DepotCell.x, DepotCell.y + 1f);
                _workbench.Open();
                return null;
            });
            Do("I is the screen's own business while another is up", () =>
            {
                Tap(Key.I);
                return null;
            });
            Do("the Workbench stayed, alone", () =>
            {
                if (!_workbench.IsOpen || _crate.IsOpen || _world.Screens.OpenCount != 1)
                {
                    return $"workbench={_workbench.IsOpen}, crate={_crate.IsOpen}, {_world.Screens.OpenCount} up";
                }
                _workbench.Close();
                _management.Open();
                return null;
            });
            Do("open the crate over Management, as the coordinator would", () =>
            {
                _crate.Open(_depot);
                return null;
            });
            Do("Management closed for it", () =>
            {
                if (_management.IsOpen || !_crate.IsOpen || _world.Screens.OpenCount != 1)
                {
                    return $"management={_management.IsOpen}, crate={_crate.IsOpen}";
                }
                _crate.Close();
                return null;
            });
            Do("press I at the depot", () => { Tap(Key.I); return null; });
            Do("the Crate screen is up with both panels, alone, the prompt hidden", () =>
            {
                if (!_crate.IsOpen || _crate.Crate != _depot)
                {
                    return "I did not open the crate screen on the depot";
                }
                if (!_crate.CratePanel.IsVisibleInTree() || !_crate.PackPanel.IsVisibleInTree())
                {
                    return "a panel is hidden";
                }
                if (_workbench.IsOpen || _management.IsOpen || _world.Screens.OpenCount != 1)
                {
                    return $"{_world.Screens.OpenCount} screens up";
                }
                if (_prompt.Caption != "" || _hud.Showing)
                {
                    return $"prompt '{_prompt.Caption}', HUD={_hud.Showing}";
                }
                if (_crate.CrateTitle != "Crate (-4,6)")
                {
                    return $"titled '{_crate.CrateTitle}'";
                }
                _log.Add($"crate screen: '{_crate.CrateTitle}' beside the pack, alone, prompt hidden");
                return null;
            });
        }

        // --- 4. Put and take --------------------------------------------------------------------

        private void PlanPutAndTake()
        {
            Do("30 Coke in the pack, none in the crate, room for Scrap", () =>
            {
                SetPack(ItemType.Coke, 30);
                SetPack(ItemType.Scrap, 50);
                Snapshot();
                return null;
            });
            Do("Shift-click Coke in the pack", () => Click(_crate.PackRows, ItemType.Coke, shift: true));
            Do("all 30 went in", () => Moved(ItemType.Coke, crate: 30, pack: 0));
            Do("30 more in the pack, the crate at 48", () =>
            {
                Give(ItemType.Coke, 30);
                _depot.Buffer.Deposit(ItemType.Coke, 18);
                Snapshot();
                return null;
            });
            Do("Shift-click Coke in the pack", () => Click(_crate.PackRows, ItemType.Coke, shift: true));
            string full = null;
            Do("2 went in, and the status says the crate is full", () =>
            {
                full = _crate.Status;
                return Moved(ItemType.Coke, crate: 50, pack: 28) ?? (full.Contains("Crate full of Coke") ? null : $"status '{full}'");
            });
            Do("click Coke in the crate", () => Click(_crate.CrateRows, ItemType.Coke));
            Do("exactly one came out", () =>
            {
                string failure = Moved(ItemType.Coke, crate: 49, pack: 29);
                if (failure == null)
                {
                    Shot("crate screen after put and take");
                }
                return failure;
            });
            Do("Shift-click Scrap in the crate", () => Click(_crate.CrateRows, ItemType.Scrap, shift: true));
            Do("it came back to the pack", () => Moved(ItemType.Scrap, crate: 0, pack: 65));
            Do("put and take", () => { _log.Add($"put 30, then 2 ('{full}'), a click took 1, Scrap came back; totals held"); return null; });
        }

        // --- 5. Label ---------------------------------------------------------------------------

        private void PlanLabel()
        {
            Do("Scrap inside, Copper Ore known to the ledger", () =>
            {
                _world.TechTree.Ledger.RecordItem(ItemType.CopperOre);
                _depot.Buffer.Deposit(ItemType.Scrap, 5);
                return null;
            });
            Do("click the label button", () => { Click(_crate.LabelButton); return null; });
            Do("the picker lists Any first, then only goods the ledger has seen", () =>
            {
                if (!_crate.PickerOpen || _crate.LabelButtonText != "Choose a label")
                {
                    return $"picker open={_crate.PickerOpen}, button '{_crate.LabelButtonText}'";
                }
                IReadOnlyList<(string good, Button button)> options = _crate.PickerOptions;
                if (options.Count < 2 || options[0].good != null || options[0].button.FindChild("Any", true, false) == null)
                {
                    return "Any is not first";
                }
                string unseen = options.Skip(1).Select(o => o.good).FirstOrDefault(g => !_world.TechTree.Ledger.HasItem(g));
                if (unseen != null || !options.Any(o => o.good == ItemType.CopperOre))
                {
                    return unseen != null ? $"offers {unseen}, never made" : "Copper Ore missing";
                }
                Shot("label picker");
                return null;
            });
            Do("press Escape", () => { Tap(Key.Escape); return null; });
            Do("Escape closed the picker, not the screen", () =>
                !_crate.PickerOpen && _crate.IsOpen ? null : $"picker={_crate.PickerOpen}, screen={_crate.IsOpen}");
            Do("click the label button", () => { Click(_crate.LabelButton); return null; });
            Do("click Copper Ore", () => { Click(_crate.PickerOptions.Single(o => o.good == ItemType.CopperOre).button); return null; });
            Do("the depot and its tile are labelled", () =>
            {
                if (_depot.FilterItemType != ItemType.CopperOre || _crate.PickerOpen)
                {
                    return $"label {_depot.FilterItemType}, picker open={_crate.PickerOpen}";
                }
                if (!_world.Endpoints.TryGetEndpoint(DepotCell, out IItemEndpoint endpoint) || endpoint is not FilteredBufferEndpoint)
                {
                    return "the tile still publishes an unlabelled crate";
                }
                return _crate.Status == "Label: Copper Ore" && _crate.LabelButtonText == "Label: Copper Ore" ? null : $"status '{_crate.Status}'";
            });
            Do("click Scrap in the pack", () => { Snapshot(); return Click(_crate.PackRows, ItemType.Scrap); });
            Do("refused for the label, nothing moved", () =>
                Moved(ItemType.Scrap, crate: 5, pack: 65) ?? (_crate.Status == "This crate is for Copper Ore" ? null : $"status '{_crate.Status}'"));
            Do("Shift-click Scrap in the crate", () => Click(_crate.CrateRows, ItemType.Scrap, shift: true));
            Do("the Scrap inside still comes out", () =>
            {
                string failure = Moved(ItemType.Scrap, crate: 0, pack: 70);
                if (failure == null)
                {
                    _log.Add("label: picker offered Any then the ledger's goods; Copper Ore relabelled the tile; Scrap refused in, taken out");
                }
                return failure;
            });
        }

        // --- 6. Take all ------------------------------------------------------------------------

        private void PlanTakeAll()
        {
            Do("click Take all", () => { Snapshot(); Click(_crate.TakeAllButton); return null; });
            Do("everything that fits came out, as E would", () =>
            {
                string failure = Moved(ItemType.Coke, crate: 0, pack: 78);
                if (failure == null)
                {
                    _log.Add($"take all: '{_crate.Status}'");
                }
                return failure;
            });
        }

        // --- 7. Above the cap ---------------------------------------------------------------------

        private void PlanAboveTheCap()
        {
            Do("label the crate back to any goods", () => { Click(_crate.LabelButton); return null; });
            Do("click Any", () => { Click(_crate.PickerOptions[0].button); return null; });
            Do("a pack restored at 140 Scrap", () =>
            {
                if (_depot.FilterItemType != null)
                {
                    return "Any did not clear the label";
                }
                _world.Wallet.Pack.RestoreQuantity(ItemType.Scrap, 140);
                Snapshot();
                return null;
            });
            Do("its row reads 140/100; click it", () =>
            {
                string text = (_crate.PackRows.TryGetValue(ItemType.Scrap, out Button row) ? row.FindChild("Quantity", true, false) as Label : null)?.Text;
                return text != "140/100" ? $"row reads '{text}'" : Click(_crate.PackRows, ItemType.Scrap);
            });
            Do("one went into the crate", () => Moved(ItemType.Scrap, crate: 1, pack: 139));
            Do("click Scrap in the crate", () => Click(_crate.CrateRows, ItemType.Scrap));
            Do("refused: the pack is over its cap", () =>
                Moved(ItemType.Scrap, crate: 1, pack: 139) ?? (_crate.Status == "Pack full of Scrap" ? null : $"status '{_crate.Status}'"));
            Do("spend the pack under 100", () =>
            {
                _world.Wallet.TryCharge(new[] { new RecipeIngredient(ItemType.Scrap, 40) });
                Snapshot();
                return null;
            });
            Do("Shift-click Scrap in the crate", () => Click(_crate.CrateRows, ItemType.Scrap, shift: true));
            Do("now it comes back", () =>
            {
                string failure = Moved(ItemType.Scrap, crate: 0, pack: 100);
                if (failure == null)
                {
                    _log.Add("above the cap: 140/100 gave one, took none back until under 100");
                }
                return failure;
            });
        }

        // --- 10. The guide's dock ---------------------------------------------------------------

        private void PlanGuideDock()
        {
            Do("the guide on smelt-feed, a step done in the crate screen", () =>
            {
                _world.Tutorial.Restore("smelt-feed", 0, false);
                return null;
            });
            Do("the guide's plate clears both panels", () =>
            {
                if (_world.Tutorial.CurrentStepId != "smelt-feed" || !_world.Tutorial.Current.Crate)
                {
                    return $"on '{_world.Tutorial.CurrentStepId}'";
                }
                if (!_guide.Plate.IsVisibleInTree())
                {
                    return "the guide stepped aside over the crate screen";
                }
                Rect2 plate = _guide.Plate.GetGlobalRect();
                if (plate.Intersects(_crate.CratePanel.GetGlobalRect()) || plate.Intersects(_crate.PackPanel.GetGlobalRect()))
                {
                    return $"plate {plate} overlaps a panel ({_crate.CratePanel.GetGlobalRect()}, {_crate.PackPanel.GetGlobalRect()})";
                }
                Shot("crate screen with the guide");
                _log.Add($"guide: on smelt-feed its plate {plate.Position}+{plate.Size} clears both panels");
                return null;
            });
            Do("press Escape", () => { Tap(Key.Escape); return null; });
            Do("closed", () => _crate.IsOpen ? "Escape left the crate screen open" : null);
        }

        // --- 8. Mast and Post -------------------------------------------------------------------

        private void PlanMast()
        {
            Do("a Freight Mast", () =>
            {
                Give(ItemType.Brass, 20);
                Give(ItemType.Casing, 10);
                // The Mast's 120 Iron Plate (tuning-data-spec §8.1) is more than a base pack
                // holds: the rest waits in the Post, which the wallet spends once the pack is out.
                int plate = _world.Buffers.Deposit(_world.StockpileBufferId, ItemType.IronPlate, 120);
                _world.Buffers.Deposit(SandboxWorld.PostBufferId, ItemType.IronPlate, 120 - plate);
                _world.Build.SetActivePrefab(_world.Placeables.Single(p => p.Key == "FreightMastPrefab").Prefab);
                _world.Build.PlaceOrRemove(MastCell);
                _world.Build.CancelPlacement();
                if (_world.Build.Buildings.SingleOrDefault(b => b.Cell == MastCell && !b.IsRemoved)?.GetPart<ICrate>() == null)
                {
                    return "no mast placed: " + _world.Build.LastStatusMessage;
                }
                Stand(MastCell.x, MastCell.y + 1f);
                return null;
            });
            Do("press I at the mast", () => { Tap(Key.I); return null; });
            Do("its label button is disabled", () =>
            {
                if (!_crate.IsOpen || _crate.CrateTitle != "Freight Mast (-6,6)")
                {
                    return $"open={_crate.IsOpen}, titled '{_crate.CrateTitle}'";
                }
                if (!_crate.LabelButton.Disabled || _crate.LabelButtonText != "A mast's crate takes no label")
                {
                    return $"label button '{_crate.LabelButtonText}', disabled={_crate.LabelButton.Disabled}";
                }
                Shot("crate screen at a mast");
                _log.Add("mast: titled and unlabellable");
                return null;
            });
            Do("press E", () => { Tap(Key.E); return null; });
            Do("E closed it", () => _crate.IsOpen ? "E left it open" : null);
        }

        private void PlanPost()
        {
            ICrate other = null;
            Do("two Pneumatic Post crates, 30 Coke in the pack", () =>
            {
                foreach (CoreVector2Int cell in new[] { PostCell, OtherPostCell })
                {
                    Give(ItemType.Brass, 10);
                    Give(ItemType.Casing, 5);
                    Give(ItemType.IronPlate, 40);
                    _world.Build.SetActivePrefab(_world.Placeables.Single(p => p.Key == "PneumaticPostPrefab").Prefab);
                    _world.Build.PlaceOrRemove(cell);
                    _world.Build.CancelPlacement();
                    if (_world.Build.Buildings.SingleOrDefault(b => b.Cell == cell && !b.IsRemoved)?.GetPart<ICrate>() == null)
                    {
                        return $"no Post crate placed at {cell}: " + _world.Build.LastStatusMessage;
                    }
                }
                other = _world.Build.Buildings.Single(b => b.Cell == OtherPostCell && !b.IsRemoved).GetPart<ICrate>();
                _world.Buffers.Deposit(SandboxWorld.PostBufferId, ItemType.Scrap, 40);
                SetPack(ItemType.Coke, 30);
                Stand(PostCell.x, PostCell.y + 1f);
                return null;
            });
            Do("press I at the first", () => { Tap(Key.I); return null; });
            Do("the left panel is the Pneumatic Post", () =>
            {
                if (!_crate.IsOpen || _crate.CrateTitle != "Pneumatic Post")
                {
                    return $"open={_crate.IsOpen}, titled '{_crate.CrateTitle}'";
                }
                return _crate.CrateRows.ContainsKey(ItemType.Scrap) ? null : "the store's Scrap has no row";
            });
            Do("Shift-click Coke in the pack", () => Click(_crate.PackRows, ItemType.Coke, shift: true));
            Do("all 30 moved into the shared store", () =>
            {
                int post = _world.Buffers.GetQuantity(SandboxWorld.PostBufferId, ItemType.Coke);
                if (post != 30 || Pack(ItemType.Coke) != 0)
                {
                    return $"Post {post}, pack {Pack(ItemType.Coke)} ({_crate.Status})";
                }
                if (other.Buffer.GetQuantity(ItemType.Coke) != 30)
                {
                    return $"the other Post crate holds {other.Buffer.GetQuantity(ItemType.Coke)} Coke";
                }
                Shot("crate screen at a Post crate");
                return null;
            });
            Do("press Escape", () => { Tap(Key.Escape); return null; });
            Do("walk away from every crate", () =>
            {
                if (_crate.IsOpen)
                {
                    return "Escape left it open";
                }
                Stand(-9f, 9f);
                return null;
            });
            Do("press I", () => { Tap(Key.I); return null; });
            Do("the Pack screen lists the store under the pack, read-only", () =>
            {
                if (!_pack.IsOpen)
                {
                    return "the Pack screen did not open";
                }
                if (!_pack.PostRows.ContainsKey(ItemType.Coke) || !_pack.PostRows.ContainsKey(ItemType.Scrap))
                {
                    return $"Post rows: {string.Join(", ", _pack.PostRows.Keys)}";
                }
                if (_pack.PostRows[ItemType.Coke].MouseFilter != Control.MouseFilterEnum.Ignore)
                {
                    return "a Post row takes clicks";
                }
                string text = (_pack.PostRows[ItemType.Coke].FindChild("Quantity", true, false) as Label)?.Text;
                if (text != "30/200")
                {
                    return $"the store's Coke reads '{text}'";
                }
                Shot("pack screen with the Post");
                _log.Add("post: titled Pneumatic Post, 30 Coke into the store seen at the other crate, listed under the pack");
                return null;
            });
            Do("press I again", () => { Tap(Key.I); return null; });
            Do("closed", () => _pack.IsOpen ? "I left the Pack screen open" : null);
        }

        // --- 9. Demolition ----------------------------------------------------------------------

        private void PlanDemolition()
        {
            Do("Scrap in the crate, the pack full of Scrap", () =>
            {
                _depot.Buffer.Deposit(ItemType.Scrap, 10);
                SetPack(ItemType.Scrap, 100);
                // The Post has room for it, and that changes nothing: a refund is the pack's
                // alone (crates-design §0.8).
                Stand(DepotCell.x, DepotCell.y + 2f);
                return null;
            });
            Do("press X", () => { Push(Key.X); return null; });
            Do("click the crate", () => _world.Build.IsDemolishActive ? ClickCell(DepotCell) : "X did not pick up the wrecking bar");
            Do("refused, naming the pack", () =>
            {
                if (_world.Build.Buildings.All(b => b.Cell != DepotCell || b.IsRemoved))
                {
                    return "the crate was demolished into a full pack";
                }
                if (_world.Buffers.GetQuantity(SandboxWorld.PostBufferId, ItemType.Scrap) != 40)
                {
                    return $"the Post's Scrap moved to {_world.Buffers.GetQuantity(SandboxWorld.PostBufferId, ItemType.Scrap)}";
                }
                return _world.Build.LastStatusMessage.Contains("pack full of Scrap") ? null : $"status '{_world.Build.LastStatusMessage}'";
            });
            Dictionary<string, int> before = null;
            List<RecipeIngredient> cost = null;
            Dictionary<string, int> held = null;
            Do("empty the pack's Scrap and click again", () =>
            {
                SetPack(ItemType.Scrap, 0);
                PlaceableBuilding depot = _world.Build.Buildings.Single(b => b.Cell == DepotCell && !b.IsRemoved);
                cost = depot.Cost.ToList();
                held = _depot.Buffer.Quantities.Where(q => q.Value > 0).ToDictionary(q => q.Key, q => q.Value);
                before = _world.Wallet.Pack.Quantities.ToDictionary(q => q.Key, q => q.Value);
                return ClickCell(DepotCell);
            });
            Do("demolished, cost and contents refunded exactly", () =>
            {
                if (_world.Build.Buildings.Any(b => b.Cell == DepotCell && !b.IsRemoved))
                {
                    return "still standing: " + _world.Build.LastStatusMessage;
                }
                var expected = new Dictionary<string, int>(before);
                foreach (RecipeIngredient good in cost)
                {
                    expected[good.itemType] = expected.GetValueOrDefault(good.itemType) + good.quantity;
                }
                foreach (KeyValuePair<string, int> good in held)
                {
                    expected[good.Key] = expected.GetValueOrDefault(good.Key) + good.Value;
                }
                string drift = string.Join(", ", expected.Keys.Union(_world.Wallet.Pack.Quantities.Keys)
                    .Where(g => expected.GetValueOrDefault(g) != Pack(g)).Select(g => $"{g} {expected.GetValueOrDefault(g)} != {Pack(g)}"));
                if (drift.Length > 0)
                {
                    return drift;
                }
                _log.Add($"demolition: refused into a full pack, then refunded {string.Join(", ", cost.Select(c => c.quantity + " " + c.itemType))} and {string.Join(", ", held.Select(h => h.Value + " " + h.Key))}");
                return null;
            });
            Do("put the wrecking bar down", () => { Push(Key.Escape); return null; });
        }

        // --- Helpers ----------------------------------------------------------------------------

        private int Pack(string good) => _world.Wallet.Pack.GetQuantity(good);

        private void Give(string good, int quantity) => _world.Buffers.Deposit(_world.StockpileBufferId, good, quantity);

        private void SetPack(string good, int quantity) => _world.Wallet.Pack.RestoreQuantity(good, quantity);

        private int Stall() => _world.Nodes.TryGetNode("ScrapNode", out ResourceNode node) ? node.RemainingQuantity : -1;

        private void Snapshot() =>
            _totals = _depot.Buffer.Quantities.Keys.Union(_world.Wallet.Pack.Quantities.Keys)
                .ToDictionary(g => g, g => Pack(g) + _depot.Buffer.GetQuantity(g));

        /// <summary>The crate and pack hold what they should, and no good's total changed.</summary>
        private string Moved(string good, int crate, int pack)
        {
            foreach (KeyValuePair<string, int> total in _totals)
            {
                int now = Pack(total.Key) + _depot.Buffer.GetQuantity(total.Key);
                if (now != total.Value)
                {
                    return $"{total.Key}: crate + pack was {total.Value}, now {now}";
                }
            }
            int inCrate = _depot.Buffer.GetQuantity(good);
            return inCrate == crate && Pack(good) == pack ? null : $"{good}: crate {inCrate}, pack {Pack(good)}; expected {crate} and {pack} ({_crate.Status})";
        }

        /// <summary>A frame worth looking at, for a --write-movie run.</summary>
        private void Shot(string what) => _log.Add($"frame {Engine.GetFramesDrawn()}: {what}");

        private void Do(string name, Func<string> step) => _steps.Enqueue((name, step));

        private void Stand(float x, float y) => _player.TeleportTo(new CoreVector3(x, y, 0f));

        // A tap holds the key across frames, unflushed, so "just pressed" reads as a real one.
        private void Tap(Key key)
        {
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true });
            _heldTap = key;
        }

        private string Click(IReadOnlyDictionary<string, Button> rows, string good, bool shift = false)
        {
            if (!rows.TryGetValue(good, out Button row))
            {
                return $"no {good} row";
            }
            Click(row, shift);
            return null;
        }

        private void Click(Control control, bool shift = false)
        {
            Vector2 at = control.GetGlobalRect().GetCenter();
            _viewport.WarpMouse(at);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at, ShiftPressed = shift });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at, ShiftPressed = shift });
        }

        // The world's tools read events as BuildScenario sends them: flushed, press and release.
        private string ClickCell(CoreVector2Int cell)
        {
            Vector2 at = _viewport.GetCanvasTransform() * GridConversions.CellToWorld(cell);
            foreach (InputEvent e in new InputEvent[]
            {
                new InputEventMouseMotion { Position = at, GlobalPosition = at },
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at, GlobalPosition = at },
                new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at, GlobalPosition = at },
            })
            {
                _viewport.WarpMouse(at);
                Input.ParseInputEvent(e);
                Input.FlushBufferedEvents();
            }
            return null;
        }

        private void Push(Key key)
        {
            foreach (bool down in new[] { true, false })
            {
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = down });
                Input.FlushBufferedEvents();
            }
        }
    }
}
