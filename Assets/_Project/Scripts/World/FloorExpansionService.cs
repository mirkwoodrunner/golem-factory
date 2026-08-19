using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using GolemFactory.Economy;
using GolemFactory.PunchCards;

namespace GolemFactory.World
{
    /// <summary>
    /// Buys and builds §11 item 15's Floor Expansion: pushes the workshop's back wall north,
    /// paints the new plank rows onto the Tilemap and moves the wall run to the new edge --
    /// <b>at runtime</b>, which is the part §11 flags as "more work than 'purchasable growth'
    /// suggests".
    ///
    /// <para>
    /// <b>Why it cannot just call the generator.</b> <c>SandboxFloorGenerator</c> lives in
    /// <c>GolemFactory.Editor</c> and is unavailable in a build, so the shell it paints is
    /// authored-time only. What IS shared is the layout math: this walks the same
    /// <see cref="FloorLayout"/> methods and the same <see cref="FloorTileVariant"/> chooser the
    /// generator does, so an expanded row is tiled by the identical rule as an authored one and
    /// the seam is invisible. Only the two things Editor code owns -- Tile assets and wall
    /// sprites -- are handed over as serialized references.
    /// </para>
    ///
    /// <para>
    /// <b>It repaints only the delta.</b> Expansion only ever adds rows, so the existing floor is
    /// left alone; the walls are the exception, because the back wall and its two corner posts
    /// have MOVED rather than grown, and the side runs have to reach the new corner. Those are
    /// rebuilt wholesale from data -- the same "always re-render from data" idiom
    /// <c>WorkbenchController.RebuildUI</c> and <c>BeltNetwork.Relink</c> follow -- because a
    /// diff of wall segments is a second definition of where a wall goes.
    /// </para>
    /// </summary>
    public sealed class FloorExpansionService : MonoBehaviour
    {
        [SerializeField] private FloorBoundsHolder boundsHolder;
        [SerializeField] private Tilemap tilemap;

        [Tooltip("Floor tile variants, in FloorTileVariant.Select order. Wired by the authoring " +
                 "pass from the same Tile assets the generator paints with.")]
        [SerializeField] private TileBase[] floorTiles = new TileBase[0];

        [Header("Walls")]
        [SerializeField] private Transform wallsParent;
        [SerializeField] private Sprite northWallSprite;
        [SerializeField] private Sprite northWallLampSprite;
        [SerializeField] private Sprite eastWallSprite;
        [SerializeField] private Sprite westWallSprite;
        [SerializeField] private Sprite cornerPostSprite;
        [SerializeField] private int lampSpacing = 6;

        // The lit sprite material the authored walls use. Without it a rebuilt segment renders
        // unlit beside its neighbours -- the seam this whole class exists to avoid.
        [SerializeField] private Material wallMaterial;

        [Header("Cost")]
        // TUNING, not derived: §11 prices Floor Expansion at nothing, and §5.1 only records that
        // Iron Plate is among its inputs. Rows get dearer as the room grows, which is what "land
        // is finite and expensive" asks for -- the cap alone would make the last row as cheap as
        // the first.
        [SerializeField] private int scrapPerRow = 40;
        [SerializeField] private int ironPlatePerRow = 20;
        [SerializeField] private int rowsPerPurchase = 2;
        [SerializeField] private string stockpileBufferId = "FactoryStockpile";
        [SerializeField] private StorageBufferRegistryHolder bufferRegistryHolder;

        private GridCoordinateConverter _converter = new GridCoordinateConverter(Vector2.one);

        /// <summary>Set when a purchase is refused, for the panel to show.</summary>
        public string LastStatusMessage { get; private set; } = "";

        public FloorBounds Bounds => boundsHolder != null ? boundsHolder.Bounds : null;

        public int RowsPerPurchase => rowsPerPurchase < 1 ? 1 : rowsPerPurchase;

