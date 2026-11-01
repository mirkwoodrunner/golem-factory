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
        private string postBufferId;
        private List<RecipeDefinition> candidateRecipes = new List<RecipeDefinition>();

        private readonly List<RecipeDefinition> _crankable = new List<RecipeDefinition>();
        private int _selectedIndex;
        private int _progressTicks;
        private int _turnFrame;

        /// <summary>Frames in a turn of the crank: the wheel's and the Artificer's.</summary>
        public const int TurnFrames = 4;

        /// <summary>Set by the interactor while the player holds [E] at this bench.</summary>
        public bool IsCranking { get; set; }

        public IReadOnlyList<RecipeDefinition> CrankableRecipes => _crankable;

        public RecipeDefinition SelectedRecipe =>
            _crankable.Count == 0 ? null : _crankable[Mathf.Clamp(_selectedIndex, 0, _crankable.Count - 1)];

        public int SelectedIndex => _selectedIndex;
        public int ProgressTicks => _progressTicks;

        /// <summary>
        /// Whether the crank is turning right now: held, with a recipe it can afford. Holding
        /// [E] at a recipe it can't afford makes no progress (see <see cref="Tick"/>), so it
        /// doesn't turn either -- the still wheel is the player's cue that something is missing.
        /// </summary>
        public bool IsTurning => IsCranking && SelectedRecipe != null && CanAffordSelected();

        /// <summary>
        /// Which of the <see cref="TurnFrames"/> turning pictures shows: one step per SIMULATION
        /// tick that made progress, so the wheel and the Artificer turn with the work at any game
        /// speed and stop when it stops (paused, or out of goods).
        /// </summary>
        public int TurnFrame => _turnFrame;

        /// <summary>The bench's picture: <paramref name="baseSprite"/>, or its turning frame.</summary>
        public string SpriteFor(string baseSprite) =>
            IsTurning ? $"{baseSprite}_turn_{_turnFrame}" : baseSprite;

        public int RequiredTicks =>
            SelectedRecipe == null ? 0 : HandCrankRules.CrankTicks(SelectedRecipe.durationTicks);

        /// <summary>Crafts finished since the bench was built -- the interactor's popup watches it.</summary>
        public int CompletedCrafts { get; private set; }

        /// <summary>What the last craft made, which the dial may since have been turned away from.</summary>
        public RecipeDefinition LastCompletedRecipe { get; private set; }

        /// <param name="postId">The Pneumatic Post's store, spent after the pack (crates-design §10); null for none.</param>
        public void Configure(StorageBufferRegistry buffers, string bufferId, IEnumerable<RecipeDefinition> recipes, string postId = null)
        {
            stockpile = buffers;
            postBufferId = postId;
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
        /// The pack (and the Post) a bench placed from the build menu crafts from: the catalog's
        /// prefab is built before any registry exists, so the world wires each placed one.
        /// </summary>
        public void ConfigureStockpile(StorageBufferRegistry buffers, string bufferId, string postId = null)
        {
            stockpile = buffers;
            postBufferId = postId;
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
                postBufferId = postBufferId,
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

            _turnFrame = (_turnFrame + 1) % TurnFrames;

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

            if (!Wallet.TryCharge(recipe.inputs))
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

        // The pack, then the Post: inputs are a cost (crates-design §4.4). The output still lands
        // in the pack, which is what the player carries away from the bench.
        private PlayerWallet Wallet => new PlayerWallet(stockpile, stockpileBufferId, postBufferId);

        public bool CanAffordSelected()
        {
            RecipeDefinition recipe = SelectedRecipe;
            if (recipe == null || stockpile == null || !stockpile.TryGetBuffer(stockpileBufferId, out _))
            {
                return false;
            }

            for (int i = 0; i < recipe.inputs.Count; i++)
            {
                if (Wallet.Quantity(recipe.inputs[i].itemType) < recipe.inputs[i].quantity)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
