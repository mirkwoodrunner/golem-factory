using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using GolemFactory.Buildings;
using GolemFactory.ClockTower;
using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.Menus;
using GolemFactory.Overview;
using GolemFactory.Supply;
using GolemFactory.Tests.EditMode;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Tests.Overview
{
    /// <summary>docs/supply-design.md §4: goods spent, counted where the factory uses them up.</summary>
    public class GoodsSpentTests
    {
        private ConsumptionTally _tally;

        [SetUp]
        public void SetUp()
        {
            _tally = new ConsumptionTally();
            _tally.Attach();
        }

        [TearDown]
        public void TearDown() => _tally.Detach();

        private int Spent(string item) => _tally.Totals.TryGetValue(item, out int n) ? n : 0;

        [Test]
        public void ASlagHeap_SpendsTheSlagItVoids_AndTheCokeItBurns()
        {
            var heap = new SlagHeap("Heap", startingCoke: 2, slagPerCoke: 4);
            for (int i = 0; i < 5; i++)
            {
                Assert.IsTrue(heap.TryVoid());
            }
            Assert.AreEqual(5, Spent(ItemType.Slag));
            Assert.AreEqual(1, Spent(ItemType.Coke), "one Coke on the fourth Slag");
        }

        [Test]
        public void ARecycler_SpendsWhatItTakes_AndTheCokeItBurns()
        {
            var recycler = new ScrapRecycler("Recycler", startingCoke: 5);
            int taken = 0;
            while (recycler.ScrapStock == 0 && taken < 20)
            {
                Assert.IsTrue(recycler.TryRecycle(ItemType.Slag));
                taken++;
            }
            Assert.AreEqual(taken, Spent(ItemType.Slag));
            Assert.AreEqual(ScrapRecycler.CokePerScrap * recycler.ScrapStock, Spent(ItemType.Coke));
            Assert.AreEqual(0, Spent(ItemType.Scrap), "what it makes isn't spent");
        }

        [Test]
        public void TheTower_SpendsWhatItIsDelivered_AndNothingItRefuses()
        {
            SandboxWorld world = CrateWorld.Compose();
            try
            {
                ClockTowerSite site = world.ClockTower.Site;
                site.RestoreProgress(1, 0L, false);
                string good = site.ActiveStage.demands[0].itemType;
                Assert.AreEqual(3, site.RecordDelivery(good, 3, 10));
                Assert.AreEqual(3, Spent(good));
                Assert.AreEqual(0, site.RecordDelivery(ItemType.Scrap, 3, 11), "precondition: the stage doesn't take Scrap");
                Assert.AreEqual(0, Spent(ItemType.Scrap));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void ARunningFactory_SpendsItsRecipesInputs_AndItsBoilersCoke_WhileItsGolemsOnlyMoveGoods()
        {
            SandboxWorld world = CrateWorld.Compose();
            try
            {
                world.Clock.Play();
                TitleShowcase.Stage(world, 6);
                int coke = Spent(ItemType.Coke);
                for (int i = 0; i < 600; i++)
                {
                    world.Advance(1f / 30f);
                }
                Assert.Greater(Spent(ItemType.Coal), 0, "the coker's Coal");
                Assert.Greater(Spent(ItemType.Coke), coke, "the boilers burn Coke");
                Assert.AreEqual(Spent(ItemType.Coal), world.Consumption.Totals[ItemType.Coal], "the world's own tally agrees");

                IReadOnlyList<string> goods = world.History.Goods(HistoryRange.TenMinutes);
                CollectionAssert.Contains(goods, ItemType.Coal, "a good only spent is still on the graph");
                Assert.AreEqual(0f, world.History.Average(ItemType.Coal, HistoryRange.TenMinutes, ChartMeasure.Made), "nothing makes Coal");
                Assert.Greater(world.History.Average(ItemType.Coal, HistoryRange.TenMinutes, ChartMeasure.Spent), 0f);
            }
            finally
            {
                world.Dispose();
            }
        }
    }

    public class SpentOnTheGraphTests
    {
        private static Dictionary<string, int> Totals(params (string good, int n)[] counts) => counts.ToDictionary(c => c.good, c => c.n);

        private static ProductionHistory History()
        {
            var history = new ProductionHistory();
            history.Record(0f, Totals(), Totals());
            for (int b = 1; b <= 12; b++)
            {
                // Coke: 2 made and 3 spent a bucket. Coal: only spent, 1 a bucket. Gear: made only.
                history.Record(b * 10f, Totals(("Coke", 2 * b), ("Gear", 5 * b)), Totals(("Coke", 3 * b), ("Coal", b)));
            }
            return history;
        }

        [Test]
        public void SpentIsItsOwnSeries_AndTheGoodsAreTheMostMadeThenTheMostSpent()
        {
            ProductionHistory history = History();
            Assert.AreEqual(12f, history.Rates("Coke", HistoryRange.TenMinutes)[ProductionHistory.Points - 1], 1e-4);
            Assert.AreEqual(18f, history.Rates("Coke", HistoryRange.TenMinutes, ChartMeasure.Spent)[ProductionHistory.Points - 1], 1e-4);
            Assert.AreEqual(6f, history.Average("Coal", HistoryRange.TenMinutes, ChartMeasure.Spent), 1e-4);
            CollectionAssert.AreEqual(new[] { "Gear", "Coke", "Coal" }, history.Goods(HistoryRange.TenMinutes));
        }

        [Test]
        public void TheMeasure_ChangesWhatTheLinesCount_NotTheirColours_AndTheLegendShowsBoth()
        {
            var chart = new ProductionChart(History());
            chart.Refresh();
            CollectionAssert.AreEqual(new[] { "Gear", "Coke", "Coal" }, chart.Shown);
            Assert.AreEqual(50f, chart.AxisTop(), "Gear's 30 a minute made, rounded up");
            StringAssert.EndsWith(" · made", chart.HoverLines(ProductionHistory.Points - 1)[0]);
            Assert.AreEqual("Goods made a minute", chart.Heading);

            chart.Measure = ChartMeasure.Spent;
            CollectionAssert.AreEqual(new[] { "Gear", "Coke", "Coal" }, chart.Shown, "the same lines");
            Assert.AreEqual(1, chart.SlotOf("Coke"), "in the same colours");
            Assert.AreEqual(20f, chart.AxisTop(), "Coke's 18 a minute spent");
            Assert.AreEqual(18f, chart.Rates("Coke")[ProductionHistory.Points - 1], 1e-4);
            IReadOnlyList<string> hover = chart.HoverLines(ProductionHistory.Points - 1);
            Assert.AreEqual("now · spent", hover[0]);
            Assert.AreEqual("Coke  18 a min", hover[2]);
            Assert.AreEqual("Goods spent a minute", chart.Heading);

            ChartRow coke = chart.Rows().Single(r => r.ItemType == "Coke");
            Assert.AreEqual("12", coke.MadeText);
            Assert.AreEqual("18", coke.SpentText);
            ChartRow coal = chart.Rows().Single(r => r.ItemType == "Coal");
            Assert.AreEqual("0", coal.MadeText);
            Assert.AreEqual("6", coal.SpentText);
            Assert.AreEqual("0.5", ProductionChart.Number(0.5f));
        }
    }

    /// <summary>docs/supply-design.md §4: each cart, its stock, and its rate in and out.</summary>
    public class MarketViewTests
    {
        [Test]
        public void AFlow_CountsTheTrickleIn_AndAFallInStockOut_OverAMinute()
        {
            var flow = new CartFlow(10);
            int stock = 0;
            // Ten seconds: 1 in every tick, and 5 taken each second.
            for (int t = 0; t < 100; t++)
            {
                if (t % 10 == 5)
                {
                    stock -= 5;
                }
                flow.Tick(stock, 1);
                stock += 1;
            }
            Assert.AreEqual(600f, flow.InPerMinute, 1e-3, "100 in ten seconds");
            Assert.AreEqual(300f, flow.OutPerMinute, 1e-3, "50 out in ten seconds");

            var young = new CartFlow(10);
            young.Tick(0, 1);
            Assert.AreEqual(6f, young.InPerMinute, 1e-3, "a first good is spread over ten seconds, not one");

            // Past a minute only the last minute counts.
            for (int t = 0; t < 700; t++)
            {
                young.Tick(0, t < 100 ? 1 : 0);
            }
            Assert.AreEqual(0f, young.InPerMinute, 1e-3);
            Assert.AreEqual(CartFlow.Seconds, young.SecondsCounted);
        }

        [Test]
        public void TheStreet_CountsItsCartsFlow_AndTheRowsSayWhatIsHappening()
        {
            SandboxWorld world = CrateWorld.Compose();
            try
            {
                world.Clock.Play();
                var cell = new Vector2Int(-4, -16);
                Assert.IsTrue(world.GrantCart(cell, ItemType.Coal).Done);
                SupplyCart coal = world.Supply.SpotAt(cell).Cart;

                MarketRow row = MarketReadout.Row(cell, coal);
                Assert.AreEqual("Coal cart", row.Name);
                Assert.AreEqual(MarketReadout.Empty, row.State);
                Assert.IsTrue(row.Short, "an empty cart needs a look");

                for (int i = 0; i < 20; i++)
                {
                    world.Advance(1f);
                }
                row = MarketReadout.Row(cell, coal);
                Assert.AreEqual(coal.RatePerMinute, row.In, 0.5f, "it brings its rate");
                Assert.AreEqual(0f, row.Out);
                Assert.AreEqual(MarketReadout.Filling, row.State);
                Assert.AreEqual($"{coal.Stock} of {coal.Capacity}", row.Holds);

                // Topped up, then taken faster than it fills: 2 a second against its 30 a minute,
                // until over the minute it has lost more than it brought.
                coal.Node.Deliver(coal.Capacity - coal.Stock);
                world.Advance(1f); // a rise outside the trickle is seen at a tick before anything is taken
                for (int i = 0; i < 30; i++)
                {
                    Assert.IsTrue(coal.Node.TryExtract(out _));
                    Assert.IsTrue(coal.Node.TryExtract(out _));
                    world.Advance(1f);
                }
                row = MarketReadout.Row(cell, coal);
                Assert.Greater(coal.Stock, 0, "precondition: not yet empty");
                Assert.Greater(row.Out, row.In);
                Assert.AreEqual(60f * 60f / coal.Flow.SecondsCounted, row.Out, 0.01f, "60 taken, over the seconds counted");
                Assert.AreEqual(MarketReadout.RunningDown, row.State);
                Assert.IsTrue(row.Short);
                StringAssert.Contains("buy Rate", MarketReadout.Tooltip(row));

                IReadOnlyList<MarketRow> rows = MarketReadout.Rows(world.Supply);
                CollectionAssert.AreEqual(rows.Select(r => r.Cell.x).OrderBy(x => x), rows.Select(r => r.Cell.x), "west to east");
                Assert.AreEqual(world.Supply.Carts.Count(), rows.Count);
                Assert.IsTrue(rows.All(r => PlayerText.RawIdIn(r.Name) == null && PlayerText.RawIdIn(r.Holds) == null));
                Assert.AreEqual($"2 carts · {world.Supply.Spots.Count - 2} empty spots", MarketReadout.Summary(world.Supply));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void ACartTakenFromAsFastAsItBrings_IsKeepingUp_NotRunningDown()
        {
            SandboxWorld world = CrateWorld.Compose();
            try
            {
                world.Clock.Play();
                SupplyCart scrap = world.Supply.Carts.Single();
                int perSecond = scrap.RatePerMinute / 60;
                Assert.Greater(perSecond, 0, "precondition: a whole good a second");
                for (int k = 0; k < 10; k++)
                {
                    Assert.IsTrue(scrap.Node.TryExtract(out _)); // below full first, so it trickles
                }
                world.Advance(1f);
                for (int i = 0; i < 30; i++)
                {
                    // Three more than it brings, once: out a few a minute ahead of in, within the tolerance.
                    for (int k = 0; k < perSecond + (i == 15 ? 3 : 0); k++)
                    {
                        Assert.IsTrue(scrap.Node.TryExtract(out _));
                    }
                    world.Advance(1f);
                }
                MarketRow row = MarketReadout.Rows(world.Supply).Single();
                Assert.Greater(row.Out, row.In, "precondition: taken a little faster than it fills");
                Assert.Less(row.Out - row.In, MarketReadout.Tolerance(scrap));
                Assert.Less(scrap.Stock, scrap.Capacity, "precondition: below full, so it trickles");
                Assert.AreEqual(MarketReadout.KeepingUp, row.State, $"in {row.In}, out {row.Out}");
                Assert.IsFalse(row.Short);
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void AFullCart_BringsNothing_AndIsNotShort()
        {
            SandboxWorld world = CrateWorld.Compose();
            try
            {
                world.Clock.Play();
                SupplyCart scrap = world.Supply.Carts.Single();
                Assert.AreEqual(scrap.Capacity, scrap.Stock, "precondition: the starting cart is full");
                for (int i = 0; i < 15; i++)
                {
                    world.Advance(1f);
                }
                MarketRow row = MarketReadout.Rows(world.Supply).Single();
                Assert.AreEqual(MarketReadout.Full, row.State);
                Assert.AreEqual(0f, row.In, "a full cart's trickle holds");
                Assert.IsFalse(row.Short);
                Assert.AreEqual(1f, row.Fill);
            }
            finally
            {
                world.Dispose();
            }
        }
    }
}
