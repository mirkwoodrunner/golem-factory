using UnityEngine;

namespace GolemFactory.World
{
    /// <summary>
    /// Scene presence for <see cref="FreightMastRegistry"/>, following the Holder pattern every
    /// other plain-C# manager here uses. Unwired, a scene simply has no masts and a Zeppelin's
    /// <c>FreightLaunch</c> stalls naming the fact -- the same additive fork steam and the
    /// extractor cap ride.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FreightMastRegistryHolder : MonoBehaviour
    {
        public FreightMastRegistry Registry { get; } = new FreightMastRegistry();
    }
}
