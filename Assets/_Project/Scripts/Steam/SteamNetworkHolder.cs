using UnityEngine;
using GolemFactory.Simulation;

namespace GolemFactory.Steam
{
    // Thin scene wrapper owning one SteamNetwork, per the Holder pattern (GridMapHolder,
    // ConveyorSystemHolder, BeltNetworkHolder, SpatialEndpointRegistryHolder...). All the
    // logic -- the flood fill, the assignment, the burn arithmetic -- lives in plain C# so it
    // is unit-testable without a scene; this class exists only to give it a scene presence
    // other components can reference in the Inspector, and to be registered with the clock.
    //
    // ITickable, because the burn has to happen on simulation ticks rather than in Update:
    // Coke consumption is per unit of work done, so it must follow the clock's Play/Pause and
    // speed exactly like every belt and golem does.
    public sealed class SteamNetworkHolder : MonoBehaviour, ITickable
    {
        private readonly SteamNetwork _network = new SteamNetwork();

        public SteamNetwork Network => _network;

        public void Tick(long tick) => _network.Tick(tick);
    }
}
