using System.Linq;
using Godot;
using GolemFactory.World;
using CoreVector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// G4's exit check on Sandbox.tscn: the world is built from its rules, and the player can walk
    /// it.
    ///
    /// <list type="number">
    ///   <item>The shell holds exactly SandboxLayout's walls, sconces and props.</item>
    ///   <item>Every stall publishes its node on its own cell, so a golem beside it can extract.</item>
    ///   <item>Walking south, the player reaches the street's far end and is stopped by the world
    ///   clamp; walking north again, they are back inside the workshop.</item>
    ///   <item>The camera has followed: once the player stops, it settles on them.</item>
    /// </list>
    /// </summary>
    public sealed class WorldScenario : IScenario
    {
        private enum Phase { South, North, Settle }

        private const double PhaseSeconds = 7.0;
        private const double SettleSeconds = 2.0;

        private ScenarioRunner _runner;
        private PlayerNode _player;
        private Camera2D _camera;
        private Phase _phase;
        private double _elapsed;
        private float _southmost = float.MaxValue;
        private string _staticFailure;
        private string _staticReport;

        public void Begin(ScenarioRunner runner)
        {
            _runner = runner;
            SceneTree tree = runner.GetTree();
            var shell = tree.GetFirstNodeInGroup(ShellNode.GroupName) as ShellNode;
            var sandbox = tree.GetFirstNodeInGroup(SandboxNode.GroupName) as SandboxNode;
            _player = tree.Root.FindChild("Player", true, false) as PlayerNode;
            _camera = tree.Root.GetViewport().GetCamera2D();
            WorldNode world = runner.World;

            if (shell == null || sandbox == null || _player == null || _camera == null || world.Setup == null)
            {
                _staticFailure = "Sandbox.tscn is missing its shell, sandbox, player, camera or setup";
                return;
            }

            int walls = SandboxLayout.Walls(world.Bounds.NorthExtent).Count;
            int props = SandboxLayout.Props().Count;
            if (shell.WallCount != walls || shell.PropCount != props || shell.SconceCount != 7)
            {
                _staticFailure = $"shell built {shell.WallCount} walls/{shell.PropCount} props/{shell.SconceCount} sconces, " +
                                 $"layout says {walls}/{props}/7";
                return;
            }

            var unpublished = world.Setup.nodes
                .Where(n => !world.Endpoints.HasEndpoint(new CoreVector2Int(n.x, n.y)))
                .Select(n => n.id).ToList();
            if (sandbox.Markers.Count != world.Setup.nodes.Count || unpublished.Count > 0)
            {
                _staticFailure = $"stalls: {sandbox.Markers.Count} built, unpublished: {string.Join(", ", unpublished)}";
                return;
            }

            string depth = CheckSortPoints(tree.Root.FindChild("Entities", true, false));
            if (depth != null)
            {
                _staticFailure = depth;
                return;
            }

            _staticReport = $"{walls} walls, {props} props, 7 sconces, {sandbox.Markers.Count} stalls published, {_standingChecked} standing sprites sort at their feet";
            _player.ScriptedMove = new Vector2(0f, -1f);
        }

        private int _standingChecked;

        // Pieces whose contact line is not their bottom edge: side walls stand on a VERTICAL
        // line, and the skirting and kerb hang below theirs. Their pivots are Unity's, by design.
        private static readonly string[] NotFeet = { "wall_side_", "floor_edge", "street_edge", "ground_shadow" };

        /// <summary>
        /// Y-sorting compares each item's origin, so a standing sprite whose feet are not at its
        /// origin draws in front of things it stands behind (or the reverse). Checks every
        /// standing sprite in the entity layer: feet within 4px of the node that is sorted.
        /// </summary>
        private string CheckSortPoints(Node entities)
        {
            var stack = new System.Collections.Generic.Stack<Node>();
            stack.Push(entities);
            while (stack.Count > 0)
            {
                Node node = stack.Pop();
                foreach (Node child in node.GetChildren())
                {
                    stack.Push(child);
                }

                if (node is not Sprite2D sprite || sprite.Centered || sprite.Texture == null || sprite.ZIndex < 0)
                {
                    continue;
                }
                string file = sprite.Texture.ResourcePath.GetFile();
                if (System.Array.Exists(NotFeet, prefix => file.StartsWith(prefix)))
                {
                    continue;
                }

                // The sorted item is the ancestor whose parent does the y-sorting.
                Node2D sorted = sprite;
                while (sorted.GetParent() is Node2D parent && !parent.YSortEnabled)
                {
                    sorted = parent;
                }

                float feet = sprite.GlobalPosition.Y + (sprite.Offset.Y + sprite.Texture.GetHeight()) * sprite.GlobalScale.Y;
                float gap = feet - sorted.GlobalPosition.Y;
                if (Mathf.Abs(gap) > 4f)
                {
                    return $"{sorted.Name} ({file}) draws its feet {gap:F0}px from its sort point";
                }
                _standingChecked++;
            }
            return null;
        }

        public ScenarioResult? Step(double delta)
        {
            if (_staticFailure != null)
            {
                return new ScenarioResult(false, _staticFailure);
            }
            if (_player == null)
            {
                return null; // Begin is deferred
            }

            _elapsed += delta;
            float y = _player.CorePosition.y;
            _southmost = Mathf.Min(_southmost, y);

            switch (_phase)
            {
                case Phase.South when _elapsed >= PhaseSeconds:
                    _phase = Phase.North;
                    _elapsed = 0;
                    _player.ScriptedMove = new Vector2(0f, 1f);
                    return null;

                case Phase.North when _elapsed >= PhaseSeconds:
                    _phase = Phase.Settle;
                    _elapsed = 0;
                    _player.ScriptedMove = Vector2.Zero;
                    return null;

                case Phase.Settle when _elapsed >= SettleSeconds:
                    int farRow = -FloorLayout.HalfExtent - FloorLayout.StreetDepth;
                    bool reachedStreetEnd = _southmost <= farRow + 0.5f;
                    bool stoppedByClamp = _southmost >= farRow - 0.5f;
                    bool backInside = y > -FloorLayout.HalfExtent;
                    float cameraGap = _camera.GlobalPosition.DistanceTo(_player.GlobalPosition);
                    bool cameraFollowed = cameraGap < GridConversions.CellPixels / 2f;
                    bool pass = reachedStreetEnd && stoppedByClamp && backInside && cameraFollowed;
                    return new ScenarioResult(pass,
                        $"{_staticReport}; walked south to y={_southmost:F2} (street end {farRow}), " +
                        $"back north to y={y:F2}; camera {cameraGap:F1}px from the player");

                default:
                    return null;
            }
        }
    }
}
