using System.Linq;
using GolemFactory.Data;
using GolemFactory.Tests.Data;
using GolemFactory.Tests.World;
using GolemFactory.Tutorial;
using GolemFactory.World;
using NUnit.Framework;

namespace GolemFactory.Tests.Tutorial
{
    /// <summary>
    /// The playtest kit (G10): fast-forward a chapter by PERFORMING its steps, so later chapters
    /// can be playtested from the ground a player who did the earlier ones would stand on.
    /// </summary>
    public class PlaytestKitTests
    {
        private static SandboxWorld Compose()
        {
            DefinitionSet definitions = AuthoredData.Load();
            SandboxWorld world = SandboxWorld.Compose(definitions, SandboxSetupTests.LoadReal(), PlaceableCatalogTests.LoadReal(definitions));
            world.Clock.Play();
            return world;
        }

        /// <summary>Runs the world until the kit stops, at most a simulated two minutes.</summary>
        private static void RunKit(SandboxWorld world)
        {
            Assert.IsTrue(world.Tutorial.StartKit(), "the kit started");
            for (int frame = 0; frame < 3600 && world.Tutorial.KitRunning; frame++)
            {
                world.Advance(1f / 30f);
            }
            Assert.IsFalse(world.Tutorial.KitRunning, $"the kit finished (stuck on '{world.Tutorial.Current?.Title}')");
        }

        [Test]
        public void EveryStep_ButTheLast_HasSomethingTheKitCanDo()
        {
            SandboxWorld world = Compose();
            foreach (TutorialStep step in world.Tutorial.Steps.Where(s => s.Id != "done"))
            {
                Assert.IsTrue(world.Tutorial.HasKitAction(step.Id), step.Id);
            }
        }

        [Test]
        public void TheKit_CarriesAFreshGame_ThroughEveryChapter()
        {
            SandboxWorld world = Compose();

            RunKit(world);
            Assert.AreEqual("gears", world.Tutorial.Current.Id, "chapter 1 done: the Scavenger works");
            Assert.AreEqual(2, world.Tutorial.ChapterOf(world.Tutorial.Index));

            RunKit(world);
            Assert.AreEqual("coking-card", world.Tutorial.Current.Id, "chapter 2 done: the Presser makes Iron Plate");

            RunKit(world);
            Assert.AreEqual("patent", world.Tutorial.Current.Id, "chapter 3 done: Coke reaches the new boiler");

            RunKit(world);
            Assert.AreEqual("done", world.Tutorial.Current.Id, "chapter 4 done: patented, stamped, turned, stalled and resumed");

            // The ground is real: the layouts stand, and the golems are the ones a player builds.
            Assert.AreEqual(4, world.Golems.Count(g => !g.IsRemoved));
            Assert.IsTrue(world.Build.Buildings.Any(b => !b.IsRemoved && b.Cell == world.Tutorial.Boiler2Spot));

            var kit = world.Tutorial.Playtest.KitUses;
            Assert.AreEqual(4, kit.Count, "the report says what was fast-forwarded");
            StringAssert.Contains("chapter 1", kit[0]);
            StringAssert.Contains("Playtest kit", world.Tutorial.Playtest.Compose("", world.Tutorial.Now));
        }

        [Test]
        public void TheKit_StartsFromWhereverThePlayerIs()
        {
            // Half-way through chapter 1 by hand, then the kit finishes it.
            SandboxWorld world = Compose();
            world.Buffers.Deposit(world.StockpileBufferId, Economy.ItemType.Scrap, 20);
            world.Buffers.Deposit(world.StockpileBufferId, Economy.ItemType.Coal, 5);
            world.Tutorial.Update();
            Assert.AreEqual("coke", world.Tutorial.Current.Id);

            RunKit(world);

            Assert.AreEqual("gears", world.Tutorial.Current.Id);
            StringAssert.Contains("from 'Make Coke'", world.Tutorial.Playtest.KitUses.Single());
        }
    }
}
