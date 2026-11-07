using System;
using System.Collections.Generic;
using System.Linq;
using GolemFactory.AssemblyLine;
using GolemFactory.Buildings;
using GolemFactory.Compat;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.Supply;
using GolemFactory.World;

namespace GolemFactory.Tutorial
{
    /// <summary>One step of the guide: what to do, how far along it is, and what to point at.</summary>
    public sealed class TutorialStep
    {
        public string Id { get; }
        private readonly string _title;

        /// <summary>The step's title, its key names filled like <see cref="Body"/>.</summary>
        public string Title => Settings.TextTokens.Fill(_title);
        private readonly string _body;

        /// <summary>The step's text, its key names and colour words filled (<see cref="Settings.TextTokens.Fill"/>).</summary>
        public string Body => Settings.TextTokens.Fill(_body);

        /// <summary>The build-menu row the step needs (a placeable key), or null.</summary>
        public string MenuKey { get; }

        /// <summary>
        /// The tile the step wants something placed on, or null. The panel marks it on the floor;
        /// with <see cref="SpotFacing"/> set, the marker also shows which way to face. Read live:
        /// the guide lays its later tiles out around where the player actually built (round 3).
        /// </summary>
        public Vector2Int? Spot
        {
            get
            {
                IReadOnlyList<Vector2Int> spots = Spots;
                return spots.Count > 0 ? spots[0] : (Vector2Int?)null;
            }
        }

        /// <summary>Every tile the step marks: <see cref="Spot"/> first, then any others (a pipe run).</summary>
        public IReadOnlyList<Vector2Int> Spots
        {
            get
            {
                // Cached per layout: the panel and the notes read these many times a frame, and
                // they change only when the guide's plan does.
                int version = MarksVersion?.Invoke() ?? -1;
                if (_cachedSpots != null && version >= 0 && version == _cachedVersion)
                {
                    return _cachedSpots;
                }
                var spots = new List<Vector2Int>();
                Vector2Int? spot = _spot?.Invoke();
                if (spot != null)
                {
                    spots.Add(spot.Value);
                }
                Vector2Int[] more = _moreSpots?.Invoke();
                if (more != null)
                {
                    spots.AddRange(more);
                }
                _cachedSpots = spots;
                _cachedVersion = version;
                return spots;
            }
        }

        /// <summary>The guide's plan version, set by the guide; marks are cached while it holds.</summary>
        internal Func<int> MarksVersion { get; set; }

        private IReadOnlyList<Vector2Int> _cachedSpots;
        private int _cachedVersion = -1;

        public Facing? SpotFacing => _spotFacing?.Invoke();

        private readonly Func<Vector2Int?> _spot;
        private readonly Func<Vector2Int[]> _moreSpots;
        private readonly Func<Facing?> _spotFacing;

        /// <summary>
        /// The card this step asks the player to claim (its appendage's name), or null. The
        /// Assembly Line keeps it on show while the step is current.
        /// </summary>
        public string Card { get; }

        /// <summary>
        /// Whether the step is done inside the Workbench, so the guide stays on show over it
        /// (docked clear of its controls) rather than stepping aside as it does over other screens.
        /// </summary>
        public bool Workbench { get; }

        /// <summary>
        /// Whether the step is done inside the Crate screen, so the guide stays on show over it in
        /// its usual dock, which that screen leaves clear (docs/inventory-screens-design.md §5).
        /// </summary>
        public bool Crate { get; }

        /// <summary>
        /// The program this step asks for, or null (docs/onboarding-feedback-design.md §2.1). One
        /// definition, read three ways: the Workbench hint walks it, the step's Done check is its
        /// <see cref="ProgramPlan.Matches"/>, and the playtest kit writes it.
        /// </summary>
        public ProgramPlan Program { get; }

        /// <summary>Whether a golem is the one <see cref="Program"/> is meant for. Null without a plan.</summary>
        public Func<GolemEntity, bool> ProgramTarget { get; }

        /// <summary>
        /// The kind of building the step's marked tiles are for, or null (a golem's tile, a goal).
        /// The guide uses it to tell "something else is on the marked tile" from "the right thing".
        /// </summary>
        internal Func<PlaceableBuilding, bool> Builds { get; }

        /// <summary>
        /// Whether the step wants its building ON the marked tile because later steps are laid
        /// out around it. Building it anywhere else then earns a note saying so. Pipes and the
        /// mast take any spot that works, and say nothing.
        /// </summary>
        internal bool ExactTile { get; }

        internal Func<SandboxWorld, bool> Done { get; }
        public Func<SandboxWorld, string> ProgressText { get; }
        internal Func<SandboxWorld, Vector2Int?> Target { get; }

        internal TutorialStep(
            string id, string title, string body, Func<SandboxWorld, bool> done,
            Func<SandboxWorld, Vector2Int?> target, Func<SandboxWorld, string> progress = null, string menuKey = null,
            Func<Vector2Int?> spot = null, Func<Facing?> spotFacing = null, Func<Vector2Int[]> moreSpots = null, string card = null,
            bool workbench = false, Func<PlaceableBuilding, bool> builds = null, bool exactTile = false, bool crate = false,
            ProgramPlan program = null, Func<GolemEntity, bool> programTarget = null)
        {
            Program = program;
            ProgramTarget = programTarget;
            Crate = crate;
            Builds = builds;
            ExactTile = exactTile;
            Card = card;
            Workbench = workbench;
            _spot = spot;
            _spotFacing = spotFacing;
            _moreSpots = moreSpots;
            Id = id;
            _title = title;
            _body = body;
            Done = done;
            Target = target;
            ProgressText = progress;
            MenuKey = menuKey;
        }
    }

    /// <summary>
    /// The step-by-step guide through a first session (G10, at the user's call: "I think there
    /// needs to be a tutorial"). It walks the cold start testscript Part E describes -- Scrap,
    /// a Coal cart, Coke and Iron Plate at the bench, a fuelled boiler -- and then the first
    /// golem: built, programmed, given a depot, and set to work beside the Scrap cart.
    ///
    /// <para>
    /// <b>Every step is DETECTED, never ticked off by hand.</b> A step is done when the world shows
    /// it (the pack holds the Coal, a boiler has Coke in it, a golem has finished a cycle),
    /// so a player who does things early or in another order is carried straight past what they
    /// already did, and the guide can never claim something the factory does not show. Advancing
    /// is one-way: spending the Coal on Coke does not send the guide back a step.
    /// </para>
    ///
    /// <para>
    /// Engine-free, like everything the Sandbox composes: the Godot panel only draws
    /// <see cref="Current"/>, its <see cref="Progress"/> and its <see cref="TargetCell"/>.
    /// </para>
    /// </summary>
    public sealed partial class TutorialGuide
    {
        private readonly SandboxWorld _world;
        private readonly List<TutorialStep> _steps;
        private bool _cycleSeen;
        private readonly HashSet<string> _completedSinceEntry = new HashSet<string>();

        public TutorialGuide(SandboxWorld world)
        {
            _world = world;
            // Before the steps, which hold their plans: built from this world's data, so a
            // batch that mirrors a recipe or the heap's ratio is never a literal.
            IronPresserPlan = IronPresserPlanFor(world.Definitions);
            CokerPlan = CokerPlanFor(world.Definitions);
            SmelterPlan = SmelterPlanFor(world.Definitions);
            CarrierPlan = CarrierPlanFor(HeapSlagPerCoke);
            _steps = BuildSteps();
            foreach (TutorialStep step in _steps)
            {
                step.MarksVersion = () => PlanVersion;
            }
            EventBus.GolemCompleted += OnGolemCompleted;
        }

        public IReadOnlyList<TutorialStep> Steps => _steps;

        /// <summary>The index of the current step; <see cref="StepCount"/> once finished.</summary>
        public int Index { get; private set; }

        public int StepCount => _steps.Count;

        /// <summary>Put away by the player (Skip guide), or finished. F1 brings it back.</summary>
        public bool Dismissed { get; private set; }

        public bool IsFinished => Index >= _steps.Count;

        /// <summary>Whether the panel should be drawn.</summary>
        public bool IsShowing => !Dismissed && !IsFinished;

        public TutorialStep Current => IsFinished ? null : _steps[Index];

        /// <summary>"Coal 3 / 5", or "" when the step has no count.</summary>
        public string Progress => Current?.ProgressText?.Invoke(_world) ?? "";

        /// <summary>The world cell the arrow points at, or null.</summary>
        public Vector2Int? TargetCell => Current?.Target(_world);

        /// <summary>
        /// The card the current step asks the player to claim (by its appendage), or null. The
        /// Assembly Line keeps it on show.
        /// </summary>
        public string WantedCardAppendage => IsShowing || KitRunning ? Current?.Card : null;

        /// <summary>Bumped whenever the step changes, so a view redraws on change only.</summary>
        public int Version { get; private set; }

        /// <summary>Moves past every step the world already shows done. Cheap; called each frame.</summary>
        public void Update()
        {
            Settle();
            KitTick();
        }

        /// <summary>
        /// Moves past every step that is already done, and nothing else. A save calls this, never
        /// <see cref="Update"/>: Update also runs the playtest kit, and a kit step called from
        /// inside a save performed the NEXT step (Load) mid-save, which captured whatever the
        /// stale file held.
        /// </summary>
        public void Settle()
        {
            while (!IsFinished && Current.Done(_world))
            {
                Enter(Index + 1);
            }
        }

        /// <summary>The panel's Skip guide button.</summary>
        public void Dismiss()
        {
            Dismissed = true;
            Version++;
        }

        /// <summary>F1: the guide comes back where it was -- or, once finished, from the start.</summary>
        public void Reopen()
        {
            Dismissed = false;
            if (IsFinished)
            {
                Enter(0);
            }
            Version++;
            Update();
        }

        /// <summary>The last step's Finish button.</summary>
        public void Finish() => Enter(_steps.Count);

        /// <summary>What a save records once the guide is finished: no step has this id.</summary>
        public const string FinishedStepId = "finished";

        /// <summary>The current step's id, or <see cref="FinishedStepId"/>: what a save records.</summary>
        public string CurrentStepId => Current?.Id ?? FinishedStepId;

        /// <summary>
        /// A load: the saved step and whether the guide was put away. The step is found by its
        /// <paramref name="stepId"/>, because every chapter added since the guide shipped has
        /// inserted steps, and a bare index then names a different step (a finished guide came
        /// back on chapter 3). <paramref name="index"/> is only the fallback for a save written
        /// before ids were saved, or one naming a step this build no longer has.
        /// </summary>
        public void Restore(string stepId, int index, bool dismissed)
        {
            Dismissed = dismissed;
            if (stepId == FinishedStepId)
            {
                Enter(_steps.Count);
            }
            else
            {
                int byId = string.IsNullOrEmpty(stepId) ? -1 : _steps.FindIndex(s => s.Id == stepId);
                Enter(byId >= 0 ? byId : Math.Max(0, Math.Min(index, _steps.Count)));
            }
            _enteredByLoad = true;
        }

        // Set when the current step was entered by a load rather than reached in play: every
        // golem then predates the step, including one the player built for it before saving.
        private bool _enteredByLoad;

        // A save from before roles were saved: its golems have no recorded roles at all.
        private bool _rolesFromOldSave;

        private void Enter(int index)
        {
            _enteredByLoad = false;
            Index = index;
            _cycleSeen = false;
            _completedSinceEntry.Clear();
            _golemsAtEntry.Clear();
            foreach (GolemEntity golem in LiveGolems)
            {
                _golemsAtEntry.Add(golem.GolemId);
            }
            _buildingsAtEntry.Clear();
            _buildingsAtEntry.UnionWith(Built);
            Version++;
            ReportStep();
        }

        // --- Which golem a step means ---------------------------------------------------------
        //
        // Chapters 4 to 6 each build a Scavenger and then talk about THAT golem for several steps.
        // They used to find it by build order (the second Scavenger, the third, the fourth), so a
        // spare built along the way, or one taken down with the wrecking bar, made every later
        // step check the wrong golem: "An extractor" completed on a spare at once, and "An
        // unloader" then waited forever on a golem with no Haul (from review). Now the golem a
        // step builds is remembered by id the moment it appears, and the save keeps it.

        /// <summary>Each role, the step that builds its golem, and the golem's chassis, in guide order.</summary>
        private static readonly (string role, string stepId, string chassis)[] RoleOpenings =
        {
            ("first", "golem", "ClockworkScavenger"),
            ("scav2", "scav2", "ClockworkScavenger"),
            ("extractor", "scav3", "ClockworkScavenger"),
            ("unloader", "unloader", "ClockworkScavenger"),
        };

        private readonly Dictionary<string, string> _roles = new Dictionary<string, string>();
        private readonly HashSet<string> _golemsAtEntry = new HashSet<string>();
        private readonly HashSet<PlaceableBuilding> _buildingsAtEntry = new HashSet<PlaceableBuilding>();

