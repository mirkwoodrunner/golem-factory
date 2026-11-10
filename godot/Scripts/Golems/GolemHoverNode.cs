using Godot;
using GolemFactory.Golems;
using GolemFactory.UI;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// Mouse hover over golems (docs/onboarding-feedback-design.md §4): feeds the pointer's cell
    /// to Core's <see cref="GolemInspector"/>, lights the hovered golem's source and target tiles
    /// through its <see cref="GolemNode"/>, and draws the hover card Core composes
    /// (<see cref="GolemHoverText"/>). It decides nothing about what a golem is doing.
    ///
    /// <para>
    /// No pointer in the world -- nothing hovered -- while the mouse is over a control, a full
    /// screen is open, the wrecking bar is up (its ghost says what a click takes), or the mouse
    /// is off the window. With the pad in use, the pad's cursor is the pointer
    /// (<see cref="BuildCursorNode.PadCell"/>), and the card hangs from its cell.
    /// </para>
    /// </summary>
    public partial class GolemHoverNode : CanvasLayer
    {
        /// <summary>How long a golem is hovered before its card shows. <b>TUNING.</b></summary>
        public const float CardDelaySeconds = 0.25f;

        /// <summary>The card's offset from the pointer, right and down.</summary>
        private const float CardOffset = 16f;

        private WorldNode _world;
        private PanelContainer _card;
        private Label _cardLabel;
        private GolemEntity _hovered;
        private float _hoveredFor;
        private float _strainingFor;

        /// <summary>The golem under the mouse, or null -- for a scenario.</summary>
        public GolemEntity Hovered => _hovered;
        public bool CardVisible => _card.Visible;
        public string CardText => _cardLabel.Text;
        public Vector2 CardPosition => _card.Position;

        public override void _Ready()
        {
            // Above the guide (40) and the playtest card (41): the guide's arrow covered the card's
            // text in the frames. Transient, and never shown over a control, so it hides nothing.
            Layer = 42;
            _world = WorldNode.Find(this);

            var style = new StyleBoxFlat
            {
                BgColor = new Color(0.13f, 0.10f, 0.08f, 0.94f),
                BorderColor = new Color(0.78f, 0.60f, 0.32f, 1f),
                ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 6,
            };
            style.SetBorderWidthAll(2);
            style.SetCornerRadiusAll(4);
            _card = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
            _card.AddThemeStyleboxOverride("panel", style);
            _cardLabel = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
            _cardLabel.AddThemeFontSizeOverride("font_size", 14);
            _cardLabel.AddThemeColorOverride("font_color", new Color(0.97f, 0.93f, 0.85f));
            _card.AddChild(_cardLabel);
            AddChild(_card);
        }

        public override void _Process(double delta)
        {
            using var perf = NodePerf.Measure("node.hover");
            World.SandboxWorld sandbox = _world?.Sandbox;
            if (sandbox == null)
            {
                return;
            }

            sandbox.Inspector.Point(PointerCell(sandbox));
            GolemEntity hovered = sandbox.Inspector.Hovered;
            if (hovered != _hovered)
            {
                _strainingFor = 0f;
            }
            _hoveredFor = hovered == _hovered ? _hoveredFor + (float)delta : 0f;
            _hovered = hovered;

            GolemNode hoveredNode = null;
            bool show = hovered != null && _hoveredFor >= CardDelaySeconds;
            foreach (Node node in GetTree().GetNodesInGroup(GolemNodeGroup.Name))
            {
                if (node is GolemNode golem)
                {
                    golem.Hovered = hovered != null && golem.Entity == hovered;
                    golem.HoverCardUp = golem.Hovered && show;
                    if (golem.Hovered)
                    {
                        hoveredNode = golem;
                    }
                }
            }

            show &= hoveredNode != null;
            _card.Visible = show;
            if (!show)
            {
                return;
            }

            GolemMood mood = (int)hoveredNode.DrawnMood < 0 ? hovered.Mood : hoveredNode.DrawnMood;
            _strainingFor = GolemHoverText.NearlyFull(hovered) ? _strainingFor + (float)delta : 0f;
            string text = sandbox.HoverCard(hovered, mood, _strainingFor >= GolemMoodRules.AdvisoryDwellSeconds).Text;
            if (_cardLabel.Text != text)
            {
                _cardLabel.Text = text;
            }

            // Right of and below the pointer (or the pad's cursor cell), kept on screen.
            _card.ResetSize();
            Vector2 view = GetViewport().GetVisibleRect().Size;
            Vector2 at = CardAnchor() + new Vector2(CardOffset, CardOffset);
            at.X = Mathf.Clamp(at.X, 0f, Mathf.Max(0f, view.X - _card.Size.X));
            at.Y = Mathf.Clamp(at.Y, 0f, Mathf.Max(0f, view.Y - _card.Size.Y));
            _card.Position = at;
        }

        // The pointer as the motion events last put it, as the GUI tracks it. Read from events
        // rather than polled, so a pointer a scenario moves is the pointer hover sees.
        private Vector2 _mouse = new Vector2(-1f, -1f);
        private bool _mouseInWindow = true;

        public override void _Input(InputEvent e)
        {
            if (e is InputEventMouse mouse)
            {
                _mouse = mouse.Position;
                _mouseInWindow = true;
            }
        }

        public override void _Notification(int what)
        {
            if (what == NotificationWMMouseExit)
            {
                _mouseInWindow = false;
            }
        }

        /// <summary>
        /// The cell under the mouse, or null when the pointer isn't in the world. With the pad in
        /// use it is the pad's cursor (docs/steam-deck-design.md D5.2): the cell ahead of the
        /// Artificer, or where the right stick moved it. A resting mouse means nothing then.
        /// </summary>
        private Compat.Vector2Int? PointerCell(World.SandboxWorld sandbox)
        {
            Viewport viewport = GetViewport();
            _cursor ??= FindCursor(GetTree().Root);
            if (_cursor != null && _cursor.UsingPad)
            {
                return ModalScreens.AnyOpen(GetTree()) || sandbox.Build.IsDemolishActive ? null : _cursor.PadCell;
            }
            Vector2 mouse = _mouse;
            if (!_mouseInWindow || !viewport.GetVisibleRect().HasPoint(mouse) ||
                viewport.GuiGetHoveredControl() != null ||
                ModalScreens.AnyOpen(GetTree()) ||
                sandbox.Build.IsDemolishActive)
            {
                return null;
            }
            Vector2 world = viewport.GetCanvasTransform().AffineInverse() * mouse;
            return GridConversions.WorldToCell(world);
        }

        private BuildCursorNode _cursor;

        /// <summary>Where the card hangs from: the pointer, or with the pad the cursor cell's top-right corner on screen.</summary>
        private Vector2 CardAnchor()
        {
            if (_cursor != null && _cursor.UsingPad)
            {
                float half = GridConversions.CellPixels / 2f;
                Vector2 corner = GridConversions.CellToWorld(_cursor.PadCell) + new Vector2(half, -half);
                return GetViewport().GetCanvasTransform() * corner;
            }
            return _mouse;
        }

        private static BuildCursorNode FindCursor(Node node)
        {
            if (node is BuildCursorNode cursor)
            {
                return cursor;
            }
            foreach (Node child in node.GetChildren())
            {
                if (FindCursor(child) is BuildCursorNode found)
                {
                    return found;
                }
            }
            return null;
        }
    }
}
