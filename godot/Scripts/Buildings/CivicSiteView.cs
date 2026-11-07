using Godot;
using GolemFactory.Civic;
using GolemFactory.World;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The civic works' site in the town square (docs/endless-design.md §1.2): roped off until the
    /// Clock Tower stands, then open. Each stage built glints (Phase 6's existing effect). One
    /// placeholder picture for every work and stage until their art is commissioned. Its feet are
    /// the footprint's south edge, on the node's origin, as the tower's are.
    /// </summary>
    public partial class CivicSiteView : Node2D
    {
        public const string OpenSprite = "res://art/civic_site.png";
        public const string ClosedSprite = "res://art/civic_site_closed.png";
        public const int CanvasWidth = 192, FootRow = 197;

        private readonly CivicWorks _civic;
        private readonly FxLayer _fx;
        private Sprite2D _sprite;
        private bool? _open;
        private int _built;

        public CivicSiteView(CivicWorks civic, FxLayer fx)
        {
            _civic = civic;
            _fx = fx;
            Name = "CivicSite";
            Position = GridConversions.FootprintFeet(TownSquare.CivicCentre, TownSquare.TowerSize);
        }

        /// <summary>The picture it wears, for a scenario.</summary>
        public string SpritePath => _open == true ? OpenSprite : ClosedSprite;

        public override void _Ready()
        {
            _sprite = new Sprite2D
            {
                Centered = false,
                Offset = new Vector2(-CanvasWidth / 2f, -FootRow),
                TextureFilter = TextureFilterEnum.Nearest,
            };
            AddChild(_sprite);
            _built = _civic?.StagesBuilt ?? 0;
            Refresh();
        }

        public override void _Process(double delta) => Refresh();

        private void Refresh()
        {
            bool open = _civic != null && _civic.IsOpen;
            if (_open != open)
            {
                _open = open;
                _sprite.Texture = GD.Load<Texture2D>(SpritePath);
            }
            int built = _civic?.StagesBuilt ?? 0;
            if (built > _built && _fx != null)
            {
                _fx.Spawn(FxKind.Glint, GlobalPosition + new Vector2(0f, -150f));
            }
            _built = built;
        }
    }
}
