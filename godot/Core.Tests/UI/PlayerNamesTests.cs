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
    // Phase 2 critique: the player reads names, never ids. The badge and the alerts strip printed
    // "Crate(2,-1)" and "PlayerGolem-004"; the Workbench's card subtitles "ScrapNode → ScrapBeltA";
    // its header "TARGET · PlayerGolem-001". One naming path (GolemNames, TileNames, ItemTiers).
    public class PlayerNamesTests
    {
        private static readonly Vector2Int Behind = new Vector2Int(2, -2);
        private static readonly Vector2Int At = new Vector2Int(2, -1);
        private static readonly Vector2Int InFront = new Vector2Int(2, 0);

        private static GolemEntity Golem(string id, string chassis, SpatialEndpointRegistry endpoints = null,
            params AppendageActionDefinition[] cards)
        {
            var golem = new GolemEntity();
            golem.Configure(id, null);
            if (chassis != null)
            {
                golem.Program.chassis = new ChassisDefinition { name = chassis, maxAppendageSlots = 4 };
            }
            golem.Program.logicCore = new LogicCoreDefinition { triggerType = TriggerType.AlwaysOn };
            foreach (AppendageActionDefinition card in cards)
            {
                golem.Program.appendages.Add(card);
            }
            if (endpoints != null)
            {
                golem.ConfigureSpatial(endpoints, At, Facing.North);
            }
            return golem;
        }

        private static AppendageActionDefinition Card(AppendageActionType type, string name = "Card") =>
            new AppendageActionDefinition { name = name, actionType = type, durationTicks = 1 };

        // --- GolemNames ---------------------------------------------------------------------

        [Test]
        public void AGolemIsNamedByChassisAndTheNumberInItsId()
        {
            Assert.AreEqual("Scavenger #1", GolemNames.Short(Golem("PlayerGolem-001", "ClockworkScavenger")));
            Assert.AreEqual("Presser #12", GolemNames.Short(Golem("PlayerGolem-012", "BrassPresser")));
            Assert.AreEqual("Zeppelin #7", GolemNames.Short(Golem("PlayerGolem-007", "ZeppelinFreightLoader")));
            Assert.AreEqual("Overclocker #3", GolemNames.Short(Golem("PlayerGolem-003", "MainspringOverclocker")));
            Assert.AreEqual("Golem #5", GolemNames.Short(Golem("PlayerGolem-005", null)));
            Assert.AreEqual("Clockwork Scavenger #1", GolemNames.Full(Golem("PlayerGolem-001", "ClockworkScavenger")));
        }

        [Test]
        public void AnIdTheStationDidntHandOut_IsKept_RatherThanGuessedAt()
        {
            Assert.AreEqual("Golem GolemD", GolemNames.Short(Golem("GolemD", null)));
            Assert.AreEqual("A golem", GolemNames.Short(null));
        }

        // --- PlayerText ---------------------------------------------------------------------

        [Test]
        public void RawIds_AreCaught_AndPlayerNamesPass()
        {
            foreach (string raw in new[] { "PlayerGolem-004 stalled", "no input in Crate(2,-1)", "Belt(0,-12)#3",
                "ScrapNode depleted", "trigger · ScrapBuffer ≥ 5", "ScrapNode → ScrapBeltA", "need 2 more IronPlate",
                "Mast(9,1)", "PneumaticPost" })
            {
                Assert.IsNotNull(PlayerText.RawIdIn(raw), raw);
            }
            foreach (string clean in new[] { "[!] Crate (2,-1) full", "Scavenger #4 stalled: Scrap cart is depleted",
                "need 2 more Iron Plate", "TARGET  ·  Presser #2", "2/5 Scrap", "waiting for Copper Ore",
                "action · 1t · from the tile behind", "Belt (0,-12)", "Freight Mast (9,1)", "Pneumatic Post",
                "No golem needs you · 1 waiting for goods.", "ENGAGE" })
            {
                Assert.IsNull(PlayerText.RawIdIn(clean), clean);
            }
        }

        // --- The badge and the strip ---------------------------------------------------------

        [Test]
        public void TheBadge_NamesTheTileAndTheGood_NotTheirIds()
        {
            var endpoints = new SpatialEndpointRegistry();
            endpoints.Register(Behind, new StorageBufferEndpoint(new StorageBuffer("Crate(2,-2)", 50)));
            var full = new StorageBuffer("Crate(2,0)", 2);
            full.Deposit(ItemType.IronPlate, 2);
            endpoints.Register(InFront, new StorageBufferEndpoint(full));
            var tiles = new TileNames(endpoints);
            GolemEntity golem = Golem("PlayerGolem-004", "ClockworkScavenger", endpoints,
                Card(AppendageActionType.Haul, "HaulScrap"), Card(AppendageActionType.Push, "PushOutput"));
            golem.Program.SetItemTypeAt(0, ItemType.Scrap);

            golem.Tick(0);
            Assert.AreEqual(StallReason.BufferEmpty, golem.StallReason);
            string empty = StallDiagnostics.DescribeShort(golem, tiles);
            Assert.AreEqual("no input in Crate (2,-2)", empty);

            GolemEntity pusher = Golem("PlayerGolem-005", "ClockworkScavenger", endpoints,
                Card(AppendageActionType.Push, "PushOutput"));
            pusher.Inventory.AddInput(ItemType.IronPlate, 1);
            pusher.Tick(0);
            Assert.AreEqual(StallReason.BeltFull, pusher.StallReason);
            string front = StallDiagnostics.DescribeShort(pusher, tiles);
            Assert.AreEqual("Crate (2,0) full", front);

            Assert.AreEqual("need 2 more Iron Plate",
                StallDiagnostics.DescribeShort(StallReason.MissingItem,
                    StallDiagnostics.PlayerResource(golem, StallReason.MissingItem, ItemType.IronPlate, tiles), 2));
            Assert.AreEqual("nothing behind me at (2,-2)",
                StallDiagnostics.DescribeShort(StallReason.NoSourceAtTile,
                    StallDiagnostics.PlayerResource(golem, StallReason.NoSourceAtTile, Behind.ToString(), tiles)));
            foreach (string text in new[] { empty, front })
            {
                Assert.IsNull(PlayerText.RawIdIn(text), text);
            }
        }

        [Test]
        public void TheStrip_NamesTheGolemAndTheTile_WhenItKnowsThem()
        {
            var endpoints = new SpatialEndpointRegistry();
            var node = new ResourceNode("ScrapNode", ItemType.Scrap, 0);
            endpoints.Register(Behind, new ResourceNodeEndpoint(node));
            endpoints.Register(InFront, new StorageBufferEndpoint(new StorageBuffer("Crate(2,0)", 50)));
            GolemEntity golem = Golem("PlayerGolem-004", "ClockworkScavenger", endpoints,
                Card(AppendageActionType.ExtractFromNode, "ExtractScrap"), Card(AppendageActionType.Push, "PushOutput"));
            var golems = new List<GolemEntity> { golem };
            var strip = new AlertsStrip(() => golems) { Names = new TileNames(endpoints) };

            golem.Tick(0);
            Assert.AreEqual(StallReason.NodeEmpty, golem.StallReason);
            strip.Reconcile();
            Assert.AreEqual("[!] Scavenger #4 stalled: Scrap cart is depleted", strip.Text);
            Assert.IsNull(PlayerText.RawIdIn(strip.Text), strip.Text);

            var bare = new AlertsStrip(() => golems);
            bare.Reconcile();
            StringAssert.Contains("PlayerGolem-004", bare.Text, "without names wired, the ported ids as before");
        }

        // --- The Workbench --------------------------------------------------------------------

        [Test]
        public void TheWorkbench_SaysWhereACardActs_NotItsDemoRoute()
        {
            var extract = new AppendageActionDefinition
            {
                name = "ExtractScrap", actionType = AppendageActionType.ExtractFromNode, durationTicks = 1,
                sourceId = "ScrapNode", destinationId = "ScrapBeltA",
            };
            var haul = new AppendageActionDefinition
            {
                name = "HaulScrap", actionType = AppendageActionType.Haul, durationTicks = 1,
                sourceId = "ScrapNode", destinationId = "ScrapBuffer",
            };
            Assert.AreEqual("action · 1t · from the tile behind", WorkbenchSession.CardSubtitle(null, extract));
            Assert.AreEqual("action · 1t · from the tile behind", WorkbenchSession.CardSubtitle(null, haul));
            Assert.AreEqual("action · 1t · onto the tile in front",
                WorkbenchSession.CardSubtitle(null, Card(AppendageActionType.Push)));
            Assert.AreEqual("action · 1t · to its Freight Mast",
                WorkbenchSession.CardSubtitle(null, Card(AppendageActionType.FreightLaunch)));
            Assert.AreEqual("trigger · on signal from golem #3",
                WorkbenchSession.CardSubtitle(new LogicCoreDefinition { triggerType = TriggerType.Signal, signalGolemId = "PlayerGolem-003" }, null));
            Assert.AreEqual("Presser #1", WorkbenchSession.GolemDisplayName(Golem("PlayerGolem-001", "BrassPresser")));
        }

        // --- Badge overlap -------------------------------------------------------------------

        [Test]
        public void WideBadgesOnGolemsTwoCellsApart_Stagger_AndNarrowOnesAreUntouched()
        {
            // Three red badges, each 3 cells wide, on golems two cells apart in a row.
            var wide = new[]
            {
                new WorldHudRequest(new Vector3(0f, 1.75f, 0f), "A", 1.5f),
                new WorldHudRequest(new Vector3(2f, 1.75f, 0f), "B", 1.5f),
                new WorldHudRequest(new Vector3(4f, 1.75f, 0f), "C", 1.5f),
            };
            Vector3[] placed = WorldHudLayout.Resolve(wide);
            for (int i = 0; i < placed.Length; i++)
            {
                for (int j = i + 1; j < placed.Length; j++)
                {
                    bool apartX = Mathf.Abs(placed[i].x - placed[j].x) >= 3f;
                    bool apartY = Mathf.Abs(placed[i].y - placed[j].y) >= WorldHudLayout.RowHeight * 0.999f;
                    Assert.IsTrue(apartX || apartY, $"{wide[i].OwnerId} and {wide[j].OwnerId} overlap at {placed[i]} / {placed[j]}");
                }
            }
            Assert.AreEqual(1.75f, placed[0].y, 1e-4f, "the first stays on its golem");
            Assert.AreEqual(0f, placed[1].x, 2f, "each stays over its own golem: it only climbs");

            // Zoomed out a badge is taller than a row: it climbs its own height (the moods
            // scenario caught a one-row climb still overlapping by 5 px).
            Vector3[] tall = WorldHudLayout.Resolve(new[]
            {
                new WorldHudRequest(new Vector3(0f, 1.75f, 0f), "N", 2.5f, 0.6f),
                new WorldHudRequest(new Vector3(4f, 1.75f, 0f), "F", 1.6f, 0.6f),
            });
            Assert.GreaterOrEqual(Mathf.Abs(tall[1].y - tall[0].y), 0.6f * 0.999f, "tall badges still overlap");

            // Without a width, exactly as before: two cells apart is two columns, untouched.
            Vector3[] narrow = WorldHudLayout.Resolve(new[]
            {
                new WorldHudRequest(new Vector3(0f, 1.75f, 0f), "A"),
                new WorldHudRequest(new Vector3(2f, 1.75f, 0f), "B"),
            });
            Assert.AreEqual(1.75f, narrow[1].y, 1e-4f);
        }
    }
}
