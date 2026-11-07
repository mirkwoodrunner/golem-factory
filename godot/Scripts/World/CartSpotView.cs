using Godot;
using GolemFactory.Supply;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// One supply-cart spot on the street, drawn (docs/supply-design.md §1): the roped-off spot,
    /// or the cart standing on it in its good's market-cart art. It stands like a stall, its feet
    /// on its node's origin, so the stalls' y-sort and pivots hold. It only reads Core's spot and
    /// swaps the picture when the cart comes, goes or changes its good.
    /// </summary>
    public partial class CartSpotView : Node2D
    {
        private readonly CartSpot _spot;
        private Sprite2D _sprite;

        /// <summary>The sprite name showing, for scenarios.</summary>
        public string Shown { get; private set; }

        public CartSpot Spot => _spot;

        public CartSpotView() { } // Godot needs a parameterless constructor for every node type.

        public CartSpotView(CartSpot spot) => _spot = spot;

        public override void _Ready()
        {
            Shown = SupplyArt.SpriteFor(_spot);
            _sprite = SpritePivots.Make(Shown);
            AddChild(_sprite);
        }

        public override void _Process(double delta)
        {
            string name = SupplyArt.SpriteFor(_spot);
            if (name == Shown)
            {
                return;
            }
            Shown = name;
            _sprite.Texture = GD.Load<Texture2D>("res://art/" + name + ".png");
            SpritePivots.Apply(_sprite, name); // every cart shares the spot's 96x120 canvas and pivot
        }
    }
}
