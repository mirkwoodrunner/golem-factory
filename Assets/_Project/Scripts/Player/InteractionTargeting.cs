using System.Collections.Generic;
using UnityEngine;

namespace GolemFactory.Player
{
    /// <summary>
    /// What interacting with the currently-selected target would do. Ordered so that a tie in
    /// distance resolves to the cheapest, most-frequent action first (harvesting), which is
    /// what a player standing between a node and a station almost always means.
    /// </summary>
    public enum InteractionKind
    {
        None = 0,
        Harvest = 1,
        Construct = 2,
        Program = 3
    }

    /// <summary>
    /// How loudly the affordance for a pick should be drawn.
    /// </summary>
    public enum InteractionAffordance
    {
        /// <summary>Nothing worth pointing at -- draw no ring or prompt at all.</summary>
        Hidden = 0,
        /// <summary>Something is close enough to notice but too far to act on.</summary>
        OutOfRange = 1,
        /// <summary>
        /// In range, but the action would fail anyway -- a depleted resource node being the
        /// case that motivated it. Distinct from OutOfRange because walking closer will not
        /// help, and distinct from Ready because offering the key would be a lie.
        /// </summary>
        Unavailable = 2,
        /// <summary>Pressing Interact right now will do something.</summary>
        Ready = 3
    }

    /// <summary>
    /// Which candidate <see cref="InteractionTargeting.SelectNearest"/> chose: its kind, its
    /// index into that kind's list, and how far away it is. Distance is returned rather than
    /// squared distance because every consumer (range test, affordance banding, prompt text)
    /// wants real world units.
    /// </summary>
    public readonly struct InteractionPick
    {
        public readonly InteractionKind Kind;
        public readonly int Index;
        public readonly float Distance;

        public InteractionPick(InteractionKind kind, int index, float distance)
        {
            Kind = kind;
            Index = index;
            Distance = distance;
        }

        public static InteractionPick None => new InteractionPick(InteractionKind.None, -1, float.PositiveInfinity);

        public bool Exists => Kind != InteractionKind.None;

        public bool IsInRange(float range) => Exists && Distance <= range;
    }

    /// <summary>
    /// Pure selection geometry for <see cref="PlayerInteractor"/>: given the player's position
    /// and the positions of each kind of interactable, decide which single one is being
    /// targeted and how it should be advertised.
    /// <para>
    /// Extracted from PlayerInteractor so it is testable without a scene, the same idiom as
    /// PlayerMovement.ComputeDisplacement / GridCoordinateConverter / YSortUtility. Doing so
    /// also fixed a real behaviour bug: the original inline version checked node markers
    /// first, then stations, then golems, and returned the first kind with *any* candidate in
    /// range -- so a resource node at the very edge of range beat a construction station the
    /// player was standing on top of. This picks the genuinely nearest of all three.
    /// </para>
    /// </summary>
    public static class InteractionTargeting
    {
        /// <summary>
        /// How much further than the interact range something can be and still get a dimmed
        /// "move closer" affordance. Wide enough that walking toward a node lights it up
        /// before you arrive (so range is discoverable), tight enough that the ring isn't
        /// permanently parked on something across the room.
        /// </summary>
        public const float OutOfRangeBandMultiplier = 2.6f;

        /// <summary>
        /// Nearest candidate of any kind, regardless of range. Range gating is deliberately
        /// the caller's job: the out-of-range affordance needs to know about a target the
        /// player cannot yet act on, so filtering here would throw away the interesting case.
        /// </summary>
        public static InteractionPick SelectNearest(
            Vector3 origin,
            IReadOnlyList<Vector3> harvestables,
            IReadOnlyList<Vector3> stations,
            IReadOnlyList<Vector3> golems)
        {
            InteractionPick best = InteractionPick.None;
            // Evaluated in enum order with a strict less-than, so an exact distance tie keeps
            // the earlier kind -- see the InteractionKind doc comment for why that order.
            Consider(origin, harvestables, InteractionKind.Harvest, ref best);
            Consider(origin, stations, InteractionKind.Construct, ref best);
            Consider(origin, golems, InteractionKind.Program, ref best);
            return best;
        }

        private static void Consider(
            Vector3 origin, IReadOnlyList<Vector3> positions, InteractionKind kind, ref InteractionPick best)
        {
            if (positions == null)
            {
                return;
            }

            for (int i = 0; i < positions.Count; i++)
            {
                float distance = Vector3.Distance(origin, positions[i]);
                if (distance < best.Distance)
                {
                    best = new InteractionPick(kind, i, distance);
                }
            }
        }

        /// <summary>
        /// Bands a pick into the three affordance states the prompt view draws.
        /// </summary>
        public static InteractionAffordance ClassifyAffordance(InteractionPick pick, float interactRange)
        {
            if (!pick.Exists)
            {
                return InteractionAffordance.Hidden;
            }

            if (pick.Distance <= interactRange)
            {
                return InteractionAffordance.Ready;
            }

            return pick.Distance <= interactRange * OutOfRangeBandMultiplier
                ? InteractionAffordance.OutOfRange
                : InteractionAffordance.Hidden;
        }

        /// <summary>
        /// The verb shown on the prompt. Present tense and specific -- "Harvest", not
        /// "Interact" -- because the whole point of the prompt is that the player knows what
        /// the key will do before pressing it.
        /// </summary>
        public static string Verb(InteractionKind kind)
        {
            switch (kind)
            {
                case InteractionKind.Harvest: return "Harvest";
                case InteractionKind.Construct: return "Build Golem";
                case InteractionKind.Program: return "Program";
                default: return "";
            }
        }

        /// <summary>
        /// The full prompt line. In range it leads with the key so it scans as an action
        /// ("[E] Harvest Scrap - 12 left"); out of range it leads with the instruction, since
        /// pressing the key would do nothing and showing it would be a lie.
        /// </summary>
        /// <param name="detail">Optional trailing context (remaining quantity, cost, state).</param>
        public static string BuildPrompt(
            InteractionKind kind, string targetName, string detail, InteractionAffordance affordance, string interactKey)
        {
            if (kind == InteractionKind.None || affordance == InteractionAffordance.Hidden)
            {
                return "";
            }

            string name = string.IsNullOrEmpty(targetName) ? "" : " " + targetName;
            string suffix = string.IsNullOrEmpty(detail) ? "" : "  -  " + detail;

            if (affordance == InteractionAffordance.OutOfRange)
            {
                // Only the verb is lowercased -- a target name is a proper noun ("Aether",
                // "PlayerGolem-001") and lowercasing it made the line read as a typo.
                return "Move closer to " + Verb(kind).ToLowerInvariant() + name + suffix;
            }

            if (affordance == InteractionAffordance.Unavailable)
            {
                // No key: the action cannot succeed, and printing "[E] Harvest" next to
                // "depleted" told the player two contradictory things at once.
                string subject = string.IsNullOrEmpty(targetName) ? Verb(kind) : targetName;
                return subject + suffix;
            }

            string key = string.IsNullOrEmpty(interactKey) ? "E" : interactKey;
            return "[" + key + "]  " + Verb(kind) + name + suffix;
        }
    }
}
