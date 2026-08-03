using System.Collections.Generic;

namespace GolemFactory.Simulation
{
    public enum ClockState
    {
        Paused,
        Running
    }

    // Plain C# on purpose (see GolemFactory.Simulation.asmdef's noEngineReferences):
    // this is the deterministic tick source golems run on, and it needs to be
    // EditMode-testable without a scene. A thin MonoBehaviour in
    // GolemFactory.Runtime should own an instance and call Advance() from Update().
    public sealed class SimulationClock
    {
        private readonly List<ITickable> _tickables = new List<ITickable>();
        private double _accumulator;

        public long CurrentTick { get; private set; }
        public ClockState State { get; private set; } = ClockState.Paused;
        public float TicksPerSecond { get; set; } = 10f;
        public float Speed { get; set; } = 1f;

        /// <summary>
        /// How far through the current tick the accumulator has got, in [0, 1). Read-only and
        /// purely informational -- Advance() is untouched by it. Exists so *presentation* code
        /// (Belts/BeltSegmentVisual) can interpolate between the discrete integer Progress values
        /// the simulation produces, instead of teleporting items once per tick. Nothing in the
        /// simulation may read this: doing so would make behaviour frame-rate dependent.
        /// </summary>
        public float TickFraction
        {
            get
            {
                if (TicksPerSecond <= 0f)
                {
                    return 0f;
                }

                double fraction = _accumulator * TicksPerSecond;
                if (fraction <= 0.0)
                {
                    return 0f;
                }

                return fraction >= 1.0 ? 1f : (float)fraction;
            }
        }

        public void Register(ITickable tickable) => _tickables.Add(tickable);

        public void Unregister(ITickable tickable) => _tickables.Remove(tickable);

        public void Play() => State = ClockState.Running;

        public void Pause() => State = ClockState.Paused;

        public void Advance(float deltaTimeSeconds)
        {
            if (State != ClockState.Running || TicksPerSecond <= 0f)
            {
                return;
            }

            _accumulator += deltaTimeSeconds * Speed;
            double tickDuration = 1.0 / TicksPerSecond;

            while (_accumulator >= tickDuration)
            {
                _accumulator -= tickDuration;
                CurrentTick++;

                for (int i = 0; i < _tickables.Count; i++)
                {
                    _tickables[i].Tick(CurrentTick);
                }
            }
        }
    }
}
