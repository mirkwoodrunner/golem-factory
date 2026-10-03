using Godot;
using GolemFactory.World;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The golem construction station, cut down to the one verb the slice needs: [E] builds the
    /// extractor golem at <see cref="SpawnCell"/>.
    ///
    /// <para>
    /// SPIKE SCOPE: it builds once and charges nothing. Unity's station spends the chassis cost
    /// through StorageBufferRegistry.TryWithdrawScrapAndBrass (already ported, in Core) and
    /// retargets the Workbench; with no Workbench and no starting stockpile in this scene, a
    /// cost would only make the slice unplayable without proving anything new.
    /// </para>
    /// </summary>
    public partial class ConstructionStationNode : Node2D, IInteractable
    {
        [Export] public Vector2I Cell { get; set; }
        [Export] public Vector2I SpawnCell { get; set; }
        [Export] public Facing SpawnFacing { get; set; } = Facing.East;
        [Export] public string GolemId { get; set; } = "PlayerGolem-001";

        public GolemNode Built { get; private set; }

        public string Prompt => Built == null ? "[E] Build a Scavenger" : "Station idle (spike builds one golem)";

        public override void _EnterTree() => AddToGroup(InteractableGroup.Name);

        public override void _Ready()
        {
            Position = GridConversions.CellToWorld(Cell);
            var sprite = new Sprite2D { Texture = GD.Load<Texture2D>("res://art/golem_construction_station.png") };
            GridConversions.StandOnCell(sprite);
            AddChild(sprite);
        }

        public void Interact()
        {
            if (Built != null)
            {
                return;
            }

            Built = new GolemNode
            {
                Name = GolemId,
                GolemId = GolemId,
                Cell = SpawnCell,
                Facing = SpawnFacing,
                ProgramKind = SliceProgram.Extractor,
            };
            // Into the y-sorted entity layer, beside the other standing things, not under the
            // station -- a golem is not part of the building that made it.
            GetParent().AddChild(Built);
        }
    }
}
