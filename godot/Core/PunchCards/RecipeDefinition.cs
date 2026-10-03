using System;
using System.Collections.Generic;
using GolemFactory.Compat;

namespace GolemFactory.PunchCards
{
    /// <summary>
    /// One typed ingredient of a <see cref="RecipeDefinition"/>. A plain
    /// <c>[Serializable]</c> value pair, following the same shape as
    /// <see cref="Belts.ItemStack"/> and <c>BeltSegmentVisual</c>'s serialized rows rather than
    /// inventing a parallel-arrays layout that could go out of sync in the Inspector.
    /// </summary>
    [Serializable]
    public struct RecipeIngredient
    {
        // Bare-string item type, matching the project-wide convention for node/buffer/belt ids
        // (Economy/ItemType.cs holds the canonical constants).
        public string itemType;
        public int quantity;

        public RecipeIngredient(string itemType, int quantity)
        {
            this.itemType = itemType;
            this.quantity = quantity;
        }
    }

    // A crafting recipe -- docs/progression-design.md §5.2, "What multi-input assembly must
    // support": 1-4 distinct input types; per-input quantities 1-10; output quantity > 1 (R4
    // makes 2 Iron Plate, R7 makes 3 Brass, R19 makes 3 Copper Wire); exactly one optional
    // byproduct with its own quantity (R4's 1 Slag); deterministic integers throughout -- no
    // probability, no fluids, no heat, no catalysts.
    //
    // Why this is a separate ScriptableObject rather than more fields on
    // AppendageActionDefinition: the appendage asset is the *card*, shared by every golem
    // holding it, and §1.5 authors 20 crafting recipes against 5 chassis. A recipe is data the
    // card points at, so the same Assemble card can be re-pointed and so a recipe can later be
    // referenced by the Assembly Line tech tree and the Workbench roster without dragging an
    // appendage's routing ids along with it.
    //
    // ONE OPTIONAL BYPRODUCT, NOT A LIST. §5.2 says exactly one, R4 is the only recipe that has
    // one, and Push already empties the whole output stock in a single step (§2 Consequence 3)
    // so a second output costs the recipe no slots. A List<RecipeIngredient> of outputs would be
    // strictly more general and strictly harder to author and validate for a case the design
    // says will never occur.
    public sealed class RecipeDefinition
    {
        // ScriptableObject.name in Unity: the asset's file name, which the catalog,
        // the save file and the Ledger all key on. Plain data now, so it is a field.
        public string name;

        /// <summary>
        /// §2 Consequence 1: a processing program is <c>N x Haul + Assemble + Push</c>, so
        /// <c>slots = inputCount + 2</c> and the 6-slot Zeppelin is the largest chassis in the
        /// roster. A 5-input recipe is therefore not "expensive", it is unbuildable, which is
        /// why it is an authoring error rather than a tuning choice.
        /// </summary>
        public const int MaxInputs = 4;

        /// <summary>
        /// The authored per-input range §5.2 gives (1-10). Deliberately NOT enforced by
        /// <see cref="IsWellFormed()"/> -- see the comment there.
        /// </summary>
        public const int MaxAuthoredInputQuantity = 10;

        // 1-4 distinct input types, each 1-10 units, consumed atomically from the golem's INPUT
        // stock. Duplicate types are an authoring error, not a sum.
        public List<RecipeIngredient> inputs = new List<RecipeIngredient>();

        // "Deposited into the golem's OUTPUT stock when the recipe finishes."
        public string outputItemType;

        // May be greater than 1 -- R4 makes 2 Iron Plate, R7 makes 3 Brass, R19 makes 3 Copper
        // Wire.
        public int outputQuantity = 1;

        // Optional, at most one (R4's Slag is the only one in the design). Leave blank for no
        // byproduct. Deposited alongside the output at completion, and its own room in output
        // stock is checked before any input is consumed -- a backed-up byproduct slot is what
        // stalls the smelter (progression-design section 5.3c).
        public string byproductItemType;

