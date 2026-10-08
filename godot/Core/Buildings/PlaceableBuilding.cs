using System.Collections.Generic;
using GolemFactory.Compat;
using GolemFactory.PunchCards;

namespace GolemFactory.Buildings
{
    /// <summary>
    /// A building on the grid: where it stands, which way it faces, what it cost, and the
    /// kind-specific <see cref="IBuildingPart"/>s that make it a depot, a belt or a boiler.
    ///
    /// <para>
    /// PORTED FROM Unity's sealed MonoBehaviour of the same name (milestone G2b). Unity
    /// composed a building's kind as sibling components on one prefab; here those are parts,
    /// <see cref="GetPart{T}"/> is GetComponent, and <see cref="Instantiate"/> is Instantiate
    /// -- so build mode's placement and demolition chains ported almost line for line. A
    /// "prefab" is simply a PlaceableBuilding that is never placed itself.
    /// </para>
    /// </summary>
    public sealed class PlaceableBuilding
    {
        public const string LocalPlayerOwnerId = "LocalPlayer";

        /// <summary>
        /// The prefab's name -- Unity's GameObject name, which build mode records as the save
        /// file's <see cref="PrefabKey"/> and the menu shows. An instance carries its prefab's.
        /// </summary>
        public string name = "";

        /// <summary>
        /// What this building costs to place, as an item bundle (docs/progression-design.md
        /// §11 item 8), replacing the <c>scrapCost</c>/<c>brassCost</c> int pair. §3.1 already
        /// prices a Boiler at 30 Scrap + 10 Iron Plate and a Steam Pipe at 1 Iron Plate, and
        /// §3.3 a Floor Expansion at 60 Scrap + 30 Iron Plate -- none of which the pair could
        /// express.
        /// </summary>
        private List<RecipeIngredient> cost = new List<RecipeIngredient>();

        public Vector2Int Cell { get; set; }
        public GolemFactory.World.Facing Facing { get; set; } = GolemFactory.World.Facing.North;

        // --- Can this be laid in a RUN? -----------------------------------------------------
        /// <summary>
        /// Whether holding the mouse down and dragging lays a line of these rather than one.
        ///
        /// <para>
        /// Authored per prefab rather than derived from "has a PlaceableBelt or a
        /// PlaceableSteamPipe", because the question is about the PLAYER'S GESTURE, not about
        /// the part: a belt and a pipe are the two things anyone lays fifteen of in a row
        /// today, but so is a fence, and a Clock Tower with a belt bolted to it would not be. A
        /// flag also fails safe -- anything nobody has thought about places one at a time.
        /// </para>
        /// </summary>
        private bool dragPlaceable;

        public bool IsDragPlaceable => dragPlaceable;

        /// <summary>Test/bootstrap setter, matching the <c>Configure(...)</c> idiom.</summary>
        public void ConfigureDragPlaceable(bool value) => dragPlaceable = value;

        public string OwnerId { get; set; } = LocalPlayerOwnerId;

        // --- Was this building placed during play, or authored into the scene? ---------------
        // Read only by the save system, which rebuilds the former and never the latter, exactly
        // as GolemEntity.IsRuntimeSpawned governs golems. A scene's own furniture comes back
        // with the scene every time it loads, so a save that also rebuilt it would double it on
        // the floor.
        private bool _isRuntimePlaced;

        /// <summary>
        /// True for a building the player placed with build mode (or one rebuilt from a save),
        /// false for one authored into the scene. Defaults to false, so a building nobody marks
        /// is treated as scene furniture the save system must not recreate.
        /// </summary>
        public bool IsRuntimePlaced => _isRuntimePlaced;

        /// <summary>Which prefab to rebuild this from. Null for scene furniture.</summary>
        public string PrefabKey { get; private set; }

        /// <summary>
        /// Marks this building as player-placed and records the prefab it came from. One-way,
        /// like <c>GolemEntity.MarkRuntimeSpawned</c>.
        /// </summary>
        public void MarkRuntimePlaced(string prefabKey)
        {
            _isRuntimePlaced = true;
            PrefabKey = prefabKey;
        }

        public IReadOnlyList<RecipeIngredient> Cost => cost;

        public void ConfigureCost(IEnumerable<RecipeIngredient> newCost)
        {
            cost = newCost == null
                ? new List<RecipeIngredient>()
                : new List<RecipeIngredient>(newCost);
        }

        /// <summary>The pre-bundle two-good form, kept for the call sites that still use it.</summary>
        public void ConfigureCost(int scrapCost, int brassCost)
        {
            cost = new List<RecipeIngredient>();
            if (scrapCost > 0)
            {
                cost.Add(new RecipeIngredient(Economy.ItemType.Scrap, scrapCost));
            }
            if (brassCost > 0)
            {
                cost.Add(new RecipeIngredient(Economy.ItemType.Brass, brassCost));
            }
        }

        // --- Parts: Unity's sibling components ------------------------------------------------

        private readonly List<IBuildingPart> _parts = new List<IBuildingPart>();

        public IReadOnlyList<IBuildingPart> Parts => _parts;

        /// <summary>Unity's AddComponent. Returns the part, for chaining in setup code.</summary>
        public T AddPart<T>(T part) where T : IBuildingPart
        {
            _parts.Add(part);
            return part;
        }

        /// <summary>Unity's GetComponent: the first part of type <typeparamref name="T"/>, or null.</summary>
        public T GetPart<T>() where T : class
        {
            for (int i = 0; i < _parts.Count; i++)
            {
                if (_parts[i] is T match)
                {
                    return match;
                }
            }
            return null;
        }

        /// <summary>
        /// A new building from this one as a prefab -- Unity's Instantiate. Authored settings
        /// carry over (name, cost, the drag flag, each part's own settings); placement and the
        /// runtime-placed mark do not, because build mode sets those on the instance.
        /// </summary>
        public PlaceableBuilding Instantiate()
        {
            var instance = new PlaceableBuilding
            {
                name = name,
                cost = new List<RecipeIngredient>(cost),
                dragPlaceable = dragPlaceable,
                OwnerId = OwnerId,
            };
            for (int i = 0; i < _parts.Count; i++)
            {
                instance._parts.Add(_parts[i].CloneForInstance());
            }
            return instance;
        }

        /// <summary>
        /// True once build mode has torn this building down -- Unity's destroyed-object null.
        /// </summary>
        public bool IsRemoved { get; private set; }

        /// <summary>Marks this building gone. Called by build mode after unregistering it.</summary>
        public void MarkRemoved() => IsRemoved = true;
    }
}
