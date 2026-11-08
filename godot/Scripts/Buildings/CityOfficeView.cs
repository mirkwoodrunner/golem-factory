using Godot;
using GolemFactory.Contracts;
using GolemFactory.World;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The City Office in the town square (docs/contracts-design.md §1): roped off until its first
    /// contract posts, then open. A good the contract counts pops out over its door, and a contract
    /// done glints (§5, Phase 6's existing effects). Its feet are the footprint's south edge, on the
    /// node's origin, so it y-sorts with the tower and the golems around it. It only draws: the
    /// board is Core's (<see cref="SandboxWorld.Contracts"/>).
    /// </summary>
    public partial class CityOfficeView : Node2D
    {
        public const string OpenSprite = "res://art/city_office.png";
        public const string ClosedSprite = "res://art/city_office_closed.png";
        public const int CanvasWidth = 192, FootRow = 173;

        private readonly ContractBoard _board;
        private readonly FxLayer _fx;
        private Sprite2D _sprite;
        private bool? _open;
        private int _delivered;
        private int _done;
        private float _sincePop = 99f;

        public CityOfficeView(ContractBoard board, FxLayer fx)
        {
            _board = board;
            _fx = fx;
            Name = "CityOffice";
            Position = GridConversions.CellToWorld(TownSquare.OfficeCell) + new Vector2(0f, GridConversions.CellPixels / 2f);
        }

        /// <summary>The picture it wears, for a scenario: the open office or the roped-off one.</summary>
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
            _done = _board?.Done.Count ?? 0;
            _delivered = Delivered();
            Refresh();
        }

        public override void _Process(double delta)
        {
            _sincePop += (float)delta;
            Refresh();
        }

        private int Delivered()
        {
            if (_board?.Current == null)
            {
                return 0;
            }
            int n = 0;
            foreach (PunchCards.RecipeIngredient ask in _board.Current.asks)
            {
                n += _board.Delivered(ask.itemType);
            }
            return n;
        }

        private void Refresh()
        {
            bool open = _board != null && _board.IsOpen;
            if (_open != open)
            {
                _open = open;
                _sprite.Texture = GD.Load<Texture2D>(SpritePath);
            }
            if (_board == null || _fx == null)
            {
                return;
            }
            // Over the door: the plate above it, in world pixels from the feet.
            Vector2 door = GlobalPosition + new Vector2(0f, -88f);
            int delivered = Delivered();
            // As a crate's lid pops, at most one every CratePopMinInterval, however fast goods land.
            if (delivered > _delivered && _board.Current != null && _sincePop >= FeelRules.CratePopMinInterval)
            {
                _sincePop = 0f;
                string good = _board.Current.asks.Find(a => _board.Delivered(a.itemType) > 0).itemType;
                _fx.Spawn(FxKind.GoodPop, door, ItemIcons.Load(good));
            }
            _delivered = delivered;
            if (_board.Done.Count > _done)
            {
                _fx.Spawn(FxKind.Glint, door + new Vector2(0f, -40f));
            }
            _done = _board.Done.Count;
        }
    }
}
