using System.Linq;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.Tutorial;
using GolemFactory.UI;
using NUnit.Framework;

namespace GolemFactory.Tests.UI
{
    /// <summary>
    /// The Workbench's next-drag hint (docs/onboarding-feedback-design.md §2.2, §7): one card and
    /// one socket at a time, by the guide's plan, and never a card placed for the player.
    /// </summary>
    public class WorkbenchHintTests
    {
        private DefinitionSet _defs;
        private WorkbenchSession _session;
        private GolemEntity _golem;

        private void Open(string chassis, ProgramPlan plan)
        {
            _defs = AuthoredData.Load();
            _golem = new GolemEntity();
            _golem.Configure("PlayerGolem-001", null);
            _golem.Program.TryAssignChassis(_defs.Chassis[chassis]);
            _session = new WorkbenchSession(6);
            _session.ConfigureRoster(_defs.Chassis.Values.ToArray(), _defs.LogicCores.Values.ToArray(), _defs.Appendages.Values.ToArray());
            _session.HintSource = g => plan;
            _session.RetargetGolem(_golem);
            _session.Open();
        }

        private void Drop(string card, WorkbenchZone zone)
        {
            if (_defs.LogicCores.TryGetValue(card, out LogicCoreDefinition core))
            {
                _session.HandleDrop(WorkbenchCardRef.FromVault(core), zone);
            }
            else
            {
                _session.HandleDrop(WorkbenchCardRef.FromVault(_defs.Appendages[card]), zone);
            }
        }

        private static WorkbenchHintTarget Card(string card, WorkbenchZone zone) =>
            new WorkbenchHintTarget(WorkbenchHintKind.VaultCard, card, zone, 0);

        [Test]
        public void AnEmptyDraft_HintsTheCoreToTrigger_ThenEachSlotInOrder_ThenTheLever()
        {
            Open("ClockworkScavenger", TutorialGuide.ScavengerPlan);
            Assert.AreEqual(Card("AlwaysOnCore", WorkbenchZone.Trigger), _session.Hint);

            Drop("AlwaysOnCore", WorkbenchZone.Trigger);
            Assert.AreEqual(Card("ExtractScrap", WorkbenchZone.Socket(0)), _session.Hint);

            Drop("ExtractScrap", WorkbenchZone.Socket(0));
            Assert.AreEqual(Card("PushOutput", WorkbenchZone.Socket(1)), _session.Hint);

            Drop("PushOutput", WorkbenchZone.Socket(1));
            Assert.AreEqual(WorkbenchHintTarget.Lever, _session.Hint);

            Assert.IsTrue(_session.Engage());
            Assert.AreEqual(WorkbenchHintTarget.None, _session.Hint, "the step's check passes on the committed program: the hint stops");
        }

        [Test]
        public void AWrongCardInASocket_HintsTheRightOneToIt()
        {
            Open("ClockworkScavenger", TutorialGuide.ScavengerPlan);
            Drop("AlwaysOnCore", WorkbenchZone.Trigger);
            Drop("HaulScrap", WorkbenchZone.Socket(0));

            Assert.AreEqual(Card("ExtractScrap", WorkbenchZone.Socket(0)), _session.Hint, "dropping replaces, so no drag-out first");

            Drop("ExtractScrap", WorkbenchZone.Socket(0));
            Assert.AreEqual("ExtractScrap", _session.DraftAppendageAt(0).name, "a drop on a filled socket replaces its card");
        }

        [Test]
        public void AGoodMismatch_HintsThePicker_AndABatchMismatch_TheDialTowardThePlan()
        {
            Open("AetherHauler", TutorialGuide.SmelterPlan);
            Drop("AlwaysOnCore", WorkbenchZone.Trigger);
            Drop("HaulScrap", WorkbenchZone.Socket(0));
            Assert.AreEqual(new WorkbenchHintTarget(WorkbenchHintKind.QuantityDial, null, WorkbenchZone.Socket(0), +1), _session.Hint,
                "Scrap is right; the batch is 1, the plan 2");

            _session.AdjustDraftQuantity(0, +4);
            Assert.AreEqual(-1, _session.Hint.Delta, "at 5 the dial goes down");
            _session.AdjustDraftQuantity(0, -3);

            Drop("HaulScrap", WorkbenchZone.Socket(1));
            Assert.AreEqual(WorkbenchHintKind.GoodPicker, _session.Hint.Kind, "the second Haul starts on Scrap; the plan wants Coke");
            Assert.AreEqual(1, _session.Hint.Zone.AppendageIndex);
        }