        public void Configure(
            FloorBoundsHolder bounds, Tilemap map, Vector2 cellSize,
            StorageBufferRegistryHolder buffers, string bufferId)
        {
            boundsHolder = bounds;
            tilemap = map;
            _converter = new GridCoordinateConverter(cellSize);
            bufferRegistryHolder = buffers;
            if (!string.IsNullOrEmpty(bufferId))
            {
                stockpileBufferId = bufferId;
            }
        }

        /// <summary>
        /// What the next expansion costs. Scales with how far the room has already grown, so the
        /// tenth row is a decision rather than a formality.
        /// </summary>
        public IReadOnlyList<RecipeIngredient> NextCost()
        {
            FloorBounds bounds = Bounds;
            int rowsGrown = bounds == null ? 0 : bounds.NorthExtent - FloorLayout.DefaultNorthExtent;
            int step = 1 + rowsGrown / RowsPerPurchase;

            return new[]
            {
                new RecipeIngredient(ItemType.Scrap, scrapPerRow * RowsPerPurchase * step),
                new RecipeIngredient(ItemType.IronPlate, ironPlatePerRow * RowsPerPurchase * step),
            };
        }

        private int StockOf(string itemType) =>
            bufferRegistryHolder == null
                ? 0
                : bufferRegistryHolder.Registry.GetQuantity(stockpileBufferId, itemType);

        public bool CanAfford() =>
            GolemFactory.UI.ConstructionCostPolicy.CanAfford(StockOf, NextCost());

        /// <summary>
        /// Charges for and builds one expansion. Public so a test and a button drive the same
        /// path, as <c>BuildModeController.PlaceOrRemove</c> is.
        /// </summary>
        public bool TryPurchaseExpansion()
        {
            LastStatusMessage = "";
            FloorBounds bounds = Bounds;
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

            // Atomic with a full refund on shortfall, like every other purchase in the game. And
            // charged BEFORE the room grows, so a failed paint can never leave a paid-for
            // expansion half-built -- the rows are the cheap half, the charge is the one that
            // must not happen twice.
            if (bufferRegistryHolder != null &&
                !bufferRegistryHolder.Registry.TryWithdrawBundle(stockpileBufferId, cost))
            {
                LastStatusMessage = GolemFactory.UI.ConstructionCostPolicy.FormatShortfall(StockOf, cost);
                return false;
            }

            int previousNorth = bounds.NorthExtent;
            int added = bounds.Expand(RowsPerPurchase);
            if (added <= 0)
            {
                // Refund: the cap moved under us. Cannot happen through this method's own guard,
                // but a charge with nothing delivered is the one outcome worth being certain of.
                if (bufferRegistryHolder != null)
                {
                    for (int i = 0; i < cost.Count; i++)
                    {
                        bufferRegistryHolder.Registry.Deposit(
                            stockpileBufferId, cost[i].itemType, cost[i].quantity);
                    }
                }

                return false;
            }

            PaintRows(previousNorth + 1, bounds.NorthExtent);
            RebuildWalls();
            return true;
        }

        /// <summary>
        /// Paints the plank rows from <paramref name="fromRow"/> to <paramref name="toRow"/>
        /// inclusive, using the same variant chooser the authored floor uses.
        /// </summary>
        public void PaintRows(int fromRow, int toRow)
        {
            FloorBounds bounds = Bounds;
            if (tilemap == null || floorTiles == null || floorTiles.Length == 0 || bounds == null)
            {
                return;
            }

            for (int y = fromRow; y <= toRow; y++)
            {
                for (int x = -bounds.HalfExtent; x <= bounds.HalfExtent; x++)
                {
                    int variant = FloorTileVariant.Select(x, y);
                    TileBase tile = floorTiles[variant % floorTiles.Length];
                    tilemap.SetTile(new Vector3Int(x, y, 0), tile);
                }
            }
        }

