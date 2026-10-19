using GolemFactory.Events;
using GolemFactory.PunchCards;

namespace GolemFactory.Golems
{
    /// <summary>Whether the factory will clear a stall on its own (docs/onboarding-feedback-design.md §3).</summary>
    public enum StallUrgency
    {
        /// <summary>Only the player can fix it: drawn red (or blue for steam), counted, shaken.</summary>
        Stuck = 0,

        /// <summary>Something already in the factory will clear it: drawn grey, quiet.</summary>
        Waiting = 1,
    }

    /// <summary>
    /// What a stalled golem's neighbours look like, read by <c>World.StallFactsProbe</c>. Plain
    /// data, so <see cref="StallTriage"/> is a pure function a test can call with any facts.
    /// </summary>
    public readonly struct StallFacts
    {
        /// <summary>The step that stalled (Haul, Assemble, Push, ...).</summary>
        public AppendageActionType StalledAction { get; }

        /// <summary>The tile behind holds something that can give: a crate, a belt, a stall, a mast, the recycler.</summary>
        public bool SourceGives { get; }

        /// <summary>Something other than this golem will put the wanted good on the tile behind.</summary>
        public bool SourceFed { get; }

        /// <summary>Something other than this golem will take goods from the tile in front.</summary>
        public bool TargetDrained { get; }

        /// <summary>The stall behind has a truckload on its way.</summary>
        public bool TruckloadOnTheWay { get; }

        /// <summary>The good the stalled step needs, or null (§4.3).</summary>
        public string WantedGood { get; }

        public StallFacts(AppendageActionType stalledAction, bool sourceGives, bool sourceFed,
            bool targetDrained, bool truckloadOnTheWay, string wantedGood)
        {
            StalledAction = stalledAction;
            SourceGives = sourceGives;
            SourceFed = sourceFed;
            TargetDrained = targetDrained;
            TruckloadOnTheWay = truckloadOnTheWay;
            WantedGood = wantedGood;
        }

        /// <summary>A step that takes from the tile behind.</summary>
        public bool IsTake =>
            StalledAction == AppendageActionType.Haul ||
            StalledAction == AppendageActionType.ExtractFromNode;
    }

    /// <summary>
    /// Red or grey: the table of docs/onboarding-feedback-design.md §3.3, as a function. Every
    /// <see cref="StallReason"/> is named here, and a test walks the enum, so a reason can't be
    /// appended without deciding who must act on it.
    /// </summary>
    public static class StallTriage
    {
        public static StallUrgency Urgency(StallReason reason, StallFacts facts)
        {
            switch (reason)
            {
                case StallReason.NodeEmpty:
                    return facts.TruckloadOnTheWay ? StallUrgency.Waiting : StallUrgency.Stuck;
                case StallReason.BeltFull:
                    return facts.TargetDrained ? StallUrgency.Waiting : StallUrgency.Stuck;
                case StallReason.BeltEmpty:
                case StallReason.BufferEmpty:
                    return facts.SourceGives && facts.SourceFed ? StallUrgency.Waiting : StallUrgency.Stuck;
                case StallReason.MissingItem:
                    // At a take, the source may yet be fed with the good. At an Assemble the hold
                    // won't refill: only the Hauls before it fill it, and they have run (§3.3).
                    return facts.IsTake && facts.SourceGives && (facts.SourceFed || facts.TruckloadOnTheWay)
                        ? StallUrgency.Waiting
                        : StallUrgency.Stuck;
                case StallReason.None:
                case StallReason.Unconfigured:
                case StallReason.NoSourceAtTile:
                case StallReason.NoTargetAtTile:
                case StallReason.InputFull:
                case StallReason.OutputFull:
                case StallReason.NoSteam:
                case StallReason.NodeCrowded:
                case StallReason.FilterMismatch:
                    return StallUrgency.Stuck;
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(reason), reason,
                        "StallTriage has no answer for this StallReason; map it (onboarding-feedback-design §3.3).");
            }
        }
    }
}
