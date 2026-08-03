using System.Collections.Generic;
using UnityEngine;

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
        public BeltSegment Next { get; set; }

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
