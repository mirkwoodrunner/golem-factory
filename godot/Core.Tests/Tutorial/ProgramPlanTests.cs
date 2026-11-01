using System.Collections.Generic;
using System.Linq;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.Tests.Data;
using GolemFactory.Tests.World;
using GolemFactory.Tutorial;
using GolemFactory.World;
using NUnit.Framework;

namespace GolemFactory.Tests.Tutorial
{
    /// <summary>
    /// The guide's program plans (docs/guide-chapters-design.md §3): nine steps, each naming its
    /// program once, read by the hint, the step's Done check and the kit.
    /// </summary>
    public class ProgramPlanTests
    {
        /// <summary>Each program step and the chassis its golem is.</summary>
        private static readonly Dictionary<string, string> Steps = new Dictionary<string, string>
        {
            ["program"] = "ClockworkScavenger",
            ["program-presser"] = "BrassPresser",
            ["program-coker"] = "BrassPresser",
            ["stamp"] = "ClockworkScavenger",
            ["program-smelter"] = "AetherHauler",
            ["program-carrier"] = "BrassPresser",
            ["scav3"] = "ClockworkScavenger",
            ["unloader"] = "ClockworkScavenger",
            ["program-zeppelin"] = "ZeppelinFreightLoader",
        };

        private static SandboxWorld Compose()
        {
            DefinitionSet definitions = AuthoredData.Load();
            return SandboxWorld.Compose(definitions, SandboxSetupTests.LoadReal(), PlaceableCatalogTests.LoadReal(definitions));
        }

        [Test]
        public void ExactlyTheNineProgramSteps_CarryAPlan_AndATarget()
        {
            SandboxWorld world = Compose();
            CollectionAssert.AreEquivalent(Steps.Keys,
                world.Tutorial.Steps.Where(s => s.Program != null).Select(s => s.Id));
            foreach (TutorialStep step in world.Tutorial.Steps.Where(s => s.Program != null))
            {
                Assert.IsNotNull(step.ProgramTarget, step.Id);
                Assert.IsTrue(step.Workbench, step.Id + " is done in the Workbench, so the guide stays on show there");
            }
        }

        [Test]
        public void EveryPlan_FitsItsChassis_AndNamesRealCards()
        {
            SandboxWorld world = Compose();
            foreach (TutorialStep step in world.Tutorial.Steps.Where(s => s.Program != null))
            {
                ChassisDefinition chassis = world.Definitions.Chassis[Steps[step.Id]];
                Assert.LessOrEqual(step.Program.Slots.Count, chassis.maxAppendageSlots, step.Id);
                Assert.IsNotNull(step.Program.Resolve(world.Definitions.LogicCores, world.Definitions.Appendages), step.Id + " names a card that doesn't exist");

                // Written as the kit writes it, the plan passes its own check.
                GolemProgram program = Write(world.Definitions, chassis, step.Program);
                Assert.IsTrue(step.Program.Matches(program), step.Id);
                Assert.IsTrue(step.Program.Exactly(program), step.Id);
            }
        }

        private static GolemProgram Write(DefinitionSet defs, ChassisDefinition chassis, ProgramPlan plan)
        {
            var program = new GolemProgram();
            var resolved = plan.Resolve(defs.LogicCores, defs.Appendages).Value;
            Assert.IsTrue(GolemProgramWriter.TryWrite(program, chassis, resolved.core, resolved.cards, resolved.goods, resolved.quantities));
            return program;
        }

        private static GolemProgram Program(DefinitionSet defs, string chassis, params (string card, string good)[] slots)
        {
            var program = new GolemProgram();
            program.TryAssignChassis(defs.Chassis[chassis]);
            program.logicCore = defs.LogicCores["AlwaysOnCore"];
            for (int i = 0; i < slots.Length; i++)
            {
                program.TryAddAppendage(defs.Appendages[slots[i].card]);
                if (slots[i].good != null)
                {
                    program.SetItemTypeAt(i, slots[i].good);
                }
            }
            return program;
        }

        /// <summary>Each plan's Matches is the check its step always had, lenient where that was.</summary>
        [Test]
        public void EachPlan_MatchesWhatItsStepAlwaysAccepted_AndNothingElse()
        {
            DefinitionSet d = AuthoredData.Load();
            GolemProgram scavenger = Program(d, "ClockworkScavenger", ("ExtractScrap", null), ("PushOutput", null));
            GolemProgram reversed = Program(d, "ClockworkScavenger", ("PushOutput", null), ("ExtractScrap", null));
            GolemProgram iron = Program(d, "BrassPresser", ("HaulScrap", null), ("AssembleScrapReclamation", null), ("PushOutput", null));
            GolemProgram coker = Program(d, "BrassPresser", ("ExtractScrap", null), ("AssembleCoking", null), ("PushOutput", null));
            GolemProgram smelter = Program(d, "AetherHauler", ("HaulScrap", ItemType.Coke), ("HaulScrap", ItemType.Scrap), ("AssembleIronSmelting", null), ("PushOutput", null));
            GolemProgram carrier = Program(d, "BrassPresser", ("HaulScrap", ItemType.Slag), ("HaulScrap", ItemType.Coke), ("PushOutput", null));
            GolemProgram unloader = Program(d, "ClockworkScavenger", ("HaulScrap", ItemType.CopperOre), ("PushOutput", null));
            GolemProgram zeppelin = Program(d, "ZeppelinFreightLoader", ("HaulScrap", ItemType.CopperOre), ("FreightLaunch", null));
            GolemProgram gears = Program(d, "BrassPresser", ("HaulScrap", ItemType.IronPlate), ("AssembleGearCutting", null), ("PushOutput", null));

            var all = new[] { scavenger, reversed, iron, coker, smelter, carrier, unloader, zeppelin, gears };
            AssertMatchesOnly(TutorialGuide.ScavengerPlan, all, scavenger, reversed, coker);
            AssertMatchesOnly(TutorialGuide.IronPresserPlanFor(d), all, iron);
            AssertMatchesOnly(TutorialGuide.CokerPlanFor(d), all, coker);
            AssertMatchesOnly(TutorialGuide.SmelterPlanFor(d), all, smelter);
            AssertMatchesOnly(TutorialGuide.CarrierPlanFor(Compose().Tutorial.HeapSlagPerCoke), all, carrier);
            AssertMatchesOnly(TutorialGuide.UnloaderPlan, all, unloader);
            AssertMatchesOnly(TutorialGuide.ZeppelinPlan, all, zeppelin);

            GolemProgram noCore = Program(d, "ClockworkScavenger", ("ExtractScrap", null), ("PushOutput", null));
            noCore.logicCore = null;
            Assert.IsFalse(TutorialGuide.ScavengerPlan.Matches(noCore), "a program with no trigger never runs");
        }

        private static void AssertMatchesOnly(ProgramPlan plan, GolemProgram[] all, params GolemProgram[] matching)
        {
            for (int i = 0; i < all.Length; i++)
            {
                Assert.AreEqual(matching.Contains(all[i]), plan.Matches(all[i]), $"{plan} against program {i}");
            }
        }
    }
}
