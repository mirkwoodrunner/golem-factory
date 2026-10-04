using Godot;
using GolemFactory.Economy;
using GolemFactory.World;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// An unlabelled depot: a <see cref="StorageBuffer"/> published on its cell as a
    /// <see cref="StorageBufferEndpoint"/>, the endpoint Unity's PlaceableDepot publishes when it
    /// has no filter.
    /// </summary>
    public partial class DepotNode : Node2D
    {
        [Export] public string BufferId { get; set; } = "Depot";
        [Export] public Vector2I Cell { get; set; }

        public StorageBuffer Buffer { get; private set; }

        public override void _Ready()
        {
            WorldNode world = WorldNode.Find(this);
            Buffer = world.Buffers.GetOrCreate(BufferId);
            world.Endpoints.Register(GridConversions.ToCore(Cell), new StorageBufferEndpoint(Buffer));

            Position = GridConversions.CellToWorld(Cell);
            var sprite = new Sprite2D { Texture = GD.Load<Texture2D>("res://art/depot.png") };
            GridConversions.StandOnCell(sprite);
            AddChild(sprite);
        }
    }
}
