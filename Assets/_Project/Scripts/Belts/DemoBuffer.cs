using System.Collections.Generic;

namespace GolemFactory.Belts
{
    // M4 placeholder sink for LoadIntoBuffer -- NOT the real StorageBuffer (that's M5).
    // Just enough to prove golem -> belt -> golem/storage flow: an in-memory count per id.
    public static class DemoBuffer
    {
        private static readonly Dictionary<string, int> _counts = new Dictionary<string, int>();

        public static void Deposit(string bufferId, string itemType)
        {
            _counts.TryGetValue(bufferId, out int count);
            _counts[bufferId] = count + 1;
        }

        public static int GetCount(string bufferId) => _counts.TryGetValue(bufferId, out int count) ? count : 0;

        // Test hygiene only -- static state must be reset between tests.
        public static void ResetAll() => _counts.Clear();
    }
}
