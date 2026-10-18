using System;
using System.Collections.Generic;
using System.Linq;
using GolemFactory.Buildings;
using GolemFactory.Economy;

namespace GolemFactory.UI
{
    /// <summary>
    /// One line of a Pack or Crate screen panel: a good held, or the panel's one message row when
    /// it holds nothing (<see cref="IsMessage"/>).
    /// </summary>
    public readonly struct CrateScreenRow
    {
        public readonly string ItemType;
        public readonly string Name;
        public readonly int Quantity;

        /// <summary>"37/50", or "112/100" above the cap; the bare count where nothing caps it.</summary>
        public readonly string QuantityText;

        public readonly StockBar Bar;

        /// <summary>Whether a click would move anything. A row that can't still takes the click, to say why.</summary>
        public readonly bool CanMove;

        /// <summary>The refusal a click would give, else null.</summary>
        public readonly string Reason;

        public bool IsMessage => ItemType == null;

        public CrateScreenRow(string itemType, string name, int quantity, string quantityText, StockBar bar, bool canMove, string reason)
        {
            ItemType = itemType;
            Name = name;
            Quantity = quantity;
            QuantityText = quantityText;
            Bar = bar;
            CanMove = canMove;
            Reason = reason;
        }

        public static CrateScreenRow Message(string text) =>
            new CrateScreenRow(null, text, 0, "", default, false, null);
    }

    /// <summary>
    /// The rules behind the Pack and Crate screens (docs/inventory-screens-design.md §2): what a
    /// row says, what a click moves and why one is refused, and which labels a crate is offered.
    /// The screens draw these rows and forward clicks here; every move goes through
    /// <see cref="CrateTransfer"/>, so the screen and E's collect can't disagree.
    /// </summary>
    public sealed class CrateScreenReadout
    {
        public const string CrateEmpty = "Empty.";
        public const string PackEmpty = "Your pack is empty.";
        public const string PackTitle = "Your pack";
        public const string PostTitle = "Pneumatic Post";
        public const string ChoosingLabel = "Choose a label";
        public const string MastLabel = "A mast's crate takes no label";

        private readonly StorageBufferRegistry _buffers;
        private readonly string _packId;
        private readonly string _postId;
        private readonly Func<IEnumerable<string>> _knownGoods;

        /// <param name="knownGoods">Every good the factory has made: the ledger, which only grows.</param>
        /// <param name="postId">The Pneumatic Post's store, shown under the pack (inventory spec §4.5); null for none.</param>
        public CrateScreenReadout(StorageBufferRegistry buffers, string packId, Func<IEnumerable<string>> knownGoods, string postId = null)
        {
            _buffers = buffers;
            _packId = packId;
            _knownGoods = knownGoods;
            _postId = postId;
        }

        public StorageBuffer Pack => _buffers.GetOrCreate(_packId);

        // --- Rows ------------------------------------------------------------------------------

        /// <summary>The crate's goods, each clickable into the pack.</summary>
        public List<CrateScreenRow> CrateRows(ICrate crate) =>
            Rows(crate?.Buffer, good => CrateTransfer.PlanTake(crate, Pack, good, int.MaxValue), CrateEmpty);

        /// <summary>The pack's goods: into <paramref name="crate"/> when one is open, else only shown.</summary>
        public List<CrateScreenRow> PackRows(ICrate crate) =>
            Rows(Pack, crate == null ? null : good => CrateTransfer.PlanPut(Pack, crate, good, int.MaxValue), PackEmpty);

        /// <summary>
        /// The Post store's goods, read-only, for the Pack screen under the pack's own rows
        /// (inventory spec §4.5). Empty, with no message row, while the store holds nothing: the
        /// panel then shows no Post header at all.
        /// </summary>
        public List<CrateScreenRow> PostRows()
        {
            StorageBuffer post = _postId != null && _buffers.TryGetBuffer(_postId, out StorageBuffer b) ? b : null;
            return post == null || !post.Quantities.Any(q => q.Value > 0)
                ? new List<CrateScreenRow>()
                : Rows(post, null, PostTitle);
        }

        /// <summary>
        /// One row per good <paramref name="side"/> holds, in tier order. <paramref name="plan"/>
        /// says what a click would move; null for a side that is only shown.
        /// </summary>
        public static List<CrateScreenRow> Rows(StorageBuffer side, Func<string, TransferResult> plan, string emptyMessage)
        {
            var rows = new List<CrateScreenRow>();
            List<string> goods = side == null
                ? new List<string>()
                : side.Quantities.Where(q => q.Value > 0).Select(q => q.Key).ToList();
            if (goods.Count == 0)
            {
                rows.Add(CrateScreenRow.Message(emptyMessage));
                return rows;
            }

            BufferTrendUtility.SortItemTypes(goods);
            int cap = side.CapacityPerType;
            int largest = goods.Max(side.GetQuantity);
            foreach (string good in goods)
            {
                int quantity = side.GetQuantity(good);
                TransferResult? would = plan?.Invoke(good);
                rows.Add(new CrateScreenRow(
                    good,
                    ItemTiers.DisplayName(good),
                    quantity,
                    cap > 0 ? quantity + "/" + cap : quantity.ToString(),
                    StockBarPolicy.Evaluate(quantity, cap, largest),
                    would == null || would.Value.Moved > 0,
                    would?.Refusal));
            }
            return rows;
        }

