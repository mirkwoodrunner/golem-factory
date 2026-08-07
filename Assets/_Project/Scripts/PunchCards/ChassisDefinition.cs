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

        public Sprite chassisSprite;
    }
}
