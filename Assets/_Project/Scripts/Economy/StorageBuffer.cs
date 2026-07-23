using System.Collections.Generic;

namespace GolemFactory.Economy
{
    // Real replacement for M4's Belts/DemoBuffer placeholder: per-item-type quantities
    // held in one buffer, so a buffer can hold a mix of resources and the inventory UI
    // can list them individually instead of one opaque total count.
    public sealed class StorageBuffer
    {
        public string BufferId { get; }

        private readonly Dictionary<string, int> _quantities = new Dictionary<string, int>();
        public IReadOnlyDictionary<string, int> Quantities => _quantities;

        public StorageBuffer(string bufferId) => BufferId = bufferId;

        public void Deposit(string itemType, int amount = 1)
        {
            _quantities.TryGetValue(itemType, out int current);
            _quantities[itemType] = current + amount;
        }

        public bool TryWithdraw(string itemType, int amount = 1)
        {
            if (!_quantities.TryGetValue(itemType, out int current) || current < amount)
            {
                return false;
            }

            _quantities[itemType] = current - amount;
            return true;
        }

        public int GetQuantity(string itemType) => _quantities.TryGetValue(itemType, out int quantity) ? quantity : 0;
    }
}
