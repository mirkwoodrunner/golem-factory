using System;
using System.Collections.Generic;
using System.Linq;
using GolemFactory.AssemblyLine;
using GolemFactory.Buildings;
using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.World;

namespace GolemFactory.Tutorial
{
    /// <summary>One step of the guide: what to do, how far along it is, and what to point at.</summary>
    public sealed class TutorialStep
    {
        public string Id { get; }
        public string Title { get; }
        public string Body { get; }

        /// <summary>The build-menu row the step needs (a placeable key), or null.</summary>
        public string MenuKey { get; }

        /// <summary>
        /// The tile the step wants something placed on, or null. The panel marks it on the floor;
        /// with <see cref="SpotFacing"/> set, the marker also shows which way to face.
        /// </summary>
        public Vector2Int? Spot { get; }

        /// <summary>Every tile the step marks: <see cref="Spot"/> first, then any others (a pipe run).</summary>
        public IReadOnlyList<Vector2Int> Spots { get; }

        public Facing? SpotFacing { get; }

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

        internal Func<SandboxWorld, bool> Done { get; }
        internal Func<SandboxWorld, string> ProgressText { get; }
        internal Func<SandboxWorld, Vector2Int?> Target { get; }

        internal TutorialStep(
            string id, string title, string body, Func<SandboxWorld, bool> done,
            Func<SandboxWorld, Vector2Int?> target, Func<SandboxWorld, string> progress = null, string menuKey = null,
            Vector2Int? spot = null, Facing? spotFacing = null, Vector2Int[] moreSpots = null, string card = null,
            bool workbench = false)
        {
            Card = card;
            Workbench = workbench;
            Spot = spot ?? (moreSpots != null && moreSpots.Length > 0 ? moreSpots[0] : (Vector2Int?)null);
            SpotFacing = spotFacing;
            var spots = new List<Vector2Int>();
            if (spot != null)
            {
                spots.Add(spot.Value);
            }
            if (moreSpots != null)
            {
                spots.AddRange(moreSpots);
            }
            Spots = spots;
            Id = id;
            Title = title;
            Body = body;
            Done = done;
            Target = target;
            ProgressText = progress;
            MenuKey = menuKey;
        }
    }

