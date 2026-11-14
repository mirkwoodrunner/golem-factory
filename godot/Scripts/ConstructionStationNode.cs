using Godot;
using GolemFactory.Buildings;
using GolemFactory.Golems;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The starter golem construction station: draws the world's own
    /// <see cref="GolemConstructionStation"/> where Core put it.
    ///
    /// <para>
    /// The station is Core's real one -- it charges the chassis cost, assigns the id, and wires
    /// the golem in SpawnGolem's order -- and the world hosts the golems it builds. [E] at it is
    /// Core's PlayerInteractor, which opens the construction panel.
    /// </para>
    /// </summary>
    public partial class ConstructionStationNode : Node2D
    {
        public GolemConstructionStation Station { get; private set; }

        public override void _Ready()
        {
            Station = WorldNode.Find(this).Sandbox.StarterStation;
            if (Station == null)
            {
                GD.PushWarning("ConstructionStationNode: the setup has no starter station to draw.");
                return;
            }

            Position = GridConversions.CellToWorld(new Vector2I(Station.Cell.x, Station.Cell.y));
            var sprite = new Sprite2D { Texture = GD.Load<Texture2D>("res://art/golem_construction_station.png") };
            GridConversions.StandOnCell(sprite);
            AddChild(sprite);
        }

        /// <summary>Builds the first chassis in the roster: what <c>--demo</c> stages for a capture.</summary>
        public void BuildFirst()
        {
            if (Station != null && Station.ChassisRoster.Length > 0)
            {
                Station.TryConstructGolem(Station.ChassisRoster[0], out GolemEntity _);
            }
        }
    }
}
