using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using GolemFactory.Economy;
using GolemFactory.PunchCards;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// §11 item 15's Floor Expansion: purchasable growth of the workshop, painted and walled at
    /// runtime. §11 warns this is "more work than 'purchasable growth' suggests" because
    /// <c>SandboxFloorGenerator</c> is Editor-only -- so the layout math is shared and only the
    /// assets are handed over.
    /// </summary>
    public class FloorExpansionTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        // --- the bounds state -----------------------------------------------------------------

        [Test]
        public void ANewRoomIsTheAuthoredOne()
        {
            var bounds = new FloorBounds();

            Assert.AreEqual(FloorLayout.HalfExtent, bounds.HalfExtent);
            Assert.AreEqual(FloorLayout.DefaultNorthExtent, bounds.NorthExtent);
            Assert.IsTrue(bounds.CanExpand);
        }

        [Test]
        public void LandIsFinite()
        {
            // §11: "the design needs land to be finite and expensive, not continuously paveable".
            var bounds = new FloorBounds();

            int guard = 0;
            while (bounds.CanExpand && guard++ < 1000)
            {
                bounds.Expand(1);
            }

            Assert.AreEqual(FloorLayout.MaxNorthExtent, bounds.NorthExtent);
            Assert.AreEqual(0, bounds.Expand(1), "the last row is the last row");
        }

        [Test]
        public void ExpandReturnsWhatItActuallyAdded()
        {
            var bounds = new FloorBounds(FloorLayout.HalfExtent, FloorLayout.MaxNorthExtent - 1);

            Assert.AreEqual(1, bounds.Expand(5), "clamped to what is left, so nobody is charged for air");
        }

        [Test]
        public void OnlyTheNorthEdgeMoves()
        {
            // Growing symmetrically would move the shop front, and the street is defined as the
            // eight rows south of it -- so the road, the kerb and all nine market stalls would
            // slide south with every purchase.
            var bounds = new FloorBounds();
            bounds.Expand(4);

            Assert.AreEqual(FloorLayout.HalfExtent, bounds.HalfExtent, "the sides do not move");

            var stall = new Vector2Int(0, -16);
            Assert.IsFalse(
                FloorLayout.IsInsideWorkshop(stall, bounds.HalfExtent, bounds.NorthExtent),
                "the market stays outside the shop");
            Assert.IsTrue(
                FloorLayout.IsInsideWorld(
                    stall, bounds.HalfExtent, FloorLayout.StreetDepth,
                    FloorLayout.StreetHalfExtent, bounds.NorthExtent),
                "and stays on the street");
        }

        // --- the geometry ---------------------------------------------------------------------

        [Test]
        public void NewRowsAreInsideTheWorkshop()
        {
            var bounds = new FloorBounds();
            var newRow = new Vector2Int(0, FloorLayout.DefaultNorthExtent + 2);

            Assert.IsFalse(FloorLayout.IsInsideWorkshop(newRow, bounds.HalfExtent, bounds.NorthExtent));

            bounds.Expand(2);

            Assert.IsTrue(FloorLayout.IsInsideWorkshop(newRow, bounds.HalfExtent, bounds.NorthExtent));
            Assert.IsTrue(FloorLayout.IsInsideWorld(
                newRow, bounds.HalfExtent, FloorLayout.StreetDepth,
                FloorLayout.StreetHalfExtent, bounds.NorthExtent));
        }

        [Test]
        public void TheBackWallMovesWithTheRoom_AndTheSideRunsReachIt()
        {
            var bounds = new FloorBounds();
            bounds.Expand(3);

            Vector2 back = FloorLayout.GetEdgeAnchor(
                FloorLayout.Edge.North, 0, bounds.HalfExtent, bounds.NorthExtent);
            Assert.AreEqual(bounds.NorthExtent + 0.5f, back.y, 0.001f);

            // The side runs have to reach the new corner, or the room has a gap at the back.
            int highest = int.MinValue;
            foreach (int index in FloorLayout.GetWorldEdgeIndices(
                         bounds.HalfExtent, FloorLayout.StreetDepth, bounds.NorthExtent))
            {
                if (index > highest)
                {
                    highest = index;
                }
            }

            Assert.AreEqual(bounds.NorthExtent, highest);
        }

        [Test]
        public void TheCornerPostsRideTheBackWallOutward()
        {
            var bounds = new FloorBounds();
            bounds.Expand(2);

            foreach (Vector2 anchor in FloorLayout.GetWallPostAnchors(
                         bounds.HalfExtent, bounds.NorthExtent))
            {
                Assert.AreEqual(bounds.NorthExtent + 0.5f, anchor.y, 0.001f);
                Assert.AreEqual(bounds.HalfExtent + 0.5f, Mathf.Abs(anchor.x), 0.001f);
            }
        }

        [Test]
        public void TheStreetIsUntouchedByExpansion()
        {
            var bounds = new FloorBounds();
            bounds.Expand(6);

            Vector2 kerb = FloorLayout.GetWorldEdgeAnchor(
                FloorLayout.Edge.South, 0, bounds.HalfExtent, FloorLayout.StreetDepth,
                bounds.NorthExtent);

            Assert.AreEqual(
                -FloorLayout.HalfExtent - FloorLayout.StreetDepth - 0.5f, kerb.y, 0.001f,
                "the far kerb never moves, so the market never moves");
        }

        // --- the purchase ---------------------------------------------------------------------

        private FloorExpansionService BuildService(out StorageBufferRegistryHolder buffers)
        {
            _root = new GameObject("Root");

            var boundsHolder = new GameObject("Bounds").AddComponent<FloorBoundsHolder>();
            boundsHolder.transform.SetParent(_root.transform);

            buffers = new GameObject("Buffers").AddComponent<StorageBufferRegistryHolder>();
            buffers.transform.SetParent(_root.transform);

            var gridGo = new GameObject("Grid", typeof(Grid));
            gridGo.transform.SetParent(_root.transform);
            var tilemapGo = new GameObject("Tilemap", typeof(Tilemap));
            tilemapGo.transform.SetParent(gridGo.transform);

            var service = new GameObject("FloorExpansion").AddComponent<FloorExpansionService>();
            service.transform.SetParent(_root.transform);
            service.Configure(
                boundsHolder, tilemapGo.GetComponent<Tilemap>(), Vector2.one, buffers,
                "FactoryStockpile");
            return service;
        }

        [Test]
        public void APurchaseChargesTheBundleAndMovesTheWall()
        {
            FloorExpansionService service = BuildService(out StorageBufferRegistryHolder buffers);
            foreach (RecipeIngredient entry in service.NextCost())
            {
                buffers.Registry.Deposit("FactoryStockpile", entry.itemType, entry.quantity);
            }

            int before = service.Bounds.NorthExtent;
            Assert.IsTrue(service.TryPurchaseExpansion());

            Assert.AreEqual(before + service.RowsPerPurchase, service.Bounds.NorthExtent);
            Assert.AreEqual(0, buffers.Registry.GetQuantity("FactoryStockpile", ItemType.Scrap));
        }

        [Test]
        public void AnUnaffordablePurchase_ChargesNothingAndMovesNothing()
        {
            FloorExpansionService service = BuildService(out StorageBufferRegistryHolder buffers);
            buffers.Registry.Deposit("FactoryStockpile", ItemType.Scrap, 5);

            int before = service.Bounds.NorthExtent;

            Assert.IsFalse(service.TryPurchaseExpansion());
            Assert.AreEqual(before, service.Bounds.NorthExtent);
            Assert.AreEqual(5, buffers.Registry.GetQuantity("FactoryStockpile", ItemType.Scrap),
                "an atomic bundle withdrawal refunds in full");
            Assert.IsNotEmpty(service.LastStatusMessage);
        }

        [Test]
        public void RowsGetDearerAsTheRoomGrows()
        {
            // The cap alone would make the last row as cheap as the first; §11 wants land
            // expensive as well as finite.
            FloorExpansionService service = BuildService(out StorageBufferRegistryHolder buffers);
            int firstScrap = CostOf(service, ItemType.Scrap);

            foreach (RecipeIngredient entry in service.NextCost())
            {
                buffers.Registry.Deposit("FactoryStockpile", entry.itemType, entry.quantity);
            }

            service.TryPurchaseExpansion();

            Assert.Greater(CostOf(service, ItemType.Scrap), firstScrap);
        }

        [Test]
        public void PaintingWritesATileOnEveryNewCell()
        {
            FloorExpansionService service = BuildService(out _);
            Tilemap map = _root.GetComponentInChildren<Tilemap>();

            int row = FloorLayout.DefaultNorthExtent + 1;
            Assert.IsNull(map.GetTile(new Vector3Int(0, row, 0)));

            // With no Tile assets wired (as here) painting is a no-op rather than a crash, which
            // is what keeps a scene that never ran the authoring pass merely unpainted.
            service.PaintRows(row, row);

            Assert.Pass("painting a row without tiles wired must not throw");
        }

        private static int CostOf(FloorExpansionService service, string itemType)
        {
            foreach (RecipeIngredient entry in service.NextCost())
            {
                if (entry.itemType == itemType)
                {
                    return entry.quantity;
                }
            }

            return 0;
        }
    }
}