        // --- Why a step is waiting, when the answer is where something was built ---------------
        //
        // Round 2 of the exact-tile plan. Buildings later steps are laid out around stay on their
        // marked tiles, but the guide no longer waits in silence: it says when the right building
        // went up somewhere else, and when something else is sitting on a marked tile (a pipe laid
        // on a free route, since round 1, can pave over a later chapter's tile).

        /// <summary>A note under the step, or "": what is in the way of the marked tiles.</summary>
        public string Notice => IsShowing ? NoticeFor(Current) : "";

        private string NoticeFor(TutorialStep step)
        {
            if (step == null || step.Spots.Count == 0)
            {
                return "";
            }

            foreach (Vector2Int spot in step.Spots)
            {
                PlaceableBuilding there = BuildingOn(spot);
                if (there == null || (step.Builds != null && step.Builds(there)))
                {
                    continue;
                }
                string what = NameOf(there);
                return step.SpotFacing != null
                    ? $"A {what} is on the golem's tile, and a golem can't stand on a building. Take it up with the wrecking bar (full refund)."
                    : $"A {what} is on a marked tile. Click it to take it up (full refund), then click again to build.";
            }

            // A golem's tile the layout moved somewhere steam doesn't reach yet (round 3).
            if (step.SpotFacing != null && step.Spot is Vector2Int golemTile && !SteamReaches(golemTile))
            {
                return "No steam reaches the marked tile yet: lay a Steam Pipe beside it from your boilers.";
            }

            if (step.MenuKey == "BeltPrefab")
            {
                foreach (Vector2Int spot in step.Spots)
                {
                    if (_world.Belts.TryGetBelt(spot, out PlacedBelt belt) && belt.Facing != CopperFacing)
                    {
                        return $"A belt on the run points {Way(belt.Facing)}, and the run must point {Way(CopperFacing)}, "
                            + "away from the cart. Click it to take it up (full refund) and drag the run again from the cart's end.";
                    }
                }
            }

            if (step.ExactTile && step.Builds != null)
            {
                IReadOnlyList<Vector2Int> marks = step.Spots;
                List<PlaceableBuilding> strays = Built.Where(b =>
                    !_buildingsAtEntry.Contains(b) && step.Builds(b) && !marks.Contains(b.Cell)).ToList();
                // The smelter's depots commit as a PAIR, so the first of the player's own pair is
                // not a stray yet: telling them to take it back would stop them building the second.
                PlaceableBuilding stray = step.Id == "smelt-depots" && strays.Count < 2 ? null : strays.FirstOrDefault();
                if (stray != null)
                {
                    return $"That {NameOf(stray)} doesn't line up: a golem works between two things two tiles apart in a "
                        + "straight line, with the tile between them free. Click it again to take it back (full refund).";
                }
            }
            return "";
        }

        private PlaceableBuilding BuildingOn(Vector2Int cell) =>
            _world.Grid.TryGetOccupant(cell, out object occupant) ? occupant as PlaceableBuilding : null;

        private static string NameOf(PlaceableBuilding building) =>
            UI.BuildMenuLabels.NameFor(building.PrefabKey ?? building.name);

        private static string Way(Facing facing) =>
            facing == Facing.North ? "up" : facing == Facing.East ? "right" : facing == Facing.South ? "down" : "left";

        private static bool IsDepot(PlaceableBuilding b) => b.GetPart<PlaceableDepot>() != null;

        private static bool IsBoiler(PlaceableBuilding b) => b.GetPart<PlaceableBoiler>() != null;

        private static bool IsPipe(PlaceableBuilding b) => b.GetPart<PlaceableSteamPipe>() != null;

        /// <summary>
        /// The golem playing <paramref name="role"/>, or null before its step has built one.
        /// Assigned on the step that builds it, to a golem of its chassis that was not standing
        /// when the step began. A role whose golem is gone (dismantled), or that a save from
        /// before roles never recorded, falls to the oldest golem of its chassis holding no
        /// other role -- what build order used to answer, minus the golems already spoken for.
        /// </summary>
        private GolemEntity Role(string role)
        {
            ResolveRoles();
            return _roles.TryGetValue(role, out string id) ? LiveGolem(id) : null;
        }

        private GolemEntity LiveGolem(string id) => LiveGolems.FirstOrDefault(g => g.GolemId == id);

        private int[] _roleOpeningIndex;

        private void ResolveRoles()
        {
            _roleOpeningIndex ??= RoleOpenings.Select(r => _steps.FindIndex(s => s.Id == r.stepId)).ToArray();
            for (int r = 0; r < RoleOpenings.Length; r++)
            {
                (string role, string _, string chassis) = RoleOpenings[r];
                int opening = _roleOpeningIndex[r];
                if (opening < 0 || Index < opening)
                {
                    return; // later roles open later still
                }
                if (_roles.TryGetValue(role, out string held) && LiveGolem(held) != null)
                {
                    continue;
                }

                IEnumerable<GolemEntity> free = LiveGolems.Where(g =>
                    g.Program?.chassis?.name == chassis && !_roles.Values.Contains(g.GolemId));
                // The step that builds this role's golem takes one built since it began -- or,
                // loading a save from before roles, the newest free one, since everything
                // predates a load and that save could not say which golem it built. A
                // PAST role refills only from golems that predate the current step, so a role
                // whose golem was dismantled cannot take the golem just built for this step
                // (from review: the extractor's new golem went to chapter 4's empty role).
                GolemEntity pick = Index == opening
                    ? free.LastOrDefault(g => (_enteredByLoad && _rolesFromOldSave) || !_golemsAtEntry.Contains(g.GolemId))
                    : free.FirstOrDefault(g => _golemsAtEntry.Contains(g.GolemId));
                if (pick != null)
                {
                    _roles[role] = pick.GolemId;
                }
            }
        }

        /// <summary>What a save records of the roles: "role=golemId" pairs.</summary>
        public IEnumerable<string> RoleEntries
        {
            get
            {
                ResolveRoles();
                return _roles.OrderBy(r => r.Key).Select(r => r.Key + "=" + r.Value).ToList();
            }
        }

        /// <summary>A load: the roles a save recorded. Call before <see cref="Restore"/>.</summary>
        public void RestoreRoles(IEnumerable<string> entries)
        {
            _roles.Clear();
            _rolesFromOldSave = entries == null || !entries.Any();
            foreach (string entry in entries ?? Enumerable.Empty<string>())
            {
                int split = entry?.IndexOf('=') ?? -1;
                if (split > 0)
                {
                    _roles[entry.Substring(0, split)] = entry.Substring(split + 1);
                }
            }
        }

        /// <summary>Playtest mode, when on: questions and timings ride along with the steps.</summary>
        public PlaytestSession Playtest { get; private set; }

        private Func<float> _clock = () => 0f;

        /// <summary>Turns playtest mode on, timed by <paramref name="clock"/> (real seconds).</summary>
        public void AttachPlaytest(PlaytestSession session, Func<float> clock)
        {
            Playtest = session;
            _clock = clock ?? (() => 0f);
            ReportStep();
        }

        /// <summary>Real seconds since the session began, as the playtest report counts them.</summary>
        public float Now => _clock();

        private void ReportStep()
        {
            if (Playtest == null)
            {
                return;
            }
            TutorialStep step = Current;
            Playtest.StepEntered(step?.Id ?? "finished", step?.Title ?? "Finished", _clock(), ChapterOf(Index));
        }

        /// <summary>Stops listening to the bus, for good: the world is going (SandboxWorld.Dispose).</summary>
        public void Detach() => EventBus.GolemCompleted -= OnGolemCompleted;

        private void OnGolemCompleted(GolemCompletedEvent e)
        {
            // Only this world's golems: tests compose many worlds on one static bus.
            if (_world.Golems.Any(g => g != null && !g.IsRemoved && g.GolemId == e.GolemId))
            {
                _cycleSeen = true;
                _completedSinceEntry.Add(e.GolemId);
            }
        }

        // --- The steps --------------------------------------------------------------------------

        private int Stock(string item) => _world.Buffers.GetQuantity(_world.StockpileBufferId, item);

        private string Count(string item, int goal) =>
            $"{ItemTiers.DisplayName(item)}  {Math.Min(Stock(item), goal)} / {goal}";

        // --- The street's carts (docs/supply-design.md §6) ----------------------------------------

        /// <summary>
        /// Where the guide wants a good's first cart: the spot it marks. Its home spot (§6), or,
        /// if another good's cart stands there, the nearest empty spot, so the mark is one the
        /// player can use.
        /// </summary>
        private Vector2Int? Home(string item)
        {
            Vector2Int? home = _world.Setup?.HomeSpot(item);
            CartSpot at = home != null ? _world.Supply?.SpotAt(home.Value) : null;
            if (at?.Cart == null || at.Cart.ItemType == item)
            {
                return home;
            }
            return _world.Supply.Spots.Where(s => s.IsEmpty).OrderBy(s => Math.Abs(s.Cell.x - home.Value.x)).ThenBy(s => s.Cell.x)
                .Select(s => (Vector2Int?)s.Cell).FirstOrDefault() ?? home;
        }

        /// <summary>The spot holding a cart of this good, its home spot first; null with none.</summary>
        private CartSpot CartSpotOf(string item)
        {
            Vector2Int? home = Home(item);
            return _world.Supply?.Spots.Where(s => s.Cart?.ItemType == item)
                .OrderBy(s => home != null && s.Cell == home.Value ? 0 : 1).FirstOrDefault();
        }

        private SupplyCart CartOf(string item) => CartSpotOf(item)?.Cart;

        /// <summary>Where a good comes from: its cart if one stands, else the spot marked for it.</summary>
        private Vector2Int? Cart(string item) => CartSpotOf(item)?.Cell ?? Home(item);

        private CartLadder Ladder(string item) =>
            _world.Supply != null && _world.Supply.TryGetLadder(item, out CartLadder ladder) ? ladder : null;

        /// <summary>"80 Scrap": what a cart of this good costs to add.</summary>
        private IReadOnlyList<RecipeIngredient> CartPrice(string item) =>
            Ladder(item)?.AddPrice ?? new List<RecipeIngredient>();

        /// <summary>The first Rate rung's price for this good's cart.</summary>
        private IReadOnlyList<RecipeIngredient> FirstRatePrice(string item) =>
            Ladder(item)?.RateCosts.FirstOrDefault() ?? new List<RecipeIngredient>();

        private int RateAt(string item, int rung) =>
            Ladder(item) is CartLadder l && rung < l.Rates.Count ? l.Rates[rung] : 0;

        /// <summary>Points at the Scrap cart while the player can't pay for this good's cart, else at its spot.</summary>
        private Vector2Int? CartOrScrap(string item) =>
            CartOf(item) == null && Stock(ItemType.Scrap) < LineOf(CartPrice(item), ItemType.Scrap) ? Cart(ItemType.Scrap) : Cart(item);

        private Vector2Int? Bench => _world.Setup?.starterBench != null
            ? SandboxSetup.CellOf(_world.Setup.starterBench)
            : (Vector2Int?)null;

        private Vector2Int? Station => _world.Setup?.starterStation != null
            ? SandboxSetup.CellOf(_world.Setup.starterStation)
            : (Vector2Int?)null;

        /// <summary>Points at the Scrap cart while the player is short of Scrap, else at <paramref name="then"/>.</summary>
        private Vector2Int? ScrapFirst(int need, Vector2Int? then) => Stock(ItemType.Scrap) < need ? Cart(ItemType.Scrap) : then;

        // --- The first golem's layout --------------------------------------------------------------
        //
        // Fixed, beside the starting Scrap cart, so the three placements explain each other (G10,
        // from playtest: "it would be good to specify where the boiler, depot and golem should
        // be placed"):
        //
        //     depot        <- in front: the golem pushes into it
        //     golem boiler <- the boiler beside it powers it, no pipe needed
        //     Scrap cart   <- behind: the golem extracts from it
        //
        // The golem faces north, away from the cart. Every cell is a street cell the golems
        // scenario has already built on. That is the DEFAULT: the marks follow what the player
        // actually builds (round 3, TutorialGuide.Layout.cs).

        private Vector2Int StallCell => Cart(ItemType.Scrap) ?? new Vector2Int(0, 0);

        /// <summary>Where the first golem stands: beside the Scrap cart (north by default).</summary>
        public Vector2Int GolemSpot => Plan.Golem1;

        /// <summary>Which way the first golem faces: away from the stall, at its depot.</summary>
        public Facing GolemFacing => Plan.Facing1;

        /// <summary>In front of the golem.</summary>
        public Vector2Int DepotSpot => Plan.Depot1;

        /// <summary>Beside the golem, east of it.</summary>
        public Vector2Int BoilerSpot => StallCell + new Vector2Int(1, 1);

