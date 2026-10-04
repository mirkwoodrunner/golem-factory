using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// <c>Repeat(n)</c> -- docs/progression-design.md §6, the Overclocker's verb. Re-runs the
    /// immediately preceding <c>Assemble</c> n more times from the same input stock, at n x its
    /// duration, stalling on the same shortfall rules.
    ///
    /// <para>
    /// It is the verb that replaced the cut adjacency speed aura, so the properties worth
    /// pinning are the ones that made the aura unacceptable: it is local (nothing outside this
    /// golem changes), rigid (a shortfall stalls rather than adapting) and exclusive (it costs
    /// the slot a third ingredient would have taken).
    /// </para>
    /// </summary>
    public class RepeatAppendageTests
    {

        [TearDown]
        public void TearDown()
        {
        }

        private static ChassisDefinition MakeChassis(int slots, bool allowsRepeat)
        {
            var chassis = new ChassisDefinition();
            chassis.maxAppendageSlots = slots;
            chassis.allowsRepeat = allowsRepeat;
            return chassis;
        }

        private static AppendageActionDefinition MakeCard(AppendageActionType type)
        {
            var card = new AppendageActionDefinition();
            card.actionType = type;
            return card;
        }

        private static RecipeDefinition MakeRecipe(int durationTicks)
        {
            var recipe = new RecipeDefinition();
            recipe.inputs = new List<RecipeIngredient> { new RecipeIngredient(ItemType.Scrap, 2) };
            recipe.outputItemType = ItemType.IronPlate;
            recipe.outputQuantity = 1;
            recipe.durationTicks = durationTicks;
            return recipe;
        }

        // --- The chassis gate ----------------------------------------------------------------

        [Test]
        public void OnlyAChassisThatAllowsIt_MayHoldRepeat()
        {
            var program = new GolemProgram();
            program.TryAssignChassis(MakeChassis(4, allowsRepeat: false));

            Assert.IsFalse(program.TryAddAppendage(MakeCard(AppendageActionType.Repeat)),
                "§6: the Overclocker ALONE may hold Repeat.");
            Assert.AreEqual(0, program.appendages.Count);
        }

        [Test]
        public void TheOverclocker_MayHoldRepeat()
        {
            var program = new GolemProgram();
            program.TryAssignChassis(MakeChassis(5, allowsRepeat: true));

            Assert.IsTrue(program.TryAddAppendage(MakeCard(AppendageActionType.Repeat)));
            Assert.AreEqual(1, program.appendages.Count);
        }

        [Test]
        public void RepeatCostsASlot_SoItAndAThirdIngredientAreExclusive()
        {
            // The decision §6 is built around: on a 3-slot budget, Repeat and a third Haul
            // cannot both fit. That exclusivity is what makes it a choice rather than an upgrade.
            var program = new GolemProgram();
            program.TryAssignChassis(MakeChassis(3, allowsRepeat: true));

            Assert.IsTrue(program.TryAddAppendage(MakeCard(AppendageActionType.Haul)));
            Assert.IsTrue(program.TryAddAppendage(MakeCard(AppendageActionType.Haul)));
            Assert.IsTrue(program.TryAddAppendage(MakeCard(AppendageActionType.Repeat)));

            Assert.IsFalse(program.TryAddAppendage(MakeCard(AppendageActionType.Haul)));
        }

        [Test]
        public void CanHold_AnswersForTheUiWithoutMutatingAnything()
        {
            var program = new GolemProgram();
            program.TryAssignChassis(MakeChassis(4, allowsRepeat: false));
            AppendageActionDefinition repeat = MakeCard(AppendageActionType.Repeat);

            Assert.IsFalse(program.CanHold(repeat));
            Assert.IsTrue(program.CanHold(MakeCard(AppendageActionType.Assemble)));
            Assert.AreEqual(0, program.appendages.Count);
        }

        // --- The cost quote ------------------------------------------------------------------

        [Test]
        public void RepeatCosts_NTimesTheAssemblyItRepeats()
        {
            Assert.AreEqual(56, StepDurationRules.Repeat(2, 28));
            Assert.AreEqual(28, StepDurationRules.Repeat(1, 28));
            // A recipe's own duration, not a fixed number: one Assemble card can point at a
            // 12-tick coking run or a 90-tick Chronometer Core.
            Assert.AreEqual(180, StepDurationRules.Repeat(2, 90));
        }

        [Test]
        public void RepeatTakesAQuantity_SoTheWorkbenchShowsItsDial()
        {
            AppendageActionDefinition repeat = MakeCard(AppendageActionType.Repeat);

            Assert.IsTrue(GolemFactory.UI.WorkbenchQuantityPolicy.TakesQuantity(repeat));
            Assert.IsFalse(GolemFactory.UI.WorkbenchQuantityPolicy.TakesQuantity(
                MakeCard(AppendageActionType.Push)));
        }

        // --- Execution -----------------------------------------------------------------------

        private GolemEntity BuildGolem(int repeatCount, int scrapInStock, out GolemProgram program)
        {
            var golem = new GolemEntity();

            program = golem.Program;
            program.TryAssignChassis(MakeChassis(4, allowsRepeat: true));

            AppendageActionDefinition assemble = MakeCard(AppendageActionType.Assemble);
            assemble.recipe = MakeRecipe(durationTicks: 2);
            program.TryAddAppendage(assemble);
            program.TryAddAppendage(MakeCard(AppendageActionType.Repeat));
            program.SetQuantityAt(1, repeatCount);

            var logicCore = new LogicCoreDefinition();
            logicCore.triggerType = TriggerType.AlwaysOn;
            program.logicCore = logicCore;

            golem.Inventory.AddInput(ItemType.Scrap, scrapInStock);
            return golem;
        }

        [Test]
        public void Repeat_RunsThePrecedingAssembleNMoreTimes()
        {
            // 2 iterations of a 2-input, 2-tick recipe: 3 plates in total (the Assemble itself,
            // then the two repeats), 6 Scrap consumed.
            GolemEntity golem = BuildGolem(repeatCount: 2, scrapInStock: 12, out GolemProgram program);

            for (long tick = 0; tick < 6; tick++)
            {
                golem.Tick(tick);
            }

            Assert.AreEqual(3, golem.Inventory.GetOutput(ItemType.IronPlate));
            Assert.AreEqual(6, golem.Inventory.GetInput(ItemType.Scrap));
            Assert.AreEqual(0, program.CurrentStepIndex, "The cycle should have come round.");
        }

        [Test]
        public void Repeat_TakesNTimesTheDurationRatherThanOneStep()
        {
            GolemEntity golem = BuildGolem(repeatCount: 3, scrapInStock: 12, out _);

            // 2 ticks for the Assemble, then 3 x 2 for the repeats. After 6 ticks the third
            // repeat has not finished, so only 3 of the 4 batches exist.
            for (long tick = 0; tick < 6; tick++)
            {
                golem.Tick(tick);
            }

            Assert.AreEqual(3, golem.Inventory.GetOutput(ItemType.IronPlate));

            golem.Tick(6);
            golem.Tick(7);
            Assert.AreEqual(4, golem.Inventory.GetOutput(ItemType.IronPlate));
        }

        [Test]
        public void Repeat_RunningOutOfInputMidBatch_StallsWithFinishedBatchesKept()
        {
            // The atomicity that made per-iteration execution the right shape: a repeat that
            // runs dry keeps what it made and strands nothing inside the golem.
            GolemEntity golem = BuildGolem(repeatCount: 5, scrapInStock: 5, out GolemProgram program);

            for (long tick = 0; tick < 12; tick++)
            {
                golem.Tick(tick);
            }

            Assert.AreEqual(GolemState.Stalled, program.State);
            Assert.AreEqual(StallReason.MissingItem, golem.StallReason);
            Assert.AreEqual(ItemType.Scrap, golem.StallResourceId);
            Assert.AreEqual(2, golem.Inventory.GetOutput(ItemType.IronPlate));
            Assert.AreEqual(1, golem.Inventory.GetInput(ItemType.Scrap),
                "The odd unit stays in stock -- a part-consumed withdrawal could never be undone.");
        }

        [Test]
        public void RepeatWithNothingToRepeat_StallsUnconfigured()
        {
            var golem = new GolemEntity();

            GolemProgram program = golem.Program;
            program.TryAssignChassis(MakeChassis(4, allowsRepeat: true));
            program.TryAddAppendage(MakeCard(AppendageActionType.Repeat));

            var logicCore = new LogicCoreDefinition();
            logicCore.triggerType = TriggerType.AlwaysOn;
            program.logicCore = logicCore;

            golem.Tick(0);

            Assert.AreEqual(GolemState.Stalled, program.State);
            Assert.AreEqual(StallReason.Unconfigured, golem.StallReason,
                "A Repeat with no Assemble in front of it is an unfinished program, not a " +
                "condition the world can clear.");
        }
    }
}
