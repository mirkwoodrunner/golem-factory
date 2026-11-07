using System.Collections.Generic;
using Godot;
using GolemFactory.Buildings;
using GolemFactory.Golems;
using GolemFactory.World;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// Populates the world from the applied <see cref="SandboxSetup"/>: the market street's cart
    /// spots and the starter Hand-Crank Bench.
    ///
    /// <para>
    /// Each cart is a Core <see cref="ResourceNodeMarker"/> publishing its node on its cell,
    /// which is what lets a golem standing beside it ExtractFromNode; its good is the cart
    /// SPRITE (root CLAUDE.md -- a tint never survives Play). The bench is a real building with
    /// a <see cref="HandCrankBench"/>, configured with every recipe (it filters to the crankable
    /// ones itself) and ticked by the clock. Since G5 all of that is built by Core's
    /// SandboxWorld; this node draws it and hosts the golems the world's stations build.
    /// </para>
    /// </summary>
    public partial class SandboxNode : Node2D
    {
        public const string GroupName = "sandbox_world";

        public IReadOnlyList<ResourceNodeMarker> Markers => _world?.Sandbox.Markers ?? new List<ResourceNodeMarker>();
        public HandCrankBench StarterBench => _world?.Sandbox.StarterBench;

        private WorldNode _world;
        private readonly List<CartSpotView> _spotViews = new List<CartSpotView>();

        /// <summary>The supply street's spots as drawn, in the street's order.</summary>
        public IReadOnlyList<CartSpotView> SpotViews => _spotViews;

        public override void _EnterTree() => AddToGroup(GroupName);

        public override void _Ready()
        {
            _world = WorldNode.Find(this);
            SandboxSetup setup = _world.Setup;
            if (setup == null)
            {
                GD.PushWarning("SandboxNode: WorldNode.ApplySandboxSetup is off, so there is nothing to populate.");
                return;
            }

            // The supply street's spots (docs/supply-design.md §1), each drawn as its rope or its
            // cart. Kept by spot, never found by name.
            foreach (Supply.CartSpot spot in _world.Sandbox.Supply.Spots)
            {
                var view = new CartSpotView(spot)
                {
                    Name = "CartSpot",
                    Position = GridConversions.CellToWorld(new Vector2I(spot.Cell.x, spot.Cell.y)),
                };
                AddChild(view);
                _spotViews.Add(view);
            }

            if (setup.starterBench != null)
            {
                AddChild(new StarterBenchView(_world.Sandbox.StarterBench)
                {
                    Name = "StarterHandCrankBench",
                    Position = GridConversions.CellToWorld(SandboxSetup.CellOf(setup.starterBench)),
                });
            }

            // Every golem any station builds -- the starter one, or one the player placed --
            // gets a node in the y-sorted entity layer. A dismantled golem's node frees itself.
            _world.Sandbox.GolemSpawned += OnGolemSpawned;
        }

        public override void _ExitTree()
        {
            if (_world?.Sandbox != null)
            {
                _world.Sandbox.GolemSpawned -= OnGolemSpawned;
            }
        }

        private void OnGolemSpawned(GolemEntity golem) => GetParent().AddChild(GolemNode.Host(golem));
    }

    /// <summary>The starter Hand-Crank Bench, drawn: its wheel turns while it makes progress.</summary>
    public partial class StarterBenchView : Node2D
    {
        private const string BaseSprite = "hand_crank_bench";

        private readonly HandCrankBench _bench;
        private readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();
        private Sprite2D _sprite;
        private string _shown;

        /// <summary>The picture showing: the bench, or one of its turning frames.</summary>
        public string Shown => _shown;

        public StarterBenchView() { } // Godot needs a parameterless constructor for every node type.

        public StarterBenchView(HandCrankBench bench) => _bench = bench;

        public override void _Ready()
        {
            _sprite = SpritePivots.Make(BaseSprite);
            _shown = BaseSprite;
            AddChild(_sprite);
        }

        public override void _Process(double delta)
        {
            string name = _bench?.SpriteFor(BaseSprite) ?? BaseSprite;
            if (name == _shown)
            {
                return;
            }
            _shown = name;
            if (!_textures.TryGetValue(name, out Texture2D texture))
            {
                _textures[name] = texture = GD.Load<Texture2D>("res://art/" + name + ".png");
            }
            _sprite.Texture = texture; // every turning frame shares the bench's canvas and pivot
        }
    }
}
