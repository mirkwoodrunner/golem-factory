using System.Collections.Generic;
using GolemFactory.PunchCards;

namespace GolemFactory.Buildings
{
    /// <summary>
    /// What dismantling a golem pays back. Pure and engine-free, in the same idiom as
    /// <c>StepDurationRules</c> and <c>ConstructionCostPolicy</c>: the station pays it out, a
    /// refusal quotes it, and both read one function rather than two copies of the arithmetic.
    ///
    /// <para>
    /// <b>A golem's cargo is refunded as well as its chassis, and that is the whole reason this
    /// file exists.</b> A dismantle is a removal, and removal in this game is
    /// <b>never punitive</b> (see <c>BuildModeController.RefundBuilding</c>'s note on the
    /// settled full-refund call). A golem stalled holding 8 Coal is exactly the golem a player
    /// wants to get rid of, so destroying the coal as the price of tidying up would put the
    /// sting back into the one case that needs it least.
    /// </para>
    ///
    /// <para>
    /// <b>The chassis and the cargo are separate arguments on purpose.</b> Only the chassis is
    /// gated on <c>GolemEntity.IsRuntimeSpawned</c> — refunding a scene-authored golem's chassis
    /// would mint goods out of the scenery, the same way refunding authored furniture would. The
    /// cargo is real goods that really exist wherever the golem came from, so it always comes
    /// back. Pass <c>null</c> for <paramref name="chassisCost"/> to express that distinction.
    /// </para>
    /// </summary>
    public static class GolemDismantleRules
    {
        /// <summary>
        /// One payout bundle from a chassis cost and the two stocks a golem carries, summing
        /// duplicate item types so a golem holding Scrap in both stocks is refunded one merged
        /// Scrap line rather than two the buffer has to be asked about separately.
        ///
        /// <para>
        /// Order is first-seen, and it is deterministic because <c>GolemInventory.Stock</c>
        /// enumerates by <c>TypesInOrder</c> rather than by dictionary order — the same reason
        /// that list exists for <c>Push</c>. Two identically-loaded golems must quote the same
        /// refund string.
        /// </para>
        /// </summary>
        public static List<RecipeIngredient> ComposeRefund(
            IReadOnlyList<RecipeIngredient> chassisCost,
            IReadOnlyList<RecipeIngredient> inputStock,
            IReadOnlyList<RecipeIngredient> outputStock)
        {
            var merged = new List<RecipeIngredient>();
            var indexByType = new Dictionary<string, int>();

            Accumulate(merged, indexByType, chassisCost);
            Accumulate(merged, indexByType, inputStock);
            Accumulate(merged, indexByType, outputStock);

            return merged;
        }

        private static void Accumulate(
            List<RecipeIngredient> merged, Dictionary<string, int> indexByType,
            IReadOnlyList<RecipeIngredient> bundle)
        {
            if (bundle == null)
            {
                return;
            }

            for (int i = 0; i < bundle.Count; i++)
            {
                RecipeIngredient line = bundle[i];
                // A zero or negative line is dropped rather than carried: it would show up in
                // the refund popup as "+0 Coal" and would make the room check ask the buffer
                // about a good the payout never touches.
                if (string.IsNullOrEmpty(line.itemType) || line.quantity <= 0)
                {
                    continue;
                }

                int existing;
                if (indexByType.TryGetValue(line.itemType, out existing))
                {
                    merged[existing] = new RecipeIngredient(
                        line.itemType, merged[existing].quantity + line.quantity);
                }
                else
                {
                    indexByType[line.itemType] = merged.Count;
                    merged.Add(line);
                }
            }
        }
    }
}
