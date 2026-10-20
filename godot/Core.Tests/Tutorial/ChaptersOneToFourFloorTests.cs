using System.Collections.Generic;
using System.Linq;
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

        /// <summary>tuning-data-spec §14: two thirds of each chapter's budget, in seconds.</summary>
        public static readonly Dictionary<int, float> Ceilings = new Dictionary<int, float>
        {
            { 1, 3 * 60 + 20 }, { 2, 3 * 60 }, { 3, 2 * 60 + 20 }, { 4, 60 + 20 },
            { 5, 13 * 60 + 20 }, { 6, 10 * 60 }, { 7, 6 * 60 + 40 }, { 8, 30 * 60 }, { 9, 20 * 60 }, { 10, 30 * 60 },
        };

        [Test]
        public void ChaptersOneToFour_PlayedInstantly_FitWellInsideFifteenMinutesOfGameTime()
        {
            var player = new FloorPlayer(StepTimeout);
            TutorialGuide guide = player.Guide;

            var chapterStart = new SortedDictionary<int, float>();
            var stepTimes = new List<string>();
            while (guide.Current.Id != PlaytestSession.ChapterFiveStepId)
            {
                TutorialStep step = guide.Current;
                int chapter = guide.ChapterOf(guide.Index);
                Assert.LessOrEqual(chapter, 4, "the guide left chapter 4 without passing r4-card");
                if (!chapterStart.ContainsKey(chapter))
                {
                    chapterStart[chapter] = player.Now;
                }
                float entered = player.Now;
                player.Play(step.Id);
                stepTimes.Add($"{step.Id} {player.Now - entered:0.0}s");
            }
            float end = player.Now;

            var took = new Dictionary<int, float>();
            for (int c = 1; c <= 4; c++)
            {
                took[c] = (c < 4 ? chapterStart[c + 1] : end) - chapterStart[c];
                TestContext.Out.WriteLine($"chapter {c}: {FloorPlayer.Clock(took[c])} (ceiling {FloorPlayer.Clock(Ceilings[c])})");
            }
            TestContext.Out.WriteLine($"chapters 1-4: {FloorPlayer.Clock(end)} of game time; cranking {FloorPlayer.Clock(player.Cranked.Values.Sum())} "
                + $"({string.Join(", ", player.Cranked.Select(kv => $"{kv.Key} {kv.Value:0}s"))})");
            TestContext.Out.WriteLine(string.Join(" · ", stepTimes));

            string line = guide.Playtest.ChaptersOneToFour();
            TestContext.Out.WriteLine(line);
            StringAssert.EndsWith("kit used: no", line, "nothing here is the kit's fast-forward");

            // Measured 2026-10-19 before Phase 3's tuning: 7:58 (1:20, 4:15, 2:16, 0:06). After
            // it (tuning-data-spec §18): 5:16 (1:26, 2:20, 1:22, 0:06). A retune that adds minutes
            // of unavoidable grind to chapters 1-4 fails here before a playtest finds it.
            for (int c = 1; c <= 4; c++)
            {
                Assert.Less(took[c], Ceilings[c], $"chapter {c}'s floor passes its ceiling");
            }
            Assert.Less(end, 10 * 60f, "the unavoidable game time of chapters 1-4 crowds out the 15-minute target");
        }
    }
}
