using System;
using System.Collections.Generic;

namespace GolemFactory.Simulation
{
    // One-off callbacks at a future tick (e.g. "refine finishes in N ticks"),
    // separate from the per-tick ITickable registrants on SimulationClock.
    public sealed class TickScheduler : ITickable
    {
        private readonly List<(long dueTick, Action callback)> _scheduled = new List<(long, Action)>();

        public void ScheduleAfter(long currentTick, long ticksFromNow, Action callback)
        {
            _scheduled.Add((currentTick + ticksFromNow, callback));
        }

        public void Tick(long tick)
        {
            for (int i = _scheduled.Count - 1; i >= 0; i--)
            {
                if (_scheduled[i].dueTick <= tick)
                {
                    Action callback = _scheduled[i].callback;
                    _scheduled.RemoveAt(i);
                    callback();
                }
            }
        }
    }
}
