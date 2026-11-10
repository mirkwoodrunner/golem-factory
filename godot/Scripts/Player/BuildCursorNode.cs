using Godot;
using GolemFactory.Buildings;
using GolemFactory.Player;
using GolemFactory.World;
using CoreVector2Int = GolemFactory.Compat.Vector2Int;
using CoreColor = GolemFactory.Compat.Color;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The mouse half of build mode: turns the pointer into the cell verbs Core's
    /// BuildModeController takes, and draws the ghost.
    ///
    /// <para>
    /// Press = <c>Click</c> (place or demolish, and start a drag), move = <c>Hover</c> (which
    /// extends a live drag), release = <c>Release</c>. R turns the placement (<c>RotateKey</c>).
    /// Right-click or Escape puts the tool down (<c>CancelPlacement</c>). Clicks a UI control
    /// consumed never arrive here -- this listens on <c>_UnhandledInput</c> -- which is what
    /// Unity's <c>pointerOverUi</c> argument guarded against: selecting a row in the build menu
    /// must not also build on the tile under the menu.
    /// </para>
    ///
    /// <para>
    /// The ghost is <c>build_ghost_tile</c> at floor level, tinted by BuildGhostVisuals from the
    /// controller's own GhostStateFor (so the ghost and the click read one answer), with the
    /// facing arrow 0.28 cells toward the facing, hidden for the wrecking bar.
    /// </para>
    ///
    /// <para>
    /// It also owns the pad's cursor, with a tool in hand or not: with empty hands it is where
    /// the hover card looks (<see cref="GolemHoverNode"/>, docs/steam-deck-design.md D5.2).
    /// </para>
    /// </summary>
    public partial class BuildCursorNode : Node2D
    {
        public const string RotateAction = "build_rotate";
        public const string CancelAction = "build_cancel";

        private WorldNode _world;
        private Sprite2D _ghost;
        private Sprite2D _arrow;
        private CoreVector2Int _lastCell = new CoreVector2Int(int.MinValue, int.MinValue);
        private double _time;

        private BuildModeController Build => _world.Sandbox.Build;

        // The pad's cursor (docs/steam-deck-design.md D5.1, built with D4 so a pad can place):
        // an offset from the player's cell, so it walks with them, stepped by the right stick.
        /// <summary>How far, in cells, the pad's cursor may stray from the player.</summary>
        public const int PadReach = 6;
        /// <summary>The right stick held over: the first step waits this long before repeating...</summary>
        public const double PadFirstRepeat = 0.3;
        /// <summary>...then steps at this rate.</summary>
        public const double PadRepeat = 0.12;

        private CoreVector2Int _padOffset;
        private bool _wasActive;
        private double _padHeldFor = -1;
        private double _padNextStep;
        private bool _padPlacing;
        private bool _padAimed;
        private Sprite2D _reticle;
        private PlayerNode _player;

        /// <summary>
        /// The look-around tile's tint: brass, not the ghost's valid green, so it reads as a glance,
        /// not a placement. Bright enough to find on the Deck's 7-inch screen. <b>TUNING.</b>
        /// </summary>
        private static readonly Color ReticleTint = new Color(1.6f, 1.25f, 0.6f, 0.95f);

        public override void _Ready()
        {
            _world = WorldNode.Find(this);
            Bind(RotateAction, Key.R);
            Bind(CancelAction, Key.Escape);
            _player = FindPlayer(GetTree().Root);

            // Floor level: beneath every standing thing, above the floor tiles.
            ZIndex = -1;
            _ghost = new Sprite2D { Texture = GD.Load<Texture2D>("res://art/build_ghost_tile.png"), Visible = false };
            AddChild(_ghost);
            _arrow = new Sprite2D { Texture = GD.Load<Texture2D>("res://art/facing_arrow.png") };
            _ghost.AddChild(_arrow);
            _reticle = new Sprite2D { Texture = _ghost.Texture, Modulate = ReticleTint, Visible = false };
            AddChild(_reticle);
        }

        /// <summary>Whether the ghost is drawn, and where (Core cell) -- for a scenario to check.</summary>
        public bool GhostVisible => _ghost.Visible;
        public CoreVector2Int GhostCell => GridConversions.WorldToCell(_ghost.Position);
        public Color GhostTint => _ghost.Modulate;

        /// <summary>The cell the build tool aims at: the pad's cursor while the pad is in use, else the pointer's.</summary>
        public CoreVector2Int PointerCell => UsingPad ? PadCell : GridConversions.WorldToCell(GetGlobalMousePosition());

        /// <summary>Whether the pad's cursor, not the mouse, aims the tool.</summary>
        public bool UsingPad => InputDevices.PadInUse && _player != null;

        /// <summary>The pad's cursor cell: the player's cell plus the offset the right stick set.</summary>
        public CoreVector2Int PadCell => PlayerCell + _padOffset;

        private CoreVector2Int PlayerCell =>
            _player == null ? default : new CoreVector2Int(Mathf.RoundToInt(_player.CorePosition.x), Mathf.RoundToInt(_player.CorePosition.y));

        public override void _UnhandledInput(InputEvent e)
        {
            // The pad's place trigger: the left click's press and release, at the pad's cursor.
            if (Build.IsBuildToolActive && UsingPad && (e.IsActionPressed(PadBindings.BuildPlace) || e.IsActionReleased(PadBindings.BuildPlace)))
            {
                if (e.IsActionPressed(PadBindings.BuildPlace) && !_padPlacing)
                {
                    _padPlacing = true;
                    _lastCell = PadCell;
                    Build.Hover(PadCell);
                    Build.Click(PadCell, pointerOverUi: false);
                }
                else if (e.IsActionReleased(PadBindings.BuildPlace) && _padPlacing)
                {
                    _padPlacing = false;
                    Build.Release();
                }
                GetViewport().SetInputAsHandled();
                return;
            }

            if (e.IsActionPressed(CancelAction) || (e is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } && Build.IsBuildToolActive))
            {
                if (Build.CancelPlacement())
                {
                    GetViewport().SetInputAsHandled();
                }
                return;
            }

            if (e.IsActionPressed(RotateAction) && Build.RotateKey())
            {
                GetViewport().SetInputAsHandled();
                return;
            }

            if (e is InputEventMouseButton { ButtonIndex: MouseButton.Left } button && Build.IsBuildToolActive)
            {
                CoreVector2Int cell = GridConversions.WorldToCell(GetGlobalMousePosition());
                if (button.Pressed)
                {
                    Build.Click(cell, pointerOverUi: false);
                }
                else
                {
                    Build.Release();
                }
                GetViewport().SetInputAsHandled();
            }
        }

        public override void _Process(double delta)
        {
            using var perf = NodePerf.Measure("node.cursor");
            _time += delta;
            bool active = Build.IsBuildToolActive;
            _ghost.Visible = active;
            if (!active)
            {
                if (Build.IsDragging)
                {
                    Build.Release(); // a drag must never outlive its tool
                }
                _wasActive = false;
                _padPlacing = false;
                LookAround(delta);
                return;
            }
            _reticle.Visible = false;
            if (!_wasActive)
            {
                // A tool just picked up: the pad's cursor starts one cell ahead of the Artificer.
                _wasActive = true;
                _padAimed = false;
                _padOffset = FacingStep(_player?.Facing ?? ArtificerFacing.Down);
                _padHeldFor = -1;
            }
            if (UsingPad)
            {
                StepPadCursor(delta);
            }

            CoreVector2Int cell = PointerCell;
            if (cell != _lastCell)
            {
                _lastCell = cell;
                Build.Hover(cell);
            }
            ShowGhost(cell);
        }

        /// <summary>Draws the ghost at <paramref name="cell"/>. Public so a scenario can drive it without a mouse.</summary>
        public void ShowGhost(CoreVector2Int cell)
        {
            // Hidden over UI, as Unity's BuildClickPolicy.ShouldPlace(tool, pointerOverUi) hid
            // it: a ghost under the menu promises a click the menu will take.
            // The pad's cursor is never "over the UI": a resting mouse's hover means nothing then.
            _ghost.Visible = BuildClickPolicy.ShouldPlace(Build.IsBuildToolActive, !UsingPad && GetViewport().GuiGetHoveredControl() != null);
            _ghost.Position = GridConversions.CellToWorld(cell);
            CoreColor tint = BuildGhostVisuals.Evaluate(Build.GhostStateFor(cell), (float)_time, _world.Sandbox.Motion.Pulse);
            _ghost.Modulate = new Color(tint.r, tint.g, tint.b, tint.a);

            // No arrow for the wrecking bar, nor for a splitter, which has no facing to show:
            // its outputs are whichever neighbouring belts lead away from it.
            _arrow.Visible = !Build.IsDemolishActive && Build.ActivePrefab?.GetPart<PlaceableBeltSplitter>() == null;
            Facing facing = Build.PlacementFacing;
            _arrow.Rotation = GridConversions.FacingToRotation(facing);
            _arrow.Position = GridConversions.FacingStep(facing) * 0.28f;
        }

        /// <summary>
        /// With no tool in hand, the pad's cursor is where the hover card looks (D5.2): one cell
        /// ahead of the Artificer, turning as they turn, until the right stick moves it to look
        /// around. Then it holds its offset, drawn as a brass tile, until the player walks again.
        /// </summary>
        private void LookAround(double delta)
        {
            if (!UsingPad || ModalScreens.AnyOpen(GetTree()))
            {
                _padAimed &= UsingPad; // a screen opened over an aimed cursor keeps it
                _padHeldFor = -1;
                _reticle.Visible = false;
                return;
            }
            if (_player.Walking)
            {
                _padAimed = false;
                _padHeldFor = -1;
            }
            if (!_padAimed)
            {
                // The first push looks on from the cell ahead, not from wherever a tool left it.
                _padOffset = FacingStep(_player.Facing);
            }
            if (!_player.Walking && StepPadCursor(delta))
            {
                _padAimed = true;
            }
            _reticle.Visible = _padAimed;
            _reticle.Position = GridConversions.CellToWorld(PadCell);
        }

        /// <summary>Whether the right stick has moved the look-around cursor off the cell ahead -- for a scenario.</summary>
        public bool PadAimed => _padAimed;

        /// <summary>Whether the look-around cursor's tile is drawn -- for a scenario.</summary>
        public bool ReticleVisible => _reticle.Visible;

        /// <summary>
        /// The right stick steps the cursor a cell at a time: once on the push, then again after
        /// <see cref="PadFirstRepeat"/> and every <see cref="PadRepeat"/> while held, never further
        /// than <see cref="PadReach"/> from the player. Returns whether it stepped.
        /// </summary>
        private bool StepPadCursor(double delta)
        {
            if (TextEntry.IsTyping(GetViewport()) || InputDevices.OverlayHoldsPad)
            {
                return false;
            }
            Vector2 stick = Input.GetVector(PadBindings.CursorLeft, PadBindings.CursorRight, PadBindings.CursorDown, PadBindings.CursorUp); // +y north
            if (stick == Vector2.Zero)
            {
                _padHeldFor = -1;
                return false;
            }
            if (_padHeldFor < 0)
            {
                _padHeldFor = 0;
                _padNextStep = PadFirstRepeat; // the push steps at once
            }
            else
            {
                _padHeldFor += delta;
                if (_padHeldFor < _padNextStep)
                {
                    return false;
                }
                _padNextStep += PadRepeat;
            }
            // A diagonal push steps both ways; a mostly-sideways one steps sideways only.
            int dx = Mathf.Abs(stick.X) >= 0.38f ? System.Math.Sign(stick.X) : 0;
            int dy = Mathf.Abs(stick.Y) >= 0.38f ? System.Math.Sign(stick.Y) : 0;
            _padOffset = new CoreVector2Int(
                System.Math.Clamp(_padOffset.x + dx, -PadReach, PadReach),
                System.Math.Clamp(_padOffset.y + dy, -PadReach, PadReach));
            return true;
        }

        private static CoreVector2Int FacingStep(ArtificerFacing facing) => facing switch
        {
            ArtificerFacing.Up => new CoreVector2Int(0, 1),
            ArtificerFacing.Left => new CoreVector2Int(-1, 0),
            ArtificerFacing.Right => new CoreVector2Int(1, 0),
            _ => new CoreVector2Int(0, -1),
        };

        private static PlayerNode FindPlayer(Node node)
        {
            if (node is PlayerNode player)
            {
                return player;
            }
            foreach (Node child in node.GetChildren())
            {
                if (FindPlayer(child) is PlayerNode found)
                {
                    return found;
                }
            }
            return null;
        }

        private static void Bind(string action, Key key)
        {
            if (!InputMap.HasAction(action))
            {
                InputMap.AddAction(action);
                InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
            }
            PadBindings.AddTo(action);
        }
    }
}
