using GolemFactory.Steam;
using GolemFactory.Events;

namespace GolemFactory.UI
{
    // Turns a StallReason plus the id that blocked it into the sentence the player reads.
    // Engine-free static so it is unit-testable without a scene, the same split
    // GridCoordinateConverter/YSortUtility/WorkbenchDiagnostics/BeltSignalUtility already use.
    //
    // The phrasing names the blocking resource, because in a strictly-linear execution model
    // "GolemD is stalled" is not actionable on its own -- the golem will retry the same step
    // forever, so the only thing the player can act on is the resource that is blocking it.
    public static class StallDiagnostics
    {
        // A belt the player laid is named for its cell plus a tie-breaking counter
        // ("Belt(0,-12)#3", BeltNetwork) -- a key, not a name, and the badge used to show it
        // verbatim. A golem only ever pulls from the belt behind it and pushes onto the one in
        // front, so the badge says that, and the strip says where.
        private static readonly System.Text.RegularExpressions.Regex PlacedBeltId =
            new System.Text.RegularExpressions.Regex(@"^Belt(\(-?\d+,-?\d+\))#\d+$");

        /// <summary>The cell a player-laid belt's id names, as "(x,y)", or null for any other id.</summary>
        public static string PlacedBeltCell(string resourceId)
        {
            System.Text.RegularExpressions.Match match =
                string.IsNullOrEmpty(resourceId) ? null : PlacedBeltId.Match(resourceId);
            return match != null && match.Success ? match.Groups[1].Value : null;
        }

        // Short form for the world-space badge floating over a golem -- no golem id, since the
        // badge is already attached to the golem it describes.
        //
        // shortfall is OPTIONAL and defaults to 0, which reproduces the pre-§1.3 text exactly.
        // Only Assemble's MissingItem ever supplies one: a Haul type mismatch and an empty Push
        // hold have no meaningful "how many more", and inventing one there would be a lie.
        public static string DescribeShort(StallReason reason, string resourceId, int shortfall = 0,
            SteamShortage steam = SteamShortage.None)
        {
            // A NoSteam stall with a known cause says the cause, because the three have three
            // different fixes (G10: a player with a pipe running to the golem was told only
            // "no steam" while its boiler sat empty).
            if (reason == StallReason.NoSteam)
            {
                switch (steam)
                {
                    case SteamShortage.NoPipe: return "no steam pipe reaches me";
                    case SteamShortage.BoilerOutOfCoke: return "my boiler is out of Coke";
                    case SteamShortage.BoilerAtCapacity: return "my boiler already powers 8 golems";
                }
            }

            string target = string.IsNullOrEmpty(resourceId) ? "source" : resourceId;
            switch (reason)
            {
                case StallReason.NodeEmpty:
                    return target + " depleted";
                case StallReason.BeltFull:
                    return PlacedBeltCell(resourceId) != null ? "the belt in front of me is full" : target + " full";
                case StallReason.BeltEmpty:
                    return PlacedBeltCell(resourceId) != null ? "waiting on the belt behind me" : "waiting on " + target;
                case StallReason.BufferEmpty:
                    return "no input in " + target;
                // Spatial stalls name the *tile*, not a resource id, because the id is not the
                // actionable fact -- the golem is facing the wrong way or standing in the wrong
                // place, and "nothing behind me" is the sentence that tells the player to rotate
                // or move it. Plain ASCII only (no arrow glyphs): TMP's LiberationSans SDF atlas
                // has no entry for them, same constraint as ComposeStripText's "[!]".
                case StallReason.NoSourceAtTile:
                    return string.IsNullOrEmpty(resourceId)
                        ? "nothing behind me"
                        : "nothing behind me at " + resourceId;
                case StallReason.NoTargetAtTile:
                    return string.IsNullOrEmpty(resourceId)
                        ? "nothing in front of me"
                        : "nothing in front of me at " + resourceId;
                // The two internal-stock stalls name an ITEM TYPE rather than a belt/node/
                // buffer id, so they get their own fallback wording -- "source" would be a
                // lie here, since the thing that is full is the golem itself.
                case StallReason.InputFull:
                    return "hold full of " + ItemOrGoods(resourceId);
                case StallReason.OutputFull:
                    return "output full of " + ItemOrGoods(resourceId);
                // Says the type is missing, not that a place is empty -- the endpoint may be
                // full of something else entirely. See StallReason.MissingItem.
                //
                // With an amount it says how many MORE are needed rather than how many the
                // recipe wants, because that is the number the player acts on: "need 9 more
                // Casing" and "need 1 more Casing" are the difference between a dead line and a
                // hiccup, and progression-design §8 asks for exactly that distinction.
                case StallReason.MissingItem:
                    return HasAmount(resourceId, shortfall)
                        ? "need " + shortfall + " more " + resourceId
                        : "no " + ItemOrGoods(resourceId);
                // Names the TILE, like the two spatial stalls above and for the same reason:
                // the fix is spatial (lay a pipe, move the golem, or add a boiler because the
                // one it depends on is at its 8-golem cap), so the cell is the actionable fact.
                // progression-design §3.1 asks for exactly this -- "stalls with a new
                // StallReason.NoSteam, naming the tile".
                case StallReason.NoSteam:
                    return string.IsNullOrEmpty(resourceId)
                        ? "no steam"
                        : "no steam at " + resourceId;
                // §3.2's cap. Says the seam is CROWDED, never that it is empty: the node is
                // visibly still full, and the fix is to walk this golem out to another site.
                case StallReason.NodeCrowded:
                    return string.IsNullOrEmpty(resourceId)
                        ? "seam already crewed"
                        : resourceId + " already has 2 extractors";
                // §1's labelled crate. Says what the destination WILL NOT take rather than what
                // it wants, because the golem is standing there holding the refused good and
                // that is the thing the player has to re-route. "Depot wants Iron Plate" would
                // be true and useless -- it does not say which of a mixed hold is stuck.
                case StallReason.FilterMismatch:
                    return string.IsNullOrEmpty(resourceId)
                        ? "wrong crate for this load"
                        : "nothing here takes " + resourceId;
                case StallReason.Unconfigured:
                    return "not wired up";
                default:
                    return "stalled";
            }
        }

