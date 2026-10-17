using NUnit.Framework;
using GolemFactory.Golems;
using GolemFactory.PunchCards;

namespace GolemFactory.Tests.EditMode
{
    public class GolemWorkMotionTests
    {
        [Test]
        public void EachVerbHasItsMotion()
        {
            Assert.AreEqual(GolemWorkMotion.Motion.Stamp, GolemWorkMotion.MotionFor("BrassPresser", AppendageActionType.Assemble));
            Assert.AreEqual(GolemWorkMotion.Motion.FastStamp, GolemWorkMotion.MotionFor("MainspringOverclocker", AppendageActionType.Assemble));
            Assert.AreEqual(GolemWorkMotion.Motion.FastStamp, GolemWorkMotion.MotionFor("MainspringOverclocker", AppendageActionType.Repeat));
            Assert.AreEqual(GolemWorkMotion.Motion.Tug, GolemWorkMotion.MotionFor("ClockworkScavenger", AppendageActionType.ExtractFromNode));
            Assert.AreEqual(GolemWorkMotion.Motion.Heave, GolemWorkMotion.MotionFor("AetherHauler", AppendageActionType.Haul));
            Assert.AreEqual(GolemWorkMotion.Motion.Lunge, GolemWorkMotion.MotionFor("AetherHauler", AppendageActionType.Push));
            Assert.AreEqual(GolemWorkMotion.Motion.Launch, GolemWorkMotion.MotionFor("ZeppelinFreightLoader", AppendageActionType.FreightLaunch));
        }

        [Test]
        public void TheIdRoutedVerbsKeepTheBob()
        {
            Assert.IsFalse(GolemWorkMotion.Animates("ClockworkScavenger", AppendageActionType.Refine));
            Assert.IsFalse(GolemWorkMotion.Animates("ClockworkScavenger", AppendageActionType.LoadIntoBuffer));
        }

        [Test]
        public void EveryMotionRestsOutsideTheStep()
        {
            foreach (GolemWorkMotion.Motion motion in System.Enum.GetValues(typeof(GolemWorkMotion.Motion)))
            {
                AssertRest(GolemWorkMotion.Evaluate(motion, 0f, 24));
                AssertRest(GolemWorkMotion.Evaluate(motion, 24f, 24));
            }
        }

        [Test]
        public void TheOverclockerStampsFasterAndGlowsHottestOnImpact()
        {
            Assert.Greater(GolemWorkMotion.StrokeCount(40, GolemWorkMotion.FastStampStrokeTicks), GolemWorkMotion.StrokeCount(40));

            // 40 ticks, 8 strokes of 5.
            GolemWorkMotion.Pose landed = GolemWorkMotion.FastStamp(5f * 0.72f, 40);
            GolemWorkMotion.Pose windUp = GolemWorkMotion.FastStamp(5f * 0.5f, 40);
            Assert.IsTrue(landed.Impact);
            Assert.Less(landed.ScaleY, 0.9f);
            Assert.Greater(landed.Glow, windUp.Glow);
            Assert.Greater(windUp.Glow, 0f, "it never cools all the way mid-batch");
        }

        [Test]
        public void TheTugReachesBackThenYanksForward()
        {
            // 7 ticks: one stroke.
            GolemWorkMotion.Pose reach = GolemWorkMotion.Tug(7f * 0.45f, 7);
            GolemWorkMotion.Pose yank = GolemWorkMotion.Tug(7f * 0.66f, 7);
            Assert.Less(reach.Forward, 0f, "toward the node behind");
            Assert.Less(reach.Tilt, 0f);
            Assert.Greater(yank.Forward, 0f, "past upright");
            Assert.AreEqual(GolemWorkMotion.StrokeCount(14, GolemWorkMotion.TugStrokeTicks), 2, "one tug per seven ticks");
        }

        [Test]
        public void TheHeaveLeansBackThenSinksUnderTheLoad()
        {
            GolemWorkMotion.Pose reach = GolemWorkMotion.Heave(4f, 10);
            GolemWorkMotion.Pose loaded = GolemWorkMotion.Heave(6.9f, 10);
            Assert.Less(reach.Forward, 0f);
            Assert.Less(reach.Tilt, 0f);
            Assert.Less(loaded.ScaleY, 0.93f, "squashed by the weight");
            Assert.AreEqual(0f, loaded.Forward, 0.01f, "upright again");
        }

