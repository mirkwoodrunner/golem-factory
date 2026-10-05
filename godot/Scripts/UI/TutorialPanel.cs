using System.Linq;
using Godot;
using GolemFactory.Tutorial;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// Draws Core's <see cref="TutorialGuide"/> (G10): the current step in a plate under the
    /// Clock Tower panel, a bobbing arrow over the thing to use next, and a pulsing outline on
    /// the build-menu row a step needs. Skip guide puts it away; F1 brings it back.
    ///
    /// <para>
    /// Layer 40 -- above every full screen -- because one step happens INSIDE the Workbench and
    /// a guide that vanished the moment the Workbench opened would drop the player at the one
    /// step with the most to explain. Over any other screen, or on any other step, it steps
    /// aside, and the world arrow always hides under a screen (the world is behind it).
    /// </para>
    /// </summary>
    public partial class TutorialPanel : CanvasLayer
    {
        private static readonly Color Amber = new Color(0.95f, 0.72f, 0.33f, 1f);
        private static readonly Color Ink = new Color(0.93f, 0.89f, 0.80f, 1f);
        private static readonly Color Dim = new Color(0.70f, 0.66f, 0.58f, 1f);
        private const float PlateWidth = 340f;
        private const float PlateHeight = 178f;

        private WorldNode _world;
        private Panel _plate;
        private Label _counter;
        private Label _title;
        private Label _body;
        private Label _progress;
        private Button _skip;
        private Button _finish;
        private TextureRect _arrow;
        private Panel _rowHighlight;
        private BuildMenuNode _menu;
        private WorkbenchScreen _workbench;
        private int _renderedVersion = -1;
        private bool _docked;
        private float _time;

        private TutorialGuide Guide => _world?.Sandbox?.Tutorial;

        /// <summary>For scenarios.</summary>
        public Control Plate => _plate;
        public string TitleText => _title?.Text ?? "";
        public string ProgressText => _progress?.Text ?? "";
        public Button SkipButton => _skip;
        public Button FinishButton => _finish;
        public TextureRect Arrow => _arrow;
        public Control RowHighlight => _rowHighlight;

        public override void _Ready()
        {
            Layer = 40;
            _world = WorldNode.Find(this);

            var root = Ugui.Fill(new Control { Name = "Root", MouseFilter = Control.MouseFilterEnum.Ignore });
            AddChild(root);

            _arrow = new TextureRect
            {
                Name = "Arrow",
                Texture = GD.Load<Texture2D>("res://art/facing_arrow.png"),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Modulate = Amber,
                Visible = false,
                PivotOffset = Vector2.Zero,
            };
            root.AddChild(_arrow);

            var outline = new StyleBoxFlat { DrawCenter = false, BorderColor = Amber };
            outline.SetBorderWidthAll(3);
            outline.SetCornerRadiusAll(3);
            _rowHighlight = new Panel { Name = "RowHighlight", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
            _rowHighlight.AddThemeStyleboxOverride("panel", outline);
            root.AddChild(_rowHighlight);

            _plate = Ugui.Image("GuidePlate", Ugui.NineSlice("res://art/UI/Steampunk/steampunk_panel_iron_bolt.png", 12, 10, 12, 10), new Color(0.10f, 0.08f, 0.06f, 0.95f));
            _plate.MouseFilter = Control.MouseFilterEnum.Ignore; // only its buttons take clicks
            root.AddChild(_plate);

            var column = Ugui.Fill(new VBoxContainer { Name = "Column", MouseFilter = Control.MouseFilterEnum.Ignore });
            column.OffsetLeft = 14f;
            column.OffsetRight = -14f;
            column.OffsetTop = 10f;
            column.OffsetBottom = -8f;
            column.AddThemeConstantOverride("separation", 3);
            _plate.AddChild(column);

            _counter = Ugui.Text("Counter", "", 12, Dim);
            _title = Ugui.Text("Title", "", 17, Amber, bold: true);
            _body = Ugui.Text("Body", "", 13, Ink);
            _body.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _body.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            _body.VerticalAlignment = VerticalAlignment.Top;
            _progress = Ugui.Text("Progress", "", 14, Amber, bold: true);
            column.AddChild(_counter);
            column.AddChild(_title);
            column.AddChild(_body);

            var footer = new HBoxContainer { Name = "Footer", MouseFilter = Control.MouseFilterEnum.Ignore };
            footer.AddChild(_progress);
            _progress.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _skip = FooterButton("Skip", "Skip guide");
            _skip.Pressed += () => Guide?.Dismiss();
            _finish = FooterButton("Finish", "Finish");
            _finish.Pressed += () => Guide?.Finish();
            footer.AddChild(_finish);
            footer.AddChild(_skip);
            column.AddChild(footer);

            Dock(false);
        }

        private static Button FooterButton(string name, string text)
        {
            var button = new Button { Name = name, Flat = true, FocusMode = Control.FocusModeEnum.None };
            button.AddChild(Ugui.Fill(Ugui.Text("Label", text, 12, Dim, align: 4)));
            button.CustomMinimumSize = new Vector2(text.Length * 7f + 8f, 18f);
            return button;
        }

        /// <summary>
        /// Top right under the Clock Tower panel -- or, during the step that happens in the
        /// Workbench, in the Blueprint Viewport's empty lower half, under the step sockets and
        /// clear of the lever, the vault and the tape (docked bottom right, it covered the
        /// ENGAGE lever, caught in the frames).
        /// </summary>
        private void Dock(bool inWorkbench)
        {
            _docked = inWorkbench;
            if (inWorkbench)
            {
                Ugui.Place(_plate, 0f, 1f, 0f, 1f, 268f, -377f, PlateWidth, PlateHeight, 0f, 1f);
            }
            else
            {
                Ugui.Place(_plate, 1f, 1f, 1f, 1f, -12f, -168f, PlateWidth, PlateHeight, 1f, 1f);
            }
        }

        /// <summary>The step that is done inside the Workbench, and so is shown over it.</summary>
        private const string WorkbenchStepId = "program";

        public override void _UnhandledInput(InputEvent e)
        {
            if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F1 } && Guide != null)
            {
                if (Guide.IsShowing)
                {
                    Guide.Dismiss();
                }
                else
                {
                    Guide.Reopen();
                }
                GetViewport().SetInputAsHandled();
            }
        }

        public override void _Process(double delta)
        {
            _time += (float)delta;
            TutorialGuide guide = Guide;
            bool screenOpen = ModalScreens.AnyOpen(GetTree());
            _workbench ??= GetTree().Root.FindChild("Workbench", true, false) as WorkbenchScreen;
            bool inWorkbench = screenOpen && _workbench != null && _workbench.IsOpen && guide?.Current?.Id == WorkbenchStepId;

            // Over a full screen the guide shows only for the step that happens there; every other
            // step is about the world behind the screen, and the screens have no room to spare.
            bool showing = guide != null && guide.IsShowing && (!screenOpen || inWorkbench);
            _plate.Visible = showing;
            if (!showing)
            {
                _arrow.Visible = false;
                _rowHighlight.Visible = false;
                return;
            }

            if (inWorkbench != _docked)
            {
                Dock(inWorkbench);
            }

            if (guide.Version != _renderedVersion)
            {
                _renderedVersion = guide.Version;
                TutorialStep step = guide.Current;
                _counter.Text = $"GUIDE  ·  step {guide.Index + 1} of {guide.StepCount}  ·  F1 hides";
                _title.Text = step.Title;
                _body.Text = step.Body;
                bool last = guide.Index == guide.StepCount - 1;
                _finish.Visible = last;
                _skip.Visible = !last;
            }
            _progress.Text = guide.Progress;

            PointArrow(guide, screenOpen);
            HighlightRow(guide, screenOpen);
        }

        private void PointArrow(TutorialGuide guide, bool screenOpen)
        {
            Compat.Vector2Int? cell = guide.TargetCell;
            if (cell == null || screenOpen)
            {
                _arrow.Visible = false;
                return;
            }

            _arrow.Size = _arrow.Texture.GetSize();
            _arrow.Scale = Vector2.One * 1.5f;
            _arrow.PivotOffset = _arrow.Size / 2f;

            // The target's own screen point (the middle of its cell, where a standing sprite's
            // body is), and the screen we have to show it on.
            Vector2 target = GetViewport().GetCanvasTransform() *
                (GridConversions.CellToWorld(cell.Value) - new Vector2(0f, GridConversions.CellPixels * 0.5f));
            Rect2 screen = GetViewport().GetVisibleRect();
            float bob = Mathf.Sin(_time * 4f) * 6f;

            // Where the arrow may stand: inside the screen, clear of the HUD's top strip and its
            // bottom control bar (an edge-pinned arrow pointing at the street sat on PAUSE).
            Rect2 field = new Rect2(
                screen.Position + new Vector2(EdgeMargin, TopMargin),
                screen.Size - new Vector2(EdgeMargin * 2f, TopMargin + BottomMargin));

            if (field.HasPoint(target))
            {
                // On screen: above the target, pointing down at it, bobbing so it reads as a
                // pointer, not a prop.
                _arrow.Rotation = GridConversions.FacingToRotation(World.Facing.South);
                _arrow.Position = target - new Vector2(0f, GridConversions.CellPixels * 0.8f + bob) - _arrow.Size / 2f;
                OffScreen = false;
            }
            else
            {
                // Off screen -- the market street from the workshop, usually: pinned to the edge
                // of the screen on the line toward the target, pointing at it, bobbing along
                // that line. An arrow over a stall sixteen cells away is no arrow at all.
                Vector2 centre = field.GetCenter();
                Vector2 toward = (target - centre).Normalized();
                Vector2 half = field.Size / 2f;
                float reach = Mathf.Min(
                    Mathf.Abs(toward.X) > 1e-4f ? half.X / Mathf.Abs(toward.X) : float.MaxValue,
                    Mathf.Abs(toward.Y) > 1e-4f ? half.Y / Mathf.Abs(toward.Y) : float.MaxValue);
                _arrow.Rotation = toward.Angle();
                _arrow.Position = centre + toward * (reach + bob) - _arrow.Size / 2f;
                OffScreen = true;
            }
            _arrow.Visible = true;
        }

        private const float EdgeMargin = 40f;
        private const float TopMargin = 60f;
        private const float BottomMargin = 90f;

        /// <summary>Whether the arrow is pinned to the screen edge (target off screen). For scenarios.</summary>
        public bool OffScreen { get; private set; }

        private void HighlightRow(TutorialGuide guide, bool screenOpen)
        {
            string key = guide.Current?.MenuKey;
            _menu ??= GetTree().Root.FindChild("BuildMenu", true, false) as BuildMenuNode;
            Button row = key == null || _menu == null || screenOpen || !_menu.IsBodyVisible
                ? null
                : _menu.Rows.FirstOrDefault(r => r.prefab.name == key).row;
            if (row == null)
            {
                _rowHighlight.Visible = false;
                return;
            }

            Rect2 rect = row.GetGlobalRect().Grow(3f);
            _rowHighlight.Position = rect.Position;
            _rowHighlight.Size = rect.Size;
            _rowHighlight.Modulate = new Color(1f, 1f, 1f, 0.55f + 0.45f * Mathf.Sin(_time * 5f));
            _rowHighlight.Visible = true;
        }
    }
}
