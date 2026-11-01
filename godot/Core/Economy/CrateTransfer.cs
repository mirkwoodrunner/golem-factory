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
        /// <summary>
        /// A click moves one; a Shift-click moves all that fits, as automation games' inventories
        /// do (user, 2026-10-18; inventory spec §2.1).
        /// </summary>
        public static int AmountFor(bool shift) => shift ? int.MaxValue : 1;

        /// <summary>The refusal when the giving side holds none of the good.</summary>
        public const string NoneLeft = "None left";

        /// <summary>Up to <paramref name="want"/> of a good from the crate into the pack. Labels don't apply.</summary>
        public static TransferResult Take(ICrate crate, StorageBuffer pack, string itemType, int want) =>
            Apply(crate?.Buffer, pack, PlanTake(crate, pack, itemType, want));

        /// <summary>
        /// Up to <paramref name="want"/> of a good from the pack into the crate, refused when the
        /// crate is labelled for another good.
        /// </summary>
        public static TransferResult Put(StorageBuffer pack, ICrate crate, string itemType, int want) =>
            Apply(pack, crate?.Buffer, PlanPut(pack, crate, itemType, want));

        /// <summary>What <see cref="Take"/> would move, or why it would refuse, moving nothing.</summary>
        public static TransferResult PlanTake(ICrate crate, StorageBuffer pack, string itemType, int want)
        {
            StorageBuffer from = crate?.Buffer;
            if (from == null || pack == null || from.GetQuantity(itemType) <= 0)
            {
                return new TransferResult(itemType, 0, NoneLeft);
            }
            return Plan(from, pack, itemType, want, "Pack full of " + ItemTiers.DisplayName(itemType));
        }

        /// <summary>What <see cref="Put"/> would move, or why it would refuse, moving nothing.</summary>
        public static TransferResult PlanPut(StorageBuffer pack, ICrate crate, string itemType, int want)
        {
            StorageBuffer to = crate?.Buffer;
            if (to == null || pack == null || pack.GetQuantity(itemType) <= 0)
            {
                return new TransferResult(itemType, 0, NoneLeft);
            }
            string label = crate.FilterItemType;
            if (!string.IsNullOrEmpty(label) && label != itemType)
            {
                return new TransferResult(itemType, 0, "This crate is for " + ItemTiers.DisplayName(label));
            }
            return Plan(pack, to, itemType, want, "Crate full of " + ItemTiers.DisplayName(itemType));
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
            foreach (string good in from.HeldGoods())
            {
                results.Add(Take(crate, pack, good, int.MaxValue));
            }
            return results;
        }

        /// <summary>"12 Scrap, 3 Coke": the goods that moved, for a status line.</summary>
        public static string Describe(IEnumerable<TransferResult> results) =>
            string.Join(", ", results.Where(r => r.Moved > 0).Select(r => r.Moved + " " + ItemTiers.DisplayName(r.ItemType)));

        private static TransferResult Plan(StorageBuffer from, StorageBuffer to, string itemType, int want, string fullRefusal)
        {
            int moved = System.Math.Min(want, System.Math.Min(from.GetQuantity(itemType), to.RoomFor(itemType)));
            return new TransferResult(itemType, moved > 0 ? moved : 0, fullRefusal);
        }

        // Withdraw, then deposit: the plan was bounded by the receiver's room, so the deposit
        // takes all of it.
        private static TransferResult Apply(StorageBuffer from, StorageBuffer to, TransferResult plan)
        {
            if (plan.Moved > 0)
            {
                from.TryWithdraw(plan.ItemType, plan.Moved);
                to.Deposit(plan.ItemType, plan.Moved);
            }
            return plan;
        }
    }
}
