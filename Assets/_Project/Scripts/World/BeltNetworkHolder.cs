using UnityEngine;
using GolemFactory.Belts;

namespace GolemFactory.World
{
    // Thin scene wrapper owning one BeltNetwork, per the Holder pattern (GridMapHolder,
    // ConveyorSystemHolder, SpatialEndpointRegistryHolder...). Unlike most Holders this one
    // also has to hand its manager two collaborators, so it resolves them from sibling Holders
    // in Awake rather than making every caller do it.
    public sealed class BeltNetworkHolder : MonoBehaviour
    {
        [SerializeField] private ConveyorSystemHolder conveyorHolder;
        [SerializeField] private SpatialEndpointRegistryHolder spatialEndpointHolder;

        // How many ticks an item takes to cross one belt tile. One cell of belt is one segment,
        // so this is literally the belt's speed.
        [SerializeField] private int segmentLengthTicks = 4;

        private readonly BeltNetwork _network = new BeltNetwork();
        private bool _configured;

        public BeltNetwork Network
        {
            get
            {
                EnsureConfigured();
                return _network;
            }
        }

        private void Awake() => EnsureConfigured();

        // Lazy as well as Awake-driven: a test that adds this component and reads Network in
        // the same frame gets a wired network without needing Awake to have run.
        private void EnsureConfigured()
        {
            if (_configured)
            {
                return;
            }

            _configured = true;
            ConveyorSystem conveyor = conveyorHolder != null ? conveyorHolder.System : null;
            SpatialEndpointRegistry endpoints = spatialEndpointHolder != null ? spatialEndpointHolder.Registry : null;
            _network.Configure(conveyor, endpoints, segmentLengthTicks);
        }

        /// <summary>
        /// Programmatic wiring for tests and bootstraps, mirroring the Configure(...) idiom
        /// used across the project instead of relying solely on Inspector references.
        /// </summary>
        public void Configure(
            ConveyorSystemHolder conveyor, SpatialEndpointRegistryHolder endpoints, int lengthTicks)
        {
            conveyorHolder = conveyor;
            spatialEndpointHolder = endpoints;
            segmentLengthTicks = lengthTicks;
            _configured = false;
            EnsureConfigured();
        }
    }
}
