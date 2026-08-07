using System.Collections.Generic;
using UnityEngine;
using GolemFactory.PunchCards;

namespace GolemFactory.Buildings
{
    // Minimal placeholder for M1's click-to-place slice. Real building types (extractors,
    // assembly bays, etc.) will subclass or replace this once the placement flow is proven out.
    // sealed, so a distinct building's extra behavior (e.g. GolemConstructionStation) is
    // added as a sibling component on the same prefab, not a subclass.
    public sealed class PlaceableBuilding : MonoBehaviour
    {
        public const string LocalPlayerOwnerId = "LocalPlayer";

        /// <summary>
        /// What this building costs to place, as an item bundle
        /// (docs/progression-design.md §11 item 8), replacing the <c>scrapCost</c>/
        /// <c>brassCost</c> int pair. §3.1 already prices a Boiler at 30 Scrap + 10 Iron Plate
        /// and a Steam Pipe at 1 Iron Plate, and §3.3 a Floor Expansion at 60 Scrap + 30 Iron
        /// Plate -- none of which the pair could express, which is why those three costs were
        /// only recorded as constants until now.
        ///
        /// <para>
        /// Reuses <see cref="RecipeIngredient"/> for the same reason
        /// <see cref="ChassisDefinition.cost"/> does. Empty by default, so every prefab
        /// authored before this field existed (M1's placeholder, and every belt/depot prefab in
        /// Sandbox.unity) stays exactly as free to place as it always was.
        /// </para>
        /// </summary>
        [SerializeField] private List<RecipeIngredient> cost = new List<RecipeIngredient>();

        public Vector2Int Cell { get; set; }

        // Which way this building points, chosen by the player with R at placement time.
        // Meaningless for a plain decorative building and harmlessly ignored by one; it exists
        // because two placeables genuinely need it -- a belt (which direction items travel) and
        // a GolemConstructionStation (which tile its golem steps out onto, and which way that
        // golem starts facing). Kept on the shared base rather than duplicated on both so
        // BuildModeController has exactly one thing to write after Instantiate.
        public GolemFactory.World.Facing Facing { get; set; } = GolemFactory.World.Facing.North;

        public string OwnerId { get; set; } = LocalPlayerOwnerId;

        public IReadOnlyList<RecipeIngredient> Cost => cost;

        // Test/bootstrap-friendly setter, matching the Configure(...) idiom used across the
        // project (GolemEntity, BuildModeController, WorkbenchController) instead of relying
        // solely on Inspector-authored prefab values.
        public void ConfigureCost(IEnumerable<RecipeIngredient> newCost)
        {
            cost = newCost == null
                ? new List<RecipeIngredient>()
                : new List<RecipeIngredient>(newCost);
        }

        /// <summary>
        /// Convenience overload for the Scrap-and-Brass shape the tests and M1's placeables
        /// were written against. Kept because it is genuinely the common two-good case and it
        /// keeps a caller from having to build a list to say "10 Scrap"; a zero quantity is
        /// omitted rather than authored, so <c>ConfigureCost(0, 0)</c> means free.
        /// </summary>
        public void ConfigureCost(int scrapCost, int brassCost)
        {
            cost = new List<RecipeIngredient>();
            if (scrapCost > 0)
            {
                cost.Add(new RecipeIngredient(Economy.ItemType.Scrap, scrapCost));
            }

            if (brassCost > 0)
            {
                cost.Add(new RecipeIngredient(Economy.ItemType.Brass, brassCost));
            }
        }
    }
}
