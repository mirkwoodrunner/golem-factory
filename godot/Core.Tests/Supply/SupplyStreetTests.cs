using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Player;
using GolemFactory.PunchCards;
using GolemFactory.Save;
using GolemFactory.Supply;
using GolemFactory.Tests.Data;
using GolemFactory.Tests.EditMode;
using GolemFactory.Tests.World;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Tests.Supply
{
    /// <summary>docs/supply-design.md: the street's supply carts, step 1 (the eight new spots).</summary>
    public class SupplyStreetTests
    {
        private static readonly Vector2Int West = new Vector2Int(-20, -16);
        private static readonly Vector2Int East = new Vector2Int(20, -16);

        private SandboxWorld _world;
        private string _folder;

        [SetUp]
        public void SetUp()
        {
            _world = CrateWorld.Compose();
            _folder = Path.Combine(Path.GetTempPath(), "golem-factory-supply-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            _world.Dispose();
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, true);
            }
        }

        private SupplyStreet Street => _world.Supply;
        private CartSpot Spot(Vector2Int cell) => Street.SpotAt(cell);
        private int Pack(string item) => CrateWorld.Pack(_world, item);
        private void See(string item) => _world.TechTree.Ledger.RecordItem(item);

        private void Empty(string item) =>
            _world.Buffers.TryWithdraw(_world.StockpileBufferId, item, Pack(item));

        private SupplyCart AddCoal(Vector2Int cell)
        {
            See(ItemType.Coal);
            CrateWorld.Give(_world, ItemType.Scrap, 80);
            Assert.IsTrue(Street.Add(Spot(cell), ItemType.Coal).Done);
            return Spot(cell).Cart;
        }

        private void TickMinutes(double minutes)
        {
            long ticks = (long)Math.Round(minutes * Street.TicksPerMinute);
            for (long t = 0; t < ticks; t++)
            {
                Street.Tick(t);
            }
        }

        // --- The street as authored -------------------------------------------------------------

        [Test]
        public void TheStreet_HasEightSpots_AtItsEnds_AndNineStallsUnmoved()
        {
            CollectionAssert.AreEquivalent(
                new[] { -32, -28, -24, -20, 20, 24, 28, 32 }, Street.Spots.Select(s => s.Cell.x));
            Assert.IsTrue(Street.Spots.All(s => s.Cell.y == -16 && s.IsEmpty), "every spot roped off at the start");
            Assert.IsTrue(Street.Spots.All(s => FloorLayout.IsInsideWorld(s.Cell)), "every spot is on the street");
            Assert.AreEqual(9, _world.Markers.Count, "the nine stalls, and no cart yet");
            Assert.IsTrue(Street.Spots.All(s => _world.Markers.All(m => (int)m.Position.x != s.Cell.x)),
                "no spot sits on a stall");
        }

        [Test]
        public void EveryLadder_IsWhole_AndEveryPrice_FitsTheStartingPack()
        {
            int pack = _world.Wallet.PackCapacity;
            foreach (string item in new[] { ItemType.Scrap, ItemType.Coal, ItemType.CopperOre, ItemType.ZincOre, ItemType.Aether })
            {
                Assert.IsTrue(Street.TryGetLadder(item, out CartLadder ladder), item);
                Assert.AreEqual(3, ladder.TopRateRung, item);
                Assert.AreEqual(3, ladder.TopCapacityRung, item);
                CollectionAssert.IsOrdered(ladder.Rates, item);
                CollectionAssert.IsOrdered(ladder.Capacities, item);
                IEnumerable<RecipeIngredient> prices = ladder.AddPrice
                    .Concat(ladder.RateCosts.SelectMany(c => c)).Concat(ladder.CapacityCosts.SelectMany(c => c));
                Assert.IsTrue(prices.All(p => p.quantity > 0 && p.quantity <= pack), item + ": every line fits a 100 pack");
            }
        }

        // --- Offers and adding ------------------------------------------------------------------

        [Test]
        public void ASpot_OffersOnlyGoodsTheLedgerHasSeen()
        {
            _world.TechTree.Ledger.Clear();
            Assert.IsEmpty(Street.Offers());
            Assert.AreEqual("Nothing to sell here yet.", _world.SupplyPanel.Status(Spot(West)));
            See(ItemType.Coal);
            See(ItemType.Scrap);
            CollectionAssert.AreEqual(new[] { ItemType.Scrap, ItemType.Coal }, Street.Offers().Select(l => l.ItemType));

            SupplyResult unseen = Street.Add(Spot(West), ItemType.CopperOre);
            Assert.IsFalse(unseen.Done);
            Assert.AreEqual("Requires: Copper Ore", unseen.Message);
            Assert.IsTrue(Spot(West).IsEmpty);
        }

        [Test]
        public void AddingACart_ChargesThePack_AndPlacesASourceOnTheSpot()
        {
            int scrap = Pack(ItemType.Scrap);
            SupplyCart cart = AddCoal(West);

            Assert.AreEqual(scrap, Pack(ItemType.Scrap), "80 given, 80 charged");
            Assert.AreEqual(ItemType.Coal, cart.ItemType);
            Assert.AreEqual(0, cart.Stock, "arrives empty");
            Assert.AreEqual(30, cart.RatePerMinute);
            Assert.AreEqual(100, cart.Capacity);
            Assert.IsTrue(_world.Markers.Contains(cart.Marker), "a marker, like a stall's");
            Assert.IsTrue(_world.Endpoints.TryGetEndpoint(West, out _), "a source golems extract from");
            Assert.IsTrue(_world.Nodes.TryGetNode(Spot(West).NodeId, out ResourceNode node) && node.ItemType == ItemType.Coal);
        }

        [Test]
        public void ARefusedAdd_TakesNothing_AndSaysWhatIsShort()
        {
            See(ItemType.Coal);
            Empty(ItemType.Scrap);
            CrateWorld.Give(_world, ItemType.Scrap, 50);

            SupplyResult result = Street.Add(Spot(West), ItemType.Coal);

            Assert.IsFalse(result.Done);
            StringAssert.Contains("Need 30 more Scrap", result.Message);
            Assert.AreEqual(50, Pack(ItemType.Scrap));
            Assert.IsTrue(Spot(West).IsEmpty);
        }

        [Test]
        public void ASpotWithABuildingOnIt_RefusesACart()
        {
            CrateWorld.PlaceDepot(_world, West);
            See(ItemType.Coal);
            CrateWorld.Give(_world, ItemType.Scrap, 80);
            int scrap = Pack(ItemType.Scrap);

            SupplyResult result = Street.Add(Spot(West), ItemType.Coal);

            Assert.AreEqual("Clear the spot first.", result.Message);
            Assert.AreEqual(scrap, Pack(ItemType.Scrap));
        }

        // --- The trickle ------------------------------------------------------------------------

        [Test]
        public void TheTrickle_AddsItsRateExactly_AndStopsAtCapacity()
        {
            SupplyCart cart = AddCoal(West);
            TickMinutes(1);
            Assert.AreEqual(30, cart.Stock, "30 a minute, exactly");
            TickMinutes(0.5);
            Assert.AreEqual(45, cart.Stock);
            TickMinutes(10);
            Assert.AreEqual(100, cart.Stock, "never past capacity");
            Assert.AreEqual(0, cart.Accumulator, "a full cart holds no remainder");

            for (int i = 0; i < 7; i++)
            {
                Assert.IsTrue(cart.Marker.TryHarvest(out _));
            }
            TickMinutes(1);
            Assert.AreEqual(100, cart.Stock, "refills what was taken, up to capacity");
        }

        [Test]
        public void AFastTrickle_StillStopsAtCapacity()
        {
            // Faster than a good a tick: one tick brings several, and capacity still holds.
            var nodes = new ResourceNodeRegistry();
            var street = new SupplyStreet(nodes, new SpatialEndpointRegistry(), new GridCoordinateConverter(new Vector2(1f, 1f)),
                new List<ResourceNodeMarker>(), () => _world.Wallet, _ => true, creative: false, ticksPerMinute: 600);
            street.AddSpot(West);
            street.AddLadder(new CartLadder(ItemType.Coal, new List<RecipeIngredient>(), new[] { 6000 }, new[] { 25 },
                new List<IReadOnlyList<RecipeIngredient>>(), new List<IReadOnlyList<RecipeIngredient>>()));
            Assert.IsTrue(street.Add(street.SpotAt(West), ItemType.Coal).Done);
            street.Tick(0);
            Assert.AreEqual(10, street.SpotAt(West).Cart.Stock, "6000 a minute is 10 a tick");
            street.Tick(1);
            street.Tick(2);
            Assert.AreEqual(25, street.SpotAt(West).Cart.Stock, "never past capacity");
        }

        [Test]
        public void TheTrickle_FollowsTheClock()
        {
            SupplyCart cart = AddCoal(West);
            _world.Clock.Play();
            _world.Advance(30.05f);
            Assert.AreEqual(15, cart.Stock, "half a minute of game time, 15 Coal");
        }

        // --- Rungs ------------------------------------------------------------------------------

        [Test]
        public void Rungs_ChargeTheirPrice_ClimbOneAtATime_AndStopAtTheTop()
        {
            SupplyCart cart = AddCoal(West);
            CrateWorld.Give(_world, ItemType.IronPlate, 100);
            CrateWorld.Give(_world, ItemType.Gear, 100);
            int plate = Pack(ItemType.IronPlate);

            SupplyResult rate = Street.UpgradeRate(Spot(West));
            Assert.IsTrue(rate.Done, rate.Message);
            Assert.AreEqual("Coal cart: 60 a minute.", rate.Message);
            Assert.AreEqual(plate - 40, Pack(ItemType.IronPlate));
            Assert.IsTrue(Street.UpgradeCapacity(Spot(West)).Done);
            Assert.AreEqual(200, cart.Capacity);

            SupplyResult unpaid = Street.UpgradeRate(Spot(West));
            Assert.IsFalse(unpaid.Done, "rung 2 wants Copper Ingot and Brass");
            Assert.AreEqual(1, cart.RateRung);

            CrateWorld.Give(_world, ItemType.CopperIngot, 40);
            CrateWorld.Give(_world, ItemType.Brass, 20);
            CrateWorld.Give(_world, ItemType.Casing, 20);
            CrateWorld.Give(_world, ItemType.Mainspring, 4);
            Assert.IsTrue(Street.UpgradeRate(Spot(West)).Done);
            Assert.IsTrue(Street.UpgradeRate(Spot(West)).Done);
            Assert.AreEqual(180, cart.RatePerMinute);
            Assert.AreEqual("At the top rung.", Street.UpgradeRate(Spot(West)).Message);
            Assert.IsNull(SupplyStreet.NextRateCost(cart));
        }

        // --- Change and remove -------------------------------------------------------------------

        [Test]
        public void Removing_RefundsHalf_RoundedDown_WithTheStock()
        {
            SupplyCart cart = AddCoal(West);
            CrateWorld.Give(_world, ItemType.IronPlate, 41);
            CrateWorld.Give(_world, ItemType.Gear, 11);
            Street.UpgradeRate(Spot(West));
            TickMinutes(1);
            Assert.AreEqual(60, cart.Stock);
            int scrap = Pack(ItemType.Scrap), plate = Pack(ItemType.IronPlate), gear = Pack(ItemType.Gear), coal = Pack(ItemType.Coal);

            SupplyResult result = Street.Remove(Spot(West));

            Assert.IsTrue(result.Done, result.Message);
            Assert.AreEqual(scrap + 40, Pack(ItemType.Scrap), "half of 80");
            Assert.AreEqual(plate + 20, Pack(ItemType.IronPlate), "half of 40");
            Assert.AreEqual(gear + 5, Pack(ItemType.Gear), "half of 10");
            Assert.AreEqual(coal + 60, Pack(ItemType.Coal), "the stock comes back too");
            Assert.IsTrue(Spot(West).IsEmpty);
            Assert.IsFalse(_world.Endpoints.TryGetEndpoint(West, out _));
            Assert.IsFalse(_world.Nodes.TryGetNode(Spot(West).NodeId, out _));
            Assert.AreEqual(9, _world.Markers.Count);
        }

        [Test]
        public void Removing_LeavesAnotherEndpointOnTheCell()
        {
            AddCoal(West);
            var other = new ResourceNodeEndpoint(new ResourceNode("Other", ItemType.Scrap, 5));
            _world.Endpoints.Register(West, other);

            Assert.IsTrue(Street.Remove(Spot(West)).Done);

            Assert.IsTrue(_world.Endpoints.TryGetEndpoint(West, out IItemEndpoint still));
            Assert.AreSame(other, still, "only the cart's own endpoint goes");
        }

        [Test]
        public void AFullCart_SaysFull()
        {
            SupplyCart cart = AddCoal(West);
            TickMinutes(5);
            Assert.AreEqual("100 of 100 · full", SupplyText.Holding(cart));
        }

        [Test]
        public void AHalfRefund_RoundsDownPerGood()
        {
            SupplyCart cart = AddCoal(West);
            CrateWorld.Give(_world, ItemType.IronPlate, 60);
            CrateWorld.Give(_world, ItemType.Gear, 15);
            Assert.IsTrue(Street.UpgradeRate(Spot(West)).Done, "40 Plate + 10 Gear");
            Assert.IsTrue(Street.UpgradeCapacity(Spot(West)).Done, "20 Plate + 5 Gear");
            List<RecipeIngredient> half = SupplyStreet.HalfRefund(cart);
            Assert.AreEqual(7, half.Single(h => h.itemType == ItemType.Gear).quantity, "15 Gear paid, 7 back");
            Assert.AreEqual(30, half.Single(h => h.itemType == ItemType.IronPlate).quantity);
            Assert.AreEqual(40, half.Single(h => h.itemType == ItemType.Scrap).quantity);
        }

        [Test]
        public void ARemovalThePackCantHold_IsRefused_AndChangesNothing()
        {
            SupplyCart cart = AddCoal(West);
            TickMinutes(2);
            CrateWorld.Give(_world, ItemType.Coal, _world.Wallet.RoomFor(ItemType.Coal));
            int scrap = Pack(ItemType.Scrap);

            SupplyResult result = Street.Remove(Spot(West));

            Assert.AreEqual("Not removed: pack full of Coal.", result.Message);
            Assert.AreSame(cart, Spot(West).Cart);
            Assert.AreEqual(60, cart.Stock);
            Assert.AreEqual(scrap, Pack(ItemType.Scrap));
        }

        [Test]
        public void Changing_PaysTheNewGood_KeepsTheRungs_AndReturnsTheStock()
        {
            SupplyCart coal = AddCoal(West);
            CrateWorld.Give(_world, ItemType.IronPlate, 40);
            CrateWorld.Give(_world, ItemType.Gear, 10);
            Street.UpgradeRate(Spot(West));
            TickMinutes(1);
            See(ItemType.Scrap);
            CrateWorld.Give(_world, ItemType.Scrap, 60);
            int scrap = Pack(ItemType.Scrap), coalHeld = Pack(ItemType.Coal);

            SupplyResult result = Street.Change(Spot(West), ItemType.Scrap);

            Assert.IsTrue(result.Done, result.Message);
            SupplyCart now = Spot(West).Cart;
            Assert.AreEqual(ItemType.Scrap, now.ItemType);
            Assert.AreEqual(1, now.RateRung, "its rung stays");
            Assert.AreEqual(180, now.RatePerMinute, "the Scrap ladder's rung 1");
            Assert.AreEqual(0, now.Stock, "starts empty as its new good");
            Assert.AreEqual(scrap - 60, Pack(ItemType.Scrap), "the Scrap cart's price");
            Assert.AreEqual(coalHeld + 60, Pack(ItemType.Coal), "the Coal it held comes back");
            Assert.AreEqual(ItemType.Scrap, _world.Nodes.TryGetNode(Spot(West).NodeId, out ResourceNode node) ? node.ItemType : null);

            // What a removal halves now: the Scrap cart's price and the rung, not the Coal's price.
            List<RecipeIngredient> half = SupplyStreet.HalfRefund(now);
            Assert.AreEqual(30, half.Single(h => h.itemType == ItemType.Scrap).quantity);
            Assert.AreEqual(20, half.Single(h => h.itemType == ItemType.IronPlate).quantity);
            Assert.AreNotSame(coal, now);
        }

        [Test]
        public void AKeptGood_KeepsItsLastCart()
        {
            Street.PlaceStartingCart(East, ItemType.Scrap);
            SupplyCart start = Spot(East).Cart;
            See(ItemType.Scrap);
            Assert.AreEqual(start.Capacity, start.Stock, "a starting cart starts full");
            See(ItemType.Coal);

            Assert.AreEqual("The street needs one Scrap cart.", Street.Remove(Spot(East)).Message);
            Assert.AreEqual("The street needs one Scrap cart.", Street.Change(Spot(East), ItemType.Coal).Message);

            CrateWorld.Give(_world, ItemType.Scrap, 60);
            Assert.IsTrue(Street.Add(Spot(West), ItemType.Scrap).Done);
            Empty(ItemType.Scrap);
            start.Node.RestoreQuantity(0);
            Assert.IsTrue(Street.Remove(Spot(East)).Done, "with a second Scrap cart, the first can go");
            Assert.AreEqual("The street needs one Scrap cart.", Street.Remove(Spot(West)).Message);
        }

        // --- Saves ------------------------------------------------------------------------------

        [Test]
        public void ASave_RestoresEveryCartExactly_AndTheTrickleCarriesOn()
        {
            SupplyCart cart = AddCoal(West);
            CrateWorld.Give(_world, ItemType.IronPlate, 40);
            CrateWorld.Give(_world, ItemType.Gear, 10);
            Street.UpgradeRate(Spot(West));
            for (int t = 0; t < 1234; t++)
            {
                Street.Tick(t);
            }
            int stock = cart.Stock;
            long remainder = cart.Accumulator;
            string path = Path.Combine(_folder, "supply.json");
            Directory.CreateDirectory(_folder);
            _world.SaveTo(path);

            Street.Remove(Spot(West));
            Street.Add(Spot(East), ItemType.Coal);
            StringAssert.StartsWith("Loaded", _world.LoadFrom(path));

            Assert.IsTrue(Spot(East).IsEmpty, "a load replaces the street's carts");
            SupplyCart back = Spot(West).Cart;
            Assert.IsNotNull(back);
            Assert.AreEqual(ItemType.Coal, back.ItemType);
            Assert.AreEqual(1, back.RateRung);
            Assert.AreEqual(stock, back.Stock);
            Assert.AreEqual(remainder, back.Accumulator);
            CollectionAssert.AreEqual(cart.Paid.Select(p => p.itemType + p.quantity), back.Paid.Select(p => p.itemType + p.quantity));
            Assert.IsTrue(_world.Endpoints.TryGetEndpoint(West, out _));
            Assert.AreEqual(1, _world.Markers.Count(m => m.NodeId == Spot(West).NodeId), "one marker, not two");

            SaveData data = SaveFileIO.ReadFromFile(path);
            Assert.AreEqual(1, data.supply.Count);
            Assert.IsFalse(data.market.stallIds.Contains(Spot(West).NodeId), "a cart is never saved as a stall");
        }

        [Test]
        public void ASaveFromBeforeCarts_LoadsWithTheStreetAtItsStart()
        {
            AddCoal(West);
            string path = Path.Combine(_folder, "old.json");
            Directory.CreateDirectory(_folder);
            _world.SaveTo(path);
            SaveData data = SaveFileIO.ReadFromFile(path);
            data.supply = null;
            SaveFileIO.WriteToFile(data, path);

            StringAssert.StartsWith("Loaded", _world.LoadFrom(path));

            Assert.IsTrue(Street.Spots.All(s => s.IsEmpty));
            Assert.AreEqual(9, _world.Markers.Count);
        }

        // --- Creative ---------------------------------------------------------------------------

        [Test]
        public void Creative_CartsCostNothing_AndNeverRunOut()
        {
            SandboxWorld creative = CreativeTests.Compose(creative: true);
            try
            {
                CartSpot spot = creative.Supply.SpotAt(West);
                int scrap = creative.Buffers.GetQuantity(creative.StockpileBufferId, ItemType.Scrap);

                Assert.IsTrue(creative.Supply.Add(spot, ItemType.Coal).Done);
                Assert.IsTrue(creative.Supply.UpgradeRate(spot).Done);

                Assert.AreEqual(scrap, creative.Buffers.GetQuantity(creative.StockpileBufferId, ItemType.Scrap));
                Assert.IsTrue(spot.Cart.IsInfinite);
                Assert.IsTrue(spot.Cart.Marker.TryHarvest(out _));
                Assert.AreEqual("never runs out", SupplyText.Holding(spot.Cart));
            }
            finally
            {
                creative.Dispose();
            }
        }

        // --- The panel and the keys -----------------------------------------------------------

        [Test]
        public void ThePanel_ListsWhatASpotOffers_AndWhatACartCanDo()
        {
            See(ItemType.Coal);
            See(ItemType.Scrap);
            Empty(ItemType.Scrap);
            CrateWorld.Give(_world, ItemType.Scrap, 70);
            IReadOnlyList<SupplyRow> offers = _world.SupplyPanel.Rows(Spot(West));
            Assert.AreEqual("Cart spot", _world.SupplyPanel.Title(Spot(West)));
            CollectionAssert.AreEqual(new[] { "Scrap cart", "Coal cart" }, offers.Select(r => r.Label));
            Assert.IsTrue(offers[0].Enabled, "60 Scrap: affordable");
            Assert.IsFalse(offers[1].Enabled, "80 Scrap: not");
            Assert.AreEqual("Need 10 more Scrap", offers[1].Note);
            Assert.AreEqual("30 a minute · holds 100", offers[1].Detail);

            CrateWorld.Give(_world, ItemType.Scrap, 10);
            Assert.IsTrue(_world.SupplyPanel.Press(Spot(West), _world.SupplyPanel.Rows(Spot(West))[1]).Done);
            IReadOnlyList<SupplyRow> rows = _world.SupplyPanel.Rows(Spot(West));
            Assert.AreEqual("Coal cart", _world.SupplyPanel.Title(Spot(West)));
            Assert.AreEqual("0 of 100 · +30 a minute", _world.SupplyPanel.Status(Spot(West)));
            CollectionAssert.AreEqual(new[] { "Rate", "Capacity", "Change to Scrap cart", "Remove" }, rows.Select(r => r.Label));
            Assert.AreEqual("30 to 60 a minute", rows[0].Detail);
            Assert.AreEqual("returns 40 Scrap", rows[3].Price);
            Assert.IsTrue(rows[3].Enabled);
        }

        [Test]
        public void AnUnseenGoodInAPrice_ReadsRequires()
        {
            AddCoal(West);
            _world.TechTree.Ledger.Clear();
            See(ItemType.Coal);
            SupplyRow rate = _world.SupplyPanel.Rows(Spot(West)).First(r => r.Action == SupplyAction.Rate);
            Assert.IsFalse(rate.Enabled);
            Assert.AreEqual("Requires: Iron Plate, Gear", rate.Price);
        }

        private sealed class FakeSupplyScreen : ISupplyScreen
        {
            public CartSpot Opened;
            public bool IsOpen => Opened != null;
            public void Open(CartSpot spot) => Opened = spot;
        }

        [Test]
        public void E_AtAnEmptySpot_AndI_AtACart_OpenThePanel()
        {
            var screen = new FakeSupplyScreen();
            _world.ConfigureSupplyScreen(screen);
            _world.Interactor.Position = new Vector3(West.x, West.y + 1f, 0f);
            _world.Interactor.RefreshInteractables();
            _world.Interactor.RefreshAffordance();
            Assert.AreEqual(InteractionKind.Supply, _world.Interactor.CurrentPick.Kind);
            StringAssert.StartsWith("[E]  Add a cart at this spot", _world.Interactor.CurrentPrompt);

            Assert.IsTrue(_world.Interactor.Interact());
            Assert.AreSame(Spot(West), screen.Opened);

            screen.Opened = null;
            SupplyCart cart = AddCoal(West);
            TickMinutes(1);
            _world.Interactor.RefreshAffordance();
            Assert.AreEqual(InteractionKind.Harvest, _world.Interactor.CurrentPick.Kind, "a cart is taken from, like a stall");
            StringAssert.Contains("Coal cart  -  30 of 100 · +30 a minute · [I] open", _world.Interactor.CurrentPrompt);
            Assert.IsTrue(_world.Interactor.Interact());
            Assert.AreEqual(29, cart.Stock, "E took one");
            Assert.IsNull(screen.Opened);

            Assert.IsTrue(_world.Interactor.OpenInventory());
            Assert.AreSame(Spot(West), screen.Opened);
        }
    }
}
