using System.Collections.Generic;
using GolemFactory.Compat;

namespace GolemFactory.UI
{
    /// <summary>
    /// One world-space HUD label asking for a place to sit: where its owner stands, and who is
    /// asking. The owner key breaks ties, so the layout cannot depend on the order the badges
    /// happened to register in.
    /// </summary>
    public readonly struct WorldHudRequest
    {
        public readonly Vector3 Anchor;
        public readonly string OwnerId;

        /// <summary>
        /// Half the label's drawn width, in cells, or 0 when unknown. A label that says how
        /// wide it is is also kept off neighbours in OTHER columns (Phase 2 critique: two red
        /// badges on golems two cells apart, each wider than two cells, overlapped).
        /// </summary>
        public readonly float HalfWidth;

        /// <summary>
        /// The label's drawn height, in cells, or 0 for "one row". Zoomed out, a badge is taller
        /// than <see cref="WorldHudLayout.RowHeight"/>, and a climb of one row left it overlapping.
        /// </summary>
        public readonly float Height;

        public WorldHudRequest(Vector3 anchor, string ownerId, float halfWidth = 0f, float height = 0f)
        {
            Anchor = anchor;
            OwnerId = ownerId ?? "";
            HalfWidth = halfWidth < 0f ? 0f : halfWidth;
            Height = height < 0f ? 0f : height;
        }
    }

    /// <summary>
    /// Keeps world-space labels off each other: stall badges and the interaction caption both
    /// anchor to whatever they describe, and two golems standing a cell apart put their badges
    /// in the same place.
    ///
    /// <para>
    /// <b>Stacked, not scattered.</b> Colliding labels are pushed straight up in a column rather
    /// than nudged apart in a circle: a column keeps every label horizontally over the thing it
    /// describes, which is the whole of what makes it readable at a glance, and a radial nudge
    /// puts a badge over a NEIGHBOUR at exactly the moment the neighbour also has something to
    /// say. The badge furthest south stays put and the ones behind it climb, because the
    /// nearest thing to the camera is the one the player is most likely acting on.
    /// </para>
    ///
    /// <para>
    /// <b>Deterministic and single-pass.</b> Labels are grouped by rounded column and ordered by
    /// world Y then owner id -- no iterative relaxation, no dependence on registration order, and
    /// the same factory laid out twice reads identically. An iterative solver would also settle
    /// differently frame to frame as golems moved, which is a badge that shivers.
    /// </para>
    ///
    /// <para>
    /// Pure and engine-light, like <c>YSortUtility</c> and <c>GridCoordinateConverter</c>: the
    /// views hand it their anchors and apply what comes back.
    /// </para>
    /// </summary>
    public static class WorldHudLayout
    {
        /// <summary>
        /// How close two anchors must be horizontally before they are treated as the same
        /// column. Just under a cell: two golems on ADJACENT cells overlap (a badge is wider
        /// than the golem under it), two a cell apart do not.
        /// </summary>
        public const float ColumnWidth = 0.9f;

        /// <summary>How far a displaced label climbs. Roughly one caption's height.</summary>
        public const float RowHeight = 0.42f;

        /// <summary>
        /// Resolved positions for every request, in the order they were given. A label with no
        /// neighbour sits exactly on its anchor, so the common case is untouched.
        /// </summary>
        public static Vector3[] Resolve(IReadOnlyList<WorldHudRequest> requests) =>
            Resolve(requests, ColumnWidth, RowHeight);

        public static Vector3[] Resolve(
            IReadOnlyList<WorldHudRequest> requests, float columnWidth, float rowHeight)
        {
            if (requests == null || requests.Count == 0)
            {
                return new Vector3[0];
            }

            var resolved = new Vector3[requests.Count];
            for (int i = 0; i < requests.Count; i++)
            {
                resolved[i] = requests[i].Anchor;
            }

            // Bucket by column. A rounded X rather than a distance test, because a chain of
            // labels each just inside the threshold of the next has no "correct" pairing --
            // bucketing gives every member of a crowd the same answer about who it is crowding.
            var columns = new Dictionary<int, List<int>>();
            float width = columnWidth <= 0f ? ColumnWidth : columnWidth;
            for (int i = 0; i < requests.Count; i++)
            {
                int column = Mathf.RoundToInt(requests[i].Anchor.x / width);
                if (!columns.TryGetValue(column, out List<int> members))
                {
                    members = new List<int>();
                    columns[column] = members;
                }

                members.Add(i);
            }

            foreach (KeyValuePair<int, List<int>> column in columns)
            {
                List<int> members = column.Value;
                if (members.Count < 2)
                {
                    continue;
                }

                // South first, then owner id. Sorting by Y alone leaves two golems on the same
                // row to be ordered by whoever registered first, which is exactly the
                // frame-to-frame flicker this exists to prevent.
                members.Sort((a, b) =>
                {
                    int byY = requests[a].Anchor.y.CompareTo(requests[b].Anchor.y);
                    return byY != 0
                        ? byY
                        : string.CompareOrdinal(requests[a].OwnerId, requests[b].OwnerId);
                });

                for (int rank = 1; rank < members.Count; rank++)
                {
                    int index = members[rank];
                    Vector3 position = resolved[index];
                    position.y += rowHeight * rank;
                    resolved[index] = position;
                }
            }

            SpreadWideLabels(requests, resolved, rowHeight);
            return resolved;
        }

        /// <summary>
        /// The width-aware pass. Labels that gave a width are placed south first, then by
        /// owner id (the column pass's order, so it stays deterministic); each climbs a row at a
        /// time while its box overlaps one already placed. A column of narrow labels is left as
        /// the column pass stacked it; a row of wide ones staggers, every other one a row up,
        /// so each still sits over its own golem. Labels with no width are untouched.
        /// </summary>
        private static void SpreadWideLabels(IReadOnlyList<WorldHudRequest> requests, Vector3[] resolved, float rowHeight)
        {
            var wide = new List<int>();
            for (int i = 0; i < requests.Count; i++)
            {
                if (requests[i].HalfWidth > 0f)
                {
                    wide.Add(i);
                }
            }
            if (wide.Count < 2 || rowHeight <= 0f)
            {
                return;
            }
            wide.Sort((a, b) =>
            {
                int byY = resolved[a].y.CompareTo(resolved[b].y);
                return byY != 0 ? byY : string.CompareOrdinal(requests[a].OwnerId, requests[b].OwnerId);
            });

            var placed = new List<int>();
            foreach (int index in wide)
            {
                // Bounded: a label can only climb past each placed one once.
                for (int guard = 0; guard <= placed.Count; guard++)
                {
                    bool overlaps = false;
                    foreach (int other in placed)
                    {
                        float rows = (Mathf.Max(rowHeight, requests[index].Height) + Mathf.Max(rowHeight, requests[other].Height)) / 2f;
                        if (Mathf.Abs(resolved[index].x - resolved[other].x) < requests[index].HalfWidth + requests[other].HalfWidth &&
                            Mathf.Abs(resolved[index].y - resolved[other].y) < rows * 0.999f)
                        {
                            overlaps = true;
                            break;
                        }
                    }
                    if (!overlaps)
                    {
                        break;
                    }
                    Vector3 position = resolved[index];
                    position.y += Mathf.Max(rowHeight, requests[index].Height);
                    resolved[index] = position;
                }
                placed.Add(index);
            }
        }
    }
}