        [Test]
        public void TheSmeltersHauls_InEitherOrder_GoToTheLever()
        {
            Open("AetherHauler", TutorialGuide.SmelterPlan);
            Drop("AlwaysOnCore", WorkbenchZone.Trigger);
            Drop("HaulScrap", WorkbenchZone.Socket(0));
            Drop("HaulScrap", WorkbenchZone.Socket(1));
            while (_session.DraftItemTypeAt(0) != ItemType.Coke)
            {
                _session.CycleDraftItemType(0, +1);
            }
            Drop("AssembleIronSmelting", WorkbenchZone.Socket(2));
            Drop("PushOutput", WorkbenchZone.Socket(3));

            Assert.AreEqual(WorkbenchHintTarget.Lever, _session.Hint);
        }

        [Test]
        public void ACardPastTheExactPlan_IsHintedOut()
        {
            var exact = new ProgramPlan("AlwaysOnCore", null, new PlanSlot("ExtractScrap"), new PlanSlot("PushOutput"));
            Open("BrassPresser", exact);
            Drop("AlwaysOnCore", WorkbenchZone.Trigger);
            Drop("ExtractScrap", WorkbenchZone.Socket(0));
            Drop("PushOutput", WorkbenchZone.Socket(1));
            Drop("HaulScrap", WorkbenchZone.Socket(2));

            Assert.AreEqual(new WorkbenchHintTarget(WorkbenchHintKind.DragOut, null, WorkbenchZone.Socket(2), 0), _session.Hint);
        }

        [Test]
        public void ReadingTheHint_NeverChangesTheDraftOrTheGolem()
        {
            Open("ClockworkScavenger", TutorialGuide.ScavengerPlan);
            Drop("AlwaysOnCore", WorkbenchZone.Trigger);
            int version = _session.Version;

            for (int i = 0; i < 10; i++)
            {
                Assert.AreNotEqual(WorkbenchHintKind.None, _session.Hint.Kind);
            }

            Assert.AreEqual(version, _session.Version, "the hint is read, never written");
            Assert.IsNull(_session.DraftAppendageAt(0), "no card was placed for the player");
            Assert.IsNull(_golem.Program.logicCore, "nothing reached the golem");
            Assert.AreEqual(0, _golem.Program.appendages.Count);
        }

        [Test]
        public void APlanThatDoesNotFit_OrNamesACardNotOffered_HintsNothing()
        {
            Open("ClockworkScavenger", TutorialGuide.SmelterPlan);
            Assert.AreEqual(WorkbenchHintTarget.None, _session.Hint, "four slots on a two-slot Scavenger");

            Open("BrassPresser", TutorialGuide.IronPresserPlan);
            _session.ConfigureRoster(_defs.Chassis.Values.ToArray(), _defs.LogicCores.Values.ToArray(),
                _defs.Appendages.Values.Where(a => a.name != "AssembleScrapReclamation").ToArray());
            Assert.AreEqual(WorkbenchHintTarget.None, _session.Hint, "the card isn't claimed yet: the step's text says so");
        }

        [Test]
        public void WithoutAPlan_ABareGolem_GetsTrigger_ThenStepOne_ThenTheLever_AndNoCard()
        {
            Open("ClockworkScavenger", null);
            Assert.AreEqual(Card(null, WorkbenchZone.Trigger), _session.Hint);

            Drop("IntervalCore10", WorkbenchZone.Trigger);
            Assert.AreEqual(Card(null, WorkbenchZone.Socket(0)), _session.Hint);

            Drop("HaulScrap", WorkbenchZone.Socket(0));
            Assert.AreEqual(WorkbenchHintTarget.Lever, _session.Hint);

            _session.Engage();
            Assert.AreEqual(WorkbenchHintTarget.None, _session.Hint, "a programmed golem is the player's design");
        }

        [Test]
        public void TheOutlinePulses_BetweenItsBounds()
        {
            Assert.AreEqual(WorkbenchHint.PulseMaxAlpha, WorkbenchHint.PulseAlpha(0f), 1e-4);
            Assert.AreEqual(WorkbenchHint.PulseMinAlpha, WorkbenchHint.PulseAlpha(WorkbenchHint.PulsePeriodSeconds / 2f), 1e-4);
            Assert.AreEqual(WorkbenchHint.PulseMaxAlpha, WorkbenchHint.PulseAlpha(WorkbenchHint.PulsePeriodSeconds), 1e-4);
            for (float t = 0f; t < 3f; t += 0.05f)
            {
                float a = WorkbenchHint.PulseAlpha(t);
                Assert.That(a, Is.InRange(0.35f - 1e-4, 0.95f + 1e-4));
            }
        }
    }
}
