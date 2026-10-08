using System.Collections.Generic;
using Godot;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// Where each environment sprite touches the floor -- Unity's import pivots
    /// (SandboxFloorGenerator.EnvironmentPivots), carried over value for value.
    ///
    /// <para>
    /// THE RULE, uniform across the set: the pivot is the piece's CONTACT LINE WITH THE FLOOR --
    /// the base of the body, excluding the contact shadow the art draws beneath it -- and that
    /// point lands exactly on the layout's anchor, so consecutive wall segments butt together.
    /// Fractions are normalized from the BOTTOM-LEFT, as Unity wrote them, and written unreduced
    /// (3/96 = "3 shadow rows out of a 96px canvas") so they stay legible against the generator.
    /// </para>
    /// </summary>
    public static class SpritePivots
    {
        private static readonly Dictionary<string, Vector2> Pivots = new Dictionary<string, Vector2>
        {
            // Head-on wall, 64x96, three shadow rows at the foot. Used on the north edge.
            { "wall_segment_nw", new Vector2(0.5f, 3f / 96f) },
            { "wall_segment_nw_lamp", new Vector2(0.5f, 3f / 96f) },
            // Side walls, 40x64: the contact line is VERTICAL, so x is pinned to the boundary
            // and y centres on the cell. West is the mirror (pixel 3 -> 37).
            { "wall_side_e", new Vector2(3f / 40f, 0.5f) },
            { "wall_side_w", new Vector2(37f / 40f, 0.5f) },
            // Skirting and kerb hang entirely BELOW their edge: pivot at the top of the canvas.
            { "floor_edge_sw", new Vector2(0.5f, 1f) },
            { "street_edge", new Vector2(0.5f, 1f) },
            { "wall_corner_post", new Vector2(0.5f, 1f / 96f) },
            { "prop_crate", new Vector2(0.5f, 1f / 56f) },
            { "prop_barrel", new Vector2(0.5f, 1f / 56f) },
            { "prop_shelf", new Vector2(0.5f, 1f / 56f) },
            { "prop_hearth", new Vector2(0.5f, 1f / 56f) },
            { "prop_workbench", new Vector2(0.5f, 1f / 56f) },
            { "prop_tool_rack", new Vector2(0.5f, 1f / 56f) },
            { "ground_shadow", new Vector2(0.5f, 0.5f) },
            // Market carts, 96x120, wider than their cell on purpose (stalls sit on a two-cell pitch).
            { "stall_scrap", new Vector2(0.5f, 3f / 120f) },
            { "stall_coal", new Vector2(0.5f, 3f / 120f) },
            { "stall_copper_ore", new Vector2(0.5f, 3f / 120f) },
            { "stall_zinc_ore", new Vector2(0.5f, 3f / 120f) },
            { "stall_aether", new Vector2(0.5f, 3f / 120f) },
        };

        /// <summary>A sprite for <paramref name="name"/> whose pivot sits at the node's origin.</summary>
        public static Sprite2D Make(string name)
        {
            var sprite = new Sprite2D { Texture = GD.Load<Texture2D>("res://art/" + name + ".png") };
            Apply(sprite, name);
            return sprite;
        }

        /// <summary>
        /// Offsets <paramref name="sprite"/> so its pivot is the node's origin. Unknown names stand
        /// on the cell's bottom edge, the BottomCenter default every building used.
        /// </summary>
        public static void Apply(Sprite2D sprite, string name)
        {
            if (!Pivots.TryGetValue(name, out Vector2 pivot))
            {
                GridConversions.StandOnCell(sprite);
                return;
            }

            sprite.Centered = false;
            Vector2 size = sprite.Texture.GetSize();
            // Bottom-left normalized (Unity) -> top-left pixel offset (Godot, y down).
            sprite.Offset = new Vector2(-pivot.X * size.X, -(1f - pivot.Y) * size.Y);
        }
    }
}