        public int byproductQuantity = 1;

        // How long one Assemble of this recipe takes. Unlike Haul/Extract/Push this is authored,
        // not derived (progression-design section 2's cycle-time table).
        public int durationTicks = 1;

        /// <summary>True when this recipe emits a second good alongside its output.</summary>
        public bool HasByproduct =>
            !string.IsNullOrEmpty(byproductItemType) && byproductQuantity > 0;

        /// <summary>
        /// Allocation-free well-formedness check, safe to call every tick.
        /// </summary>
        public bool IsWellFormed()
        {
            string problem;
            return IsWellFormed(out problem);
        }

        /// <summary>
        /// Validation lives HERE, at the authoring edge, and never inside the tick loop as a
        /// throw. A malformed recipe is an authoring mistake, and a golem that hit one mid-run
        /// must stall <see cref="Events.StallReason.Unconfigured"/> ("not wired up") rather than
        /// half-execute or take the whole simulation down -- the same courtesy the registries
        /// extend to a null id by returning false instead of letting a dictionary throw.
        ///
        /// <paramref name="problem"/> is the authoring-facing explanation; nothing in the tick
        /// loop reads it (see the no-arg overload).
        /// </summary>
        public bool IsWellFormed(out string problem)
        {
            problem = null;

            if (inputs == null || inputs.Count == 0)
            {
                problem = "a recipe needs at least one input";
                return false;
            }

            if (inputs.Count > MaxInputs)
            {
                problem = "a recipe may have at most " + MaxInputs + " input types (slots = inputs + 2)";
                return false;
            }

            for (int i = 0; i < inputs.Count; i++)
            {
                RecipeIngredient ingredient = inputs[i];

                if (string.IsNullOrEmpty(ingredient.itemType))
                {
                    problem = "input " + i + " names no item type";
                    return false;
                }

                // A zero or negative quantity would make the recipe consume nothing and stall
                // forever with no shortfall to report, which is indistinguishable from a bug.
                //
                // The 1-10 UPPER bound §5.2 gives is deliberately NOT checked. An over-large
                // quantity is not structurally impossible the way a 5th input type is -- it
                // simply can never be satisfied, because GolemInventory.CapacityPerType is 12,
                // and the golem reports that honestly every tick as MissingItem naming the type
                // and the shortfall. That is exactly the behaviour §6 relies on to make Repeat
                // on R15 (10 Casing) impossible later, so turning it into "not wired up" would
                // hide the one number the player needs to see.
                if (ingredient.quantity < 1)
                {
                    problem = "input " + i + " (" + ingredient.itemType + ") has a quantity below 1";
                    return false;
                }

                for (int j = 0; j < i; j++)
                {
                    if (string.Equals(inputs[j].itemType, ingredient.itemType, StringComparison.Ordinal))
                    {
                        problem = "input type " + ingredient.itemType + " is listed twice";
                        return false;
                    }
                }
            }

            if (string.IsNullOrEmpty(outputItemType))
            {
                problem = "a recipe needs an output item type";
                return false;
            }

            if (outputQuantity < 1)
            {
                problem = "outputQuantity must be at least 1";
                return false;
            }

            if (!string.IsNullOrEmpty(byproductItemType))
            {
                if (byproductQuantity < 1)
                {
                    problem = "byproductQuantity must be at least 1 when a byproduct is named";
                    return false;
                }

                // Output and byproduct share one per-type slot in output stock if they are the
                // same good, so the two independent room checks in GolemEntity.BeginAssemble
                // would both pass while there was only room for one of them -- and the second
                // deposit would be silently clamped away. Cheaper to forbid than to special-case.
                if (string.Equals(byproductItemType, outputItemType, StringComparison.Ordinal))
                {
                    problem = "the byproduct repeats the output type; raise outputQuantity instead";
                    return false;
                }
            }

            return true;
        }
    }
}
