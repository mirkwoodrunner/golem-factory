using System.Collections.Generic;
using GolemFactory.Events;

namespace GolemFactory.Economy
{
    /// <summary>
    /// Every good the factory has used up, counted (<see cref="ItemConsumedEvent"/>): the
    /// overview's "goods spent" (docs/supply-design.md §4). The counterpart of
    /// <see cref="ProductionTally"/>, and like it monotone counts of the factory's own work: a
    /// recipe's inputs, fuel burned, Slag voided, what a recycler takes and the tower is
    /// delivered. Never a price the player pays or what the hand crank uses.
    /// </summary>
    public sealed class ConsumptionTally
    {
        private readonly Dictionary<string, int> _spent = new Dictionary<string, int>();
        private bool _attached;

        /// <summary>Every good used up so far, by item type: monotone counts.</summary>
        public IReadOnlyDictionary<string, int> Totals => _spent;

        public void Attach()
        {
            if (!_attached)
            {
                EventBus.ItemConsumed += OnItemConsumed;
                _attached = true;
            }
        }

        public void Detach()
        {
            if (_attached)
            {
                EventBus.ItemConsumed -= OnItemConsumed;
                _attached = false;
            }
        }

        private void OnItemConsumed(ItemConsumedEvent e)
        {
            if (string.IsNullOrEmpty(e.ItemType) || e.Quantity <= 0)
            {
                return;
            }
            _spent.TryGetValue(e.ItemType, out int total);
            _spent[e.ItemType] = total + e.Quantity;
        }
    }
}
