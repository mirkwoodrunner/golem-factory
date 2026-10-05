using System.Linq;
using GolemFactory.Buildings;
using GolemFactory.Compat;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.Steam;
using GolemFactory.Tests.Data;
using GolemFactory.Tests.World;
using GolemFactory.World;
using NUnit.Framework;

namespace GolemFactory.Tests.Steam
{
    /// <summary>
    /// Only working golems burn Coke, at 3 a minute each (G10, the user's call from playtest:
    /// "it seems to burn very fast"). Through the real Sandbox: a golem standing beside a
    /// fuelled boiler with nothing to do costs nothing; the same golem working costs 3 a minute.
    /// </summary>
    public class WorkingGolemBurnTests
    {
        private static (SandboxWorld world, SteamBoiler boiler, GolemEntity golem) BoilerAndGolem()
        {
            DefinitionSet definitions = AuthoredData.Load();
            SandboxWorld world = SandboxWorld.Compose(definitions, SandboxSetupTests.LoadReal(), PlaceableCatalogTests.LoadReal(definitions));
            world.Buffers.Deposit(world.StockpileBufferId, ItemType.Scrap, 100);
            world.Buffers.Deposit(world.StockpileBufferId, ItemType.IronPlate, 10);

            // Beside the Scrap stall, as the guide lays it out: stall behind, depot in front.
            SandboxSetup.NodeEntry stall = world.Setup.nodes.Single(n => n.id == "ScrapNode");
            var golemCell = new Vector2Int(stall.x, stall.y + 1);
            world.Build.SetActivePrefab(world.Placeables.Single(p => p.Key == "BoilerPrefab").Prefab);
            world.Build.PlaceOrRemove(golemCell + new Vector2Int(1, 0));
            world.Build.SetActivePrefab(world.Placeables.Single(p => p.Key == "DepotPrefab").Prefab);
            world.Build.PlaceOrRemove(golemCell + new Vector2Int(0, 1));
            world.Build.CancelPlacement();
            PlaceableBoiler boiler = world.Build.Buildings.Select(b => b.GetPart<PlaceableBoiler>()).Single(b => b != null);
            boiler.Boiler.AddCoke(100);

            Assert.IsTrue(world.StarterStation.TryConstructGolem(definitions.Chassis["ClockworkScavenger"], out GolemEntity golem));
            golem.SetPlacement(golemCell, Facing.North);
            world.Clock.Play();
            return (world, boiler.Boiler, golem);
        }

        private static void RunOneMinute(SandboxWorld world)
        {
            // 60 s at 10 ticks a second, in frames.
            for (int frame = 0; frame < 600; frame++)
            {
                world.Advance(0.1f);
            }
        }

        [Test]
        public void AGolemWithNothingToDo_BesideAFuelledBoiler_BurnsNothing()
        {
            var (world, boiler, _) = BoilerAndGolem();

            RunOneMinute(world);

            Assert.AreEqual(100, boiler.CokeStock, "unprogrammed: powered, idle, free");
        }

        [Test]
        public void AWorkingGolem_BurnsThreeCokeAMinute()
        {
            var (world, boiler, golem) = BoilerAndGolem();
            golem.Program.logicCore = world.Definitions.LogicCores["AlwaysOnCore"];
            golem.Program.TryAddAppendage(world.Definitions.Appendages["ExtractScrap"]);
            golem.Program.TryAddAppendage(world.Definitions.Appendages["PushOutput"]);

            RunOneMinute(world);

            Assert.AreNotEqual(GolemState.Stalled, golem.Program.State, $"precondition: working, not stalled ({golem.StallReason})");
            Assert.AreEqual(SteamGaugeUtility.CokePerMinutePerGolem, 100 - boiler.CokeStock, 1,
                "about 3 Coke in a minute of work");
        }
    }
}
