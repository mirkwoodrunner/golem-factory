using System.Collections.Generic;
using System.IO;
using System.Linq;
using GolemFactory.Compat;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.UI;
using GolemFactory.World;
using NUnit.Framework;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// docs/tuning-data-spec.md §13.1 and invariant 9: a Repeat can always finish. n + 1 runs
    /// of the Assemble before it must fit the 12-per-good hold, inputs and outputs alike, so the
    /// dial stops at the recipe's limit, the one writer refuses a Repeat that can't finish, and a
    /// save holding more loads clamped.
    /// </summary>
    public class RepeatRulesTests
    {
        // §13.1's table. MaxIterations computes from the data; this is what it must say.
        private static readonly Dictionary<string, int> Expected = new Dictionary<string, int>
        {
            { "R1_Coking", 11 }, { "R2_ScrapReclamation", 11 }, { "R3_Glassmaking", 11 },
            { "R4_IronSmelting", 5 }, { "R5_CopperSmelting", 5 }, { "R6_ZincSmelting", 5 },
            { "R8_GearCutting", 5 }, { "R12_AetherContainment", 5 },
            { "R7_BrassAlloying", 3 }, { "R10_LensGrinding", 3 }, { "R11_MainspringWinding", 3 }, { "R19_WireDrawing", 3 },
            { "R9_CasingPress", 2 },
        };

        [Test]
        public void MaxIterations_MatchesTheSpecsTable_ForEveryRecipeARepeatCanFollow()
        {
            DefinitionSet defs = AuthoredData.Load();
            foreach (KeyValuePair<string, int> row in Expected)
            {
                Assert.AreEqual(row.Value, RepeatRules.MaxIterations(defs.Recipes[row.Key]), row.Key);
            }
            foreach (RecipeDefinition recipe in defs.Recipes.Values)
            {
                int n = RepeatRules.MaxIterations(recipe);
                TestContext.Out.WriteLine($"{recipe.name}: {n}");
                foreach (RecipeIngredient input in recipe.inputs)
                {
                    Assert.LessOrEqual((n + 1) * input.quantity, GolemInventory.CapacityPerType, $"{recipe.name}: {input.itemType} for n + 1 runs");
                }
                Assert.LessOrEqual((n + 1) * recipe.outputQuantity, GolemInventory.CapacityPerType, $"{recipe.name}: its output");
            }
        }

        [Test]
        public void ThreeInputRecipes_NeverMeetARepeat_SoTheirLimitDoesNotMatter()
        {
            // 3 Hauls + Assemble + Repeat + Push is six slots; the Overclocker has five.
            DefinitionSet defs = AuthoredData.Load();
            int overclocker = defs.Chassis["MainspringOverclocker"].maxAppendageSlots;
            foreach (RecipeDefinition recipe in defs.Recipes.Values.Where(r => r.inputs.Count >= 3))
            {
                Assert.Greater(recipe.inputs.Count + 3, overclocker, recipe.name);
            }
        }

        private static (WorkbenchSession session, GolemEntity golem, DefinitionSet defs) Bench()
        {
            DefinitionSet defs = AuthoredData.Load();
            var golem = new GolemEntity();
            golem.Configure("Golem", null);
            var session = new WorkbenchSession(6);
            session.ConfigureGolem(golem);
            session.ConfigureRoster(new[] { defs.Chassis["MainspringOverclocker"] }, new[] { defs.LogicCores["AlwaysOnCore"] },
                defs.Appendages.Values.ToArray());
            session.SelectChassis(defs.Chassis["MainspringOverclocker"]);
            session.HandleDrop(WorkbenchCardRef.FromVault(defs.LogicCores["AlwaysOnCore"]), WorkbenchZone.Trigger);
            return (session, golem, defs);
        }

        [Test]
        public void TheDial_ForARepeatBehindCasingPress_StopsAtTwo()
        {
            (WorkbenchSession session, GolemEntity golem, DefinitionSet defs) = Bench();
            session.HandleDrop(WorkbenchCardRef.FromVault(defs.Appendages["HaulScrap"]), WorkbenchZone.Socket(0));
            session.HandleDrop(WorkbenchCardRef.FromVault(defs.Appendages["HaulScrap"]), WorkbenchZone.Socket(1));
            session.HandleDrop(WorkbenchCardRef.FromVault(defs.Appendages["AssembleCasingPress"]), WorkbenchZone.Socket(2));
            session.HandleDrop(WorkbenchCardRef.FromVault(defs.Appendages["RepeatAssembly"]), WorkbenchZone.Socket(3));

            Assert.AreEqual(2, session.DraftQuantityMax(3));
            session.AdjustDraftQuantity(3, +11);
            Assert.AreEqual(2, session.DraftQuantityAt(3), "the dial stops at R9's limit");
            Assert.AreEqual(WorkbenchQuantityPolicy.MaxQuantity, session.DraftQuantityMax(0), "a Haul's dial is the hold");

            Assert.IsTrue(session.Engage(), session.StatusText);
            Assert.AreEqual(2, golem.Program.GetQuantityAt(3));
        }

        private static IReadOnlyList<AppendageActionDefinition> Cards(DefinitionSet defs, params string[] names) =>
            names.Select(n => n == null ? null : defs.Appendages[n]).ToList();

        private static bool Write(GolemProgram program, DefinitionSet defs, IReadOnlyList<AppendageActionDefinition> cards,
            int[] quantities, out string refusal) =>
            GolemProgramWriter.TryWrite(program, defs.Chassis["MainspringOverclocker"], defs.LogicCores["AlwaysOnCore"],
                cards, cards.Select(_ => "").ToList(), quantities, out refusal);

        [Test]
        public void TheWriter_RefusesARepeatBehindNoAssemble_AndLeavesTheProgramAlone()
        {
            DefinitionSet defs = AuthoredData.Load();
            var golem = new GolemEntity();
            Assert.IsTrue(Write(golem.Program, defs, Cards(defs, "HaulScrap", "AssembleCoking", "PushOutput"), new[] { 1, 1, 1 }, out _));

            Assert.IsFalse(Write(golem.Program, defs, Cards(defs, "HaulScrap", "RepeatAssembly", "PushOutput"), new[] { 1, 3, 1 }, out string refusal));
            Assert.AreEqual("Repeat needs an Assemble before it", refusal);
            Assert.AreEqual(3, golem.Program.appendages.Count, "a refused write touches nothing");
            Assert.AreEqual("AssembleCoking", golem.Program.appendages[1].name);
        }

        [Test]
        public void TheWriter_RefusesARepeatBehindAnAssembleWithLimitZero()
        {
            // R15 needs 10 Casing a run: two runs can't be held. No three-input recipe fits a
            // Repeat on a real chassis, so the card list here is the writer's input alone.
            DefinitionSet defs = AuthoredData.Load();
            Assert.AreEqual(0, RepeatRules.MaxIterations(defs.Recipes["R15_FrameSection"]));
            var golem = new GolemEntity();
            Assert.IsFalse(Write(golem.Program, defs, Cards(defs, "AssembleFrameSection", "RepeatAssembly", "PushOutput"), new[] { 1, 1, 1 }, out string refusal));
            Assert.AreEqual("Repeat can't fit another run of Frame Section in a 12 hold", refusal);
        }

        [Test]
        public void TheWriter_AcceptsCokingWithRepeatEleven_AndHoldsTwelveToEleven()
        {
            DefinitionSet defs = AuthoredData.Load();
            var golem = new GolemEntity();
            IReadOnlyList<AppendageActionDefinition> cards = Cards(defs, "HaulScrap", "AssembleCoking", "RepeatAssembly", "PushOutput");
            Assert.IsTrue(Write(golem.Program, defs, cards, new[] { 12, 1, 11, 1 }, out string refusal), refusal);
            Assert.AreEqual(11, golem.Program.GetQuantityAt(2));

            Assert.IsTrue(Write(golem.Program, defs, cards, new[] { 12, 1, 12, 1 }, out refusal), refusal);
            Assert.AreEqual(11, golem.Program.GetQuantityAt(2), "written at the limit, as the dial stops");
        }

        [Test]
        public void AnOverclockerCoking_WithHaulTwelveAndRepeatEleven_MakesTwelveCokeACycle_AndNeverJams()
        {
            DefinitionSet defs = AuthoredData.Load();
            var endpoints = new SpatialEndpointRegistry();
            var coal = new StorageBuffer("Coal");
            var coke = new StorageBuffer("Coke");
            coal.Deposit(ItemType.Coal, 1000);
            endpoints.Register(new Vector2Int(0, 0), new StorageBufferEndpoint(coal));
            endpoints.Register(new Vector2Int(0, 2), new StorageBufferEndpoint(coke));

            var golem = new GolemEntity();
            golem.Configure("Overclocker", null);
            IReadOnlyList<AppendageActionDefinition> cards = Cards(defs, "HaulScrap", "AssembleCoking", "RepeatAssembly", "PushOutput");
            Assert.IsTrue(GolemProgramWriter.TryWrite(golem.Program, defs.Chassis["MainspringOverclocker"], defs.LogicCores["AlwaysOnCore"],
                cards, new[] { ItemType.Coal, "", "", "" }, new[] { 12, 1, 11, 1 }, out string refusal), refusal);
            golem.ConfigureSpatial(endpoints, new Vector2Int(0, 1), Facing.North);

            int cycles = 0;
            Events.EventBus.GolemCompleted += Count;
            try
            {
                for (long tick = 0; tick < 2000; tick++)
                {
                    golem.Tick(tick);
                    Assert.AreNotEqual(StallReason.OutputFull, golem.StallReason, $"tick {tick}");
                }
            }
            finally
            {
                Events.EventBus.GolemCompleted -= Count;
            }
            Assert.Greater(cycles, 1, "it came round");
            Assert.AreEqual(0, coke.GetQuantity(ItemType.Coke) % 12, "each Push lands a whole cycle's 12 Coke");
            Assert.AreEqual(cycles * 12, coke.GetQuantity(ItemType.Coke));

            void Count(Events.GolemCompletedEvent e)
            {
                if (e.GolemId == golem.GolemId)
                {
                    cycles++;
                }
            }
        }

        /// <summary>The real Sandbox, its pack at the top rung, and the Overclocker's price in it, as the station asks.</summary>
        private static (SandboxWorld world, ChassisDefinition overclocker) PaidForOverclocker()
        {
            SandboxWorld world = CrateWorld.Compose();
            world.PackUpgrade.Restore(world.PackUpgrade.Rungs.Count);
            ChassisDefinition overclocker = world.Definitions.Chassis["MainspringOverclocker"];
            foreach (RecipeIngredient c in overclocker.cost)
            {
                CrateWorld.Give(world, c.itemType, c.quantity);
            }
            return (world, overclocker);
        }

        [Test]
        public void AnUnclaimedOverclocker_BuiltAtTheStation_IsProgrammedInTheWorkbench_WithRepeatAtItsLimit()
        {
            // tuning-data-spec §13: the station builds any chassis unclaimed, and the Workbench
            // takes the golem's own chassis into its draft; only the cards must be owned.
            (SandboxWorld world, ChassisDefinition overclocker) = PaidForOverclocker();
            DefinitionSet definitions = world.Definitions;
            string user = world.Setup.assemblyLine.claimUserId;
            var deck = definitions.Decks[world.Setup.assemblyLine.deck];
            Assert.IsFalse(world.AssemblyLine.GetClaimedCards(user).Any(c => c.chassis == overclocker), "precondition: its card unclaimed");
            world.AssemblyLine.GrantClaim(user, deck.Cards.First(c => c.appendage?.name == "AssembleCoking"));
            world.AssemblyLine.GrantClaim(user, deck.Cards.First(c => c.appendage?.name == "RepeatAssembly"));

            Assert.IsTrue(world.StarterStation.TryConstructGolem(overclocker, out GolemEntity golem));
            WorkbenchSession bench = world.Workbench;
            bench.RetargetGolem(golem);
            Assert.AreSame(overclocker, bench.DraftChassis, "the golem's own chassis, card or no card");
            bench.HandleDrop(WorkbenchCardRef.FromVault(definitions.LogicCores["AlwaysOnCore"]), WorkbenchZone.Trigger);
            bench.HandleDrop(WorkbenchCardRef.FromVault(definitions.Appendages["HaulScrap"]), WorkbenchZone.Socket(0));
            bench.HandleDrop(WorkbenchCardRef.FromVault(definitions.Appendages["AssembleCoking"]), WorkbenchZone.Socket(1));
            bench.HandleDrop(WorkbenchCardRef.FromVault(definitions.Appendages["RepeatAssembly"]), WorkbenchZone.Socket(2));
            bench.HandleDrop(WorkbenchCardRef.FromVault(definitions.Appendages["PushOutput"]), WorkbenchZone.Socket(3));
            bench.AdjustDraftQuantity(2, +20);
            Assert.AreEqual(11, bench.DraftQuantityAt(2), "R1's limit");

            Assert.IsTrue(bench.Engage(), bench.StatusText);
            Assert.AreEqual(4, golem.Program.appendages.Count);
            Assert.AreEqual(11, golem.Program.GetQuantityAt(2));
        }

        [Test]
        public void ASaveHoldingRepeatTwelve_LoadsClampedToEleven()
        {
            (SandboxWorld world, ChassisDefinition overclocker) = PaidForOverclocker();
            DefinitionSet definitions = world.Definitions;
            Assert.IsTrue(world.StarterStation.TryConstructGolem(overclocker, out GolemEntity golem));
            IReadOnlyList<AppendageActionDefinition> cards = Cards(definitions, "HaulScrap", "AssembleCoking", "RepeatAssembly", "PushOutput");
            Assert.IsTrue(GolemProgramWriter.TryWrite(golem.Program, null, definitions.LogicCores["AlwaysOnCore"],
                cards, new[] { ItemType.Coal, "", "", "" }, new[] { 12, 1, 11, 1 }));
            golem.Program.SetQuantityAt(2, 12); // as a save from before the limit holds it

            string path = Path.Combine(Path.GetTempPath(), $"repeat-rules-{System.Guid.NewGuid():N}.json");
            try
            {
                world.SaveTo(path);
                world.LoadFrom(path);
            }
            finally
            {
                File.Delete(path);
            }

            GolemEntity loaded = world.Golems.Single(g => !g.IsRemoved && g.Program.chassis == overclocker);
            Assert.AreEqual(4, loaded.Program.appendages.Count, "the program is kept");
            Assert.AreEqual("RepeatAssembly", loaded.Program.appendages[2].name);
            Assert.AreEqual(11, loaded.Program.GetQuantityAt(2), "n clamped to R1's limit");
            Assert.AreEqual(12, loaded.Program.GetQuantityAt(0), "the Haul's batch is the player's");
        }
    }
}