        // --- Text ------------------------------------------------------------------------------

        /// <summary>"Crate (3,-2)", "Freight Mast (5,4)", or "Pneumatic Post" for a shared store.</summary>
        public static string Title(ICrate crate)
        {
            if (crate == null)
            {
                return "";
            }
            if (!crate.OwnsBuffer)
            {
                return PostTitle;
            }
            string id = crate.CrateId ?? "";
            int open = id.IndexOf('(');
            string where = open >= 0 ? " " + id.Substring(open) : "";
            return (crate is PlaceableFreightMast ? "Freight Mast" : "Crate") + where;
        }

        public static string CapLine(ICrate crate)
        {
            int cap = crate?.Buffer?.CapacityPerType ?? 0;
            string each = cap > 0 ? cap + " of each good" : "any amount of each good";
            return crate != null && !crate.OwnsBuffer ? "Shared by every Post crate · " + each : "Holds " + each;
        }

        public string PackCapLine =>
            Pack.CapacityPerType > 0 ? "Carries " + Pack.CapacityPerType + " of each good" : "Carries any amount of each good";

        /// <summary>"7 goods · 100 of each".</summary>
        public string PackSummary
        {
            get
            {
                int goods = Pack.Quantities.Count(q => q.Value > 0);
                string count = goods == 1 ? "1 good" : goods + " goods";
                return Pack.CapacityPerType > 0 ? count + " · " + Pack.CapacityPerType + " of each" : count;
            }
        }

        /// <summary>The label button: "Label: Copper Ore", "Choose a label" while picking, or a mast's.</summary>
        public static string LabelText(ICrate crate, bool picking)
        {
            if (crate == null || !crate.CanLabel)
            {
                return MastLabel;
            }
            return picking ? ChoosingLabel : "Label: " + DepotFilterOptions.Describe(crate.FilterItemType);
        }

        /// <summary>What a picker option is called: "Any goods" for none, else the good's name.</summary>
        public static string OptionName(string itemType) =>
            string.IsNullOrEmpty(itemType) ? "Any goods" : ItemTiers.DisplayName(itemType);

        // --- Clicks ----------------------------------------------------------------------------

        /// <summary>A crate row's click: all that fits into the pack, or one with Shift. Returns the status line.</summary>
        public string Take(ICrate crate, string itemType, bool shift)
        {
            TransferResult moved = CrateTransfer.Take(crate, Pack, itemType, CrateTransfer.AmountFor(shift));
            return Said(moved, "to your pack", CrateTransfer.PlanTake(crate, Pack, itemType, 1));
        }

        /// <summary>A pack row's click: all that fits into the crate, or one with Shift. Returns the status line.</summary>
        public string Put(ICrate crate, string itemType, bool shift)
        {
            TransferResult moved = CrateTransfer.Put(Pack, crate, itemType, CrateTransfer.AmountFor(shift));
            return Said(moved, "into the crate", CrateTransfer.PlanPut(Pack, crate, itemType, 1));
        }

        /// <summary>Take all: E's collect. Returns the status line.</summary>
        public string TakeAll(ICrate crate)
        {
            List<TransferResult> results = CrateTransfer.Collect(crate, Pack);
            List<TransferResult> moved = results.Where(r => r.Moved > 0).ToList();
            if (moved.Count == 0)
            {
                return results.Count == 0 ? "Nothing to take" : results[0].Refusal;
            }
            string line = "Moved " + string.Join(", ", moved.Select(r => r.Moved + " " + ItemTiers.DisplayName(r.ItemType))) + " to your pack";
            string left = crate.Buffer.Quantities.Any(q => q.Value > 0)
                ? CrateTransfer.PlanTake(crate, Pack, crate.Buffer.Quantities.First(q => q.Value > 0).Key, 1).Refusal
                : null;
            return left != null && left != CrateTransfer.NoneLeft ? line + " · " + left : line;
        }

        // "Moved 2 Coke into the crate · Crate full of Coke": a move the receiver cut short says
        // so, since the player asked for more than went.
        private static string Said(TransferResult moved, string where, TransferResult next)
        {
            if (moved.Moved <= 0)
            {
                return moved.Refusal;
            }
            string line = "Moved " + moved.Moved + " " + ItemTiers.DisplayName(moved.ItemType) + " " + where;
            return next.Moved == 0 && next.Refusal != CrateTransfer.NoneLeft ? line + " · " + next.Refusal : line;
        }

        // --- Labels ----------------------------------------------------------------------------

        /// <summary>
        /// "Any goods" (null) first, then every good the factory has made, in tier order, plus the
        /// crate's own label if it is somehow missing. Empty for a crate that takes no label.
        /// </summary>
        public IReadOnlyList<string> LabelOptions(ICrate crate) =>
            crate == null || !crate.CanLabel
                ? new string[0]
                : DepotFilterOptions.BuildCycle(_knownGoods?.Invoke(), crate.FilterItemType);

        /// <summary>Labels the crate; goods already inside stay. Returns the status line.</summary>
        public string SetLabel(ICrate crate, string itemType)
        {
            if (crate == null || !crate.CanLabel)
            {
                return MastLabel;
            }
            crate.SetFilter(string.IsNullOrEmpty(itemType) ? null : itemType);
            return LabelText(crate, picking: false);
        }
    }
}
