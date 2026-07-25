using UnityEngine;

namespace GolemFactory.Buildings
{
    // Minimal placeholder for M1's click-to-place slice. Real building types (extractors,
    // assembly bays, etc.) will subclass or replace this once the placement flow is proven out.
    // sealed, so a distinct building's extra behavior (e.g. GolemConstructionStation) is
    // added as a sibling component on the same prefab, not a subclass.
    public sealed class PlaceableBuilding : MonoBehaviour
    {
        public const string LocalPlayerOwnerId = "LocalPlayer";

        // Default 0 -- every prefab authored before these fields existed (M1's placeholder)
        // stays exactly as free to place as it always was.
        [SerializeField] private int scrapCost;
        [SerializeField] private int brassCost;

        public Vector2Int Cell { get; set; }
        public string OwnerId { get; set; } = LocalPlayerOwnerId;
        public int ScrapCost => scrapCost;
        public int BrassCost => brassCost;

        // Test/bootstrap-friendly setter, matching the Configure(...) idiom used across the
        // project (GolemEntity, BuildModeController, WorkbenchController) instead of relying
        // solely on Inspector-authored prefab values.
        public void ConfigureCost(int newScrapCost, int newBrassCost)
        {
            scrapCost = newScrapCost;
            brassCost = newBrassCost;
        }
    }
}
