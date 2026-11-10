using GolemFactory.Settings;
using Godot;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The Pack screen ([I] away from a crate, docs/inventory-screens-design.md): what the player
    /// carries, one row a good against the pack's cap. Nothing moves from here; goods move at a
    /// crate, on the Crate screen.
    /// </summary>
    public partial class PackScreen : CanvasLayer, IPackScreen, IClosableScreen
    {
        private Label _hint;

        /// <summary>The hint as drawn.</summary>
        public string HintText => _hint?.Text ?? "";

        public static string Hint => $"Stand at a crate and press {KeyNames.Inventory} to fill it from here.";

        /// <summary>The hint with the pad in use: Y opens a crate as I does.</summary>
        public const string PadHint = "Stand at a crate and press Y to fill it from here.";

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
            _world.Screens.Register(this);
            _world.ConfigureInventoryScreens(this, null);
        }

        public void Open()
        {
            _world.Screens.Opening(this);
            IsOpen = true;
            _panel.Invalidate();
            _root.Visible = true;
            Refresh();
            _pad.Reset();
            _padOpenedFrame = Engine.GetProcessFrames();
        }

        public void Close()
        {
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
        // Nothing moves from here, so the ring only walks the rows, scrolling a long pack; B, Y
        // or A closes, as Escape, I or E does.

        public const string StopRow = "Row";

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
            if (!opening && (Input.IsActionJustPressed(BuildCursorNode.CancelAction) || Input.IsActionJustPressed(PlayerNode.Inventory)
                             || Input.IsActionJustPressed(PlayerNode.Interact)))
            {
                Close();
                return;
            }
            var stops = new System.Collections.Generic.List<PadFocus.Stop>();
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

        private void Refresh()
        {
            _panel.Render(_world.Inventory.PackRows(null), _world.Inventory.PostRows());
            _panel.Cap.Text = _world.Inventory.PackCapLine; // live: a pack upgrade raises it
            _summary.Text = _world.Inventory.PackSummary;
            _hint.Text = InputDevices.PadInUse ? PadHint : Hint; // the Pack key can be rebound (Settings)
        }

        private void Build()
        {
            _root = Ugui.Fill(new Control { Name = "PackScreen", MouseFilter = Control.MouseFilterEnum.Stop });
            AddChild(_root);
            _root.AddChild(Ugui.Fill(Ugui.Rect("Backdrop", new Color(0.055f, 0.045f, 0.038f, 0.80f))));

            _panel = new InventoryPanel("PackPanel", 64f, Close, null);
            _root.AddChild(Ugui.Place(_panel.Root, 0.5f, 0.5f, 0.5f, 0.5f, 0f, 0f, 380f, 520f, 0.5f, 0.5f));
            _panel.Title.Text = CrateScreenReadout.PackTitle;
            _summary = Ugui.Place(Ugui.Text("Summary", "", 12, ManagementScreen.DimInk), 0f, 0f, 1f, 0f, 0f, 12f, -24f, 30f, 0.5f, 0f);
            _panel.Root.AddChild(_summary);

            _root.AddChild(Ugui.Place(_hint = Ugui.Text("Hint", Hint, 12, ManagementScreen.DimInk, align: 2), 0.5f, 0.5f, 0.5f, 0.5f, 0f, -272f, 380f, 18f, 0.5f, 1f));
            _pad = new PadFocus(_root);
        }
    }
}
