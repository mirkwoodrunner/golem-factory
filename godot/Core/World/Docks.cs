using System;
using System.Collections.Generic;
using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.PunchCards;

namespace GolemFactory.World
{
    /// <summary>
    /// The Docks (docs/region-design.md): the second region, east of the market street's end. A
    /// gated lane on the street's rows joins the street to the Docks' own floor, whose south row is
    /// the quay, where barges stand on cart spots of their own. Cell arithmetic only, like
    /// <see cref="TownSquare"/>: whether the Docks are open, and how far north they reach, is
    /// <see cref="FloorBounds"/>' (<see cref="FloorBounds.DocksNorth"/>), and the shell draws them
    /// from these answers.
    /// </summary>
    public static class Docks
    {
        /// <summary>The lane's west column: one past the street's east kerb.</summary>
        public const int LaneWest = FloorLayout.StreetHalfExtent + 1;

        /// <summary>The Docks' floor, west and east columns.</summary>
        public const int West = LaneWest + 5;
        public const int East = West + 24;

        /// <summary>The quay: the Docks' south row, level with the street's far kerb.</summary>
        public const int Bottom = FloorLayout.WorldMinY;

        /// <summary>The lane's north row: the street's top row.</summary>
        public const int LaneTop = -FloorLayout.HalfExtent - 1;

        /// <summary>The Docks' north row when they open, and the most rows can push it to.</summary>
        public const int StartNorth = -4;
        public const int MaxNorth = 8;

        /// <summary>What <see cref="FloorBounds.DocksNorth"/> reads while the gate is shut: nothing is the Docks.</summary>
        public const int Closed = int.MinValue;

        /// <summary>The quay's eight barge spots, west to east, on <see cref="Bottom"/> (pitch 3).</summary>
        public static readonly int[] QuayColumns = { West + 3, West + 6, West + 9, West + 12, West + 15, West + 18, West + 21, West + 24 };

        /// <summary>The gate stands in the lane's middle column.</summary>
        public static readonly int GateColumn = LaneWest;

        public static IEnumerable<Vector2Int> QuaySpots()
        {
            foreach (int x in QuayColumns)
            {
                yield return new Vector2Int(x, Bottom);
            }
        }

        public static bool IsQuaySpot(Vector2Int cell) => cell.y == Bottom && Array.IndexOf(QuayColumns, cell.x) >= 0;

        public static bool InLane(Vector2Int cell) =>
            cell.x >= LaneWest && cell.x < West && cell.y >= Bottom && cell.y <= LaneTop;

        public static bool InFloor(Vector2Int cell, int north) =>
            north != Closed && cell.x >= West && cell.x <= East && cell.y >= Bottom && cell.y <= north;

        /// <summary>The region's name, for the HUD, while the player stands on the Docks' floor; empty elsewhere (region-design §5).</summary>
        public const string Name = "The Docks";

        /// <summary>What the HUD's plate says at the street's east end while the gate is roped.</summary>
        public const string ShutLine = Name + " · open once you build a Zeppelin";

        /// <summary>What the city's note says the moment the Docks open (region-design §3).</summary>
        public const string OpenedNote = Name + " are open: through the gate at the street's east end.";

        /// <summary>
        /// The HUD's plate: the region's name on the Docks' floor, and while they are shut, why, to a
        /// player at the street's east end beside the roped gate; empty elsewhere.
        /// </summary>
        public static string RegionName(Vector2Int playerCell, int north)
        {
            if (InFloor(playerCell, north))
            {
                return Name;
            }
            bool atTheGate = playerCell.x >= FloorLayout.StreetHalfExtent - 2 && playerCell.x < West
                && playerCell.y >= Bottom && playerCell.y <= LaneTop;
            return north == Closed && atTheGate ? ShutLine : "";
        }

        /// <summary>Whether a cell is the Docks' ground (the lane or the floor) with the Docks reaching <paramref name="north"/>.</summary>
        public static bool Contains(Vector2Int cell, int north) =>
            north != Closed && (InLane(cell) || InFloor(cell, north));

        /// <summary>Every cell of the Docks' ground, lane first, then the floor south to north.</summary>
        public static IEnumerable<Vector2Int> Cells(int north)
        {
            if (north == Closed)
            {
                yield break;
            }
            for (int y = Bottom; y <= LaneTop; y++)
            {
                for (int x = LaneWest; x < West; x++)
                {
                    yield return new Vector2Int(x, y);
                }
            }
            for (int y = Bottom; y <= north; y++)
            {
                for (int x = West; x <= East; x++)
                {
                    yield return new Vector2Int(x, y);
                }
            }
        }

