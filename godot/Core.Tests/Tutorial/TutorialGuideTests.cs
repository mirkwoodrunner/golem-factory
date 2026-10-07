using System.IO;
using System.Linq;
using GolemFactory.Buildings;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.Tests.Data;
using GolemFactory.Tests.World;
using GolemFactory.Tutorial;
using GolemFactory.World;
using NUnit.Framework;
using Vector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Tests.Tutorial
{
    /// <summary>
    /// The step-by-step guide (G10, at the user's call). Walked end to end through a fresh
    /// Sandbox with the game's own verbs, so every step's "done" is a state the real game reaches.
    /// </summary>
    public class TutorialGuideTests
    {
        private static SandboxWorld Compose()
        {
            DefinitionSet definitions = AuthoredData.Load();
            return SandboxWorld.Compose(definitions, SandboxSetupTests.LoadReal(), PlaceableCatalogTests.LoadReal(definitions));
        }

        private static void Give(SandboxWorld world, string item, int quantity) =>
            world.Buffers.Deposit(world.StockpileBufferId, item, quantity);

        private static PlaceableBuilding Place(SandboxWorld world, string key, int x, int y)
        {
            world.Build.SetActivePrefab(world.Placeables.Single(p => p.Key == key).Prefab);
            world.Build.PlaceOrRemove(new Vector2Int(x, y));
            world.Build.CancelPlacement();
            return world.Build.Buildings.Single(b => !b.IsRemoved && b.Cell == new Vector2Int(x, y));
        }

        private static string StepId(SandboxWorld world)
        {
            world.Tutorial.Update();
            return world.Tutorial.Current?.Id ?? "finished";
        }

        [Test]
        public void TheSandboxStartsTheGuide_OnItsFirstStep_PointingAtTheScrapStall()
        {
            SandboxWorld world = Compose();

            Assert.IsNotNull(world.Tutorial, "sandbox.json turns the guide on");
            Assert.IsTrue(world.Tutorial.IsShowing);
            Assert.AreEqual("scrap", StepId(world));
            SandboxSetup.NodeEntry stall = world.Setup.nodes.Single(n => n.id == "ScrapNode");
            Assert.AreEqual(new Vector2Int(stall.x, stall.y), world.Tutorial.TargetCell);
            Assert.AreEqual("Scrap  0 / 20", world.Tutorial.Progress);
        }

        [Test]
        public void TheWholeGuide_FromAColdStartToTheFirstIronPlate()
        {
            SandboxWorld world = Compose();
            DefinitionSet defs = world.Definitions;

            Give(world, ItemType.Scrap, 20);
            Assert.AreEqual("coal", StepId(world));

            Give(world, ItemType.Coal, 5);
            Assert.AreEqual("coke", StepId(world));

            Give(world, ItemType.Coke, 5);
            Assert.AreEqual("iron", StepId(world));

            Give(world, ItemType.IronPlate, 10);
            Assert.AreEqual("boiler", StepId(world));
            Assert.AreEqual("BoilerPrefab", world.Tutorial.Current.MenuKey, "the step lights the build menu's Boiler row");

            Give(world, ItemType.Scrap, 100);
            Give(world, ItemType.IronPlate, 10);
            PlaceableBuilding boiler = Place(world, "BoilerPrefab", world.Tutorial.BoilerSpot.x, world.Tutorial.BoilerSpot.y);
            Assert.AreEqual("fuel", StepId(world));
            Assert.AreEqual(boiler.Cell, world.Tutorial.TargetCell, "points at the boiler it means");

            Assert.IsTrue(world.Interactor.TryRefuelBoiler(boiler.GetPart<PlaceableBoiler>()), "precondition: fuelled");
            Assert.AreEqual("golem", StepId(world));

            Assert.IsTrue(world.StarterStation.TryConstructGolem(defs.Chassis["ClockworkScavenger"], out GolemEntity golem));
            Assert.AreEqual("program", StepId(world));

            golem.Program.logicCore = defs.LogicCores["AlwaysOnCore"];
            golem.Program.TryAddAppendage(defs.Appendages["ExtractScrap"]);
            golem.Program.TryAddAppendage(defs.Appendages["PushOutput"]);
            Assert.AreEqual("depot", StepId(world));

            Place(world, "DepotPrefab", -8, -14);
            Assert.AreEqual("work", StepId(world));

            // Scrap stall behind, depot in front, boiler beside: the marked layout.
            golem.SetPlacement(world.Tutorial.GolemSpot, Facing.North);
            world.Clock.Play();
            for (int frame = 0; frame < 600 && StepId(world) == "work"; frame++)
            {
                world.Advance(1f / 30f);
            }
            Assert.AreEqual("gears", StepId(world), "a finished cycle ends chapter 1");

            // --- Chapter 2: the Brass Presser ---------------------------------------------------
            Give(world, ItemType.Gear, 10);
            Assert.AreEqual("claim", StepId(world));

            // Claim through the real board: other cards first if Scrap Reclamation is not yet
            // offered, exactly as the step tells the player to.
            Give(world, ItemType.Scrap, 400);
            Give(world, ItemType.IronPlate, 100);
            Give(world, ItemType.Gear, 20);
            Give(world, ItemType.Coal, 40);
            Give(world, ItemType.Coke, 100);
            var claimed = new System.Collections.Generic.List<string>();
            for (int attempt = 0; attempt < 30 && StepId(world) == "claim"; attempt++)
            {
                int slot = Enumerable.Range(0, world.AssemblyLine.SlotCount)
                    .FirstOrDefault(i => world.AssemblyLine.GetCard(i)?.appendage?.name == "AssembleScrapReclamation");
                claimed.Add(world.AssemblyLine.GetCard(slot)?.name);
                Assert.IsTrue(world.AssemblyLineBoard.Claim(slot), world.AssemblyLineBoard.Status + " after " + string.Join(", ", claimed)
                    + " | waiting: " + string.Join(", ", world.AssemblyLine.WaitingCards.Select(c => c.name)));
            }
            Assert.AreEqual(1, claimed.Count, "Scrap Reclamation is on show without claiming anything else: " + string.Join(", ", claimed));
            Assert.AreEqual("presser", StepId(world));

            Assert.IsTrue(world.StarterStation.TryConstructGolem(defs.Chassis["BrassPresser"], out GolemEntity presser));
            Assert.AreEqual("pipe", StepId(world));
            Assert.AreEqual("SteamPipePrefab", world.Tutorial.Current.MenuKey);
            CollectionAssert.AreEqual(world.Tutorial.PipeSpots, world.Tutorial.Current.Spots, "both pipe tiles marked");

            foreach (Vector2Int pipe in world.Tutorial.PipeSpots)
            {
                Place(world, "SteamPipePrefab", pipe.x, pipe.y);
            }
            Assert.AreEqual("depot2", StepId(world));

            Place(world, "DepotPrefab", world.Tutorial.PresserDepotSpot.x, world.Tutorial.PresserDepotSpot.y);
            Assert.AreEqual("program-presser", StepId(world));

            presser.Program.logicCore = defs.LogicCores["AlwaysOnCore"];
            Assert.IsTrue(presser.Program.TryAddAppendage(defs.Appendages["HaulScrap"]));
            Assert.IsTrue(presser.Program.TryAddAppendage(defs.Appendages["AssembleScrapReclamation"]));
            Assert.IsTrue(presser.Program.TryAddAppendage(defs.Appendages["PushOutput"]));
            Assert.AreEqual("work-presser", StepId(world));

            // Depot 1 behind (Scrap from the stockpile), depot 2 in front, a pipe beside.
            presser.SetPlacement(world.Tutorial.PresserSpot, Facing.North);
            for (int frame = 0; frame < 1200 && StepId(world) == "work-presser"; frame++)
            {
                world.Advance(1f / 30f);
            }
            Assert.AreEqual("done", StepId(world),
                $"the Presser's first cycle ends chapter 2 (presser {presser.Program.State}, {presser.StallReason} {presser.StallResourceId})");

            world.Tutorial.Finish();
            Assert.IsFalse(world.Tutorial.IsShowing);
        }

        [Test]
        public void StepsAlreadyDone_ArePassedStraightOver_AndTheGuideNeverGoesBack()
        {
            SandboxWorld world = Compose();
            Give(world, ItemType.Scrap, 20);
            Give(world, ItemType.Coal, 5);
            Give(world, ItemType.Coke, 5);

            Assert.AreEqual("iron", StepId(world), "three steps the stockpile already shows");

            world.Buffers.TryWithdraw(world.StockpileBufferId, ItemType.Coke, 5);
            Assert.AreEqual("iron", StepId(world), "spending the Coke does not undo the step");
        }

        [Test]
        public void WhileShortOfScrap_TheArrowPointsBackAtTheScrapStall()
        {
            SandboxWorld world = Compose();
            Give(world, ItemType.Scrap, 20);
            Give(world, ItemType.Coal, 5);
            Give(world, ItemType.Coke, 5);
            Give(world, ItemType.IronPlate, 10);
            Assert.AreEqual("boiler", StepId(world));
            world.Buffers.TryWithdraw(world.StockpileBufferId, ItemType.Scrap, 20);

            SandboxSetup.NodeEntry stall = world.Setup.nodes.Single(n => n.id == "ScrapNode");
            Assert.AreEqual(new Vector2Int(stall.x, stall.y), world.Tutorial.TargetCell);
        }

        [Test]
        public void TheFirstGolemsLayout_IsMarkedBesideTheScrapStall()
        {
            SandboxWorld world = Compose();
            SandboxSetup.NodeEntry stall = world.Setup.nodes.Single(n => n.id == "ScrapNode");
            var s = new Vector2Int(stall.x, stall.y);

            // depot / golem + boiler / stall, the golem facing the depot.
            Assert.AreEqual(s + new Vector2Int(0, 1), world.Tutorial.GolemSpot);
            Assert.AreEqual(s + new Vector2Int(0, 2), world.Tutorial.DepotSpot);
            Assert.AreEqual(s + new Vector2Int(1, 1), world.Tutorial.BoilerSpot);

            TutorialStep boiler = world.Tutorial.Steps.Single(t => t.Id == "boiler");
            TutorialStep depot = world.Tutorial.Steps.Single(t => t.Id == "depot");
            TutorialStep work = world.Tutorial.Steps.Single(t => t.Id == "work");
            Assert.AreEqual(world.Tutorial.BoilerSpot, boiler.Spot);
            Assert.AreEqual(world.Tutorial.DepotSpot, depot.Spot);
            Assert.AreEqual(world.Tutorial.GolemSpot, work.Spot);
            Assert.AreEqual(Facing.North, work.SpotFacing, "facing the depot");
        }

        [Test]
        public void ABoilerOnTheMarkedTile_CompletesTheStep_ButOneFarAwayDoesNot()
        {
            SandboxWorld world = Compose();
            Give(world, ItemType.Scrap, 200);
            Give(world, ItemType.Coal, 5);
            Give(world, ItemType.Coke, 5);
            Give(world, ItemType.IronPlate, 40);
            Assert.AreEqual("boiler", StepId(world));
            Assert.AreEqual(world.Tutorial.BoilerSpot, world.Tutorial.TargetCell, "the arrow points at the marked tile");

            Place(world, "BoilerPrefab", 8, 8);
            Assert.AreEqual("boiler", StepId(world), "a boiler whose steam cannot reach the golem's tile");

            Place(world, "BoilerPrefab", world.Tutorial.BoilerSpot.x, world.Tutorial.BoilerSpot.y);
            Assert.AreEqual("fuel", StepId(world));
            Assert.AreEqual(world.Tutorial.BoilerSpot, world.Tutorial.TargetCell, "fuel the one beside the golem's tile");
        }

        [Test]
        public void ADepotOffTheMarkedTile_DoesNotCompleteTheStep()
        {
            SandboxWorld world = Compose();
            DefinitionSet defs = world.Definitions;
            Give(world, ItemType.Scrap, 300);
            Give(world, ItemType.Coal, 5);
            Give(world, ItemType.Coke, 30); // 20 go into the boiler; the Coke step still sees 10
            Give(world, ItemType.IronPlate, 20);
            PlaceableBuilding boiler = Place(world, "BoilerPrefab", world.Tutorial.BoilerSpot.x, world.Tutorial.BoilerSpot.y);
            world.Interactor.TryRefuelBoiler(boiler.GetPart<PlaceableBoiler>());
            world.StarterStation.TryConstructGolem(defs.Chassis["ClockworkScavenger"], out GolemEntity golem);
            golem.Program.logicCore = defs.LogicCores["AlwaysOnCore"];
            golem.Program.TryAddAppendage(defs.Appendages["ExtractScrap"]);
            golem.Program.TryAddAppendage(defs.Appendages["PushOutput"]);
            Assert.AreEqual("depot", StepId(world));

            Place(world, "DepotPrefab", 8, 8);
            Assert.AreEqual("depot", StepId(world));
            Place(world, "DepotPrefab", world.Tutorial.DepotSpot.x, world.Tutorial.DepotSpot.y);
            Assert.AreEqual("work", StepId(world));
            Assert.AreEqual(world.Tutorial.GolemSpot, world.Tutorial.Current.Spot);
        }

        [Test]
        public void SkipAndReopen_AndTheStepIsSaved()
        {
            string path = Path.Combine(Path.GetTempPath(), "golem-factory-guide-" + System.Guid.NewGuid() + ".json");
            try
            {
                SandboxWorld world = Compose();
                Give(world, ItemType.Scrap, 20);
                StepId(world);
                world.Tutorial.Dismiss();
                Assert.IsFalse(world.Tutorial.IsShowing);
                world.SaveTo(path);

                SandboxWorld fresh = Compose();
                fresh.LoadFrom(path);
                Assert.AreEqual(1, fresh.Tutorial.Index, "the step it was on");
                Assert.IsFalse(fresh.Tutorial.IsShowing, "still put away");

                fresh.Tutorial.Reopen();
                Assert.IsTrue(fresh.Tutorial.IsShowing);
                Assert.AreEqual("coal", fresh.Tutorial.Current.Id);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
