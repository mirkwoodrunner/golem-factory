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
