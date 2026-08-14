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

        // --- Was this building placed during play, or authored into the scene? ---------------
        // Read only by the save system, which rebuilds the former and never the latter, exactly
        // as GolemEntity.IsRuntimeSpawned governs golems.
        //
        // Sandbox authors two buildings directly into the scene -- StarterHandCrankBench and
        // StarterConstructionStation. They come back with the scene every time it loads, so a
        // save that also rebuilt them would double them on the floor, and the duplicate would
        // occupy a cell the original already holds.
        private bool _isRuntimePlaced;

        /// <summary>
        /// True for a building the player placed with build mode (or one rebuilt from a save),
        /// false for one authored into the scene. Defaults to false, so a building nobody marks
        /// is treated as scene furniture the save system must not recreate.
        /// </summary>
        public bool IsRuntimePlaced => _isRuntimePlaced;

        /// <summary>
        /// The prefab this instance came from, which is how a save file names it. Set at
        /// placement rather than derived from <c>gameObject.name</c>, because Instantiate
        /// appends "(Clone)" and a renamed instance would stop resolving.
        /// </summary>
        public string PrefabKey { get; private set; }

        /// <summary>
        /// Marks this building as player-placed and records which prefab built it. One-way, for
        /// the same reason <c>GolemEntity.MarkRuntimeSpawned</c> is: nothing that happens to a
        /// building afterwards should be able to drop it from the player's save.
        /// </summary>
        public void MarkRuntimePlaced(string prefabKey)
        {
            _isRuntimePlaced = true;
            PrefabKey = prefabKey;
        }

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
