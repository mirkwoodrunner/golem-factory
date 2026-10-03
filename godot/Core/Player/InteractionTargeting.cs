using System.Collections.Generic;
using GolemFactory.Compat;

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
        Program = 3,

        // Hand-loading Coke into a boiler. Appended, so the tie-break order above is unchanged
        // for the three kinds that already existed: a boiler only wins a tie against nothing.
        // That is the right end of the order for it -- a boiler is a large obvious building the
        // player walks up to deliberately, while a node or a golem sharing its tile is the thing
        // they are more likely to have meant.
        Refuel = 4,

        // Labelling a depot (docs/cozy-automation-design.md §1). Appended for the same reason
        // Refuel was, and it belongs at this end of the order for a stronger reason than the
        // boiler does: a golem almost always stands beside the depot it pushes into, and a crate
        // that beat that golem on a distance tie would make it unprogrammable.
        Sort = 5
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
            IReadOnlyList<Vector3> golems,
            IReadOnlyList<Vector3> boilers = null,
            IReadOnlyList<Vector3> depots = null)
        {
            InteractionPick best = InteractionPick.None;
            // Evaluated in enum order with a strict less-than, so an exact distance tie keeps
            // the earlier kind -- see the InteractionKind doc comment for why that order.
            Consider(origin, harvestables, InteractionKind.Harvest, ref best);
            Consider(origin, stations, InteractionKind.Construct, ref best);
            Consider(origin, golems, InteractionKind.Program, ref best);
            // Optional and last, so every existing three-list caller (Main.unity's scenes and the
            // whole pre-existing targeting suite) picks exactly what it always did.
            Consider(origin, boilers, InteractionKind.Refuel, ref best);
            Consider(origin, depots, InteractionKind.Sort, ref best);
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
                // "Fuel", not "Refuel": the case that matters most is the boiler that has never
                // been lit, and a player told to RE-fuel one looks for the fuel they must have
                // spilled.
                case InteractionKind.Refuel: return "Fuel Boiler";
                // "Label", not "Filter" or "Sort": the fantasy is chalking a word on a crate,
                // and the verb has to say that pressing the key CHANGES something. "Sort Depot"
                // would read as an action the depot performs on its contents.
                case InteractionKind.Sort: return "Label Depot";
                default: return "";
            }
        }

        /// <summary>
        /// The separator between two clauses of a prompt's detail. U+00B7, deliberately: the
        /// project's TMP atlas has no em dash and no arrow, and both draw as a missing-glyph box.
        /// </summary>
        public const string DetailSeparator = " · ";

        /// <summary>
        /// What [G] would do to the golem within arm's reach: pick it up, or set down the one
        /// already in hand.
        /// </summary>
        /// <remarks>
        /// Here rather than spelled out at its two call sites because those two sites are the
        /// ONLY places the game ever tells the player that [G] exists, and they had to agree.
        /// One is the golem's own caption; the other is the aside appended to whatever else won
        /// the [E] pick -- which is the case that matters, because a freshly built golem stands
        /// on the tile in front of its station, so at the spot the player is standing when it
        /// appears the *station* is nearest and the golem's caption is not the one being drawn.
        /// Without the aside, a player who never wanders off the station never learns the key.
        /// </remarks>
        /// <param name="golemId">
        /// Named only when the caption is not already about that golem; pass null from the
        /// golem's own prompt, where the name is the prompt's subject.
        /// </param>
        public static string GolemHandlingHint(string golemId, bool isCarrying)
        {
            if (isCarrying)
            {
                // "Set down", not "drop": dropping is what happens to something you were not
                // being careful with, and this places the golem on a chosen tile.
                return "[G] set down";
            }

            return string.IsNullOrEmpty(golemId) ? "[G] carry" : "[G] carry " + golemId;
        }

        /// <summary>
        /// Joins another clause onto a prompt's detail, supplying the separator only when there
        /// is something on both sides of it.
        /// </summary>
        public static string AppendDetail(string detail, string addition)
        {
            if (string.IsNullOrEmpty(addition))
            {
                return detail;
            }

            return string.IsNullOrEmpty(detail) ? addition : detail + DetailSeparator + addition;
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
