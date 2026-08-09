namespace GolemFactory.UI
{
    // Why the Workbench's status line currently says what it says. Carried alongside the
    // message so the line can be *retired* when the condition that produced it resolves --
    // the original implementation only ever wrote to the status line and never cleared it,
    // so "Not enough Focus (need 10)" stayed on screen while the tape ticker visibly read
    // FOCUS 42/100, and "remove appendages to fit its slot count first" survived removing
    // every appendage.
    public enum WorkbenchStatusReason
    {
        None,
        // A one-off report ("Gears engaged", "Patented as BP-001") that is true when
        // written and simply goes stale with time.
        Info,
        InsufficientFocusEngage,
        InsufficientFocusPatent,
        ChassisTooSmall,
        NoTarget,
        // The targeted golem's program has more appendages than this Workbench has sockets to
        // show, so the draft cannot represent it. Appended, not inserted -- this enum is not
        // serialized anywhere today, but the project's two enums that ARE (StallReason,
        // AppendageActionType) make append-only the house habit rather than a special case.
        DraftTruncated
    }

    // Pure, engine-free staleness policy for the status line -- WorkbenchController is the
    // thin applier that feeds it live numbers each frame, same idiom as
    // WorkbenchDiagnostics / WorkbenchLeverMotion.
    public static class WorkbenchStatusPolicy
    {
        // How long a purely informational message stays up before retiring itself.
        public const float InfoSeconds = 6f;

        // True once the condition the message described no longer holds, so the caller
        // should blank the line.
        //
        // chassisSlotLimit is the slot count of the chassis whose selection was rejected
        // (meaningful only for ChassisTooSmall); assignedAppendages is the draft's live
        // appendage count.
        public static bool ShouldClear(
            WorkbenchStatusReason reason,
            float shownSeconds,
            float focus,
            float engageCost,
            float patentCost,
            int assignedAppendages,
            int chassisSlotLimit,
            bool hasTarget,
            // Optional with a default so every existing caller -- and every existing test --
            // compiles and behaves unchanged. A message that predates this parameter cannot be
            // DraftTruncated, so the default is the only value that could apply to one.
            bool draftTruncated = false)
        {
            switch (reason)
            {
                case WorkbenchStatusReason.None:
                    return false;
                case WorkbenchStatusReason.Info:
                    return shownSeconds >= InfoSeconds;
                case WorkbenchStatusReason.InsufficientFocusEngage:
                    // Focus regenerates on wall-clock time, so this one un-becomes true on
                    // its own without the player doing anything.
                    return focus >= engageCost;
                case WorkbenchStatusReason.InsufficientFocusPatent:
                    return focus >= patentCost;
                case WorkbenchStatusReason.ChassisTooSmall:
                    return assignedAppendages <= chassisSlotLimit;
                case WorkbenchStatusReason.NoTarget:
                    return hasTarget;
                case WorkbenchStatusReason.DraftTruncated:
                    // Un-becomes true only by retargeting onto a golem the sockets can hold.
                    // Deliberately NOT time-based: the program is still unrepresentable after
                    // six seconds, and a line that retired itself would leave the lever
                    // refusing with nothing on screen explaining why.
                    return !draftTruncated;
                default:
                    return false;
            }
        }
    }
}