        /// <summary>The gate's cells: the lane's middle column, every street row. A fixture, never a building.</summary>
        public static IEnumerable<Vector2Int> GateCells()
        {
            for (int y = Bottom; y <= LaneTop; y++)
            {
                yield return new Vector2Int(GateColumn, y);
            }
        }

        /// <summary>The gate's drawing cell: the middle of its column.</summary>
        public static Vector2Int GateCell => new Vector2Int(GateColumn, (Bottom + LaneTop) / 2);

        /// <summary>
        /// The nearest point of the Docks' ground to <paramref name="cellFraction"/>, from the lane's
        /// rectangle and the floor's: the clamp's two extra rectangles while the Docks are open. The
        /// lane reaches one column into the street and the floor one into the lane, the same overlap
        /// the road keeps with the room, so the nearest-rectangle rule leaves no seam to stick in.
        /// </summary>
        public static Vector2 Nearest(Vector2 cellFraction, int north)
        {
            var lane = new Vector2(
                Mathf.Clamp(cellFraction.x, LaneWest - 1f, West),
                Mathf.Clamp(cellFraction.y, Bottom, LaneTop));
            var floor = new Vector2(
                Mathf.Clamp(cellFraction.x, West, East),
                Mathf.Clamp(cellFraction.y, Bottom, north));
            return (cellFraction - lane).sqrMagnitude <= (cellFraction - floor).sqrMagnitude ? lane : floor;
        }
    }

    /// <summary>
    /// Buying the Docks' floor north, two rows at a time, as Floor Expansion buys the workshop's
    /// (docs/region-design.md §1): an authored price per purchase, charged atomically through the
    /// wallet, refused once the land runs out, and only while the Docks are open.
    /// </summary>
    public sealed class DocksExpansionService
    {
        private FloorBounds _bounds;
        private StorageBufferRegistry _buffers;
        private string _packId;
        private string _postId;
        private IReadOnlyList<IReadOnlyList<RecipeIngredient>> _purchases = new IReadOnlyList<RecipeIngredient>[0];
        private int _rowsPerPurchase = 2;

        public string LastStatusMessage { get; private set; } = "";

        public FloorBounds Bounds => _bounds;

        public int RowsPerPurchase => _rowsPerPurchase < 1 ? 1 : _rowsPerPurchase;

        public void Configure(FloorBounds bounds, StorageBufferRegistry buffers, string packId, string postId,
            IReadOnlyList<IReadOnlyList<RecipeIngredient>> purchases, int rowsPerPurchase)
        {
            _bounds = bounds;
            _buffers = buffers;
            _packId = packId;
            _postId = postId;
            _purchases = purchases ?? new IReadOnlyList<RecipeIngredient>[0];
            _rowsPerPurchase = rowsPerPurchase;
        }

        public int PurchasesMade => _bounds == null ? 0 : _bounds.DocksRows / RowsPerPurchase;

        public bool CanExpand => _bounds != null && _bounds.DocksOpen && _bounds.DocksRows < Docks.MaxNorth - Docks.StartNorth;

        public IReadOnlyList<RecipeIngredient> NextCost() =>
            _purchases.Count == 0 ? new RecipeIngredient[0] : _purchases[Math.Min(PurchasesMade, _purchases.Count - 1)];

        private PlayerWallet Wallet => _buffers == null ? null : new PlayerWallet(_buffers, _packId, _postId);

        public bool CanAfford() => Wallet != null && Wallet.CanAfford(NextCost());

        public bool TryPurchase()
        {
            LastStatusMessage = "";
            if (_bounds == null || !_bounds.DocksOpen)
            {
                LastStatusMessage = "The Docks are roped off.";
                return false;
            }
            if (!CanExpand)
            {
                LastStatusMessage = "The Docks cannot be extended any further.";
                return false;
            }
            IReadOnlyList<RecipeIngredient> cost = NextCost();
            if (Wallet != null && !Wallet.TryCharge(cost))
            {
                LastStatusMessage = UI.ConstructionCostPolicy.FormatShortfall(Wallet, cost);
                return false;
            }
            _bounds.RestoreDocks(_bounds.DocksRows + RowsPerPurchase);
            return true;
        }
    }
}