        // Long form for the alerts strip, which has no other context about which golem it means.
        // shortfall behaves exactly as in DescribeShort -- optional, 0 means "no amount".
        public static string Describe(
            string golemId, StallReason reason, string resourceId, int shortfall = 0,
            SteamShortage steam = SteamShortage.None)
        {
            string who = string.IsNullOrEmpty(golemId) ? "A golem" : golemId;
            if (reason == StallReason.NoSteam)
            {
                switch (steam)
                {
                    case SteamShortage.NoPipe:
                        return who + " stalled: no steam pipe reaches it from a boiler";
                    case SteamShortage.BoilerOutOfCoke:
                        return who + " stalled: its boiler is out of Coke - fuel it with [E]";
                    case SteamShortage.BoilerAtCapacity:
                        return who + " stalled: its boiler already powers 8 golems - build another";
                }
            }
            string beltCell = PlacedBeltCell(resourceId);
            string target = string.IsNullOrEmpty(resourceId) ? "its source"
                : beltCell != null ? "the belt at " + beltCell : resourceId;
            switch (reason)
            {
                case StallReason.NodeEmpty:
                    return who + " stalled: " + target + " is depleted";
                case StallReason.BeltFull:
                    return who + " stalled: " + target + " is full";
                case StallReason.BeltEmpty:
                    return who + " stalled: waiting for items on " + target;
                case StallReason.BufferEmpty:
                    return who + " stalled: " + target + " has no input";
                case StallReason.NoSourceAtTile:
                    return string.IsNullOrEmpty(resourceId)
                        ? who + " stalled: nothing to pull from behind it"
                        : who + " stalled: nothing to pull from on tile " + resourceId;
                case StallReason.NoTargetAtTile:
                    return string.IsNullOrEmpty(resourceId)
                        ? who + " stalled: nothing to push to in front of it"
                        : who + " stalled: nothing to push to on tile " + resourceId;
                case StallReason.InputFull:
                    return who + " stalled: its hold is full of " + ItemOrGoods(resourceId);
                case StallReason.OutputFull:
                    return who + " stalled: its output is full of " + ItemOrGoods(resourceId);
                // "no Aether available" rather than "ScrapBuffer has no input": the endpoint
                // may be visibly full of the wrong good, so naming it would send the player to
                // the one place that clearly is not empty.
                case StallReason.MissingItem:
                    return HasAmount(resourceId, shortfall)
                        ? who + " stalled: needs " + shortfall + " more " + resourceId
                        : who + " stalled: no " + ItemOrGoods(resourceId) + " available";
                case StallReason.NoSteam:
                    return string.IsNullOrEmpty(resourceId)
                        ? who + " stalled: no steam reaching it"
                        : who + " stalled: no steam reaching tile " + resourceId;
                case StallReason.NodeCrowded:
                    return string.IsNullOrEmpty(resourceId)
                        ? who + " stalled: that seam is already fully crewed"
                        : who + " stalled: " + resourceId + " is already worked by 2 extractors";
                case StallReason.FilterMismatch:
                    return string.IsNullOrEmpty(resourceId)
                        ? who + " stalled: its load does not fit the crate in front of it"
                        : who + " stalled: nothing in front of it takes " + resourceId;
                case StallReason.Unconfigured:
                    return who + " stalled: not wired up";
                default:
                    return who + " is stalled";
            }
        }

        // Fallback for the two stalls whose resourceId is an item type. Deliberately not the
        // "source"/"its source" wording the endpoint-id stalls use -- nothing external is
        // blocking an InputFull/OutputFull golem, so pointing the player outward would send
        // them looking in the wrong place.
        private static string ItemOrGoods(string itemType) =>
            string.IsNullOrEmpty(itemType) ? "goods" : itemType;

        // An amount is only worth printing when there is a type to attach it to. "need 3 more
        // goods" is worse than the generic sentence, so both halves have to be present or the
        // text falls back to precisely what it said before shortfalls existed.
        private static bool HasAmount(string itemType, int shortfall) =>
            shortfall > 0 && !string.IsNullOrEmpty(itemType);

        // What the strip shows overall. Naming the single blocking resource beats a bare count,
        // and the "+N more" suffix keeps a cascading factory from overflowing one line.
        public static string ComposeStripText(int stalledCount, StallSnapshot primary)
        {
            if (stalledCount <= 0)
            {
                return "All golems running.";
            }

            // Plain ASCII, not the U+26A0 glyph -- TMP's default SDF atlas (LiberationSans SDF)
            // has no entry for it, unlike legacy Text's dynamic OS font fallback, so it would
            // render as a missing-glyph box.
            string text = "[!] " + Describe(
                primary.GolemId, primary.Reason, primary.ResourceId, primary.Shortfall, primary.Steam);
            if (stalledCount > 1)
            {
                text += "  (+" + (stalledCount - 1) + " more)";
            }

            return text;
        }
    }
}
