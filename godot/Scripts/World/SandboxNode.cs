using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.Buildings;
using GolemFactory.World;
using CoreVector2 = GolemFactory.Compat.Vector2;
using CoreVector3 = GolemFactory.Compat.Vector3;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// Populates the world from the applied <see cref="SandboxSetup"/>: the market stalls and the
    /// starter Hand-Crank Bench -- what Unity authored into Sandbox.unity by hand and wired in
    /// SandboxBootstrap.Start.
    ///
    /// <para>
    /// Each stall is a Core <see cref="ResourceNodeMarker"/> publishing its node on its cell,
    /// which is what lets a golem standing beside it ExtractFromNode; node identity is the cart
    /// SPRITE (root CLAUDE.md -- a tint never survives Play). The bench is a real building with
    /// a <see cref="HandCrankBench"/> part, configured with every recipe (it filters to the
    /// crankable ones itself) and ticked by the clock. (The starter construction station is its
    /// own node in the scene, which already hosts the golems it builds.)
    /// </para>
    /// </summary>
    public partial class SandboxNode : Node2D
    {
        public const string GroupName = "sandbox_world";

        private static readonly GridCoordinateConverter UnitCells = new GridCoordinateConverter(new CoreVector2(1f, 1f));

        public List<ResourceNodeMarker> Markers { get; } = new List<ResourceNodeMarker>();
        public List<PlaceableBuilding> Buildings { get; } = new List<PlaceableBuilding>();
        public HandCrankBench StarterBench { get; private set; }

        public override void _EnterTree() => AddToGroup(GroupName);

        public override void _Ready()
        {
            WorldNode world = WorldNode.Find(this);
            SandboxSetup setup = world.Setup;
            if (setup == null)
            {
                GD.PushWarning("SandboxNode: WorldNode.ApplySandboxSetup is off, so there is nothing to populate.");
                return;
            }

            foreach (SandboxSetup.NodeEntry entry in setup.nodes)
            {
                var marker = new ResourceNodeMarker { Position = new CoreVector3(entry.x, entry.y, 0f) };
                marker.Configure(world.Nodes, entry.id);
                marker.RegisterAsSpatialEndpoint(world.Endpoints, UnitCells);
                Markers.Add(marker);

                var node = new Node2D { Name = entry.id, Position = GridConversions.CellToWorld(new Vector2I(entry.x, entry.y)) };
                node.AddChild(SpritePivots.Make(entry.sprite));
                AddChild(node);
            }

            if (setup.starterBench != null)
            {
                var building = new PlaceableBuilding { name = "StarterHandCrankBench", Cell = SandboxSetup.CellOf(setup.starterBench) };
                StarterBench = building.AddPart(new HandCrankBench());
                StarterBench.Configure(world.Buffers, setup.stockpileBufferId, world.Definitions.Recipes.Values.OrderBy(r => r.name));
                world.Clock.Register(StarterBench);
                Buildings.Add(building);

                var node = new Node2D { Name = building.name, Position = GridConversions.CellToWorld(building.Cell) };
                node.AddChild(SpritePivots.Make("hand_crank_bench"));
                AddChild(node);
            }
        }
    }
}
