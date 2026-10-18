using System.Collections.Generic;
using System.Linq;
using GolemFactory.Buildings;

namespace GolemFactory.Economy
{
    /// <summary>What one move between a crate and the pack did: how many of what, or why none.</summary>
    public readonly struct TransferResult
    {
        public readonly string ItemType;
        public readonly int Moved;

        /// <summary>Why nothing moved ("Pack full of Scrap"); null when something did.</summary>
        public readonly string Refusal;

        public TransferResult(string itemType, int moved, string refusal)
        {
            ItemType = itemType;
            Moved = moved;
            Refusal = moved > 0 ? null : refusal;
        }
    }

    /// <summary>
    /// The player's hands between a crate and the pack (docs/inventory-screens-design.md §2.1):
    /// E's collect, and the crate screen's clicks. Every move withdraws only what the other side
    /// has room for, then deposits it, so the deposit never refuses and no good is lost or made
    /// (docs/crates-design.md §0.1). A label is honoured on the way in, never on the way out.
    /// </summary>
    public static class CrateTransfer
    {
        /// <summary>A click moves all that fits; a Shift-click moves one.</summary>
        public static int AmountFor(bool shift) => shift ? 1 : int.MaxValue;

        /// <summary>Up to <paramref name="want"/> of a good from the crate into the pack. Labels don't apply.</summary>
        public static TransferResult Take(ICrate crate, StorageBuffer pack, string itemType, int want)
        {
            StorageBuffer from = crate?.Buffer;
            if (from == null || pack == null || from.GetQuantity(itemType) <= 0)
            {
                return new TransferResult(itemType, 0, "None left");
            }
            return Move(from, pack, itemType, want, "Pack full of " + ItemTiers.DisplayName(itemType));
        }

        /// <summary>
        /// Up to <paramref name="want"/> of a good from the pack into the crate, refused when the
        /// crate is labelled for another good.
        /// </summary>
        public static TransferResult Put(StorageBuffer pack, ICrate crate, string itemType, int want)
        {
            StorageBuffer to = crate?.Buffer;
            if (to == null || pack == null || pack.GetQuantity(itemType) <= 0)
            {
                return new TransferResult(itemType, 0, "None left");
            }
            string label = crate.FilterItemType;
            if (label != null && label != itemType)
            {
                return new TransferResult(itemType, 0, "This crate is for " + ItemTiers.DisplayName(label));
            }
            return Move(pack, to, itemType, want, "Crate full of " + ItemTiers.DisplayName(itemType));
        }

        /// <summary>
        /// E at a crate: all that fits of every good it holds, in tier order. What doesn't fit
        /// stays, as a golem's partial haul does. One result per good the crate held.
        /// </summary>
        public static List<TransferResult> Collect(ICrate crate, StorageBuffer pack)
        {
            var results = new List<TransferResult>();
            StorageBuffer from = crate?.Buffer;
            if (from == null)
            {
                return results;
            }
            List<string> goods = from.Quantities.Where(q => q.Value > 0).Select(q => q.Key).ToList();
            BufferTrendUtility.SortItemTypes(goods);
            foreach (string good in goods)
            {
                results.Add(Take(crate, pack, good, int.MaxValue));
            }
            return results;
        }

        private static TransferResult Move(StorageBuffer from, StorageBuffer to, string itemType, int want, string fullRefusal)
        {
            int moved = System.Math.Min(want, System.Math.Min(from.GetQuantity(itemType), to.RoomFor(itemType)));
            if (moved <= 0)
            {
                return new TransferResult(itemType, 0, fullRefusal);
            }
            from.TryWithdraw(itemType, moved);
            to.Deposit(itemType, moved);
            return new TransferResult(itemType, moved, null);
        }
    }
}
