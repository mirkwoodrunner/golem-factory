using GolemFactory.Settings;
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
        private bool _fitted;
        private Panel _noticePlate;
        private Label _notice;
        private Button _skip;
        private Button _finish;
        private TextureRect _arrow;
        private Panel _rowHighlight;
        private Panel _spot;
        private TextureRect _spotFacing;
        private BuildMenuNode _menu;
        private WorkbenchScreen _workbench;
        private CrateScreen _crateScreen;
        private int _renderedVersion = -1;
        private int _renderedKeys = -1;
        private TutorialStep _renderedStep;
        private bool _docked;
        private float _time;

        private TutorialGuide Guide => _world?.Sandbox?.Tutorial;

        /// <summary>For scenarios.</summary>
        public Control Plate => _plate;
        public string TitleText => _title?.Text ?? "";
        public string BodyText => _body?.Text ?? "";
        public string ProgressText => _progress?.Text ?? "";

        /// <summary>The note under the guide (what is in the way of a marked tile), as drawn.</summary>
        public bool NoticeVisible => _noticePlate != null && _noticePlate.IsVisibleInTree();
        public string NoticeText => _notice?.Text ?? "";
        public Control NoticePlate => _noticePlate;
        public Button SkipButton => _skip;
        public Button FinishButton => _finish;
        public TextureRect Arrow => _arrow;
        public Control RowHighlight => _rowHighlight;
        public Control SpotMarker => _spot;

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

            // "Place it here": a pulsing outline on the floor tile a step wants, with an arrow
            // inside when the step also cares which way it faces. Drawn first, under the arrow.
            var spotStyle = new StyleBoxFlat { BgColor = new Color(Amber, 0.18f), BorderColor = Amber };
            spotStyle.SetBorderWidthAll(3);
            _spot = new Panel { Name = "Spot", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
            _spot.AddThemeStyleboxOverride("panel", spotStyle);
            _spotFacing = new TextureRect
            {
                Name = "Facing",
                Texture = GD.Load<Texture2D>("res://art/facing_arrow.png"),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Modulate = Amber,
            };
            _spot.AddChild(_spotFacing);
            root.AddChild(_spot);
            root.MoveChild(_spot, 0);

            _plate = Ugui.Image("GuidePlate", Ugui.NineSlice("res://art/UI/Hud/hud_panel_bolted.png", 12, 10, 12, 10), new Color(0.10f, 0.08f, 0.06f, 0.95f));
            _plate.MouseFilter = Control.MouseFilterEnum.Ignore; // only its buttons take clicks
            root.AddChild(_plate);

            var column = Ugui.Fill(new VBoxContainer { Name = "Column", MouseFilter = Control.MouseFilterEnum.Ignore });
            column.OffsetLeft = 14f;
            column.OffsetRight = -14f;
            column.OffsetTop = 10f;
            column.OffsetBottom = -8f;
            column.AddThemeConstantOverride("separation", 3);
            _plate.AddChild(column);

            _counter = Wrap(Ugui.Text("Counter", "", 12, Dim)); // the rack's narrower dock wraps it
            _title = Ugui.Text("Title", "", 17, Amber, bold: true);
            _body = Ugui.Text("Body", "", 13, Ink);
            Wrap(_body);
            _body.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            _body.VerticalAlignment = VerticalAlignment.Top;
            // The step's goods wrap too: a long list ended in an ellipsis (the Phase 2 playtest).
            _progress = Wrap(Ugui.Text("Progress", "", 14, Amber, bold: true));
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

            // The note: its own small plate hanging under the guide's, so a sentence about what
            // is in the way never squeezes the step text in a fixed-height plate. It grows to
            // its lines. Only build and golem-tile steps raise one, and none of those is shown
            // over the Workbench, so it never hangs over the lever.
            _noticePlate = Ugui.Image("GuideNotice", Ugui.NineSlice("res://art/UI/Hud/hud_panel_bolted.png", 12, 10, 12, 10), new Color(0.30f, 0.10f, 0.07f, 0.95f));
            _noticePlate.MouseFilter = Control.MouseFilterEnum.Ignore;
            _noticePlate.Visible = false;
            _plate.AddChild(_noticePlate);
            _notice = Wrap(Ugui.Text("Notice", "", 13, Ink));
            _notice.CustomMinimumSize = new Vector2(PlateWidth - 28f, 0f);
            _notice.Position = new Vector2(14f, 8f);
            _noticePlate.AddChild(_notice);

            Dock(false);
        }

        /// <summary>Wraps at words and asks for every line's height; a clipping label asks for none (see PlaytestNode).</summary>
        private static Label Wrap(Label label)
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.ClipText = false;
            label.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
            return label;
        }

        /// <summary>
        /// Grows the plate down to its text, never below <see cref="PlateHeight"/>: a long step or
        /// list of goods used to be cut off. Measured like the notice, each wrapping label at the
        /// width it will get.
        /// </summary>
        private void FitPlate()
        {
            float inner = _plateWidth - 28f;
            float buttons = (_skip.Visible ? _skip.CustomMinimumSize.X + 4f : 0f) + (_finish.Visible ? _finish.CustomMinimumSize.X + 4f : 0f);
            _counter.Size = new Vector2(inner, 0f);
            _body.Size = new Vector2(inner, 0f);
            _progress.Size = new Vector2(inner - buttons, 0f);
            float height = 10f + 8f + 3f * 3f
                + _counter.GetCombinedMinimumSize().Y
                + _title.GetCombinedMinimumSize().Y
                + _body.GetCombinedMinimumSize().Y
                + Mathf.Max(_progress.GetCombinedMinimumSize().Y, 18f);
            _plate.OffsetBottom = _plate.OffsetTop + Mathf.Max(PlateHeight, height);
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
            Rect2 rack = inWorkbench && _workbench?.RackArea != null ? _workbench.RackArea.GetGlobalRect() : default;
            if (rack.Size.X > 0f)
            {
                // In the chassis rack, empty while the Workbench is on a golem (every guide program
                // step's case). The viewport's lower half, where it sat, holds an Aether-Hauler's
                // STEP 4: the glow there went under the plate (frames, 2026-11-08).
                _plateWidth = rack.Size.X;
                Ugui.Place(_plate, 0f, 1f, 0f, 1f, rack.Position.X, -rack.Position.Y, _plateWidth, PlateHeight, 0f, 1f);
            }
            else
            {
                _plateWidth = PlateWidth;
                Ugui.Place(_plate, 1f, 1f, 1f, 1f, -12f, -168f, PlateWidth, PlateHeight, 1f, 1f);
            }
        }

        private float _plateWidth = PlateWidth;

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

            // The playtest kit: playtest mode only, never in a normal game.
            if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F9 } && Guide?.Playtest != null)
            {
                Guide.StartKit();
                GetViewport().SetInputAsHandled();
            }
        }

        public override void _Process(double delta)
        {
            using var perf = NodePerf.Measure("node.tutorial");
            _time += (float)delta;
            TutorialGuide guide = Guide;
            bool screenOpen = ModalScreens.AnyOpen(GetTree());
            _workbench ??= GetTree().Root.FindChild("Workbench", true, false) as WorkbenchScreen;
            bool benchOpen = screenOpen && _workbench != null && _workbench.IsOpen;
            // The station opens the Workbench on a new golem at once, often steps before the step
            // that programs it: show that step's words then, until the golem runs its program.
            TutorialStep ahead = AheadStep(guide, benchOpen);
            bool inWorkbench = benchOpen && (guide?.Current?.Workbench == true || ahead != null);
            // The Crate screen leaves the usual dock clear (inventory-screens-design §4.1), so a
            // step done in it keeps the plate where it always is.
            _crateScreen ??= GetTree().Root.FindChild("CrateScreen", true, false) as CrateScreen;
            bool inCrate = screenOpen && _crateScreen != null && _crateScreen.IsOpen && guide?.Current?.Crate == true;

            // Over a full screen the guide shows only for the step that happens there; every other
            // step is about the world behind the screen, and the screens have no room to spare.
            // A playtest question takes the plate's place while it waits (PlaytestNode); the arrow,
            // markers and outline stay, so the step is still pointed at.
            bool asking = guide?.Playtest?.Current != null && !screenOpen;
            bool showing = guide != null && guide.IsShowing && (!screenOpen || inWorkbench || inCrate);
            _plate.Visible = showing && !asking;
            if (!showing)
            {
                _arrow.Visible = false;
                _rowHighlight.Visible = false;
                _spot.Visible = false;
                HideExtraSpots(0);
                return;
            }

            if (inWorkbench != _docked)
            {
                Dock(inWorkbench);
                _fitted = false;
            }

            TutorialStep step = ahead ?? guide.Current;
            if (guide.Version != _renderedVersion || TextTokens.Version != _renderedKeys || step != _renderedStep)
            {
                _renderedVersion = guide.Version;
                _renderedKeys = TextTokens.Version; // a rebind or the marks setting redraws the step's words
                _renderedStep = step;
                int index = guide.IndexOf(step);
                int chapter = guide.ChapterOf(index);
                _counter.Text = guide.KitRunning
                    ? $"PLAYTEST KIT  ·  fast-forwarding chapter {chapter}..."
                    : ahead != null
                        ? $"CHAPTER {chapter}  ·  {index + 1}/{guide.StepCount}  ·  coming up"
                        : $"CHAPTER {chapter}  ·  {index + 1}/{guide.StepCount}  ·  F1 hides"
                          + (guide.Playtest != null ? "  ·  F9 skips" : "");
                _title.Text = step.Title;
                _body.Text = step.Body + MenuKeyHint(step) + ReportHint(step, guide);
                bool last = index == guide.StepCount - 1;
                _finish.Visible = last;
                _skip.Visible = !last;
                _fitted = false;
            }
            // The current step's goods and notice are about the world behind the Workbench, not
            // the program shown ahead of it.
            string progress = ahead != null ? "" : guide.Progress;
            if (_progress.Text != progress)
            {
                _progress.Text = progress;
                _fitted = false;
            }
            if (!_fitted)
            {
                FitPlate();
                _fitted = true;
            }
            ShowNotice(ahead != null ? null : guide.Notice);

            PointArrow(guide, screenOpen);
            HighlightRow(guide, screenOpen);
            MarkSpot(guide, screenOpen);
        }

        /// <summary>
        /// A later step of this chapter that programs the golem on the open Workbench, while the
        /// current step isn't a Workbench step and the golem doesn't yet run that program; else null.
        /// </summary>
        private TutorialStep AheadStep(TutorialGuide guide, bool benchOpen)
        {
            if (!benchOpen || guide == null || guide.Current == null || guide.Current.Workbench)
            {
                return null;
            }
            Golems.GolemEntity golem = _world?.Sandbox?.Workbench?.TargetGolem;
            TutorialStep step = guide.ProgramStepFor(golem);
            return step != null && step != guide.Current && !step.Program.Matches(golem.Program) ? step : null;
        }

        /// <summary>The step whose words the plate shows: a program step ahead of the guide, over the Workbench.</summary>
        public string ShownStepId => _renderedStep?.Id ?? "";

        private void ShowNotice(string notice)
        {
            bool show = !string.IsNullOrEmpty(notice);
            _noticePlate.Visible = show;
            if (!show)
            {
                return;
            }
            if (_notice.Text != notice)
            {
                _notice.Text = notice;
            }
            _notice.Size = new Vector2(PlateWidth - 28f, 0f);
            float height = _notice.GetCombinedMinimumSize().Y + 16f;
            _noticePlate.Position = new Vector2(0f, _plate.Size.Y + 4f);
            _noticePlate.Size = new Vector2(PlateWidth, height);
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
            // build bar along the bottom (an edge-pinned arrow pointing at the street once sat on PAUSE).
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
        private const float BottomMargin = 100f; // clear of the build bar along the bottom

        /// <summary>Whether the arrow is pinned to the screen edge (target off screen). For scenarios.</summary>
        public bool OffScreen { get; private set; }

        private readonly System.Collections.Generic.List<Panel> _extraSpots = new System.Collections.Generic.List<Panel>();

        /// <summary>For scenarios: every visible marker, the primary first.</summary>
        public System.Collections.Generic.IEnumerable<Control> SpotMarkers =>
            new Control[] { _spot }.Concat(_extraSpots).Where(c => c.Visible);

        private void HideExtraSpots(int fromIndex)
        {
            for (int i = System.Math.Max(0, fromIndex); i < _extraSpots.Count; i++)
            {
                _extraSpots[i].Visible = false;
            }
        }

        /// <summary>
        /// Lays <paramref name="marker"/> over a floor tile: its two corners through the canvas
        /// transform, so it follows the camera's zoom as well as its position. Returns its size.
        /// </summary>
        private Vector2 PlaceOnTile(Control marker, Compat.Vector2Int cell)
        {
            Transform2D canvas = GetViewport().GetCanvasTransform();
            Vector2 centre = GridConversions.CellToWorld(cell); // a floor tile's centre
            Vector2 half = new Vector2(GridConversions.CellPixels, GridConversions.CellPixels) * 0.5f;
            Vector2 topLeft = canvas * (centre - half);
            Vector2 size = canvas * (centre + half) - topLeft;
            marker.Position = topLeft;
            marker.Size = size;
            marker.Modulate = new Color(1f, 1f, 1f, 0.6f + 0.4f * Mathf.Sin(_time * 5f));
            return size;
        }

        private void MarkSpot(TutorialGuide guide, bool screenOpen)
        {
            TutorialStep step = guide.Current;
            if (step?.Spot == null || screenOpen)
            {
                _spot.Visible = false;
                HideExtraSpots(0);
                return;
            }

            Vector2 size = PlaceOnTile(_spot, step.Spot.Value);

            // A step can mark more than one tile (a pipe run): the rest get plain outlines.
            for (int i = 1; i < step.Spots.Count; i++)
            {
                if (_extraSpots.Count < i)
                {
                    var extra = new Panel { Name = "Spot" + i, MouseFilter = Control.MouseFilterEnum.Ignore };
                    extra.AddThemeStyleboxOverride("panel", _spot.GetThemeStylebox("panel"));
                    _spot.GetParent().AddChild(extra);
                    _spot.GetParent().MoveChild(extra, 0);
                    _extraSpots.Add(extra);
                }
                PlaceOnTile(_extraSpots[i - 1], step.Spots[i]);
                _extraSpots[i - 1].Visible = true;
            }
            HideExtraSpots(step.Spots.Count - 1);

            _spotFacing.Visible = step.SpotFacing != null;
            if (step.SpotFacing != null)
            {
                _spotFacing.Size = _spotFacing.Texture.GetSize();
                _spotFacing.PivotOffset = _spotFacing.Size / 2f;
                _spotFacing.Rotation = GridConversions.FacingToRotation(step.SpotFacing.Value);
                _spotFacing.Position = size / 2f - _spotFacing.Size / 2f;
            }
            _spot.Visible = true;
        }

        /// <summary>
        /// " Shortcut: 5." for a step that needs a build-menu row -- read from the menu's own row
        /// order, so the hint cannot drift from the key when the menu changes.
        /// </summary>
        private string MenuKeyHint(TutorialStep step)
        {
            if (step?.MenuKey == null || _world?.Sandbox == null)
            {
                return "";
            }
            var placeables = _world.Sandbox.Placeables;
            for (int i = 0; i < placeables.Count; i++)
            {
                if (placeables[i].Key == step.MenuKey)
                {
                    string key = UI.BuildMenuLabels.KeyFor(i);
                    return key.Length > 0 ? $" Shortcut: {key}." : "";
                }
            }
            return "";
        }

        /// <summary>On the last step of a playtest, where the report is: the thing to send back.</summary>
        private static string ReportHint(TutorialStep step, TutorialGuide guide) =>
            guide.Playtest != null && step.Id == "done"
                ? "\n\nPlaytest report (updates as you play): " + PlaytestNode.ReportFile
                : "";

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
