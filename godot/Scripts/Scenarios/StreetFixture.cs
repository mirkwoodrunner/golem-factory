using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.Buildings;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.World;
using CoreVector2Int = GolemFactory.Compat.Vector2Int;
using CoreVector3 = GolemFactory.Compat.Vector3;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// The street the `moods` and `hover` scenarios share (docs/onboarding-feedback-design.md
    /// §8): a boiler and a pipe main along y=0 power a row of golems on y=1, each between the
    /// crates that make it waiting, stuck or straining.
    ///
    /// <code>
    ///  y=1   C S> C P> C                       W> C              (all facing East)
    ///  y=0   ~ ~ ~ ~ ~ ~ ~ ~ ~ B ~ ~ ~ ~ ~ ~ ~ ~ ~                 pipes
    ///  y=-1                N>    C F> C(full) H> C
    ///
    /// Two rows, so the badges of neighbours don't cover each other in the frames.
    /// </code>
    ///
    /// <list type="bullet">
    ///   <item>S, a Scavenger on a trigger that never comes: asleep, it feeds P's crate "some day".</item>
    ///   <item>P hauls Scrap from the crate S pushes into: <b>waiting</b>, grey.</item>
    ///   <item>N faces bare floor: <b>stuck</b>, red, NoSourceAtTile.</item>
    ///   <item>F pushes into a full crate nothing hauls from: red; H, programmed later, hauls from it.</item>
    ///   <item>W assembles Coking from 10 Coal in its hold: running at 10 of 12, <b>straining</b>.</item>
    ///   <item>X stands far from any pipe: <b>starved</b>, blue.</item>
    /// </list>
    ///
    /// Setup goes through the Core calls the player's gestures reach, as the `golems` scenario
    /// does; the gestures under test (hover, G, E) are real input.
    /// </summary>
    public abstract class StreetFixture : IScenario
    {
        protected const int FramesBetweenSteps = 3;

        private readonly Queue<(string name, Func<string> step, double timeout, bool mayTimeOut)> _steps =
            new Queue<(string, Func<string>, double, bool)>();
        protected readonly List<string> Log = new List<string>();
        protected SandboxWorld World;
        protected SceneTree Tree;
        protected Viewport Viewport;
        protected PlayerNode Player;
        private ConstructionPanelNode _panel;
        private int _wait;
        private double _waited;
        private double _stepElapsed;
        private Key? _heldTap;

        protected GolemEntity S, P, N, F, H, W, X;

        protected readonly CoreVector2Int SourceOfS = new CoreVector2Int(-9, 1);
        protected readonly CoreVector2Int PCrate = new CoreVector2Int(-7, 1);
        protected readonly CoreVector2Int FullCrate = new CoreVector2Int(2, -1);

        public void Begin(ScenarioRunner runner)
        {
            Tree = runner.GetTree();
            World = runner.World.Sandbox;
            Viewport = runner.GetViewport();
            Player = Tree.Root.FindChild("Player", true, false) as PlayerNode;
            _panel = Tree.Root.FindChild("ConstructionPanel", true, false) as ConstructionPanelNode;
            if (Player == null || _panel == null || World?.Setup == null)
            {
                Do("scene", () => "Sandbox.tscn is missing the player, construction panel or setup");
                return;
            }
            try
            {
                Setup();
            }
            catch (Exception e)
            {
                Do("setup", () => e.Message);
                return;
            }
            Stand(-2f, 3.5f);
            Plan();
        }

        /// <summary>The scenario's own steps, queued with <see cref="Do"/>, <see cref="Until"/> and <see cref="Wait"/>.</summary>
        protected abstract void Plan();

        public ScenarioResult? Step(double delta)
        {
            // The pointer stays where the scenario put it: with a window up (--write-movie), the
            // real mouse would otherwise take it back between steps.
            if (_pointer != null)
            {
                Warp(_pointer());
            }
            if (_heldTap is Key held)
            {
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = held, Keycode = held, Pressed = false });
                _heldTap = null;
            }
            if (_wait-- > 0)
            {
                return null;
            }
            if (_waited > 0)
            {
                _waited -= delta;
                return null;
            }
            if (_steps.Count == 0)
            {
                return new ScenarioResult(true, string.Join("; ", Log));
            }

            (string name, Func<string> step, double timeout, bool mayTimeOut) = _steps.Peek();
            string failure = step();
            if (failure != null && timeout > 0 && _stepElapsed < timeout)
            {
                _stepElapsed += delta;
                return null; // keep asking until it holds or times out
            }
            _steps.Dequeue();
            _stepElapsed = 0;
            if (failure != null && !mayTimeOut)
            {
                return new ScenarioResult(false, $"{name}: {failure}");
            }
            _wait = FramesBetweenSteps;
            return null;
        }

        protected void Do(string name, Func<string> step) => _steps.Enqueue((name, step, 0, false));

        /// <summary>
        /// A check asked every frame until it holds, failing after <paramref name="seconds"/> --
        /// or, with <paramref name="mayTimeOut"/>, a watch that simply runs that long.
        /// </summary>
        protected void Until(string name, double seconds, Func<string> check, bool mayTimeOut = false) =>
            _steps.Enqueue((name, check, seconds, mayTimeOut));

        protected void Wait(double seconds) => Do("wait " + seconds + " s", () => { _waited = seconds; return null; });

        protected void Stand(float x, float y) => Player.TeleportTo(new CoreVector3(x, y, 0f));

        /// <summary>A real key tap: down now, up a frame later, unflushed (root CLAUDE.md).</summary>
        protected void Tap(Key key)
        {
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true });
            _heldTap = key;
        }

        /// <summary>Moves the real pointer to a screen point (warped: headless has no mouse).</summary>
        protected void PointAt(Vector2 at)
        {
            _pointer = () => at;
            Warp(at);
        }

        /// <summary>Points at a cell, and keeps pointing at it as the camera moves.</summary>
        protected void PointAtCell(CoreVector2Int cell)
        {
            _pointer = () => Viewport.GetCanvasTransform() * GridConversions.CellToWorld(cell);
            Warp(_pointer());
        }

        private Func<Vector2> _pointer;
        private Vector2? _lastWarp;

        private void Warp(Vector2 at)
        {
            if (_lastWarp == at)
            {
                return;
            }
            _lastWarp = at;
            Viewport.WarpMouse(at);
            // Flushed, then disposed at once: a scenario re-points every frame while it waits,
            // and hundreds of wrappers left to the finalizer crashed the engine on quit.
            using var motion = new InputEventMouseMotion { Position = at, GlobalPosition = at };
            Input.ParseInputEvent(motion);
            Input.FlushBufferedEvents();
        }


        protected GolemNode NodeOf(GolemEntity golem) =>
            Tree.GetNodesInGroup(GolemNodeGroup.Name).OfType<GolemNode>().FirstOrDefault(n => n.Entity == golem);

        protected static string Is(GolemNode node, GolemMood mood) =>
            node == null ? "no node" : node.DrawnMood == mood ? null : $"drawn {node.DrawnMood}, wanted {mood} ({node.Entity.StallReason})";

        // --- Setup -------------------------------------------------------------------------

        private void Setup()
        {
            DefinitionSet defs = World.Definitions;
            PlaceableBoiler boiler = Place<PlaceableBoiler>("BoilerPrefab", 0, 0);
            for (int x = -9; x <= 8; x++)
            {
                if (x != 0)
                {
                    Place<PlaceableSteamPipe>("SteamPipePrefab", x, 0);
                }
            }
            Deposit(ItemType.Coke, 100);
            if (!World.Interactor.TryRefuelBoiler(boiler))
            {
                throw new InvalidOperationException("refuel: " + World.Interactor.LastStatusMessage);
            }

            Place<PlaceableDepot>("DepotPrefab", SourceOfS.x, SourceOfS.y);   // S's: empty, nothing feeds it
            Place<PlaceableDepot>("DepotPrefab", PCrate.x, PCrate.y);         // between S and P
            Place<PlaceableDepot>("DepotPrefab", -5, 1);                      // P's output
            Feed(Place<PlaceableDepot>("DepotPrefab", 0, -1), ItemType.Scrap, 50);
            Feed(Place<PlaceableDepot>("DepotPrefab", FullCrate.x, FullCrate.y), ItemType.Scrap, 50);
            Place<PlaceableDepot>("DepotPrefab", 4, -1);                       // H's output
            Place<PlaceableDepot>("DepotPrefab", 7, 1);                       // W's output

            S = Build("ClockworkScavenger");
            P = Build("ClockworkScavenger");
            N = Build("ClockworkScavenger");
            F = Build("ClockworkScavenger");
            H = Build("ClockworkScavenger");
            W = Build("ClockworkScavenger");
            X = Build("ClockworkScavenger");
            (World.WorkbenchScreen as WorkbenchScreen)?.Close();

            // S sleeps on a trigger that won't come in this run, so P's crate is fed "some day":
            // the probe counts S as a feeder, and nothing arrives.
            Program(S, -8, 1, ("HaulScrap", 1), ("PushOutput", 0));
            S.Program.logicCore = new LogicCoreDefinition { name = "Never", triggerType = TriggerType.Interval, intervalTicks = 1000003 };
            Program(P, -6, 1, ("HaulScrap", 1), ("PushOutput", 0));
            Program(W, 6, 1, ("AssembleCoking", 0), ("PushOutput", 0));
            W.Inventory.AddInput(ItemType.Coal, 12);
            // The rest stand unprogrammed (violet, uncounted) until ProgramTheStuck, so a
            // scenario can first see a street where nothing needs the player.
            N.SetPlacement(new CoreVector2Int(-3, -1), Facing.East);
            F.SetPlacement(new CoreVector2Int(1, -1), Facing.East);
            H.SetPlacement(new CoreVector2Int(3, -1), Facing.East);           // programmed by the scenario
            X.SetPlacement(new CoreVector2Int(9, 6), Facing.East);
        }

        /// <summary>N faces bare floor, F pushes into a full crate, X stands off the steam grid.</summary>
        protected void ProgramTheStuck()
        {
            Program(N, -3, -1, ("HaulScrap", 1), ("PushOutput", 0));
            Program(F, 1, -1, ("HaulScrap", 1), ("PushOutput", 0));
            Program(X, 9, 6, ("HaulScrap", 1), ("PushOutput", 0));           // no steam here
        }

        protected void Program(GolemEntity golem, int x, int y, params (string card, int quantity)[] steps)
        {
            DefinitionSet defs = World.Definitions;
            golem.SetPlacement(new CoreVector2Int(x, y), Facing.East);
            golem.Program.logicCore = defs.LogicCores["AlwaysOnCore"];
            foreach ((string card, int quantity) in steps)
            {
                if (!golem.Program.TryAddAppendage(defs.Appendages[card]))
                {
                    throw new InvalidOperationException($"{golem.GolemId} refused {card}");
                }
                if (quantity > 0)
                {
                    golem.Program.SetQuantityAt(golem.Program.appendages.Count - 1, quantity);
                }
            }
        }

        private GolemEntity Build(string chassis)
        {
            ChassisDefinition definition = World.Definitions.Chassis[chassis];
            Pay(definition.cost);
            _panel.Open(World.StarterStation);
            return _panel.TryConstruct(definition) ? _panel.LastBuilt : throw new InvalidOperationException($"build {chassis}: {_panel.Status}");
        }

        protected T Place<T>(string key, int x, int y) where T : class
        {
            var cell = new CoreVector2Int(x, y);
            PlaceableBuilding prefab = World.Placeables.Single(p => p.Key == key).Prefab;
            Pay(prefab.Cost);
            World.Build.SetActivePrefab(prefab);
            World.Build.PlaceOrRemove(cell);
            World.Build.CancelPlacement();
            PlaceableBuilding placed = World.Build.Buildings.SingleOrDefault(b => b.Cell == cell && !b.IsRemoved)
                ?? throw new InvalidOperationException($"could not place {key} at {cell}: {World.Build.LastStatusMessage}");
            return placed.GetPart<T>();
        }

        protected void Deposit(string itemType, int quantity) => World.Buffers.Deposit(World.StockpileBufferId, itemType, quantity);

        private void Pay(IEnumerable<RecipeIngredient> cost)
        {
            foreach (RecipeIngredient c in cost)
            {
                Deposit(c.itemType, c.quantity);
            }
        }

        protected PlaceableDepot CrateAt(CoreVector2Int cell) =>
            World.Build.Buildings.First(b => b.Cell == cell && !b.IsRemoved).GetPart<PlaceableDepot>();

        protected void Feed(ICrate crate, string itemType, int quantity)
        {
            Deposit(itemType, quantity);
            CrateTransfer.Put(World.Wallet.Pack, crate, itemType, quantity);
        }
    }
}
