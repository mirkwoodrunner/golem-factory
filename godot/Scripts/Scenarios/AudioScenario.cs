using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.Audio;
using GolemFactory.Buildings;
using GolemFactory.Diagnostics;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.UI;
using GolemFactory.World;
using CoreVector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// The sound, end to end (docs/audio-design.md §7), on the dummy audio driver and with the
    /// Master bus muted: it checks what was asked to play, never what was heard. Every slot in
    /// <c>audio.json</c> loads and the buses exist; the <c>golem-motion</c> row, run as its
    /// fixture, asks for each chassis's work sound, and nothing once the clock is paused; and
    /// the Workbench's card snap, Engage and Patent, a Claim, placing, demolishing and
    /// collecting each ask for theirs.
    /// </summary>
    public sealed class AudioScenario : IScenario
    {
        private const int PausedFrames = 90;

        private readonly GolemMotionScenario _row = new GolemMotionScenario();
        private readonly List<string> _problems = new List<string>();
        private SandboxWorld _world;
        private AudioNode _audio;
        private int _phase;
        private int _pausedFrames;
        private int _workBeforePause;
        private string _rowVerdict;

        private static readonly string[] WorkSlots =
            { "work.stamp", "work.fast-stamp", "work.tug", "work.heave", "work.lunge", "work.launch" };

        public void Begin(ScenarioRunner runner)
        {
            _world = runner.World.Sandbox;
            _audio = AudioNode.Find(runner);
            CheckLoaded();
            _row.Begin(runner);
        }

        public ScenarioResult? Step(double delta)
        {
            switch (_phase)
            {
                case 0:
                {
                    // The golem-motion row, as the fixture: every chassis at its signature verb.
                    ScenarioResult? row = _row.Step(delta);
                    if (row == null)
                    {
                        return null;
                    }
                    if (!row.Value.Passed)
                    {
                        return new ScenarioResult(false, "the golem-motion fixture failed: " + row.Value.Report);
                    }
                    _rowVerdict = string.Join(", ", WorkSlots.Select(s => $"{s} x{_world.Audio.Requested(s)}"));
                    foreach (string slot in WorkSlots.Where(s => _world.Audio.Requested(s) == 0))
                    {
                        _problems.Add($"no {slot} while its golem worked");
                    }
                    _workBeforePause = WorkSlots.Sum(_world.Audio.Requested);
                    _world.Clock.Pause();
                    _phase = 1;
                    return null;
                }
                case 1:
                    // Paused: the golems hold still, and so does their sound (invariant 3).
                    if (++_pausedFrames < PausedFrames)
                    {
                        return null;
                    }
                    int paused = WorkSlots.Sum(_world.Audio.Requested) - _workBeforePause;
                    if (paused != 0)
                    {
                        _problems.Add($"{paused} work sounds while paused");
                    }
                    _world.Clock.Play();
                    CheckPlayerSounds();
                    _phase = 2;
                    return null;
                default:
                    return _problems.Count > 0
                        ? new ScenarioResult(false, string.Join("; ", _problems))
                        : new ScenarioResult(true, $"{_audio.LoadedSlots} slots loaded; {_rowVerdict}; " +
                            "none while paused; snap, engage, patent, claim, place, demolish, collect each asked for");
            }
        }

        private void CheckLoaded()
        {
            if (_audio == null)
            {
                _problems.Add("no AudioNode in the scene");
                return;
            }
            if (!_audio.Silent)
            {
                _problems.Add("a scenario run isn't silent");
            }
            if (_audio.MissingSlots.Count > 0)
            {
                _problems.Add("no file for " + string.Join(", ", _audio.MissingSlots));
            }
            if (_audio.LoadedSlots != _world.Audio.Slots.Count)
            {
                _problems.Add($"{_audio.LoadedSlots} of {_world.Audio.Slots.Count} slots loaded");
            }
            foreach (string bus in AudioNode.Buses)
            {
                if (AudioServer.GetBusIndex(bus) < 0)
                {
                    _problems.Add($"no {bus} bus");
                }
            }
            if (AudioServer.GetBusSend(AudioServer.GetBusIndex("UI")) != "Effects")
            {
                _problems.Add("the UI bus doesn't go through Effects");
            }
        }

        /// <summary>Each of the player's moments asks for its sound, once.</summary>
        private void CheckPlayerSounds()
        {
            AudioDirector audio = _world.Audio;
            void Expect(string slot, System.Action act, string what)
            {
                // A long enough wait that the copy cap has freed the slot.
                audio.Update(10f, null, null, 0);
                int before = audio.Requested(slot);
                act();
                if (audio.Requested(slot) != before + 1)
                {
                    _problems.Add($"{what} asked for {audio.Requested(slot) - before} {slot}");
                }
            }

            // Engage first, on a golem's own program, so the draft is one the lever takes.
            WorkbenchSession bench = _world.Workbench;
            bench.RetargetGolem(_world.Golems.First());
            Expect(AudioRules.Engage, () =>
            {
                if (!bench.Engage())
                {
                    _problems.Add("Engage refused: " + bench.StatusText);
                }
            }, "Engage");
            Expect(AudioRules.Patent, () => bench.Patent(), "Patent");
            AppendageActionDefinition card = _world.Definitions.Appendages["PushOutput"];
            Expect(AudioRules.CardSnap, () => bench.HandleDrop(WorkbenchCardRef.FromVault(card), WorkbenchZone.Socket(1)), "a card dropped in a socket");

            if (_world.AssemblyLine != null && _world.AssemblyLine.GetCard(0) != null)
            {
                EndgameLoad.Grant(_world, _world.AssemblyLine.GetCurrentCostBundle(0));
                Expect(AudioRules.Claim, () =>
                {
                    if (!_world.AssemblyLineBoard.Claim(0))
                    {
                        _problems.Add("Claim refused: " + _world.AssemblyLineBoard.Status);
                    }
                }, "Claim");
            }
            else
            {
                _problems.Add("no card on the Assembly Line to claim");
            }

            var cell = new CoreVector2Int(-7, -12);
            PlaceableBuilding pipe = _world.Placeables.Single(p => p.Key == "SteamPipePrefab").Prefab;
            EndgameLoad.Grant(_world, pipe.Cost);
            _world.Build.SetActivePrefab(pipe);
            Expect(AudioRules.Place, () => _world.Build.PlaceOrRemove(cell), "placing a pipe");
            Expect(AudioRules.Demolish, () => _world.Build.PlaceOrRemove(cell), "demolishing it");
            _world.Build.CancelPlacement();

            ICrate crate = _world.Build.Buildings.Select(b => b.GetPart<ICrate>()).First(c => c != null);
            crate.Buffer.Deposit(ItemType.Coke, 2);
            Expect(AudioRules.Collect, () => _world.Interactor.TryCollect(crate), "collecting from a crate");
        }
    }
}
