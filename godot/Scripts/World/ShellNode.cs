using System.Collections.Generic;
using Godot;
using GolemFactory.World;
using CoreVector2 = GolemFactory.Compat.Vector2;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The Sandbox's static shell -- walls, kerbs, posts, furniture, clutter and the wall
    /// sconces -- built at runtime from Core's <see cref="SandboxLayout"/>.
    ///
    /// <para>
    /// Unity baked ~300 GameObjects into Sandbox.unity with an editor script; here the same rules
    /// run when the scene loads, so there is no generated scene to drift from them. Pieces join
    /// the y-sorted entity layer this node is a child of, so a golem walking behind a crate is
    /// drawn behind it. Floor Expansion calls <see cref="RebuildWalls"/> after the room grows.
    /// </para>
    /// </summary>
    public partial class ShellNode : Node2D
    {
        public const string GroupName = "sandbox_shell";

        // The dramatic-lighting pass, as Unity tuned it in its third setting: ambient 0.95, a lit
        // spot about 3x the shadow between lamps.
        [Export] public float AmbientLevel { get; set; } = 0.95f;
        [Export] public float SconceEnergy { get; set; } = 1.1f;
        [Export] public Color LampColor { get; set; } = new Color(1f, 0.72f, 0.42f);

        // Unity's LampLightInset: the lit pool sits just inside the room, not at the painted
        // flame -- a sprite's height under top-down is fake elevation, and a light at the flame
        // would put its core outside the room.
        private const float LampLightInsetCells = -0.25f;
        private const float SconceOuterRadiusCells = 5.5f;

        private readonly List<Node2D> _walls = new List<Node2D>();
        private Texture2D _lightTexture;

        public int WallCount => _walls.Count;
        public int PropCount { get; private set; }
        public int SconceCount { get; private set; }

        public override void _EnterTree() => AddToGroup(GroupName);

        public override void _Ready()
        {
            YSortEnabled = true;
            // The ambient floor. Deferred: the parent is still adding its children during _Ready.
            var ambient = new CanvasModulate { Name = "Ambient", Color = new Color(AmbientLevel, AmbientLevel, AmbientLevel) };
            Callable.From(() => GetParent().AddChild(ambient)).CallDeferred();

            WorldNode world = WorldNode.Find(this);
            FloorBounds bounds = world?.Bounds ?? new FloorBounds();
            RebuildWalls(bounds.NorthExtent);

            // The back wall, its sconces and corner posts move north with a Floor Expansion.
            if (world?.Setup != null)
            {
                world.Sandbox.FloorExpansion.RowsAdded += (from, to) => RebuildWalls(bounds.NorthExtent);
            }

            foreach (LayoutPiece prop in SandboxLayout.Props())
            {
                Node2D piece = Place(prop);
                // Every prop stands on a soft contact shadow, as Unity's GroundShadow drew it.
                Sprite2D shadow = SpritePivots.Make("ground_shadow");
                shadow.Scale = new Vector2(0.95f, 0.9f);
                shadow.ZIndex = -1;
                shadow.ZAsRelative = true;
                piece.AddChild(shadow);
                piece.MoveChild(shadow, 0);
                PropCount++;
            }
        }

        /// <summary>Removes the walls and builds them again for a room whose north wall is at <paramref name="northExtent"/>.</summary>
        public void RebuildWalls(int northExtent)
        {
            foreach (Node2D wall in _walls)
            {
                // Detached now, freed later: a queued node stays in the tree until the frame
                // ends, so the new "WallNorth_0" would have been renamed out from under its own
                // name (found by the `management` scenario's Floor Expansion check).
                RemoveChild(wall);
                wall.QueueFree();
            }
            _walls.Clear();
            SconceCount = 0;

            foreach (LayoutPiece piece in SandboxLayout.Walls(northExtent))
            {
                Node2D node = Place(piece);
                if (piece.HasSconce)
                {
                    node.AddChild(MakeSconce());
                    SconceCount++;
                }
                _walls.Add(node);
            }
        }

        private Node2D Place(LayoutPiece piece)
        {
            var node = new Node2D { Name = piece.Name, Position = ToPixels(piece.Anchor), ZIndex = piece.SortingBias };
            node.AddChild(SpritePivots.Make(piece.Sprite));
            AddChild(node);
            return node;
        }

        private PointLight2D MakeSconce()
        {
            _lightTexture ??= new GradientTexture2D
            {
                Width = 256,
                Height = 256,
                Fill = GradientTexture2D.FillEnum.Radial,
                FillFrom = new Vector2(0.5f, 0.5f),
                FillTo = new Vector2(0.5f, 0f),
                Gradient = new Gradient
                {
                    Colors = new[] { new Color(1, 1, 1, 1), new Color(1, 1, 1, 0) },
                    Offsets = new[] { 0f, 1f },
                },
            };

            return new PointLight2D
            {
                Name = "SconceLight",
                Texture = _lightTexture,
                TextureScale = SconceOuterRadiusCells * 2f * GridConversions.CellPixels / 256f,
                Color = LampColor,
                Energy = SconceEnergy,
                Position = new Vector2(0f, -LampLightInsetCells * GridConversions.CellPixels),
            };
        }

        private static Vector2 ToPixels(CoreVector2 cell) =>
            new Vector2(cell.x * GridConversions.CellPixels, -cell.y * GridConversions.CellPixels);
    }
}
