using Godot;
using GolemFactory.World;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// (LoopSlice.tscn's hand-placed node; the real Sandbox builds its stalls in SandboxNode.)
    /// A resource node on the floor: registers a Core <see cref="ResourceNode"/> and publishes
    /// its endpoint on its cell, which is what lets a golem facing away from it ExtractFromNode.
    /// Same two calls Unity's ResourceNodeMarker made.
    /// </summary>
    public partial class NodeMarkerNode : Node2D
    {
        [Export] public string NodeId { get; set; } = "ScrapNode";
        [Export] public string ItemType { get; set; } = Economy.ItemType.Scrap;
        [Export] public Vector2I Cell { get; set; }
        [Export] public Texture2D Icon { get; set; }

        public ResourceNode Node { get; private set; }

        public override void _Ready()
        {
            WorldNode world = WorldNode.Find(this);
            Node = new ResourceNode(NodeId, ItemType);
            world.Nodes.Register(Node);
            world.Endpoints.Register(GridConversions.ToCore(Cell), new ResourceNodeEndpoint(Node));

            Position = GridConversions.CellToWorld(Cell);
            // Node identity is the sprite (CLAUDE.md: a node's tint never survives Play), so the
            // scrap node wears the scrap item, drawn large enough to read as a heap.
            AddChild(new Sprite2D { Texture = Icon, Scale = new Vector2(1.75f, 1.75f) });
        }
    }
}
