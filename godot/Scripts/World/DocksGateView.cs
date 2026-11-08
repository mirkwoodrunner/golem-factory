using Godot;
using GolemFactory.World;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The Docks' gate at the street's end (docs/region-design.md §1): roped across until the
    /// first Zeppelin, then open. Drawn only: it is never a fixture, so belts and pipes run through
    /// it down the lane. Its feet are its cell's south edge, as standing art's are.
    /// </summary>
    public partial class DocksGateView : Node2D
    {
        public const string OpenSprite = "docks_gate";
        public const string ClosedSprite = "docks_gate_closed";

        private readonly FloorBounds _bounds;
        private Sprite2D _sprite;
        private bool? _open;

        public DocksGateView() { } // Godot needs a parameterless constructor for every node type.

        public DocksGateView(FloorBounds bounds)
        {
            _bounds = bounds;
            Position = GridConversions.CellToWorld(Docks.GateCell);
        }

        /// <summary>The picture it wears, for a scenario.</summary>
        public string Shown => _open == true ? OpenSprite : ClosedSprite;

        public override void _Ready()
        {
            _sprite = SpritePivots.Make(ClosedSprite);
            AddChild(_sprite);
            Refresh();
        }

        public override void _Process(double delta) => Refresh();

        private void Refresh()
        {
            bool open = _bounds != null && _bounds.DocksOpen;
            if (_open == open)
            {
                return;
            }
            _open = open;
            _sprite.Texture = GD.Load<Texture2D>("res://art/" + Shown + ".png");
            SpritePivots.Apply(_sprite, Shown);
        }
    }
}