        /// <summary>
        /// Rebuilds the back wall, the side runs and the two corner posts at the current extent.
        ///
        /// <para>
        /// Wholesale, not diffed: the back wall MOVES rather than grows, so a diff would have to
        /// know which segments were the old back wall and which the sides -- a second definition
        /// of where a wall goes, and the first thing to drift from <see cref="FloorLayout"/>.
        /// </para>
        /// </summary>
        public void RebuildWalls()
        {
            FloorBounds bounds = Bounds;
            if (wallsParent == null || bounds == null)
            {
                return;
            }

            // Only the pieces this service owns are cleared. The skirting, the kerb, the
            // shoulders and the street's own edges are all anchored to the SHOP FRONT, which
            // expansion never moves, so destroying them would mean rebuilding art that did not
            // change -- and every one of them needs a sprite this component would then have to
            // carry a reference to.
            var doomed = new List<GameObject>();
            for (int i = 0; i < wallsParent.childCount; i++)
            {
                Transform child = wallsParent.GetChild(i);
                if (child.name.StartsWith("WallNorth_") ||
                    child.name.StartsWith("WallEast_") ||
                    child.name.StartsWith("WallWest_") ||
                    child.name.StartsWith("WallPost_"))
                {
                    doomed.Add(child.gameObject);
                }
            }

            for (int i = 0; i < doomed.Count; i++)
            {
                if (Application.isPlaying)
                {
                    Destroy(doomed[i]);
                }
                else
                {
                    DestroyImmediate(doomed[i]);
                }
            }

            foreach (int index in FloorLayout.GetWorldEdgeIndices(
                         bounds.HalfExtent, FloorLayout.StreetDepth, bounds.NorthExtent))
            {
                PlaceWall("WallEast_" + index, eastWallSprite, FloorLayout.GetWorldEdgeAnchor(
                    FloorLayout.Edge.East, index, bounds.HalfExtent, FloorLayout.StreetDepth,
                    bounds.NorthExtent));
                PlaceWall("WallWest_" + index, westWallSprite, FloorLayout.GetWorldEdgeAnchor(
                    FloorLayout.Edge.West, index, bounds.HalfExtent, FloorLayout.StreetDepth,
                    bounds.NorthExtent));
            }

            foreach (int index in FloorLayout.GetEdgeIndices(bounds.HalfExtent))
            {
                bool lit = lampSpacing > 0 && Mod(index, lampSpacing) == 0;
                PlaceWall(
                    "WallNorth_" + index,
                    lit && northWallLampSprite != null ? northWallLampSprite : northWallSprite,
                    FloorLayout.GetEdgeAnchor(
                        FloorLayout.Edge.North, index, bounds.HalfExtent, bounds.NorthExtent));
            }

            int post = 0;
            foreach (Vector2 anchor in FloorLayout.GetWallPostAnchors(
                         bounds.HalfExtent, bounds.NorthExtent))
            {
                PlaceWall("WallPost_" + post, cornerPostSprite, anchor, sortingBias: 1);
                post++;
            }
        }

        private void PlaceWall(string name, Sprite sprite, Vector2 anchor, int sortingBias = 0)
        {
            if (sprite == null)
            {
                return;
            }

            var go = new GameObject(name);
            go.transform.SetParent(wallsParent, false);
            go.transform.position = _converter.CellFractionToWorld(anchor);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;

            // A BAKED sorting order, not a YSortSpriteRenderer, and that matches the authored
            // walls exactly: a wall never moves, so paying for a LateUpdate per segment to
            // recompute a constant would be waste. The bias breaks the tie between a corner post
            // and the wall segments it caps, which share a world Y and would otherwise flip
            // between rebuilds -- Unity gives equal sorting orders no defined draw order.
            renderer.sharedMaterial = wallMaterial != null ? wallMaterial : renderer.sharedMaterial;
            renderer.sortingOrder =
                YSortUtility.ComputeSortingOrder(go.transform.position.y) + sortingBias;
        }

        // Floor-mod, so a negative index lights the same segments a positive one does.
        private static int Mod(int value, int modulus) => ((value % modulus) + modulus) % modulus;
    }
}
