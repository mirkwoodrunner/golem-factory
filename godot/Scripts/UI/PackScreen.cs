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
        public const string Hint = "Stand at a crate and press I to fill it from here.";

        private SandboxWorld _world;
        private Control _root;
        private InventoryPanel _panel;
        private Label _summary;

        public bool IsOpen { get; private set; }

        // --- For scenarios ------------------------------------------------------------------
        public Control Panel => _panel.Root;
        public System.Collections.Generic.IReadOnlyDictionary<string, Button> Rows => _panel.Rows;

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
        }

        public void Close()
        {
            IsOpen = false;
            _root.Visible = false;
        }

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
            }
        }

        private void Refresh()
        {
            _panel.Render(_world.Inventory.PackRows(null));
            _summary.Text = _world.Inventory.PackSummary;
        }

        private void Build()
        {
            _root = Ugui.Fill(new Control { Name = "PackScreen", MouseFilter = Control.MouseFilterEnum.Stop });
            AddChild(_root);
            _root.AddChild(Ugui.Fill(Ugui.Rect("Backdrop", new Color(0.055f, 0.045f, 0.038f, 0.80f))));

            _panel = new InventoryPanel("PackPanel", 64f, Close, null);
            _root.AddChild(Ugui.Place(_panel.Root, 0.5f, 0.5f, 0.5f, 0.5f, 0f, 0f, 380f, 520f, 0.5f, 0.5f));
            _panel.Title.Text = CrateScreenReadout.PackTitle;
            _panel.Cap.Text = _world.Inventory.PackCapLine;
            _summary = Ugui.Place(Ugui.Text("Summary", "", 12, ManagementScreen.DimInk), 0f, 0f, 1f, 0f, 0f, 12f, -24f, 30f, 0.5f, 0f);
            _panel.Root.AddChild(_summary);

            _root.AddChild(Ugui.Place(Ugui.Text("Hint", Hint, 12, ManagementScreen.DimInk, align: 2), 0.5f, 0.5f, 0.5f, 0.5f, 0f, -272f, 380f, 18f, 0.5f, 1f));
        }
    }
}
