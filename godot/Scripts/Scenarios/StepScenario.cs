using System;
using System.Collections.Generic;
using Godot;
using GolemFactory.Menus;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// A scenario as a queue of named steps that may cross scene reloads (<see cref="IReloadingScenario"/>):
    /// each step returns null or why it failed; an <see cref="Until"/> step retries until its time
    /// runs out. Real input only: keys, the pointer and clicks go through <see cref="Input"/>.
    /// The title and slots scenarios are built on it.
    /// </summary>
    public abstract class StepScenario : IReloadingScenario
    {
        private const int FramesBetweenSteps = 3;

        private readonly Queue<(string name, Func<string> step, double timeout)> _steps =
            new Queue<(string, Func<string>, double)>();
        private readonly List<string> _log = new List<string>();
        private int _wait;
        private double _stepElapsed;
        private Key? _heldTap;
        private int _reloads;

        protected ScenarioRunner Runner { get; private set; }
        protected SceneTree Tree => Runner.GetTree();

        public void Begin(ScenarioRunner runner)
        {
            Runner = runner;
            ParkPointer();
            Plan();
        }

        /// <summary>Queues every step, in order.</summary>
        protected abstract void Plan();

        /// <summary>Called once the scenario ends, passed or failed.</summary>
        protected virtual void Cleanup()
        {
        }

        public void Resumed(ScenarioRunner runner)
        {
            Runner = runner;
            _reloads++;
            ParkPointer();
        }

        /// <summary>
        /// The pointer over the world, clear of the menus: windowed, the real mouse would
        /// otherwise light whichever entry it happens to be over.
        /// </summary>
        protected void ParkPointer()
        {
            Vector2 window = new Vector2(1180f, 640f) * Tree.Root.ContentScaleFactor;
            Tree.Root.WarpMouse(window);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = window, GlobalPosition = window });
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
                Cleanup();
                return new ScenarioResult(true, string.Join("; ", _log));
            }
            (string name, Func<string> step, double timeout) = _steps.Peek();
            string failure;
            try
            {
                failure = step();
            }
            catch (Exception e)
            {
                failure = e.GetType().Name + ": " + e.Message;
                timeout = 0;
            }
            if (failure != null && _stepElapsed < timeout)
            {
                _stepElapsed += delta;
                return null;
            }
            _steps.Dequeue();
            _stepElapsed = 0;
            if (failure != null)
            {
                Cleanup();
                return new ScenarioResult(false, $"{name}: {failure}");
            }
            _wait = FramesBetweenSteps;
            return null;
        }

        protected void Do(string name, Func<string> step) => _steps.Enqueue((name, step, 0));

        protected void Until(string name, double seconds, Func<string> check) => _steps.Enqueue((name, check, seconds));

        protected void Log(string line) => _log.Add(line);

        /// <summary>Asks for <paramref name="launch"/> and reloads the scene, as the title does, then waits for the new scene.</summary>
        protected void Reload(string name, LaunchKind launch) =>
            Reloading(name, 1, () =>
            {
                GameLaunch.Request(launch);
                Tree.ReloadCurrentScene();
            });

        /// <summary>
        /// Runs <paramref name="cause"/> (a click or key that reloads the scene), then waits for
        /// <paramref name="reloads"/> new scenes to hand the scenario back. Counted from before
        /// the cause: a reload can land between two steps.
        /// </summary>
        protected void Reloading(string name, int reloads, Action cause)
        {
            int expected = -1;
            Do(name, () =>
            {
                expected = _reloads + reloads;
                cause();
                return null;
            });
            Until(name + ": the scene reloads", 8, () => _reloads >= expected ? null : $"{expected - _reloads} reload(s) still to come");
        }

        protected void Wait(double seconds)
        {
            double until = -1;
            Do("wait " + seconds + " s", () => { until = seconds; return null; });
            Until("waited " + seconds + " s", seconds + 1, () => (until -= 1.0 / 60.0) <= 0 ? null : "waiting");
        }

        // --- Input ------------------------------------------------------------------------------

        /// <summary>A key down now and up next frame.</summary>
        protected void Tap(Key key)
        {
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = true });
            _heldTap = key;
        }

        protected static void Press(Key key, bool down) =>
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = down });

        protected void PointAt(Control control)
        {
            Vector2 window = control.GetGlobalRect().GetCenter() * Tree.Root.ContentScaleFactor;
            Tree.Root.WarpMouse(window);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = window, GlobalPosition = window });
        }

        protected void Click(Control control)
        {
            Vector2 window = control.GetGlobalRect().GetCenter() * Tree.Root.ContentScaleFactor;
            Tree.Root.WarpMouse(window);
            Input.ParseInputEvent(new InputEventMouseMotion { Position = window, GlobalPosition = window });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = window, GlobalPosition = window });
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = window, GlobalPosition = window });
        }
    }
}
