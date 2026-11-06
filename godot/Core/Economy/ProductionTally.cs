using System.Collections.Generic;
using GolemFactory.Events;

namespace GolemFactory.Economy
{
    /// <summary>
    /// Every good golems have assembled, counted (docs/crates-design.md §4.5). The Ledger's live
    /// rate used to read the stockpile, which every golem filled; with crates, a line's output
    /// lands in its own crate and the pack measures only the player's hands. Fresh production is
    /// <see cref="ItemAssembledEvent"/>, the signal the Clock Tower counts, so the rate reads it.
    ///
    /// <para>
    /// The counts are monotone, so the world samples them into the rate tracker under
    /// <see cref="SeriesId"/>, and a fitted slope is the production rate.
    /// </para>
    /// </summary>
    public sealed class ProductionTally
    {
        /// <summary>The rate tracker's series for fresh production. Not a buffer: never in the registry.</summary>
        public const string SeriesId = "Produced";

        private readonly Dictionary<string, int> _produced = new Dictionary<string, int>();
        private bool _attached;

        /// <summary>Every good assembled so far, by item type: monotone counts.</summary>
        public IReadOnlyDictionary<string, int> Totals => _produced;

        public void Attach()
        {
            if (!_attached)
            {
                EventBus.ItemAssembled += OnItemAssembled;
                _attached = true;
            }
        }

        public void Detach()
        {
            if (_attached)
            {
                EventBus.ItemAssembled -= OnItemAssembled;
                _attached = false;
            }
        }

        private void OnItemAssembled(ItemAssembledEvent e)
        {
            if (string.IsNullOrEmpty(e.ItemType) || e.Quantity <= 0)
            {
                return;
            }
            _produced.TryGetValue(e.ItemType, out int total);
            _produced[e.ItemType] = total + e.Quantity;
        }

        /// <summary>Records every count into <paramref name="tracker"/> at <paramref name="time"/>.</summary>
        public void Sample(float time, BufferRateTracker tracker)
        {
            foreach (KeyValuePair<string, int> pair in _produced)
            {
                tracker.Sample(time, SeriesId, pair.Key, pair.Value);
            }
        }
    }
}
