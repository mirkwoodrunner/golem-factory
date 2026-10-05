using Godot;
using GolemFactory.Buildings;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.World;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// A golem construction station: draws Core's <see cref="GolemConstructionStation"/> and
    /// hosts the golems it builds.
    ///
    /// <para>
    /// The station is the real one -- it charges the chassis cost from the stockpile, assigns
    /// the id, and wires the golem in SpawnGolem's order. This node only (a) hands it the
    /// world's services, (b) gives each golem it raises through <c>GolemSpawned</c> a
    /// <see cref="GolemNode"/>, and (c) does what the slice has no UI for yet.
    /// </para>
    ///
    /// <para>
    /// SLICE SCOPE, both temporary: [E] builds the first chassis in the roster, and the new
    /// golem -- which steps out of the station door onto the station's target tile -- is then
    /// moved to <see cref="PostCell"/> and given the slice's extractor program. In the game
    /// the player carries it there with [G] (G6) and programs it at the Workbench (G7).
    /// </para>
    /// </summary>
    public partial class ConstructionStationNode : Node2D, IInteractable
    {
        [Export] public Vector2I Cell { get; set; }
        [Export] public Facing StationFacing { get; set; } = Facing.North;
        [Export] public string StockpileBufferId { get; set; } = "FactoryStockpile";
        /// <summary>Chassis names this station builds; "all" means every authored chassis.</summary>
        [Export] public string[] Roster { get; set; } = { "ClockworkScavenger" };

        /// <summary>
        /// The slice's stand-ins for the [G] carry and the Workbench: move the built golem to
        /// <see cref="PostCell"/> and give it the extractor program. On in LoopSlice.tscn only;
        /// the real Sandbox leaves the golem where the station put it.
        /// </summary>
        [Export] public bool SliceAutoProgram { get; set; }

        /// <summary>Where the slice puts the built golem to work, standing in for the [G] carry.</summary>
        [Export] public Vector2I PostCell { get; set; }
        [Export] public Facing PostFacing { get; set; } = Facing.East;

        public GolemConstructionStation Station { get; } = new GolemConstructionStation();

        /// <summary>The most recent golem this station built, or null.</summary>
        public GolemNode Built { get; private set; }

        private WorldNode _world;

        public string Prompt
        {
            get
            {
                if (Built != null)
                {
                    return "Station idle (the slice builds one golem)";
                }
                ChassisDefinition chassis = Station.ChassisRoster.Length > 0 ? Station.ChassisRoster[0] : null;
                if (chassis == null)
                {
                    return "Station has nothing to build";
                }
                string cost = UI.ConstructionCostPolicy.FormatCost(chassis.cost);
                return Station.CanAfford(chassis)
                    ? $"[E] Build a {chassis.name} ({cost})"
                    : $"Can't afford a {chassis.name} ({cost})";
            }
        }

        public override void _EnterTree() => AddToGroup(InteractableGroup.Name);

        public override void _Ready()
        {
            _world = WorldNode.Find(this);

            ChassisDefinition[] roster = System.Array.IndexOf(Roster, "all") >= 0
                ? System.Linq.Enumerable.ToArray(System.Linq.Enumerable.OrderBy(
                    _world.Definitions.Chassis.Values, c => c.maxAppendageSlots))
                : System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(
                    Roster, name => _world.Definitions.Chassis[name]));
            Station.Configure(roster, () => new GolemEntity(), _world.Conveyor, _world.Nodes,
                _world.Buffers, _world.Clock, null, StockpileBufferId);
            Station.ConfigureSpatial(_world.Endpoints, null);
            Station.SetPlacement(GridConversions.ToCore(Cell), StationFacing);
            Station.GolemSpawned += OnGolemSpawned;

            Position = GridConversions.CellToWorld(Cell);
            var sprite = new Sprite2D { Texture = GD.Load<Texture2D>("res://art/golem_construction_station.png") };
            GridConversions.StandOnCell(sprite);
            AddChild(sprite);
        }

        public override void _ExitTree() => Station.GolemSpawned -= OnGolemSpawned;

        public void Interact()
        {
            if (Built != null || Station.ChassisRoster.Length == 0)
            {
                return;
            }

            if (!Station.TryConstructGolem(Station.ChassisRoster[0], out GolemEntity golem))
            {
                return;
            }

            if (SliceAutoProgram)
            {
                // Slice stand-ins for the carry and the Workbench; see the class comment.
                golem.SetPlacement(GridConversions.ToCore(PostCell), PostFacing);
                GolemNode.ApplySliceProgram(golem.Program, SliceProgram.Extractor, _world.Definitions);
            }
        }

        private void OnGolemSpawned(GolemEntity golem)
        {
            Built = GolemNode.Host(golem);
            // Into the y-sorted entity layer beside the other standing things, not under the
            // station -- a golem is not part of the building that made it.
            GetParent().AddChild(Built);
        }
    }
}
