using System.Text;
using Godot;
using GolemFactory.Economy;

namespace GolemFactory.Nodes
{
    /// <summary>A one-label readout: the clock, the depot, every golem's mood, and the [E] prompt.</summary>
    public partial class Hud : CanvasLayer
    {
        [Export] public NodePath PlayerPath { get; set; }
        [Export] public NodePath DepotPath { get; set; }

        private Label _label;
        private WorldNode _world;
        private PlayerNode _player;
        private DepotNode _depot;

        public override void _Ready()
        {
            _world = WorldNode.Find(this);
            _player = GetNode<PlayerNode>(PlayerPath);
            _depot = GetNode<DepotNode>(DepotPath);

            var panel = new PanelContainer { Position = new Vector2(12f, 12f) };
            _label = new Label();
            _label.AddThemeFontSizeOverride("font_size", 16);
            panel.AddChild(_label);
            AddChild(panel);
        }

        public override void _Process(double delta)
        {
            var text = new StringBuilder();
            text.Append("Tick ").Append(_world.Clock.CurrentTick)
                .Append(" · Depot Scrap: ").Append(_depot.Buffer.GetQuantity(ItemType.Scrap));
            foreach (Node node in GetTree().GetNodesInGroup(GolemNodeGroup.Name))
            {
                if (node is GolemNode golem && golem.Entity != null)
                {
                    text.Append('\n').Append(golem.GolemId).Append(": ").Append(golem.Entity.Mood)
                        .Append(", ").Append(golem.CompletedCycles).Append(" cycles");
                }
            }
            text.Append('\n').Append(_player.Focus != null ? _player.Focus.Prompt : "WASD to move · E to interact");
            _label.Text = text.ToString();
        }
    }

    public static class GolemNodeGroup
    {
        public const string Name = "golems";
    }
}
