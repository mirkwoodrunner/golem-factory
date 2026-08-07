using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using GolemFactory.Belts;
using GolemFactory.ClockTower;
using GolemFactory.Economy;
using GolemFactory.Events;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// The Clock Tower end to end (docs/progression-design.md §7): four rate-scaled stages, a
    /// supply-pressure meter per demanded item, progress that scales with delivered rate and
    /// caps at 3x, gating by the weakest line, and a win that leaves the save running.
    ///
    /// <para>
    /// THE STAGE TABLE BELOW IS TRANSCRIBED FROM §7 BY HAND, deliberately not read off the
    /// authoring script or the .asset files -- exactly the split <c>RecipeCatalogTests</c> uses
    /// for §5.2. This file asks "does the mechanic behave as §7 describes"; the catalog test asks
    /// "do the shipped assets say what §7 says". Sourcing both from one place would make either
    /// question unanswerable.
    /// </para>
    /// </summary>
    public class ClockTowerSiteTests
    {
        private readonly List<Object> _spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _spawned)
            {
                Object.DestroyImmediate(o);
            }

            _spawned.Clear();
        }

        // --- §7's stage table, hand-transcribed ------------------------------------------------

        private ClockTowerStageDefinition Stage(
            int number, string name, int nominalSeconds, params StageDemand[] demands)
        {
            var stage = ScriptableObject.CreateInstance<ClockTowerStageDefinition>();
            _spawned.Add(stage);
            stage.Configure(number, name, nominalSeconds, demands);
            return stage;
        }

        private static StageDemand D(string itemType, int ratePerMinute) =>
            new StageDemand(itemType, ratePerMinute);

        private ClockTowerStageDefinition Stage1() =>
            Stage(1, "Foundation", 6 * 60, D(ItemType.FrameSection, 6));

        private ClockTowerStageDefinition Stage2() =>
            Stage(2, "The Movement", 8 * 60,
                D(ItemType.GreatCog, 3), D(ItemType.FrameSection, 3));

        private ClockTowerStageDefinition Stage3() =>
            Stage(3, "Aether Illumination", 8 * 60,
                D(ItemType.AetherConduit, 3), D(ItemType.Lens, 24), D(ItemType.FrameSection, 2));

        private ClockTowerStageDefinition Stage4() =>
            Stage(4, "The Chronometer", 10 * 60,
                D(ItemType.ChronometerCore, 2), D(ItemType.GreatCog, 2), D(ItemType.AetherConduit, 1));

        private List<ClockTowerStageDefinition> AllStages() =>
            new List<ClockTowerStageDefinition> { Stage1(), Stage2(), Stage3(), Stage4() };

        // --- The supply driver ------------------------------------------------------------------
        //
        // Delivers a rate EXACTLY, tick by tick, with no rounding slack: the units due on tick t
        // are floor((t+1)*rate/600) - floor(t*rate/600), so any 600-tick window holds precisely
        // `rate` units. That is what makes "exactly 1x supply" a testable statement rather than
        // an approximation, and it is why the nominal-duration assertions below can be equalities.
        private static int UnitsDueOnTick(long tick, int ratePerMinute)
        {
            long window = ClockTowerProgress.SupplyWindowTicks;
            return (int)((tick + 1) * ratePerMinute / window - tick * ratePerMinute / window);
        }

        private sealed class Supply
        {
            public readonly Dictionary<string, int> DeliveryRates = new Dictionary<string, int>();
            public readonly Dictionary<string, int> FreshRates = new Dictionary<string, int>();

            public static Supply At(ClockTowerStageDefinition stage, int deliveryMultiple, int freshMultiple)
            {
                var supply = new Supply();
                foreach (StageDemand demand in stage.demands)
                {
                    supply.DeliveryRates[demand.itemType] = demand.ratePerMinute * deliveryMultiple;
                    supply.FreshRates[demand.itemType] = demand.ratePerMinute * freshMultiple;
                }

                return supply;
            }

            public Supply WithFresh(string itemType, int ratePerMinute)
            {
                FreshRates[itemType] = ratePerMinute;
                return this;
            }

            public Supply WithDelivery(string itemType, int ratePerMinute)
            {
                DeliveryRates[itemType] = ratePerMinute;
                return this;
            }
        }

        // Fills both 60 s windows before the tower is ever ticked, so the run below starts from a
        // SATURATED steady state. §7's nominal duration is the time at exactly 1x supply, and a
        // rate measured over a rolling minute cannot read 1x until it has had a minute to
        // measure -- a real factory spends that minute spinning up, and a test that folded the
        // spin-up into the duration would be asserting the ramp, not the formula.
        private static void Preload(ClockTowerSite site, Supply supply)
        {
            for (long tick = 0; tick < ClockTowerProgress.SupplyWindowTicks; tick++)
            {
                Deliver(site, supply, tick);
            }
        }

        private static void Deliver(ClockTowerSite site, Supply supply, long tick)
        {
            foreach (KeyValuePair<string, int> line in supply.DeliveryRates)
            {
                int units = UnitsDueOnTick(tick, line.Value);
                if (units > 0)
                {
                    site.RecordDelivery(line.Key, units, tick);
                }
            }

            foreach (KeyValuePair<string, int> line in supply.FreshRates)
            {
                int units = UnitsDueOnTick(tick, line.Value);
                if (units > 0)
                {
                    site.RecordFreshProduction(line.Key, units, tick);
                }
            }
        }

        /// <summary>
        /// Runs a saturated tower until the stage index moves, and returns how many ticks that
        /// took. Returns -1 if it never finished inside the budget.
        /// </summary>
        private static int TicksToAdvance(ClockTowerSite site, Supply supply, int maxTicks)
        {
            int startIndex = site.StageIndex;
            long tick = ClockTowerProgress.SupplyWindowTicks;

            for (int elapsed = 1; elapsed <= maxTicks; elapsed++, tick++)
            {
                // Delivered before the site's own tick, matching a golem registered with the
                // clock ahead of the tower.
                Deliver(site, supply, tick);
                site.Tick(tick);

                if (site.StageIndex != startIndex)
                {
                    return elapsed;
                }
            }

            return -1;
        }

        private ClockTowerSite SiteFor(ClockTowerStageDefinition stage)
        {
            var site = new ClockTowerSite();
            site.SetStages(new[] { stage });
            return site;
        }

        // --- Nominal duration at exactly 1x -- one test per stage -------------------------------
        //
        // The cheapest possible check that §7's formula is wired the right way up. If the ratio
        // were inverted, or the window were the wrong length, or the per-tick increment were
        // scaled wrong, these are the assertions that catch it -- and they are equalities, not
        // ranges, because the arithmetic is integer end to end.

        [Test]
        public void Stage1_AtExactlyOneTimesSupply_TakesItsNominalSixMinutes()
        {
            ClockTowerStageDefinition stage = Stage1();
            ClockTowerSite site = SiteFor(stage);
            Supply supply = Supply.At(stage, 1, 1);
            Preload(site, supply);

            Assert.AreEqual(3600, TicksToAdvance(site, supply, 8000));
        }

        [Test]
        public void Stage2_AtExactlyOneTimesSupply_TakesItsNominalEightMinutes()
        {
            ClockTowerStageDefinition stage = Stage2();
            ClockTowerSite site = SiteFor(stage);
            Supply supply = Supply.At(stage, 1, 1);
            Preload(site, supply);

            Assert.AreEqual(4800, TicksToAdvance(site, supply, 9000));
        }

        [Test]
        public void Stage3_AtExactlyOneTimesSupply_TakesItsNominalEightMinutes()
        {
            ClockTowerStageDefinition stage = Stage3();
            ClockTowerSite site = SiteFor(stage);
            Supply supply = Supply.At(stage, 1, 1);
            Preload(site, supply);

            Assert.AreEqual(4800, TicksToAdvance(site, supply, 9000));
        }

        [Test]
        public void Stage4_AtExactlyOneTimesSupply_TakesItsNominalTenMinutes()
        {
            ClockTowerStageDefinition stage = Stage4();
            ClockTowerSite site = SiteFor(stage);
            Supply supply = Supply.At(stage, 1, 1);
            Preload(site, supply);

            Assert.AreEqual(6000, TicksToAdvance(site, supply, 11000));
        }

        // --- Surplus is rewarded ----------------------------------------------------------------

        [Test]
        public void AtThreeTimesSupply_AStageTakesExactlyAThirdOfItsNominalTime()
        {
            // §7: "A factory running 3x the demand finishes a stage in a third of the time --
            // surplus is rewarded." This is the whole decision the phase hands the player.
            ClockTowerStageDefinition stage = Stage1();
            ClockTowerSite site = SiteFor(stage);
            Supply supply = Supply.At(stage, 3, 3);
            Preload(site, supply);

            Assert.AreEqual(1200, TicksToAdvance(site, supply, 8000));
            Assert.AreEqual(3000, site.StageMultiplierMilli, "the multiplier reads x3.00");
        }

        [Test]
        public void AtFiveTimesSupply_TheMultiplierStillClampsAtThree()
        {
            ClockTowerStageDefinition stage = Stage1();
            ClockTowerSite site = SiteFor(stage);
            Supply supply = Supply.At(stage, 5, 5);
            Preload(site, supply);

            Assert.AreEqual(1200, TicksToAdvance(site, supply, 8000),
                "five times demand finishes no faster than three times");
        }

        // --- THE HOARD-BLITZ HOLE ----------------------------------------------------------------

        [Test]
        public void HoardBlitz_DeliveringFromAWarehouseCannotOutrunFreshProduction()
        {
            // THE EXPLOIT THIS CLOSES, in the design's own words: "a player could over-produce
            // stage-4 goods during stages 1-3 and then unload at 3x to finish the climax in
            // three minutes."
            //
            // Delivery here is a genuine, sustained 3x -- a warehouse emptying into the tower at
            // triple the demand rate, which on delivery alone would earn the full x3 multiplier
            // and finish stage 4 in 200 seconds. Fresh Assemble output is only 1x, because the
            // factory is not actually making any more than it needs. The multiplier must
            // therefore be 1x and the stage must take its full nominal ten minutes.
            //
            // The 60-unit meter clamp does NOT close this on its own: the clamp caps banked
            // STOCK credit, and a sustained warehouse drain is a delivery RATE, which the clamp
            // never sees. Only min(delivery, fresh) catches it.
            ClockTowerStageDefinition stage = Stage4();
            ClockTowerSite site = SiteFor(stage);
            Supply supply = Supply.At(stage, 3, 1);
            Preload(site, supply);

            Assert.AreEqual(6000, TicksToAdvance(site, supply, 11000),
                "three times the DELIVERY at one times production is a x1 stage");
            Assert.AreEqual(1000, site.StageMultiplierMilli);
        }

        [Test]
        public void HoardBlitz_AStockpileStillSmoothsAProductionDipItDoesNotRaiseTheCeiling()
        {
            // The other half of the same rule, and the reason it is min() rather than "fresh
            // production only": a warehouse is allowed to COVER a shortfall. Delivery at 1x
            // against production of 3x still earns exactly 1x -- neither number alone decides.
            ClockTowerStageDefinition stage = Stage1();
            ClockTowerSite site = SiteFor(stage);
            Supply supply = Supply.At(stage, 1, 3);
            Preload(site, supply);

            Assert.AreEqual(3600, TicksToAdvance(site, supply, 8000));
        }

        [Test]
        public void FreshProduction_WithNoAssembleOutputAtAll_FreezesTheStageEvenAtFloodDelivery()
        {
            // A pure warehouse dump with nothing being made behind it earns nothing at all.
            ClockTowerStageDefinition stage = Stage1();
            ClockTowerSite site = SiteFor(stage);
            Supply supply = Supply.At(stage, 3, 3);
            supply.FreshRates.Clear();
            Preload(site, supply);

            Assert.AreEqual(-1, TicksToAdvance(site, supply, 2000));
            Assert.AreEqual(0, site.ProgressUnits);
            Assert.AreEqual(0, site.StageMultiplierMilli);
        }

        // --- The weakest line gates ---------------------------------------------------------------

        [Test]
        public void TheWeakestLineGatesTheWholeStage()
        {
            // §7: "Progress is gated by the weakest line (min across items), so all lines must
            // run." Great Cog at triple rate buys nothing while Frame Section runs at one.
            ClockTowerStageDefinition stage = Stage2();
            ClockTowerSite site = SiteFor(stage);
            Supply supply = Supply.At(stage, 3, 3)
                .WithDelivery(ItemType.FrameSection, 3)
                .WithFresh(ItemType.FrameSection, 3);
            Preload(site, supply);

            Assert.AreEqual(4800, TicksToAdvance(site, supply, 9000));
            Assert.AreEqual(ItemType.FrameSection, site.LimitingItemType);
        }

        [Test]
        public void TheLimitingItemIsTheFirstInAuthoredOrderWhenTwoLinesTie()
        {
            // Determinism note (1): two identically-supplied towers must name the same weakest
            // line. Authored order is the only ordering they provably share -- the same rule
            // GolemEntity.BeginAssemble uses for naming the first short ingredient.
            ClockTowerStageDefinition stage = Stage2();
            ClockTowerSite site = SiteFor(stage);
            Supply supply = Supply.At(stage, 1, 1);
            Preload(site, supply);

            TicksToAdvance(site, supply, 10);

            Assert.AreEqual(ItemType.GreatCog, site.LimitingItemType,
                "section 7 lists Great Cog before Frame Section for stage 2");
        }

        // --- Freezing at zero ---------------------------------------------------------------------

        [Test]
        public void WhenAMeterEmpties_ProgressFreezesAtItsCurrentValueAndNeverGoesBackwards()
        {
            // §7: "If any meter hits 0, progress is 0 -- frozen, never negative." Stages cannot
            // fail, only take longer; this is the assertion that pins the rubric-5 guarantee.
            ClockTowerStageDefinition stage = Stage1();
            ClockTowerSite site = SiteFor(stage);
            Supply supply = Supply.At(stage, 1, 1);
            Preload(site, supply);

            long tick = ClockTowerProgress.SupplyWindowTicks;
            for (int i = 0; i < 600; i++, tick++)
            {
                Deliver(site, supply, tick);
                site.Tick(tick);
            }

            long earned = site.ProgressUnits;
            Assert.Greater(earned, 0L, "the tower earned something while it was being supplied");

            // Everything stops: no deliveries, no production. The delivery window empties within
            // a minute and the meter drains out behind it.
            var nothing = new Supply();
            long frozenAt = 0L;
            for (int i = 0; i < 3000; i++, tick++)
            {
                Deliver(site, nothing, tick);
                site.Tick(tick);
                Assert.GreaterOrEqual(site.ProgressUnits, frozenAt, "progress never decreases");
                frozenAt = site.ProgressUnits;
            }

            Assert.AreEqual(0, site.MeterUnits(ItemType.FrameSection), "the meter bottomed out");
            Assert.AreEqual(0, site.StageMultiplierMilli, "and the multiplier is frozen at zero");
            Assert.AreEqual(0, site.StageIndex, "the stage did not fail, roll back or advance");
            Assert.Less(site.ProgressUnits, stage.RequiredProgressUnits());
        }

        [Test]
        public void AStarvedStage_NamesTheStarvedItemAndItsDeficitInItemsPerMinute()
        {
            // §8: "an alert names the starved item; the HUD shows the deficit in items/min."
            ClockTowerStageDefinition stage = Stage2();
            ClockTowerSite site = SiteFor(stage);

            // Frame Section flows; Great Cog was never delivered at all, so its meter starts and
            // stays empty. That is the state a player arrives in when the Mechanism chain has
            // not been built yet -- stage 2's whole introduction.
            Supply supply = Supply.At(stage, 1, 1);
            supply.DeliveryRates.Remove(ItemType.GreatCog);
            supply.FreshRates.Remove(ItemType.GreatCog);
            Preload(site, supply);

            long tick = ClockTowerProgress.SupplyWindowTicks;
            for (int i = 0; i < 50; i++, tick++)
            {
                Deliver(site, supply, tick);
                site.Tick(tick);
            }

            Assert.AreEqual(ItemType.GreatCog, site.StarvedItemType);
            Assert.AreEqual(3, site.StarvedDeficitPerMinute, "the stage demands 3/min and gets 0");
            Assert.AreEqual(0L, site.ProgressUnits, "and nothing at all was earned");
        }

        // --- Stage advance and the win -------------------------------------------------------------

        [Test]
        public void StagesAdvanceInOrderAndTheFourthCompletionIsTheWin()
        {
            List<ClockTowerStageDefinition> stages = AllStages();
            var site = new ClockTowerSite();
            site.SetStages(stages);

            var completed = new List<ClockTowerStageCompletedEvent>();
            System.Action<ClockTowerStageCompletedEvent> listener = e => completed.Add(e);
            EventBus.ClockTowerStageCompleted += listener;

            try
            {
                // Every item any stage demands, supplied at three times the highest rate any
                // stage asks for it, so no line is ever the reason a stage is slow.
                var supply = new Supply();
                supply.DeliveryRates[ItemType.FrameSection] = 18;
                supply.DeliveryRates[ItemType.GreatCog] = 9;
                supply.DeliveryRates[ItemType.AetherConduit] = 9;
                supply.DeliveryRates[ItemType.Lens] = 72;
                supply.DeliveryRates[ItemType.ChronometerCore] = 6;
                foreach (KeyValuePair<string, int> line in supply.DeliveryRates)
                {
                    supply.FreshRates[line.Key] = line.Value;
                }

                Preload(site, supply);

                long tick = ClockTowerProgress.SupplyWindowTicks;
                int lastIndex = 0;
                for (int i = 0; i < 40_000 && !site.IsComplete; i++, tick++)
                {
                    Deliver(site, supply, tick);
                    site.Tick(tick);

                    // MONOTONIC: the stage index may stand still or step forward by one, never
                    // back. A tower that could regress would be a stage that can fail.
                    Assert.GreaterOrEqual(site.StageIndex, lastIndex);
                    Assert.LessOrEqual(site.StageIndex - lastIndex, 1);
                    lastIndex = site.StageIndex;
                }

                Assert.IsTrue(site.IsComplete, "all four stages finished");
                Assert.AreEqual(4, completed.Count);
                Assert.AreEqual(new[] { 1, 2, 3, 4 }, new[]
                {
                    completed[0].StageNumber, completed[1].StageNumber,
                    completed[2].StageNumber, completed[3].StageNumber
                });
                Assert.IsFalse(completed[0].IsFinalStage);
                Assert.IsFalse(completed[2].IsFinalStage);
                Assert.IsTrue(completed[3].IsFinalStage, "stage 4 completion is the win");
                Assert.AreEqual("The Chronometer", completed[3].StageName);

                // §7: "leaves the save running -- the Clock Tower is a win, not a game-over."
                // The site keeps taking ticks and simply stops accruing; nothing here is allowed
                // to stop, pause or throw.
                long completedTick = site.CompletedTick;
                int eventsAtWin = completed.Count;
                for (int i = 0; i < 2000; i++, tick++)
                {
                    Deliver(site, supply, tick);
                    site.Tick(tick);
                }

                Assert.IsTrue(site.IsComplete);
                Assert.AreEqual(completedTick, site.CompletedTick, "the win does not re-fire");
                Assert.AreEqual(eventsAtWin, completed.Count);
                Assert.AreEqual(100, site.ProgressPercent);
            }
            finally
            {
                EventBus.ClockTowerStageCompleted -= listener;
            }
        }

        // --- The input tile ------------------------------------------------------------------------

        [Test]
        public void TheInputEndpointAcceptsOnlyWhatTheRunningStageDemands()
        {
            ClockTowerSite site = SiteFor(Stage1());
            var endpoint = new ClockTowerInputEndpoint(site);

            Assert.IsTrue(endpoint.CanGive(), "a running stage accepts something");
            Assert.IsTrue(endpoint.CanGive(ItemType.FrameSection));
            Assert.IsFalse(endpoint.CanGive(ItemType.Lens), "stage 1 does not demand Lens");

            Assert.IsTrue(endpoint.TryGive(new ItemStack { ItemType = ItemType.FrameSection }));
            Assert.IsFalse(endpoint.TryGive(new ItemStack { ItemType = ItemType.Lens }),
                "an undemanded good is refused, not swallowed");
        }

        [Test]
        public void TheInputEndpointIsAPureSinkAndNeverHandsAnythingBack()
        {
            ClockTowerSite site = SiteFor(Stage1());
            var endpoint = new ClockTowerInputEndpoint(site);
            endpoint.TryGive(new ItemStack { ItemType = ItemType.FrameSection });

            ItemStack taken;
            Assert.IsFalse(endpoint.TryTake(out taken));
            Assert.IsNull(endpoint.PeekAvailableType());

            int quantity;
            Assert.IsFalse(endpoint.TryTake(ItemType.FrameSection, 1, out quantity));
            Assert.AreEqual(0, quantity);
        }

        [Test]
        public void DeliveringThroughTheEndpointCreditsTheDeliveryRateAndTheMeter()
        {
            ClockTowerStageDefinition stage = Stage1();
            ClockTowerSite site = SiteFor(stage);
            var endpoint = new ClockTowerInputEndpoint(site);

            for (int i = 0; i < 4; i++)
            {
                Assert.IsTrue(endpoint.TryGive(new ItemStack { ItemType = ItemType.FrameSection }));
            }

            // Asserted before any Tick: the meter is read in whole units and one tick of stage-1
            // decay (6 scaled units out of 600 per unit) would round the fourth unit away, which
            // would be testing the decay rather than the credit.
            Assert.AreEqual(4, site.DeliveryRatePerMinute(ItemType.FrameSection));
            Assert.AreEqual(4, site.MeterUnits(ItemType.FrameSection),
                "one meter unit per unit delivered");
            Assert.AreEqual(0, site.FreshProductionRatePerMinute(ItemType.FrameSection),
                "delivering is not producing");
        }

        // --- Readout ----------------------------------------------------------------------------------

        [Test]
        public void BuildReading_ReportsAllFourColumnsPerDemandedItem()
        {
            ClockTowerStageDefinition stage = Stage1();
            ClockTowerSite site = SiteFor(stage);

            // Delivery ahead of production: the exact state §8's fourth column exists for.
            Supply supply = Supply.At(stage, 3, 1);
            Preload(site, supply);

            long tick = ClockTowerProgress.SupplyWindowTicks;
            Deliver(site, supply, tick);
            site.Tick(tick);

            ClockTowerReading reading = site.BuildReading();
            Assert.IsTrue(reading.HasActiveStage);
            Assert.AreEqual(1, reading.StageNumber);
            Assert.AreEqual("Foundation", reading.StageName);
            Assert.AreEqual(1, reading.Demands.Count);

            ClockTowerDemandReading row = reading.Demands[0];
            Assert.AreEqual(ItemType.FrameSection, row.ItemType);
            Assert.AreEqual(6, row.RequiredPerMinute);
            Assert.AreEqual(18, row.DeliveredPerMinute);
            Assert.AreEqual(6, row.FreshPerMinute);
            Assert.AreEqual(1000, row.MultiplierMilli,
                "capped by fresh production, not by delivery -- the whole point of the column");
        }

        [Test]
        public void BuildReading_DoesNotAdvanceTheSimulation()
        {
            // Same rule as SteamNetwork.LastEvaluatedPoweredCount: a read-only view must never
            // perturb the simulation it is reporting on.
            ClockTowerStageDefinition stage = Stage1();
            ClockTowerSite site = SiteFor(stage);
            Supply supply = Supply.At(stage, 1, 1);
            Preload(site, supply);

            long tick = ClockTowerProgress.SupplyWindowTicks;
            Deliver(site, supply, tick);
            site.Tick(tick);

            long before = site.ProgressUnits;
            for (int i = 0; i < 50; i++)
            {
                site.BuildReading();
            }

            Assert.AreEqual(before, site.ProgressUnits);
            Assert.AreEqual(6, site.DeliveryRatePerMinute(ItemType.FrameSection),
                "and repeated reads did not age the window out");
        }

        // --- Authoring guards -------------------------------------------------------------------------

        [Test]
        public void AMalformedStageIsDroppedRatherThanTakingTheTowerDown()
        {
            ClockTowerStageDefinition broken = Stage(1, "Broken", 360);
            ClockTowerStageDefinition good = Stage1();

            var site = new ClockTowerSite();
            site.SetStages(new[] { broken, good });

            Assert.AreEqual(1, site.StageCount);
            Assert.AreEqual("Foundation", site.ActiveStage.stageName);
        }

        [Test]
        public void ATowerWithNoStagesTicksHarmlesslyAndAcceptsNothing()
        {
            var site = new ClockTowerSite();
            site.SetStages(null);

            site.Tick(0);
            site.Tick(1);

            Assert.IsFalse(site.IsComplete);
            Assert.IsFalse(site.AcceptsAnything);
            Assert.IsFalse(site.Demands(ItemType.FrameSection));
            Assert.AreEqual(0L, site.ProgressUnits);
        }
    }
}