        // Chapter 2 extends the same column north: the Presser stands on the far side of the first
        // depot, hauling the Scrap the Scavenger delivers into that depot's crate (a golem reaches
        // only the crates beside it), and pushes Iron Plate into a second depot in front of it. It is two tiles
        // from the boiler, so it needs the game's first steam pipe -- the lesson of Phase 2.
        //
        //     depot 2      <- the Presser pushes Iron Plate into it
        //     Presser  P   <- P: steam pipe
        //     depot 1  P
        //     golem boiler
        //     Scrap cart

        /// <summary>Where the first Brass Presser stands: on the first depot's far side.</summary>
        public Vector2Int PresserSpot => Plan.Presser;

        public Facing PresserFacing => Plan.PresserFacing;

        /// <summary>In front of the Presser.</summary>
        public Vector2Int PresserDepotSpot => Plan.PresserDepot;

        /// <summary>The pipe run from the boiler to the Presser's side.</summary>
        public Vector2Int[] PipeSpots => Plan.Pipe1;

        private bool SteamReachesPresserSpot => SteamReaches(PresserSpot);

        // --- Pipe steps are done by what the steam does, not by which tiles hold pipe ------------
        //
        // The marked tiles are a suggestion the guide draws; a player who routes a pipe another
        // way and gets the same steam to the same golem tiles is done too (from review: a working
        // factory stuck on "Join the boilers" because three exact tiles were bare). Steam reach is
        // a layout question, so these ignore Coke: fuel is a different step's problem.

        /// <summary>"Pipes to lay  3": how many marked pipe tiles are still bare.</summary>
        private string PipesLeft(Vector2Int[] route) => $"Pipes to lay  {route.Count(c => !_world.Steam.HasPipe(c))}";

        /// <summary>Whether any boiler's pipes reach <paramref name="cell"/>, fuelled or not.</summary>
        private bool SteamReaches(Vector2Int cell) => _world.Steam.Reaches(cell, _world.Clock.CurrentTick);

        /// <summary>
        /// Chapter 3's pipes join an older boiler to the new one, so the older one powers the coker
        /// before the new boiler has any Coke. Joined boilers share their reach (SteamNetwork), so
        /// any route that joins them reaches the coker's tile beside the new boiler. Only a pipe
        /// joins boilers, never a golem standing between them.
        /// </summary>
        private bool FirstBoilerPowersCokerAndJoins
        {
            get
            {
                long tick = _world.Clock.CurrentTick;
                return BoilerBuildings.Any(b => b.Cell != Boiler2Spot
                    && _world.Steam.ReachesFrom(b.Cell, CokerSpot, tick)
                    && _world.Steam.AreJoined(b.Cell, Boiler2Spot, tick));
            }
        }

        private IEnumerable<GolemEntity> Pressers =>
            LiveGolems.Where(g => g.Program?.chassis != null && g.Program.chassis.name == "BrassPresser");

        // --- The program plans ----------------------------------------------------------------
        //
        // docs/guide-chapters-design.md §3. Each program step names its program once: the
        // Workbench hint walks the slots, the step's Done check is the plan's Matches (the check
        // each step always had, moved onto its plan), and the kit writes the slots. The cards,
        // goods and batch sizes are what the kit always passed.
        //
        // A plan that only names cards is static. A plan whose batch mirrors a recipe or the Slag
        // Heap's ratio is built from this world's loaded data in the constructor (the *PlanFor
        // builders), so a retune moves the hint, the kit and the step's text with it, and no
        // batch that matches a recipe or a ratio is a literal. Matches never reads a batch size,
        // so a golem programmed with an older batch still counts.

        private const string AlwaysOn = "AlwaysOnCore";

        private static PlanSlot Slot(string card, string good = null, int quantity = 0) => new PlanSlot(card, good, quantity);

        /// <summary>Whether a program has a logic core and every one of these cards, in any slot.</summary>
        private static bool HasCards(GolemProgram program, params string[] cards) =>
            program?.logicCore != null
            && cards.All(card => program.appendages.Any(a => a != null && a.name == card));

        private static int HaulsOf(GolemProgram program, string itemType) =>
            Enumerable.Range(0, program.appendages.Count).Count(i =>
                program.appendages[i]?.actionType == AppendageActionType.Haul
                && program.GetItemTypeAt(i) == itemType);

        /// <summary>Chapters 1, 4 and 6: Extract, then Push. Any trigger, either order.</summary>
        public static readonly ProgramPlan ScavengerPlan = new ProgramPlan(AlwaysOn,
            p => HasCards(p, "ExtractScrap", "PushOutput"),
            Slot("ExtractScrap"), Slot("PushOutput"));

        /// <summary>Chapter 2: Haul, Assemble Scrap Reclamation, Push. Built from the data: <see cref="IronPresserPlanFor"/>.</summary>
        public ProgramPlan IronPresserPlan { get; }

        /// <summary>Chapter 3: Extract, Assemble Coking, Push. Built from the data: <see cref="CokerPlanFor"/>.</summary>
        public ProgramPlan CokerPlan { get; }

        /// <summary>Chapter 5: two Hauls, Scrap and Coke, in either order. Built from the data: <see cref="SmelterPlanFor"/>.</summary>
        public ProgramPlan SmelterPlan { get; }

        /// <summary>Chapter 5: Slag and the Coke that burns it, then Push. Built from the data: <see cref="CarrierPlanFor"/>.</summary>
        public ProgramPlan CarrierPlan { get; }

        // The unloader's and the Zeppelin's Haul of 1 Copper Ore is a teaching choice, not a
        // mirror: nothing downstream takes ore in batches (a Push empties whatever is held, and
        // Freight Launch flies whatever it holds), so one ore a trip is simply the plainest
        // setting to show. Neither step's text names the number.

        public static readonly ProgramPlan UnloaderPlan = new ProgramPlan(AlwaysOn,
            p => HasCards(p, "PushOutput") && HaulsOf(p, ItemType.CopperOre) > 0,
            Slot("HaulScrap", ItemType.CopperOre, 1), Slot("PushOutput"));

        public static readonly ProgramPlan ZeppelinPlan = new ProgramPlan(AlwaysOn,
            p => HasCards(p, "FreightLaunch") && HaulsOf(p, ItemType.CopperOre) > 0,
            Slot("HaulScrap", ItemType.CopperOre, 1), Slot("FreightLaunch"));

        /// <summary>
        /// One run of <paramref name="card"/>'s recipe's <paramref name="item"/>, from
        /// <paramref name="definitions"/>: the batch a slot feeding that recipe hauls. 0 when the
        /// card or recipe is unknown, which a <see cref="PlanSlot"/> reads as "the card's own batch".
        /// </summary>
        public static int RecipeInput(DefinitionSet definitions, string card, string item)
        {
            RecipeDefinition recipe = definitions?.Appendages != null
                && definitions.Appendages.TryGetValue(card, out AppendageActionDefinition a) ? a.recipe : null;
            return recipe == null ? 0 : LineOf(recipe.inputs, item);
        }

        /// <summary>The iron Presser's Haul takes one run of R2's Scrap (Assemble Scrap Reclamation).</summary>
        public static ProgramPlan IronPresserPlanFor(DefinitionSet definitions) => new ProgramPlan(AlwaysOn,
            p => HasCards(p, "HaulScrap", "AssembleScrapReclamation", "PushOutput"),
            Slot("HaulScrap", quantity: RecipeInput(definitions, "AssembleScrapReclamation", ItemType.Scrap)),
            Slot("AssembleScrapReclamation"), Slot("PushOutput"));

        /// <summary>The coker's Extract takes one run of R1's Coal (Assemble Coking).</summary>
        public static ProgramPlan CokerPlanFor(DefinitionSet definitions) => new ProgramPlan(AlwaysOn,
            p => HasCards(p, "ExtractScrap", "AssembleCoking", "PushOutput"),
            Slot("ExtractScrap", quantity: RecipeInput(definitions, "AssembleCoking", ItemType.Coal)),
            Slot("AssembleCoking"), Slot("PushOutput"));

        /// <summary>The smelter's two Hauls take one run of R4's Scrap and Coke (Assemble Iron Smelting).</summary>
        public static ProgramPlan SmelterPlanFor(DefinitionSet definitions) => new ProgramPlan(AlwaysOn,
            p => HasCards(p, "AssembleIronSmelting", "PushOutput") && HaulsOf(p, ItemType.Scrap) > 0 && HaulsOf(p, ItemType.Coke) > 0,
            Slot("HaulScrap", ItemType.Scrap, RecipeInput(definitions, "AssembleIronSmelting", ItemType.Scrap)),
            Slot("HaulScrap", ItemType.Coke, RecipeInput(definitions, "AssembleIronSmelting", ItemType.Coke)),
            Slot("AssembleIronSmelting"), Slot("PushOutput"));

        /// <summary>
        /// The carrier hauls one burn's worth to the heap: <paramref name="slagPerCoke"/> Slag (the
        /// heap's ratio) and the one Coke that burns them. The Coke is 1 by the ratio's own
        /// definition, Slag per ONE Coke, not by choice.
        /// </summary>
        public static ProgramPlan CarrierPlanFor(int slagPerCoke) => new ProgramPlan(AlwaysOn,
            p => HasCards(p, "PushOutput") && HaulsOf(p, ItemType.Slag) > 0 && HaulsOf(p, ItemType.Coke) > 0,
            Slot("HaulScrap", ItemType.Slag, slagPerCoke), Slot("HaulScrap", ItemType.Coke, CokePerBurn),
            Slot("PushOutput"));

        /// <summary>A Slag Heap's ratio is Slag per one Coke burned.</summary>
        private const int CokePerBurn = 1;

        /// <summary>
        /// The Slag Heap's ratio as this world's build menu places it (placeables.json's
        /// <c>slagPerCoke</c>), or the rule's default without one.
        /// </summary>
        public int HeapSlagPerCoke =>
            _world.Placeables?.FirstOrDefault(p => p.Key == "SlagHeapPrefab")?.Prefab?.GetPart<PlaceableSlagHeap>()?.SlagPerCoke
            ?? SlagHeap.DefaultSlagPerCoke;

        /// <summary>The batch <paramref name="plan"/> sets on its Haul of <paramref name="good"/>.</summary>
        private static int Batch(ProgramPlan plan, string good) => plan.Slots.FirstOrDefault(s => s.Good == good).Quantity;

        /// <summary>
        /// The plan for <paramref name="golem"/>: the first step, from the current one to the end
        /// of its chapter, whose <see cref="TutorialStep.ProgramTarget"/> accepts it. Looking ahead
        /// matters because the station opens the Workbench on a new golem at once: building the
        /// Brass Presser opens it two steps before "Program the Presser". Null with the guide put
        /// away or finished, and for a golem no step means.
        /// </summary>
        public ProgramPlan PlanFor(GolemEntity golem)
        {
            if (golem == null || !IsShowing)
            {
                return null;
            }
            int chapter = ChapterOf(Index);
            for (int i = Index; i < _steps.Count && ChapterOf(i) == chapter; i++)
            {
                TutorialStep step = _steps[i];
                if (step.Program != null && step.ProgramTarget != null && step.ProgramTarget(golem))
                {
                    return step.Program;
                }
            }
            return null;
        }

        private bool IsIronPresser(GolemEntity golem) => IronPresserPlan.Matches(golem.Program);

        /// <summary>The Presser chapter 2 set to make Iron Plate, or null.</summary>
        private GolemEntity IronPresser => Pressers.FirstOrDefault(IsIronPresser);

        /// <summary>
        /// The Presser built last: the one a "program the new Presser" step means. Picking by
        /// program instead ("not the iron Presser") pointed at the WORKING Presser whenever the
        /// player had programmed it any differently from the guide, e.g. cutting Gears instead.
        /// Golems are listed in the order they were built.
        /// </summary>
        private GolemEntity NewestPresser => Pressers.LastOrDefault();

        /// <summary>
        /// What a step calls a card by: its appendage's name, or for a chassis card the chassis's.
        /// </summary>
        public static string CardKey(DraftableCardDefinition card) =>
            card?.appendage?.name ?? card?.chassis?.name;

        private bool HasClaimed(string appendageName)
        {
            string user = _world.Setup?.assemblyLine?.claimUserId;
            return _world.AssemblyLine != null && user != null
                && _world.AssemblyLine.GetClaimedCards(user).Any(c => CardKey(c) == appendageName);
        }

        private string Counts(params (string item, int goal)[] goals) =>
            string.Join("  \u00b7  ", goals.Select(g => Count(g.item, g.goal)));

        // --- Quoted prices come from the data -------------------------------------------------
        //
        // docs/tuning-data-spec.md \u00a712.3 and invariant 12: a text or a count that names a price
        // builds it from the definition, so a retune can't leave the guide quoting the old number.

        private static readonly IReadOnlyList<RecipeIngredient> NoCost = Array.Empty<RecipeIngredient>();

