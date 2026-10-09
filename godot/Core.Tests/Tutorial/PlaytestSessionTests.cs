using System.Linq;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Tests.Data;
using GolemFactory.Tests.World;
using GolemFactory.Tutorial;
using GolemFactory.World;
using NUnit.Framework;

namespace GolemFactory.Tests.Tutorial
{
    /// <summary>Playtest mode (G10, at the user's call: "integrate the playtest script into the tutorial").</summary>
    public class PlaytestSessionTests
    {
        private static SandboxWorld Compose()
        {
            DefinitionSet definitions = AuthoredData.Load();
            return SandboxWorld.Compose(definitions, SandboxSetupTests.LoadReal(), PlaceableCatalogTests.LoadReal(definitions));
        }

        [Test]
        public void EveryQuestion_WaitsForAStepTheGuideHas()
        {
            SandboxWorld world = Compose();
            var stepIds = world.Tutorial.Steps.Select(s => s.Id).ToList();
            foreach (PlaytestQuestion question in PlaytestSession.Questions)
            {
                CollectionAssert.Contains(stepIds, question.AfterStepId, question.Id);
                Assert.GreaterOrEqual(question.Options.Count, 2, question.Id);
            }
            CollectionAssert.AllItemsAreUnique(PlaytestSession.Questions.Select(q => q.Id));
        }

        [Test]
        public void TheSandboxRunsPlaytestMode_AndAsksAboutTheLightFirst()
        {
            SandboxWorld world = Compose();
            PlaytestSession playtest = world.Tutorial.Playtest;

            Assert.IsNotNull(playtest, "sandbox.json turns playtest mode on");
            Assert.AreEqual("light", playtest.Current?.Id);
        }

        [Test]
        public void AQuestion_WaitsUntilItsStep_ThenAnAnswerIsRecorded()
        {
            SandboxWorld world = Compose();
            PlaytestSession playtest = world.Tutorial.Playtest;
            playtest.Skip(1f);
            Assert.IsNull(playtest.Current, "nothing else is due on step 1");

            world.Buffers.Deposit(world.StockpileBufferId, ItemType.Scrap, 20);
            world.Tutorial.Update();
            Assert.IsNull(playtest.Current, "the Workbench question waits for the Workbench");

            // Straight to the depot step: the first golem has been programmed.
            foreach (string item in new[] { ItemType.Coal, ItemType.Coke, ItemType.IronPlate })
            {
                world.Buffers.Deposit(world.StockpileBufferId, item, 50);
            }
            world.Buffers.Deposit(world.StockpileBufferId, ItemType.Scrap, 200);
            world.Build.SetActivePrefab(world.Placeables.Single(p => p.Key == "BoilerPrefab").Prefab);
            world.Build.PlaceOrRemove(world.Tutorial.BoilerSpot);
            world.Build.CancelPlacement();
            world.Tutorial.Update();
            var boiler = world.Build.Buildings.Single(b => !b.IsRemoved && b.Cell == world.Tutorial.BoilerSpot);
            world.Interactor.TryRefuelBoiler(boiler.GetPart<Buildings.PlaceableBoiler>());
            world.StarterStation.TryConstructGolem(world.Definitions.Chassis["ClockworkScavenger"], out var golem);
            golem.Program.logicCore = world.Definitions.LogicCores["AlwaysOnCore"];
            golem.Program.TryAddAppendage(world.Definitions.Appendages["ExtractScrap"]);
            golem.Program.TryAddAppendage(world.Definitions.Appendages["PushOutput"]);
            world.Tutorial.Update();
            Assert.AreEqual("depot", world.Tutorial.Current.Id);

            Assert.AreEqual("workbench", playtest.Current?.Id, "asked once the first program is written");
            playtest.Answer("A real decision", "  the dial is nice  ", 120f);

            Assert.IsNull(playtest.Current);
            PlaytestAnswer answer = playtest.Answers.Single(a => a.QuestionId == "workbench");
            Assert.AreEqual("A real decision", answer.Choice);
            Assert.AreEqual("the dial is nice", answer.Note, "trimmed");
        }

        [Test]
        public void TheReport_HasAnswers_Timings_KitUses_AndErrors()
        {
            var playtest = new PlaytestSession();
            playtest.StepEntered("scrap", "Gather Scrap", 0f);
            playtest.Answer("Too dark", "", 5f);
            playtest.StepEntered("coal", "Buy Coal", 95f);
            playtest.RecordKit("jumped to chapter 4", 100f);
            playtest.RecordError("NullReferenceException in Foo", 101f);

            string report = playtest.Compose("Build: test", 130f);

            StringAssert.Contains("Build: test", report);
            StringAssert.Contains("**Look around the workshop. Is the room too dark?** Too dark", report);
            StringAssert.Contains("| Gather Scrap | 0:00 | 1:35 |", report);
            StringAssert.Contains("| Buy Coal | 1:35 | 0:35 |", report);
            StringAssert.Contains("1:40  jumped to chapter 4", report);
            StringAssert.Contains("1:41  NullReferenceException in Foo", report);
        }

        [Test]
        public void AQuestionIsAskedOnce_EvenIfItsStepComesRoundAgain()
        {
            var playtest = new PlaytestSession();
            playtest.StepEntered("scrap", "Gather Scrap", 0f);
            playtest.Skip(1f);
            playtest.StepEntered("coal", "Buy Coal", 2f);
            playtest.StepEntered("scrap", "Gather Scrap", 3f); // F1 after finishing restarts the guide

            Assert.IsNull(playtest.Current);
            Assert.AreEqual(1, playtest.Answers.Count);
            Assert.AreEqual("skipped", playtest.Answers[0].Choice);
        }
    }
}
