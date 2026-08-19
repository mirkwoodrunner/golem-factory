using System.Collections.Generic;
using UnityEngine;

namespace GolemFactory.PunchCards
{
    [CreateAssetMenu(fileName = "NewChassis", menuName = "Golem Factory/Punch Cards/Chassis")]
    public sealed class ChassisDefinition : ScriptableObject
    {
        public int maxAppendageSlots = 2;
        public int tier = 1;

        /// <summary>
        /// What this chassis costs, as an item bundle (docs/progression-design.md §6, §11 item
        /// 8). Replaces the <c>scrapCost</c>/<c>brassCost</c> int pair, which could not express
        /// a single one of §6's costs from the Presser on -- the Overclocker is
        /// <c>2 Mainspring + 20 Brass + 24 Casing + 12 Gear</c>.
        ///
        /// <para>
        /// This reuses <see cref="RecipeIngredient"/> rather than declaring a second identical
        /// (itemType, quantity) pair. It is already <c>[Serializable]</c>, already renders as a
        /// list in the Inspector, already lives in this namespace, and a chassis cost and a
        /// recipe ingredient are the same thing -- an amount of a named good. A parallel
        /// <c>ItemCost</c> type would need its own authoring script branch, its own tests and
        /// its own conversion at every boundary, for a type with the same two fields.
        /// </para>
        ///
        /// <para>
        /// An empty bundle is FREE, matching <c>StorageBufferRegistry.TryWithdrawBundle</c> and
        /// the zero-cost rule the old int pair had -- every chassis authored before this field
        /// existed reads as free rather than as unbuildable.
        /// </para>
        /// </summary>
        public List<RecipeIngredient> cost = new List<RecipeIngredient>();

        /// <summary>
        /// Whether this chassis may hold a <c>Repeat</c> appendage
        /// (docs/progression-design.md §6, "The Overclocker's verb"). False everywhere but the
        /// Mainspring Overclocker.
        ///
        /// <para>
        /// A FLAG ON THE DATA, not a name comparison in code. The roster is authored as assets
        /// and every other per-chassis difference (slots, tier, cost) already lives here;
        /// keying an ability off <c>name == "Mainspring Overclocker"</c> would put one chassis's
        /// identity in a string literal that renaming the asset silently breaks.
        /// </para>
        ///
        /// <para>
        /// Defaults to FALSE, which is the safe direction: a chassis authored before this field
        /// existed reads as "may not hold Repeat", so the exclusivity is on from the first run
        /// rather than depending on a migration.
        /// </para>
        /// </summary>
        public bool allowsRepeat;

        /// <summary>
        /// Whether this chassis may hold a <c>FreightLaunch</c> appendage (§6, "The Zeppelin's
        /// verb"). False everywhere but the Zeppelin Freight Loader.
        ///
        /// <para>
        /// Same shape and same reasoning as <see cref="allowsRepeat"/>: a flag on the data
        /// rather than a name comparison, and defaulting FALSE so a chassis authored before the
        /// verb existed reads as "may not hold it".
        /// </para>
        /// </summary>
        public bool allowsFreightLaunch;

        public Sprite chassisSprite;
    }
}
