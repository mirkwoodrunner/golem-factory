using System.Collections.Generic;
using GolemFactory.Compat;

namespace GolemFactory.World
{
    /// <summary>One static piece of the Sandbox's shell: a wall segment, a post, or a prop.</summary>
    public readonly struct LayoutPiece
    {
        /// <summary>Unity's GameObject name for it ("WallNorth_4", "Prop_-6_12"), kept for parity.</summary>
        public readonly string Name;

        /// <summary>The art file's base name, e.g. "wall_segment_nw_lamp".</summary>
        public readonly string Sprite;

        /// <summary>Where the piece's contact line sits, in cell coordinates (cell centres are integers).</summary>
        public readonly Vector2 Anchor;

        /// <summary>Draw-order nudge for pieces sharing a row (a post over the wall it caps).</summary>
        public readonly int SortingBias;

        /// <summary>A lamp-lit north segment, which carries a sconce light.</summary>
        public readonly bool HasSconce;

        /// <summary>A prop, which stands on a soft contact shadow.</summary>
        public readonly bool HasShadow;

        public LayoutPiece(string name, string sprite, Vector2 anchor, int sortingBias = 0,
            bool hasSconce = false, bool hasShadow = false)
        {
            Name = name;
            Sprite = sprite;
            Anchor = anchor;
            SortingBias = sortingBias;
            HasSconce = hasSconce;
            HasShadow = hasShadow;
        }
    }

    /// <summary>
    /// The Sandbox's static shell -- walls, kerbs, posts, furniture and clutter -- as data.
    ///
    /// <para>
    /// PORTED FROM Unity's Editor/SandboxFloorGenerator (milestone G4), which baked these into
    /// Sandbox.unity as ~300 GameObjects. The rules are its rules, reasoning kept; what changed
    /// is that they are now a pure function the scene builds from at runtime, so there is no
    /// generated scene file to drift from them.
    /// </para>
    /// </summary>
    public static class SandboxLayout
    {
        /// <summary>A sconce on every fourth north segment.</summary>
        public const int LampSpacing = 4;

        /// <summary>
        /// Which cobble a street cell gets: an alternation on a coprime-ish stride, so the two
        /// variants interleave without banding. Deliberately NOT FloorTileVariant: that hashes for
        /// rare accent tiles (brass plate, grate), which are workshop fittings and would read as
        /// debris in the road.
        /// </summary>
        public static int StreetTileVariant(Vector2Int cell, int variantCount = 2) =>
            Mod(cell.x * 5 + cell.y * 3, variantCount);

        /// <summary>
        /// The walls for a room whose north wall stands at <paramref name="northExtent"/> --
        /// FloorLayout's default, or wherever Floor Expansion has pushed it.
        ///
        /// <para>
        /// WHICH EDGES ARE WALLED IS A TOP-DOWN DECISION: three walls and the south edge left open
        /// so the camera can see in. North gets the head-on face; east and west get the side caps;
        /// south keeps the skirting slab that gives the floor a visible thickness. THE SIDE RUNS
        /// FOLLOW THE WORLD, THE SKIRTING FOLLOWS THE ROOM: the side walls run the world's full
        /// height (down the street too), while the skirting stops where the plank deck does.
        /// </para>
        /// </summary>
        public static List<LayoutPiece> Walls(int northExtent = FloorLayout.DefaultNorthExtent)
        {
            int he = FloorLayout.HalfExtent;
            int depth = FloorLayout.StreetDepth;
            var pieces = new List<LayoutPiece>();

            foreach (int index in FloorLayout.GetWorldEdgeIndices(he, depth, northExtent))
            {
                pieces.Add(new LayoutPiece("WallEast_" + index, "wall_side_e",
                    FloorLayout.GetWorldEdgeAnchor(FloorLayout.Edge.East, index, he, depth, northExtent)));
                pieces.Add(new LayoutPiece("WallWest_" + index, "wall_side_w",
                    FloorLayout.GetWorldEdgeAnchor(FloorLayout.Edge.West, index, he, depth, northExtent)));
            }

            foreach (int index in FloorLayout.GetEdgeIndices(he))
            {
                bool lit = Mod(index, LampSpacing) == 0;
                pieces.Add(new LayoutPiece("WallNorth_" + index, lit ? "wall_segment_nw_lamp" : "wall_segment_nw",
                    FloorLayout.GetEdgeAnchor(FloorLayout.Edge.North, index, he, northExtent), hasSconce: lit));
                pieces.Add(new LayoutPiece("SkirtSouth_" + index, "floor_edge_sw",
                    FloorLayout.GetEdgeAnchor(FloorLayout.Edge.South, index, he, northExtent)));
            }

            // The far kerb runs the STREET's width, not the room's: the road is wider than the
            // building, and reusing the skirting's loop would leave its outer cells unedged.
            foreach (int index in FloorLayout.GetStreetEdgeIndices())
            {
                pieces.Add(new LayoutPiece("KerbStreet_" + index, "street_edge",
                    FloorLayout.GetWorldEdgeAnchor(FloorLayout.Edge.South, index, he, depth, northExtent)));
            }

            // The shoulders: the building's south face where it is no longer the world's edge.
            // Head-on wall art, because from the street these are the flanks of the shop.
            int shoulder = 0;
            foreach (Vector2 anchor in FloorLayout.GetShoulderAnchors())
            {
                pieces.Add(new LayoutPiece("WallShoulder_" + shoulder++, "wall_segment_nw", anchor));
            }

            // +1, because a post shares its exact row with the north segments it caps and overlaps
            // them; equal draw orders have no defined winner, so the corner would flicker.
            int post = 0;
            foreach (Vector2 anchor in FloorLayout.GetWallPostAnchors(he, northExtent))
            {
                pieces.Add(new LayoutPiece("WallPost_" + post++, "wall_corner_post", anchor, sortingBias: 1));
            }

            return pieces;
        }

