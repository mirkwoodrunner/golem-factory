using System.Collections.Generic;
using UnityEngine;
using GolemFactory.Economy;
using GolemFactory.PunchCards;
using GolemFactory.Simulation;

namespace GolemFactory.Buildings
{
    /// <summary>
    /// The Hand-Crank Bench (docs/progression-design.md §11 item 7, §9 Phase 1): the player turns
    /// it by hand to run a one-input recipe at 25 % speed, straight into their own stockpile.
    ///
    /// <para>
    /// IT IS WHAT BREAKS THE OPENING'S CIRCULARITY, not a convenience. A production golem needs
    /// three program steps (<c>Extract</c>/<c>Haul</c> -> <c>Assemble</c> -> <c>Push</c>), so the
    /// 2-slot Clockwork Scavenger cannot produce anything -- an <c>Assemble</c> with no
    /// <c>Push</c> fills the output stock to the per-type cap and stalls with the goods sealed
    /// inside. The first chassis that can produce is the 3-slot Brass Presser, and the Presser
    /// costs 20 Iron Plate + 10 Gear, which only a producing golem could make. §9 Phase 1 resolves
    /// that by having the player hand-make all of it: "Hand-gather and hand-crank your way to the
    /// first Brass Presser". Without this bench the game is soft-locked at the first golem.
    /// </para>
    ///
    /// <para>
    /// EXPLICITLY UNPOWERED, and this class must stay that way. §10 clears the "total blackout
    /// with no golems to recover" row *only* because "the Hand-Crank Bench runs R1 without steam,
    /// so the player can always hand-crank coke to restart. <b>The bench must be explicitly
    /// unpowered.</b>" It therefore holds no <c>SteamNetworkHolder</c> and asks nothing about
    /// power, which is a property a test pins rather than a comment anyone has to remember.
    /// </para>
    ///
    /// <para>
    /// Sibling component alongside <see cref="PlaceableBuilding"/>, like PlaceableDepot,
    /// PlaceableBoiler and PlaceableClockTower -- and <c>ITickable</c>, because progress must
    /// accrue on simulation ticks rather than in <c>Update</c>: cranking is work done, so it has
    /// to follow Play/Pause and the speed multiplier exactly as every golem and belt does.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(PlaceableBuilding))]
    public sealed class HandCrankBench : MonoBehaviour, ITickable
    {
        [SerializeField] private StorageBufferRegistryHolder stockpileHolder;
        [SerializeField] private string stockpileBufferId = "FactoryStockpile";

        /// <summary>
        /// Candidate recipes, authored in R1..R19 order. Filtered to the crankable ones at
        /// Awake, so the asset list can simply be "all recipes" and
        /// <see cref="HandCrankRules"/> decides what a person can actually turn.
        /// </summary>
        [SerializeField] private List<RecipeDefinition> candidateRecipes = new List<RecipeDefinition>();

        private readonly List<RecipeDefinition> _crankable = new List<RecipeDefinition>();
        private int _selectedIndex;
        private int _progressTicks;

        /// <summary>
        /// Whether the player is holding Interact at this bench right now. Set every frame by
        /// PlayerInteractor and consumed by <see cref="Tick"/>; the bench never reads input
        /// itself, which is what lets a test crank it without an InputActionAsset.
        /// </summary>
        public bool IsCranking { get; set; }

        public IReadOnlyList<RecipeDefinition> CrankableRecipes => _crankable;

        public RecipeDefinition SelectedRecipe =>
            _crankable.Count == 0 ? null : _crankable[Mathf.Clamp(_selectedIndex, 0, _crankable.Count - 1)];

        public int SelectedIndex => _selectedIndex;

        /// <summary>Ticks turned so far on the current craft. Resets on completion or on a swap.</summary>
        public int ProgressTicks => _progressTicks;

        /// <summary>Ticks this craft needs in total, or 0 with nothing selected.</summary>
        public int RequiredTicks =>
            SelectedRecipe == null ? 0 : HandCrankRules.CrankTicks(SelectedRecipe.durationTicks);

        /// <summary>How many crafts this bench has completed. Exposed for tests and the HUD.</summary>
        public int CompletedCrafts { get; private set; }

        /// <summary>Test/bootstrap-friendly wiring, matching the Configure(...) idiom.</summary>
        public void Configure(
            StorageBufferRegistryHolder holder, string bufferId, IEnumerable<RecipeDefinition> recipes)
        {
            stockpileHolder = holder;
            if (!string.IsNullOrEmpty(bufferId))
            {
                stockpileBufferId = bufferId;
            }

            candidateRecipes = recipes == null
                ? new List<RecipeDefinition>()
                : new List<RecipeDefinition>(recipes);

            RebuildCrankableList();
        }

