using System.Collections.Generic;
using GolemFactory.Economy;

namespace GolemFactory.UI
{
    public enum InventoryRowKind
    {
        /// <summary>A buffer's title row: its id and how many item types it holds.</summary>
        Header,

        /// <summary>One item type in one buffer: icon, name, bar, quantity, rate.</summary>
        Item,

        /// <summary>A line of explanation where there would otherwise be nothing.</summary>
        Message,
    }

    /// <summary>One row of the Inventory tab, decided; the screen only lays it out.</summary>
    public sealed class InventoryRow
    {
        public InventoryRowKind Kind;

        /// <summary>The buffer id (Header), the item type (Item) or the sentence (Message).</summary>
        public string Text = "";

        /// <summary>Header: "1 type" / "3 types".</summary>
        public string CountText = "";

        public string BufferId = "";
        public string ItemType = "";
        public int Quantity;

        /// <summary>"84/100" against a capacity, bare "84" without one.</summary>
        public string QuantityText = "";

        public StockBar Bar;

        /// <summary>"↑ +60/min", "⇄ 30/min", or "--" with no reading yet.</summary>
        public string RateText = "";

        public StockFlowKind RateKind;
        public StockTrend Trend;
    }

    /// <summary>
    /// The Inventory tab's rows, composed from the stockpile and the throughput history:
    /// Unity's InventoryPanel.Refresh with its UGUI taken out (milestone G8).
    ///
    /// <para>
    /// Every buffer, sorted by id (dictionary order is not contractual, and a list that
    /// re-renders every frame must not reshuffle); per buffer a header, then one row per item
    /// type in BufferTrendUtility's order. An uncapped buffer's bar is relative to the largest
    /// quantity on screen; a capped one's is against its own capacity, and its quantity reads
    /// as a fraction. The rate column is "--" until there is history to read -- never a false
    /// "0/min" -- and tells a balanced flow (in = out) apart from a dead line.
    /// </para>
    /// </summary>
    public static class InventoryReadout
    {
        public const string NoRegistryMessage = "Inventory unavailable: no storage registry wired.";
        public const string NoBuffersMessage = "No stockpiles yet. Harvest a resource node to start one.";

        public static List<InventoryRow> Compose(StorageBufferRegistry registry, BufferRateTracker tracker)
        {
            var rows = new List<InventoryRow>();
            if (registry == null)
            {
                rows.Add(Message(NoRegistryMessage));
                return rows;
            }

            IReadOnlyDictionary<string, StorageBuffer> buffers = registry.Buffers;
            if (buffers.Count == 0)
            {
                rows.Add(Message(NoBuffersMessage));
                return rows;
            }

            var ids = new List<string>(buffers.Keys);
            ids.Sort(string.CompareOrdinal);

            int largest = 0;
            foreach (string id in ids)
            {
                foreach (KeyValuePair<string, int> entry in buffers[id].Quantities)
                {
                    if (entry.Value > largest)
                    {
                        largest = entry.Value;
                    }
                }
            }

            foreach (string id in ids)
            {
                StorageBuffer buffer = buffers[id];
                var types = new List<string>(buffer.Quantities.Keys);
                BufferTrendUtility.SortItemTypes(types);
                rows.Add(new InventoryRow
                {
                    Kind = InventoryRowKind.Header,
                    Text = id,
                    BufferId = id,
                    CountText = types.Count == 1 ? "1 type" : types.Count + " types",
                });
                if (types.Count == 0)
                {
                    rows.Add(Message("   empty"));
                    continue;
                }
                foreach (string type in types)
                {
                    rows.Add(Item(id, type, buffer.GetQuantity(type), buffer.CapacityPerType, largest, tracker));
                }
            }
            return rows;
        }

        private static InventoryRow Item(string bufferId, string itemType, int quantity, int capacity, int largest, BufferRateTracker tracker)
        {
            StockBar bar = StockBarPolicy.Evaluate(quantity, capacity, largest);

            float rate = 0f;
            bool hasReading = tracker != null && tracker.TryGetRatePerMinute(bufferId, itemType, out rate);
            float inRate = 0f;
            float outRate = 0f;
            bool hasFlow = tracker != null && tracker.TryGetFlowPerMinute(bufferId, itemType, out inRate, out outRate);
            StockFlowKind kind = BufferFlowUtility.Classify(hasReading, rate, hasFlow, inRate, outRate);
            StockTrend trend = BufferTrendUtility.Classify(rate);
            string text = BufferFlowUtility.FormatFlow(kind, rate, inRate, outRate);

            return new InventoryRow
            {
                Kind = InventoryRowKind.Item,
                Text = itemType,
                BufferId = bufferId,
                ItemType = itemType,
                Quantity = quantity,
                QuantityText = bar.Mode == StockBarMode.AgainstCapacity ? quantity + "/" + capacity : quantity.ToString(),
                Bar = bar,
                RateKind = kind,
                Trend = trend,
                RateText = kind == StockFlowKind.Unknown ? text : BufferFlowUtility.Glyph(kind, trend) + " " + text,
            };
        }

        private static InventoryRow Message(string text) => new InventoryRow { Kind = InventoryRowKind.Message, Text = text };
    }
}
