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

            Assert.AreEqual("Brass Presser #4", card.Title);
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

        // --- §17: the waiting golem names its feeder ---------------------------------------

        /// <summary>A floor of named golems with a probe, composing cards as SandboxWorld.HoverCard does.</summary>
        private sealed class Street
        {
            public readonly SpatialEndpointRegistry Endpoints = new SpatialEndpointRegistry();
            public readonly GolemFactory.World.BeltNetwork Belts = new GolemFactory.World.BeltNetwork();
            public readonly List<GolemEntity> Golems = new List<GolemEntity>();
            public readonly StallFactsProbe Probe;
            public readonly TileNames Names;
            public readonly List<string> Lines = new List<string>();

            public Street(System.Func<TruckloadMarket> market = null)
            {
                Belts.Configure(new GolemFactory.Belts.ConveyorSystem(), Endpoints, 4);
                Probe = new StallFactsProbe(() => Golems, Endpoints, Belts, market);
                Names = new TileNames(Endpoints);
            }

            public StorageBuffer Crate(Vector2Int cell, string id = null)
            {
                var buffer = new StorageBuffer(id ?? "Crate(" + cell.x + "," + cell.y + ")", 50);
                Endpoints.Register(cell, new StorageBufferEndpoint(buffer));
                return buffer;
            }

            /// <summary>One store on two cells, as Post crates share one.</summary>
            public void Shared(string id, Vector2Int a, Vector2Int b)
            {
                var buffer = new StorageBuffer(id, 50);
                Endpoints.Register(a, new StorageBufferEndpoint(buffer));
                Endpoints.Register(b, new StorageBufferEndpoint(buffer));
            }

            /// <summary>"Scavenger #n" at <paramref name="cell"/>, facing north: Haul(good) from the south, Push north.</summary>
            public GolemEntity Golem(string chassis, int number, Vector2Int cell, string good = ItemType.Scrap,
                TriggerType trigger = TriggerType.AlwaysOn)
            {
                var golem = new GolemEntity();
                golem.Configure(GolemNames.IdPrefix + number.ToString("000"), null);
                golem.Program.chassis = new ChassisDefinition { name = chassis, maxAppendageSlots = 4 };
                golem.Program.logicCore = new LogicCoreDefinition { triggerType = trigger, intervalTicks = 1000003 };
                golem.Program.appendages.Add(Haul());
                golem.Program.appendages.Add(Push());
                golem.Program.SetItemTypeAt(0, good);
                golem.ConfigureSpatial(Endpoints, cell, Facing.North);
                golem.ConfigureStallFacts(Probe.Read);
                Golems.Add(golem);
                return golem;
            }

            /// <summary>The card's state line, the feeders passed exactly when SandboxWorld.HoverCard passes them.</summary>
            public string State(GolemEntity golem)
            {
                GolemMood mood = golem.Mood;
                StallFacts facts = golem.StallFacts;
                IReadOnlyList<GolemEntity> feeders = mood == GolemMood.Waiting && facts.IsTake && facts.SourceGives && facts.SourceFed
                    ? Probe.FeedersOf(golem)
                    : null;
                HoverCard card = GolemHoverText.Compose(golem, mood, facts, Names, false, feeders);
                Lines.Add(card.Text);
                return card.State;
            }
        }

        private const string Scavenger = "ClockworkScavenger";
        private const string Presser = "BrassPresser";

        [Test]
        public void AWaitingGolem_NamesItsFeeder()
        {
            var street = new Street();
            street.Crate(new Vector2Int(0, 0));
            street.Crate(new Vector2Int(0, 2));
            street.Golem(Scavenger, 1, new Vector2Int(0, 1));
            GolemEntity presser = street.Golem(Presser, 2, new Vector2Int(0, 3));
            presser.Tick(0);

            Assert.AreEqual(GolemMood.Waiting, presser.Mood);
            Assert.AreEqual("Waiting for Scrap · Scavenger #1 brings it", street.State(presser));
        }

        [Test]
        public void AWaitingGolem_NamesASleepingFeeder_WhenItsTriggerFires()
        {
            var street = new Street();
            street.Crate(new Vector2Int(0, 0));
            street.Crate(new Vector2Int(0, 2));
            street.Golem(Scavenger, 1, new Vector2Int(0, 1), trigger: TriggerType.Interval);
            GolemEntity presser = street.Golem(Presser, 2, new Vector2Int(0, 3));
            presser.Tick(0);

            Assert.AreEqual("Waiting for Scrap · Scavenger #1 brings it when its trigger fires", street.State(presser));
        }

        [Test]
        public void TwoFeeders_AreNamedInNumberOrder()
        {
            var street = new Street();
            street.Crate(new Vector2Int(0, 0));
            street.Crate(new Vector2Int(3, 0));
            street.Shared("Crate(0,2)", new Vector2Int(0, 2), new Vector2Int(3, 2));
            street.Golem(Scavenger, 7, new Vector2Int(0, 1));
            street.Golem(Presser, 3, new Vector2Int(3, 1));
            GolemEntity taker = street.Golem(Presser, 9, new Vector2Int(0, 3));
            taker.Tick(0);

            Assert.AreEqual("Waiting for Scrap · Presser #3 and Scavenger #7 bring it", street.State(taker));
        }

        [Test]
        public void ThreeFeeders_ReadAsOthers()
        {
            var street = new Street();
            street.Shared("Crate(0,2)", new Vector2Int(0, 2), new Vector2Int(3, 2));
            street.Endpoints.Register(new Vector2Int(6, 2), street.Endpoints.TryGetEndpoint(new Vector2Int(0, 2), out IItemEndpoint same) ? same : null);
            foreach (int x in new[] { 0, 3, 6 })
            {
                street.Crate(new Vector2Int(x, 0));
            }
            street.Golem(Scavenger, 8, new Vector2Int(0, 1));
            street.Golem(Scavenger, 2, new Vector2Int(3, 1));
            street.Golem(Scavenger, 5, new Vector2Int(6, 1));
            GolemEntity taker = street.Golem(Presser, 11, new Vector2Int(0, 3));
            taker.Tick(0);

            Assert.AreEqual(3, street.Probe.FeedersOf(taker).Count);
            Assert.AreEqual(2, street.Probe.FeedersOf(taker, 2).Count, "stops at max");
            Assert.AreEqual("Waiting for Scrap · Scavenger #2 and others bring it", street.State(taker));
        }

        [Test]
        public void AFeederUpABelt_IsNamed()
        {
            var street = new Street();
            street.Crate(new Vector2Int(0, 0));
            for (int y = 2; y <= 6; y++)
            {
                street.Belts.TryPlace(new Vector2Int(0, y), Facing.North, out _);
            }
            street.Golem(Scavenger, 7, new Vector2Int(0, 1));        // pushes onto (0,2)
            GolemEntity unloader = street.Golem(Presser, 8, new Vector2Int(0, 7)); // takes from (0,6), five cells on
            unloader.Tick(0);

            Assert.AreEqual(StallReason.BeltEmpty, unloader.StallReason);
            Assert.AreEqual("Waiting for Scrap · Scavenger #7 brings it", street.State(unloader));
        }

        [Test]
        public void AChain_NamesTheNearFeeder_NotTheRoot()
        {
            var street = new Street();
            street.Crate(new Vector2Int(0, 0));
            street.Crate(new Vector2Int(0, 2));
            street.Crate(new Vector2Int(0, 4));
            GolemEntity s1 = street.Golem(Scavenger, 1, new Vector2Int(0, 1));
            GolemEntity s2 = street.Golem(Scavenger, 2, new Vector2Int(0, 3));
            GolemEntity p = street.Golem(Presser, 3, new Vector2Int(0, 5));
            s1.Tick(0);
            s2.Tick(0);
            p.Tick(0);

            Assert.AreEqual(GolemMood.Stalled, s1.Mood, "red at the root");
            Assert.AreEqual("Waiting for Scrap · Scavenger #2 brings it", street.State(p));
            Assert.AreEqual("Waiting for Scrap · Scavenger #1 brings it", street.State(s2), "hovering S2 names S1: one link a hover");
        }

        [Test]
        public void ATruckloadComing_NamesNoFeeder()
        {
            var nodes = new ResourceNodeRegistry();
            var node = new ResourceNode("CoalNode", ItemType.Coal, 0);
            nodes.Register(node);
            var market = new TruckloadMarket(nodes, new GameMode(false));
            market.AddOffer(new MarketOffer("CoalNode", new List<RecipeIngredient>(), 1, 5));
            var street = new Street(() => market);
            street.Endpoints.Register(new Vector2Int(0, 0), new ResourceNodeEndpoint(node));
            GolemEntity golem = street.Golem(Scavenger, 1, new Vector2Int(0, 1));
            golem.Program.appendages[0] = new AppendageActionDefinition { name = "ExtractScrap", actionType = AppendageActionType.ExtractFromNode, durationTicks = 1 };
            golem.Program.SetItemTypeAt(0, "");
            golem.Tick(0);
            Assert.AreEqual(MarketOrderResult.Ordered, market.TryOrder("CoalNode", null, null, 0));

            Assert.AreEqual(GolemMood.Waiting, golem.Mood);
            Assert.AreEqual("Waiting for Coal · a truckload is on its way", street.State(golem));
        }

        [Test]
        public void ACollectingHaul_KeepsItsProgress_AndNamesItsFeeder()
        {
            var street = new Street();
            street.Crate(new Vector2Int(0, 0));
            street.Crate(new Vector2Int(0, 2)).Deposit(ItemType.Scrap, 5);
            street.Golem(Scavenger, 1, new Vector2Int(0, 1));
            GolemEntity presser = street.Golem(Presser, 2, new Vector2Int(0, 3));
            presser.Program.SetQuantityAt(0, 10);
            presser.Tick(0);

            Assert.AreEqual("Waiting for Scrap · 5/10 · Scavenger #1 brings it", street.State(presser));
        }

        [Test]
        public void TheFeederLine_KeepsTheFontRule_AndShowsNoId()
        {
            var street = new Street();
            street.Shared("Crate(0,2)", new Vector2Int(0, 2), new Vector2Int(3, 2));
            street.Crate(new Vector2Int(0, 0));
            street.Crate(new Vector2Int(3, 0));
            street.Golem(Scavenger, 4, new Vector2Int(0, 1), trigger: TriggerType.Signal);
            GolemEntity taker = street.Golem(Presser, 6, new Vector2Int(0, 3));
            taker.Tick(0);
            street.State(taker);                              // one feeder, its trigger clause
            street.Golem(Presser, 5, new Vector2Int(3, 1));
            street.State(taker);                              // two
            street.Golems[0].SetHeld(true);
            street.State(taker);                              // one again, Always On

            Assert.AreEqual(3, street.Lines.Count);
            foreach (string text in street.Lines)
            {
                AssertInFont(text);
                Assert.IsNull(PlayerText.RawIdIn(text), text);
            }
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
