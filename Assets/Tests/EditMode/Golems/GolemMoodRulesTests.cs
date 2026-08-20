using NUnit.Framework;
using GolemFactory.Events;
using GolemFactory.Golems;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// Moods (docs/cozy-automation-design.md §2): six of them, three volumes. A pure
    /// classification of state the golem already exposes -- nothing here is simulation state.
    /// </summary>
    public class GolemMoodRulesTests
    {
        private static GolemMood Classify(
            GolemState state, StallReason reason = StallReason.None,
            bool runnable = true, int fullest = 0) =>
            GolemMoodRules.Classify(state, reason, runnable, fullest);

        [Test]
        public void RunningIsWorking()
        {
            Assert.AreEqual(GolemMood.Working, Classify(GolemState.Running));
        }

        [Test]
        public void IdleIsSleeping()
        {
            Assert.AreEqual(GolemMood.Sleeping, Classify(GolemState.Idle));
        }

        [Test]
        public void NoSteamIsStarved_NotJustStalled()
        {
            // The split that matters: a boiler that has gone out is a different kind of wrong
            // from a golem facing a wall, and a field of Starved badges reads as ONE fault.
            Assert.AreEqual(
                GolemMood.Starved, Classify(GolemState.Stalled, StallReason.NoSteam));
        }

        [Test]
        public void EveryOtherStallIsStalled()
        {
            foreach (StallReason reason in System.Enum.GetValues(typeof(StallReason)))
            {
                if (reason == StallReason.NoSteam)
                {
                    continue;
                }

                Assert.AreEqual(
                    GolemMood.Stalled, Classify(GolemState.Stalled, reason), reason.ToString());
            }
        }

        [Test]
        public void ARunningGolemNearItsCapIsStraining()
        {
            Assert.AreEqual(
                GolemMood.Working,
                Classify(GolemState.Running, fullest: GolemMoodRules.StrainingUnits - 1));
            Assert.AreEqual(
                GolemMood.Straining,
                Classify(GolemState.Running, fullest: GolemMoodRules.StrainingUnits));
        }

        [Test]
        public void TheStrainingThresholdSitsBelowTheCapItWarnsAbout()
        {
            // A warning that fires at the cap is not a warning, it is a stall with extra steps.
            Assert.Less(GolemMoodRules.StrainingUnits, GolemInventory.CapacityPerType);
        }

        [Test]
        public void AnIdleGolemNearItsCapIsStillSleeping()
        {
            // Straining is about a RUNNING machine filling up. An idle one is not producing, so
            // its hold is not going anywhere and there is nothing to warn about yet.
            Assert.AreEqual(
                GolemMood.Sleeping,
                Classify(GolemState.Idle, fullest: GolemInventory.CapacityPerType));
        }

        [Test]
        public void NothingToRunIsUnprogrammed_EvenWhenItHasSomehowStalled()
        {
            // Checked before the stall branch on purpose: "not wired up" is a worse sentence
            // than "no program" for something the player fixes at the Workbench.
            Assert.AreEqual(GolemMood.Unprogrammed, Classify(GolemState.Idle, runnable: false));
            Assert.AreEqual(
                GolemMood.Unprogrammed,
                Classify(GolemState.Stalled, StallReason.Unconfigured, runnable: false));
        }

        // --- Dwell -------------------------------------------------------------------------

        [Test]
        public void WorkingNeverDrawsABadge()
        {
            // Silence is a state. A factory of forty golems each wearing a "working" icon is a
            // factory nobody can read.
            Assert.IsFalse(GolemMoodRules.ShouldShowBadge(GolemMood.Working, 0f));
            Assert.IsFalse(GolemMoodRules.ShouldShowBadge(GolemMood.Working, 999f));
        }

        [Test]
        public void TheStoppedMoodsAppearImmediately()
        {
            Assert.IsTrue(GolemMoodRules.ShouldShowBadge(GolemMood.Stalled, 0f));
            Assert.IsTrue(GolemMoodRules.ShouldShowBadge(GolemMood.Starved, 0f));
            Assert.AreEqual(0f, GolemMoodRules.DwellSeconds(GolemMood.Stalled));
            Assert.AreEqual(0f, GolemMoodRules.DwellSeconds(GolemMood.Starved));
        }

        [Test]
        public void SleepingWaitsOutItsDwell()
        {
            // THE REASON DWELL EXISTS. An AlwaysOn golem is Idle for a single tick at the end of
            // every cycle; without this the badge would strobe at cycle rate.
            Assert.IsFalse(GolemMoodRules.ShouldShowBadge(GolemMood.Sleeping, 0f));
            Assert.IsFalse(
                GolemMoodRules.ShouldShowBadge(
                    GolemMood.Sleeping, GolemMoodRules.SleepingDwellSeconds - 0.01f));
            Assert.IsTrue(
                GolemMoodRules.ShouldShowBadge(
                    GolemMood.Sleeping, GolemMoodRules.SleepingDwellSeconds));
        }

        [Test]
        public void ASingleTickOfIdleIsShorterThanTheSleepDwell()
        {
            // Concretely: at the clock's 10 ticks/second an AlwaysOn cycle's Idle tick is 0.1 s,
            // and the dwell must comfortably outlast it or the strobe comes back.
            const float oneTickAtDefaultRate = 0.1f;
            Assert.Greater(GolemMoodRules.SleepingDwellSeconds, oneTickAtDefaultRate * 4f);
        }

        [Test]
        public void TheAdvisoryMoodsWaitLongerThanSleeping()
        {
            // A momentary spike to 9 units on a line that is keeping up is not news.
            Assert.Greater(
                GolemMoodRules.DwellSeconds(GolemMood.Straining),
                GolemMoodRules.DwellSeconds(GolemMood.Sleeping));
            Assert.AreEqual(
                GolemMoodRules.AdvisoryDwellSeconds, GolemMoodRules.DwellSeconds(GolemMood.Unprogrammed));
        }

        [Test]
        public void OnlyTheTwoStoppedMoodsCountAsStopped()
        {
            Assert.IsTrue(GolemMoodRules.IsStopped(GolemMood.Stalled));
            Assert.IsTrue(GolemMoodRules.IsStopped(GolemMood.Starved));
            Assert.IsFalse(GolemMoodRules.IsStopped(GolemMood.Working));
            Assert.IsFalse(GolemMoodRules.IsStopped(GolemMood.Sleeping));
            Assert.IsFalse(GolemMoodRules.IsStopped(GolemMood.Straining));
            Assert.IsFalse(GolemMoodRules.IsStopped(GolemMood.Unprogrammed));
        }

        [Test]
        public void TheStoppedMoodsCarryNoCaptionOfTheirOwn()
        {
            // They are captioned by StallDiagnostics, which names the blocking resource -- and
            // naming the resource is the whole reason that class exists.
            Assert.AreEqual("", GolemMoodRules.Caption(GolemMood.Stalled));
            Assert.AreEqual("", GolemMoodRules.Caption(GolemMood.Starved));
            Assert.AreEqual("", GolemMoodRules.Caption(GolemMood.Working));
        }

        [Test]
        public void EveryAdvisoryCaptionIsPlainAscii()
        {
            // TMP's default LiberationSans SDF atlas has no arrows and no U+26A0 -- the same
            // constraint StallDiagnostics.ComposeStripText records.
            foreach (GolemMood mood in System.Enum.GetValues(typeof(GolemMood)))
            {
                string caption = GolemMoodRules.Caption(mood);
                foreach (char c in caption)
                {
                    Assert.Less((int)c, 128, mood + " caption has a non-ASCII glyph: " + caption);
                }
            }

            Assert.AreEqual("zzz", GolemMoodRules.Caption(GolemMood.Sleeping));
        }
    }
}
