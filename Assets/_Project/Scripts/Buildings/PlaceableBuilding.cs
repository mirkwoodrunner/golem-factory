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

        // Which way this building points, chosen by the player with R at placement time.
        // Meaningless for a plain decorative building and harmlessly ignored by one; it exists
        // because two placeables genuinely need it -- a belt (which direction items travel) and
        // a GolemConstructionStation (which tile its golem steps out onto, and which way that
        // golem starts facing). Kept on the shared base rather than duplicated on both so
        // BuildModeController has exactly one thing to write after Instantiate.
        public GolemFactory.World.Facing Facing { get; set; } = GolemFactory.World.Facing.North;

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
