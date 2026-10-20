using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GolemFactory.Tutorial;
using NUnit.Framework;

namespace GolemFactory.Tests.Tutorial
{
    /// <summary>
    /// docs/tuning-data-spec.md §15.4 and invariant 6: the chapters 1-4 floor carried through
    /// chapter 10. A fresh game is played to the guide's last step by the same instant player
    /// (<see cref="FloorPlayer"/>), earning every good on the world's clock; every chapter must
    /// come in under its ceiling (two thirds of its budget, §14) and no step may stick.
    ///
    /// <para>
    /// Stage 1 is the one exception: its floor is the stage's nominal time, the tower fed 6
    /// Frame Sections a minute as a line built for that rate would. Building that line is the
    /// player's design, timed only by the human run (testscript/full-playthrough.md).
    /// </para>
    /// </summary>
    public class FullRunFloorTests
    {
        /// <summary>
        /// Chapters whose measured floor passes its ceiling, in seconds: an open finding for the
        /// user (tuning-data-spec §18, correction 7), not a raised ceiling. Chapter 8 measured
        /// 35:39 against 30:00 on 2026-10-19: one Aether-Hauler per two-input good (80 Iron Plate
        /// + 40 Gear + 30 Coke each, a chapter 1-4 price §8.1 leaves alone) and the Plate and
        /// Coke lines that pay for them. The only §8.1 raise in chapter 8 is the Post's 80 Iron
        /// Plate; lowering the Mast, Zeppelin or Overclocker would not touch it.
        /// </summary>
        private static readonly Dictionary<int, float> OverCeiling = new Dictionary<int, float>
        {
            { 8, 35 * 60 + 39 },
        };

        /// <summary>§15.4: a step that hasn't finished in 20 minutes of game time is stuck.</summary>
        private const float StepTimeout = 20 * 60f;

        [Test]
        public void FullRunFloor_EveryChapter_FitsItsCeiling_AndNoStepSticks()
        {
            var wall = Stopwatch.StartNew();
            var player = new FloorPlayer(StepTimeout, dt: 0.1f);
            TutorialGuide guide = player.Guide;

            var chapterStart = new SortedDictionary<int, float>();
            var stepTimes = new List<string>();
            float stageOne = 0f;
            while (guide.Current != null && guide.Current.Id != "done")
            {
                TutorialStep step = guide.Current;
                int chapter = guide.ChapterOf(guide.Index);
                if (!chapterStart.ContainsKey(chapter))
                {
                    chapterStart[chapter] = player.Now;
                }
                float entered = player.Now;
                var spentBefore = new Dictionary<string, float>(player.Spent);
                player.Play(step.Id);
                float took = player.Now - entered;
                if (step.Id == "stage1")
                {
                    stageOne = took;
                }
                stepTimes.Add($"{step.Id} {took:0.0}s");
                TestContext.Progress.WriteLine($"  {FloorPlayer.Clock(player.Now)}  chapter {chapter}  {step.Id} {took:0.0}s");
                if (took > 60f)
                {
                    TestContext.Progress.WriteLine("      " + string.Join(", ", player.Spent.Where(kv => kv.Value - (spentBefore.TryGetValue(kv.Key, out float b) ? b : 0f) >= 1f)
                        .OrderByDescending(kv => kv.Value - (spentBefore.TryGetValue(kv.Key, out float b2) ? b2 : 0f))
                        .Select(kv => $"{kv.Key} {kv.Value - (spentBefore.TryGetValue(kv.Key, out float b3) ? b3 : 0f):0}s")));
                }
            }
            float end = player.Now;

            var tookByChapter = new Dictionary<int, float>();
            foreach (int c in chapterStart.Keys)
            {
                tookByChapter[c] = (chapterStart.ContainsKey(c + 1) ? chapterStart[c + 1] : end) - chapterStart[c];
                TestContext.Out.WriteLine($"chapter {c}: {FloorPlayer.Clock(tookByChapter[c])} (ceiling {FloorPlayer.Clock(ChaptersOneToFourFloorTests.Ceilings[c])})");
            }
            TestContext.Out.WriteLine($"chapters 1-10: {FloorPlayer.Clock(end)} of game time; stage 1 {FloorPlayer.Clock(stageOne)}; "
                + $"cranking {FloorPlayer.Clock(player.Cranked.Values.Sum())} ({string.Join(", ", player.Cranked.Select(kv => $"{kv.Key} {kv.Value:0}s"))})");
            TestContext.Out.WriteLine($"reference lines: {string.Join("; ", player.LinesBuilt)}");
            TestContext.Out.WriteLine(string.Join(" · ", stepTimes));
            TestContext.Out.WriteLine($"wall time {wall.Elapsed.TotalSeconds:0.0} s");

            Assert.AreEqual(10, tookByChapter.Count, "every chapter was played");
            // Stage 1 at exactly its demand takes its nominal time once the tower's one-minute
            // windows have filled; for the minute before, they are filling (ClockTowerSite), and
            // the step waits on the guide's own check besides. Measured: 6:15.
            Assert.LessOrEqual(stageOne, 360f + 60f + 1f, "stage 1 at its demand builds in its nominal time");
            foreach (KeyValuePair<int, float> chapter in tookByChapter)
            {
                if (OverCeiling.TryGetValue(chapter.Key, out float measured))
                {
                    // An open finding (tuning-data-spec §18): held to what was measured, so it
                    // can't quietly get worse, until the user decides what moves.
                    Assert.LessOrEqual(chapter.Value, measured + 60f,
                        $"chapter {chapter.Key}'s floor, already over its ceiling, grew past its measured {FloorPlayer.Clock(measured)}");
                    continue;
                }
                Assert.Less(chapter.Value, ChaptersOneToFourFloorTests.Ceilings[chapter.Key], $"chapter {chapter.Key}'s floor passes its ceiling");
            }
            Assert.Less(end, 2 * 60 * 60f, "the full run's floor passes two hours");
        }
    }
}
