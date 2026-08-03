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
        public static string DescribeShort(StallReason reason, string resourceId)
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
                case StallReason.Unconfigured:
                    return "not wired up";
                default:
                    return "stalled";
            }
        }

        // Long form for the alerts strip, which has no other context about which golem it means.
        public static string Describe(string golemId, StallReason reason, string resourceId)
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
                case StallReason.Unconfigured:
                    return who + " stalled: not wired up";
                default:
                    return who + " is stalled";
            }
        }

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
            string text = "[!] " + Describe(primary.GolemId, primary.Reason, primary.ResourceId);
            if (stalledCount > 1)
            {
                text += "  (+" + (stalledCount - 1) + " more)";
            }

            return text;
        }
    }
}
