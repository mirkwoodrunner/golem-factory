using UnityEngine;
using GolemFactory.Events;
using GolemFactory.Simulation;

namespace GolemFactory
{
    // Thin scene-resident owner for the plain-C# SimulationClock, mirroring how
    // GridMap is owned by GridMapHolder (see World/GridMapHolder.cs). Lives outside
    // the Simulation/ folder because that asmdef has noEngineReferences: true.
    public sealed class SimulationClockRunner : MonoBehaviour
    {
        public SimulationClock Clock { get; } = new SimulationClock();

        public void Play() => Clock.Play();

        public void Pause() => Clock.Pause();

        public void SetSpeed(float speed) => Clock.Speed = speed;

        public void Register(ITickable tickable) => Clock.Register(tickable);

        public void Unregister(ITickable tickable) => Clock.Unregister(tickable);

        private void Update()
        {
            long tickBefore = Clock.CurrentTick;
            Clock.Advance(Time.deltaTime);

            for (long tick = tickBefore + 1; tick <= Clock.CurrentTick; tick++)
            {
                EventBus.Publish(new TickAdvancedEvent(tick));
            }
        }
    }
}
