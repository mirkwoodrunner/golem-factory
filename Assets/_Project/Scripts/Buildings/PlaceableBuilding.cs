using UnityEngine;

namespace GolemFactory.Buildings
{
    // Minimal placeholder for M1's click-to-place slice. Real building types (extractors,
    // assembly bays, etc.) will subclass or replace this once the placement flow is proven out.
    public sealed class PlaceableBuilding : MonoBehaviour
    {
        public const string LocalPlayerOwnerId = "LocalPlayer";

        public Vector2Int Cell { get; set; }
        public string OwnerId { get; set; } = LocalPlayerOwnerId;
    }
}
