using System;
using System.Collections.Generic;
using System.Linq;
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

        public Facing? SpotFacing { get; }

        internal Func<SandboxWorld, bool> Done { get; }
        internal Func<SandboxWorld, string> ProgressText { get; }
        internal Func<SandboxWorld, Vector2Int?> Target { get; }

        internal TutorialStep(
            string id, string title, string body, Func<SandboxWorld, bool> done,
            Func<SandboxWorld, Vector2Int?> target, Func<SandboxWorld, string> progress = null, string menuKey = null,
            Vector2Int? spot = null, Facing? spotFacing = null)
        {
            Spot = spot;
            SpotFacing = spotFacing;
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
    public sealed class TutorialGuide
    {
        private readonly SandboxWorld _world;
        private readonly List<TutorialStep> _steps;
        private bool _cycleSeen;

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

        /// <summary>Bumped whenever the step changes, so a view redraws on change only.</summary>
        public int Version { get; private set; }

        /// <summary>Moves past every step the world already shows done. Cheap; called each frame.</summary>
        public void Update()
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

        /// <summary>A load: the saved step and whether the guide was put away.</summary>
        public void Restore(int index, bool dismissed)
        {
            Dismissed = dismissed;
            Enter(Math.Max(0, Math.Min(index, _steps.Count)));
        }

        private void Enter(int index)
        {
            Index = index;
            _cycleSeen = false;
            Version++;
        }

        private void OnGolemCompleted(GolemCompletedEvent e)
        {
            // Only this world's golems: tests compose many worlds on one static bus.
            if (_world.Golems.Any(g => g != null && !g.IsRemoved && g.GolemId == e.GolemId))
            {
                _cycleSeen = true;
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

        private bool SteamReachesGolemSpot => _world.Steam.Reaches(GolemSpot, _world.Clock.CurrentTick);

        private IEnumerable<PlaceableBuilding> Built => _world.Build.Buildings.Where(b => !b.IsRemoved);

        private IEnumerable<PlaceableBuilding> BoilerBuildings => Built.Where(b => b.GetPart<PlaceableBoiler>() != null);

        private IEnumerable<PlaceableBoiler> Boilers => BoilerBuildings.Select(b => b.GetPart<PlaceableBoiler>());

        private IEnumerable<GolemEntity> LiveGolems => _world.Golems.Where(g => g != null && !g.IsRemoved);

        private bool IsProgrammed(GolemEntity golem) =>
            golem.Program?.logicCore != null
            && golem.Program.appendages.Any(a => a != null && a.name == "ExtractScrap")
            && golem.Program.appendages.Any(a => a != null && a.name == "PushOutput");

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
                w => FirstGolem),

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
                "Press G by the golem to pick it up, then G again to set it down on the marked tile, between the "
                + "Scrap stall and the depot. Press R by it until it faces the depot (up): it takes Scrap from "
                + "behind and pushes it forward.",
                w => _cycleSeen,
                w => LiveGolems.Any(g => g.Cell == GolemSpot) ? GolemSpot
                    : LiveGolems.Where(IsProgrammed).Select(g => (Vector2Int?)g.Cell).FirstOrDefault() ?? GolemSpot,
                spot: GolemSpot,
                spotFacing: Facing.North),

            new TutorialStep(
                "done", "Your factory is running",
                "Press Tab for the Management screen: your Inventory, the Assembly Line (claim new cards to "
                + "program with), and the Ledger (what to build next). F1 brings this guide back.",
                w => false,
                w => null),
        };
    }
}
