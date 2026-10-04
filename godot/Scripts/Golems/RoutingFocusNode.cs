using System.Collections.Generic;
using Godot;
using GolemFactory.World;
using CoreVector3 = GolemFactory.Compat.Vector3;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// Shows where the nearest golem takes from and gives to: Unity's RoutingFocusController.
    ///
    /// <para>
    /// Every golem draws its facing arrow all the time; only the ONE nearest the player (within
    /// 3.5 cells) also lights its source tile (teal) and target tile (gold) on the floor, picked
    /// by Core's <see cref="RoutingFocus"/>. That is the moment the player is about to carry,
    /// turn or program it, which is when routing matters, and showing every golem's tiles at
    /// once would carpet the floor.
    /// </para>
    /// </summary>
    public partial class RoutingFocusNode : Node
    {
        /// <summary>Unity's focusRange, in cells.</summary>
        [Export] public float FocusRange { get; set; } = 3.5f;

        [Export] public NodePath PlayerPath { get; set; }

        private PlayerNode _player;
        private readonly List<GolemNode> _golems = new List<GolemNode>();
        private CoreVector3[] _positions = new CoreVector3[0];

        /// <summary>The golem whose tiles are lit, or null.</summary>
        public GolemNode Focused { get; private set; }

        public override void _Ready() =>
            _player = PlayerPath != null && !PlayerPath.IsEmpty ? GetNode<PlayerNode>(PlayerPath) : null;

        public override void _Process(double delta)
        {
            if (_player == null)
            {
                return;
            }

            _golems.Clear();
            foreach (Node node in GetTree().GetNodesInGroup(GolemNodeGroup.Name))
            {
                if (node is GolemNode golem && golem.Entity != null && !golem.Entity.IsRemoved)
                {
                    _golems.Add(golem);
                }
            }
            if (_positions.Length != _golems.Count)
            {
                _positions = new CoreVector3[_golems.Count];
            }
            for (int i = 0; i < _golems.Count; i++)
            {
                Compat.Vector2Int cell = _golems[i].Entity.Cell;
                _positions[i] = new CoreVector3(cell.x, cell.y, 0f);
            }

            int focused = RoutingFocus.SelectNearestIndex(_player.CorePosition, _positions, FocusRange);
            Focused = focused == RoutingFocus.None ? null : _golems[focused];
            for (int i = 0; i < _golems.Count; i++)
            {
                _golems[i].ShowTiles = i == focused;
            }
        }
    }
}
