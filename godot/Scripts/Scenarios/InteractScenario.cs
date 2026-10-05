using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.Economy;
using GolemFactory.PunchCards;
using GolemFactory.World;
using CoreVector3 = GolemFactory.Compat.Vector3;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// The player's hands in the Sandbox, driven with real key and mouse events: what the G5
    /// hands-on check found missing ("the carts don't tell you if you can get resources, and
    /// the bench doesn't have an interface").
    ///
    /// <list type="number">
    ///   <item>At the free Scrap stall: the ring and the "[E] ..." caption show; E harvests one
    ///   Scrap and a "+1 Scrap" popup rises.</item>
    ///   <item>At an empty Coal stall with 10 Scrap: E orders a truckload and pays for it.</item>
    ///   <item>At the starter bench: the Hand-Crank panel shows its readout; holding E cranks.</item>
    ///   <item>At the starter station: E opens "Construct Golem" with a row per chassis; clicking
    ///   the Scavenger's row builds one and closes the panel; Escape would close it too.</item>
    ///   <item>G picks the new golem up and G puts it down again.</item>
    ///   <item>Out of reach of everything: no prompt.</item>
    /// </list>
    /// </summary>
    public sealed class InteractScenario : IScenario
    {
        private const int FramesBetweenSteps = 4;

        private readonly Queue<(string name, Func<string> step)> _steps = new Queue<(string, Func<string>)>();
        private readonly List<string> _log = new List<string>();
        private SandboxWorld _world;
        private PlayerNode _player;
        private InteractionPromptNode _prompt;
        private HandCrankPanelNode _crank;
        private ConstructionPanelNode _construction;
        private int _wait;

        public void Begin(ScenarioRunner runner)
        {
            SceneTree tree = runner.GetTree();
            _world = runner.World.Sandbox;
            _player = tree.Root.FindChild("Player", true, false) as PlayerNode;
            _prompt = tree.Root.FindChild("InteractionPrompt", true, false) as InteractionPromptNode;
            _crank = tree.Root.FindChild("HandCrankPanel", true, false) as HandCrankPanelNode;
            _construction = tree.Root.FindChild("ConstructionPanel", true, false) as ConstructionPanelNode;
            if (_player == null || _prompt == null || _crank == null || _construction == null || _world?.Setup == null)
            {
                Do("scene", () => "Sandbox.tscn is missing the player, prompt, crank panel or construction panel");
                return;
            }

            PlanStalls();
            PlanBench();
            PlanStation();
            Do("walk away from everything", () => { Stand(-9f, 9f); return null; });
            Do("no prompt out of reach", () => _prompt.Caption == "" ? null : $"still advertising '{_prompt.Caption}'");
        }

        public ScenarioResult? Step(double delta)
        {
            if (_wait-- > 0)
            {
                return null;
            }
            if (_heldTap is Key held)
            {
                Press(held, false);
                _heldTap = null;
            }
            if (_steps.Count == 0)
            {
                return new ScenarioResult(true, string.Join("; ", _log));
            }
            (string name, Func<string> step) = _steps.Dequeue();
            string failure = step();
            if (failure != null)
            {
                return new ScenarioResult(false, $"{name}: {failure}");
            }
            _wait = FramesBetweenSteps;
            return null;
        }

        private void PlanStalls()
        {
            SandboxSetup.NodeEntry scrap = _world.Setup.nodes.Single(n => n.id == "ScrapNode");
            SandboxSetup.NodeEntry coal = _world.Setup.nodes.Single(n => n.id == "CoalNode");

            Do("walk up to the free Scrap stall", () => { Stand(scrap.x, scrap.y + 1f); return null; });
            Do("the stall advertises itself", () =>
                _prompt.Caption.StartsWith("[E]") && _prompt.Caption.Contains("Scrap") ? null : $"caption '{_prompt.Caption}'");
            Do("press E", () => { Tap(Key.E); return null; });
            Do("one Scrap harvested, and said so", () =>
            {
                int got = _world.Buffers.GetQuantity(_world.StockpileBufferId, ItemType.Scrap);
                if (got != 1)
                {
                    return $"stockpile holds {got} Scrap";
                }
                if (_prompt.LivePopups == 0)
                {
                    return "no popup rose";
                }
                _log.Add($"Scrap stall: '{_prompt.Caption}', E harvested 1");
                return null;
            });

            Do("walk up to the empty Coal stall with 10 Scrap", () =>
            {
                _world.Buffers.Deposit(_world.StockpileBufferId, ItemType.Scrap, 9);
                Stand(coal.x, coal.y + 1f);
                return null;
            });
            Do("the empty stall offers a truckload", () =>
                _prompt.Caption.Length > 0 ? null : "no caption at the Coal stall");
            Do("press E", () => { Tap(Key.E); return null; });
            Do("the truckload is ordered and paid for", () =>
            {
                int left = _world.Buffers.GetQuantity(_world.StockpileBufferId, ItemType.Scrap);
                if (left != 0)
                {
                    return $"{left} Scrap left; the 10 Scrap price was not paid";
                }
                _log.Add("Coal stall: E ordered a truckload for 10 Scrap");
                return null;
            });
        }

        private void PlanBench()
        {
            SandboxSetup.Placement bench = _world.Setup.starterBench;
            Do("walk up to the starter bench", () =>
            {
                // R1 Coking needs Coal; a little in the stockpile lets the crank actually turn.
                _world.Buffers.Deposit(_world.StockpileBufferId, ItemType.Coal, 5);
                Stand(bench.x, bench.y - 1f);
                return null;
            });
            Do("the Hand-Crank panel shows", () =>
            {
                if (!_crank.IsShowing)
                {
                    return "no panel at the bench";
                }
                if (!_crank.Text.StartsWith("HAND-CRANK BENCH"))
                {
                    return $"panel reads '{_crank.Text}'";
                }
                return null;
            });
            Do("hold E", () => { Press(Key.E, true); return null; });
            Do("the handle turns", () => _world.StarterBench.IsCranking ? null : "holding E did not crank");
            Do("let go", () => { Press(Key.E, false); return null; });
            Do("the handle stops", () =>
            {
                if (_world.StarterBench.IsCranking)
                {
                    return "still cranking after release";
                }
                _log.Add("bench: panel '" + _crank.Text.Split('\n')[0] + "', hold E cranks");
                return null;
            });
        }

        private void PlanStation()
        {
            SandboxSetup.Placement station = _world.Setup.starterStation;
            ChassisDefinition scavenger = _world.Definitions.Chassis["ClockworkScavenger"];

            Do("walk up to the starter station, able to afford a Scavenger", () =>
            {
                foreach (RecipeIngredient c in scavenger.cost)
                {
                    _world.Buffers.Deposit(_world.StockpileBufferId, c.itemType, c.quantity);
                }
                Stand(station.x, station.y - 1f);
                return null;
            });
            Do("the station advertises itself", () => _prompt.Caption.Length > 0 ? null : "no caption at the station");
            Do("press E", () => { Tap(Key.E); return null; });
            Do("Construct Golem opens with the whole roster", () =>
            {
                if (!_construction.IsOpen)
                {
                    return "the panel did not open";
                }
                if (_construction.Rows.Count != _world.Definitions.Chassis.Count)
                {
                    return $"{_construction.Rows.Count} rows for {_world.Definitions.Chassis.Count} chassis";
                }
                return _prompt.Caption == "" ? null : "the world prompt still shows over the open panel";
            });
            CoreVector3 heldAt = default;
            Do("hold W while it is open", () =>
            {
                heldAt = _player.CorePosition;
                Press(Key.W, true);
                return null;
            });
            Do("the player is held while it is open", () =>
            {
                Press(Key.W, false);
                return (_player.CorePosition - heldAt).magnitude < 1e-4f ? null : "the player walked off behind the panel";
            });
            Do("click the Scavenger's row", () =>
            {
                Button row = _construction.Rows.Single(r => r.chassis == scavenger).row;
                Click(row.GetGlobalRect().GetCenter());
                return null;
            });
            Do("a Scavenger was built and the panel closed", () =>
            {
                if (_construction.IsOpen)
                {
                    return "panel still open: " + _construction.Status;
                }
                if (_construction.LastBuilt == null || _world.Golems.Count != 1)
                {
                    return $"{_world.Golems.Count} golems in the world";
                }
                _log.Add($"station: panel listed {_world.Definitions.Chassis.Count} chassis, row click built {_construction.LastBuilt.GolemId}");
                return null;
            });
            Do("the Workbench opened on the new golem", () =>
            {
                // Unity's order: a golem arrives bare, so the Workbench opens on it next.
                var workbench = _world.WorkbenchScreen;
                if (workbench == null || !workbench.IsOpen)
                {
                    return "the Workbench did not open after the build";
                }
                return workbench.TargetGolem == _construction.LastBuilt ? null : "the Workbench targets a different golem";
            });
            Do("Escape closes the Workbench", () => { Tap(Key.Escape); return null; });
            Do("the Workbench is closed", () =>
                _world.WorkbenchScreen.IsOpen ? "Escape left the Workbench open" : null);
            Do("stand by the golem and press G", () =>
            {
                Compat.Vector2Int cell = _construction.LastBuilt.Cell;
                Stand(cell.x, cell.y - 1f);
                return null;
            });
            Do("press G", () => { Tap(Key.G); return null; });
            Do("the golem is carried", () =>
                _world.Interactor.CarriedGolem == _construction.LastBuilt ? null : "G did not pick it up: " + _world.Interactor.LastStatusMessage);
            Do("press G again", () => { Tap(Key.G); return null; });
            Do("the golem is set down", () =>
            {
                if (_world.Interactor.CarriedGolem != null)
                {
                    return "still carried: " + _world.Interactor.LastStatusMessage;
                }
                _log.Add("G carried the golem and set it down");
                return null;
            });
        }

        private void Do(string name, Func<string> step) => _steps.Enqueue((name, step));

        private void Stand(float x, float y) => _player.TeleportTo(new CoreVector3(x, y, 0f));

        // A press and its release parsed in the same frame never reads as "just pressed", so a
        // tap holds the key down until the next step (FramesBetweenSteps frames later).
        private Key? _heldTap;

        private void Tap(Key key)
        {
            Press(key, true);
            _heldTap = key;
        }

        private static void Press(Key key, bool down)
        {
            // Not flushed: parsed during this node's _Process, a flushed event would stamp "just
            // pressed" on a frame the player node has already run. Buffered, it is delivered at
            // the start of the next frame, before anyone's _Process -- as a real key press is.
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = down });
        }

        private void Click(Vector2 at)
        {
            _player.GetViewport().WarpMouse(at);
            foreach (bool down in new[] { true, false })
            {
                Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, Position = at, GlobalPosition = at });
                Input.FlushBufferedEvents();
            }
        }
    }
}
