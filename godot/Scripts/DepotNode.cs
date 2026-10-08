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
        [Export] public string BufferId { get; set; } = "FactoryStockpile";
        [Export] public Vector2I Cell { get; set; }

        /// <summary>Scrap in the buffer when the scene starts -- the slice seeds the price of
        /// one Scavenger, standing in for SandboxSetup's starting stock (G2d/G4).</summary>
        [Export] public int StartingScrap { get; set; }

        public StorageBuffer Buffer { get; private set; }

        public override void _Ready()
        {
            WorldNode world = WorldNode.Find(this);
            Buffer = world.Buffers.GetOrCreate(BufferId);
            if (StartingScrap > 0)
            {
                world.Buffers.Deposit(BufferId, Economy.ItemType.Scrap, StartingScrap);
            }
            world.Endpoints.Register(GridConversions.ToCore(Cell), new StorageBufferEndpoint(Buffer));

            Position = GridConversions.CellToWorld(Cell);
            var sprite = new Sprite2D { Texture = GD.Load<Texture2D>("res://art/depot.png") };
            GridConversions.StandOnCell(sprite);
            AddChild(sprite);
        }
    }
}
