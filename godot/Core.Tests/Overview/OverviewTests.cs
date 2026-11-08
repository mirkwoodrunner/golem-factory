using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using GolemFactory.Buildings;
using GolemFactory.Compat;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.Menus;
using GolemFactory.Overview;
using GolemFactory.Player;
using GolemFactory.Steam;
using GolemFactory.Tests.EditMode;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Tests.Overview
{
    /// <summary>docs/menus-design.md §8: the graph's data.</summary>
    public class ProductionHistoryTests
    {
        private static Dictionary<string, int> Totals(params (string good, int n)[] counts) => counts.ToDictionary(c => c.good, c => c.n);

        [Test]
        public void ABucketIsWhatWasMadeInItsTenSeconds_AndAPointBeforeTheRecordIsNaN()
        {
            var history = new ProductionHistory();
            history.Record(0f, Totals(("Gear", 5)));            // the start: nothing counted yet
            history.Record(9.9f, Totals(("Gear", 8)));
            Assert.AreEqual(0, history.BucketCount);
            history.Record(10f, Totals(("Gear", 9)));           // 4 made in the first ten seconds
            Assert.AreEqual(1, history.BucketCount);

            float[] rates = history.Rates("Gear", HistoryRange.TenMinutes);
            Assert.AreEqual(ProductionHistory.Points, rates.Length);
            Assert.AreEqual(24f, rates[rates.Length - 1], 1e-4, "4 in ten seconds is 24 a minute");
            Assert.IsTrue(float.IsNaN(rates[rates.Length - 2]), "before the record began");
            Assert.AreEqual(0f, history.Rates("Coke", HistoryRange.TenMinutes)[rates.Length - 1], "a good not made reads 0, not NaN");
        }

        [Test]
        public void ALongFrame_ClosesEveryBucketItCrossed()
        {
            var history = new ProductionHistory();
            history.Record(0f, Totals());
            history.Record(35f, Totals(("Gear", 6)));
            Assert.AreEqual(3, history.BucketCount);
            float[] rates = history.Rates("Gear", HistoryRange.TenMinutes);
            Assert.AreEqual(36f, rates[rates.Length - 3], 1e-4, "the first bucket took what was counted");
            Assert.AreEqual(0f, rates[rates.Length - 1]);
        }

        [Test]
        public void TheHour_IsAMinuteAPoint_AndOnlyAnHourIsKept()
        {
            var history = new ProductionHistory();
            history.Record(0f, Totals());
            int made = 0;
            for (int bucket = 1; bucket <= ProductionHistory.MaxBuckets + 30; bucket++)
            {
                made += 2;
                history.Record(bucket * ProductionHistory.BucketSeconds, Totals(("Gear", made)));
            }
            Assert.AreEqual(ProductionHistory.MaxBuckets, history.BucketCount);
            float[] hour = history.Rates("Gear", HistoryRange.Hour);
            Assert.IsTrue(hour.All(r => Math.Abs(r - 12f) < 1e-4), "2 every ten seconds is 12 a minute, at every point");
            Assert.AreEqual(12f, history.Average("Gear", HistoryRange.Hour), 1e-4);
            Assert.AreEqual(600f, ProductionHistory.Seconds(HistoryRange.TenMinutes));
            Assert.AreEqual(3600f, ProductionHistory.Seconds(HistoryRange.Hour));
        }

        [Test]
        public void Goods_TheMostMadeFirst_OnlyThoseInTheRange()
        {
            var history = new ProductionHistory();
            history.Record(0f, Totals());
            history.Record(10f, Totals(("Coke", 9)));                                   // an hour ago, gone from ten minutes
            for (int b = 2; b <= 80; b++)
            {
                history.Record(b * 10f, Totals(("Coke", 9), ("Gear", b), ("Brass", 2 * b)));
            }
            CollectionAssert.AreEqual(new[] { "Brass", "Gear" }, history.Goods(HistoryRange.TenMinutes));
            CollectionAssert.AreEqual(new[] { "Brass", "Gear", "Coke" }, history.Goods(HistoryRange.Hour));
            Assert.AreEqual(0f, history.Average("Nothing", HistoryRange.Hour));
        }
    }

    public class ProductionChartTests
    {
        private static ProductionHistory Made(params (string good, int perBucket)[] goods)
        {
            var history = new ProductionHistory();
            history.Record(0f, new Dictionary<string, int>());
            for (int b = 1; b <= 12; b++)
            {
                history.Record(b * 10f, goods.ToDictionary(g => g.good, g => g.perBucket * b));
            }
            return history;
        }

        [Test]
        public void TheMostMadeTakeTheLines_Once()
        {
            var chart = new ProductionChart(new ProductionHistory());
            chart.Refresh();
            Assert.IsEmpty(chart.Shown, "nothing made, nothing drawn");

            var history = Made(("Scrap", 9), ("Coke", 8), ("Gear", 7), ("IronPlate", 6), ("Brass", 5));
            chart = new ProductionChart(history);
            chart.Refresh();
            CollectionAssert.AreEqual(new[] { "Scrap", "Coke", "Gear", "IronPlate" }, chart.Shown);
            Assert.IsTrue(chart.Toggle("Coke"));
            chart.Refresh();
            CollectionAssert.DoesNotContain(chart.Shown, "Coke", "refreshing never undoes the player's choice");
        }

        [Test]
        public void AGoodKeepsItsColour_WhateverElseIsHidden_AndAFifthIsRefused()
        {
            var chart = new ProductionChart(Made(("Scrap", 9), ("Coke", 8), ("Gear", 7), ("IronPlate", 6), ("Brass", 5)));
            chart.Refresh();
            Assert.AreEqual(2, chart.SlotOf("Gear"));
            Assert.IsFalse(chart.Toggle("Brass"), "four lines already");
            Assert.AreEqual(-1, chart.SlotOf("Brass"));

            chart.Toggle("Coke");
            Assert.AreEqual(2, chart.SlotOf("Gear"), "hiding another didn't repaint it");
            Assert.AreEqual(3, chart.SlotOf("IronPlate"));
            Assert.IsTrue(chart.Toggle("Brass"));
            Assert.AreEqual(1, chart.SlotOf("Brass"), "the freed slot");

            chart.Range = HistoryRange.Hour;
            Assert.AreEqual(2, chart.SlotOf("Gear"), "the range changes the time axis only");
        }

        [Test]
        public void TheLegendListsEveryGoodMade_WithItsAverage()
        {
            var chart = new ProductionChart(Made(("IronPlate", 3), ("Gear", 1)));
            chart.Refresh();
            IReadOnlyList<ChartRow> rows = chart.Rows();
            Assert.AreEqual("Iron Plate", rows[0].Name);
            Assert.AreEqual("18 a min", rows[0].RateText);
            Assert.AreEqual("6 a min", rows[1].RateText);
            Assert.IsTrue(rows.All(r => r.Shown));
        }

        [Test]
        public void TheAxisRoundsUp_AndTheWordsReadLikeAPerson()
        {
            Assert.AreEqual(10f, ProductionChart.RoundUp(0f));
            Assert.AreEqual(10f, ProductionChart.RoundUp(10f));
            Assert.AreEqual(25f, ProductionChart.RoundUp(21f));
            Assert.AreEqual(50f, ProductionChart.RoundUp(26f));
            Assert.AreEqual(200f, ProductionChart.RoundUp(101f));
            Assert.AreEqual("0.5 a min", ProductionChart.RateText(0.5f));
            Assert.AreEqual("0 a min", ProductionChart.RateText(0f));
            Assert.AreEqual("now", ProductionChart.Ago(0f));
            Assert.AreEqual("40 s ago", ProductionChart.Ago(40f));
            Assert.AreEqual("12 min ago", ProductionChart.Ago(720f));

            var chart = new ProductionChart(Made(("Gear", 1)));
            chart.Refresh();
            Assert.AreEqual("now", chart.TimeAt(ProductionHistory.Points - 1));
            Assert.AreEqual("10 min ago", chart.TimeAt(0), "the oldest point, 590 s back");
            Assert.AreEqual(10f, chart.AxisTop(), "6 a min fits under 10");
            chart.Range = HistoryRange.Hour;
            Assert.AreEqual("1 hour ago", chart.RangeStart);
            var busier = new ProductionChart(Made(("Gear", 3)));
            busier.Refresh();
            Assert.AreEqual(20f, busier.AxisTop(), "18 a min rounds up to 20");
            IReadOnlyList<string> hover = chart.HoverLines(ProductionHistory.Points - 1);
            Assert.AreEqual("now · made", hover[0], "the time, and what the lines count");
            StringAssert.StartsWith("Gear  ", hover[1]);
        }
    }

    public class AlertLogTests
    {
        private static StallSnapshot Stall(string id, StallReason reason = StallReason.BeltFull, string resource = "Crate") =>
            new StallSnapshot(id, reason, resource);

        [Test]
        public void AStop_IsLoggedOnce_AndClosedWhenItRuns()
        {
            var log = new AlertLog();
            log.Observe(1f, new[] { Stall("G1") }, s => "Scavenger #1 stalled");
            log.Observe(1.5f, new[] { Stall("G1") }, s => "again");
            Assert.AreEqual(1, log.Entries.Count, "still the same stop");
            Assert.AreEqual("Scavenger #1 stalled", log.Entries[0].Text);
            Assert.IsTrue(log.Entries[0].Open);
            Assert.AreEqual("still stopped · 0:41", AlertLog.For(log.Entries[0], 42f));

            log.Observe(43f, Array.Empty<StallSnapshot>(), null);
            Assert.IsFalse(log.Entries[0].Open);
            Assert.AreEqual("stopped for 0:42", AlertLog.For(log.Entries[0], 100f));
            Assert.AreEqual("2 min ago", AlertLog.When(log.Entries[0], 121f));
        }

        [Test]
        public void ANewCause_IsANewEntry_NewestFirst()
        {
            var log = new AlertLog();
            log.Observe(0f, new[] { Stall("G1") }, s => "full");
            log.Observe(5f, new[] { Stall("G1", StallReason.NoSteam, null) }, s => "no steam");
            Assert.AreEqual(2, log.Entries.Count);
            Assert.AreEqual("no steam", log.Entries[0].Text);
            Assert.IsTrue(log.Entries[0].NoSteam);
            Assert.IsFalse(log.Entries[1].Open, "the first cause ended when the second began");
        }

        [Test]
        public void TheOldestFinishedStopsGo_AnOpenOneStays()
        {
            var log = new AlertLog();
            log.Observe(0f, new[] { Stall("Old") }, s => "old and open");
            for (int i = 0; i < AlertLog.MaxEntries + 5; i++)
            {
                log.Observe(i + 1, new[] { Stall("Old"), Stall("G" + i) }, s => s.GolemId);
                log.Observe(i + 1.5f, new[] { Stall("Old") }, null);
            }
            Assert.AreEqual(AlertLog.MaxEntries, log.Entries.Count);
            Assert.AreEqual("old and open", log.Entries.Last().Text, "the still-open stop is kept");
            log.Observe(100f, new[] { Stall("Old") }, s => "duplicate");
            Assert.IsFalse(log.Entries.Any(e => e.Text == "duplicate"), "and isn't logged twice");
        }
    }

    /// <summary>The overview on a real world: the kit's first chapters, then a boiler run dry.</summary>
    public class OverviewWorldTests
    {
        private static SandboxWorld Staged(int chapters = 6)
        {
            SandboxWorld world = CrateWorld.Compose();
            world.Clock.Play();
            TitleShowcase.Stage(world, chapters);
            return world;
        }

        [Test]
        public void TheMap_ShowsEverythingBuilt_EveryGolem_TheCartsAndSpots_TheTowerAndThePlayer()
        {
            SandboxWorld world = Staged();
            try
            {
                FactoryMap map = FactoryMap.Compose(world);
                List<GolemEntity> golems = world.Golems.Where(g => g != null && !g.IsRemoved).ToList();
                Assert.AreEqual(golems.Count, map.Marks.Count(m => m.Kind == MapMarkKind.Golem));
                int built = world.Build.Buildings.Count(b => !b.IsRemoved) + world.AuthoredBuildings.Count(b => !b.IsFixture); // the tower is its own mark
                Assert.AreEqual(built, map.Marks.Count(m => m.Kind != MapMarkKind.Golem && m.Kind != MapMarkKind.Player
                    && m.Kind != MapMarkKind.Cart && m.Kind != MapMarkKind.Spot && m.Kind != MapMarkKind.Tower));
                Assert.AreEqual(world.Supply.Carts.Count(), map.Marks.Count(m => m.Kind == MapMarkKind.Cart));
                Assert.AreEqual(world.Supply.Spots.Count(s => s.IsEmpty && world.Supply.IsOpen(s)), map.Marks.Count(m => m.Kind == MapMarkKind.Spot),
                    "every empty spot that can take a cart: the Docks' quay only once they open");
                Assert.IsTrue(map.Marks.Where(m => m.Kind == MapMarkKind.Cart).All(m => m.Label.EndsWith(" cart")), "a cart is named a cart");
                Assert.AreEqual(1, map.Marks.Count(m => m.Kind == MapMarkKind.Tower));
                Assert.AreEqual(1, map.Marks.Count(m => m.Kind == MapMarkKind.Player));
                Assert.IsTrue(map.Marks.Any(m => m.Kind == MapMarkKind.Belt), "the kit laid a belt");
                Assert.IsTrue(map.Marks.Any(m => m.Kind == MapMarkKind.Steam), "and boilers");

                foreach (MapMark mark in map.Marks)
                {
                    Assert.IsTrue(map.Extent.Contains(mark.Cell), $"{mark.Label} at {mark.Cell} is off the map");
                    Assert.IsNull(PlayerText.RawIdIn(mark.Label), "an id in a label: '" + mark.Label + "'");
                    Assert.IsNull(PlayerText.RawIdIn(map.Describe(mark)), "an id under the pointer: '" + map.Describe(mark) + "'");
                    Assert.IsNotEmpty(mark.Label);
                }
                StringAssert.StartsWith(golems.Count + " golems · ", map.Summary);

                GolemEntity first = golems[0];
                MapMark? at = map.MarkAt(first.Cell);
                Assert.AreEqual(MapMarkKind.Golem, at?.Kind, "a golem before what it stands on");
                Assert.AreEqual(first.GolemId, at?.GolemId);
                Assert.AreEqual(GolemNames.Short(first), at?.Label);
                StringAssert.StartsWith(GolemNames.Short(first) + " · ", map.Describe(at.Value), "the pointer adds its state");
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void ADryBoiler_StopsGolems_TheMapRingsThem_AndTheLogKeepsTheStops()
        {
            SandboxWorld world = Staged();
            try
            {
                for (int i = 0; i < 300; i++)
                {
                    world.Advance(1f / 30f);
                }
                Assert.IsNotEmpty(world.History.Goods(HistoryRange.TenMinutes), "the factory made something in ten seconds");

                foreach (PlaceableBuilding b in world.Build.Buildings.Where(b => !b.IsRemoved))
                {
                    b.GetPart<PlaceableBoiler>()?.Boiler?.SetCoke(0);
                }
                for (int i = 0; i < 300; i++)
                {
                    world.Advance(1f / 30f);
                }
                FactoryMap map = FactoryMap.Compose(world);
                List<MapMark> stopped = map.Marks.Where(m => m.Stopped).ToList();
                Assert.IsNotEmpty(stopped, "no golem stopped with the boilers dry");
                Assert.IsTrue(stopped.All(m => m.Mood == GolemMood.Starved));
                StringAssert.EndsWith(stopped.Count + " stopped", map.Summary);

                List<AlertEntry> open = world.Alerts.Log.Entries.Where(e => e.Open).ToList();
                Assert.AreEqual(stopped.Count, open.Count);
                Assert.IsTrue(open.All(e => e.NoSteam && PlayerText.RawIdIn(e.Text) == null), string.Join(" | ", open.Select(e => e.Text)));

                foreach (PlaceableBuilding b in world.Build.Buildings.Where(b => !b.IsRemoved))
                {
                    SteamBoiler boiler = b.GetPart<PlaceableBoiler>()?.Boiler;
                    boiler?.AddCoke(boiler.CokeRoom);
                }
                for (int i = 0; i < 300; i++)
                {
                    world.Advance(1f / 30f);
                }
                // Running again, a golem may stop for something new (a stall runs dry): a new entry.
                List<AlertEntry> steam = world.Alerts.Log.Entries.Where(e => e.NoSteam).ToList();
                Assert.IsTrue(steam.All(e => !e.Open), "every steam stop ran again");
                Assert.AreEqual(open.Count, steam.Count, "and none was logged twice");
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void PickingAGolem_TakesTheCameraToIt_UntilThePlayerWalks()
        {
            SandboxWorld world = Staged(2);
            try
            {
                GolemEntity golem = world.Golems.First(g => g != null && !g.IsRemoved);
                Assert.IsTrue(world.FocusGolem(golem.GolemId));
                Assert.AreEqual(CameraFocus.Alert, world.Camera.Focus);
                Assert.AreEqual(golem.GolemId, world.Camera.AlertGolemId);
                world.Camera.PlayerMoved();
                Assert.AreEqual(CameraFocus.Player, world.Camera.Focus);
                Assert.IsFalse(world.FocusGolem("PlayerGolem-999"), "a golem that is gone");
                Assert.AreEqual(CameraFocus.Player, world.Camera.Focus);
            }
            finally
            {
                world.Dispose();
            }
        }
    }
}
