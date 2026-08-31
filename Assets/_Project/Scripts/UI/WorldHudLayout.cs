using System.Collections.Generic;
using UnityEngine;

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

        public WorldHudRequest(Vector3 anchor, string ownerId)
        {
            Anchor = anchor;
            OwnerId = ownerId ?? "";
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

            return resolved;
        }
    }
}