        private IReadOnlyList<RecipeIngredient> ChassisCost(string chassis) =>
            _world.Definitions?.Chassis != null && _world.Definitions.Chassis.TryGetValue(chassis, out ChassisDefinition c) && c.cost != null
                ? c.cost
                : NoCost;

        private IReadOnlyList<RecipeIngredient> PlaceableCost(string key) =>
            _world.Placeables?.FirstOrDefault(p => p.Key == key)?.Prefab?.Cost ?? NoCost;

        /// <summary>How much of <paramref name="item"/> a price asks, summed as the wallet sums it.</summary>
        /// <summary>The Scrap a Scavenger and a Depot cost: what the build steps count toward.</summary>
        private int ScavengerScrap => LineOf(ChassisCost("ClockworkScavenger"), ItemType.Scrap);
        private int DepotScrap => LineOf(PlaceableCost("DepotPrefab"), ItemType.Scrap);

        private static int LineOf(IReadOnlyList<RecipeIngredient> cost, string item) =>
            cost.Where(c => c.itemType == item).Sum(c => c.quantity);

        private static IEnumerable<RecipeIngredient> Lines(IReadOnlyList<RecipeIngredient> cost) =>
            cost.Where(c => c.quantity > 0 && !string.IsNullOrEmpty(c.itemType));

        /// <summary>"60 Scrap + 10 Iron Plate + 5 Gear".</summary>
        private static string Price(IReadOnlyList<RecipeIngredient> cost) =>
            string.Join(" + ", Lines(cost).Select(c => $"{c.quantity} {ItemTiers.DisplayName(c.itemType)}"));

        /// <summary>"6 Mainspring, 8 Lens and 3 Aether Cell".</summary>
        private static string PriceList(IReadOnlyList<RecipeIngredient> cost)
        {
            string[] parts = Lines(cost).Select(c => $"{c.quantity} {ItemTiers.DisplayName(c.itemType)}").ToArray();
            return parts.Length < 2 ? string.Join("", parts) : string.Join(", ", parts.Take(parts.Length - 1)) + " and " + parts[parts.Length - 1];
        }

        /// <summary>A price as the step's progress line counts it, one good at a time.</summary>
        private string CostCounts(IReadOnlyList<RecipeIngredient> cost) =>
            Counts(Lines(cost).GroupBy(c => c.itemType).Select(g => (g.Key, g.Sum(c => c.quantity))).ToArray());

        /// <summary>"2 Copper Ore + 1 Coke \u2192 1 Copper Ingot": a card's recipe, from its definition.</summary>
        private string RecipeLine(string card)
        {
            RecipeDefinition recipe = _world.Definitions?.Appendages != null
                && _world.Definitions.Appendages.TryGetValue(card, out AppendageActionDefinition a) ? a.recipe : null;
            if (recipe == null)
            {
                return "";
            }
            return Price(recipe.inputs) + " \u2192 " + recipe.outputQuantity + " " + ItemTiers.DisplayName(recipe.outputItemType);
        }

        private RecipeDefinition RecipeOf(string card) =>
            _world.Definitions?.Appendages != null
            && _world.Definitions.Appendages.TryGetValue(card, out AppendageActionDefinition a) ? a.recipe : null;

        /// <summary>"1 Coal makes 1 Coke", "2 Iron Plate make 1 Gear": a one-input card's recipe, from its definition.</summary>
        private string Makes(string card)
        {
            RecipeDefinition recipe = RecipeOf(card);
            if (recipe == null || recipe.inputs.Count == 0)
            {
                return "";
            }
            RecipeIngredient input = recipe.inputs[0];
            return $"{input.quantity} {ItemTiers.DisplayName(input.itemType)} {(input.quantity == 1 ? "makes" : "make")} "
                   + $"{recipe.outputQuantity} {ItemTiers.DisplayName(recipe.outputItemType)}";
        }

        /// <summary>"2 Scrap and 1 Coke": what one run of a card's recipe takes.</summary>
        private string Takes(string card)
        {
            RecipeDefinition recipe = RecipeOf(card);
            return recipe == null ? "" : PriceList(recipe.inputs);
        }

        /// <summary>How much of <paramref name="item"/> one run of a card's recipe takes.</summary>
        private int Input(string card, string item) => RecipeInput(_world.Definitions, card, item);

        /// <summary>
        /// The smelter's Scrap and Coke a cycle (R4): what chapter 5 asks the player to set and
        /// feed, read off its plan so the text, the hint and the kit name one number.
        /// </summary>
        private int SmeltScrap => Batch(SmelterPlan, ItemType.Scrap);

        private int SmeltCoke => Batch(SmelterPlan, ItemType.Coke);

        /// <summary>The carrier's Slag and Coke a trip: one burn at the heap, off its plan.</summary>
        private int CarrySlag => Batch(CarrierPlan, ItemType.Slag);

        private int CarryCoke => Batch(CarrierPlan, ItemType.Coke);

        /// <summary>A crate's room per good (sandbox.json).</summary>
        private int CrateHolds => _world.Setup?.crateCapacityPerType ?? 50;

        /// <summary>Stage 1's Frame Sections a minute (clock_tower_stages.json).</summary>
        private int StageOneRate =>
            _world.Definitions?.ClockTowerStages?.Values
                .FirstOrDefault(s => s.stageNumber == 1)?.demands
                .Where(d => d.itemType == ItemType.FrameSection).Sum(d => d.ratePerMinute) ?? 6;

        private IReadOnlyList<RecipeIngredient> PresserCost => ChassisCost("BrassPresser");

        private IReadOnlyList<RecipeIngredient> FirstFloorCost =>
            _world.Setup?.floorExpansion?.purchases?.FirstOrDefault() ?? NoCost;

        private PackUpgradeRung FirstRung => _world.Setup?.packUpgrades?.FirstOrDefault();

        private IReadOnlyList<RecipeIngredient> FirstRungCost => FirstRung?.cost ?? NoCost;

        /// <summary>The Gears the Brass Presser costs: what chapter 2's "gears" step asks for.</summary>
        private int PresserGears => LineOf(PresserCost, ItemType.Gear);

        // Chapter 3 builds a boiler that feeds itself, beside the Coal cart: a Presser extracts
        // Coal from the cart behind it, cokes it, and pushes the Coke straight into a second
        // boiler in front of it. A pipe run from the FIRST boiler, along the carts' row and up
        // one, powers the Presser before the new boiler has any Coke of its own (it starts empty,
        // and a Presser beside only an empty boiler could never make the first Coke), and joins
        // the two boilers into one network, so the coal line keeps the whole factory in steam.
        //
        //                          pipe  boiler 2
        //     boiler 1  pipe ...  pipe  coking Presser
        //                               Coal cart

        private Vector2Int CoalCell => Cart(ItemType.Coal) ?? new Vector2Int(4, 0);

        /// <summary>Where the coking Presser stands: beside the Coal cart.</summary>
        public Vector2Int CokerSpot => Plan.Coker;

        public Facing CokerFacing => Plan.CokerFacing;

        /// <summary>The second boiler, in front of the coking Presser.</summary>
        public Vector2Int Boiler2Spot => Plan.Boiler2;

        /// <summary>
        /// From the first boiler's run to the coking Presser's side, then on to touch the second
        /// boiler. By default, east along the carts' row and up one.
        /// </summary>
        public Vector2Int[] Pipe2Spots => Plan.Pipe2;

        private bool IsCoker(GolemEntity golem) => CokerPlan.Matches(golem.Program);

        /// <summary>
        /// What claiming a card costs in one good right now: its slot's current price if the line
        /// shows it (prices decay), else the card's full claimCost.
        /// </summary>
        private int ClaimCost(string card, string item)
        {
            AssemblyLineState line = _world.AssemblyLine;
            for (int i = 0; line != null && i < line.SlotCount; i++)
            {
                if (CardKey(line.GetCard(i)) == card)
                {
                    return line.GetCurrentCostBundle(i).Where(c => c.itemType == item).Sum(c => c.quantity);
                }
            }
            string deck = _world.Setup?.assemblyLine?.deck;
            DraftableCardDefinition def = deck != null && _world.Definitions.Decks.TryGetValue(deck, out DraftableCardCatalog catalog)
                ? catalog.Cards.FirstOrDefault(c => CardKey(c) == card)
                : null;
            return def?.claimCost?.Where(c => c.itemType == item).Sum(c => c.quantity) ?? 0;
        }

        private int CokingClaimCoal => ClaimCost("AssembleCoking", ItemType.Coal);

        /// <summary>Whether a coker stands where steam reaches it, but every boiler that does is out of Coke.</summary>
        private bool CokerOutOfCoke => Pressers.Any(g => IsCoker(g)
            && _world.Steam.Diagnose(g.GolemId, _world.Clock.CurrentTick) == Steam.SteamShortage.BoilerOutOfCoke);

        /// <summary>The coal line's boiler if it stands on its tile, else the first boiler.</summary>
        private Vector2Int? Boiler2OrFirst =>
            BoilerBuildings.OrderBy(b => b.Cell == Boiler2Spot ? 0 : 1).Select(b => (Vector2Int?)b.Cell).FirstOrDefault();

        /// <summary>Chapter 3's upgrade (supply-design §6): a Coal cart past its first Rate rung.</summary>
        private bool CoalCartUpgraded => _world.Supply?.Carts.Any(c => c.ItemType == ItemType.Coal && c.RateRung >= 1) == true;

        // Chapter 4 copies the first golem's program onto a second Scavenger with a patent, and
        // stands it on the Scrap cart's EAST side, facing east into a third depot -- the first
        // golem faces north, so this one is the lesson in R. Boiler 1 is right above it.
        //
        //     golem 1   boiler 1
        //     Scrap     golem 2  -> depot 3

        /// <summary>The second Scavenger: on another side of the Scrap cart (east by default).</summary>
        public Vector2Int Scav2Spot => Plan.Scav2;

        public Facing Scav2Facing => Plan.Scav2Facing;

        /// <summary>In front of the second Scavenger.</summary>
        public Vector2Int Depot3Spot => Plan.Depot3;

        private IEnumerable<GolemEntity> Scavengers =>
            LiveGolems.Where(g => g.Program?.chassis != null && g.Program.chassis.name == "ClockworkScavenger");

        /// <summary>The Scavenger that is not the first one: chapter 4's.</summary>
        private GolemEntity SecondScavenger => Role("scav2");

        // Chapter 5 smelts: an Aether-Hauler hauls Scrap AND Coke (the Haul good picker) and runs
        // R4, which makes Slag whether you want it or not. A carrier Presser hauls the Slag, with
        // the Coke the heap burns to void it (1 per 4), into a Slag Heap. All of it stands in a
        // column east of the coal line's boiler, steamed by a pipe run straight off it.
        //
        //     Slag Heap
        //     carrier    pipe
        //     depot B    pipe        boiler 2 is west of the bottom pipe
        //     smelter    pipe
        //     depot A

        /// <summary>The Aether-Hauler that smelts: two east of the coal line's boiler by default.</summary>
        public Vector2Int SmelterSpot => Plan.Smelter;

        public Facing SmelterFacing => Plan.SmelterFacing;

        /// <summary>Behind the smelter: its Scrap and Coke, put into the crate by hand (smelt-feed).</summary>
        public Vector2Int SmeltInSpot => Plan.SmeltIn;

        /// <summary>In front of the smelter, behind the carrier: Iron Plate and Slag land here.</summary>
        public Vector2Int SmeltOutSpot => Plan.SmeltOut;

        /// <summary>The carrier Presser, beyond depot B.</summary>
        public Vector2Int CarrierSpot => Plan.Carrier;

        public Facing CarrierFacing => Plan.CarrierFacing;

        /// <summary>The Slag Heap, in front of the carrier.</summary>
        public Vector2Int SlagHeapSpot => Plan.SlagHeap;

        /// <summary>Pipe to the smelter's and the carrier's sides: by default, up the column's west side.</summary>
        public Vector2Int[] Pipe3Spots => Plan.Pipe3;

        private IEnumerable<GolemEntity> Haulers =>
            LiveGolems.Where(g => g.Program?.chassis != null && g.Program.chassis.name == "AetherHauler");

        private bool IsSmelter(GolemEntity golem) => SmelterPlan.Matches(golem.Program);

        private bool IsCarrier(GolemEntity golem) => CarrierPlan.Matches(golem.Program);

        // Chapter 6 moves goods with a belt and sorts them with a label, at the Copper Ore cart: an
        // extractor pushes ore onto a belt run, an unloader hauls it off the end into a depot
        // labelled Copper Ore. A long pipe run, laid by dragging, powers both and joins the top of
        // chapter 5's column.
        //
        //     labelled depot
        //     unloader      pipe
        //     belt ^        pipe
        //     belt ^        pipe        <- pipe continues to chapter 5's column
        //     belt ^        pipe
        //     extractor     pipe
        //     Copper Ore cart

