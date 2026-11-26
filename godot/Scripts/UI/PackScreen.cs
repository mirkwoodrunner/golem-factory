using GolemFactory.Settings;
using Godot;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The Pack screen ([I] away from a crate, docs/inventory-screens-design.md): what the player
    /// carries, one row a good against the pack's cap. Goods move at a crate, on the Crate screen;
    /// from here they can only be thrown away (Discard, 2026-11-26).
    /// </summary>
    public partial class PackScreen : CanvasLayer, IPackScreen, IClosableScreen
    {
        private Label _hint;

        /// <summary>The hint as drawn.</summary>
        public string HintText => _hint?.Text ?? "";

        public static string Hint => $"Stand at a crate and press {KeyNames.Inventory} to fill it from here.";

        /// <summary>The hint with the pad in use: Y opens a crate as I does.</summary>
        public const string PadHint = "Stand at a crate and press Y to fill it from here.";

        /// <summary>The hint while discarding.</summary>
        public const string DiscardHint = "Click a good to throw one away · Shift: all of it";

        /// <summary>The hint while discarding, with the pad in use.</summary>
        public const string DiscardPadHint = "A throws one away · X all of it";

        private Button _discard;
        private Button _discardAll;
        private bool _discarding;
        private bool _discardAllArmed;
        private string _status;

        /// <summary>Whether a row's click throws goods away.</summary>
        public bool Discarding => _discarding;

        public Button DiscardToggle => _discard;
        public Button DiscardAllButton => _discardAll;

        private SandboxWorld _world;
        private Control _root;
        private InventoryPanel _panel;
        private Label _summary;

        public bool IsOpen { get; private set; }

        // --- For scenarios ------------------------------------------------------------------
        public System.Collections.Generic.IReadOnlyDictionary<string, Button> Rows => _panel.Rows;
        public System.Collections.Generic.IReadOnlyDictionary<string, Button> PostRows => _panel.PostRows;
        public string CapText => _panel.Cap.Text;

        public override void _EnterTree() => AddToGroup(ModalScreens.GroupName);

        public override void _Ready()
        {
            Layer = 30;
            _world = WorldNode.Find(this).Sandbox;
            Build();
            _root.Visible = false;
            // The rows built now, while the scene loads: a first open then only adds the goods
            // gathered since (InventoryPanel keeps a good's row), not a row for every good (D8).
            Refresh();
            _world.Screens.Register(this);
            _world.ConfigureInventoryScreens(this, null);
        }

        public void Open()
        {
            _world.Screens.Opening(this);
            IsOpen = true;
            // No forced rebuild: the panel redraws only what changed, in place where it can (D8).
            _root.Visible = true;
            Refresh();
            _pad.Reset();
            _padOpenedFrame = Engine.GetProcessFrames();
        }

        public void Close()
        {
            SetDiscarding(false);
            IsOpen = false;
            _root.Visible = false;
        }

        // The pad's events never reach here while the screen is open (PadProcess reads them).
        public override void _UnhandledInput(InputEvent e)
        {
            if (IsOpen && (e.IsActionPressed(BuildCursorNode.CancelAction) || e.IsActionPressed(PlayerNode.Inventory) || e.IsActionPressed(PlayerNode.Interact)))
            {
                Close();
                GetViewport().SetInputAsHandled();
            }
        }

        public override void _Process(double delta)
        {
            if (IsOpen)
            {
                Refresh();
                PadProcess(delta);
            }
        }

        // --- The pad (docs/steam-deck-design.md D6) --------------------------------------------
        // The ring walks Discard, Discard all (while discarding) and the rows, scrolling a long pack.
        // A presses a button; on a row, A throws one away and X all of it while discarding, and
        // otherwise A closes, as E does. B or Y closes, as Escape or I does.

        public const string StopRow = "Row";
        public const string StopDiscard = "Discard";
        public const string StopDiscardAll = "DiscardAll";

        private PadFocus _pad;
        private ulong _padOpenedFrame;

        public bool PadRingVisible => _pad.RingVisible;
        public string PadFocusKind => _pad.Kind;
        public int PadFocusIndex => _pad.Index;
        public System.Collections.Generic.IReadOnlyList<(string good, Button row)> RowsInOrder => _panel.OrderedRows;
        public ScrollContainer RowsView => _panel.Scroll;

        /// <summary>While the screen is open every pad event is its own, read by polling in <see cref="PadProcess"/>.</summary>
        public override void _Input(InputEvent e)
        {
            if (IsOpen && (e is InputEventJoypadButton || e is InputEventJoypadMotion))
            {
                GetViewport().SetInputAsHandled();
            }
        }

        private void PadProcess(double delta)
        {
            if (!InputDevices.PadInUse)
            {
                _pad.Hide();
                return;
            }
            // The Y that opened the pack is still "just pressed" in the frame it opens.
            bool opening = Engine.GetProcessFrames() <= _padOpenedFrame + 1;
            if (!opening && (Input.IsActionJustPressed(BuildCursorNode.CancelAction) || Input.IsActionJustPressed(PlayerNode.Inventory)))
            {
                Close();
                return;
            }
            if (!opening && PadButtons())
            {
                return;
            }
            var stops = new System.Collections.Generic.List<PadFocus.Stop>();
            stops.Add(new PadFocus.Stop(StopDiscard, 0, _discard));
            if (_discarding)
            {
                stops.Add(new PadFocus.Stop(StopDiscardAll, 0, _discardAll));
            }
            for (int i = 0; i < _panel.OrderedRows.Count; i++)
            {
                Button row = _panel.OrderedRows[i].row;
                if (IsInstanceValid(row) && !row.IsQueuedForDeletion())
                {
                    stops.Add(new PadFocus.Stop(StopRow, i, row, _panel.Scroll));
                }
            }
            _pad.Update(stops, delta);
        }

        // A and X at the ring's stop. Returns true when the screen closed.
        private bool PadButtons()
        {
            bool one = Input.IsActionJustPressed(PlayerNode.Interact);
            bool all = Input.IsActionJustPressed(PlayerNode.Carry);
            if (!_pad.HasFocus)
            {
                if (one)
                {
                    Close();
                }
                return one;
            }
            switch (_pad.Kind)
            {
                case StopDiscard when one:
                    _discard.EmitSignal(BaseButton.SignalName.Pressed);
                    break;
                case StopDiscardAll when one:
                    _discardAll.EmitSignal(BaseButton.SignalName.Pressed);
                    break;
                case StopRow when _discarding && (one || all) && _pad.Index < _panel.OrderedRows.Count:
                    RowClicked(_panel.OrderedRows[_pad.Index].good, all);
                    break;
                case StopRow when one:
                    Close();
                    return true;
            }
            return false;
        }

        private void Refresh()
        {
            _panel.Render(_world.Inventory.PackRows(null), _world.Inventory.PostRows());
            _panel.Cap.Text = _world.Inventory.PackCapLine; // live: a pack upgrade raises it
            _summary.Text = _world.Inventory.PackSummary;
            _hint.Text = _status
                ?? (_discarding ? (InputDevices.PadInUse ? DiscardPadHint : DiscardHint)
                    : InputDevices.PadInUse ? PadHint : Hint); // the Pack key can be rebound (Settings)
            _discardAll.Visible = _discarding;
        }

        // A row's click: nothing unless discarding (goods move at a crate), then one away, or all with Shift.
        private void RowClicked(string good, bool shift)
        {
            if (!_discarding)
            {
                return;
            }
            _discardAllArmed = false;
            _status = _world.Inventory.Discard(good, shift);
        }

        // Two presses: the first asks, the second empties the pack.
        private void DiscardAllPressed()
        {
            _status = _world.Inventory.DiscardAll(_discardAllArmed);
            _discardAllArmed = _status == CrateScreenReadout.DiscardAllConfirm;
        }

        private void SetDiscarding(bool on)
        {
            _discarding = on;
            if (_panel != null)
            {
                _panel.Clickable = on;
            }
            _discardAllArmed = false;
            _status = null;
            if (_discard?.GetNodeOrNull<Label>("Label") is Label label)
            {
                label.Text = on ? CrateScreenReadout.DiscardDone : CrateScreenReadout.DiscardButton;
            }
            if (_discardAll != null)
            {
                _discardAll.Visible = on;
            }
        }

        private void Build()
        {
            _root = Ugui.Fill(new Control { Name = "PackScreen", MouseFilter = Control.MouseFilterEnum.Stop });
            AddChild(_root);
            _root.AddChild(Ugui.Fill(Ugui.Rect("Backdrop", new Color(0.055f, 0.045f, 0.038f, 0.80f))));

            _panel = new InventoryPanel("PackPanel", 64f, Close, RowClicked) { Clickable = false };
            _root.AddChild(Ugui.Place(_panel.Root, 0.5f, 0.5f, 0.5f, 0.5f, 0f, 0f, 380f, 520f, 0.5f, 0.5f));
            _panel.Title.Text = CrateScreenReadout.PackTitle;
            _summary = Ugui.Place(Ugui.Text("Summary", "", 12, ManagementScreen.DimInk), 0f, 0f, 1f, 0f, 0f, 12f, -24f, 30f, 0.5f, 0f);
            _panel.Root.AddChild(_summary);

            // Discard sits at the right of the cap line, under Close; Discard all, at the right of the
            // summary, shows only while discarding.
            _discard = Ugui.Place(ManagementScreen.PlateButton("DiscardButton", ManagementScreen.Ui + "Hud/hud_button.png", Colors.White,
                CrateScreenReadout.DiscardButton, 12, Colors.Black, bold: false), 1f, 1f, 1f, 1f, -10f, -36f, 70f, 22f, 1f, 1f);
            _discard.Pressed += () => SetDiscarding(!_discarding);
            _panel.Root.AddChild(_discard);
            _discardAll = Ugui.Place(ManagementScreen.PlateButton("DiscardAllButton", ManagementScreen.Ui + "Hud/hud_button_close.png", new Color(0.35f, 0.10f, 0.10f),
                CrateScreenReadout.DiscardAllButton, 12, Colors.White, bold: false), 1f, 0f, 1f, 0f, -10f, 12f, 90f, 24f, 1f, 0f);
            _discardAll.Pressed += DiscardAllPressed;
            _discardAll.Visible = false;
            _panel.Root.AddChild(_discardAll);

            _root.AddChild(Ugui.Place(_hint = Ugui.Text("Hint", Hint, 12, ManagementScreen.DimInk, align: 2), 0.5f, 0.5f, 0.5f, 0.5f, 0f, -272f, 380f, 18f, 0.5f, 1f));
            _pad = new PadFocus(_root);
            // The ring opens on the first good, not on Discard above it.
            _pad.Default = stops => PadFocus.Find(stops, StopRow, 0) ?? PadFocus.Find(stops, StopDiscard, 0);
        }
    }
}
