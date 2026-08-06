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
        // Short form for the world-space badge floating over a golem -- no golem id, since the
        // badge is already attached to the golem it describes.
        //
        // shortfall is OPTIONAL and defaults to 0, which reproduces the pre-§1.3 text exactly.
        // Only Assemble's MissingItem ever supplies one: a Haul type mismatch and an empty Push
        // hold have no meaningful "how many more", and inventing one there would be a lie.
        public static string DescribeShort(StallReason reason, string resourceId, int shortfall = 0)
        {
            string target = string.IsNullOrEmpty(resourceId) ? "source" : resourceId;
            switch (reason)
            {
                case StallReason.NodeEmpty:
                    return target + " depleted";
                case StallReason.BeltFull:
                    return target + " full";
                case StallReason.BeltEmpty:
                    return "waiting on " + target;
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
                case StallReason.Unconfigured:
                    return "not wired up";
                default:
                    return "stalled";
            }
        }

        // Long form for the alerts strip, which has no other context about which golem it means.
        // shortfall behaves exactly as in DescribeShort -- optional, 0 means "no amount".
        public static string Describe(
            string golemId, StallReason reason, string resourceId, int shortfall = 0)
        {
            string who = string.IsNullOrEmpty(golemId) ? "A golem" : golemId;
            string target = string.IsNullOrEmpty(resourceId) ? "its source" : resourceId;
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
                primary.GolemId, primary.Reason, primary.ResourceId, primary.Shortfall);
            if (stalledCount > 1)
            {
                text += "  (+" + (stalledCount - 1) + " more)";
            }

            return text;
        }
    }
}
