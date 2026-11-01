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

        // Every lighting number is Core's LightingRules (art-pipeline-design.md §6).

        private readonly List<Node2D> _walls = new List<Node2D>();

        public int WallCount => _walls.Count;

        public IReadOnlyList<Node2D> Walls => _walls;
        public int PropCount { get; private set; }
        public int SconceCount { get; private set; }

        /// <summary>The room's tint, for the `world` scenario to compare with the rules.</summary>
        public CanvasModulate Ambient { get; private set; }

        public override void _EnterTree() => AddToGroup(GroupName);

        public override void _Ready()
        {
            YSortEnabled = true;
            // The ambient floor. Deferred: the parent is still adding its children during _Ready.
            var ambient = new CanvasModulate { Name = "Ambient", Color = Ugui.ToGodot(LightingRules.Ambient) };
            Ambient = ambient;
            Callable.From(() => GetParent().AddChild(ambient)).CallDeferred();

            WorldNode world = WorldNode.Find(this);
            FloorBounds bounds = world?.Bounds ?? new FloorBounds();
            RebuildWalls(bounds.NorthExtent);

            // The back wall, its sconces and corner posts move north with a Floor Expansion.
            if (world?.Setup != null)
            {
                world.Sandbox.FloorExpansion.RowsAdded += (from, to) => RebuildWalls(bounds.NorthExtent);
                world.Sandbox.FloorExpansion.RowsRemoved += (from, to) => RebuildWalls(bounds.NorthExtent);
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

        // The painted flame needs nothing of its own: it sits inside its own lamp's pool, which
        // lights it above the ambient.
        private static PointLight2D MakeSconce() => new PointLight2D
        {
            Name = "SconceLight",
            Texture = LightTexture.Banded(),
            TextureScale = LightTexture.ScaleFor(LightingRules.SconceRadiusCells),
            Color = Ugui.ToGodot(LightingRules.SconceColor),
            Energy = LightingRules.SconceEnergy,
            Position = new Vector2(0f, -LightingRules.SconceInsetCells * GridConversions.CellPixels),
        };

        private static Vector2 ToPixels(CoreVector2 cell) =>
            new Vector2(cell.x * GridConversions.CellPixels, -cell.y * GridConversions.CellPixels);
    }
}
