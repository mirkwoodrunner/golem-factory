using System;
using System.IO;
using NUnit.Framework;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Overview;
using GolemFactory.Tests.EditMode;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Tests.Overview
{
    /// <summary>docs/endless-design.md §3: how far the factory has come, over the whole game.</summary>
    public class RunTotalsTests
    {
        [Test]
        public void TheLine_SaysGoodsAndContracts_AndCivicStagesOnceThereAreAny()
        {
            Assert.AreEqual("Since the start: 12,340 goods made · 5 contracts done", RunTotals.Text(12340, 5));
            Assert.AreEqual("Since the start: 1 goods made · 1 contract done · 2 civic stages built", RunTotals.Text(1, 1, 2));
            Assert.IsNull(PlayerText.RawIdIn(RunTotals.Text(0, 0)));
        }

        [Test]
        public void GoodsMade_CarryOnOverASave_AndNotTheAbandonedSession()
        {
            string path = Path.Combine(Path.GetTempPath(), "golem-factory-totals-" + Guid.NewGuid().ToString("N") + ".json");
            SandboxWorld world = CrateWorld.Compose();
            SandboxWorld loaded = null;
            try
            {
                EventBus.Publish(new ItemAssembledEvent("Line", ItemType.IronPlate, 30, 0));
                Assert.AreEqual(30, world.GoodsMadeEver);
                world.SaveTo(path);
                world.Dispose();

                loaded = CrateWorld.Compose();
                EventBus.Publish(new ItemAssembledEvent("Line", ItemType.Gear, 7, 0)); // this session, before the load
                loaded.LoadFrom(path);
                Assert.AreEqual(30, loaded.GoodsMadeEver, "the save's total, not the session's 7 on top");
                EventBus.Publish(new ItemAssembledEvent("Line", ItemType.Gear, 5, 0));
                Assert.AreEqual(35, loaded.GoodsMadeEver, "and counting on");
                StringAssert.StartsWith("Since the start: 35 goods made · 0 contracts done", loaded.RunTotalsLine);
            }
            finally
            {
                loaded?.Dispose();
                File.Delete(path);
            }
        }
    }
}
