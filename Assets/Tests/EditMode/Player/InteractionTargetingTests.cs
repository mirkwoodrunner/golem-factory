using NUnit.Framework;
using UnityEngine;
using GolemFactory.Player;

namespace GolemFactory.Tests.EditMode
{
    // Pure geometry, so no scene and no Play mode -- the whole point of pulling this out of
    // PlayerInteractor, where the same logic could only be reached through
    // FindObjectsByType and a live Interact() call.
    public class InteractionTargetingTests
    {
        private static readonly Vector3[] Empty = new Vector3[0];

        [Test]
        public void SelectNearest_NoCandidates_ReturnsNone()
        {
            InteractionPick pick = InteractionTargeting.SelectNearest(Vector3.zero, Empty, Empty, Empty);

            Assert.IsFalse(pick.Exists);
            Assert.AreEqual(InteractionKind.None, pick.Kind);
            Assert.IsFalse(pick.IsInRange(100f));
        }

        [Test]
        public void SelectNearest_NullLists_ReturnsNoneWithoutThrowing()
        {
            InteractionPick pick = InteractionTargeting.SelectNearest(Vector3.zero, null, null, null);

            Assert.IsFalse(pick.Exists);
        }

        [Test]
        public void SelectNearest_PicksClosestWithinOneKind()
        {
            var nodes = new[] { new Vector3(4f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(9f, 0f, 0f) };

            InteractionPick pick = InteractionTargeting.SelectNearest(Vector3.zero, nodes, Empty, Empty);

            Assert.AreEqual(InteractionKind.Harvest, pick.Kind);
            Assert.AreEqual(1, pick.Index);
            Assert.AreEqual(1f, pick.Distance, 0.0001f);
        }

        // The bug this extraction fixed: the original inline version checked node markers
        // first and returned the first kind with anything in range, so a node at the edge of
        // range beat a station the player was standing on top of.
        [Test]
        public void SelectNearest_NearerStationBeatsFartherNode()
        {
            var nodes = new[] { new Vector3(1.4f, 0f, 0f) };
            var stations = new[] { new Vector3(0.2f, 0f, 0f) };

            InteractionPick pick = InteractionTargeting.SelectNearest(Vector3.zero, nodes, stations, Empty);

            Assert.AreEqual(InteractionKind.Construct, pick.Kind);
            Assert.AreEqual(0, pick.Index);
        }

        [Test]
        public void SelectNearest_NearerGolemBeatsFartherStationAndNode()
        {
            var nodes = new[] { new Vector3(3f, 0f, 0f) };
            var stations = new[] { new Vector3(2f, 0f, 0f) };
            var golems = new[] { new Vector3(0.5f, 0f, 0f) };

            InteractionPick pick = InteractionTargeting.SelectNearest(Vector3.zero, nodes, stations, golems);

            Assert.AreEqual(InteractionKind.Program, pick.Kind);
        }

        [Test]
        public void SelectNearest_ExactTie_PrefersHarvestThenConstruct()
        {
            var nodes = new[] { new Vector3(1f, 0f, 0f) };
            var stations = new[] { new Vector3(-1f, 0f, 0f) };
            var golems = new[] { new Vector3(0f, 1f, 0f) };

            InteractionPick pick = InteractionTargeting.SelectNearest(Vector3.zero, nodes, stations, golems);

            Assert.AreEqual(InteractionKind.Harvest, pick.Kind);

            InteractionPick withoutNodes = InteractionTargeting.SelectNearest(Vector3.zero, Empty, stations, golems);
            Assert.AreEqual(InteractionKind.Construct, withoutNodes.Kind);
        }

        [Test]
        public void SelectNearest_ReturnsCandidatesBeyondRange_SoTheCallerCanShowMoveCloser()
        {
            var nodes = new[] { new Vector3(40f, 0f, 0f) };

            InteractionPick pick = InteractionTargeting.SelectNearest(Vector3.zero, nodes, Empty, Empty);

            Assert.IsTrue(pick.Exists);
            Assert.IsFalse(pick.IsInRange(1.5f));
        }

        [Test]
        public void IsInRange_UsesInclusiveBoundary()
        {
            var nodes = new[] { new Vector3(1.5f, 0f, 0f) };

            InteractionPick pick = InteractionTargeting.SelectNearest(Vector3.zero, nodes, Empty, Empty);

            Assert.IsTrue(pick.IsInRange(1.5f));
        }

        [Test]
        public void ClassifyAffordance_Ready_WithinRange()
        {
            var pick = new InteractionPick(InteractionKind.Harvest, 0, 1.0f);

            Assert.AreEqual(InteractionAffordance.Ready, InteractionTargeting.ClassifyAffordance(pick, 1.5f));
        }

        [Test]
        public void ClassifyAffordance_OutOfRange_InsideTheApproachBand()
        {
            var pick = new InteractionPick(InteractionKind.Harvest, 0, 3f);

            Assert.AreEqual(InteractionAffordance.OutOfRange, InteractionTargeting.ClassifyAffordance(pick, 1.5f));
        }

        [Test]
        public void ClassifyAffordance_Hidden_BeyondTheApproachBand()
        {
            float beyond = 1.5f * InteractionTargeting.OutOfRangeBandMultiplier + 0.1f;
            var pick = new InteractionPick(InteractionKind.Harvest, 0, beyond);

            Assert.AreEqual(InteractionAffordance.Hidden, InteractionTargeting.ClassifyAffordance(pick, 1.5f));
        }

        [Test]
        public void ClassifyAffordance_Hidden_WhenNothingWasPicked()
        {
            Assert.AreEqual(InteractionAffordance.Hidden,
                InteractionTargeting.ClassifyAffordance(InteractionPick.None, 1.5f));
        }

        [Test]
        public void BuildPrompt_Ready_LeadsWithTheKeyAndNamesTheAction()
        {
            string prompt = InteractionTargeting.BuildPrompt(
                InteractionKind.Harvest, "Scrap", "12 left", InteractionAffordance.Ready, "E");

            StringAssert.StartsWith("[E]", prompt);
            StringAssert.Contains("Harvest", prompt);
            StringAssert.Contains("Scrap", prompt);
            StringAssert.Contains("12 left", prompt);
        }

        // Out of range the key would do nothing, so showing it would be a lie.
        [Test]
        public void BuildPrompt_OutOfRange_OmitsTheKeyAndSaysMoveCloser()
        {
            string prompt = InteractionTargeting.BuildPrompt(
                InteractionKind.Harvest, "Scrap", "12 left", InteractionAffordance.OutOfRange, "E");

            StringAssert.DoesNotContain("[E]", prompt);
            StringAssert.StartsWith("Move closer", prompt);
        }

        // "Move closer to harvest aether" -- lowercasing the whole subject made the resource
        // name read as a typo. Only the verb is lowercased.
        [Test]
        public void BuildPrompt_OutOfRange_KeepsTheTargetNamesCapitalisation()
        {
            string prompt = InteractionTargeting.BuildPrompt(
                InteractionKind.Program, "PlayerGolem-001", "Idle", InteractionAffordance.OutOfRange, "E");

            StringAssert.Contains("PlayerGolem-001", prompt);
            StringAssert.Contains("program ", prompt);
        }

        // A depleted node is in range but cannot be harvested; offering the key there told the
        // player two contradictory things at once ("[E] Harvest Aether - depleted").
        [Test]
        public void BuildPrompt_Unavailable_StatesTheFactWithoutOfferingTheKey()
        {
            string prompt = InteractionTargeting.BuildPrompt(
                InteractionKind.Harvest, "Aether", "depleted", InteractionAffordance.Unavailable, "E");

            StringAssert.DoesNotContain("[E]", prompt);
            StringAssert.DoesNotContain("Move closer", prompt);
            StringAssert.Contains("Aether", prompt);
            StringAssert.Contains("depleted", prompt);
        }

        [Test]
        public void BuildPrompt_Hidden_IsEmpty()
        {
            Assert.IsEmpty(InteractionTargeting.BuildPrompt(
                InteractionKind.Harvest, "Scrap", "12 left", InteractionAffordance.Hidden, "E"));
            Assert.IsEmpty(InteractionTargeting.BuildPrompt(
                InteractionKind.None, "Scrap", "", InteractionAffordance.Ready, "E"));
        }

        [Test]
        public void BuildPrompt_DefaultsTheKeyLabelWhenUnset()
        {
            string prompt = InteractionTargeting.BuildPrompt(
                InteractionKind.Construct, "", "", InteractionAffordance.Ready, "");

            StringAssert.StartsWith("[E]", prompt);
            StringAssert.Contains("Build Golem", prompt);
        }

        [Test]
        public void Verb_IsSpecificPerKind()
        {
            Assert.AreEqual("Harvest", InteractionTargeting.Verb(InteractionKind.Harvest));
            Assert.AreEqual("Build Golem", InteractionTargeting.Verb(InteractionKind.Construct));
            Assert.AreEqual("Program", InteractionTargeting.Verb(InteractionKind.Program));
            Assert.IsEmpty(InteractionTargeting.Verb(InteractionKind.None));
        }
    }
}
