using System;
using System.Linq;
using GolemFactory.ClockTower;
using GolemFactory.World;
using NUnit.Framework;

namespace GolemFactory.Tests.World
{
    // docs/game-feel-design.md §4: every curve moves, and ends where it began.
    public class FeelRulesTests
    {
        private static void Sample(float seconds, Action<float> check)
        {
            for (float age = 0f; age < seconds; age += 1f / 120f)
            {
                check(age);
            }
        }

        [Test]
        public void ThePlaceSquash_Squashes_ThenRestsAtOne()
        {
            float minY = 1f;
            Sample(FeelRules.PlaceSeconds, a => minY = Math.Min(minY, FeelRules.PlaceSquash(a).y));
            Assert.Less(minY, 0.9f, "it lands with a squash");
            Assert.AreEqual((1f, 1f), FeelRules.PlaceSquash(FeelRules.PlaceSeconds));
            Assert.AreEqual((1f, 1f), FeelRules.PlaceSquash(-1f));
            var end = FeelRules.PlaceSquash(FeelRules.PlaceSeconds - 0.0001f);
            Assert.AreEqual(1f, end.x, 0.01f, "no jump at the end");
            Assert.AreEqual(1f, end.y, 0.01f);
        }

        [Test]
        public void TheDemolishSquash_GoesFlat_AndFades()
        {
            Assert.AreEqual(1f, FeelRules.DemolishSquash(0f).y);
            Assert.AreEqual(0f, FeelRules.DemolishSquash(FeelRules.DemolishSeconds).y);
            Assert.AreEqual(0f, FeelRules.DemolishSquash(FeelRules.DemolishSeconds).alpha);
            float last = 1f;
            Sample(FeelRules.DemolishSeconds, a =>
            {
                float y = FeelRules.DemolishSquash(a).y;
                Assert.LessOrEqual(y, last + 1e-6f);
                last = y;
            });
        }

        [Test]
        public void TheEffects_AreSeen_ThenGone()
        {
            float dust = 0f, spark = 0f, pop = 0f;
            Sample(FeelRules.DustSeconds, a => dust = Math.Max(dust, FeelRules.Dust(a).alpha));
            Sample(FeelRules.SparkSeconds, a => spark = Math.Max(spark, FeelRules.Spark(a).alpha));
            Sample(FeelRules.CratePopSeconds, a => pop = Math.Max(pop, FeelRules.GoodPop(a).alpha));
            Assert.Greater(dust, 0.5f);
            Assert.Greater(spark, 0.5f);
            Assert.Greater(pop, 0.5f);
            Assert.AreEqual(0f, FeelRules.Dust(FeelRules.DustSeconds).alpha);
            Assert.AreEqual(0f, FeelRules.Spark(FeelRules.SparkSeconds).alpha);
            Assert.AreEqual(0f, FeelRules.GoodPop(FeelRules.CratePopSeconds).alpha);
        }

        [Test]
        public void TheGood_PopsUpAndComesBackDown()
        {
            float top = 0f;
            Sample(FeelRules.CratePopSeconds, a => top = Math.Max(top, FeelRules.GoodPop(a).rise));
            Assert.Greater(top, 0.3f);
            Assert.AreEqual(0f, FeelRules.GoodPop(0f).rise);
            Assert.AreEqual((1f, 1f), FeelRules.CrateBump(FeelRules.CratePopSeconds));
        }

        [Test]
        public void ACard_DropsIntoItsSocket_AndSettlesFlat()
        {
            Assert.Less(FeelRules.CardLand(0f).drop, 0f, "it starts a little above the socket");
            Assert.AreEqual((0f, 1f, 1f), FeelRules.CardLand(FeelRules.CardLandSeconds));
            float minY = 1f;
            Sample(FeelRules.CardLandSeconds, a => minY = Math.Min(minY, FeelRules.CardLand(a).y));
            Assert.Less(minY, 0.97f, "it lands with weight: a squash");
        }

        [Test]
        public void TheTape_PrintsALine_ACharacterAtATime()
        {
            Assert.AreEqual(0, FeelRules.TapePrinted(0f, 40));
            Assert.AreEqual(6, FeelRules.TapePrinted(0.1f, 40));
            Assert.AreEqual(40, FeelRules.TapePrinted(5f, 40), "never more than the line");
            Assert.AreEqual(0, FeelRules.TapePrinted(-1f, 40));
        }

        [Test]
        public void TheTowerReveal_FlashesAndSettles()
        {
            Assert.AreEqual(1f, FeelRules.TowerReveal(0f).flash);
            Assert.AreEqual((1f, 0f), FeelRules.TowerReveal(FeelRules.TowerRevealSeconds));
        }

        // --- The tower holds its picture back until the reveal --------------------------------

        [Test]
        public void TheTower_HoldsBackItsNewStage_UntilTheReveal()
        {
            var site = new ClockTowerSite();
            site.SetStages(AuthoredData.Load().ClockTowerStages.Values.OrderBy(st => st.stageNumber));
            site.RestoreProgress(1, 0, false); // stage 1 done
            Assert.AreEqual("clock_tower_stage1", ClockTowerArt.SpriteFor(site, holdBack: false));
            Assert.AreEqual("clock_tower_stage0", ClockTowerArt.SpriteFor(site, holdBack: true));
        }
    }
}
