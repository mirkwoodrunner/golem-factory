using GolemFactory.Simulation;
using GolemFactory.World;
using NUnit.Framework;

namespace GolemFactory.Tests.World
{
    // What the shaders are told about the clock: it follows the clock and nothing else.
    public class TickVisualsTests
    {
        private static SimulationClock Clock(float ticksPerSecond = 10f)
        {
            var clock = new SimulationClock { TicksPerSecond = ticksPerSecond };
            clock.Play();
            return clock;
        }

        [Test]
        public void ThePhase_RunsWithTheClock_AndStandsStillWhilePaused()
        {
            SimulationClock clock = Clock();
            clock.Advance(0.25f); // two ticks and half of the third
            float running = TickVisuals.TickPhase(clock);
            Assert.AreEqual(2.5f, running, 0.001f);

            clock.Pause();
            clock.Advance(5f);
            Assert.AreEqual(running, TickVisuals.TickPhase(clock), "a paused clock moves no shader");
            Assert.AreEqual(0f, TickVisuals.TickRate(clock));

            clock.Play();
            clock.Advance(0.1f);
            Assert.AreEqual(3.5f, TickVisuals.TickPhase(clock), 0.001f, "and picks up where it stopped");
        }

        [Test]
        public void ThePhase_RunsAtTheClocksSpeed()
        {
            SimulationClock one = Clock(), four = Clock();
            four.Speed = 4f;
            for (int frame = 0; frame < 30; frame++)
            {
                one.Advance(1f / 60f);
                four.Advance(1f / 60f);
            }
            Assert.AreEqual(4f * TickVisuals.TickPhase(one), TickVisuals.TickPhase(four), 0.01f);
            Assert.AreEqual(10f, TickVisuals.TickRate(one));
            Assert.AreEqual(40f, TickVisuals.TickRate(four));
        }

        [Test]
        public void ThePhase_NeverGoesBackWithinAWrap_HoweverTheFramesFall()
        {
            SimulationClock clock = Clock();
            float last = 0f;
            float[] frames = { 1f / 60f, 1f / 144f, 0.2f, 1f / 30f, 0.0001f };
            for (int i = 0; i < 400 && clock.CurrentTick < TickVisuals.PhaseWrapTicks - 8; i++)
            {
                clock.Advance(frames[i % frames.Length]);
                float phase = TickVisuals.TickPhase(clock);
                Assert.GreaterOrEqual(phase, last);
                last = phase;
            }
        }

        [Test]
        public void ThePhase_Wraps_SoAFloatKeepsItsFraction()
        {
            Assert.AreEqual(0.5f, TickVisuals.TickPhase(TickVisuals.PhaseWrapTicks, 0.5f));
            Assert.AreEqual(TickVisuals.PhaseWrapTicks - 1 + 0.25f, TickVisuals.TickPhase(TickVisuals.PhaseWrapTicks * 9L - 1, 0.25f));
            // A week of play at 4x: the fraction is still exact to a thousandth of a tick.
            long week = 7L * 24 * 3600 * 40;
            float phase = TickVisuals.TickPhase(week, 0.125f);
            Assert.AreEqual(0.125f, phase - (week % TickVisuals.PhaseWrapTicks), 0.001f);
        }

        [Test]
        public void EveryPeriodAShaderUses_DividesTheWrap()
        {
            // Or the picture jumps when the phase starts again.
            Assert.AreEqual(0, TickVisuals.PhaseWrapTicks % TickVisuals.GearTicksPerTurn);
            Assert.AreEqual(0, TickVisuals.PhaseWrapTicks % TickVisuals.ConduitPulseTicks);
            Assert.AreEqual(0, TickVisuals.PhaseWrapTicks % TickVisuals.TapeTicksPerHole);
        }

        [Test]
        public void TheBeltsPhase_IsItsCargosTravel()
        {
            // The game's belt: a 4-tick segment stepped 0.4 a tick is one cell a second at 10 ticks/s.
            const float step = 0.4f;
            const int length = 4;
            Assert.AreEqual(0f, TickVisuals.BeltPhase(0, 0f, step, length));
            Assert.AreEqual(0.5f, TickVisuals.BeltPhase(5, 0f, step, length), 0.0001f);
            Assert.AreEqual(0.55f, TickVisuals.BeltPhase(5, 0.5f, step, length), 0.0001f);
            Assert.AreEqual(0f, TickVisuals.BeltPhase(10, 0f, step, length), 0.0001f, "a whole cell on, the slats are where they began");
        }

        [Test]
        public void TheBeltsPhase_StaysInItsCell_AfterAWeek()
        {
            long week = 7L * 24 * 3600 * 40;
            for (int i = 0; i < 50; i++)
            {
                float phase = TickVisuals.BeltPhase(week + i, i / 50f, 0.4f, 4);
                Assert.GreaterOrEqual(phase, 0f);
                Assert.Less(phase, 1f);
            }
            float a = TickVisuals.BeltPhase(week, 0.25f, 0.4f, 4), b = TickVisuals.BeltPhase(week, 0.75f, 0.4f, 4);
            Assert.AreEqual(0.05f, b - a, 0.0005f, "half a tick is a twentieth of a cell, still");
        }

        [Test]
        public void ABeltThatDoesNotMove_HasNoPhase()
        {
            Assert.AreEqual(0f, TickVisuals.BeltPhase(123, 0.5f, 0f, 4));
            Assert.AreEqual(0f, TickVisuals.BeltPhase(123, 0.5f, 0.4f, 0));
            Assert.AreEqual(0f, TickVisuals.TickPhase(null));
        }

        [Test]
        public void ReducedMotion_StillsTheTickMotion()
        {
            var motion = new MotionPreference();
            Assert.IsTrue(motion.TickMotion);
            motion.Reduced = true;
            Assert.IsFalse(motion.TickMotion);
        }

        [Test]
        public void TheGridsBrackets_CloseOntoTheCell_AndRest()
        {
            Assert.AreEqual(GridOverlayRules.SnapPixels, GridOverlayRules.SnapOffset(0f));
            Assert.AreEqual(0f, GridOverlayRules.SnapOffset(GridOverlayRules.SnapSeconds));
            Assert.AreEqual(0f, GridOverlayRules.SnapOffset(-1f));
            Assert.AreEqual(0f, GridOverlayRules.SnapOffset(0f, motion: false), "Reduced motion: the brackets are simply there");
            float last = GridOverlayRules.SnapPixels;
            for (float age = 0f; age < GridOverlayRules.SnapSeconds; age += 1f / 240f)
            {
                float offset = GridOverlayRules.SnapOffset(age);
                Assert.LessOrEqual(offset, last, "they only close");
                last = offset;
            }
            Assert.Less(last, 0.5f, "no jump at the end");
        }

        [Test]
        public void TheGridsColour_BlendsToTheNewState_AndRests()
        {
            Assert.AreEqual(0f, GridOverlayRules.Blend(0f));
            Assert.AreEqual(1f, GridOverlayRules.Blend(GridOverlayRules.BlendSeconds));
            Assert.AreEqual(1f, GridOverlayRules.Blend(-1f));
            Assert.AreEqual(1f, GridOverlayRules.Blend(0f, motion: false));
            Assert.AreEqual(0.5f, GridOverlayRules.Blend(GridOverlayRules.BlendSeconds / 2f), 0.001f);
        }
    }
}