        /// <summary>
        /// Furniture on the north wall plus deterministic clutter along the edges and the
        /// street's far end. Every piece stands on the wall ring or the street's last rows, never
        /// on playable floor and never on a stall's approach tiles.
        /// </summary>
        public static List<LayoutPiece> Props()
        {
            int he = FloorLayout.HalfExtent;
            var pieces = new List<LayoutPiece>();
            var furnished = new HashSet<Vector2Int>();

            // --- THE BACK WALL IS FURNITURE, NOT CLUTTER --------------------------------------
            // Authored offsets from the room's centre line, so the arrangement stays centred if
            // the workshop is ever widened. Furniture should look ARRANGED, which a hash cannot do.
            (int offset, string sprite)[] furniture =
            {
                (0, "prop_hearth"),
                (-2, "prop_tool_rack"),
                (2, "prop_tool_rack"),
                (-4, "prop_workbench"),
                (4, "prop_shelf"),
                (-6, "prop_shelf"),
                (6, "prop_workbench"),
            };
            foreach ((int offset, string sprite) in furniture)
            {
                var cell = new Vector2Int(offset, he);
                // Skipped rather than clamped if the room ever shrinks: two pieces stacked on the
                // last cell is worse than one piece missing.
                if (cell.x <= -he || cell.x >= he)
                {
                    continue;
                }
                pieces.Add(Prop(cell, sprite));
                furnished.Add(cell);
            }

            // Deterministic, irregular-looking clutter hugging all four edges, each run on a
            // different phase so the room never looks mirrored. Determinism matters: a random
            // scatter would rearrange the room on every load.
            for (int i = -he + 1; i <= he - 1; i++)
            {
                if (Mod(i * 3, 7) < 2)
                {
                    pieces.Add(Prop(new Vector2Int(he, i), Mod(i, 2) == 0 ? "prop_crate" : "prop_barrel"));
                }
                // The north run yields to the furniture.
                if (Mod(i * 5 + 2, 7) < 2 && !furnished.Contains(new Vector2Int(i, he)))
                {
                    pieces.Add(Prop(new Vector2Int(i, he), Mod(i, 2) == 0 ? "prop_barrel" : "prop_crate"));
                }
                if (Mod(i * 3 + 4, 9) < 2)
                {
                    pieces.Add(Prop(new Vector2Int(-he, i), Mod(i, 2) == 0 ? "prop_crate" : "prop_barrel"));
                }
                if (Mod(i * 7 + 1, 9) < 2)
                {
                    pieces.Add(Prop(new Vector2Int(i, -he), Mod(i, 2) == 0 ? "prop_barrel" : "prop_crate"));
                }
            }

            // STREET CLUTTER, confined to the two southernmost rows and the side walls beside
            // them: the part of the road provably clear of the stalls, which stand four rows
            // north, so their approach tiles stay open for §3.2's extractor cap.
            int kerbRow = -he - FloorLayout.StreetDepth;
            for (int x = -he + 1; x <= he - 1; x++)
            {
                if (Mod(x * 5 + 3, 8) < 2)
                {
                    pieces.Add(Prop(new Vector2Int(x, kerbRow), Mod(x, 2) == 0 ? "prop_barrel" : "prop_crate"));
                }
                if (Mod(x * 3 + 5, 11) < 2)
                {
                    pieces.Add(Prop(new Vector2Int(x, kerbRow + 1), Mod(x, 2) == 0 ? "prop_crate" : "prop_barrel"));
                }
            }

            for (int y = kerbRow; y <= kerbRow + 2; y++)
            {
                if (Mod(y * 5 + 1, 3) < 2)
                {
                    pieces.Add(Prop(new Vector2Int(he, y), Mod(y, 2) == 0 ? "prop_crate" : "prop_barrel"));
                }
                if (Mod(y * 7 + 3, 3) < 2)
                {
                    pieces.Add(Prop(new Vector2Int(-he, y), Mod(y, 2) == 0 ? "prop_barrel" : "prop_crate"));
                }
            }

            return pieces;
        }

        private static LayoutPiece Prop(Vector2Int cell, string sprite) =>
            new LayoutPiece("Prop_" + cell.x + "_" + cell.y, sprite, new Vector2(cell.x, cell.y), hasShadow: true);

        private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;
    }
}
