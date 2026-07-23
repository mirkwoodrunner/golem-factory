using System.Collections.Generic;
using GolemFactory.Simulation;

namespace GolemFactory.Belts
{
    public sealed class ConveyorSystem : ITickable
    {
        private readonly Dictionary<string, BeltSegment> _segments = new Dictionary<string, BeltSegment>();

        public void Register(BeltSegment segment) => _segments[segment.SegmentId] = segment;

        public void Unregister(string segmentId) => _segments.Remove(segmentId);

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
            foreach (BeltSegment segment in _segments.Values)
            {
                segment.Advance(1f);
            }

            // Phase 2: hand off completed heads onto Next. Kept as its own pass (after all
            // Advance calls) so a handed-off item -- reset to Progress = 0 in its new segment
            // -- is never advanced a second time in this same tick, regardless of iteration order.
            foreach (BeltSegment segment in _segments.Values)
            {
                if (segment.Next == null || !segment.TryPeekHead(out ItemStack head))
                {
                    continue;
                }

                if (segment.Next.TryEnqueue(head))
                {
                    segment.TryRemoveHead(out _);
                }
                // else: Next is full -- backpressure; head stays parked at Length.
            }
        }
    }
}
