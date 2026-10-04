using System.Collections.Generic;
using Godot;
using GolemFactory.Belts;
using GolemFactory.World;
using CoreVector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// A straight run of belts, laid through Core's <see cref="BeltNetwork"/> so they link
    /// exactly as a player-placed run does in Unity, and drawn from the lane data each frame.
    ///
    /// <para>
    /// Cargo is drawn in <see cref="_Draw"/>, not as a node per item -- the same rule
    /// BeltSegmentVisual follows with its fixed sprite pool ("no GameObject per belt item").
    /// Positions come from <see cref="BeltFlowUtility"/>'s prediction plus the clock's tick
    /// fraction, the interpolation Unity's visual uses, so items glide between ticks instead of
    /// stepping ten times a second.
    /// </para>
    /// </summary>
    public partial class BeltRun : Node2D
    {
        /// <summary>First cell of the run, in Core's frame (north = +y).</summary>
        [Export] public Vector2I StartCell { get; set; }
        [Export] public int Length { get; set; } = 5;
        [Export] public Facing Facing { get; set; } = Facing.East;

        public const string GroupName = "belt_runs";

        private readonly List<PlacedBelt> _belts = new List<PlacedBelt>();
        private WorldNode _world;
        private Texture2D _beltTexture;
        private Texture2D _itemTexture;

        /// <summary>Items currently riding any belt in this run.</summary>
        public int ItemCount
        {
            get
            {
                int count = 0;
                foreach (PlacedBelt belt in _belts)
                {
                    count += belt.Segment.Items.Count;
                }
                return count;
            }
        }

        public override void _EnterTree() => AddToGroup(GroupName);

        public override void _Ready()
        {
            _world = WorldNode.Find(this);
            _beltTexture = GD.Load<Texture2D>("res://art/belt_tile.png");
            _itemTexture = GD.Load<Texture2D>("res://art/item_scrap.png");

            CoreVector2Int cell = GridConversions.ToCore(StartCell);
            CoreVector2Int step = FacingUtility.Delta(Facing);
            for (int i = 0; i < Length; i++)
            {
                if (_world.Belts.TryPlace(cell, Facing, out PlacedBelt placed))
                {
                    _belts.Add(placed);
                }
                cell += step;
            }
        }

        public override void _Process(double delta) => QueueRedraw();

        public override void _Draw()
        {
            float rotation = GridConversions.FacingToRotation(Facing);
            Vector2 half = GridConversions.FacingStep(Facing) / 2f;
            Vector2 tileHalf = _beltTexture.GetSize() / 2f;
            Vector2 itemHalf = _itemTexture.GetSize() / 2f;
            float tickFraction = _world.Clock.TickFraction;

            foreach (PlacedBelt belt in _belts)
            {
                Vector2 centre = GridConversions.CellToWorld(belt.Cell);
                DrawSetTransform(centre, rotation, Vector2.One);
                DrawTexture(_beltTexture, -tileHalf);
            }
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);

            foreach (PlacedBelt belt in _belts)
            {
                BeltSegment segment = belt.Segment;
                IReadOnlyList<ItemStack> items = segment.Items;
                Vector2 centre = GridConversions.CellToWorld(belt.Cell);
                Vector2 laneStart = centre - half;
                Vector2 laneEnd = centre + half;
                for (int i = 0; i < items.Count; i++)
                {
                    float predicted = BeltFlowUtility.PredictProgressAfterAdvance(items, i, segment.Length, 1f);
                    float display = BeltFlowUtility.ComputeDisplayProgress(items[i].Progress, predicted, tickFraction);
                    float t = Mathf.Clamp(display / segment.Length, 0f, 1f);
                    DrawTexture(_itemTexture, laneStart.Lerp(laneEnd, t) - itemHalf);
                }
            }
        }
    }
}
