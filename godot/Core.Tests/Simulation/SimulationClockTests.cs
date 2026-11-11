using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Simulation;

namespace GolemFactory.Tests.EditMode
{
    public class SimulationClockTests
    {
        private sealed class RecordingTickable : ITickable
        {
            public List<long> Ticks { get; } = new List<long>();
            public void Tick(long tick) => Ticks.Add(tick);
        }

        [Test]
        public void Advance_WhilePaused_DoesNothing()
        {
            var clock = new SimulationClock();
            var tickable = new RecordingTickable();
            clock.Register(tickable);

            clock.Advance(10f);

            Assert.AreEqual(0, clock.CurrentTick);
            Assert.IsEmpty(tickable.Ticks);
        }

        [Test]
        public void Advance_WhileRunning_TicksAtConfiguredRate()
        {
            var clock = new SimulationClock { TicksPerSecond = 10f };
            var tickable = new RecordingTickable();
            clock.Register(tickable);
            clock.Play();

            clock.Advance(0.35f);

            Assert.AreEqual(3, clock.CurrentTick);
            CollectionAssert.AreEqual(new long[] { 1, 2, 3 }, tickable.Ticks);
        }

        [Test]
        public void Advance_CarriesLeftoverAccumulatorAcrossCalls()
        {
            var clock = new SimulationClock { TicksPerSecond = 10f };
            clock.Play();

            clock.Advance(0.09f);
            Assert.AreEqual(0, clock.CurrentTick);

            clock.Advance(0.02f);
            Assert.AreEqual(1, clock.CurrentTick);
        }

        /// <summary>
        /// A capped frame rate must not slow the game (docs/steam-deck-design.md D8.4): a minute of
        /// frames at 60, 30 or 20 fps, even at 4x where a slow frame owes more than one tick, ticks
        /// the same. The clock catches up every tick a frame owes; it never drops one.
        /// </summary>
        [TestCase(60), TestCase(30), TestCase(20)]
        public void AMinuteAtAnyFrameRate_TicksTheSame_EvenAtFourTimesSpeed(int fps)
        {
            var clock = new SimulationClock { TicksPerSecond = 10f, Speed = 4f };
            clock.Play();

            for (int frame = 0; frame < fps * 60; frame++)
            {
                clock.Advance(1f / fps);
            }

            Assert.That(clock.CurrentTick, Is.InRange(2399, 2400), "4 x 10 ticks a second for 60 s, less float rounding");
        }

        [Test]
        public void Advance_ScalesWithSpeed()
        {
            var clock = new SimulationClock { TicksPerSecond = 10f, Speed = 2f };
            clock.Play();

            clock.Advance(0.1f);

            Assert.AreEqual(2, clock.CurrentTick);
        }

        [Test]
        public void Pause_StopsFurtherTicking()
        {
            var clock = new SimulationClock { TicksPerSecond = 10f };
            clock.Play();
            clock.Advance(0.1f);
            clock.Pause();

            clock.Advance(1f);

            Assert.AreEqual(1, clock.CurrentTick);
        }

        [Test]
        public void Unregister_StopsTickableFromReceivingTicks()
        {
            var clock = new SimulationClock { TicksPerSecond = 10f };
            var tickable = new RecordingTickable();
            clock.Register(tickable);
            clock.Play();

            clock.Advance(0.1f);
            clock.Unregister(tickable);
            clock.Advance(0.1f);

            Assert.AreEqual(1, tickable.Ticks.Count);
        }

        [Test]
        public void Advance_TicksMultipleTickablesInRegistrationOrder()
        {
            var clock = new SimulationClock { TicksPerSecond = 10f };
            var order = new List<string>();
            var first = new OrderRecordingTickable("first", order);
            var second = new OrderRecordingTickable("second", order);
            clock.Register(first);
            clock.Register(second);
            clock.Play();

            clock.Advance(0.1f);

            CollectionAssert.AreEqual(new[] { "first", "second" }, order);
        }

        // TickFraction exists purely so presentation code (Belts/BeltSegmentVisual) can
        // interpolate between the integer Progress values the simulation emits. It must never
        // leave [0, 1) or the interpolated item would overshoot the tick it is heading for.
        [Test]
        public void TickFraction_IsZeroBeforeAnyTimeAccumulates()
        {
            var clock = new SimulationClock { TicksPerSecond = 10f };
            clock.Play();

            Assert.AreEqual(0f, clock.TickFraction, 0.0001f);
        }

        [Test]
        public void TickFraction_ReportsProgressThroughTheCurrentTick()
        {
            var clock = new SimulationClock { TicksPerSecond = 10f };
            clock.Play();

            clock.Advance(0.04f);

            Assert.AreEqual(0.4f, clock.TickFraction, 0.0001f);
        }

        [Test]
        public void TickFraction_WrapsBackTowardZeroOnceATickFires()
        {
            var clock = new SimulationClock { TicksPerSecond = 10f };
            clock.Play();

            clock.Advance(0.13f);

            Assert.AreEqual(1L, clock.CurrentTick);
            Assert.AreEqual(0.3f, clock.TickFraction, 0.0001f);
            Assert.Less(clock.TickFraction, 1f);
        }

        [Test]
        public void TickFraction_IsZeroWhenTicksPerSecondIsZero()
        {
            var clock = new SimulationClock { TicksPerSecond = 0f };
            clock.Play();
            clock.Advance(1f);

            Assert.AreEqual(0f, clock.TickFraction, 0.0001f);
        }

        private sealed class OrderRecordingTickable : ITickable
        {
            private readonly string _name;
            private readonly List<string> _order;

            public OrderRecordingTickable(string name, List<string> order)
            {
                _name = name;
                _order = order;
            }

            public void Tick(long tick) => _order.Add(_name);
        }
    }
}
