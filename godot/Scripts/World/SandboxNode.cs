using System.Collections.Generic;
using Godot;
using GolemFactory.Buildings;
using GolemFactory.Golems;
using GolemFactory.World;

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

            // The stalls are Core markers the world already registered and published; this
            // only draws them.
            foreach (SandboxSetup.NodeEntry entry in setup.nodes)
            {
                var node = new Node2D { Name = entry.id, Position = GridConversions.CellToWorld(new Vector2I(entry.x, entry.y)) };
                node.AddChild(SpritePivots.Make(entry.sprite));
                AddChild(node);
            }

            if (setup.starterBench != null)
            {
                var node = new Node2D { Name = "StarterHandCrankBench", Position = GridConversions.CellToWorld(SandboxSetup.CellOf(setup.starterBench)) };
                node.AddChild(SpritePivots.Make("hand_crank_bench"));
                AddChild(node);
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
}
