using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.World;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// The playtest kit through the real scene (G10): F9 three times from a fresh game must carry
    /// the guide through all three chapters, on the world's own clock, leaving the layouts a
    /// player would have built -- and the report must say the kit was used.
    /// </summary>
    public sealed class PlaytestKitScenario : IScenario
    {
        private const string Wait = "\u0001wait";
        private readonly Queue<(string name, Func<string> step)> _steps = new Queue<(string, Func<string>)>();
        private readonly List<string> _log = new List<string>();
        private SandboxWorld _world;
        private TutorialPanel _panel;
        private Key? _heldTap;
        private int _wait;
        private int _waited;

        public void Begin(ScenarioRunner runner)
        {
            SceneTree tree = runner.GetTree();
            _world = runner.World.Sandbox;
            _panel = tree.Root.FindChild("Tutorial", true, false) as TutorialPanel;
            if (_world?.Tutorial?.Playtest == null || _panel == null)
            {
                Do("scene", () => "no guide in playtest mode");
                return;
            }

            // The first question waits in the guide's place; the kit does not need it answered.
            Do("skip the light question", () => { _world.Tutorial.Playtest.Skip(0f); return null; });
            foreach ((string next, string label) in new[] { ("gears", "chapter 1"), ("coking-card", "chapter 2"), ("patent", "chapter 3"), ("r4-card", "chapter 4"), ("done", "chapter 5") })
            {
                string expect = next;
                string chapter = label;
                Do($"F9 for {chapter}", () => { Tap(Key.F9); return null; });
                Do($"the kit runs {chapter}", () =>
                {
                    if (_world.Tutorial.KitRunning)
                    {
                        return Wait;
                    }
                    if (_world.Tutorial.Current?.Id != expect)
                    {
                        return $"stopped on '{_world.Tutorial.Current?.Title}', not '{expect}'";
                    }
                    _log.Add($"{chapter} fast-forwarded");
                    return null;
                });
            }
            // Stand in the street, so a --write-movie run of this scenario frames the whole factory.
            Do("walk out to the street", () =>
            {
                (tree.Root.FindChild("Player", true, false) as PlayerNode)?.TeleportTo(new Compat.Vector3(-5f, -13f, 0f));
                return null;
            });
            Do("the factory stands and the report says so", () =>
            {
                int golems = _world.Golems.Count(g => !g.IsRemoved);
                if (golems != 6)
                {
                    return $"{golems} golems";
                }
                string report = _world.Tutorial.Playtest.Compose("", _world.Tutorial.Now);
                if (!report.Contains("fast-forwarded chapter 5"))
                {
                    return "the report does not record the kit";
                }
                _log.Add($"6 golems working, {_world.Build.Buildings.Count(b => !b.IsRemoved)} buildings, report records {_world.Tutorial.Playtest.KitUses.Count} kit uses");
                return null;
            });
        }

        public ScenarioResult? Step(double delta)
        {
            if (_heldTap is Key held)
            {
                Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = held, Keycode = held, Pressed = false });
                _heldTap = null;
            }
            if (_wait-- > 0)
            {
                return null;
            }
            if (_steps.Count == 0)
            {
                return new ScenarioResult(true, string.Join("; ", _log));
            }
            (string name, Func<string> step) = _steps.Peek();
            string failure;
            try
            {
                failure = step();
            }
            catch (Exception e)
            {
                failure = e.Message;
            }
            if (failure == Wait)
            {
                return ++_waited > 7200 ? new ScenarioResult(false, $"{name}: timed out") : null;
            }
            _waited = 0;
            _steps.Dequeue();
            if (failure != null)
            {
                return new ScenarioResult(false, $"{name}: {failure}");
            }
            _wait = 4;
            return null;
        }

        private void Do(string name, Func<string> step) => _steps.Enqueue((name, step));

        private void Tap(Key key)
        {
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true });
            _heldTap = key;
        }
    }
}