        private Vector2Int CopperCell => Cart(ItemType.CopperOre) ?? new Vector2Int(8, 0);

        /// <summary>The copper extractor, beside the Copper Ore cart (north by default).</summary>
        public Vector2Int ExtractorSpot => Plan.Extractor;

        /// <summary>Which way the copper line runs: away from the cart, along the belt.</summary>
        public Facing CopperFacing => Plan.CopperFacing;

        /// <summary>The belt run from the extractor, three cells.</summary>
        public Vector2Int[] BeltSpots => Plan.Belts;

        /// <summary>The unloader, at the belt's end.</summary>
        public Vector2Int UnloaderSpot => Plan.Unloader;

        /// <summary>The labelled depot, in front of the unloader.</summary>
        public Vector2Int CopperDepotSpot => Plan.CopperDepot;

        /// <summary>
        /// Pipe to the extractor's and the unloader's sides. By default up the copper line's west
        /// side, then west and down to the top of chapter 5's pipes.
        /// </summary>
        public Vector2Int[] Pipe4Spots => Plan.Pipe4;

        private GolemEntity CopperExtractor => Role("extractor");

        private GolemEntity CopperUnloader => Role("unloader");

        private bool BeltsLaid => BeltSpots.All(c => _world.Belts.TryGetBelt(c, out PlacedBelt belt) && belt.Facing == CopperFacing);

        private PlaceableDepot DepotAt(Vector2Int cell) =>
            Built.FirstOrDefault(b => b.Cell == cell)?.GetPart<PlaceableDepot>();

        private ICrate CrateOn(Vector2Int cell) => Built.FirstOrDefault(b => b.Cell == cell)?.GetPart<ICrate>();

        private int InCrate(Vector2Int cell, string item) => CrateOn(cell)?.Buffer?.GetQuantity(item) ?? 0;

        private string CrateCount(Vector2Int cell, string item, int goal) =>
            $"{ItemTiers.DisplayName(item)}  {Math.Min(InCrate(cell, item), goal)} / {goal}";

        /// <summary>"Scrap in the crate  12": what a collect step would take.</summary>
        private string InTheCrate(Vector2Int cell, string item) => $"{ItemTiers.DisplayName(item)} in the crate  {InCrate(cell, item)}";

        /// <summary>The first golem's depot: the one on its marked tile, else the first standing.</summary>
        private PlaceableBuilding FirstDepot => Built.Where(IsDepot).OrderBy(b => b.Cell == DepotSpot ? 0 : 1).FirstOrDefault();

        private PlaceableBuilding MastBuilding => Built.FirstOrDefault(b => b.GetPart<PlaceableFreightMast>() != null);

        /// <summary>Whether anything was ever taken out of <paramref name="building"/>'s crate: E, a click or a golem's haul.</summary>
        private static bool Withdrawn(PlaceableBuilding building, string item) =>
            building?.GetPart<ICrate>()?.Buffer?.TotalWithdrawn(item) > 0;

        /// <summary>
        /// Chapter 8's shape: claim a recipe card, then make the good. No marked tiles -- by now the
        /// player has built every kind of line the recipe needs, and choosing where is the game.
        /// Done once the factory has EVER made the good (the tech tree's ledger, which only
        /// grows), the same question the Assembly Line asks of a card's price.
        /// </summary>
        private TutorialStep Goal(string id, string title, string body, string good, string card, string cart = null) =>
            new TutorialStep(
                id, title, body,
                w => _world.TechTree.Ledger.HasItem(good),
                // A good that comes by cart: its cart, or the spot marked for it until one stands.
                w => cart != null ? Cart(cart) : null,
                // The recipe line from the card's own recipe (tuning-data-spec §12.3), not a copy.
                w => (HasClaimed(card) ? "Card claimed" : "Claim the card") + "  ·  " + RecipeLine(card),
                card: card,
                workbench: true,
                spot: cart != null ? () => CartOf(cart) == null ? Home(cart) : null : (Func<Vector2Int?>)null);

        // Chapter 9 flies the Copper home. The Zeppelin stands on the labelled Copper depot's far
        // side with the depot behind it, hauls ore out and launches it to a Freight Mast, which lands it in its
        // own crate (mast-collect). One more pipe, on the copper run's top, gives it steam.
        //
        //     pipe  Zeppelin (faces up, depot behind)              Freight Mast
        //     pipe  labelled Copper depot

        /// <summary>The Zeppelin's tile: just past the labelled Copper depot.</summary>
        public Vector2Int ZeppelinSpot => Plan.Zeppelin;

        /// <summary>The pipe that steams it: by default one tile, on top of chapter 6's run.</summary>
        public Vector2Int[] ZeppelinPipeSpots => Plan.ZeppelinPipe;

        /// <summary>The first of <see cref="ZeppelinPipeSpots"/>, or the Zeppelin's tile if steam already reaches it.</summary>
        public Vector2Int ZeppelinPipeSpot => ZeppelinPipeSpots.Length > 0 ? ZeppelinPipeSpots[0] : ZeppelinSpot;

        /// <summary>Where the guide suggests the mast: a few tiles off, across open floor.</summary>
        public Vector2Int MastSpot => Plan.Mast;

        private GolemEntity Zeppelin =>
            LiveGolems.FirstOrDefault(g => g.Program?.chassis != null && g.Program.chassis.name == "ZeppelinFreightLoader");

        private bool HasMast => Built.Any(b => b.GetPart<PlaceableFreightMast>() != null);

        /// <summary>
        /// Two free tiles for the kit's Post crates (chapter 8 marks nothing, so the player's go
        /// anywhere): the first pair the layout's free-line search finds off every chapter's tiles.
        /// </summary>
        public Vector2Int[] PostSpots => Plan.Post;

        private static bool IsPostCrate(PlaceableBuilding b) => b.GetPart<PlaceableDepot>()?.BufferId == SandboxWorld.PostBufferId;

        private int PostCrates => Built.Count(IsPostCrate);

        /// <summary>Whether the player is standing in the town square (G10).</summary>
        private bool PlayerInSquare
        {
            get
            {
                return TownSquare.Contains(_world.Interactor.PlayerCell);
            }
        }

        private ClockTower.ClockTowerSite TowerSite => _world.ClockTower.Site;

        private SlagHeap HeapAt(Vector2Int cell) =>
            Built.FirstOrDefault(b => b.Cell == cell)?.GetPart<PlaceableSlagHeap>()?.Heap;

        private IEnumerable<PlaceableBuilding> Built => _world.Build.Buildings.Where(b => !b.IsRemoved);

        private IEnumerable<PlaceableBuilding> BoilerBuildings => Built.Where(b => b.GetPart<PlaceableBoiler>() != null);

        private IEnumerable<PlaceableBoiler> Boilers => BoilerBuildings.Select(b => b.GetPart<PlaceableBoiler>());

        private IEnumerable<GolemEntity> LiveGolems => _world.Golems.Where(g => g != null && !g.IsRemoved);

        private static bool IsProgrammed(GolemEntity golem) => ScavengerPlan.Matches(golem.Program);

        private Vector2Int? FirstGolem => LiveGolems.Select(g => (Vector2Int?)g.Cell).FirstOrDefault();