        /// <summary>
        /// Points an already-authored bench at this scene's stockpile, leaving its recipe list
        /// alone. Separate from <see cref="Configure"/> because the recipes ride the prefab while
        /// the buffer registry is a scene object -- and a prefab cannot hold a reference to one.
        /// </summary>
        public void ConfigureStockpile(StorageBufferRegistryHolder holder, string bufferId)
        {
            stockpileHolder = holder;
            if (!string.IsNullOrEmpty(bufferId))
            {
                stockpileBufferId = bufferId;
            }
        }

        private void Awake() => RebuildCrankableList();

        private void RebuildCrankableList()
        {
            _crankable.Clear();
            _crankable.AddRange(HandCrankRules.Filter(candidateRecipes));
            _selectedIndex = _crankable.Count == 0 ? 0 : Mathf.Clamp(_selectedIndex, 0, _crankable.Count - 1);
            _progressTicks = 0;
        }

        /// <summary>
        /// Cycles to the next crankable recipe, abandoning any part-turned craft. Abandoning
        /// costs only time, never goods -- inputs are not taken until the craft completes.
        /// </summary>
        public void CycleRecipe()
        {
            _selectedIndex = HandCrankRules.NextIndex(_selectedIndex, _crankable.Count);
            _progressTicks = 0;
        }

        /// <summary>
        /// Advances the crank. Nothing happens unless the player is actively holding it, which is
        /// the whole point of §11 item 7's held-Interact: the manual era has to cost attention,
        /// or it is just a slower golem.
        /// </summary>
        public void Tick(long tick)
        {
            if (!IsCranking || SelectedRecipe == null)
            {
                return;
            }

            _progressTicks++;
            if (_progressTicks < RequiredTicks)
            {
                return;
            }

            _progressTicks = 0;
            TryCompleteCraft();
        }

        /// <summary>
        /// Takes the inputs and banks the output, or does neither.
        ///
        /// <para>
        /// CHECKED AND CHARGED AT COMPLETION, never at the start. Consuming up front would mean a
        /// player who lets go of the key halfway -- or who is interrupted -- loses the goods with
        /// nothing to show, and a rigid game has no step that could hand them back. Releasing the
        /// crank costs time only. This is the same atomicity §1.3 protects for <c>Assemble</c>:
        /// room for the output is confirmed before any input is withdrawn, so a full stockpile
        /// slot leaves the inputs untouched rather than swallowing them.
        /// </para>
        /// </summary>
        private bool TryCompleteCraft()
        {
            RecipeDefinition recipe = SelectedRecipe;
            if (recipe == null || stockpileHolder == null)
            {
                return false;
            }

            StorageBuffer buffer = stockpileHolder.Registry.GetOrCreate(stockpileBufferId);
            if (buffer == null)
            {
                return false;
            }

            // Room first, for both the output and any byproduct, before anything is taken.
            if (buffer.RoomFor(recipe.outputItemType) < recipe.outputQuantity)
            {
                return false;
            }

            bool hasByproduct = !string.IsNullOrEmpty(recipe.byproductItemType) && recipe.byproductQuantity > 0;
            if (hasByproduct && buffer.RoomFor(recipe.byproductItemType) < recipe.byproductQuantity)
            {
                return false;
            }

            // Atomic with a full refund on shortfall -- the same withdrawal the build menu and the
            // construction station use, so a hand-crank can never half-spend the player's goods.
            if (!stockpileHolder.Registry.TryWithdrawBundle(stockpileBufferId, recipe.inputs))
            {
                return false;
            }

            buffer.Deposit(recipe.outputItemType, recipe.outputQuantity);
            if (hasByproduct)
            {
                buffer.Deposit(recipe.byproductItemType, recipe.byproductQuantity);
            }

            CompletedCrafts++;
            return true;
        }

        /// <summary>
        /// Whether the selected recipe's inputs are currently affordable. Drives the readout, so
        /// the player is told they cannot make a thing before spending a minute turning a handle
        /// for it -- the bench is slow enough that discovering it at completion would be cruel.
        /// </summary>
        public bool CanAffordSelected()
        {
            RecipeDefinition recipe = SelectedRecipe;
            if (recipe == null || stockpileHolder == null ||
                !stockpileHolder.Registry.TryGetBuffer(stockpileBufferId, out StorageBuffer buffer))
            {
                return false;
            }

            for (int i = 0; i < recipe.inputs.Count; i++)
            {
                if (buffer.GetQuantity(recipe.inputs[i].itemType) < recipe.inputs[i].quantity)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
