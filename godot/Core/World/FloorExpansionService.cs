using System;
using System.Collections.Generic;
using GolemFactory.Economy;
using GolemFactory.PunchCards;

namespace GolemFactory.World
{
    /// <summary>
    /// Floor Expansion (docs/progression-design.md §3.3, §11 item 15): buying more workshop,
    /// northward only, at an escalating price.
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
        private int scrapPerRow = 40;
        private int ironPlatePerRow = 20;
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

        public void Configure(FloorBounds floorBounds, StorageBufferRegistry buffers, string bufferId)
        {
            bounds = floorBounds;
            bufferRegistry = buffers;
            if (!string.IsNullOrEmpty(bufferId))
            {
                stockpileBufferId = bufferId;
            }
        }

        /// <summary>Tuning setter (Unity's Inspector fields).</summary>
        public void ConfigureCost(int scrap, int ironPlate, int rows)
        {
            scrapPerRow = scrap;
            ironPlatePerRow = ironPlate;
            rowsPerPurchase = rows;
        }

        /// <summary>
        /// What the next purchase costs. Escalates one step per purchase already made, so the
        /// first extension is cheap and the room cannot be grown to its limit for pocket change.
        ///
        /// <para>
        /// <b>Clamped at the pack's cap</b> (docs/crates-design.md §6.1, a stopgap): the second
        /// purchase onwards asked for more of one good than a 100 pack can hold, so it could never
        /// be paid. Until the tuning pass reshapes the curve, no step asks for more than a full pack.
        /// </para>
        /// </summary>
        public IReadOnlyList<RecipeIngredient> NextCost()
        {
            int rowsGrown = bounds == null ? 0 : bounds.NorthExtent - FloorLayout.DefaultNorthExtent;
            int step = 1 + rowsGrown / RowsPerPurchase;
            return new[]
            {
                new RecipeIngredient(ItemType.Scrap, AtMostAPack(scrapPerRow * RowsPerPurchase * step)),
                new RecipeIngredient(ItemType.IronPlate, AtMostAPack(ironPlatePerRow * RowsPerPurchase * step)),
            };
        }

        private int AtMostAPack(int quantity)
        {
            int cap = bufferRegistry == null || string.IsNullOrEmpty(stockpileBufferId)
                ? StorageBuffer.Unlimited
                : bufferRegistry.GetOrCreate(stockpileBufferId).CapacityPerType;
            return cap == StorageBuffer.Unlimited ? quantity : Math.Min(quantity, cap);
        }

        private PlayerWallet Wallet => bufferRegistry == null ? null : new PlayerWallet(bufferRegistry, stockpileBufferId);

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
                // Charged for nothing: hand it all back.
                Wallet?.Refund(cost);
                return false;
            }

            RowsAdded?.Invoke(previousNorth + 1, bounds.NorthExtent);
            return true;
        }
    }
}
