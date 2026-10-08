using System.Collections.Generic;
using GolemFactory.Economy;
using GolemFactory.PunchCards;
using GolemFactory.Golems;

namespace GolemFactory.Buildings
{
    // N golem mount slots -- the spatial translation of the tabletop Assembly Bay's tableau
    // capacity (per game-design.md), and since §8 the CAP ON CONCURRENT GOLEMS: the
    // construction station refuses to build past it.
    //
    // §8's numbers, and the reasoning attached to each:
    //   * TEN slots to start, "above the natural Phase-2 count of ~8" -- the cap is meant to
    //     become interesting in the middle game rather than to bite on the player's third golem.
    //   * +6 per upgrade, for 40 Scrap + 20 Iron Plate: PRESSER-TIER GOODS ONLY, deliberately,
    //     "so the cap can never gate on something the cap itself prevents you from making".
    //     That is the load-bearing property here -- an upgrade priced in Mechanisms would be a
    //     soft-lock the moment a player filled their bays with the wrong golems.
    //
    // Still not the drafting loop itself (that is M9's Assembly Line stretch scope).
    public sealed class AssemblyBayStructure
    {
        /// <summary>§8: bays start at ten slots.</summary>
        public const int DefaultSlots = 10;

        /// <summary>§8: each upgrade is +6 slots.</summary>
        public const int SlotsPerUpgrade = 6;

        private int tier = 1;
        private int maxGolemSlots = DefaultSlots;

        // An item bundle, not the old scrapCost/brassCost int pair -- §11 item 8, and the same
        // move §1.5 made for chassis and building costs. §8 prices this in Iron Plate, which the
        // pair could not express at all.
        private List<RecipeIngredient> upgradeCost = new List<RecipeIngredient>
        {
            new RecipeIngredient(ItemType.Scrap, 40),
            new RecipeIngredient(ItemType.IronPlate, 20),
        };

        private readonly List<GolemEntity> _assignedGolems = new List<GolemEntity>();

        public int Tier => tier;
        public int MaxGolemSlots => maxGolemSlots;
        public IReadOnlyList<RecipeIngredient> UpgradeCost => upgradeCost;

        public IReadOnlyList<GolemEntity> AssignedGolems
        {
            get
            {
                PruneDestroyed();
                return _assignedGolems;
            }
        }

        /// <summary>Golems currently occupying a slot, destroyed ones already forgotten.</summary>
        public int OccupiedSlots
        {
            get
            {
                PruneDestroyed();
                return _assignedGolems.Count;
            }
        }

        /// <summary>Whether another golem could be built at all.</summary>
        public bool HasFreeSlot => OccupiedSlots < maxGolemSlots;

        /// <summary>
        /// Forgets golems that no longer exist. A destroyed golem holding a bay slot forever
        /// would turn the cap into a slow leak -- §10's own recovery route from an over-built
        /// factory is "delete golems, freeing both bay slots and upkeep instantly", so a slot
        /// that does not come back breaks the escape hatch rather than the accounting.
        ///
        /// <para>
        /// Derived from the golem rather than relying on every deletion path remembering to
        /// call <see cref="ReleaseGolem"/>: a removed golem reports
        /// <see cref="GolemEntity.IsRemoved"/>, so this cannot drift out of step with what is
        /// actually standing in the world. (In Unity the same job was done by a destroyed
        /// MonoBehaviour comparing equal to null.)
        /// </para>
        /// </summary>
        private void PruneDestroyed()
        {
            for (int i = _assignedGolems.Count - 1; i >= 0; i--)
            {
                if (_assignedGolems[i] == null || _assignedGolems[i].IsRemoved)
                {
                    _assignedGolems.RemoveAt(i);
                }
            }
        }

        public bool TryAssignGolem(GolemEntity golem)
        {
            PruneDestroyed();
            if (golem == null || _assignedGolems.Count >= maxGolemSlots || _assignedGolems.Contains(golem))
            {
                return false;
            }

            _assignedGolems.Add(golem);
            return true;
        }

        /// <summary>
        /// Puts a golem in a slot even if that takes the bay over its cap. For the LOAD path
        /// only: a save describes a factory that was legal when it was built, and refusing part
        /// of it on load would delete golems the player owns because a cap moved. The same
        /// reasoning as not re-charging a rebuilt building's cost.
        /// </summary>
        public void ForceAssignGolem(GolemEntity golem)
        {
            PruneDestroyed();
            if (golem != null && !_assignedGolems.Contains(golem))
            {
                _assignedGolems.Add(golem);
            }
        }

        public bool ReleaseGolem(GolemEntity golem) => _assignedGolems.Remove(golem);

        // Withdraws the upgrade bundle atomically, with a full refund on shortfall
        // (StorageBufferRegistry.TryWithdrawBundle) -- the same guarantee chassis construction
        // and building placement get, and for the same reason: a partial charge on a two-good
        // cost takes the Scrap and hands back nothing.
        /// <summary>A load: the saved tier and its slots, uncharged. Tier 1 is the unupgraded bay.</summary>
        public void RestoreTier(int savedTier)
        {
            if (savedTier < 1)
            {
                savedTier = 1;
            }
            maxGolemSlots += (savedTier - tier) * SlotsPerUpgrade;
            tier = savedTier;
        }

        public bool TryUpgrade(StorageBufferRegistry buffers, string resourceBufferId)
        {
            if (buffers == null || !buffers.TryWithdrawBundle(resourceBufferId, upgradeCost))
            {
                return false;
            }

            tier++;
            maxGolemSlots += SlotsPerUpgrade;
            return true;
        }
    }
}
