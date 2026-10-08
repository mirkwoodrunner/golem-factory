using System.Collections.Generic;
using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.PunchCards;
using GolemFactory.Simulation;

namespace GolemFactory.Buildings
{
    /// <summary>
    /// The Hand-Crank Bench (docs/progression-design.md §11 item 7): §9's manual era, a recipe
    /// the player turns by hand, one crank per simulation tick while held.
    ///
    /// <para>
    /// PORTED FROM Unity's sibling component (G2c), a building part now. Every rule is
    /// unchanged: only 1-input recipes are crankable (<see cref="HandCrankRules"/>), progress
    /// accrues on SIMULATION ticks while <see cref="IsCranking"/>, an unaffordable recipe makes
    /// no progress, and a craft is atomic -- room for the output and the byproduct is checked
    /// before a single input is withdrawn.
    /// </para>
    /// </summary>
    public sealed class HandCrankBench : IBuildingPart, ITickable
    {
        private StorageBufferRegistry stockpile;
        private string stockpileBufferId = "FactoryStockpile";
        private List<RecipeDefinition> candidateRecipes = new List<RecipeDefinition>();

        private readonly List<RecipeDefinition> _crankable = new List<RecipeDefinition>();
        private int _selectedIndex;
        private int _progressTicks;

        /// <summary>Set by the interactor while the player holds [E] at this bench.</summary>
        public bool IsCranking { get; set; }

        public IReadOnlyList<RecipeDefinition> CrankableRecipes => _crankable;

        public RecipeDefinition SelectedRecipe =>
            _crankable.Count == 0 ? null : _crankable[Mathf.Clamp(_selectedIndex, 0, _crankable.Count - 1)];

        public int SelectedIndex => _selectedIndex;
        public int ProgressTicks => _progressTicks;

        public int RequiredTicks =>
            SelectedRecipe == null ? 0 : HandCrankRules.CrankTicks(SelectedRecipe.durationTicks);

        /// <summary>Crafts finished since the bench was built -- the interactor's popup watches it.</summary>
        public int CompletedCrafts { get; private set; }

        /// <summary>What the last craft made, which the dial may since have been turned away from.</summary>
        public RecipeDefinition LastCompletedRecipe { get; private set; }

        public void Configure(StorageBufferRegistry buffers, string bufferId, IEnumerable<RecipeDefinition> recipes)
        {
            stockpile = buffers;
            if (!string.IsNullOrEmpty(bufferId))
            {
                stockpileBufferId = bufferId;
            }
            candidateRecipes = recipes == null
                ? new List<RecipeDefinition>()
                : new List<RecipeDefinition>(recipes);
            RebuildCrankableList();
        }

        public void ConfigureStockpile(StorageBufferRegistry buffers, string bufferId)
        {
            stockpile = buffers;
            if (!string.IsNullOrEmpty(bufferId))
            {
                stockpileBufferId = bufferId;
            }
        }

        // Unity's Instantiate carried the authored recipe list and stockpile over and Awake
        // rebuilt the crankable list; progress and the craft tally start fresh.
        public IBuildingPart CloneForInstance()
        {
            var clone = new HandCrankBench
            {
                stockpile = stockpile,
                stockpileBufferId = stockpileBufferId,
                candidateRecipes = new List<RecipeDefinition>(candidateRecipes),
            };
            clone.RebuildCrankableList();
            return clone;
        }

        private void RebuildCrankableList()
        {
            _crankable.Clear();
            _crankable.AddRange(HandCrankRules.Filter(candidateRecipes));
            _selectedIndex = _crankable.Count == 0 ? 0 : Mathf.Clamp(_selectedIndex, 0, _crankable.Count - 1);
            _progressTicks = 0;
        }

        /// <summary>[R] at the bench: the next crankable recipe. Turning the dial resets progress.</summary>
        public void CycleRecipe()
        {
            _selectedIndex = HandCrankRules.NextIndex(_selectedIndex, _crankable.Count);
            _progressTicks = 0;
        }

        public void Tick(long tick)
        {
            if (!IsCranking || SelectedRecipe == null)
            {
                return;
            }

            // An unaffordable recipe makes no progress: cranking a bench with nothing to work
            // must not bank turns the player can cash in the moment goods arrive.
            if (!CanAffordSelected())
            {
                return;
            }

            if (_progressTicks < RequiredTicks)
            {
                _progressTicks++;
            }

            if (_progressTicks < RequiredTicks)
            {
                return;
            }

            if (TryCompleteCraft())
            {
                _progressTicks = 0;
            }
        }

        // Atomic: room for the output AND the byproduct is checked before a single input is
        // withdrawn, so a full stockpile holds the craft at "ready" instead of eating its inputs.
        private bool TryCompleteCraft()
        {
            RecipeDefinition recipe = SelectedRecipe;
            if (recipe == null || stockpile == null)
            {
                return false;
            }

            StorageBuffer buffer = stockpile.GetOrCreate(stockpileBufferId);
            if (buffer == null)
            {
                return false;
            }

            if (buffer.RoomFor(recipe.outputItemType) < recipe.outputQuantity)
            {
                return false;
            }

            bool hasByproduct = !string.IsNullOrEmpty(recipe.byproductItemType) && recipe.byproductQuantity > 0;
            if (hasByproduct && buffer.RoomFor(recipe.byproductItemType) < recipe.byproductQuantity)
            {
                return false;
            }

            if (!stockpile.TryWithdrawBundle(stockpileBufferId, recipe.inputs))
            {
                return false;
            }

            buffer.Deposit(recipe.outputItemType, recipe.outputQuantity);
            if (hasByproduct)
            {
                buffer.Deposit(recipe.byproductItemType, recipe.byproductQuantity);
            }

            LastCompletedRecipe = recipe;
            CompletedCrafts++;
            return true;
        }

        public bool CanAffordSelected()
        {
            RecipeDefinition recipe = SelectedRecipe;
            if (recipe == null || stockpile == null ||
                !stockpile.TryGetBuffer(stockpileBufferId, out StorageBuffer buffer))
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
