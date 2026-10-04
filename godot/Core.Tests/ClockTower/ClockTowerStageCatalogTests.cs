using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.ClockTower;
using GolemFactory.Economy;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// The four authored Clock Tower stages, checked against docs/progression-design.md §7 --
    /// ON DISK, not against the authoring script's in-memory table.
    ///
    /// <para>
    /// Same reasoning as <c>RecipeCatalogTests</c>, and the same deliberate duplication: §7's
    /// table is transcribed here independently, so a bad authoring run (a typo, a half-finished
    /// edit, an asset someone hand-tweaked in the Inspector) fails the suite rather than failing
    /// the player forty minutes into Phase 6. Reading the expectation off
    /// <c>ProgressionAssetAuthoring</c> would make this a tautology.
    /// </para>
    /// </summary>
    public class ClockTowerStageCatalogTests
    {
        private const string StageRoot = "Assets/_Project/ScriptableObjects/ClockTower";

        /// <summary>§7's stage table, verbatim, hand-transcribed.</summary>
        private static readonly (int Number, string Name, int NominalSeconds,
            (string Item, int Rate)[] Demands)[] Expected =
        {
            (1, "Foundation", 360, new[] { (ItemType.FrameSection, 6) }),
            (2, "The Movement", 480, new[] { (ItemType.GreatCog, 3), (ItemType.FrameSection, 3) }),
            (3, "Aether Illumination", 480, new[]
            {
                (ItemType.AetherConduit, 3), (ItemType.Lens, 24), (ItemType.FrameSection, 2)
            }),
            (4, "The Chronometer", 600, new[]
            {
                (ItemType.ChronometerCore, 2), (ItemType.GreatCog, 2), (ItemType.AetherConduit, 1)
            }),
        };

        private static List<ClockTowerStageDefinition> LoadStages()
        {
            var stages = new List<ClockTowerStageDefinition>();
            stages.AddRange(AuthoredData.All<ClockTowerStageDefinition>());

            stages.Sort((a, b) => a.stageNumber.CompareTo(b.stageNumber));
            return stages;
        }

        [Test]
        public void FourStagesAreAuthored()
        {
            Assert.AreEqual(4, LoadStages().Count, "section 7 gives the tower exactly four stages");
        }

        [Test]
        public void EveryStageIsWellFormed()
        {
            foreach (ClockTowerStageDefinition stage in LoadStages())
            {
                string problem;
                Assert.IsTrue(stage.IsWellFormed(out problem), stage.name + ": " + problem);
            }
        }

        [Test]
        public void EveryStageMatchesTheDesignTable()
        {
            List<ClockTowerStageDefinition> stages = LoadStages();
            Assert.AreEqual(Expected.Length, stages.Count);

            for (int i = 0; i < Expected.Length; i++)
            {
                ClockTowerStageDefinition stage = stages[i];
                (int number, string name, int seconds, (string Item, int Rate)[] demands) = Expected[i];

                Assert.AreEqual(number, stage.stageNumber);
                Assert.AreEqual(name, stage.stageName);
                Assert.AreEqual(seconds, stage.nominalSeconds, name + " nominal duration");
                Assert.AreEqual(demands.Length, stage.demands.Count, name + " demand count");

                for (int d = 0; d < demands.Length; d++)
                {
                    // Order is asserted, not just membership. The demand list's order decides
                    // which line the HUD names as the weakest when two tie, so it is part of
                    // the contract rather than a presentation detail.
                    Assert.AreEqual(demands[d].Item, stage.demands[d].itemType,
                        name + " demand " + d);
                    Assert.AreEqual(demands[d].Rate, stage.demands[d].ratePerMinute,
                        name + " " + demands[d].Item + " rate");
                }
            }
        }

        [Test]
        public void EveryDemandedGoodIsARealItemTypeAndTerminalOrNear()
        {
            // §5.1 makes the four Tier-5 goods terminal by design and names the Clock Tower as
            // their sink. Lens is the one exception §7 reaches back for -- stage 3's 24/min is
            // what "flips the section 5.3(c) decision from voiding to over-smelting" -- so it is
            // listed explicitly rather than allowed through by a loose check.
            var allowed = new HashSet<string>
            {
                ItemType.FrameSection, ItemType.GreatCog, ItemType.AetherConduit,
                ItemType.ChronometerCore, ItemType.Lens
            };

            foreach (ClockTowerStageDefinition stage in LoadStages())
            {
                foreach (StageDemand demand in stage.demands)
                {
                    Assert.IsTrue(allowed.Contains(demand.itemType),
                        stage.stageName + " demands " + demand.itemType +
                        ", which is not one of the Tier-5 goods (or Lens)");
                }
            }
        }

        [Test]
        public void TheAuthoredNominalDurationsAreSixEightEightAndTenMinutes()
        {
            List<ClockTowerStageDefinition> stages = LoadStages();
            var minutes = new List<int>();
            foreach (ClockTowerStageDefinition stage in stages)
            {
                Assert.AreEqual(0, stage.nominalSeconds % 60, stage.stageName + " is whole minutes");
                minutes.Add(stage.nominalSeconds / 60);
            }

            Assert.AreEqual(new[] { 6, 8, 8, 10 }, minutes.ToArray());
        }

        [Test]
        public void ARunOfTheAuthoredStagesRequiresExactlyTheirNominalTicks()
        {
            // Ties the asset numbers to the mechanic: whatever the table says, the progress
            // requirement it produces must be that many ticks at 1x.
            foreach (ClockTowerStageDefinition stage in LoadStages())
            {
                Assert.AreEqual(
                    (long)stage.nominalSeconds * ClockTowerProgress.TicksPerSecond *
                    ClockTowerProgress.ProgressScale,
                    stage.RequiredProgressUnits(),
                    stage.stageName);
            }
        }
    }
}
