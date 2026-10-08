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

        public override void _Ready()
        {
            _world = WorldNode.Find(this);
            Bind(RotateAction, Key.R);
            Bind(CancelAction, Key.Escape);

            // Floor level: beneath every standing thing, above the floor tiles.
            ZIndex = -1;
            _ghost = new Sprite2D { Texture = GD.Load<Texture2D>("res://art/build_ghost_tile.png"), Visible = false };
            AddChild(_ghost);
            _arrow = new Sprite2D { Texture = GD.Load<Texture2D>("res://art/facing_arrow.png") };
            _ghost.AddChild(_arrow);
        }

        /// <summary>Whether the ghost is drawn, and where (Core cell) -- for a scenario to check.</summary>
        public bool GhostVisible => _ghost.Visible;
        public CoreVector2Int GhostCell => GridConversions.WorldToCell(_ghost.Position);
        public Color GhostTint => _ghost.Modulate;

        /// <summary>The cell under the pointer, in Core's frame.</summary>
        public CoreVector2Int PointerCell => GridConversions.WorldToCell(GetGlobalMousePosition());

        public override void _UnhandledInput(InputEvent e)
        {
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
            _time += delta;
            bool active = Build.IsBuildToolActive;
            _ghost.Visible = active;
            if (!active)
            {
                if (Build.IsDragging)
                {
                    Build.Release(); // a drag must never outlive its tool
                }
                return;
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
            _ghost.Visible = BuildClickPolicy.ShouldPlace(Build.IsBuildToolActive, GetViewport().GuiGetHoveredControl() != null);
            _ghost.Position = GridConversions.CellToWorld(cell);
            CoreColor tint = BuildGhostVisuals.Evaluate(Build.GhostStateFor(cell), (float)_time);
            _ghost.Modulate = new Color(tint.r, tint.g, tint.b, tint.a);

            // No arrow for the wrecking bar, nor for a splitter, which has no facing to show:
            // its outputs are whichever neighbouring belts lead away from it.
            _arrow.Visible = !Build.IsDemolishActive && Build.ActivePrefab?.GetPart<PlaceableBeltSplitter>() == null;
            Facing facing = Build.PlacementFacing;
            _arrow.Rotation = GridConversions.FacingToRotation(facing);
            _arrow.Position = GridConversions.FacingStep(facing) * 0.28f;
        }

        private static void Bind(string action, Key key)
        {
            if (InputMap.HasAction(action))
            {
                return;
            }
            InputMap.AddAction(action);
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
        }
    }
}
