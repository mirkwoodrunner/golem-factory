using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>A scenario's verdict: whether it passed, and one line saying why.</summary>
    public readonly struct ScenarioResult
    {
        public readonly bool Passed;
        public readonly string Report;

        public ScenarioResult(bool passed, string report)
        {
            Passed = passed;
            Report = report;
        }
    }

    /// <summary>
    /// One end-to-end check, driven against the real scene. <see cref="Step"/> runs every frame
    /// until it returns a result.
    /// </summary>
    public interface IScenario
    {
        void Begin(ScenarioRunner runner);

        /// <summary>Null to keep running; a result to finish.</summary>
        ScenarioResult? Step(double delta);
    }

    /// <summary>
    /// The headless end-to-end checks (docs/godot-conversion-plan.md, "Verification"):
    ///
    /// <code>
    /// Godot_console.exe --headless --path godot --fixed-fps 60 -- --scenario &lt;name&gt;
    /// </code>
    ///
    /// Inert unless a scenario is named after the <c>--</c>. It runs the scenario to a verdict,
    /// prints <c>[scenario &lt;name&gt;] PASS|FAIL ...</c>, and quits with exit code 0 or 1 --
    /// which is what lets a milestone prove itself without anyone watching. <c>--fixed-fps 60</c>
    /// makes every frame 1/60 s of game time, so runs are deterministic and as fast as the
    /// machine allows.
    ///
    /// <para>
    /// <c>--demo</c> (or the spike's <c>--spike-demo</c>) only performs the scripted setup -- the
    /// station builds its golem -- without checking or quitting, for rendering frames with
    /// <c>--write-movie</c>. The spike's <c>--spike-check</c> is an alias for
    /// <c>--scenario loop</c>.
    /// </para>
    /// </summary>
    public partial class ScenarioRunner : Node
    {
        private static readonly Dictionary<string, Func<IScenario>> Scenarios =
            new Dictionary<string, Func<IScenario>>(StringComparer.Ordinal)
            {
                ["loop"] = () => new LoopScenario(),
                ["font-glyphs"] = () => new FontGlyphsScenario(),
                ["world"] = () => new WorldScenario(),
                ["build"] = () => new BuildScenario(),
                ["interact"] = () => new InteractScenario(),
                ["golems"] = () => new GolemsScenario(),
                ["workbench"] = () => new WorkbenchScenario(),
            };

        [Export] public NodePath StationPath { get; set; }
        [Export] public NodePath DepotPath { get; set; }

        private string _name;
        private IScenario _scenario;

        public ConstructionStationNode Station => GetNode<ConstructionStationNode>(StationPath);
        public DepotNode Depot => GetNode<DepotNode>(DepotPath);
        public WorldNode World => WorldNode.Find(this);

        /// <summary>The names <c>--scenario</c> accepts.</summary>
        public static IEnumerable<string> Names => Scenarios.Keys;

        public override void _Ready()
        {
            string[] args = OS.GetCmdlineUserArgs();
            _name = ScenarioName(args);

            // Headless runs get a 64x64 window, which puts the bottom-left build menu above the
            // top of the screen and makes every screen-space check meaningless. Give them the
            // window a player has.
            if (DisplayServer.GetName() == "headless")
            {
                GetTree().Root.Size = new Vector2I(
                    (int)ProjectSettings.GetSetting("display/window/size/viewport_width", 1280),
                    (int)ProjectSettings.GetSetting("display/window/size/viewport_height", 720));
            }

            if (_name == null)
            {
                if (args.Contains("--demo") || args.Contains("--spike-demo"))
                {
                    // Deferred so every sibling has finished _Ready before a golem joins the tree.
                    Callable.From(Station.Interact).CallDeferred();
                }
                SetProcess(false);
                return;
            }

            if (!Scenarios.TryGetValue(_name, out Func<IScenario> make))
            {
                Finish(new ScenarioResult(false,
                    $"no scenario named '{_name}' (known: {string.Join(", ", Names)})"));
                return;
            }

            _scenario = make();
            // Deferred for the same reason: scenarios drive nodes that must already be ready.
            Callable.From(() => _scenario.Begin(this)).CallDeferred();
        }

        public override void _Process(double delta)
        {
            if (_scenario == null)
            {
                return;
            }

            ScenarioResult? result = _scenario.Step(delta);
            if (result.HasValue)
            {
                Finish(result.Value);
            }
        }

        private static string ScenarioName(string[] args)
        {
            int index = Array.IndexOf(args, "--scenario");
            if (index >= 0)
            {
                return index + 1 < args.Length ? args[index + 1] : "";
            }
            return args.Contains("--spike-check") ? "loop" : null;
        }

        private void Finish(ScenarioResult result)
        {
            GD.Print($"[scenario {_name}] {(result.Passed ? "PASS" : "FAIL")} {result.Report}");
            _scenario = null;
            SetProcess(false);
            GetTree().Quit(result.Passed ? 0 : 1);
        }
    }
}
