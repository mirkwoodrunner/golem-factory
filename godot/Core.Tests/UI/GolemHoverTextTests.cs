using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    // Hover (docs/onboarding-feedback-design.md §4): the pick, the tile names and every sentence
    // the card says, each within the project font's character set.
    public class GolemHoverTextTests
    {
        private static bool InFont(char c) =>
            (c >= 0x20 && c <= 0x7E) || (c >= 0xA0 && c <= 0xFF) || c == '→' || c == '≥' || c == '█' || c == '░' || c == '\n';

        private static void AssertInFont(string text)
        {
            foreach (char c in text ?? "")
            {
                Assert.IsTrue(InFont(c), "'" + c + "' (U+" + ((int)c).ToString("X4") + ") is outside the font: " + text);
            }
        }

        private static readonly Vector2Int Behind = new Vector2Int(3, 4);
        private static readonly Vector2Int At = new Vector2Int(3, 5);
        private static readonly Vector2Int InFront = new Vector2Int(3, 6);

        private static GolemEntity Golem(SpatialEndpointRegistry endpoints, params AppendageActionDefinition[] cards)
        {
            var golem = new GolemEntity();
            golem.Configure("PlayerGolem-004", null);
            golem.Program.chassis = new ChassisDefinition { name = "BrassPresser", maxAppendageSlots = 4 };
            golem.Program.logicCore = new LogicCoreDefinition { triggerType = TriggerType.AlwaysOn };
            foreach (AppendageActionDefinition card in cards)
            {
                golem.Program.appendages.Add(card);
            }
            golem.ConfigureSpatial(endpoints, At, Facing.North);
            return golem;
        }

        private static AppendageActionDefinition Haul() =>
            new AppendageActionDefinition { name = "HaulScrap", actionType = AppendageActionType.Haul, durationTicks = 1 };

        private static AppendageActionDefinition Push() =>
            new AppendageActionDefinition { name = "PushOutput", actionType = AppendageActionType.Push, durationTicks = 1 };

        [Test]
        public void AStuckGolem_NamesTheTileTheGoodAndTheFix()
        {
            var endpoints = new SpatialEndpointRegistry();
            endpoints.Register(Behind, new StorageBufferEndpoint(new StorageBuffer("Crate(3,4)", 50)));
            endpoints.Register(InFront, new StorageBufferEndpoint(new StorageBuffer("Crate(3,6)", 50)));
            GolemEntity golem = Golem(endpoints, Haul(), Push());
            golem.Program.SetItemTypeAt(0, ItemType.Scrap);
            golem.Tick(0);

            HoverCard card = GolemHoverText.Compose(golem, golem.Mood, default, new TileNames(endpoints));

            Assert.AreEqual("Brass Presser · PlayerGolem-004", card.Title);
            Assert.AreEqual("Stuck: Crate (3,4) is empty, needs Scrap", card.State);
            Assert.AreEqual("Nothing fills it. Put Scrap in by hand (I), or stand a golem that pushes into it.", card.Fix);
            Assert.AreEqual("From Crate (3,4) → to Crate (3,6)", card.Route);
            AssertInFont(card.Text);
        }

        [Test]
        public void ACollectingHaul_ShowsItsProgress()
        {
            var endpoints = new SpatialEndpointRegistry();
            var crate = new StorageBuffer("Crate(3,4)", 50);
            crate.Deposit(ItemType.Scrap, 5);
            endpoints.Register(Behind, new StorageBufferEndpoint(crate));
            GolemEntity golem = Golem(endpoints, Haul(), Push());
            golem.Program.SetItemTypeAt(0, ItemType.Scrap);
            golem.Program.SetQuantityAt(0, 10);
            golem.Tick(0);

            Assert.AreEqual("5/10 Scrap", StallDiagnostics.HaulProgress(5, 10, ItemType.Scrap));
            HoverCard stuck = GolemHoverText.Compose(golem, GolemMood.Stalled, default, new TileNames(endpoints));
            Assert.AreEqual("Stuck: Crate (3,4) is empty, needs Scrap (5/10)", stuck.State);
            HoverCard waiting = GolemHoverText.Compose(golem, GolemMood.Waiting, default, new TileNames(endpoints));
            Assert.AreEqual("Waiting for Scrap · 5/10 · Crate (3,4) is empty", waiting.State);
        }

        [Test]
        public void EveryMoodAndReason_ReadsWithinTheFont()
        {
            var endpoints = new SpatialEndpointRegistry();
            GolemEntity golem = Golem(endpoints, Haul(), Push());
            var names = new TileNames(endpoints);
            foreach (GolemMood mood in System.Enum.GetValues(typeof(GolemMood)))
            {
                HoverCard card = GolemHoverText.Compose(golem, mood, default, names, strainingHeld: true);
                Assert.IsNotEmpty(card.State, mood.ToString());
                AssertInFont(card.Text);
            }
            foreach (StallReason reason in System.Enum.GetValues(typeof(StallReason)))
            {
                AssertInFont(GolemHoverText.Fix(golem, new StallFacts(AppendageActionType.Haul, false, false, false, false, ItemType.IronPlate)));
            }
        }

        [Test]
        public void TheStateLines_ByMood()
        {
            var endpoints = new SpatialEndpointRegistry();
            GolemEntity golem = Golem(endpoints, Haul(), Push());
            var names = new TileNames(endpoints);
            StringAssert.StartsWith("Working · step 1 of 2, HaulScrap", GolemHoverText.Compose(golem, GolemMood.Working, default, names).State);
            StringAssert.StartsWith("Sleeping until its trigger: always on", GolemHoverText.Compose(golem, GolemMood.Sleeping, default, names).State);
            Assert.AreEqual("No program · press E at it to open the Workbench",
                GolemHoverText.Compose(golem, GolemMood.Unprogrammed, default, names).State);

            golem.Inventory.AddInput(ItemType.Scrap, 10);
            Assert.AreEqual("Working · output 10/12 Scrap, nearly full",
                GolemHoverText.Compose(golem, GolemMood.Straining, default, names, strainingHeld: true).State);
            StringAssert.StartsWith("Working · step",
                GolemHoverText.Compose(golem, GolemMood.Straining, default, names, strainingHeld: false).State,
                "Straining shows only once held for its dwell");
        }

        [Test]
        public void TileNames_ForEachShape()
        {
            var endpoints = new SpatialEndpointRegistry();
            var masts = new FreightMastRegistry();
            endpoints.Register(new Vector2Int(3, 4), new StorageBufferEndpoint(new StorageBuffer("Crate(3,4)")));
            endpoints.Register(new Vector2Int(9, 1), new StorageBufferEndpoint(new StorageBuffer("Mast(9,1)")));
            masts.Register(new Vector2Int(9, 1), "Mast");
            endpoints.Register(new Vector2Int(5, 5), new StorageBufferEndpoint(new StorageBuffer(SandboxWorld.PostBufferId)));
            endpoints.Register(new Vector2Int(0, -12), new BeltSegmentEndpoint(new GolemFactory.Belts.BeltSegment("Belt(0,-12)#3", 1)));
            endpoints.Register(new Vector2Int(7, 7), new ResourceNodeEndpoint(new ResourceNode("CopperOreNode", ItemType.CopperOre)));
            var names = new TileNames(endpoints, masts);

            Assert.AreEqual("Crate (3,4)", names.Name(new Vector2Int(3, 4)));
            Assert.AreEqual("Freight Mast (9,1)", names.Name(new Vector2Int(9, 1)));
            Assert.AreEqual("Pneumatic Post", names.Name(new Vector2Int(5, 5)));
            Assert.AreEqual("Belt (0,-12)", names.Name(new Vector2Int(0, -12)));
            Assert.AreEqual("Copper Ore stall", names.Name(new Vector2Int(7, 7)));
            Assert.AreEqual("the floor at (3,2)", names.Name(new Vector2Int(3, 2)));
        }

        [Test]
        public void ThePick_ThePointersCellThenTheCellSouth_NeverAHeldGolem()
        {
            var endpoints = new SpatialEndpointRegistry();
            GolemEntity lower = Golem(endpoints);
            var golems = new List<GolemEntity> { lower };

            Assert.AreSame(lower, GolemInspector.Pick(At, golems));
            Assert.AreSame(lower, GolemInspector.Pick(new Vector2Int(At.x, At.y + 1), golems), "the body covers the cell above its feet");
            Assert.IsNull(GolemInspector.Pick(new Vector2Int(At.x, At.y + 2), golems));

            var upper = new GolemEntity();
            upper.Configure("PlayerGolem-009", null);
            upper.ConfigureSpatial(endpoints, new Vector2Int(At.x, At.y + 1), Facing.North);
            golems.Add(upper);
            Assert.AreSame(upper, GolemInspector.Pick(new Vector2Int(At.x, At.y + 1), golems), "the pointer's own cell wins");

            lower.SetHeld(true);
            Assert.IsNull(GolemInspector.Pick(At, golems));

            var inspector = new GolemInspector(() => golems);
            inspector.Point(new Vector2Int(At.x, At.y + 1));
            Assert.AreSame(upper, inspector.Hovered);
            inspector.Point(null);
            Assert.IsNull(inspector.Hovered);
        }

        [Test]
        public void TheTileOverlay_OneAnswerForCarryFocusAndHover()
        {
            var cell = new Vector2Int(1, 1);
            var landing = new Vector2Int(4, 4);
            Assert.IsTrue(GolemTileOverlay.Decide(true, false, null, false, cell).Show);
            Assert.IsTrue(GolemTileOverlay.Decide(false, true, null, false, cell).Show);
            Assert.IsFalse(GolemTileOverlay.Decide(false, false, null, false, cell).Show);
            GolemTileOverlay.Decision carried = GolemTileOverlay.Decide(true, true, landing, true, cell);
            Assert.IsTrue(carried.Show);
            Assert.AreEqual(landing, carried.Cell);
        }
    }
}