        private List<TutorialStep> BuildSteps() => new List<TutorialStep>
        {
            new TutorialStep(
                "scrap", "Gather Scrap",
                "Walk with [[move]] to the Scrap cart the arrow points at. Press [[interact]] to take Scrap; hold [[interact]] to keep "
                + "taking. Scrap pays for almost everything at first, and the cart refills itself.",
                w => Stock(ItemType.Scrap) >= 20,
                w => Cart(ItemType.Scrap),
                w => Count(ItemType.Scrap, 20)),

            new TutorialStep(
                "coal", "Buy a Coal cart",
                $"Coal comes by cart. At the marked spot, press [[interact]] and add a Coal cart ({Price(CartPrice(ItemType.Coal))}). "
                + "It fills by itself: press [[interact]] at it to take 5 Coal.",
                w => Stock(ItemType.Coal) >= 5,
                w => CartOrScrap(ItemType.Coal),
                w => CartOf(ItemType.Coal) == null ? "Add the Coal cart" : Count(ItemType.Coal, 5),
                spot: () => CartOf(ItemType.Coal) == null ? Home(ItemType.Coal) : null),

            new TutorialStep(
                "coke", "Make Coke",
                $"At the Hand-Crank Bench, hold [[interact]] to crank R1 Coking: {Makes("AssembleCoking")}. Boilers burn Coke.",
                w => Stock(ItemType.Coke) >= 5,
                w => Bench,
                w => Count(ItemType.Coke, 5)),

            new TutorialStep(
                "iron", "Make Iron Plate",
                $"Press [[rotate]] at the bench to switch it to R2 Scrap Reclamation ({Makes("AssembleScrapReclamation")}), then "
                + "hold [[interact]]. Holding a building? Escape puts it down first.",
                w => Stock(ItemType.IronPlate) >= 10,
                w => ScrapFirst(1, Bench),
                w => Count(ItemType.IronPlate, 10)),

            new TutorialStep(
                "boiler", "Build a Boiler",
                $"Golems run on steam. Click Boiler in the build menu ({Price(PlaceableCost("BoilerPrefab"))}), click the marked "
                + "tile beside the Scrap cart, then press Escape.",
                // Its own layout counts too, if steam from it already reaches the golem's tile.
                w => BoilerBuildings.Any(b => b.Cell == BoilerSpot) || Plan.Golem1Steamable,
                w => ScrapFirst(LineOf(PlaceableCost("BoilerPrefab"), ItemType.Scrap), BoilerSpot),
                w => Count(ItemType.Scrap, LineOf(PlaceableCost("BoilerPrefab"), ItemType.Scrap)),
                menuKey: "BoilerPrefab",
                spot: () => BoilerSpot,
                builds: IsBoiler, exactTile: true),

            new TutorialStep(
                "fuel", "Fuel the Boiler",
                "Stand at the boiler and press [[interact]] to load your Coke. No Coke, no steam.",
                w => Boilers.Any(b => b.Boiler != null && b.Boiler.CokeStock > 0),
                w => BoilerBuildings.OrderBy(b => b.Cell == BoilerSpot ? 0 : 1).Select(b => (Vector2Int?)b.Cell).FirstOrDefault()),

            new TutorialStep(
                "golem", "Build a Golem",
                $"At the construction station, press [[interact]] and pick the Clockwork Scavenger ({Price(ChassisCost("ClockworkScavenger"))}). The Workbench "
                + "opens on it.",
                w => LiveGolems.Any(),
                w => ScrapFirst(ScavengerScrap, Station),
                w => Count(ItemType.Scrap, ScavengerScrap)),

            new TutorialStep(
                "program", "Program it",
                "Drag the glowing card onto the glowing socket, then the next: Always On, Extract, Push Output. "
                + "Then pull ENGAGE GEARS. Closed it? Press [[interact]] at the golem.",
                w => LiveGolems.Any(IsProgrammed),
                w => FirstGolem,
                workbench: true,
                program: ScavengerPlan,
                programTarget: g => g == (Role("first") ?? Scavengers.FirstOrDefault())),

            new TutorialStep(
                "depot", "Build a Depot",
                $"A golem takes from the tile behind it and gives to the tile in front. Build a Depot ({Price(PlaceableCost("DepotPrefab"))}) on "
                + $"the marked tile: a crate of {CrateHolds} of each good. Misplaced? X takes it back, full refund.",
                w => Built.Any(b => b.Cell == DepotSpot && b.GetPart<PlaceableDepot>() != null),
                w => ScrapFirst(DepotScrap, DepotSpot),
                w => Count(ItemType.Scrap, DepotScrap),
                menuKey: "DepotPrefab",
                spot: () => DepotSpot,
                builds: IsDepot, exactTile: true),

            new TutorialStep(
                "work", "Put it to work",
                "Press [[carry]] by the golem to lift it. The outline under you is where it lands: [[source-tile]] is behind it, [[target-tile]] "
                + "in front. Stand on the marked tile, press [[rotate]] until [[target-tile]] is on the depot, then [[carry]].",
                w => _cycleSeen,
                w => LiveGolems.Any(g => g.Cell == GolemSpot) ? GolemSpot
                    : LiveGolems.Where(IsProgrammed).Select(g => (Vector2Int?)g.Cell).FirstOrDefault() ?? GolemSpot,
                spot: () => GolemSpot,
                spotFacing: () => GolemFacing),

            // crates-design §8: the verb the whole game runs on, taught where it is simplest. Any
            // withdrawal counts (E, the crate screen), and nothing else in chapter 1 takes from it.
            new TutorialStep(
                "collect", "Collect from the crate",
                "Your golem fills the depot's crate, not your pack. Stand at the depot and press [[interact]]: you take what "
                + "fits, the rest stays. Hover any golem to see its [[source-tile]] and [[target-tile]] tiles.",
                w => Withdrawn(FirstDepot, ItemType.Scrap),
                w => FirstDepot?.Cell ?? DepotSpot,
                w => FirstDepot == null ? "" : InTheCrate(FirstDepot.Cell, ItemType.Scrap)),

            // --- Chapter 2: the first machine that CRAFTS --------------------------------------
            // progression-design §9 Phase 1's goal and Phase 2's lesson: a Scavenger gathers but
            // cannot craft; the Brass Presser can, and running it needs the first steam pipe.

            new TutorialStep(
                "gears", "Cut Gears",
                $"The Brass Presser crafts, and costs {PresserGears} Gears. Press [[rotate]] at the bench until it shows R8 Gear Cutting "
                + $"({Makes("AssembleGearCutting")}), then hold [[interact]]. Short of Plate? Back to R2.",
                // The Presser's own Gear line, not a copy of it (tuning-data-spec §12.3).
                w => Stock(ItemType.Gear) >= PresserGears,
                w => ScrapFirst(1, Bench),
                w => Counts((ItemType.Gear, PresserGears), (ItemType.IronPlate, Input("AssembleGearCutting", ItemType.IronPlate)))),

            new TutorialStep(
                "claim", "Claim Scrap Reclamation",
                "A golem can only use cards you own. Press Tab, open the Assembly Line and click Claim on "
                + $"Assemble Scrap Reclamation ({ClaimCost("AssembleScrapReclamation", ItemType.Scrap)} Scrap): Scrap in, Iron Plate out.",
                w => HasClaimed("AssembleScrapReclamation"),
                w => null,
                card: "AssembleScrapReclamation"),

            new TutorialStep(
                "presser", "Build a Brass Presser",
                $"At the station, build the Brass Presser ({Price(PresserCost)}). Short of Scrap? "
                + "Collect from your Scavenger's crate. Program it now if you like: the cards glow.",
                w => Pressers.Any(),
                w => ScrapFirst(LineOf(PresserCost, ItemType.Scrap), Station),
                w => CostCounts(PresserCost)),

            new TutorialStep(
                "pipe", "Lay a steam pipe",
                $"The Presser will stand two tiles from the boiler, out of its reach. Lay Steam Pipe ({Price(PlaceableCost("SteamPipePrefab"))} "
                + "each) on the marked tiles: steam reaches any golem beside a pipe.",
                w => SteamReachesPresserSpot,
                // The route can be empty (steam already there, or nothing to build it from).
                w => PipeSpots.Length > 0 ? PipeSpots[0] : PresserSpot,
                menuKey: "SteamPipePrefab",
                moreSpots: () => PipeSpots,
                builds: IsPipe),

            new TutorialStep(
                "depot2", "A depot for its output",
                "Build a second Depot on the marked tile. The Presser will take Scrap from the first crate, behind "
                + "it, and push Iron Plate into this one.",
                w => Built.Any(b => b.Cell == PresserDepotSpot && b.GetPart<PlaceableDepot>() != null),
                w => ScrapFirst(DepotScrap, PresserDepotSpot),
                w => Count(ItemType.Scrap, DepotScrap),
                menuKey: "DepotPrefab",
                spot: () => PresserDepotSpot,
                builds: IsDepot, exactTile: true),

            new TutorialStep(
                "program-presser", "Program the Presser",
                "Drag the glowing cards: Always On, Haul, Assemble Scrap Reclamation, Push Output. Then ENGAGE. "
                + "Closed it? Press [[interact]] at the Presser.",
                w => Pressers.Any(IsIronPresser),
                w => Pressers.Select(g => (Vector2Int?)g.Cell).FirstOrDefault(),
                workbench: true,
                program: IronPresserPlan,
                programTarget: g => g == Pressers.FirstOrDefault()),

            new TutorialStep(
                "work-presser", "Make Iron Plate",
                "Lift the Presser ([[carry]]) onto the marked tile between the depots: [[source-tile]] on the first, [[target-tile]] on the new "
                + "one ([[rotate]] turns it), then [[carry]]. Grey means it is waiting for Scrap. That's fine.",
                w => Pressers.Any(g => _completedSinceEntry.Contains(g.GolemId)),
                w => Pressers.Any(g => g.Cell == PresserSpot) ? PresserSpot
                    : Pressers.Select(g => (Vector2Int?)g.Cell).FirstOrDefault() ?? PresserSpot,
                spot: () => PresserSpot,
                spotFacing: () => PresserFacing),

            // --- Chapter 3: the Coke line ---------------------------------------------------------
            // progression-design §9 Phase 2's wall: golems burn Coke, and the boiler's runs out.
            // The answer is a coal line that feeds the boiler it runs on.

            new TutorialStep(
                "coking-card", "Claim Coking",
                "Your boiler burns Coke you crank, and it will run dry. Press Tab, open the Assembly Line and claim "
                + "Assemble Coking. It costs Coal: short? Take some at the Coal cart.",
                w => HasClaimed("AssembleCoking"),
                // Chapter 1 cranks every Coal into Coke, so the stockpile is usually out.
                w => Stock(ItemType.Coal) < CokingClaimCoal ? Cart(ItemType.Coal) : null,
                w => Count(ItemType.Coal, CokingClaimCoal),
                card: "AssembleCoking"),

            new TutorialStep(
                "presser2", "A second Presser",
                $"Build another Brass Presser ({Price(PresserCost)}). Take Plate from the first "
                + "Presser's crate ([[interact]]); crank the Gears (R8). A full crate turns its golem red.",
                w => Pressers.Count() >= 2,
                w => ScrapFirst(LineOf(PresserCost, ItemType.Scrap), Station),
                w => CostCounts(PresserCost)),

            new TutorialStep(
                "boiler2", "A boiler for the coal line",
                "Build a second Boiler on the marked tile, two out from the Coal cart. The coking Presser will "
                + "stand between them and push Coke into it.",
                w => BoilerBuildings.Any(b => b.Cell == Boiler2Spot),
                w => ScrapFirst(LineOf(PlaceableCost("BoilerPrefab"), ItemType.Scrap), Boiler2Spot),
                w => CostCounts(PlaceableCost("BoilerPrefab")),
                menuKey: "BoilerPrefab",
                spot: () => Boiler2Spot,
                builds: IsBoiler, exactTile: true),

            new TutorialStep(
                "pipes2", "Join the boilers",
                "Lay Steam Pipe on the marked tiles, from your first boiler to the new one. Joined boilers share "
                + "steam: the first powers the coking Presser until the new one has Coke, then the new one powers "
                + "the rest. Your own route counts too.",
                // The OUTCOME, not the marked tiles: any route that does both jobs the body names.
                w => FirstBoilerPowersCokerAndJoins,
                w => Pipe2Spots.Where(c => !_world.Steam.HasPipe(c)).Select(c => (Vector2Int?)c).FirstOrDefault(),
                w => PipesLeft(Pipe2Spots),
                menuKey: "SteamPipePrefab",
                moreSpots: () => Pipe2Spots,
                builds: IsPipe),

            // The step keeps its old id: a save names it (supply-design §6).
            new TutorialStep(
                "coal-order", "Upgrade the Coal cart",
                "Your coker will eat nearly all the Coal your cart brings. At the Coal cart, press [[inventory]] and buy Rate "
                + $"({Price(FirstRatePrice(ItemType.Coal))}): {RateAt(ItemType.Coal, 1)} Coal a minute, not {RateAt(ItemType.Coal, 0)}.",
                w => CoalCartUpgraded,
                w => Cart(ItemType.Coal),
                w => CostCounts(FirstRatePrice(ItemType.Coal))),

            new TutorialStep(
                "program-coker", "Program the coking Presser",
                "Drag the glowing cards: Always On, Extract, Assemble Coking, Push Output, then ENGAGE. Extract "
                + "takes whatever the cart behind holds: here, Coal.",
                w => Pressers.Any(IsCoker),
                w => NewestPresser?.Cell,
                workbench: true,
                program: CokerPlan,
                // The newest Presser that isn't the iron one: looking ahead from chapter 3's first
                // step, the newest Presser is still the iron Presser, and the hint must not ask
                // the player to turn it into a coker.
                programTarget: g => g == Pressers.LastOrDefault(p => p != IronPresser)),

            new TutorialStep(
                "work-coker", "Feed the boiler",
                "Lift the coking Presser onto the marked tile: [[source-tile]] on the Coal cart, [[target-tile]] on the new boiler. "
                + "Boilers dry? Load Coke into the new one with [[interact]].",
                w => Pressers.Any(g => IsCoker(g) && _completedSinceEntry.Contains(g.GolemId)),
                // A coker on dry boilers can never make the Coke that would fuel them, so point at
                // the new boiler (right in front of it) until one has Coke again.
                w => CokerOutOfCoke ? Boiler2OrFirst
                    : Pressers.Any(g => IsCoker(g) && g.Cell == CokerSpot) ? CokerSpot
                    : Pressers.Where(IsCoker).Select(g => (Vector2Int?)g.Cell).FirstOrDefault() ?? CokerSpot,
                w => CokerOutOfCoke ? "Boilers dry: load Coke  " + Count(ItemType.Coke, 1) : "",
                spot: () => CokerSpot,
                spotFacing: () => CokerFacing),

            // --- Chapter 4: copies, turning, and a stall on purpose --------------------------------
            // The playtest script's Part C: a patent stamped onto a second golem, R to turn it, and
            // a golem stalled deliberately to see its badge name the problem (Part I1).

            new TutorialStep(
                "patent", "Patent a program",
                "A patent saves a program to reuse. Press [[interact]] at your first Scavenger and press PATENT in the "
                + "Workbench.",
                w => _world.Patents.Blueprints.Count > 0,
                w => FirstGolem,
                workbench: true),

            // A patent is used at the construction station, which builds its chassis with the
            // program already engaged. It used to be loaded from Management's Patents tab into
            // a bare golem's Workbench, and Tab does nothing while the Workbench is open, so the
            // step's own instructions could not be followed.
            new TutorialStep(
                "scav2", "Build one from the patent",
                $"At the station, under From a patent, click your patent: a Scavenger ({Price(ChassisCost("ClockworkScavenger"))}) arrives already "
                + "programmed.",
                w => SecondScavenger != null,
                w => ScrapFirst(ScavengerScrap, Station),
                w => Count(ItemType.Scrap, ScavengerScrap)),

            // Passed at once when the Scavenger was built from the patent; it only shows for
            // a golem built bare.
            new TutorialStep(
                "stamp", "Program the new Scavenger",
                "This one arrived bare. Drag the glowing cards (Always On, Extract, Push Output) and pull ENGAGE. "
                + "Next time, build from the patent.",
                w => SecondScavenger != null && IsProgrammed(SecondScavenger),
                w => SecondScavenger != null ? SecondScavenger.Cell : (Vector2Int?)null,
                workbench: true,
                program: ScavengerPlan,
                programTarget: g => g == SecondScavenger),

            new TutorialStep(
                "depot3", "A depot beside the cart",
                "Your new Scavenger needs a crate to fill, like your first one has. Build a Depot on the marked "
                + "tile, two tiles out from another side of the Scrap cart. The new Scavenger will stand on the "
                + "empty tile between them.",
                w => Built.Any(b => b.Cell == Depot3Spot && b.GetPart<PlaceableDepot>() != null),
                w => ScrapFirst(DepotScrap, Depot3Spot),
                w => Count(ItemType.Scrap, DepotScrap),
                menuKey: "DepotPrefab",
                spot: () => Depot3Spot,
                builds: IsDepot, exactTile: true),

            new TutorialStep(
                "turn", "Turn it with [[rotate]]",
                "Lift the new Scavenger ([[carry]]) and stand on the marked tile. While you carry it, [[rotate]] turns it: turn "
                + "until [[source-tile]] is on the cart and [[target-tile]] on the depot, then set it down ([[carry]]).",
                w => SecondScavenger != null && _completedSinceEntry.Contains(SecondScavenger.GolemId),
                w => SecondScavenger != null && SecondScavenger.Cell != Scav2Spot ? SecondScavenger.Cell : Scav2Spot,
                spot: () => Scav2Spot,
                spotFacing: () => Scav2Facing),

            new TutorialStep(
                "stall", "Stall it on purpose",
                "Golems never improvise. Stand by the new Scavenger and press [[rotate]] to turn it away. It stops and "
                + "turns red. Hover it: it names the tile and what is wrong.",
                // Any stall of chapter 4's golem: which way the player turned it decides the
                // reason, and every reason's badge names the problem.
                w => SecondScavenger != null && SecondScavenger.Program.State == GolemState.Stalled,
                w => SecondScavenger?.Cell),

            new TutorialStep(
                "unstall", "And back to work",
                "Turn it back ([[rotate]]) until [[source-tile]] is on the cart. A stuck golem retries the same step, and starts "
                + "again the moment its tile is right.",
                // The golem chapter 4 built, by its saved role: a load on this step used to leave the
                // stalled golem's id behind in the last session, and the step could never finish.
                w => SecondScavenger != null && _completedSinceEntry.Contains(SecondScavenger.GolemId),
                w => SecondScavenger?.Cell,
                spot: () => Scav2Spot,
                spotFacing: () => Scav2Facing),

            // --- Chapter 5: metal, and its Slag ---------------------------------------------------
            // progression-design Phase 4: the Aether-Hauler opens two-input recipes, and R4 Iron
            // Smelting makes Slag every cycle, which must be routed or the line stalls.

            new TutorialStep(
                "r4-card", "Claim Iron Smelting",
                "Iron Smelting makes twice the Iron Plate from the same Scrap, but it needs Coke too: two "
                + "inputs. Press Tab, open the Assembly Line and claim Assemble Iron Smelting.",
                w => HasClaimed("AssembleIronSmelting"),
                w => null,
                card: "AssembleIronSmelting"),

            new TutorialStep(
                "hauler", "Build an Aether-Hauler",
                "Two inputs need a bigger frame. Build an Aether-Hauler at the station "
                + $"({Price(ChassisCost("AetherHauler"))}): four slots, enough for two Hauls, an Assemble and a Push.",
                w => Haulers.Any(),
                w => Station,
                w => CostCounts(ChassisCost("AetherHauler"))),

            new TutorialStep(
                "smelt-depots", "Depots for the smelter",
                "Build two Depots on the marked tiles, two apart with a free tile between: one behind the smelter "
                + "for its Scrap and Coke, which you fill by hand, one in front for its Iron Plate. A pair of your "
                + "own works too.",
                w => new[] { SmeltInSpot, SmeltOutSpot }.All(c => Built.Any(b => b.Cell == c && b.GetPart<PlaceableDepot>() != null)),
                w => new[] { SmeltInSpot, SmeltOutSpot }.Where(c => !Built.Any(b => b.Cell == c)).Select(c => (Vector2Int?)c).FirstOrDefault(),
                w => Count(ItemType.Scrap, 30),
                menuKey: "DepotPrefab",
                moreSpots: () => new[] { SmeltInSpot, SmeltOutSpot },
                builds: IsDepot, exactTile: true),

            new TutorialStep(
                "pipes3", "Steam for the column",
                "Lay Steam Pipe on the marked tiles, from your steam to the smelter's and the carrier's tiles, "
                + "which you'll fill next. Any route that gets steam to both their tiles counts.",
                w => SteamReaches(SmelterSpot) && SteamReaches(CarrierSpot),
                w => Pipe3Spots.Where(c => !_world.Steam.HasPipe(c)).Select(c => (Vector2Int?)c).FirstOrDefault(),
                w => PipesLeft(Pipe3Spots),
                menuKey: "SteamPipePrefab",
                moreSpots: () => Pipe3Spots,
                builds: IsPipe),

            new TutorialStep(
                "program-smelter", "Two Hauls, two goods",
                "In the Workbench: Always On, then Haul, Haul, Assemble Iron Smelting and Push Output. On the "
                + $"second Haul, click the arrow until it says Coke. Set the first to {SmeltScrap}"
                + (SmeltCoke > 1 ? $" and the second to {SmeltCoke}" : "")
                + $": the recipe takes {Takes("AssembleIronSmelting")}.",
                w => Haulers.Any(IsSmelter),
                w => Haulers.Select(g => (Vector2Int?)g.Cell).FirstOrDefault(),
                workbench: true,
                program: SmelterPlan,
                programTarget: g => g == Haulers.FirstOrDefault()),

            // Nothing brings Scrap or Coke to this crate yet, so the player does: the crate screen.
            new TutorialStep(
                "smelt-feed", "Feed the smelter",
                "No golem brings Scrap or Coke to the smelter's crate yet, so you do. Stand at the depot behind the "
                + "smelter's tile, press [[inventory]], and Shift-click Scrap, then Coke, in your pack: Shift-click moves all "
                + $"that fits, a click moves one. Each cycle takes {Takes("AssembleIronSmelting")}.",
                w => (InCrate(SmeltInSpot, ItemType.Scrap) >= SmeltScrap && InCrate(SmeltInSpot, ItemType.Coke) >= SmeltCoke)
                    || Haulers.Any(g => IsSmelter(g) && _completedSinceEntry.Contains(g.GolemId)),
                w => SmeltInSpot,
                w => CrateCount(SmeltInSpot, ItemType.Scrap, SmeltScrap) + "  \u00b7  " + CrateCount(SmeltInSpot, ItemType.Coke, SmeltCoke),
                crate: true),

            new TutorialStep(
                "work-smelter", "Smelt",
                "Put the Aether-Hauler on the marked tile between the two depots, facing the second. Watch what it "
                + $"pushes: Iron Plate, and Slag with it. Its crate holds {CrateHolds} of each, and a full Slag slot stops it.",
                w => Haulers.Any(g => IsSmelter(g) && _completedSinceEntry.Contains(g.GolemId)),
                w => Haulers.Any(g => g.Cell == SmelterSpot) ? SmelterSpot
                    : Haulers.Where(IsSmelter).Select(g => (Vector2Int?)g.Cell).FirstOrDefault() ?? SmelterSpot,
                spot: () => SmelterSpot,
                spotFacing: () => SmelterFacing),

            new TutorialStep(
                "slag-heap", "Somewhere for the Slag",
                "Slag piles up every cycle, and a full store stalls the smelter. Build a Slag Heap on the "
                + $"marked tile ({Price(PlaceableCost("SlagHeapPrefab"))}): it burns 1 Coke for every {HeapSlagPerCoke} Slag.",
                w => HeapAt(SlagHeapSpot) != null,
                w => ScrapFirst(LineOf(PlaceableCost("SlagHeapPrefab"), ItemType.Scrap), SlagHeapSpot),
                w => CostCounts(PlaceableCost("SlagHeapPrefab")),
                menuKey: "SlagHeapPrefab",
                spot: () => SlagHeapSpot,
                builds: b => b.GetPart<PlaceableSlagHeap>() != null, exactTile: true),

            new TutorialStep(
                "carrier", "A carrier",
                "Build another Brass Presser at the station. It will make nothing: it carries.",
                w => Pressers.Count() >= 3,
                w => ScrapFirst(LineOf(PresserCost, ItemType.Scrap), Station),
                w => CostCounts(PresserCost)),

            new TutorialStep(
                "program-carrier", "Carry Slag and its fuel",
                $"Program it: Always On, Haul set to Slag with the dial at {CarrySlag}, Haul set to Coke at {CarryCoke}, then Push "
                + $"Output. {CarrySlag} Slag and the one Coke that burns them, every trip.",
                w => Pressers.Any(IsCarrier),
                w => NewestPresser?.Cell,
                workbench: true,
                program: CarrierPlan,
                // The newest Presser that is neither the iron Presser nor the coker: looking ahead
                // from chapter 5's first step, the newest Presser is still the coker.
                programTarget: g => g == Pressers.LastOrDefault(p => p != IronPresser && !IsCoker(p))),

            new TutorialStep(
                "carrier-feed", "Coke for the carrier",
                "The carrier hauls from the smelter's output crate, and the smelter pushes Slag there but no Coke. "
                + "Stand at that depot, press [[inventory]], and Shift-click Coke in your pack to move all that fits.",
                w => InCrate(SmeltOutSpot, ItemType.Coke) >= CarryCoke
                    || Pressers.Any(g => IsCarrier(g) && _completedSinceEntry.Contains(g.GolemId)),
                w => SmeltOutSpot,
                w => CrateCount(SmeltOutSpot, ItemType.Coke, CarryCoke),
                crate: true),

            new TutorialStep(
                "work-carrier", "Clear the Slag",
                "Put the carrier on the marked tile between depot and heap, facing the heap. When the heap starts "
                + "burning Slag, the iron line is safe.",
                // Any heap: the carrier's job is done wherever the heap stands.
                w => Built.Any(b => b.GetPart<PlaceableSlagHeap>()?.Heap?.TotalVoided > 0),
                w => Pressers.Any(g => IsCarrier(g) && g.Cell == CarrierSpot) ? CarrierSpot
                    : Pressers.Where(IsCarrier).Select(g => (Vector2Int?)g.Cell).FirstOrDefault() ?? CarrierSpot,
                spot: () => CarrierSpot,
                spotFacing: () => CarrierFacing),

            // --- Chapter 6: belts, and a label ---------------------------------------------------
            // The playtest script's belt and labelled-depot items (F, I2): a belt carries goods a
            // golem pushes onto it, a golem must take them off its end, and a label sorts.

            new TutorialStep(
                "copper", "Buy a Copper Ore cart",
                $"At the marked spot, press [[interact]] and add a Copper Ore cart ({Price(CartPrice(ItemType.CopperOre))}). Take one ore "
                + "by hand with [[interact]]: a crate's label can only name a good your factory has handled.",
                w => Stock(ItemType.CopperOre) >= 1,
                w => Cart(ItemType.CopperOre),
                w => CartOf(ItemType.CopperOre) == null ? CostCounts(CartPrice(ItemType.CopperOre)) : Count(ItemType.CopperOre, 1),
                spot: () => CartOf(ItemType.CopperOre) == null ? Home(ItemType.CopperOre) : null),

            new TutorialStep(
                "belts", "Lay a belt",
                "Pick Belt in the build menu and DRAG along the marked run, starting from the end nearest the "
                + "Copper Ore cart: a dragged run points along itself, away from the cart.",
                w => BeltsLaid,
                w => BeltSpots.Where(c => !_world.Belts.HasBelt(c)).Select(c => (Vector2Int?)c).FirstOrDefault() ?? BeltSpots[0],
                w => $"Belts  {BeltSpots.Count(c => _world.Belts.HasBelt(c))} / {BeltSpots.Length}",
                menuKey: "BeltPrefab",
                moreSpots: () => BeltSpots,
                builds: b => b.GetPart<PlaceableBelt>() != null),

            new TutorialStep(
                "pipes4", "Drag a pipe run",
                "Steam Pipe drags too. Lay it along the marked run, from your steam to beside the belt: one long "
                + "stroke per straight stretch. Any route that steams the extractor's and the unloader's tiles counts.",
                w => SteamReaches(ExtractorSpot) && SteamReaches(UnloaderSpot),
                w => Pipe4Spots.Where(c => !_world.Steam.HasPipe(c)).Select(c => (Vector2Int?)c).FirstOrDefault(),
                w => PipesLeft(Pipe4Spots),
                menuKey: "SteamPipePrefab",
                moreSpots: () => Pipe4Spots,
                builds: IsPipe),

            new TutorialStep(
                "scav3", "An extractor",
                "Build a Scavenger and program it Extract, Push Output: it will push ore onto the belt.",
                w => CopperExtractor != null && IsProgrammed(CopperExtractor),
                w => ScrapFirst(ScavengerScrap, Station),
                w => Count(ItemType.Scrap, ScavengerScrap),
                workbench: true,
                program: ScavengerPlan,
                programTarget: g => g == CopperExtractor),

            new TutorialStep(
                "work-extractor", "Onto the belt",
                "Put it on the marked tile beside the Copper Ore cart, facing the belt. Watch the ore ride away.",
                w => BeltSpots.Any(c => _world.Belts.TryGetBelt(c, out PlacedBelt belt) && belt.Segment.Items.Count > 0),
                w => CopperExtractor != null && CopperExtractor.Cell != ExtractorSpot ? CopperExtractor.Cell : ExtractorSpot,
                spot: () => ExtractorSpot,
                spotFacing: () => CopperFacing),

            new TutorialStep(
                "copper-depot", "A labelled depot",
                "A belt only hands goods to another belt, so the ore stops at the end. Build a Depot on the "
                + "marked tile past it, then stand by it, press [[inventory]] and choose Copper Ore for its label.",
                w => DepotAt(CopperDepotSpot)?.FilterItemType == ItemType.CopperOre,
                w => DepotAt(CopperDepotSpot) == null ? ScrapFirst(DepotScrap, CopperDepotSpot) : CopperDepotSpot,
                w => DepotAt(CopperDepotSpot) == null ? Count(ItemType.Scrap, DepotScrap) : "Label: " + DepotAt(CopperDepotSpot).FilterLabel,
                menuKey: "DepotPrefab",
                spot: () => CopperDepotSpot,
                builds: IsDepot, exactTile: true, crate: true),

            new TutorialStep(
                "unloader", "An unloader",
                "Build one more Scavenger and program it Haul, set to Copper Ore, then Push Output. It takes "
                + "ore off the belt's end and pushes it into the labelled depot.",
                w => CopperUnloader != null && UnloaderPlan.Matches(CopperUnloader.Program),
                w => ScrapFirst(ScavengerScrap, Station),
                w => Count(ItemType.Scrap, ScavengerScrap),
                workbench: true,
                program: UnloaderPlan,
                programTarget: g => g == CopperUnloader),

            new TutorialStep(
                "work-unloader", "Off the belt",
                "Put it on the marked tile at the belt's end, facing the depot. Belt, golem, labelled "
                + "depot: the shape of every long haul.",
                w => CopperUnloader != null && _completedSinceEntry.Contains(CopperUnloader.GolemId),
                w => CopperUnloader != null && CopperUnloader.Cell != UnloaderSpot ? CopperUnloader.Cell : UnloaderSpot,
                spot: () => UnloaderSpot,
                spotFacing: () => CopperFacing),

            // --- Chapter 7: room to grow, and keeping it -------------------------------------------
            // The playtest script's Floor Expansion, bay cap, card-gating and Ledger items (F, G,
            // I4), and its save/load round trip (H).

            new TutorialStep(
                "expand", "More room",
                "Eight golems fill a workshop fast. Press Tab, open the Assembly Line, and click Extend "
                + $"({Price(FirstFloorCost)}): two more rows of workshop, floored and walled.",
                w => _world.Bounds.NorthExtent > _world.Bounds.MinNorthExtent,
                w => ScrapFirst(LineOf(FirstFloorCost, ItemType.Scrap), null),
                w => CostCounts(FirstFloorCost)),

            new TutorialStep(
                "bays", "More golems",
                $"The station builds at most {AssemblyBayStructure.DefaultSlots} golems. On the same tab, click "
                + "Upgrade on the assembly bays for six more.",
                w => _world.AssemblyBay.Tier >= 2,
                w => ScrapFirst(40, null),
                w => $"Golems  {LiveGolems.Count()} / {_world.AssemblyBay.MaxGolemSlots}"),

            // docs/pack-upgrades-design.md §6: the next extension asks more than a 100 pack holds.
            new TutorialStep(
                "pack", "A bigger pack",
                $"Your pack carries {_world.Setup?.packCapacityPerType ?? 100} of each good. On the same tab, click Upgrade on the pack "
                + $"({Price(FirstRungCost)}): {FirstRung?.capacityPerType ?? 150} of each. The next extension needs it.",
                w => _world.PackUpgrade.Tier >= 1,
                w => null,
                w => CostCounts(FirstRungCost)),

            new TutorialStep(
                "ledger", "Read the Ledger",
                "The Ledger maps everything you can build and how far you have come. Press Tab, open the "
                + "Ledger, and click any recipe node to see what it costs and makes.",
                w => _world.LedgerReadout.HasSelection,
                w => null),

            new TutorialStep(
                "save", "Save",
                "Press Tab, open Save / Load and click Save beside any slot. Everything you built goes into the "
                + "save: golems and their programs, buildings, your pack and every crate, your cards and this guide's place.",
                w => _world.SavesMade > 0,
                w => null),

            new TutorialStep(
                "load", "And load",
                "Now click Load beside that slot, then Load now. Your factory comes back exactly as you "
                + "saved it, mid-cycle and all.",
                w => _world.LoadsMade > 0,
                w => null),

            // --- Chapter 8: Brass, and the goods beyond --------------------------------------------
            // The playtest script's tier-2 and tier-3 chain (Parts F and G): every good the Zeppelin
            // costs. Goal steps, no marked tiles.

            Goal("copper-ingot", "Smelt Copper",
                "From here the guide names a good and its card, and you choose where to build. Claim Copper "
                + "Smelting and set an Aether-Hauler on it: Haul Copper Ore and Coke, Assemble, Push. Each line's "
                + "crates are its own: feed them by hand, by golem or by belt.",
                ItemType.CopperIngot, "AssembleCopperSmelting"),

            Goal("zinc-ingot", "Smelt Zinc",
                $"Add a Zinc Ore cart on the marked spot ({Price(CartPrice(ItemType.ZincOre))}), claim Zinc Smelting, and smelt it the same way.",
                ItemType.ZincIngot, "AssembleZincSmelting", ItemType.ZincOre),

            Goal("brass", "Alloy Brass",
                "Brass is two ingots in one: claim Brass Alloying and feed one golem Copper and Zinc Ingot. "
                + "Two inputs, so an Aether-Hauler.",
                ItemType.Brass, "AssembleBrassAlloying"),

            Goal("casing", "Press Casings",
                "Claim Casing Press: Iron Plate and Brass in, a Casing out. Two Hauls, an Assemble and a Push "
                + "fill an Aether-Hauler's four slots; a Brass Presser has only three.",
                ItemType.Casing, "AssembleCasingPress"),

            // crates-design §10: the late-game store, the walking between lines taken off the
            // player. No marks: like the rest of chapter 8, where is the player's call.
            new TutorialStep(
                "post", "A Pneumatic Post",
                $"Brass and Casing buy a Pneumatic Post crate ({Price(PlaceableCost("PneumaticPostPrefab"))}). Every Post crate opens onto "
                + $"one shared store, {_world.Setup?.postCapacityPerType ?? 200} of each good: a golem pushing into one fills what every other gives, and "
                + "your building spends from it once your pack runs short. Build two, wherever a line could use them.",
                w => PostCrates >= 2,
                w => null,
                w => "Post crates  " + System.Math.Min(PostCrates, 2) + "/2",
                menuKey: "PneumaticPostPrefab"),

            Goal("glass", "Glass from Slag",
                "Your smelters' Slag is not only waste. Claim Glassmaking, and send some Slag to a golem "
                + "running it instead of the heap.",
                ItemType.Glass, "AssembleGlassmaking"),

            Goal("lens", "Grind a Lens",
                "Claim Lens Grinding: Glass and Brass make a Lens, on an Aether-Hauler.",
                ItemType.Lens, "AssembleLensGrinding"),

            Goal("mainspring", "Wind a Mainspring",
                "Claim Mainspring Winding: Brass and Gears make a Mainspring, on an Aether-Hauler.",
                ItemType.Mainspring, "AssembleMainspringWinding"),

            Goal("aether-cell", "Bottle the Aether",
                $"Add an Aether cart on the marked spot ({Price(CartPrice(ItemType.Aether))}), claim Aether Containment, and seal it "
                + "behind Lenses on an Aether-Hauler.",
                ItemType.AetherCell, "AssembleAetherContainment", ItemType.Aether),

            // --- Chapter 9: the Zeppelin ---------------------------------------------------------------
            // The playtest script's Freight Link (Part G): a Zeppelin flies goods to a mast in one
            // flat 24-tick hop, however far.

            new TutorialStep(
                "zeppelin-card", "Claim the Zeppelin",
                "Your factory has made everything it costs: collect what the card asks for into your pack. Press "
                + "Tab, open the Assembly Line and claim the Zeppelin Freight Loader.",
                w => HasClaimed("ZeppelinFreightLoader"),
                w => null,
                card: "ZeppelinFreightLoader"),

            // The Zeppelin's own verb is a card like any other, and the Workbench only offers
            // cards you own: without this claim chapter 9 could not be programmed (from review).
            new TutorialStep(
                "freight-card", "Claim Freight Launch",
                "A Zeppelin flies what it holds with Freight Launch, and a golem can only use cards you own. "
                + "Press Tab, open the Assembly Line and claim it: it's free.",
                w => HasClaimed("FreightLaunch"),
                w => null,
                card: "FreightLaunch"),

            new TutorialStep(
                "zeppelin", "Build the Zeppelin",
                "At the construction station, build a Zeppelin Freight Loader: "
                + PriceList(ChassisCost("ZeppelinFreightLoader")) + ".",
                w => Zeppelin != null,
                w => ScrapFirst(1, Station),
                w => CostCounts(ChassisCost("ZeppelinFreightLoader"))),

            new TutorialStep(
                "mast", "Raise a Freight Mast",
                "A Zeppelin flies to a Freight Mast, and the mast lands what it brings in its own crate. "
                + $"Build one on the marked tile ({Price(PlaceableCost("FreightMastPrefab"))}); anywhere would do.",
                w => HasMast,
                w => HasMast ? (Vector2Int?)null : MastSpot,
                w => CostCounts(PlaceableCost("FreightMastPrefab")),
                menuKey: "FreightMastPrefab",
                spot: () => MastSpot,
                builds: b => b.GetPart<PlaceableFreightMast>() != null),

            new TutorialStep(
                "zeppelin-pipe", "Steam for it",
                "Steam Pipe on the marked tiles, from the copper line's run to the Zeppelin's side: anything "
                + "that gets steam to the Zeppelin's tile counts.",
                w => SteamReaches(ZeppelinSpot),
                w => ZeppelinPipeSpot,
                menuKey: "SteamPipePrefab",
                moreSpots: () => ZeppelinPipeSpots,
                builds: IsPipe),

            new TutorialStep(
                "program-zeppelin", "Program the Zeppelin",
                "Always On, Haul set to Copper Ore, then Freight Launch: it flies whatever it holds to the mast.",
                w => Zeppelin != null && ZeppelinPlan.Matches(Zeppelin.Program),
                w => null,
                workbench: true,
                program: ZeppelinPlan,
                programTarget: g => g == Zeppelin),

            new TutorialStep(
                "launch", "Fly the Copper home",
                "Put it on the marked tile past the labelled Copper depot, with the depot BEHIND it (facing "
                + $"away): it hauls from behind, like every golem. Each flight takes {GolemEntity.FreightLaunchTicks} ticks, however far the mast.",
                w => Zeppelin != null && _completedSinceEntry.Contains(Zeppelin.GolemId),
                w => Zeppelin != null && Zeppelin.Cell != ZeppelinSpot ? Zeppelin.Cell : ZeppelinSpot,
                spot: () => ZeppelinSpot,
                spotFacing: () => CopperFacing),

            // Any withdrawal: E, the crate screen, or a golem the player stood beside the mast.
            new TutorialStep(
                "mast-collect", "Unload the mast",
                $"The Copper lands in the mast's own crate, {CrateHolds} of each good, not in your pack. Press [[interact]] at the mast "
                + "to collect it, or stand a golem beside it to carry it on.",
                w => Built.Any(b => b.GetPart<PlaceableFreightMast>() != null && Withdrawn(b, ItemType.CopperOre)),
                w => MastBuilding?.Cell,
                w => MastBuilding == null ? "" : InTheCrate(MastBuilding.Cell, ItemType.CopperOre)),

            // --- Chapter 10: the Clock Tower ------------------------------------------------------------
            // The endgame (G10, the user's call): the tower in the town square, roped off until a
            // Zeppelin flew, now open. It is built in stages, each demanding a steady RATE of a
            // megaproject good; goods kept in stock count too (docs/contracts-design.md §11).

            new TutorialStep(
                "tower-visit", "The town square",
                "Your Zeppelin flew, so the rope is down. Walk out of the workshop, down the street and into "
                + "the town square to the south: the Clock Tower's site is waiting.",
                w => PlayerInSquare,
                w => TownSquare.TowerCentre),

            Goal("frame-section", "Frame Sections",
                $"Stage 1, the Foundation, wants Frame Sections, and keeps wanting them: {StageOneRate} a minute, every minute, "
                + "until it is built. Claim the card and start a line. Three inputs need five slots: build a "
                + "Mainspring Overclocker at the station.",
                ItemType.FrameSection, "AssembleFrameSection"),

            new TutorialStep(
                "tower-feed", "Feed the tower",
                "Build a depot in the square and stand a golem between it and the tower: Haul set to Frame "
                + "Section, then Push; it pushes in from any side. The depot starts empty: fill its crate by belt or "
                + "by hand, or make it a Post crate, which opens onto your Post store. The golem needs steam out here too: a boiler beside it, fuelled, or a pipe run from the workshop.",
                w => TowerSite.DeliveryRatePerMinute(ItemType.FrameSection) > 0,
                w => TownSquare.TowerCentre),

            new TutorialStep(
                "stage1", "Lay the Foundation",
                $"The tower builds at the rate you feed it: {StageOneRate} Frame Sections a minute lays it in its time, "
                + "and up to three times that lays it faster. Frame Sections you kept count as well as fresh ones. "
                + $"Short of {StageOneRate} a minute it still builds, just slower. Keep it fed until the Foundation is laid.",
                w => TowerSite.StageIndex >= 1 || TowerSite.IsComplete,
                w => TownSquare.TowerCentre,
                w => ClockTower.ClockTowerReadout.FormatHeadline(TowerSite.BuildReading())),

            new TutorialStep(
                "done", "The Foundation is laid",
                "The guide ends here; the tower doesn't. Three stages remain: The Movement, Aether Illumination "
                + "and The Chronometer, each wanting its own goods at a steady rate. The Ledger (Tab) maps every "
                + "recipe they need, and the tower's panel shows what it wants now. F1 brings this guide back.",
                w => false,
                w => null),
        };
    }
}
