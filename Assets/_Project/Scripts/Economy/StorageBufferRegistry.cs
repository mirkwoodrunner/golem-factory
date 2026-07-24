using System.Collections.Generic;

namespace GolemFactory.Economy
{
    // Registry of named StorageBuffers, mirroring ConveyorSystem's segment dictionary
    // and World/ResourceNodeRegistry. Buffers are created on first deposit -- unlike
    // belt segments, nothing needs to pre-register a buffer's shape before golems start
    // depositing into it.
    public sealed class StorageBufferRegistry
    {
        private readonly Dictionary<string, StorageBuffer> _buffers = new Dictionary<string, StorageBuffer>();
        public IReadOnlyDictionary<string, StorageBuffer> Buffers => _buffers;

        public StorageBuffer GetOrCreate(string bufferId)
        {
            if (!_buffers.TryGetValue(bufferId, out StorageBuffer buffer))
            {
                buffer = new StorageBuffer(bufferId);
                _buffers[bufferId] = buffer;
            }

            return buffer;
        }

        public bool TryGetBuffer(string bufferId, out StorageBuffer buffer)
        {
            // Guard against a null id (an unset sourceId/destinationId), matching
            // ConveyorSystem.TryGetSegment -- Dictionary<string,_> throws on a null key.
            if (bufferId == null)
            {
                buffer = null;
                return false;
            }

            return _buffers.TryGetValue(bufferId, out buffer);
        }

        public void Deposit(string bufferId, string itemType, int amount = 1)
        {
            if (bufferId == null)
            {
                return;
            }

            GetOrCreate(bufferId).Deposit(itemType, amount);
        }

        public bool TryWithdraw(string bufferId, string itemType, int amount = 1) =>
            TryGetBuffer(bufferId, out StorageBuffer buffer) && buffer.TryWithdraw(itemType, amount);

        // Withdraws Scrap then Brass, refunding the Scrap portion if the Brass withdrawal
        // fails partway through, so a failed combined-cost purchase never leaves the
        // buffer partially charged. Centralizes the pattern Buildings/AssemblyBayStructure
        // .TryUpgrade already implemented once inline -- building placement and golem
        // construction both need the same "pay in Scrap and Brass together" check, so a
        // third copy wasn't worth pasting.
        public bool TryWithdrawScrapAndBrass(string bufferId, int scrapCost, int brassCost)
        {
            // A zero cost must trivially succeed even if that item type was never
            // deposited into this buffer -- StorageBuffer.TryWithdraw looks the item type
            // up first and fails on a miss regardless of the requested amount, so a
            // literal `TryWithdraw(id, Scrap, 0)` against an untouched buffer would
            // otherwise incorrectly reject a genuinely free purchase (e.g. the default
            // zero-cost PlaceableBuilding).
            if (scrapCost > 0 && !TryWithdraw(bufferId, ItemType.Scrap, scrapCost))
            {
                return false;
            }

            if (brassCost > 0 && !TryWithdraw(bufferId, ItemType.Brass, brassCost))
            {
                if (scrapCost > 0)
                {
                    Deposit(bufferId, ItemType.Scrap, scrapCost);
                }
                return false;
            }

            return true;
        }
    }
}
