using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using GolemFactory.Economy;
using GolemFactory.PunchCards;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// §10's FIRST soft-lock row -- "Chassis cost circularity: Clear" -- pinned permanently
    /// against the authored assets rather than left as a claim §6 makes in prose.
    ///
    /// <para>
    /// §6 states it as: "No chassis requires a good that only it can produce." That is exactly
    /// the property a tuning pass breaks by accident. Pricing the Aether-Hauler in Brass, say,
    /// reads as a perfectly sensible rebalance and is an unrecoverable soft-lock, because Brass
    /// comes only from R7 (2 inputs), which needs the 4-slot Hauler, which is the thing you
    /// cannot buy. It is cheap to make impossible and expensive to discover in playtesting.
    /// </para>
    ///
    /// <para>
    /// The model: walk the roster in slot order (2/3/4/5/6). A chassis may only cost goods
    /// reachable using the chassis STRICTLY BELOW it, plus raw node goods, plus the Hand-Crank
    /// Bench -- which §4/§8 define as running 1-input Tier-1 recipes at 25 % speed and is the
    /// reason §6 can call the Presser's Iron Plate and Gears "hand-made". Reachability is a
    /// fixpoint, so a good two hand-cranked steps deep still counts.
    /// </para>
    /// </summary>
    public class ChassisCostAcyclicityTests
    {
        private const string RecipeRoot = "Assets/_Project/ScriptableObjects/Recipes";
        private const string ChassisRoot = "Assets/_Project/ScriptableObjects/Chassis";

        // §5.1 Tier 0: what a node yields and the player's own Interact harvests.
        private static readonly string[] RawGoods =
        {
            ItemType.Scrap, ItemType.Coal, ItemType.CopperOre, ItemType.ZincOre, ItemType.Aether,
        };

        /// <summary>
        /// §10: "The Hand-Crank Bench runs R1 without steam, so the player can always hand-crank
        /// coke to restart", and §8's phase table gives it "1-input Tier-1 recipes only". So the
        /// bench's capability is exactly a 3-slot chassis's: one input.
        /// </summary>
        private const int HandCrankBenchInputCapacity = 1;

        private static List<RecipeDefinition> LoadRecipes()
        {
            var recipes = new List<RecipeDefinition>();
            foreach (string guid in AssetDatabase.FindAssets("t:RecipeDefinition", new[] { RecipeRoot }))
            {
                var recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (recipe != null)
                {
                    recipes.Add(recipe);
                }
            }

            return recipes;
        }

        private static List<ChassisDefinition> LoadChassisInSlotOrder()
        {
            var chassis = new List<ChassisDefinition>();
            foreach (string guid in AssetDatabase.FindAssets("t:ChassisDefinition", new[] { ChassisRoot }))
            {
                var one = AssetDatabase.LoadAssetAtPath<ChassisDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (one != null)
                {
                    chassis.Add(one);
                }
            }

            // Slot count IS the roster order (§2 Consequence 1), tie-broken by name so the walk
            // is deterministic even if two chassis ever share a slot count.
            chassis.Sort((a, b) =>
            {
                int bySlots = a.maxAppendageSlots.CompareTo(b.maxAppendageSlots);
                return bySlots != 0 ? bySlots : string.CompareOrdinal(a.name, b.name);
            });

            return chassis;
        }

        // Everything makeable given a ceiling on how many input types one program may hold.
        // Fixpoint, because a recipe becomes runnable as soon as its inputs become makeable
        // (Gear needs Iron Plate needs Scrap -- all 1-input, all hand-crankable).
        private static HashSet<string> ProducibleWith(int maxInputTypes, List<RecipeDefinition> recipes)
        {
            var producible = new HashSet<string>(RawGoods);

            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (RecipeDefinition recipe in recipes)
                {
                    if (recipe.inputs.Count > maxInputTypes)
                    {
                        continue;
                    }

                    bool satisfiable = true;
                    foreach (RecipeIngredient input in recipe.inputs)
                    {
                        if (!producible.Contains(input.itemType))
                        {
                            satisfiable = false;
                            break;
                        }
                    }

                    if (!satisfiable)
                    {
                        continue;
                    }

                    grew |= producible.Add(recipe.outputItemType);
                    if (recipe.HasByproduct)
                    {
                        grew |= producible.Add(recipe.byproductItemType);
                    }
                }
            }

            return producible;
        }

        [Test]
        public void NoChassisRequiresAGoodOnlyItCanProduce()
        {
            List<RecipeDefinition> recipes = LoadRecipes();
            List<ChassisDefinition> roster = LoadChassisInSlotOrder();

            Assert.AreEqual(5, roster.Count, "section 6 names five chassis");

            for (int i = 0; i < roster.Count; i++)
            {
                ChassisDefinition chassis = roster[i];

                // The best program length available WITHOUT this chassis: the bench, plus every
                // cheaper chassis in the roster. slots = inputs + 2, so a chassis of N slots
                // runs recipes of up to N-2 input types.
                int reachableInputTypes = HandCrankBenchInputCapacity;
                for (int j = 0; j < i; j++)
                {
                    int inputTypes = roster[j].maxAppendageSlots - 2;
                    if (inputTypes > reachableInputTypes)
                    {
                        reachableInputTypes = inputTypes;
                    }
                }

                HashSet<string> producible = ProducibleWith(reachableInputTypes, recipes);

                Assert.IsNotNull(chassis.cost, chassis.name + " has no cost bundle");
                Assert.IsNotEmpty(chassis.cost,
                    chassis.name + " is free -- section 6 prices every chassis");

                foreach (RecipeIngredient entry in chassis.cost)
                {
                    Assert.IsTrue(producible.Contains(entry.itemType),
                        chassis.name + " costs " + entry.itemType +
                        ", which cannot be made without owning " + chassis.name +
                        " -- that is section 10's chassis-cost circularity soft-lock");
                    Assert.Greater(entry.quantity, 0,
                        chassis.name + " has a non-positive quantity of " + entry.itemType);
                }
            }
        }

        // §6's cost table, restated independently of the authoring script so the two must agree.
        // Not a duplicate of the acyclicity check: that one proves the costs are REACHABLE, this
        // one proves they are the costs the design actually specified.
        [Test]
        public void ChassisCostsMatchSection6()
        {
            var expected = new Dictionary<string, (string Item, int Quantity)[]>
            {
                { "ClockworkScavenger", new[] { (ItemType.Scrap, 12) } },
                { "BrassPresser", new[]
                    { (ItemType.Scrap, 60), (ItemType.IronPlate, 20), (ItemType.Gear, 10) } },
                { "AetherHauler", new[]
                    { (ItemType.IronPlate, 80), (ItemType.Gear, 40), (ItemType.Coke, 30) } },
                { "MainspringOverclocker", new[]
                    {
                        (ItemType.Mainspring, 2), (ItemType.Brass, 20),
                        (ItemType.Casing, 24), (ItemType.Gear, 12)
                    } },
                { "ZeppelinFreightLoader", new[]
                    {
                        (ItemType.Mainspring, 6), (ItemType.Lens, 8), (ItemType.AetherCell, 3),
                        (ItemType.Casing, 30), (ItemType.Brass, 40)
                    } },
            };

            foreach (ChassisDefinition chassis in LoadChassisInSlotOrder())
            {
                Assert.IsTrue(expected.ContainsKey(chassis.name),
                    chassis.name + " is not a chassis section 6 prices");

                (string Item, int Quantity)[] rows = expected[chassis.name];
                Assert.AreEqual(rows.Length, chassis.cost.Count, chassis.name + " cost length");

                for (int i = 0; i < rows.Length; i++)
                {
                    Assert.AreEqual(rows[i].Item, chassis.cost[i].itemType, chassis.name + " entry " + i);
                    Assert.AreEqual(rows[i].Quantity, chassis.cost[i].quantity, chassis.name + " entry " + i);
                }
            }
        }

        // §1.1 set the roster to 2/3/4/5/6 and §11 item 5 says to add nothing else. Slot count
        // is the ONLY tier gate in this design, so it is worth pinning next to the costs: a
        // tuning pass that moved a slot count would silently change which recipes exist for the
        // player, without touching a single recipe.
        [Test]
        public void SlotCountsAreStillTwoThroughSix()
        {
            List<ChassisDefinition> roster = LoadChassisInSlotOrder();
            var slots = new List<int>();
            foreach (ChassisDefinition chassis in roster)
            {
                slots.Add(chassis.maxAppendageSlots);
            }

            CollectionAssert.AreEqual(new[] { 2, 3, 4, 5, 6 }, slots);
        }
    }
}
