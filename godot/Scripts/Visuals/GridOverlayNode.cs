using System.Linq;
using Godot;
using GolemFactory.World;
using CoreVector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The build grid on the floor (<c>res://shaders/grid_overlay.gdshader</c>): while a build
    /// tool is in hand, a pool of dotted cell lines and brass corner ticks around the cursor,
    /// and brackets that close onto the cell the cursor has snapped to, in the ghost's colour.
    ///
    /// <para>
    /// One quad and one material, moved with the cursor; the lines are worked out per pixel from
    /// the world position. The node decides nothing: the cell and the colour are read off
    /// <see cref="BuildCursorNode"/>'s ghost, which reads Core's <c>BuildModeController</c> and
    /// <c>BuildGhostVisuals</c>, and the two short curves are Core's
    /// <see cref="GridOverlayRules"/>. A shader parameter is only sent when it has changed.
    /// </para>
    ///
    /// <para>
    /// <see cref="DebugCells"/> draws every cell boundary in view instead (the cells Core's
    /// <c>SpatialHashGrid</c> keys its entities by are these cells): F3 in a debug build, or
    /// <c>-- --grid-debug</c>.
    /// </para>
    ///
    /// <para>
    /// Must come after the build cursor in the scene, so it reads this frame's ghost.
    /// </para>
    /// </summary>
    public partial class GridOverlayNode : Node2D
    {
        public const string ShaderPath = "res://shaders/grid_overlay.gdshader";
        public const string DebugFlag = "--grid-debug";

        [Export] public NodePath CursorPath { get; set; }

        private static readonly StringName CursorPx = "cursor_px";
        private static readonly StringName CursorColor = "cursor_color";
        private static readonly StringName SnapPx = "snap_px";
        private static readonly StringName ShowCursor = "show_cursor";
        private static readonly StringName DebugCellsParameter = "debug_cells";

        private WorldNode _world;
        private BuildCursorNode _cursor;
        private MeshInstance2D _quad;
        private ShaderMaterial _material;

        private CoreVector2Int _cell = new CoreVector2Int(int.MinValue, int.MinValue);
        private Color _stateTint;
        private bool _wasShown;
        private float _snapAge = -1f;
        private float _blendAge = -1f;
        private Color _from;

        // What the material was last sent.
        private Vector2 _sentCursor = new Vector2(float.NaN, float.NaN);
        private Color _sentColor = new Color(float.NaN, 0f, 0f);
        private float _sentSnap = float.NaN;
        private float _sentShow = float.NaN;
        private float _sentDebug = float.NaN;

        /// <summary>Every cell boundary in view, for debugging; off in a normal game.</summary>
        public bool DebugCells { get; set; }

        // --- For scenarios ------------------------------------------------------------------
        /// <summary>Whether the grid is drawn this frame.</summary>
        public bool Shown => _quad.Visible;

        /// <summary>The cell the brackets stand on.</summary>
        public CoreVector2Int Cell => _cell;

        /// <summary>The brackets' colour and their distance out from the cell, as last sent to the shader.</summary>
        public Color DrawnColor => _sentColor;
        public float DrawnSnap => _sentSnap;

        /// <summary>The quad's rectangle in world pixels.</summary>
        public Rect2 DrawnRect => new Rect2(_quad.Position - _quad.Scale / 2f, _quad.Scale);

        public ShaderMaterial GridMaterial => _material;

        public override void _Ready()
        {
            _world = WorldNode.Find(this);
            _cursor = GetNodeOrNull<BuildCursorNode>(CursorPath);
            DebugCells = OS.GetCmdlineUserArgs().Contains(DebugFlag);

            _material = new ShaderMaterial { Shader = GD.Load<Shader>(ShaderPath) };
            _material.SetShaderParameter("cell_px", (float)GridConversions.CellPixels);
            _material.SetShaderParameter("reach_cells", GridOverlayRules.ReachCells);
            // A unit quad, scaled to the pixels it must cover.
            _quad = new MeshInstance2D
            {
                Name = "Quad",
                Mesh = new QuadMesh { Size = Vector2.One },
                Material = _material,
                Visible = false,
            };
            AddChild(_quad);

            // Floor level, with the ghost: over the belts and pipes, under everything that stands.
            ZIndex = -1;
        }

        public override void _UnhandledKeyInput(InputEvent e)
        {
            if (OS.IsDebugBuild() && e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F3 })
            {
                DebugCells = !DebugCells;
                GetViewport().SetInputAsHandled();
            }
        }

        /// <summary>
        /// While set, each frame's managed allocation by the grid is added to
        /// <see cref="AllocatedBytes"/>: the <c>tick-visuals</c> scenario's proof that it is none.
        /// </summary>
        public bool CountAllocations { get; set; }

        /// <summary>Bytes allocated by the grid's frames while <see cref="CountAllocations"/> was set.</summary>
        public long AllocatedBytes { get; set; }

        public override void _Process(double delta)
        {
            if (!CountAllocations)
            {
                Present(delta);
                return;
            }
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            Present(delta);
            AllocatedBytes += System.GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private void Present(double delta)
        {
            bool tool = _cursor != null && _cursor.GhostVisible;
            _quad.Visible = tool || DebugCells;
            if (!_quad.Visible)
            {
                _wasShown = false;
                return;
            }

            SandboxWorld sandbox = _world.Sandbox;
            bool motion = sandbox.Motion.Pulse;
            float cellPixels = GridConversions.CellPixels;

            if (tool)
            {
                CoreVector2Int cell = _cursor.GhostCell;
                // The ghost's colour is the placement state as Core judged it this frame. A state
                // is told from the next by its hue; its alpha is only the blocked state's pulse.
                // (Asking BuildModeController again here would cost the frame an allocation.)
                Color tint = _cursor.GhostTint;
                bool stateChanged = tint.R != _stateTint.R || tint.G != _stateTint.G || tint.B != _stateTint.B;
                _stateTint = tint;
                if (!_wasShown)
                {
                    // A tool just picked up: nothing to blend from.
                    stateChanged = false;
                    _blendAge = -1f;
                    _cell = cell;
                    _snapAge = 0f;
                }
                if (cell != _cell)
                {
                    _cell = cell;
                    _snapAge = 0f;
                }
                if (stateChanged)
                {
                    _from = float.IsNaN(_sentColor.R) ? tint : _sentColor;
                    _blendAge = 0f;
                }
                float dt = (float)delta;
                if (_snapAge >= 0f)
                {
                    _snapAge = _snapAge + dt >= GridOverlayRules.SnapSeconds ? -1f : _snapAge + dt;
                }
                if (_blendAge >= 0f)
                {
                    _blendAge = _blendAge + dt >= GridOverlayRules.BlendSeconds ? -1f : _blendAge + dt;
                }
                Color color = _blendAge >= 0f ? _from.Lerp(tint, GridOverlayRules.Blend(_blendAge, motion)) : tint;
                Send(GridConversions.CellToWorld(cell), color, GridOverlayRules.SnapOffset(_snapAge, motion));
            }
            _wasShown = tool;
            SendFlag(ShowCursor, ref _sentShow, tool ? 1f : 0f);
            SendFlag(DebugCellsParameter, ref _sentDebug, DebugCells ? 1f : 0f);

            // The quad covers the pool around the cursor, or the whole view when debugging.
            Rect2 view = ViewRect.World;
            if (DebugCells && view.Size != Vector2.Zero)
            {
                Place(view.GetCenter(), view.Size);
            }
            else
            {
                float span = (Mathf.Ceil(GridOverlayRules.ReachCells) * 2f + 2f) * cellPixels;
                Place(GridConversions.CellToWorld(_cell), new Vector2(span, span));
            }
        }

        private void Place(Vector2 centre, Vector2 size)
        {
            if (_quad.Position != centre)
            {
                _quad.Position = centre;
            }
            if (_quad.Scale != size)
            {
                _quad.Scale = size;
            }
        }

        private void Send(Vector2 cursor, Color color, float snap)
        {
            if (cursor != _sentCursor)
            {
                _sentCursor = cursor;
                _material.SetShaderParameter(CursorPx, cursor);
            }
            if (color != _sentColor)
            {
                _sentColor = color;
                _material.SetShaderParameter(CursorColor, color);
            }
            SendFlag(SnapPx, ref _sentSnap, snap);
        }

        private void SendFlag(StringName name, ref float sent, float value)
        {
            // Exact on purpose: "the same number as last time" is the question.
            if (value != sent)
            {
                sent = value;
                _material.SetShaderParameter(name, value);
            }
        }
    }
}
