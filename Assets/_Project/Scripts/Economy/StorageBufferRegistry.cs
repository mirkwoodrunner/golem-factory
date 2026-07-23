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
    }
}
