using UnityEngine;
using GolemFactory.Buildings;

namespace GolemFactory.Player
{
    // Thin scene-resident owner for the plain-C# ArtificerFocusMeter, mirroring
    // GridMapHolder/ConveyorSystemHolder. Regenerates on wall-clock time (Update), not
    // simulation ticks -- Focus is explicitly a separate resource from the passive
    // SimulationClock per the design doc.
    public sealed class ArtificerFocusMeterHolder : MonoBehaviour
    {
        public ArtificerFocusMeter Meter { get; } = new ArtificerFocusMeter(PlaceableBuilding.LocalPlayerOwnerId);

        private void Update()
        {
            Meter.Regen(Time.deltaTime);
        }
    }
}
