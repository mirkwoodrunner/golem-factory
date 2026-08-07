using System;
using System.Collections.Generic;
using UnityEngine;

namespace GolemFactory.ClockTower
{
    /// <summary>
    /// One item a Clock Tower stage demands, at a SUSTAINED rate in items per minute.
    /// A plain <c>[Serializable]</c> pair, the same shape as
    /// <see cref="PunchCards.RecipeIngredient"/> rather than a parallel-arrays layout that could
    /// go out of sync in the Inspector.
    /// </summary>
    [Serializable]
    public struct StageDemand
    {
        // Bare-string item type, matching the project-wide id convention
        // (Economy/ItemType.cs holds the canonical constants).
        public string itemType;

        /// <summary>Sustained items per minute. §7's table gives 1, 2, 3, 6 and 24.</summary>
        public int ratePerMinute;

        public StageDemand(string itemType, int ratePerMinute)
        {
            this.itemType = itemType;
            this.ratePerMinute = ratePerMinute;
        }
    }

    /// <summary>
    /// One of the Clock Tower's four stages (docs/progression-design.md §7). Authored as data
    /// by <c>Editor/ProgressionAssetAuthoring</c>, exactly like the 19 recipes and the five
    /// chassis cost bundles, so a tuning pass edits a table and re-runs rather than editing code.
    ///
    /// <para>
    /// THE DEMAND LIST'S ORDER IS CONTRACTUAL. §7's multiplier is a <c>min</c> across demanded
    /// items and its stage-progress alert names one starved item, so two identically-supplied
    /// towers must pick the same one. The site walks this <c>List</c> by index and takes the
    /// FIRST line achieving the minimum -- authored order, never <c>Dictionary</c> order. That
    /// is the same rule <c>GolemEntity.BeginAssemble</c> already uses to name the first short
    /// ingredient of a recipe, and for the same reason: authored order is the only ordering two
    /// separate towers provably share.
    /// </para>
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewClockTowerStage", menuName = "Golem Factory/Clock Tower/Stage")]
    public sealed class ClockTowerStageDefinition : ScriptableObject
    {
        [Tooltip("1-4, matching progression-design section 7's stage table.")]
        public int stageNumber = 1;

        [Tooltip("The stage's name as the player reads it -- Foundation, The Movement, " +
                 "Aether Illumination, The Chronometer.")]
        public string stageName;

        [Tooltip("How long the stage takes at EXACTLY 1x supply, in seconds. At 3x it is a " +
                 "third of this. 6/8/8/10 minutes in section 7's table.")]
        public int nominalSeconds = 360;

        [Tooltip("One or more items demanded at a sustained rate. ORDER IS CONTRACTUAL -- the " +
                 "first line achieving the minimum multiplier is the one the HUD names.")]
        public List<StageDemand> demands = new List<StageDemand>();

        /// <summary>
        /// Test/authoring-friendly setter, matching the <c>Configure(...)</c> idiom used across
        /// the project instead of relying solely on Inspector-authored values.
        /// </summary>
        public void Configure(int number, string displayName, int seconds, params StageDemand[] stageDemands)
        {
            stageNumber = number;
            stageName = displayName;
            nominalSeconds = seconds;
            demands = stageDemands == null
                ? new List<StageDemand>()
                : new List<StageDemand>(stageDemands);
        }

        /// <summary>Allocation-free well-formedness check, safe to call every tick.</summary>
        public bool IsWellFormed()
        {
            string problem;
            return IsWellFormed(out problem);
        }

        /// <summary>
        /// Validation lives HERE, at the authoring edge, and never inside the tick loop as a
        /// throw -- exactly the arrangement <see cref="PunchCards.RecipeDefinition"/> uses. A
        /// malformed stage is an authoring mistake; the site simply refuses to load it, so the
        /// tower stands inert rather than taking the simulation clock down mid-run.
        /// </summary>
        public bool IsWellFormed(out string problem)
        {
            problem = null;

            if (nominalSeconds < 1)
            {
                problem = "nominalSeconds must be at least 1";
                return false;
            }

            if (demands == null || demands.Count == 0)
            {
                problem = "a stage must demand at least one item";
                return false;
            }

            for (int i = 0; i < demands.Count; i++)
            {
                StageDemand demand = demands[i];

                if (string.IsNullOrEmpty(demand.itemType))
                {
                    problem = "demand " + i + " names no item type";
                    return false;
                }

                // A zero rate would divide into nothing and would make the line unable ever to
                // gate the stage, which reads as a demand the player can ignore -- a demand that
                // does not demand is an authoring slip, not a tuning choice.
                if (demand.ratePerMinute < 1)
                {
                    problem = "demand " + i + " (" + demand.itemType + ") has a rate below 1/min";
                    return false;
                }

                for (int j = 0; j < i; j++)
                {
                    if (string.Equals(demands[j].itemType, demand.itemType, StringComparison.Ordinal))
                    {
                        problem = "item type " + demand.itemType + " is demanded twice";
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Progress units this stage needs, from §7's nominal duration. See
        /// <see cref="ClockTowerProgress.RequiredProgressUnits"/>.
        /// </summary>
        public long RequiredProgressUnits() =>
            ClockTowerProgress.RequiredProgressUnits(nominalSeconds);
    }
}
