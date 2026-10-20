using System.Collections.Generic;
using System.Diagnostics;
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
        /// <summary>§15.4: a step that hasn't finished in 20 minutes of game time is stuck.</summary>
        private const float StepTimeout = 20 * 60f;

        [Test]
        public void FullRunFloor_EveryChapter_FitsItsCeiling_AndNoStepSticks()
        {
            var wall = Stopwatch.StartNew();
            var player = new FloorPlayer(StepTimeout, dt: 0.1f);
            FloorPlayer.Run run = player.PlayWhile(step => step != null && step.Id != "done", trace: true);
            float stageOne = run.StepTook["stage1"];

            foreach (KeyValuePair<int, float> chapter in run.ChapterTook)
            {
                TestContext.Out.WriteLine($"chapter {chapter.Key}: {FloorPlayer.Clock(chapter.Value)} (ceiling {FloorPlayer.Clock(FloorPlayer.Ceiling(chapter.Key))})");
            }
            TestContext.Out.WriteLine($"chapters 1-10: {FloorPlayer.Clock(run.End)} of game time; stage 1 {FloorPlayer.Clock(stageOne)}; {player.CrankedSummary()}");
            TestContext.Out.WriteLine($"reference lines: {string.Join("; ", player.LinesBuilt)}");
            TestContext.Out.WriteLine(string.Join(" · ", run.StepTimes));
            TestContext.Out.WriteLine($"wall time {wall.Elapsed.TotalSeconds:0.0} s");

            Assert.AreEqual(10, run.ChapterTook.Count, "every chapter was played");
            // Stage 1 at exactly its demand takes its nominal time once the tower's one-minute
            // windows have filled; for the minute before, they are filling (ClockTowerSite), and
            // the step waits on the guide's own check besides. Measured: 6:15.
            Assert.LessOrEqual(stageOne, 360f + 60f + 1f, "stage 1 at its demand builds in its nominal time");
            foreach (KeyValuePair<int, float> chapter in run.ChapterTook)
            {
                Assert.Less(chapter.Value, FloorPlayer.Ceiling(chapter.Key), $"chapter {chapter.Key}'s floor passes its ceiling");
            }
            Assert.Less(run.End, 2 * 60 * 60f, "the full run's floor passes two hours");
        }
    }
}
