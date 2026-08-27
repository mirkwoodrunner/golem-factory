using System.Collections.Generic;
using GolemFactory.Golems;
using GolemFactory.PunchCards;

namespace GolemFactory.Buildings
{
    /// <summary>
    /// How the wrecking bar asks the scene to take a golem back.
    ///
    /// <para>
    /// THE FOURTH LATE-WIRING SEAM, and the same shape as <c>IGolemRespawner</c>,
    /// <c>IBuildingRebuilder</c> and <c>IPlacedStationConfigurator</c>: the caller
    /// (<c>BuildModeController</c>) owns the tool, the cursor and the popups, and knows nothing
    /// about assembly bays, the tick clock or the Workbench. The implementor
    /// (<c>GolemConstructionStation</c>) is the component that <b>already</b> knows how to do
    /// this job, because it owns the birth sequence this is the exact inverse of. Putting the
    /// death sequence next to <c>SpawnGolem</c> is what stops the two drifting — the same
    /// argument that put respawn and construction through one <c>SpawnGolem</c> in the first
    /// place.
    /// </para>
    /// </summary>
    public interface IGolemDismantler
    {
        /// <summary>
        /// Removes <paramref name="golem"/> from the world and pays back what it is owed:
        /// its chassis cost if the player built it, plus everything it was carrying either way
        /// (<see cref="GolemDismantleRules.ComposeRefund"/>).
        ///
        /// <para>
        /// <b>Refuses rather than half-dismantling.</b> On <c>false</c> the golem is still
        /// standing, untouched, and <paramref name="refusalReason"/> is a player-facing sentence
        /// saying why — the caller shows it rather than inventing its own. The room for the
        /// payout is checked <em>before</em> anything is torn down, so "the stockpile is full"
        /// can never cost the player a golem <em>and</em> its cargo.
        /// </para>
        /// </summary>
        /// <param name="refunded">
        /// What was actually paid back, for the caller's popup. Empty on refusal, and empty for
        /// a free scene golem carrying nothing.
        /// </param>
        bool TryDismantleGolem(
            GolemEntity golem,
            out IReadOnlyList<RecipeIngredient> refunded,
            out string refusalReason);
    }
}
