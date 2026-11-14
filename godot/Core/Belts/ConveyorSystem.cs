using System.Collections.Generic;
using GolemFactory.Simulation;

namespace GolemFactory.Belts
{
    public sealed class ConveyorSystem : ITickable
    {
        private readonly Dictionary<string, BeltSegment> _segments = new Dictionary<string, BeltSegment>();

        // The order the tick walks: the order segments were registered in, oldest first. Kept
        // beside the dictionary because a dictionary's own order is not that: it hands a removed
        // entry's slot to the next one added, so a belt laid after another was pulled up took the
        // old belt's turn. Where two belts feed one, the turn decides which gets the free slot,
        // and the same factory rebuilt from a save walked them in a different order.
        private readonly List<BeltSegment> _order = new List<BeltSegment>();

        /// <summary>
        /// How far every item moves along its lane per tick, in a segment's progress units.
        /// 1 is the original rate (a lane of Length L crosses in L ticks); the Sandbox slows it
        /// from its setup (<c>beltCellsPerSecond</c>, G10). Spacing is unchanged: items still
        /// queue <see cref="BeltSegment.MinSpacing"/> apart, so a belt holds the same cargo.
        /// </summary>
        public float StepPerTick { get; set; } = 1f;

        public void Register(BeltSegment segment)
        {
            // A second segment under one id takes the first one's place, and its turn.
            if (_segments.TryGetValue(segment.SegmentId, out BeltSegment replaced))
            {
                _order[_order.IndexOf(replaced)] = segment;
            }
            else
            {
                _order.Add(segment);
            }

            _segments[segment.SegmentId] = segment;
        }

        public void Unregister(string segmentId)
        {
            if (segmentId != null && _segments.Remove(segmentId, out BeltSegment removed))
            {
                _order.Remove(removed);
            }
        }

        public bool TryGetSegment(string segmentId, out BeltSegment segment)
        {
            // Dictionary<string,_>.TryGetValue throws ArgumentNullException on a null key
            // (e.g. an AppendageActionDefinition whose sourceId/destinationId was never set) --
            // guard so callers get a clean "not found" instead of a crash.
            if (segmentId == null)
            {
                segment = null;
                return false;
            }

            return _segments.TryGetValue(segmentId, out segment);
        }

        public bool CanEnqueue(string segmentId) =>
            TryGetSegment(segmentId, out BeltSegment segment) && segment.CanEnqueue();

        public bool TryEnqueue(string segmentId, ItemStack item) =>
            TryGetSegment(segmentId, out BeltSegment segment) && segment.TryEnqueue(item);

        public bool TryPeekHead(string segmentId, out ItemStack item)
        {
            item = default;
            return TryGetSegment(segmentId, out BeltSegment segment) && segment.TryPeekHead(out item);
        }

        public bool TryDequeueHead(string segmentId, out ItemStack item)
        {
            item = default;
            return TryGetSegment(segmentId, out BeltSegment segment) && segment.TryRemoveHead(out item);
        }

        public void Tick(long tick)
        {
            // Phase 1: advance every segment first.
            for (int i = 0; i < _order.Count; i++)
            {
                _order[i].Advance(StepPerTick);
            }

            // Phase 2: hand off completed heads onto Next. Kept as its own pass (after all
            // Advance calls) so a handed-off item -- reset to Progress = 0 in its new segment
            // -- is never advanced a second time in this same tick, regardless of iteration order.
            for (int i = 0; i < _order.Count; i++)
            {
                // One call, whether the segment feeds one belt or several: BeltSegment.TryHandOff
                // owns the round-robin, so a splitter and a plain belt take the identical path
                // through this pass and cannot drift in their ordering guarantees.
                //
                // A refusal is backpressure, exactly as before -- the head stays parked at
                // Length and the lane behind it backs up.
                _order[i].TryHandOff();
            }
        }
    }
}
