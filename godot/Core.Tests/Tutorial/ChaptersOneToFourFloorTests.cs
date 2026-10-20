using System.Collections.Generic;
using GolemFactory.Tutorial;
using NUnit.Framework;

namespace GolemFactory.Tests.Tutorial
{
    /// <summary>
    /// Phase 2's "Done when" (docs/roadmap.md): a new player finishes chapters 1-4 without the
    /// kit in under 15 minutes. Only a person can measure that. This measures the part no text
    /// can shorten: the GAME TIME chapters 1-4 take when a player does everything instantly
    /// (no walking, no reading, no hesitation) but must still earn every good with the player's
    /// own verbs, at their real rates, on the world's own clock (<see cref="FloorPlayer"/>).
    ///
    /// The result is a floor, not a forecast: a real player adds reading, walking and thinking.
    /// Each chapter also stays under its ceiling, two thirds of its budget in
    /// guide-chapters-design.md §1 (docs/tuning-data-spec.md §14, invariant 5).
    /// </summary>
    public class ChaptersOneToFourFloorTests
    {
        private const float StepTimeout = 600f;

        [Test]
        public void ChaptersOneToFour_PlayedInstantly_FitWellInsideFifteenMinutesOfGameTime()
        {
            var player = new FloorPlayer(StepTimeout);
            TutorialGuide guide = player.Guide;

            FloorPlayer.Run run = player.PlayWhile(step =>
            {
                if (step.Id == PlaytestSession.ChapterFiveStepId)
                {
                    return false;
                }
                Assert.LessOrEqual(guide.ChapterOf(guide.Index), 4, "the guide left chapter 4 without passing r4-card");
                return true;
            });
            foreach (KeyValuePair<int, float> chapter in run.ChapterTook)
            {
                TestContext.Out.WriteLine($"chapter {chapter.Key}: {FloorPlayer.Clock(chapter.Value)} (ceiling {FloorPlayer.Clock(FloorPlayer.Ceiling(chapter.Key))})");
            }
            TestContext.Out.WriteLine($"chapters 1-4: {FloorPlayer.Clock(run.End)} of game time; {player.CrankedSummary()}");
            TestContext.Out.WriteLine(string.Join(" · ", run.StepTimes));

            string line = guide.Playtest.ChaptersOneToFour();
            TestContext.Out.WriteLine(line);
            StringAssert.EndsWith("kit used: no", line, "nothing here is the kit's fast-forward");

            // Measured 2026-10-19 before Phase 3's tuning: 7:58 (1:20, 4:15, 2:16, 0:06). After
            // it (tuning-data-spec §18): 5:16 (1:26, 2:20, 1:22, 0:06). A retune that adds minutes
            // of unavoidable grind to chapters 1-4 fails here before a playtest finds it.
            Assert.AreEqual(4, run.ChapterTook.Count, "every chapter was played");
            foreach (KeyValuePair<int, float> chapter in run.ChapterTook)
            {
                Assert.Less(chapter.Value, FloorPlayer.Ceiling(chapter.Key), $"chapter {chapter.Key}'s floor passes its ceiling");
            }
            Assert.Less(run.End, 10 * 60f, "the unavoidable game time of chapters 1-4 crowds out the 15-minute target");
        }
    }
}
