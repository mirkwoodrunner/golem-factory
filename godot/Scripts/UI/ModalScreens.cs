using Godot;
using GolemFactory.UI;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The full screens (construction panel now; the Workbench in G7 and Management in G8). A
    /// screen node joins <see cref="GroupName"/> and implements <see cref="IScreen"/>; anything
    /// that must yield to a screen -- the player's movement and keys, the world prompt -- asks
    /// <see cref="AnyOpen"/> instead of knowing every screen by name.
    /// </summary>
    public static class ModalScreens
    {
        public const string GroupName = "modal_screens";

        public static bool AnyOpen(SceneTree tree)
        {
            foreach (Node node in tree.GetNodesInGroup(GroupName))
            {
                if (node is IScreen screen && screen.IsOpen)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
