using UnityEngine;

namespace GolemFactory.AssemblyLine
{
    // Thin scene-resident owner for the plain-C# AssemblyLineState, mirroring
    // ArtificerFocusMeterHolder's ownership of ArtificerFocusMeter. Ticks on wall-clock
    // time (Update), not simulation ticks -- drafting cost decay is a real-time economy
    // concern, same reasoning as Focus's regen.
    // State is built via a field initializer, not Awake() -- Awake doesn't run outside
    // Play Mode for a plain MonoBehaviour (the same gotcha M7/the graphics-wiring pass
    // both hit), and tests need a working State immediately after AddComponent in
    // EditMode. Configure() lets a test/bootstrap pick a non-default slot count.
    public sealed class AssemblyLineStateHolder : MonoBehaviour
    {
        private const int DefaultSlotCount = 3;

        public AssemblyLineState State { get; private set; } = new AssemblyLineState(DefaultSlotCount);

        public void Configure(int slotCount)
        {
            State = new AssemblyLineState(slotCount);
        }

        private void Update()
        {
            State.Tick(Time.deltaTime);
        }
    }
}
