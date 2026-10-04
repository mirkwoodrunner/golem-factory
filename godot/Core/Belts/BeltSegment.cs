using System.Collections.Generic;
using GolemFactory.Compat;

namespace GolemFactory.Belts
{
    // Fixed-capacity lane of ItemStack, no GameObject per item (see architecture doc).
    // Items are ordered head-first (index 0 = closest to exit / largest Progress).
    public sealed class BeltSegment
    {
        public const float MinSpacing = 1f;

        public string SegmentId { get; }
        public int Length { get; }
        public int Capacity => Length + 1;
        /// <summary>
        /// Where this segment's head goes. USUALLY ONE, and the single-output case is still
        /// what every ordinary belt has: a belt has one facing, so it points at exactly one
        /// cell. More than one output means a SPLITTER, which is a cell whose downstream is
        /// every neighbour facing away from it.
        ///
        /// <para>
        /// A list rather than a second "splitter" type, because the handoff rule is identical
        /// either way -- offer the head, keep it if refused -- and a parallel type would be a
        /// second copy of <c>ConveyorSystem</c>'s two-pass ordering, which is the one piece of
        /// belt code that must never be duplicated.
        /// </para>
        /// </summary>
        public IReadOnlyList<BeltSegment> Outputs => _outputs;

        private readonly List<BeltSegment> _outputs = new List<BeltSegment>();

        /// <summary>
        /// The first output, as the single-output world saw it. Kept because BeltNetwork,
        /// ConveyorSystem's tests and Main.unity's demos all speak in terms of one Next, and
        /// setting it to null clears the fan-out -- which is exactly what "this belt feeds
        /// nothing" meant before.
        /// </summary>
        public BeltSegment Next
        {
            get => _outputs.Count > 0 ? _outputs[0] : null;
            set
            {
                _outputs.Clear();
                if (value != null)
                {
                    _outputs.Add(value);
                }

                _handoffCursor = 0;
            }
        }

        /// <summary>
        /// Which output gets offered the next item. Rotates ONLY on a successful handoff, so a
        /// splitter whose left branch is backed up does not waste its turn on it -- and two
        /// identical factories split identically, because nothing here depends on dictionary
        /// order or on wall-clock time.
        /// </summary>
        public int HandoffCursor => _outputs.Count == 0 ? 0 : _handoffCursor % _outputs.Count;

        private int _handoffCursor;

        /// <summary>Adds a downstream segment, ignoring nulls, self-links and duplicates.</summary>
        public bool AddOutput(BeltSegment output)
        {
            if (output == null || ReferenceEquals(output, this) || _outputs.Contains(output))
            {
                return false;
            }

            _outputs.Add(output);
            return true;
        }

        public void ClearOutputs()
        {
            _outputs.Clear();
            _handoffCursor = 0;
        }

        /// <summary>
        /// Offers the head to each output in turn, starting at the cursor, and advances the
        /// cursor past whichever one took it. Returns false when every output refused (or there
        /// are none), leaving the head parked -- the backpressure a full lane has always had.
        /// </summary>
        public bool TryHandOff()
        {
            if (_outputs.Count == 0 || !TryPeekHead(out ItemStack head))
            {
                return false;
            }

            for (int offset = 0; offset < _outputs.Count; offset++)
            {
                int index = (HandoffCursor + offset) % _outputs.Count;
                if (!_outputs[index].TryEnqueue(head))
                {
                    continue;
                }

                TryRemoveHead(out _);

                // Advance PAST the branch that took it, so the next item starts its search at
                // the other one. Round-robin rather than "always try the first": a fixed order
                // would feed the left branch until it backed up and only then use the right,
                // which is a priority splitter wearing a splitter's name.
                _handoffCursor = index + 1;
                return true;
            }

            return false;
        }

        private readonly List<ItemStack> _items = new List<ItemStack>();
        public IReadOnlyList<ItemStack> Items => _items;

        public BeltSegment(string segmentId, int length)
        {
            SegmentId = segmentId;
            Length = Mathf.Max(1, length);
        }

        // Side-effect-free version of TryEnqueue's guard. Exists so a producer that has to
        // consume something irreversible to make an item (GolemEntity's ExtractFromNode, which
        // decrements a finite ResourceNode) can check for room *before* consuming, instead of
        // extracting and then dropping the item on a failed enqueue.
        public bool CanEnqueue() =>
            _items.Count < Capacity &&
            (_items.Count == 0 || _items[_items.Count - 1].Progress >= MinSpacing);

        public bool TryEnqueue(ItemStack item)
        {
            if (!CanEnqueue())
            {
                return false;
            }

            item.Progress = 0f;
            _items.Add(item);
            return true;
        }

        public bool TryPeekHead(out ItemStack head)
        {
            if (_items.Count == 0)
            {
                head = default;
                return false;
            }

            head = _items[0];
            return head.Progress >= Length;
        }

        public bool TryRemoveHead(out ItemStack head)
        {
            if (!TryPeekHead(out head))
            {
                return false;
            }

            _items.RemoveAt(0);
            return true;
        }

        public void Advance(float step)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                ItemStack item = _items[i];
                float cap = i == 0 ? Length : _items[i - 1].Progress - MinSpacing;
                item.Progress = Mathf.Min(item.Progress + step, cap);
                _items[i] = item;
            }
        }
    }
}
