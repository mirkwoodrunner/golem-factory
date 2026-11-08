using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using GolemFactory.Civic;
using GolemFactory.ClockTower;
using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Tests.EditMode;
using GolemFactory.Tests.World;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Tests.Civic
{
    /// <summary>docs/endless-design.md §1.2: the civic works after the tower.</summary>
    public class CivicWorksTests
    {
        private SandboxWorld _world;
        private readonly List<CivicStageCompletedEvent> _built = new List<CivicStageCompletedEvent>();
        private readonly List<ClockTowerStageCompletedEvent> _tower = new List<ClockTowerStageCompletedEvent>();

        [SetUp]
        public void SetUp()
        {
            _world = CrateWorld.Compose();
            _built.Clear();
            _tower.Clear();
            EventBus.CivicStageCompleted += OnBuilt;
            EventBus.ClockTowerStageCompleted += OnTower;
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.CivicStageCompleted -= OnBuilt;
            EventBus.ClockTowerStageCompleted -= OnTower;
            _world.Dispose();
        }

        private void OnBuilt(CivicStageCompletedEvent e) => _built.Add(e);
        private void OnTower(ClockTowerStageCompletedEvent e) => _tower.Add(e);

        private CivicWorks Civic => _world.Civic;

        private void CompleteTheTower() =>
            _world.ClockTower.Site.RestoreProgress(_world.ClockTower.Site.StageCount, 0L, true);

        private IItemEndpoint Site()
        {
            Assert.IsTrue(_world.Endpoints.TryGetEndpoint(TownSquare.CivicCentre, out IItemEndpoint site));
            return site;
        }

        /// <summary>The stage under way, one tick short of done, its lines fed: the next ticks finish it.</summary>
        private void FinishTheStage()
        {
            ClockTowerStageDefinition stage = Civic.Site.ActiveStage;
            Civic.Site.RestoreProgress(Civic.Site.StageIndex, stage.RequiredProgressUnits() - 1, false);
            foreach (StageDemand demand in stage.demands)
            {
                for (int i = 0; i < demand.ratePerMinute; i++)
                {
                    Assert.IsTrue(Site().TryGive(new Belts.ItemStack { ItemType = demand.itemType }), demand.itemType);
                }
            }
            for (int i = 0; i < 5 && Civic.Site.ProgressUnits > 0; i++)
            {
                Civic.Tick(_world.Clock.CurrentTick + i);
            }
        }

        [Test]
        public void TheData_IsThreeWorksInStages_AskingTheCampaignsGoods()
        {
            List<CivicWorkDefinition> works = Civic.Works.ToList();
            CollectionAssert.AreEqual(new[] { "The Tramway", "The Observatory", "The Great Bell" }, works.Select(w => w.workName));
            CollectionAssert.AreEqual(new[] { 2, 3, 3 }, works.Select(w => w.stages.Count));
            foreach (CivicWorkDefinition work in works)
            {
                foreach (CivicStage stage in work.stages)
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(stage.stageName), work.name);
                    Assert.IsNull(PlayerText.RawIdIn(stage.stageName), work.name);
                    Assert.IsTrue(stage.demands.All(d => ItemTiers.IsKnown(d.itemType) && d.ratePerMinute > 0), work.name);
                }
            }
        }

        [Test]
        public void TheSite_IsRopedOff_UntilTheTowerStands()
        {
            Assert.IsFalse(Civic.IsOpen);
            Assert.IsFalse(Site().CanGive(Civic.Site.ActiveStage.demands[0].itemType), "nothing taken before the tower");
            CompleteTheTower();
            Assert.IsTrue(Civic.IsOpen);
            Assert.IsTrue(Site().CanGive(Civic.Site.ActiveStage.demands[0].itemType));
            Assert.AreEqual(CivicWorks.SiteName, _world.TileNames.Name(TownSquare.CivicCentre));
        }

        [Test]
        public void AStageBuilt_IsTheWorks_NotTheTowers()
        {
            CompleteTheTower();
            int towerStages = _world.TechTree.Ledger.CompletedTowerStages;
            FinishTheStage();
            Assert.AreEqual(1, Civic.Site.StageIndex);
            Assert.AreEqual(1, Civic.StagesBuilt);
            Assert.AreEqual("The Tramway", _built.Single().WorkName);
            Assert.IsEmpty(_tower, "no fanfare, camera event, autosave or Ledger stage");
            Assert.AreEqual(towerStages, _world.TechTree.Ledger.CompletedTowerStages, "the Ledger's tower stages are untouched");
            Assert.AreEqual("The Tramway: The Rails built", _world.Alerts.Log.Entries.First(e => e.IsNote).Text);
        }

        [Test]
        public void AWorkFinished_LoadsTheNext_AndTheLastLeavesThemAllStanding()
        {
            CompleteTheTower();
            int stages = Civic.Works.Sum(w => w.stages.Count);
            for (int i = 0; i < stages; i++)
            {
                FinishTheStage();
            }
            Assert.IsTrue(Civic.AllBuilt);
            Assert.AreEqual(stages, Civic.StagesBuilt);
            Assert.AreEqual(3, _built.Count(e => e.WorkFinished));
            Assert.IsFalse(Civic.IsOpen, "nothing left to deliver to");
            StringAssert.EndsWith("8 civic stages built", _world.RunTotalsLine);
        }

        [Test]
        public void AnySource_AndTheTowersThreeTimesCap_Hold()
        {
            // Same machinery: a stockpile at ten times the demand runs the stage at 3x, no faster.
            CompleteTheTower();
            ClockTowerStageDefinition stage = Civic.Site.ActiveStage;
            for (long t = 0; t < 600; t++)
            {
                foreach (StageDemand demand in stage.demands)
                {
                    int due = (int)((t + 1) * demand.ratePerMinute * 10 / 600 - t * demand.ratePerMinute * 10 / 600);
                    for (int i = 0; i < due; i++)
                    {
                        Civic.Site.RecordDelivery(demand.itemType, 1, t);
                    }
                }
                Civic.Tick(t);
            }
            Assert.AreEqual(3000, Civic.Site.StageMultiplierMilli);
        }

        [Test]
        public void ASave_PutsTheWorksBack_Exactly()
        {
            string path = Path.Combine(Path.GetTempPath(), "golem-factory-civic-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                CompleteTheTower();
                FinishTheStage();
                FinishTheStage(); // the Tramway done: the Observatory under way
                Civic.Site.RestoreProgress(1, 12345L, false);
                _world.SaveTo(path);
                SandboxWorld loaded = CrateWorld.Compose();
                try
                {
                    loaded.LoadFrom(path);
                    Assert.AreEqual("The Observatory", loaded.Civic.Current.workName);
                    Assert.AreEqual(1, loaded.Civic.Site.StageIndex);
                    Assert.AreEqual(12345L, loaded.Civic.Site.ProgressUnits);
                    Assert.AreEqual(3, loaded.Civic.StagesBuilt);
                }
                finally
                {
                    loaded.Dispose();
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void ThePanelsHeadline_FitsTheTowersLength()
        {
            CompleteTheTower();
            Civic.Tick(1);
            string line = CivicText.Headline(Civic, Civic.Site.BuildReading());
            Assert.AreEqual("The Tramway 1/2 - 0% - x0.00", line);
            Assert.LessOrEqual(line.Length, "Stage 4 The Chronometer - 100% - x3.00".Length, "the tower's longest");
            foreach (CivicWorkDefinition work in Civic.Works)
            {
                Assert.LessOrEqual((work.workName + " 3/3 - 100% - x3.00").Length, "Stage 4 The Chronometer - 100% - x3.00".Length, work.workName);
            }
        }

        [Test]
        public void CreativeHasNone()
        {
            SandboxWorld creative = CreativeTests.Compose(creative: true);
            try
            {
                Assert.IsFalse(creative.Civic.Enabled);
                Assert.IsFalse(creative.Civic.IsOpen);
                StringAssert.DoesNotContain("civic", creative.RunTotalsLine);
            }
            finally
            {
                creative.Dispose();
            }
        }
    }
}
