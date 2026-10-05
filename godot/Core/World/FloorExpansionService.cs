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
        /// </summary>
        public IReadOnlyList<RecipeIngredient> NextCost()
        {
            int rowsGrown = bounds == null ? 0 : bounds.NorthExtent - FloorLayout.DefaultNorthExtent;
            int step = 1 + rowsGrown / RowsPerPurchase;
            return new[]
            {
                new RecipeIngredient(ItemType.Scrap, scrapPerRow * RowsPerPurchase * step),
                new RecipeIngredient(ItemType.IronPlate, ironPlatePerRow * RowsPerPurchase * step),
            };
        }

        private int StockOf(string itemType) =>
            bufferRegistry == null ? 0 : bufferRegistry.GetQuantity(stockpileBufferId, itemType);

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
            if (bufferRegistry != null && !bufferRegistry.TryWithdrawBundle(stockpileBufferId, cost))
            {
                LastStatusMessage = UI.ConstructionCostPolicy.FormatShortfall(StockOf, cost);
                return false;
            }

            int previousNorth = bounds.NorthExtent;
            int added = bounds.Expand(RowsPerPurchase);
            if (added <= 0)
            {
                // Charged for nothing: hand it all back.
                if (bufferRegistry != null)
                {
                    for (int i = 0; i < cost.Count; i++)
                    {
                        bufferRegistry.Deposit(stockpileBufferId, cost[i].itemType, cost[i].quantity);
                    }
                }
                return false;
            }

            RowsAdded?.Invoke(previousNorth + 1, bounds.NorthExtent);
            return true;
        }
    }
}