    /// <summary>
    /// The step-by-step guide through a first session (G10, at the user's call: "I think there
    /// needs to be a tutorial"). It walks the cold start testscript Part E describes -- Scrap,
    /// a Coal truckload, Coke and Iron Plate at the bench, a fuelled boiler -- and then the first
    /// golem: built, programmed, given a depot, and set to work beside the Scrap stall.
    ///
    /// <para>
    /// <b>Every step is DETECTED, never ticked off by hand.</b> A step is done when the world shows
    /// it (the stockpile holds the Coal, a boiler has Coke in it, a golem has finished a cycle),
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
            _steps = BuildSteps();
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
                return;
            }
            int byId = string.IsNullOrEmpty(stepId) ? -1 : _steps.FindIndex(s => s.Id == stepId);
            Enter(byId >= 0 ? byId : Math.Max(0, Math.Min(index, _steps.Count)));
        }

        private void Enter(int index)
        {
            Index = index;
            _cycleSeen = false;
            _completedSinceEntry.Clear();
            Version++;
            ReportStep();
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
            Playtest.StepEntered(step?.Id ?? "finished", step?.Title ?? "Finished", _clock());
        }

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

        private Vector2Int? Stall(string id)
        {
            SandboxSetup.NodeEntry node = _world.Setup?.nodes?.FirstOrDefault(n => n.id == id);
            return node == null ? (Vector2Int?)null : new Vector2Int(node.x, node.y);
        }

        private Vector2Int? Bench => _world.Setup?.starterBench != null
            ? SandboxSetup.CellOf(_world.Setup.starterBench)
            : (Vector2Int?)null;

        private Vector2Int? Station => _world.Setup?.starterStation != null
            ? SandboxSetup.CellOf(_world.Setup.starterStation)
            : (Vector2Int?)null;

        /// <summary>Points at the Scrap stall while the player is short of Scrap, else at <paramref name="then"/>.</summary>
        private Vector2Int? ScrapFirst(int need, Vector2Int? then) => Stock(ItemType.Scrap) < need ? Stall("ScrapNode") : then;

        // --- The first golem's layout --------------------------------------------------------------
        //
        // Fixed, beside the free Scrap stall, so the three placements explain each other (G10,
        // from playtest: "it would be good to specify where the boiler, depot and golem should
        // be placed"):
        //
        //     depot        <- in front: the golem pushes into it
        //     golem boiler <- the boiler beside it powers it, no pipe needed
        //     Scrap stall  <- behind: the golem extracts from it
        //
        // The golem faces north, away from the stall. Every cell is a street cell the golems
        // scenario has already built on.

        private Vector2Int StallCell => Stall("ScrapNode") ?? new Vector2Int(0, 0);

        /// <summary>Where the first golem stands: just north of the Scrap stall.</summary>
        public Vector2Int GolemSpot => StallCell + new Vector2Int(0, 1);

        /// <summary>In front of the golem.</summary>
        public Vector2Int DepotSpot => StallCell + new Vector2Int(0, 2);

        /// <summary>Beside the golem, east of it.</summary>
        public Vector2Int BoilerSpot => StallCell + new Vector2Int(1, 1);

        // Chapter 2 extends the same column north: the Presser stands on the far side of the first
        // depot, hauling the Scrap the Scavenger delivers (every depot is a door into the one
        // stockpile), and pushes Iron Plate into a second depot in front of it. It is two tiles
        // from the boiler, so it needs the game's first steam pipe -- the lesson of Phase 2.
        //
        //     depot 2      <- the Presser pushes Iron Plate into it
        //     Presser  P   <- P: steam pipe
        //     depot 1  P
        //     golem boiler
        //     Scrap stall

        /// <summary>Where the first Brass Presser stands: just north of the first depot.</summary>
        public Vector2Int PresserSpot => StallCell + new Vector2Int(0, 3);

        /// <summary>In front of the Presser.</summary>
        public Vector2Int PresserDepotSpot => StallCell + new Vector2Int(0, 4);

        /// <summary>The pipe run from the boiler up the Presser's east side.</summary>
        public Vector2Int[] PipeSpots => new[] { StallCell + new Vector2Int(1, 2), StallCell + new Vector2Int(1, 3) };

        private bool SteamReachesPresserSpot => SteamReaches(PresserSpot);

        // --- Pipe steps are done by what the steam does, not by which tiles hold pipe ------------
        //
        // The marked tiles are a suggestion the guide draws; a player who routes a pipe another
        // way and gets the same steam to the same golem tiles is done too (from review: a working
        // factory stuck on "Join the boilers" because three exact tiles were bare). Steam reach is
        // a layout question, so these ignore Coke: fuel is a different step's problem.

        /// <summary>Whether any boiler's pipes reach <paramref name="cell"/>, fuelled or not.</summary>
        private bool SteamReaches(Vector2Int cell) => _world.Steam.Reaches(cell, _world.Clock.CurrentTick);

        /// <summary>
        /// Chapter 3's pipes do two jobs: an older boiler's run reaches the coker's tile, so it
        /// powers the coker before the new boiler has any Coke, and the same run touches the new
        /// boiler, joining them. Only a pipe joins boilers, never a golem standing between them,
        /// and the new boiler's own neighbours are its alone, so a route that joins them but
        /// stops short of the coker's tile does not count: the coker would sit beside a cold
        /// firebox.
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

        /// <summary>Whether a golem has a logic core and every one of these cards, in any slot.</summary>
        private static bool HasCards(GolemEntity golem, params string[] cards) =>
            golem.Program?.logicCore != null
            && cards.All(card => golem.Program.appendages.Any(a => a != null && a.name == card));

        private bool IsIronPresser(GolemEntity golem) =>
            HasCards(golem, "HaulScrap", "AssembleScrapReclamation", "PushOutput");

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

        // Chapter 3 builds a boiler that feeds itself, beside the Coal stall: a Presser extracts
        // Coal from the stall behind it, cokes it, and pushes the Coke straight into a second
        // boiler in front of it. A pipe run from the FIRST boiler, along the stalls' row and up
        // one, powers the Presser before the new boiler has any Coke of its own (it starts empty,
        // and a Presser beside only an empty boiler could never make the first Coke), and joins
        // the two boilers into one network, so the coal line keeps the whole factory in steam.
        //
        //                          pipe  boiler 2
        //     boiler 1  pipe ...  pipe  coking Presser
        //                               Coal stall

        private Vector2Int CoalCell => Stall("CoalNode") ?? new Vector2Int(4, 0);

        /// <summary>Where the coking Presser stands: just north of the Coal stall.</summary>
        public Vector2Int CokerSpot => CoalCell + new Vector2Int(0, 1);

        /// <summary>The second boiler, in front of the coking Presser.</summary>
        public Vector2Int Boiler2Spot => CoalCell + new Vector2Int(0, 2);

        /// <summary>
        /// From beside the first boiler east along its row to the coking Presser's west side,
        /// then up one to sit beside the second boiler.
        /// </summary>
        public Vector2Int[] Pipe2Spots => _pipe2Spots ??= ComputePipe2Spots();

        // The layout is fixed once the world is set up, and Done, Target and Progress all read
        // these every frame: compute them once rather than rebuilding the run on each read.
        private Vector2Int[] _pipe2Spots;
        private Vector2Int[] _pipe4Spots;

        private Vector2Int[] ComputePipe2Spots()
        {
            var spots = new List<Vector2Int>();
            for (int x = BoilerSpot.x + 1; x < CokerSpot.x; x++)
            {
                spots.Add(new Vector2Int(x, BoilerSpot.y));
            }
            spots.Add(new Vector2Int(CokerSpot.x - 1, Boiler2Spot.y));
            return spots.ToArray();
        }

        private bool IsCoker(GolemEntity golem) => HasCards(golem, "ExtractScrap", "AssembleCoking", "PushOutput");

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

        private bool CoalStallStocked =>
            _world.Nodes.TryGetNode("CoalNode", out var node) && node.RemainingQuantity > 0;

        // Chapter 4 copies the first golem's program onto a second Scavenger with a patent, and
        // stands it on the Scrap stall's EAST side, facing east into a third depot -- the first
        // golem faces north, so this one is the lesson in R. Boiler 1 is right above it.
        //
        //     golem 1   boiler 1
        //     Scrap     golem 2  -> depot 3

        /// <summary>The second Scavenger: east of the Scrap stall, under the first boiler.</summary>
        public Vector2Int Scav2Spot => StallCell + new Vector2Int(1, 0);

        /// <summary>East of the second Scavenger.</summary>
        public Vector2Int Depot3Spot => StallCell + new Vector2Int(2, 0);

        private IEnumerable<GolemEntity> Scavengers =>
            LiveGolems.Where(g => g.Program?.chassis != null && g.Program.chassis.name == "ClockworkScavenger");

        /// <summary>The Scavenger that is not the first one: chapter 4's.</summary>
        private GolemEntity SecondScavenger => Scavengers.Skip(1).FirstOrDefault();

        private string _stalledGolemId;

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

        private Vector2Int SmeltColumn => Boiler2Spot + new Vector2Int(2, 0);

        /// <summary>The Aether-Hauler that smelts: two east of the coal line's boiler.</summary>
        public Vector2Int SmelterSpot => SmeltColumn;

        /// <summary>Behind the smelter: its Scrap and Coke, from the stockpile.</summary>
        public Vector2Int SmeltInSpot => SmeltColumn + new Vector2Int(0, -1);

        /// <summary>In front of the smelter, behind the carrier: Iron Plate and Slag land here.</summary>
        public Vector2Int SmeltOutSpot => SmeltColumn + new Vector2Int(0, 1);

        /// <summary>The carrier Presser, in front of depot B.</summary>
        public Vector2Int CarrierSpot => SmeltColumn + new Vector2Int(0, 2);

        /// <summary>The Slag Heap, in front of the carrier.</summary>
        public Vector2Int SlagHeapSpot => SmeltColumn + new Vector2Int(0, 3);

        /// <summary>Beside boiler 2 and up the column's west side.</summary>
        public Vector2Int[] Pipe3Spots => new[]
        {
            SmeltColumn + new Vector2Int(-1, 0), SmeltColumn + new Vector2Int(-1, 1), SmeltColumn + new Vector2Int(-1, 2),
        };

        private IEnumerable<GolemEntity> Haulers =>
            LiveGolems.Where(g => g.Program?.chassis != null && g.Program.chassis.name == "AetherHauler");

        private int HaulsOf(GolemEntity golem, string itemType) =>
            Enumerable.Range(0, golem.Program.appendages.Count).Count(i =>
                golem.Program.appendages[i]?.actionType == PunchCards.AppendageActionType.Haul
                && golem.Program.GetItemTypeAt(i) == itemType);

        private bool IsSmelter(GolemEntity golem) =>
            HasCards(golem, "AssembleIronSmelting", "PushOutput")
            && HaulsOf(golem, ItemType.Scrap) > 0 && HaulsOf(golem, ItemType.Coke) > 0;

        private bool IsCarrier(GolemEntity golem) =>
            HasCards(golem, "PushOutput")
            && HaulsOf(golem, ItemType.Slag) > 0 && HaulsOf(golem, ItemType.Coke) > 0;

        // Chapter 6 moves goods with a belt and sorts them with a label, at the Copper stall: an
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
        //     Copper stall

        private Vector2Int CopperCell => Stall("CopperOreNode") ?? new Vector2Int(8, 0);

        /// <summary>The copper extractor, just north of the Copper stall.</summary>
        public Vector2Int ExtractorSpot => CopperCell + new Vector2Int(0, 1);

        /// <summary>The belt run north from the extractor, three cells.</summary>
        public Vector2Int[] BeltSpots => new[]
        {
            CopperCell + new Vector2Int(0, 2), CopperCell + new Vector2Int(0, 3), CopperCell + new Vector2Int(0, 4),
        };

        /// <summary>The unloader, at the belt's end.</summary>
        public Vector2Int UnloaderSpot => CopperCell + new Vector2Int(0, 5);

        /// <summary>The labelled depot, in front of the unloader.</summary>
        public Vector2Int CopperDepotSpot => CopperCell + new Vector2Int(0, 6);

        /// <summary>
        /// Up the copper line's west side, then west and down to the top of chapter 5's pipes.
        /// </summary>
        public Vector2Int[] Pipe4Spots => _pipe4Spots ??= ComputePipe4Spots();

        private Vector2Int[] ComputePipe4Spots()
        {
            var spots = new List<Vector2Int>();
            int west = CopperCell.x - 1;
            for (int y = ExtractorSpot.y; y <= UnloaderSpot.y + 1; y++)
            {
                spots.Add(new Vector2Int(west, y));
            }
            Vector2Int top = Pipe3Spots[Pipe3Spots.Length - 1];
            int row = UnloaderSpot.y + 1;
            for (int x = west - 1; x >= top.x; x--)
            {
                spots.Add(new Vector2Int(x, row));
            }
            for (int y = row - 1; y > top.y; y--)
            {
                spots.Add(new Vector2Int(top.x, y));
            }
            return spots.ToArray();
        }

        private GolemEntity CopperExtractor => Scavengers.Skip(2).FirstOrDefault();

        private GolemEntity CopperUnloader => Scavengers.Skip(3).FirstOrDefault();

        private bool BeltsLaid => BeltSpots.All(c => _world.Belts.TryGetBelt(c, out PlacedBelt belt) && belt.Facing == Facing.North);

        private PlaceableDepot DepotAt(Vector2Int cell) =>
            Built.FirstOrDefault(b => b.Cell == cell)?.GetPart<PlaceableDepot>();

        /// <summary>
        /// Chapter 8's shape: claim a recipe card, then make the good. No marked tiles -- by now the
        /// player has built every kind of line the recipe needs, and choosing where is the game.
        /// Done once the factory has EVER made the good (the tech tree's ledger, which only
        /// grows), the same question the Assembly Line asks of a card's price.
        /// </summary>
        private TutorialStep Goal(string id, string title, string body, string good, string card, string recipe, string stall = null) =>
            new TutorialStep(
                id, title, body,
                w => _world.TechTree.Ledger.HasItem(good),
                w => stall != null ? Stall(stall) : null,
                w => (HasClaimed(card) ? "Card claimed" : "Claim the card") + "  ·  " + recipe,
                card: card,
                workbench: true);

        // Chapter 9 flies the Copper home. The Zeppelin stands on the labelled Copper depot's far
        // side with the depot behind it, hauls ore out and launches it to a Freight Mast, which lands it in the
        // stockpile. One more pipe, on the copper run's top, gives it steam.
        //
        //     pipe  Zeppelin (faces up, depot behind)              Freight Mast
        //     pipe  labelled Copper depot

        /// <summary>The Zeppelin's tile: just past the labelled Copper depot.</summary>
        public Vector2Int ZeppelinSpot => CopperDepotSpot + new Vector2Int(0, 1);

        /// <summary>The pipe beside it, on top of chapter 6's run.</summary>
        public Vector2Int ZeppelinPipeSpot => ZeppelinSpot + new Vector2Int(-1, 0);

        /// <summary>Where the guide suggests the mast: a few tiles off, across open floor.</summary>
        public Vector2Int MastSpot => ZeppelinSpot + new Vector2Int(4, 3);

        private GolemEntity Zeppelin =>
            LiveGolems.FirstOrDefault(g => g.Program?.chassis != null && g.Program.chassis.name == "ZeppelinFreightLoader");

        private bool HasMast => Built.Any(b => b.GetPart<PlaceableFreightMast>() != null);

        /// <summary>Whether the player is standing in the town square (G10).</summary>
        private bool PlayerInSquare
        {
            get
            {
                Compat.Vector3 at = _world.Interactor.Position;
                return TownSquare.Contains(new Vector2Int(Mathf.RoundToInt(at.x), Mathf.RoundToInt(at.y)));
            }
        }

        private ClockTower.ClockTowerSite TowerSite => _world.ClockTower.Site;

        private SlagHeap HeapAt(Vector2Int cell) =>
            Built.FirstOrDefault(b => b.Cell == cell)?.GetPart<PlaceableSlagHeap>()?.Heap;

        private bool SteamReachesGolemSpot => _world.Steam.Reaches(GolemSpot, _world.Clock.CurrentTick);

        private IEnumerable<PlaceableBuilding> Built => _world.Build.Buildings.Where(b => !b.IsRemoved);

        private IEnumerable<PlaceableBuilding> BoilerBuildings => Built.Where(b => b.GetPart<PlaceableBoiler>() != null);

        private IEnumerable<PlaceableBoiler> Boilers => BoilerBuildings.Select(b => b.GetPart<PlaceableBoiler>());

        private IEnumerable<GolemEntity> LiveGolems => _world.Golems.Where(g => g != null && !g.IsRemoved);

        private bool IsProgrammed(GolemEntity golem) => HasCards(golem, "ExtractScrap", "PushOutput");

        private Vector2Int? FirstGolem => LiveGolems.Select(g => (Vector2Int?)g.Cell).FirstOrDefault();

        private List<TutorialStep> BuildSteps() => new List<TutorialStep>
        {
            new TutorialStep(
                "scrap", "Gather Scrap",
                "Walk with WASD to the Scrap stall in the market street (the arrow) and press E to take Scrap. "
                + "Scrap pays for almost everything at first.",
                w => Stock(ItemType.Scrap) >= 20,
                w => Stall("ScrapNode"),
                w => Count(ItemType.Scrap, 20)),

            new TutorialStep(
                "coal", "Buy Coal",
                "The Coal stall is empty. Press E there to order a truckload for 10 Scrap. "
                + "When the cart arrives, press E again to take the Coal.",
                w => Stock(ItemType.Coal) >= 5,
                w => _world.Market != null && !_world.Market.IsInTransit("CoalNode") && Stock(ItemType.Scrap) < 10
                    ? Stall("ScrapNode")
                    : Stall("CoalNode"),
                w => Count(ItemType.Coal, 5)),

            new TutorialStep(
                "coke", "Make Coke",
                "At the Hand-Crank Bench, hold E to crank R1 Coking: 1 Coal becomes 1 Coke. "
                + "Coke is what boilers burn.",
                w => Stock(ItemType.Coke) >= 5,
                w => Bench,
                w => Count(ItemType.Coke, 5)),

            new TutorialStep(
                "iron", "Make Iron Plate",
                "Press R at the bench to change its recipe to R2 Scrap Reclamation (1 Scrap becomes 1 Iron Plate), "
                + "then hold E. If R turns a building instead, press Escape first to put it down.",
                w => Stock(ItemType.IronPlate) >= 10,
                w => ScrapFirst(1, Bench),
                w => Count(ItemType.IronPlate, 10)),

            new TutorialStep(
                "boiler", "Build a Boiler",
                "Every golem runs on steam. Click Boiler in the build menu (30 Scrap + 10 Iron Plate) and place it "
                + "on the marked tile beside the Scrap stall, then press Escape. Your first golem will stand next to it.",
                // Its own layout counts too, if steam from it already reaches the golem's tile.
                w => BoilerBuildings.Any(b => b.Cell == BoilerSpot) || SteamReachesGolemSpot,
                w => ScrapFirst(30, BoilerSpot),
                w => Count(ItemType.Scrap, 30),
                menuKey: "BoilerPrefab",
                spot: BoilerSpot),

            new TutorialStep(
                "fuel", "Fuel the Boiler",
                "Stand at the boiler and press E to load your Coke into it. A boiler with no Coke powers nothing.",
                w => Boilers.Any(b => b.Boiler != null && b.Boiler.CokeStock > 0),
                w => BoilerBuildings.OrderBy(b => b.Cell == BoilerSpot ? 0 : 1).Select(b => (Vector2Int?)b.Cell).FirstOrDefault()),

            new TutorialStep(
                "golem", "Build a Golem",
                "At the construction station, press E and choose the Clockwork Scavenger (12 Scrap).",
                w => LiveGolems.Any(),
                w => ScrapFirst(12, Station),
                w => Count(ItemType.Scrap, 12)),

            new TutorialStep(
                "program", "Program it",
                "In the Workbench, drag the Always On core into TRIGGER, Extract Scrap into STEP 1 and "
                + "Push Output into STEP 2, then pull ENGAGE. Closed it? Press E at the golem.",
                w => LiveGolems.Any(IsProgrammed),
                w => FirstGolem,
                workbench: true),

            new TutorialStep(
                "depot", "Build a Depot",
                "A golem delivers into whatever is in front of it. Build a Depot (15 Scrap) on the marked tile, "
                + "two north of the Scrap stall: the golem will stand between them. Misplaced? Demolish refunds in full.",
                w => Built.Any(b => b.Cell == DepotSpot && b.GetPart<PlaceableDepot>() != null),
                w => ScrapFirst(15, DepotSpot),
                w => Count(ItemType.Scrap, 15),
                menuKey: "DepotPrefab",
                spot: DepotSpot),

            new TutorialStep(
                "work", "Put it to work",
                "Press G by the golem to pick it up, walk until the outline under you is on the marked tile, between "
                + "the Scrap stall and the depot, and press G again. Press R to turn it until it faces the depot (up): "
                + "it takes Scrap from behind and pushes it forward.",
                w => _cycleSeen,
                w => LiveGolems.Any(g => g.Cell == GolemSpot) ? GolemSpot
                    : LiveGolems.Where(IsProgrammed).Select(g => (Vector2Int?)g.Cell).FirstOrDefault() ?? GolemSpot,
                spot: GolemSpot,
                spotFacing: Facing.North),

            // --- Chapter 2: the first machine that CRAFTS --------------------------------------
            // progression-design §9 Phase 1's goal and Phase 2's lesson: a Scavenger gathers but
            // cannot craft; the Brass Presser can, and running it needs the first steam pipe.

            new TutorialStep(
                "gears", "Cut Gears",
                "Your Scavenger gathers, but it can't craft. The next golem can: the Brass Presser. It costs "
                + "10 Gears. At the bench, press R until it shows R8 Gear Cutting (2 Iron Plate become 1 Gear), "
                + "then hold E. Short of Iron Plate? Switch back to R2 and crank some.",
                w => Stock(ItemType.Gear) >= 10,
                w => ScrapFirst(1, Bench),
                w => Counts((ItemType.Gear, 10), (ItemType.IronPlate, 2))),

            new TutorialStep(
                "claim", "Claim Scrap Reclamation",
                "A golem can only use cards you own. Press Tab, open the Assembly Line, and click Claim on "
                + "Assemble Scrap Reclamation (4 Scrap): it lets a golem turn Scrap into Iron Plate.",
                w => HasClaimed("AssembleScrapReclamation"),
                w => null,
                card: "AssembleScrapReclamation"),

            new TutorialStep(
                "presser", "Build a Brass Presser",
                "At the construction station, press E and choose the Brass Presser "
                + "(60 Scrap + 20 Iron Plate + 10 Gear). Your Scavenger's depot fills with Scrap while you work.",
                w => Pressers.Any(),
                w => ScrapFirst(60, Station),
                w => Counts((ItemType.Scrap, 60), (ItemType.IronPlate, 20), (ItemType.Gear, 10))),

            new TutorialStep(
                "pipe", "Lay a steam pipe",
                "The Presser will stand two tiles from the boiler, out of its reach. Build Steam Pipe "
                + "(1 Iron Plate each) on the two marked tiles: steam runs along pipes to any golem beside them.",
                w => SteamReachesPresserSpot,
                w => PipeSpots[0],
                menuKey: "SteamPipePrefab",
                moreSpots: PipeSpots),

            new TutorialStep(
                "depot2", "A depot for its output",
                "Build a second Depot on the marked tile. The Presser will take Scrap from the first depot "
                + "behind it and push Iron Plate into this one. Every depot opens onto the same stockpile.",
                w => Built.Any(b => b.Cell == PresserDepotSpot && b.GetPart<PlaceableDepot>() != null),
                w => ScrapFirst(15, PresserDepotSpot),
                w => Count(ItemType.Scrap, 15),
                menuKey: "DepotPrefab",
                spot: PresserDepotSpot),

            new TutorialStep(
                "program-presser", "Program the Presser",
                "In the Workbench: Always On into TRIGGER, then Haul Scrap, Assemble Scrap Reclamation and "
                + "Push Output into STEPS 1 to 3, and pull ENGAGE. Closed it? Press E at the Presser.",
                w => Pressers.Any(IsIronPresser),
                w => Pressers.Select(g => (Vector2Int?)g.Cell).FirstOrDefault(),
                workbench: true),

            new TutorialStep(
                "work-presser", "Make Iron Plate",
                "Move the Presser with G onto the marked tile, between the two depots, and press R until it "
                + "faces up. It hauls Scrap from behind, presses it into Iron Plate, and pushes it forward.",
                w => Pressers.Any(g => _completedSinceEntry.Contains(g.GolemId)),
                w => Pressers.Any(g => g.Cell == PresserSpot) ? PresserSpot
                    : Pressers.Select(g => (Vector2Int?)g.Cell).FirstOrDefault() ?? PresserSpot,
                spot: PresserSpot,
                spotFacing: Facing.North),

            // --- Chapter 3: the Coke line ---------------------------------------------------------
            // progression-design §9 Phase 2's wall: golems burn Coke, and the boiler's runs out.
            // The answer is a coal line that feeds the boiler it runs on.

            new TutorialStep(
                "coking-card", "Claim Coking",
                "Your boiler burns the Coke you crank by hand, and it will run dry. Time to make Coke "
                + "automatically. Press Tab, open the Assembly Line and claim Assemble Coking. Its price is Coal: "
                + "short? Take some at the Coal stall with E (order a truckload if it is empty).",
                w => HasClaimed("AssembleCoking"),
                // Chapter 1 cranks every Coal into Coke, so the stockpile is usually out.
                w => Stock(ItemType.Coal) < CokingClaimCoal ? Stall("CoalNode") : null,
                w => Count(ItemType.Coal, CokingClaimCoal),
                card: "AssembleCoking"),

            new TutorialStep(
                "presser2", "A second Presser",
                "Build another Brass Presser at the station (60 Scrap + 20 Iron Plate + 10 Gear). Your first "
                + "Presser is making the Iron Plate; cut the Gears at the bench (R8) as before.",
                w => Pressers.Count() >= 2,
                w => ScrapFirst(60, Station),
                w => Counts((ItemType.Scrap, 60), (ItemType.IronPlate, 20), (ItemType.Gear, 10))),

            new TutorialStep(
                "boiler2", "A boiler for the coal line",
                "Build a second Boiler on the marked tile, two north of the Coal stall. The coking Presser "
                + "will stand between them and push its Coke straight into it.",
                w => BoilerBuildings.Any(b => b.Cell == Boiler2Spot),
                w => ScrapFirst(30, Boiler2Spot),
                w => Counts((ItemType.Scrap, 30), (ItemType.IronPlate, 10)),
                menuKey: "BoilerPrefab",
                spot: Boiler2Spot),

            new TutorialStep(
                "pipes2", "Join the boilers",
                "Lay Steam Pipe on the marked tiles, from your first boiler to the new one. The new boiler starts "
                + "empty, so the first one powers the coking Presser until it has Coke of its own; after that, "
                + "joined boilers share their golems. Your own route counts too, if its pipe reaches the coking "
                + "Presser's tile and touches the new boiler.",
                // The OUTCOME, not the marked tiles: any route that does both jobs the body names.
                w => FirstBoilerPowersCokerAndJoins,
                w => Pipe2Spots.Where(c => !_world.Steam.HasPipe(c)).Select(c => (Vector2Int?)c).FirstOrDefault(),
                w => $"Pipes  {Pipe2Spots.Count(c => _world.Steam.HasPipe(c))} / {Pipe2Spots.Length}",
                menuKey: "SteamPipePrefab",
                moreSpots: Pipe2Spots),

            new TutorialStep(
                "coal-order", "Stock the Coal stall",
                "Golems take from a stall just as you do. Press E at the empty Coal stall to order a "
                + "truckload (10 Scrap for 40 Coal); the cart takes a few seconds to arrive.",
                w => CoalStallStocked,
                w => Stall("CoalNode")),

            new TutorialStep(
                "program-coker", "Program the coking Presser",
                "In the Workbench: Always On, then Extract Scrap, Assemble Coking and Push Output. Despite its "
                + "name, Extract takes whatever the stall behind the golem holds: here, Coal.",
                w => Pressers.Any(IsCoker),
                w => NewestPresser?.Cell,
                workbench: true),

            new TutorialStep(
                "work-coker", "Feed the boiler",
                "Move the coking Presser onto the marked tile between the Coal stall and the new boiler, "
                + "facing the boiler (up). It extracts Coal, cokes it, and pushes the Coke into the firebox. "
                + "It needs steam to make the first Coke: if your boilers have run dry, load Coke into the new one (E).",
                w => Pressers.Any(g => IsCoker(g) && _completedSinceEntry.Contains(g.GolemId)),
                // A coker on dry boilers can never make the Coke that would fuel them, so point at
                // the new boiler (right in front of it) until one has Coke again.
                w => CokerOutOfCoke ? Boiler2OrFirst
                    : Pressers.Any(g => IsCoker(g) && g.Cell == CokerSpot) ? CokerSpot
                    : Pressers.Where(IsCoker).Select(g => (Vector2Int?)g.Cell).FirstOrDefault() ?? CokerSpot,
                w => CokerOutOfCoke ? "Boilers dry: load Coke  " + Count(ItemType.Coke, 1) : "",
                spot: CokerSpot,
                spotFacing: Facing.North),

            // --- Chapter 4: copies, turning, and a stall on purpose --------------------------------
            // The playtest script's Part C: a patent stamped onto a second golem, R to turn it, and
            // a golem stalled deliberately to see its badge name the problem (Part I1).

            new TutorialStep(
                "patent", "Patent a program",
                "Retyping a program for every golem is the chore a patent removes. Press E at your first "
                + "Scavenger to open its Workbench, and press PATENT to save its program.",
                w => _world.Patents.Blueprints.Count > 0,
                w => FirstGolem,
                workbench: true),

            new TutorialStep(
                "scav2", "Build a second Scavenger",
                "At the construction station, build another Clockwork Scavenger (12 Scrap).",
                w => Scavengers.Count() >= 2,
                w => ScrapFirst(12, Station),
                w => Count(ItemType.Scrap, 12)),

            new TutorialStep(
                "stamp", "Stamp the patent onto it",
                "With the new golem in the Workbench, press Tab, open Patents and click Load on your patent, "
                + "then pull ENGAGE. The same program, without dragging a card.",
                w => SecondScavenger != null && IsProgrammed(SecondScavenger),
                w => SecondScavenger != null ? SecondScavenger.Cell : (Vector2Int?)null,
                workbench: true),

            new TutorialStep(
                "depot3", "A depot beside the stall",
                "Build a Depot on the marked tile, two east of the Scrap stall.",
                w => Built.Any(b => b.Cell == Depot3Spot && b.GetPart<PlaceableDepot>() != null),
                w => ScrapFirst(15, Depot3Spot),
                w => Count(ItemType.Scrap, 15),
                menuKey: "DepotPrefab",
                spot: Depot3Spot),

            new TutorialStep(
                "turn", "Turn it with R",
                "Move the new Scavenger onto the marked tile beside the Scrap stall (G). This one must face "
                + "EAST, at the depot: stand next to it and press R to turn it. It needs the stall behind it.",
                w => SecondScavenger != null && _completedSinceEntry.Contains(SecondScavenger.GolemId),
                w => SecondScavenger != null && SecondScavenger.Cell != Scav2Spot ? SecondScavenger.Cell : Scav2Spot,
                spot: Scav2Spot,
                spotFacing: Facing.East),

            new TutorialStep(
                "stall", "Stall it on purpose",
                "Golems never improvise. Press R by the new Scavenger until it faces up, at the boiler. With "
                + "the street behind it instead of the stall, it stops, and its badge says what is missing.",
                w =>
                {
                    // Any stall of chapter 4's golem: which way the player turned it decides the
                    // reason, and every reason's badge names the problem.
                    GolemEntity scav2 = SecondScavenger;
                    if (scav2 != null && scav2.Program.State == GolemState.Stalled)
                    {
                        _stalledGolemId = scav2.GolemId;
                        return true;
                    }
                    return false;
                },
                w => SecondScavenger?.Cell),

            new TutorialStep(
                "unstall", "And back to work",
                "Turn it back to face the depot (R). A stalled golem waits rather than skipping ahead, and "
                + "picks up the moment its tile is right again.",
                w => _stalledGolemId != null && _completedSinceEntry.Contains(_stalledGolemId),
                w => LiveGolems.Where(g => g.GolemId == _stalledGolemId).Select(g => (Vector2Int?)g.Cell).FirstOrDefault(),
                spot: Scav2Spot,
                spotFacing: Facing.East),

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
                "Two inputs need a bigger frame. Build an Aether-Hauler at the station (80 Iron Plate + 40 Gear "
                + "+ 30 Coke): four slots, enough for two Hauls, an Assemble and a Push.",
                w => Haulers.Any(),
                w => Station,
                w => Counts((ItemType.IronPlate, 80), (ItemType.Gear, 40), (ItemType.Coke, 30))),

            new TutorialStep(
                "smelt-depots", "Depots for the smelter",
                "Build two Depots on the marked tiles, east of the coal line's boiler: one behind the smelter "
                + "for its Scrap and Coke, one in front for its Iron Plate.",
                w => new[] { SmeltInSpot, SmeltOutSpot }.All(c => Built.Any(b => b.Cell == c && b.GetPart<PlaceableDepot>() != null)),
                w => new[] { SmeltInSpot, SmeltOutSpot }.Where(c => !Built.Any(b => b.Cell == c)).Select(c => (Vector2Int?)c).FirstOrDefault(),
                w => Count(ItemType.Scrap, 30),
                menuKey: "DepotPrefab",
                moreSpots: new[] { SmeltInSpot, SmeltOutSpot }),

            new TutorialStep(
                "pipes3", "Steam for the column",
                "Lay Steam Pipe on the three marked tiles, from beside the coal line's boiler up the column. "
                + "The smelter and the carrier you build next both stand beside it. Any route that gets steam to "
                + "both their tiles counts.",
                w => SteamReaches(SmelterSpot) && SteamReaches(CarrierSpot),
                w => Pipe3Spots.Where(c => !_world.Steam.HasPipe(c)).Select(c => (Vector2Int?)c).FirstOrDefault(),
                w => $"Pipes  {Pipe3Spots.Count(c => _world.Steam.HasPipe(c))} / {Pipe3Spots.Length}",
                menuKey: "SteamPipePrefab",
                moreSpots: Pipe3Spots),

            new TutorialStep(
                "program-smelter", "Two Hauls, two goods",
                "In the Workbench: Always On, then Haul, Haul, Assemble Iron Smelting and Push Output. On the "
                + "second Haul, click the arrow until it says Coke. Set the first to 2: the recipe takes 2 Scrap and 1 Coke.",
                w => Haulers.Any(IsSmelter),
                w => Haulers.Select(g => (Vector2Int?)g.Cell).FirstOrDefault(),
                workbench: true),

            new TutorialStep(
                "work-smelter", "Smelt",
                "Put the Aether-Hauler on the marked tile between the two depots, facing up. Watch what it "
                + "pushes: Iron Plate, and Slag with it.",
                w => Haulers.Any(g => IsSmelter(g) && _completedSinceEntry.Contains(g.GolemId)),
                w => Haulers.Any(g => g.Cell == SmelterSpot) ? SmelterSpot
                    : Haulers.Where(IsSmelter).Select(g => (Vector2Int?)g.Cell).FirstOrDefault() ?? SmelterSpot,
                spot: SmelterSpot,
                spotFacing: Facing.North),

            new TutorialStep(
                "slag-heap", "Somewhere for the Slag",
                "Slag piles up every cycle, and a full store stalls the smelter. Build a Slag Heap on the "
                + "marked tile (20 Scrap + 10 Iron Plate): it burns 1 Coke for every 4 Slag.",
                w => HeapAt(SlagHeapSpot) != null,
                w => ScrapFirst(20, SlagHeapSpot),
                w => Counts((ItemType.Scrap, 20), (ItemType.IronPlate, 10)),
                menuKey: "SlagHeapPrefab",
                spot: SlagHeapSpot),

            new TutorialStep(
                "carrier", "A carrier",
                "Build another Brass Presser at the station. It will make nothing: it carries.",
                w => Pressers.Count() >= 3,
                w => ScrapFirst(60, Station),
                w => Counts((ItemType.Scrap, 60), (ItemType.IronPlate, 20), (ItemType.Gear, 10))),

            new TutorialStep(
                "program-carrier", "Carry Slag and its fuel",
                "Program it: Always On, Haul set to Slag with the dial at 4, Haul set to Coke at 1, then Push "
                + "Output. Four Slag and the one Coke that burns them, every trip.",
                w => Pressers.Any(IsCarrier),
                w => NewestPresser?.Cell,
                workbench: true),

            new TutorialStep(
                "work-carrier", "Clear the Slag",
                "Put the carrier on the marked tile between depot and heap, facing up. When the heap starts "
                + "burning Slag, the iron line is safe.",
                // Any heap: the carrier's job is done wherever the heap stands.
                w => Built.Any(b => b.GetPart<PlaceableSlagHeap>()?.Heap?.TotalVoided > 0),
                w => Pressers.Any(g => IsCarrier(g) && g.Cell == CarrierSpot) ? CarrierSpot
                    : Pressers.Where(IsCarrier).Select(g => (Vector2Int?)g.Cell).FirstOrDefault() ?? CarrierSpot,
                spot: CarrierSpot,
                spotFacing: Facing.North),

            // --- Chapter 6: belts, and a label ---------------------------------------------------
            // The playtest script's belt and labelled-depot items (F, I2): a belt carries goods a
            // golem pushes onto it, a golem must take them off its end, and a label sorts.

            new TutorialStep(
                "copper", "Buy Copper Ore",
                "Press E at the empty Copper stall to order a truckload (20 Scrap). When it arrives, take one "
                + "by hand with E: a depot can only be labelled with a good you have.",
                w => Stock(ItemType.CopperOre) >= 1,
                w => Stall("CopperOreNode"),
                w => Count(ItemType.CopperOre, 1)),

            new TutorialStep(
                "belts", "Lay a belt",
                "Pick Belt in the build menu and DRAG from the tile above the Copper stall's front up the "
                + "marked run: a dragged run points along itself, here north.",
                w => BeltsLaid,
                w => BeltSpots.Where(c => !_world.Belts.HasBelt(c)).Select(c => (Vector2Int?)c).FirstOrDefault() ?? BeltSpots[0],
                w => $"Belts  {BeltSpots.Count(c => _world.Belts.HasBelt(c))} / {BeltSpots.Length}",
                menuKey: "BeltPrefab",
                moreSpots: BeltSpots),

            new TutorialStep(
                "pipes4", "Drag a pipe run",
                "Steam Pipe drags too. Lay it along the marked run, up beside the belt and across to the "
                + "smelter's pipes: one long stroke per straight stretch. Any route that steams the extractor's "
                + "and the unloader's tiles counts.",
                w => SteamReaches(ExtractorSpot) && SteamReaches(UnloaderSpot),
                w => Pipe4Spots.Where(c => !_world.Steam.HasPipe(c)).Select(c => (Vector2Int?)c).FirstOrDefault(),
                w => $"Pipes  {Pipe4Spots.Count(c => _world.Steam.HasPipe(c))} / {Pipe4Spots.Length}",
                menuKey: "SteamPipePrefab",
                moreSpots: Pipe4Spots),

            new TutorialStep(
                "scav3", "An extractor",
                "Build a Scavenger and program it Extract, Push Output: it will push ore onto the belt.",
                w => CopperExtractor != null && IsProgrammed(CopperExtractor),
                w => ScrapFirst(12, Station),
                w => Count(ItemType.Scrap, 12),
                workbench: true),

            new TutorialStep(
                "work-extractor", "Onto the belt",
                "Put it on the marked tile above the Copper stall, facing the belt (up). Watch the ore ride north.",
                w => BeltSpots.Any(c => _world.Belts.TryGetBelt(c, out PlacedBelt belt) && belt.Segment.Items.Count > 0),
                w => CopperExtractor != null && CopperExtractor.Cell != ExtractorSpot ? CopperExtractor.Cell : ExtractorSpot,
                spot: ExtractorSpot,
                spotFacing: Facing.North),

            new TutorialStep(
                "copper-depot", "A labelled depot",
                "A belt only hands goods to another belt, so the ore stops at the end. Build a Depot on the "
                + "marked tile past it, then stand by it and press E until its label reads Copper Ore.",
                w => DepotAt(CopperDepotSpot)?.FilterItemType == ItemType.CopperOre,
                w => DepotAt(CopperDepotSpot) == null ? ScrapFirst(15, CopperDepotSpot) : CopperDepotSpot,
                w => DepotAt(CopperDepotSpot) == null ? Count(ItemType.Scrap, 15) : "Label: " + DepotAt(CopperDepotSpot).FilterLabel,
                menuKey: "DepotPrefab",
                spot: CopperDepotSpot),

            new TutorialStep(
                "unloader", "An unloader",
                "Build one more Scavenger and program it Haul, set to Copper Ore, then Push Output. It takes "
                + "ore off the belt's end and pushes it into the labelled depot.",
                w => CopperUnloader != null && HaulsOf(CopperUnloader, ItemType.CopperOre) > 0
                    && CopperUnloader.Program.appendages.Any(a => a?.name == "PushOutput"),
                w => ScrapFirst(12, Station),
                w => Count(ItemType.Scrap, 12),
                workbench: true),

            new TutorialStep(
                "work-unloader", "Off the belt",
                "Put it on the marked tile at the belt's end, facing the depot (up). Belt, golem, labelled "
                + "depot: the shape of every long haul.",
                w => CopperUnloader != null && _completedSinceEntry.Contains(CopperUnloader.GolemId),
                w => CopperUnloader != null && CopperUnloader.Cell != UnloaderSpot ? CopperUnloader.Cell : UnloaderSpot,
                spot: UnloaderSpot,
                spotFacing: Facing.North),

            // --- Chapter 7: room to grow, and keeping it -------------------------------------------
            // The playtest script's Floor Expansion, bay cap, card-gating and Ledger items (F, G,
            // I4), and its save/load round trip (H).

            new TutorialStep(
                "expand", "More room",
                "Eight golems fill a workshop fast. Press Tab, open the Assembly Line, and click Extend "
                + "(80 Scrap + 40 Iron Plate): two more rows of workshop, floored and walled.",
                w => _world.Bounds.NorthExtent > _world.Bounds.MinNorthExtent,
                w => ScrapFirst(80, null),
                w => Counts((ItemType.Scrap, 80), (ItemType.IronPlate, 40))),

            new TutorialStep(
                "bays", "More golems",
                $"The station builds at most {AssemblyBayStructure.DefaultSlots} golems. On the same tab, click "
                + "Upgrade on the assembly bays for six more.",
                w => _world.AssemblyBay.Tier >= 2,
                w => ScrapFirst(40, null),
                w => $"Golems  {LiveGolems.Count()} / {_world.AssemblyBay.MaxGolemSlots}"),

            new TutorialStep(
                "ledger", "Read the Ledger",
                "The Ledger maps everything you can build and how far you have come. Press Tab, open the "
                + "Ledger, and click any recipe node to see what it costs and makes.",
                w => _world.LedgerReadout.HasSelection,
                w => null),

            new TutorialStep(
                "save", "Save",
                "Press Tab, open Save/Load and click Save. Everything you built goes into the save: golems "
                + "and their programs, buildings, the stockpile, your cards and this guide's place.",
                w => _world.SavesMade > 0,
                w => null),

            new TutorialStep(
                "load", "And load",
                "Now click Load. Your factory comes back exactly as you saved it, mid-cycle and all.",
                w => _world.LoadsMade > 0,
                w => null),

            // --- Chapter 8: Brass, and the goods beyond --------------------------------------------
            // The playtest script's tier-2 and tier-3 chain (Parts F and G): every good the Zeppelin
            // costs. Goal steps, no marked tiles.

            Goal("copper-ingot", "Smelt Copper",
                "From here the guide names a good and its card, and you choose where to build. Claim Copper "
                + "Smelting and set an Aether-Hauler on it: Haul Copper Ore and Coke, Assemble, Push.",
                ItemType.CopperIngot, "AssembleCopperSmelting", "2 Copper Ore + 1 Coke → 1 Copper Ingot"),

            Goal("zinc-ingot", "Smelt Zinc",
                "Order Zinc Ore at the Zinc stall, claim Zinc Smelting, and smelt it the same way.",
                ItemType.ZincIngot, "AssembleZincSmelting", "2 Zinc Ore + 1 Coke → 1 Zinc Ingot", "ZincOreNode"),

            Goal("brass", "Alloy Brass",
                "Brass is two ingots in one: claim Brass Alloying and feed one golem Copper and Zinc Ingot.",
                ItemType.Brass, "AssembleBrassAlloying", "2 Copper Ingot + 1 Zinc Ingot → 1 Brass"),

            Goal("casing", "Press Casings",
                "Claim Casing Press. A Brass Presser can run it: Iron Plate and Brass in, a Casing out.",
                ItemType.Casing, "AssembleCasingPress", "4 Iron Plate + 1 Brass → 1 Casing"),

            Goal("glass", "Glass from Slag",
                "Your smelters' Slag is not only waste. Claim Glassmaking, and send some Slag to a golem "
                + "running it instead of the heap.",
                ItemType.Glass, "AssembleGlassmaking", "1 Slag → 1 Glass"),

            Goal("lens", "Grind a Lens",
                "Claim Lens Grinding: Glass and Brass make a Lens.",
                ItemType.Lens, "AssembleLensGrinding", "2 Glass + 1 Brass → 1 Lens"),

            Goal("mainspring", "Wind a Mainspring",
                "Claim Mainspring Winding: Brass and Gears make a Mainspring.",
                ItemType.Mainspring, "AssembleMainspringWinding", "3 Brass + 2 Gear → 1 Mainspring"),

            Goal("aether-cell", "Bottle the Aether",
                "Order Aether at the Aether stall, claim Aether Containment, and seal it behind Lenses.",
                ItemType.AetherCell, "AssembleAetherContainment", "1 Aether + 2 Lens → 1 Aether Cell", "AetherNode"),

            // --- Chapter 9: the Zeppelin ---------------------------------------------------------------
            // The playtest script's Freight Link (Part G): a Zeppelin flies goods to a mast in one
            // flat 24-tick hop, however far.

            new TutorialStep(
                "zeppelin-card", "Claim the Zeppelin",
                "Everything it costs is in your stockpile now. Press Tab, open the Assembly Line and claim "
                + "the Zeppelin Freight Loader.",
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
                "At the construction station, build a Zeppelin Freight Loader: 6 Mainspring, 8 Lens, 3 Aether "
                + "Cell, 30 Casing and 40 Brass.",
                w => Zeppelin != null,
                w => ScrapFirst(1, Station),
                w => Counts((ItemType.Mainspring, 6), (ItemType.Lens, 8), (ItemType.AetherCell, 3), (ItemType.Casing, 30), (ItemType.Brass, 40))),

            new TutorialStep(
                "mast", "Raise a Freight Mast",
                "A Zeppelin flies to a Freight Mast, and the mast lands what it brings in the stockpile. "
                + "Build one on the marked tile (20 Brass + 10 Casing); anywhere would do.",
                w => HasMast,
                w => HasMast ? (Vector2Int?)null : MastSpot,
                w => Counts((ItemType.Brass, 20), (ItemType.Casing, 10)),
                menuKey: "FreightMastPrefab",
                spot: MastSpot),

            new TutorialStep(
                "zeppelin-pipe", "Steam for it",
                "One more pipe, on the marked tile at the top of the copper line's run: anything that gets "
                + "steam to the Zeppelin's tile counts.",
                w => SteamReaches(ZeppelinSpot),
                w => ZeppelinPipeSpot,
                menuKey: "SteamPipePrefab",
                spot: ZeppelinPipeSpot),

            new TutorialStep(
                "program-zeppelin", "Program the Zeppelin",
                "Always On, Haul set to Copper Ore, then Freight Launch: it flies whatever it holds to the mast.",
                w => Zeppelin != null && HaulsOf(Zeppelin, ItemType.CopperOre) > 0
                    && Zeppelin.Program.appendages.Any(a => a?.name == "FreightLaunch"),
                w => null,
                workbench: true),

            new TutorialStep(
                "launch", "Fly the Copper home",
                "Put it on the marked tile past the labelled Copper depot, with the depot BEHIND it (facing "
                + "up): it hauls from behind, like every golem. Each flight takes 24 ticks, however far the mast.",
                w => Zeppelin != null && _completedSinceEntry.Contains(Zeppelin.GolemId),
                w => Zeppelin != null && Zeppelin.Cell != ZeppelinSpot ? Zeppelin.Cell : ZeppelinSpot,
                spot: ZeppelinSpot,
                spotFacing: Facing.North),

            // --- Chapter 10: the Clock Tower ------------------------------------------------------------
            // The endgame (G10, the user's call): the tower in the town square, roped off until a
            // Zeppelin flew, now open. It is built in stages, each demanding a steady RATE of a
            // megaproject good rather than a pile of it.

            new TutorialStep(
                "tower-visit", "The town square",
                "Your Zeppelin flew, so the rope is down. Walk out of the workshop, down the street and into "
                + "the town square to the south: the Clock Tower's site is waiting.",
                w => PlayerInSquare,
                w => TownSquare.TowerCentre),

            Goal("frame-section", "Frame Sections",
                "Stage 1, the Foundation, wants Frame Sections, and keeps wanting them: 6 a minute, every minute, "
                + "until it is built. Claim the card and start a line.",
                ItemType.FrameSection, "AssembleFrameSection", "10 Casing + 6 Iron Plate + 4 Brass → 1 Frame Section"),

            new TutorialStep(
                "tower-feed", "Feed the tower",
                "Get Frame Sections to the tower: a golem beside its footprint, pushing into it, from any side. "
                + "A Zeppelin can fly them out to a mast beside the square.",
                w => TowerSite.DeliveryRatePerMinute(ItemType.FrameSection) > 0,
                w => TownSquare.TowerCentre),

            new TutorialStep(
                "stage1", "Lay the Foundation",
                "The tower builds at the rate you feed it, and it only counts Frame Sections your line is making "
                + "now: a stockpile emptied into it builds nothing. Short of 6 a minute it still builds, just "
                + "slower. Keep the line running until the Foundation is laid.",
                w => TowerSite.StageIndex >= 1 || TowerSite.IsComplete,
                w => TownSquare.TowerCentre,
                w => ClockTower.ClockTowerReadout.FormatHeadline(TowerSite.BuildReading())),

            new TutorialStep(
                "done", "A factory that feeds itself",
                "Scrap, Iron Plate and Coke now run without you, and a patent copies a program in a click. "
                + "Keep the Coal stall stocked (E when it runs dry). Tab shows your Inventory, the Assembly Line "
                + "and the Ledger, which maps the road ahead: the Aether-Hauler and two-input recipes. "
                + "F1 brings this guide back.",
                w => false,
                w => null),
        };
    }
}