        [Test]
        public void TheLungeRocksBackThenThrustsForward()
        {
            GolemWorkMotion.Pose rock = GolemWorkMotion.Lunge(2.9f, 10);
            GolemWorkMotion.Pose thrust = GolemWorkMotion.Lunge(4.5f, 10);
            Assert.Less(rock.Forward, 0f);
            Assert.Greater(thrust.Forward, 0.1f);
            Assert.Greater(thrust.Tilt, 0f);
        }

        [Test]
        public void TheZeppelinCrouchesThenHopsWithAPuff()
        {
            int d = GolemFactory.Golems.GolemEntity.FreightLaunchTicks;
            GolemWorkMotion.Pose crouch = GolemWorkMotion.Launch(d * 0.65f, d);
            GolemWorkMotion.Pose takeoff = GolemWorkMotion.Launch(d * 0.72f, d);
            GolemWorkMotion.Pose apex = GolemWorkMotion.Launch(d * 0.8f, d);
            Assert.Less(crouch.ScaleY, 0.9f, "wound down");
            Assert.AreEqual(0f, crouch.Lift);
            Assert.IsTrue(takeoff.Impact, "the puff fires as it leaves the floor");
            Assert.Greater(apex.Lift, 0.25f);
            Assert.IsFalse(apex.Impact);
        }

        [Test]
        public void StrokesAreWholeSoTheLastLandsAtTheStepsEnd()
        {
            Assert.AreEqual(3, GolemWorkMotion.StrokeCount(24));
            Assert.AreEqual(5, GolemWorkMotion.StrokeCount(40));
            Assert.AreEqual(1, GolemWorkMotion.StrokeCount(3), "a short step still stamps once");
            Assert.AreEqual(1, GolemWorkMotion.StrokeCount(0));
        }

        [Test]
        public void StandsAtRestOutsideTheStep()
        {
            AssertRest(GolemWorkMotion.Stamp(0f, 24));
            AssertRest(GolemWorkMotion.Stamp(24f, 24));
            AssertRest(GolemWorkMotion.Stamp(30f, 24));
        }

        [Test]
        public void WindsUpTallThenLandsSquashed()
        {
            // 24 ticks, 3 strokes of 8: the first stroke's wind-up peaks just before 0.62 of it.
            GolemWorkMotion.Pose windUp = GolemWorkMotion.Stamp(8f * 0.6f, 24);
            Assert.Greater(windUp.ScaleY, 1f, "stretched tall");
            Assert.Greater(windUp.Lift, 0f, "lifted");

            GolemWorkMotion.Pose landed = GolemWorkMotion.Stamp(8f * 0.71f, 24);
            Assert.Less(landed.ScaleY, 0.9f, "squashed");
            Assert.Greater(landed.ScaleX, 1.05f, "spread");
            Assert.AreEqual(0f, landed.Lift, "feet on the floor");
            Assert.IsTrue(landed.Impact);
        }

        [Test]
        public void EveryStrokeEndsBackAtRest()
        {
            GolemWorkMotion.Pose end = GolemWorkMotion.Stamp(8f * 0.999f, 24);
            Assert.AreEqual(1f, end.ScaleX, 0.01f);
            Assert.AreEqual(1f, end.ScaleY, 0.01f);
            Assert.IsFalse(end.Impact);
        }

        [Test]
        public void APuffSwellsDriftsAndFadesOut()
        {
            var early = GolemWorkMotion.Puff(0.05f);
            var late = GolemWorkMotion.Puff(0.4f);
            Assert.Greater(late.scale, early.scale, "swells");
            Assert.Greater(late.drift, early.drift, "drifts outward");
            Assert.Greater(late.rise, early.rise, "rises");
            Assert.Less(late.alpha, 0.5f, "fading by the end");
            Assert.AreEqual(0f, GolemWorkMotion.Puff(GolemWorkMotion.PuffSeconds).alpha, "gone after its life");
        }

        private static void AssertRest(GolemWorkMotion.Pose pose)
        {
            Assert.AreEqual(1f, pose.ScaleX);
            Assert.AreEqual(1f, pose.ScaleY);
            Assert.AreEqual(0f, pose.Lift);
            Assert.AreEqual(0f, pose.Forward);
            Assert.AreEqual(0f, pose.Tilt);
            Assert.AreEqual(0f, pose.Glow);
            Assert.IsFalse(pose.Impact);
        }
    }
}
