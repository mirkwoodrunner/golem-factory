using System;
using System.Collections.Generic;
using System.Linq;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.World;
using NUnit.Framework;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// docs/art-style-bible.md §4 and art-pipeline-design.md §7.4: which frame a golem shows,
    /// what file holds it, and the fallback that lets art land one chassis, facing and frame at
    /// a time.
    /// </summary>
    public class GolemFramesTests
    {
        private const string Stem = "chassis_brass_presser";
        private static readonly Facing[] Facings = { Facing.North, Facing.East, Facing.South, Facing.West };
        private static readonly GolemWorkMotion.Motion None = GolemWorkMotion.Motion.None;
        private static readonly GolemWorkMotion.Motion Stamp = GolemWorkMotion.Motion.Stamp;

        [Test]
        public void Carried_ShowsTheCarryFrame_WhateverTheMoodOrMotion([ValueSource(nameof(Facings))] Facing facing)
        {
            foreach (GolemMood mood in (GolemMood[])Enum.GetValues(typeof(GolemMood)))
            {
                Assert.AreEqual(new GolemFrameKey(GolemFrameState.Carry, 0, facing),
                    GolemFrames.FrameFor(mood, Stamp, 0.7f, carried: true, time: 0f, facing));
            }
        }

        [Test]
        public void StalledAndStarved_ShowTheStallFrame_EvenMidStroke([ValueSource(nameof(Facings))] Facing facing)
        {
            foreach (GolemMood mood in new[] { GolemMood.Stalled, GolemMood.Starved })
            {
                Assert.AreEqual(new GolemFrameKey(GolemFrameState.Stall, 0, facing),
                    GolemFrames.FrameFor(mood, Stamp, 0.7f, carried: false, time: 0f, facing));
            }
        }

        [TestCase(0f, 0)]
        [TestCase(0.61f, 0)]
        [TestCase(0.62f, 1)]
        [TestCase(0.79f, 1)]
        [TestCase(0.80f, 2)]
        [TestCase(0.99f, 2)]
        public void ASignatureStroke_ShowsWindUpImpactRecover_ByStrokePhase(float phase, int index)
        {
            foreach (Facing facing in Facings)
            {
                Assert.AreEqual(new GolemFrameKey(GolemFrameState.Work, index, facing),
                    GolemFrames.FrameFor(GolemMood.Working, Stamp, phase, carried: false, time: 0f, facing));
            }
        }

        [Test]
        public void TheImpactFrame_StartsWhereTheStampLands()
        {
            Assert.AreEqual(GolemWorkMotion.StampImpact, GolemFrames.ImpactStart);
        }

        [TestCase(GolemMood.Working)]
        [TestCase(GolemMood.Sleeping)]
        [TestCase(GolemMood.Waiting)]
        [TestCase(GolemMood.Unprogrammed)]
        [TestCase(GolemMood.Straining)]
        public void WithNoSignatureStroke_TheGolemIdles_BaseThenTheLitFrame(GolemMood mood)
        {
            foreach (Facing facing in Facings)
            {
                Assert.AreEqual(GolemFrameKey.Base(facing), GolemFrames.FrameFor(mood, None, 0f, false, 0f, facing));
                Assert.AreEqual(GolemFrameKey.Base(facing), GolemFrames.FrameFor(mood, None, 0f, false, 1.59f, facing));
                Assert.AreEqual(new GolemFrameKey(GolemFrameState.Idle, 1, facing),
                    GolemFrames.FrameFor(mood, None, 0f, false, 1.7f, facing));
                // The pulse repeats every 1.8 s.
                Assert.AreEqual(GolemFrameKey.Base(facing), GolemFrames.FrameFor(mood, None, 0f, false, 1.8f + 0.5f, facing));
                Assert.AreEqual(new GolemFrameKey(GolemFrameState.Idle, 1, facing),
                    GolemFrames.FrameFor(mood, None, 0f, false, 3.6f * 10f + 1.65f, facing));
            }
        }

        [Test]
        public void TheIdlePulsesFirstFrame_IsTheBaseKey()
        {
            GolemFrameKey idle0 = GolemFrames.FrameFor(GolemMood.Sleeping, None, 0f, false, 0f, Facing.East);
            Assert.AreEqual(GolemFrameState.Base, idle0.State);
            Assert.AreEqual(GolemFrameKey.Base(Facing.East), idle0);
            Assert.AreEqual("chassis_brass_presser_base_east.png", GolemFrames.FileName(Stem, idle0));
        }

        [Test]
        public void TheWorkFrame_FollowsStepProgress_NotTime()
        {
            // A 40-tick Assemble stamps five 8-tick strokes. Tick 5 of the first stroke is
            // 0.625 of the way through it: the impact. The time argument never moves it.
            float phase = GolemFrames.StrokePhase(Stamp, 5f, 40);
            Assert.AreEqual(0.625f, phase, 1e-5f);
            for (float time = 0f; time < 5f; time += 0.37f)
            {
                Assert.AreEqual(new GolemFrameKey(GolemFrameState.Work, 1, Facing.South),
                    GolemFrames.FrameFor(GolemMood.Working, Stamp, phase, false, time, Facing.South));
            }
            // The second stroke starts over in its wind-up.
            Assert.AreEqual(0.125f, GolemFrames.StrokePhase(Stamp, 9f, 40), 1e-5f);
            // A one-move verb (Launch) spans the whole step.
            Assert.AreEqual(0.5f, GolemFrames.StrokePhase(GolemWorkMotion.Motion.Launch, 10f, 20), 1e-5f);
            Assert.AreEqual(0f, GolemFrames.StrokePhase(Stamp, 0f, 40));
        }

        [TestCase("ClockworkScavenger", AppendageActionType.ExtractFromNode, GolemWorkMotion.Motion.Tug)]
        [TestCase("BrassPresser", AppendageActionType.Assemble, GolemWorkMotion.Motion.Stamp)]
        [TestCase("MainspringOverclocker", AppendageActionType.Assemble, GolemWorkMotion.Motion.FastStamp)]
        [TestCase("MainspringOverclocker", AppendageActionType.Repeat, GolemWorkMotion.Motion.FastStamp)]
        [TestCase("AetherHauler", AppendageActionType.Haul, GolemWorkMotion.Motion.Heave)]
        [TestCase("ZeppelinFreightLoader", AppendageActionType.FreightLaunch, GolemWorkMotion.Motion.Launch)]
        [TestCase("BrassPresser", AppendageActionType.Push, GolemWorkMotion.Motion.None)]
        [TestCase("ClockworkScavenger", AppendageActionType.Haul, GolemWorkMotion.Motion.None)]
        [TestCase("AetherHauler", AppendageActionType.Push, GolemWorkMotion.Motion.None)]
        [TestCase(null, AppendageActionType.Assemble, GolemWorkMotion.Motion.None)]
        public void WorkFrames_PlayOnlyForTheChassissSignatureVerb(string chassis, AppendageActionType action, GolemWorkMotion.Motion expected)
        {
            Assert.AreEqual(expected, GolemFrames.WorkMotionFor(chassis, action));
        }

        [Test]
        public void EveryChassisInTheData_HasASignatureVerb()
        {
            foreach (string name in AuthoredData.Load().Chassis.Values.Select(c => c.name))
            {
                Assert.AreNotEqual(GolemWorkMotion.Motion.None, GolemFrames.SignatureMotion(name), name);
            }
        }

        [Test]
        public void FileName_TheSouthBaseIsTheStem_EveryOtherFrameNamesStateAndFacing()
        {
            Assert.AreEqual("chassis_brass_presser.png", GolemFrames.FileName(Stem, GolemFrameKey.Base(Facing.South)));
            Assert.AreEqual("chassis_brass_presser_base_north.png", GolemFrames.FileName(Stem, GolemFrameKey.Base(Facing.North)));
            Assert.AreEqual("chassis_brass_presser_work_1_east.png",
                GolemFrames.FileName(Stem, new GolemFrameKey(GolemFrameState.Work, 1, Facing.East)));
            Assert.AreEqual("chassis_brass_presser_idle_1_west.png",
                GolemFrames.FileName(Stem, new GolemFrameKey(GolemFrameState.Idle, 1, Facing.West)));
            Assert.AreEqual("chassis_brass_presser_stall_south.png",
                GolemFrames.FileName(Stem, new GolemFrameKey(GolemFrameState.Stall, 0, Facing.South)));
            Assert.AreEqual("chassis_brass_presser_carry_north.png",
                GolemFrames.FileName(Stem, new GolemFrameKey(GolemFrameState.Carry, 0, Facing.North)));
        }

        [Test]
        public void AChassisHas28Files_7DistinctPerFacing()
        {
            var names = new HashSet<string>();
            foreach (Facing facing in Facings)
            {
                foreach (GolemFrameKey key in AllKeys(facing))
                {
                    names.Add(GolemFrames.FileName(Stem, key));
                }
            }
            Assert.AreEqual(28, names.Count);
            Assert.IsTrue(names.Contains("chassis_brass_presser.png"));
        }

        [Test]
        public void Stem_DropsThePngFromTheDataName()
        {
            Assert.AreEqual(Stem, GolemFrames.Stem("chassis_brass_presser.png"));
            Assert.AreEqual(Stem, GolemFrames.Stem("chassis_brass_presser"));
            Assert.IsNull(GolemFrames.Stem(null));
            Assert.IsNull(GolemFrames.Stem(""));
        }

        [Test]
        public void Resolve_WalksTheFallback_OneStepAtATime()
        {
            var key = new GolemFrameKey(GolemFrameState.Work, 1, Facing.East);
            string frame = "chassis_brass_presser_work_1_east.png";
            string south = "chassis_brass_presser_work_1_south.png";
            string baseEast = "chassis_brass_presser_base_east.png";
            string southBase = "chassis_brass_presser.png";

            Assert.AreEqual(frame, Resolve(key, frame, south, baseEast, southBase));
            Assert.AreEqual(south, Resolve(key, south, baseEast, southBase));
            Assert.AreEqual(baseEast, Resolve(key, baseEast, southBase));
            Assert.AreEqual(southBase, Resolve(key, southBase));
            Assert.AreEqual(GolemFrames.FallbackSprite, Resolve(key));
        }

        [Test]
        public void Resolve_WithTodaysArt_EveryFrameIsTheSouthBase()
        {
            // Today only the south bases exist, so the game looks exactly as it did.
            foreach (Facing facing in Facings)
            {
                foreach (GolemFrameKey key in AllKeys(facing))
                {
                    Assert.AreEqual("chassis_brass_presser.png", Resolve(key, "chassis_brass_presser.png"), key.ToString());
                }
            }
        }

        [Test]
        public void Resolve_WithNoChassis_IsTheGenericGolem()
        {
            Assert.AreEqual("golem_generic_copper.png", GolemFrames.Resolve(null, GolemFrameKey.Base(Facing.South), _ => true));
        }

        [Test]
        public void Resolve_NeverAsksForTheFallback()
        {
            var asked = new List<string>();
            GolemFrames.Resolve(Stem, GolemFrameKey.Base(Facing.West), n => { asked.Add(n); return false; });
            CollectionAssert.AreEqual(new[]
            {
                "chassis_brass_presser_base_west.png", "chassis_brass_presser.png",
                "chassis_brass_presser_base_west.png", "chassis_brass_presser.png",
            }, asked);
        }

        private static string Resolve(GolemFrameKey key, params string[] existing)
        {
            var set = new HashSet<string>(existing);
            return GolemFrames.Resolve(Stem, key, set.Contains);
        }

        private static IEnumerable<GolemFrameKey> AllKeys(Facing facing)
        {
            yield return GolemFrameKey.Base(facing);
            yield return new GolemFrameKey(GolemFrameState.Idle, 1, facing);
            for (int i = 0; i < GolemFrames.WorkFrames; i++)
            {
                yield return new GolemFrameKey(GolemFrameState.Work, i, facing);
            }
            yield return new GolemFrameKey(GolemFrameState.Stall, 0, facing);
            yield return new GolemFrameKey(GolemFrameState.Carry, 0, facing);
        }
    }
}
