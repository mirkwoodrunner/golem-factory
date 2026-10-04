using NUnit.Framework;
using GolemFactory.Buildings;
using GolemFactory.Economy;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// §4b's junk hopper: throw anything in, get Scrap out, burn Coke doing it. The Slag Heap's
    /// pattern with the sign flipped -- it charges AND returns something.
    /// </summary>
    public class ScrapRecyclerTests
    {
        private static ScrapRecycler Fuelled(int coke) => new ScrapRecycler("Hopper", coke);

        // --- Pricing by tier ----------------------------------------------------------------

        [Test]
        public void GoodsAreWorthMoreTheDeeperTheyAre()
        {
            Assert.AreEqual(1, ScrapRecycler.PointsFor(ItemType.Scrap), "tier 0");
            Assert.AreEqual(2, ScrapRecycler.PointsFor(ItemType.Slag), "tier 1");
            Assert.AreEqual(3, ScrapRecycler.PointsFor(ItemType.Brass), "tier 2");
            Assert.AreEqual(5, ScrapRecycler.PointsFor(ItemType.Casing), "tier 3");
            Assert.AreEqual(8, ScrapRecycler.PointsFor(ItemType.Mechanism), "tier 4");
            Assert.AreEqual(13, ScrapRecycler.PointsFor(ItemType.GreatCog), "tier 5");
        }

        [Test]
        public void CokeIsFuel_NotFeedstock()
        {
            // Pushing Coke into a recycler refuels it, exactly as with a Slag Heap or a boiler.
            // Valuing it as a tier-1 good would make it both at once.
            Assert.AreEqual(0, ScrapRecycler.PointsFor(ItemType.Coke));
            Assert.IsFalse(Fuelled(10).CanAccept(ItemType.Coke));
        }

        [Test]
        public void AnUnknownGoodIsRefused_NotDefaulted()
        {
            // Silently valuing an unrecognised good is how a future item becomes an exploit.
            Assert.AreEqual(0, ScrapRecycler.PointsFor("Unobtanium"));
            Assert.IsFalse(Fuelled(10).CanAccept("Unobtanium"));
            Assert.IsFalse(Fuelled(10).TryRecycle("Unobtanium"));
            Assert.IsFalse(Fuelled(10).CanAccept(null));
        }

        // --- The ratio ----------------------------------------------------------------------

        [Test]
        public void TwoSlagBuyOneScrapForOneCoke()
        {
            // THE HEADLINE TRADE. Against the Slag Heap's 4 Slag per Coke returning nothing:
            // half the disposal rate, plus a good.
            ScrapRecycler hopper = Fuelled(1);

            Assert.IsTrue(hopper.TryRecycle(ItemType.Slag));
            Assert.AreEqual(0, hopper.ScrapStock, "not paid until the batch completes");
            Assert.AreEqual(1, hopper.CokeStock);

            Assert.IsTrue(hopper.TryRecycle(ItemType.Slag));
            Assert.AreEqual(1, hopper.ScrapStock);
            Assert.AreEqual(0, hopper.CokeStock);
            Assert.AreEqual(0, hopper.PendingPoints);
        }

        [Test]
        public void FourScrapInYieldsOneScrapOut_SoTheFreeGoodCannotBeFarmed()
        {
            // Scrap is free at the market. Feeding the recycler its own output must be the worst
            // move in the game, or the disposal machine becomes a money printer.
            ScrapRecycler hopper = Fuelled(4);

            for (int i = 0; i < 4; i++)
            {
                Assert.IsTrue(hopper.TryRecycle(ItemType.Scrap));
            }

            Assert.AreEqual(1, hopper.ScrapStock, "4 in, 1 out");
            Assert.AreEqual(3, hopper.CokeStock, "and a Coke burnt for the privilege");
        }

        [Test]
        public void TheRemainderIsCarried_SoTheRatioIsExactAtEveryScale()
        {
            // Integer accumulator, no floats -- §1.4's boiler discipline, which SlagHeap follows
            // for the same reason: two identical factories must recover identical amounts.
            //
            // Collected as it goes, because that is how a hopper is actually used: 100 Slag is
            // 50 Scrap and the hopper only holds 24, so a run this long with no golem on the
            // other end would back up long before the arithmetic finished. What is being pinned
            // is the RATIO across a long run, not the buffer.
            ScrapRecycler hopper = Fuelled(50);
            int collected = 0;

            for (int i = 0; i < 100; i++)
            {
                Assert.IsTrue(hopper.TryRecycle(ItemType.Slag), "unit " + i);
                collected += hopper.TakeScrap(hopper.ScrapStock);
            }

            Assert.AreEqual(50, collected, "100 Slag at 2 points = 200 points = 50 Scrap");
            Assert.AreEqual(0, hopper.CokeStock);
            Assert.AreEqual(0, hopper.PendingPoints);
            Assert.AreEqual(100, hopper.TotalRecycled);
        }

        [Test]
        public void ADeepGoodPaysOutSeveralScrapAtOnce()
        {
            // 13 points is three whole batches with 1 banked. An `if` rather than a `while` in
            // TryRecycle would hand out one Scrap and quietly keep the other nine points.
            ScrapRecycler hopper = Fuelled(10);

            Assert.IsTrue(hopper.TryRecycle(ItemType.GreatCog));

            Assert.AreEqual(3, hopper.ScrapStock);
            Assert.AreEqual(7, hopper.CokeStock, "three Coke burnt");
            Assert.AreEqual(1, hopper.PendingPoints, "13 - 12, banked");
        }

        // --- Running dry --------------------------------------------------------------------

        [Test]
        public void AUnitBelowTheCrossingIsTakenEvenWithNoCoke()
        {
            // Only the unit that CROSSES the threshold needs fuel in hand -- the rule that makes
            // a Slag Heap's last Coke void four Slag rather than one.
            ScrapRecycler hopper = Fuelled(0);

            Assert.IsTrue(hopper.TryRecycle(ItemType.Slag), "2 of 4 points, nothing spent");
            Assert.AreEqual(2, hopper.PendingPoints);
            Assert.IsFalse(hopper.TryRecycle(ItemType.Slag), "this one would complete a batch");
            Assert.AreEqual(2, hopper.PendingPoints, "and the refused unit changed nothing");
        }

        [Test]
        public void ADryHopperRefusesFeedstock_WhichIsTheMechanicNotAnEdgeCase()
        {
            ScrapRecycler hopper = Fuelled(0);
            hopper.TryRecycle(ItemType.Slag);

            Assert.IsFalse(hopper.CanAccept(ItemType.Slag));

            hopper.AddCoke(1);

            Assert.IsTrue(hopper.CanAccept(ItemType.Slag), "refuelling is the recovery");
            Assert.IsTrue(hopper.TryRecycle(ItemType.Slag));
            Assert.AreEqual(1, hopper.ScrapStock);
        }

        // --- Backing up ---------------------------------------------------------------------

        [Test]
        public void AFullHopperStopsAccepting_SoNobodyCollectingIsVisible()
        {
            ScrapRecycler hopper = Fuelled(500);

            for (int i = 0; i < ScrapRecycler.OutputCapacity * 2; i++)
            {
                hopper.TryRecycle(ItemType.Slag);
            }

            Assert.AreEqual(ScrapRecycler.OutputCapacity, hopper.ScrapStock);
            Assert.IsFalse(hopper.CanAccept(ItemType.Slag), "backed up rather than voiding");
        }

        [Test]
        public void AFullHopperRefusesEvenAUnitThatWouldOnlyBankPoints()
        {
            // The one place this deliberately differs from SlagHeap's "only the crossing unit
            // pays". Banked points against a full output have no path to being paid out except
            // somebody collecting, so accepting them would be taking in goods the machine cannot
            // process -- the accepted-then-stuck behaviour that makes backpressure illegible.
            ScrapRecycler hopper = Fuelled(500);
            while (hopper.TryRecycle(ItemType.Slag)) { }

            Assert.AreEqual(ScrapRecycler.OutputCapacity, hopper.ScrapStock);
            Assert.AreEqual(0, hopper.PendingPoints, "nothing banked");
            Assert.IsFalse(
                hopper.CanAccept(ItemType.Slag),
                "a 2-point unit would only bank, and must STILL be refused");
        }

        [Test]
        public void TheOutputCapacityOutlastsOneGolemHaul()
        {
            // Twice a golem's per-type cap, so a collector running at half the recycler's rate
            // still keeps up and one Haul does not empty the hopper.
            Assert.AreEqual(
                GolemFactory.Golems.GolemInventory.CapacityPerType * 2, ScrapRecycler.OutputCapacity);
        }

        [Test]
        public void CollectingFromAFullHopperUnblocksIt()
        {
            ScrapRecycler hopper = Fuelled(500);
            while (hopper.TryRecycle(ItemType.Slag)) { }

            Assert.AreEqual(12, hopper.TakeScrap(12), "one golem's worth");
            Assert.IsTrue(hopper.CanAccept(ItemType.Slag));
        }

        [Test]
        public void TakingScrapIsPartial_NeverOverdrawn()
        {
            ScrapRecycler hopper = Fuelled(10);
            hopper.TryRecycle(ItemType.Slag);
            hopper.TryRecycle(ItemType.Slag);

            Assert.AreEqual(1, hopper.TakeScrap(8), "took what was there");
            Assert.AreEqual(0, hopper.ScrapStock);
            Assert.AreEqual(0, hopper.TakeScrap(1));
            Assert.AreEqual(0, hopper.TakeScrap(-3));
        }

        // --- Balance guards ------------------------------------------------------------------

        [Test]
        public void ItDisposesOfSlagAtExactlyHalfTheSlagHeapsRate()
        {
            // The trade that keeps §5.3(c)'s decision alive. If this ever stops holding, the
            // recycler has quietly become strictly better than the heap and the Slag economy has
            // one outlet fewer than the design thinks.
            int slagPerCokeInRecycler = ScrapRecycler.PointsPerScrap / ScrapRecycler.PointsFor(ItemType.Slag);

            Assert.AreEqual(2, slagPerCokeInRecycler);
            Assert.AreEqual(SlagHeap.SlagPerCoke, slagPerCokeInRecycler * 2);
        }

        [Test]
        public void ItIsAStrictCokeSink_WhichIsWhatActuallyStopsALoop()
        {
            // THE REAL INVARIANT, and not the one this design's first draft claimed.
            //
            // Reasoning in Scrap says every cycle is lossy, and that is simply false: R4 turns
            // 2 Scrap into 2 Iron Plate plus a Slag, and R19 turns 1 Copper Ingot into 3 Copper
            // Wire, so any positive per-unit value multiplies across a recipe whose output count
            // exceeds its input count. No integer tier table can avoid that.
            //
            // What holds instead is structural: Coke only ever goes DOWN. A loop has to close in
            // the currency it spends, and this machine can neither produce nor return any.
            var hopper = new ScrapRecycler("Hopper", 40);
            int previousCoke = hopper.CokeStock;

            foreach (string itemType in ItemTiers.CanonicalOrder)
            {
                for (int i = 0; i < 6; i++)
                {
                    hopper.TryRecycle(itemType);
                    hopper.TakeScrap(hopper.ScrapStock);
                    Assert.LessOrEqual(
                        hopper.CokeStock, previousCoke, "Coke rose while recycling " + itemType);
                    previousCoke = hopper.CokeStock;
                }
            }
        }

        [Test]
        public void RecyclingIsAlwaysObviouslyWorseThanUsingTheThing()
        {
            // The other half of why the multiplier does not matter: Scrap is FREE at the market
            // (§10 forbids a soft-lock), so a machine whose only output is Scrap cannot be an
            // economic exploit -- what it saves is the walk. The tier table's job is fairness,
            // and fairness has a ceiling: the deepest good in the game must come back worth a
            // rounding error, or scrapping a Chronometer Core becomes a strategy.
            const int GenerousCeilingInScrap = 4;

            foreach (string itemType in ItemTiers.CanonicalOrder)
            {
                int scrapReturned = ScrapRecycler.PointsFor(itemType) / ScrapRecycler.PointsPerScrap;
                Assert.LessOrEqual(
                    scrapReturned, GenerousCeilingInScrap,
                    itemType + " returns enough Scrap to be worth farming");
            }
        }

        [Test]
        public void ADeeperGoodNeverReturnsLessThanAShallowerOne()
        {
            // "Worth more the deeper it is" is the whole readable promise of the table. A tuning
            // pass that inverted two tiers would make scrapping a Gear better than scrapping a
            // Mechanism, which reads as a bug rather than as a price.
            int previous = 0;
            foreach (string itemType in ItemTiers.CanonicalOrder)
            {
                if (itemType == ItemType.Coke)
                {
                    continue;
                }

                int points = ScrapRecycler.PointsFor(itemType);
                Assert.GreaterOrEqual(points, previous, itemType + " is worth less than a shallower good");
                previous = points;
            }
        }

        // --- Restore ---------------------------------------------------------------------

        [Test]
        public void RestoreSetsRatherThanAdds_AndClampsWhatItIsGiven()
        {
            ScrapRecycler hopper = Fuelled(9);
            hopper.Restore(4, 7, 3);

            Assert.AreEqual(4, hopper.CokeStock, "set, not added to the prefab's starting fuel");
            Assert.AreEqual(7, hopper.ScrapStock);
            Assert.AreEqual(3, hopper.PendingPoints);

            hopper.Restore(-1, ScrapRecycler.OutputCapacity + 50, ScrapRecycler.PointsPerScrap + 5);

            Assert.AreEqual(0, hopper.CokeStock);
            Assert.AreEqual(ScrapRecycler.OutputCapacity, hopper.ScrapStock);
            Assert.AreEqual(ScrapRecycler.PointsPerScrap - 1, hopper.PendingPoints,
                "a banked remainder at or above the crossing would have already been spent");
        }
    }
}
