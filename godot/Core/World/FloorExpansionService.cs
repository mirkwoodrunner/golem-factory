using System;
using System.Collections.Generic;
using GolemFactory.Economy;
using GolemFactory.PunchCards;

namespace GolemFactory.World
{
    /// <summary>
    /// Floor Expansion (docs/progression-design.md §3.3, §11 item 15): buying more workshop,
    /// northward only, at an escalating price: six authored bundles, priced up the tiers
    /// (docs/pack-upgrades-design.md §4).
    ///
    /// <para>
    /// PORTED FROM Unity's MonoBehaviour of the same name (G2d) -- the rules: the price grows
    /// one step per purchase, the withdrawal is atomic, and a purchase that adds no rows (the
    /// room is at its limit) refunds in full rather than charging for air. Unity's version also
    /// painted the new rows into a Tilemap and rebuilt the walls; the scene does that now, from
    /// <see cref="RowsAdded"/>.
    /// </para>
    /// </summary>
    public sealed class FloorExpansionService
    {
        private FloorBounds bounds;
        private StorageBufferRegistry bufferRegistry;
        private string stockpileBufferId = "FactoryStockpile";
        private string postBufferId;
        private IReadOnlyList<IReadOnlyList<RecipeIngredient>> purchases = new IReadOnlyList<RecipeIngredient>[0];
        private int rowsPerPurchase = 2;

        /// <summary>Raised after a purchase grows the room: the first and last new row, inclusive.</summary>
        public event Action<int, int> RowsAdded;

        /// <summary>Rows a load took away (from, to inclusive): a save made before an expansion.</summary>
        public event Action<int, int> RowsRemoved;

        /// <summary>
        /// A load: the room becomes the saved size, announcing the rows it gained or lost so the
        /// floor and walls follow. Never charges and never refunds -- the stockpile is restored
        /// from the same save.
        /// </summary>
        public void Restore(int northExtent)
        {
            if (bounds == null || northExtent <= 0)
            {
                return;
            }
            int previous = bounds.Restore(northExtent);
            if (bounds.NorthExtent > previous)
            {
                RowsAdded?.Invoke(previous + 1, bounds.NorthExtent);
            }
            else if (bounds.NorthExtent < previous)
            {
                RowsRemoved?.Invoke(bounds.NorthExtent + 1, previous);
            }
        }

        public string LastStatusMessage { get; private set; } = "";

        public FloorBounds Bounds => bounds;

        public int RowsPerPurchase => rowsPerPurchase < 1 ? 1 : rowsPerPurchase;

        /// <param name="postId">The Pneumatic Post's store, spent after the pack (crates-design §10); null for none.</param>
        public void Configure(FloorBounds floorBounds, StorageBufferRegistry buffers, string bufferId, string postId = null)
        {
            bounds = floorBounds;
            bufferRegistry = buffers;
            postBufferId = postId;
            if (!string.IsNullOrEmpty(bufferId))
            {
                stockpileBufferId = bufferId;
            }
        }

        /// <summary>The price list (sandbox.json): one bundle per purchase, in order.</summary>
        public void ConfigureCost(IReadOnlyList<IReadOnlyList<RecipeIngredient>> bundles, int rows)
        {
            purchases = bundles ?? new IReadOnlyList<RecipeIngredient>[0];
            rowsPerPurchase = rows;
        }

        /// <summary>Purchases made so far: rows grown past the authored room, a purchase's worth at a time.</summary>
        public int PurchasesMade =>
            bounds == null ? 0 : Math.Max(0, bounds.NorthExtent - FloorLayout.DefaultNorthExtent) / RowsPerPurchase;

        /// <summary>
        /// What the next purchase costs: the bundle at <see cref="PurchasesMade"/>, unclamped. The
        /// pack's cap grows by upgrade to meet it (docs/pack-upgrades-design.md, which superseded
        /// crates-design §6.1's clamp at a full pack). Past the list, the last bundle.
        /// </summary>
        public IReadOnlyList<RecipeIngredient> NextCost() =>
            purchases.Count == 0 ? new RecipeIngredient[0] : purchases[Math.Min(PurchasesMade, purchases.Count - 1)];

        private PlayerWallet Wallet => bufferRegistry == null ? null : new PlayerWallet(bufferRegistry, stockpileBufferId, postBufferId);

        private int StockOf(string itemType) => Wallet?.Quantity(itemType) ?? 0;

        public bool CanAfford() => UI.ConstructionCostPolicy.CanAfford(StockOf, NextCost());

        public bool TryPurchaseExpansion()
        {
            LastStatusMessage = "";
            if (bounds == null)
            {
                LastStatusMessage = "No floor bounds in this scene.";
                return false;
            }

            if (!bounds.CanExpand)
            {
                LastStatusMessage = "The workshop cannot be extended any further.";
                return false;
            }

            IReadOnlyList<RecipeIngredient> cost = NextCost();
            if (Wallet != null && !Wallet.TryCharge(cost))
            {
                LastStatusMessage = UI.ConstructionCostPolicy.FormatShortfall(StockOf, cost);
                return false;
            }

            int previousNorth = bounds.NorthExtent;
            int added = bounds.Expand(RowsPerPurchase);
            if (added <= 0)
            {
                // Charged for nothing: hand it all back where it came from. Not Refund, which is
                // pack-only: the part the Post paid might not fit back in the pack.
                Wallet?.Uncharge(cost);
                return false;
            }

            RowsAdded?.Invoke(previousNorth + 1, bounds.NorthExtent);
            return true;
        }
    }
}
