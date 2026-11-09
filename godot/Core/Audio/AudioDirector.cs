using System;
using System.Collections.Generic;
using GolemFactory.Compat;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.Simulation;

namespace GolemFactory.Audio
{
    /// <summary>One sound to start: a slot, and where it stands if it is positional.</summary>
    public readonly struct AudioRequest
    {
        public readonly string Slot;
        public readonly bool HasPosition;

        /// <summary>Where it plays, in cells (Core's frame); meaningless unless <see cref="HasPosition"/>.</summary>
        public readonly Vector2 Position;

        public AudioRequest(string slot, bool hasPosition, Vector2 position)
        {
            Slot = slot;
            HasPosition = hasPosition;
            Position = position;
        }
    }

    /// <summary>
    /// What the game sounds like, decided (docs/audio-design.md §4, invariant 2). Owned by
    /// <see cref="World.SandboxWorld"/>, so a test composes it. It hears the world's moments --
    /// a stroke landing, a golem turning red, a card snapping in, a tower stage -- applies each
    /// slot's copy cap and the off-screen cut, and queues what to start. The audio node drains
    /// the queue each frame and plays it; a test or scenario reads the same queue and counts.
    /// The loops' levels (the steam hiss, the music layers) are read off it, not queued.
    /// </summary>
    public sealed class AudioDirector
    {
        /// <summary>A sound's length when the player hasn't said, for the copy cap.</summary>
        public const float DefaultLengthSeconds = 0.5f;

        private readonly IReadOnlyDictionary<string, AudioSlotDefinition> _slots;
        private readonly Dictionary<string, float> _lengths = new Dictionary<string, float>();
        private readonly Dictionary<string, List<float>> _endsBySlot = new Dictionary<string, List<float>>();
        private readonly List<AudioRequest> _pending = new List<AudioRequest>();
        private readonly Dictionary<string, int> _requested = new Dictionary<string, int>();
        private readonly Dictionary<GolemEntity, (int Step, int Beats)> _beats = new Dictionary<GolemEntity, (int, int)>();
        private readonly HashSet<GolemEntity> _chimed = new HashSet<GolemEntity>();
        private float _now;
        private long _lastClockSecond = -1;
        private bool _hasView;
        private Vector2 _viewCentre;
        private Vector2 _viewHalfSize;

        public AudioDirector(IReadOnlyDictionary<string, AudioSlotDefinition> slots)
        {
            _slots = slots ?? new Dictionary<string, AudioSlotDefinition>();
        }

        /// <summary>Every slot, from <c>data/audio.json</c>.</summary>
        public IReadOnlyDictionary<string, AudioSlotDefinition> Slots => _slots;

        /// <summary>
        /// While true, nothing is queued: a load rebuilding the factory isn't the player placing
        /// fifty buildings.
        /// </summary>
        public bool Suppressed { get; set; }

        /// <summary>The boilers' hiss, 0 to 1 (<see cref="AudioRules.SteamHissVolume"/>).</summary>
        public float SteamHiss { get; private set; }

        /// <summary>How many music layers play (<see cref="AudioRules.MusicLayers"/>).</summary>
        public int MusicLayers { get; private set; } = 1;

        /// <summary>The sounds to start, oldest first, until <see cref="Drain"/>.</summary>
        public IReadOnlyList<AudioRequest> Pending => _pending;

        /// <summary>How many times <paramref name="slot"/> was queued since the start: what scenarios count.</summary>
        public int Requested(string slot) => _requested.TryGetValue(slot, out int n) ? n : 0;

        /// <summary>The pending requests, handed over and cleared.</summary>
        public List<AudioRequest> Drain()
        {
            var drained = new List<AudioRequest>(_pending);
            _pending.Clear();
            return drained;
        }

        /// <summary>A slot's length in seconds, from its file: how long one copy counts toward the cap.</summary>
        public void SetLength(string slot, float seconds) => _lengths[slot] = Mathf.Max(0.01f, seconds);

        /// <summary>
        /// The screen, in cells, for the off-screen cut (§0 invariant 5). Until it is set, as in a
        /// test, every positional sound is in view.
        /// </summary>
        public void SetView(Vector2 centre, Vector2 halfSize)
        {
            _hasView = true;
            _viewCentre = centre;
            _viewHalfSize = halfSize;
        }

        /// <summary>
        /// Queues <paramref name="slot"/>, at <paramref name="position"/> if it is positional.
        /// Dropped, returning false, for an unknown slot, while suppressed, off screen, or with
        /// the slot's <see cref="AudioSlotDefinition.maxCopies"/> already playing (§5).
        /// </summary>
        public bool Play(string slot, Vector2? position = null)
        {
            if (Suppressed || slot == null || !_slots.TryGetValue(slot, out AudioSlotDefinition def) || def.loop)
            {
                return false;
            }
            bool placed = def.positional && position.HasValue;
            if (placed && _hasView && !AudioRules.Audible(position.Value, _viewCentre, _viewHalfSize))
            {
                return false;
            }

            if (!_endsBySlot.TryGetValue(slot, out List<float> ends))
            {
                ends = new List<float>();
                _endsBySlot[slot] = ends;
            }
            ends.RemoveAll(end => end <= _now);
            if (ends.Count >= Mathf.Max(1, def.maxCopies))
            {
                return false;
            }
            ends.Add(_now + (_lengths.TryGetValue(slot, out float length) ? length : DefaultLengthSeconds));

            _pending.Add(new AudioRequest(slot, placed, placed ? position.Value : default));
            _requested[slot] = Requested(slot) + 1;
            return true;
        }

        /// <summary>
        /// Once a frame, after the clock: the clock's tick, the golems' work sounds and red
        /// chimes, and the loops' levels. <paramref name="seconds"/> is real time;
        /// <paramref name="workingGolems"/> is what steam powered this tick.
        /// </summary>
        private long _updates;

        // Stalled golems whose stall has been classified since they last ran: a new stall is
        // classified at once, so its chime is never late; after that, on the golem's turn.
        private readonly HashSet<GolemEntity> _classified = new HashSet<GolemEntity>();

        public void Update(float seconds, SimulationClock clock, IReadOnlyList<GolemEntity> golems, int workingGolems)
        {
            _updates++;
            _now += seconds;
            bool running = clock != null && clock.State == ClockState.Running;
            float fraction = running ? clock.TickFraction : 0f;

            // The ticking clock: once a game second while it runs; silent while paused (§2).
            if (clock != null)
            {
                long second = (long)Math.Floor(clock.CurrentTick / Math.Max(1f, clock.TicksPerSecond));
                if (running && _lastClockSecond >= 0 && second != _lastClockSecond)
                {
                    Play(AudioRules.ClockTick);
                }
                _lastClockSecond = second;
            }

            int count = 0;
            if (golems != null)
            {
                for (int i = 0; i < golems.Count; i++)
                {
                    GolemEntity golem = golems[i];
                    if (golem == null || golem.IsRemoved)
                    {
                        continue;
                    }
                    count++;
                    HearWork(golem, fraction);
                    HearMood(golem, i);
                }
                if (_beats.Count > count * 2 + 8)
                {
                    Prune(golems);
                }
            }

            SteamHiss = AudioRules.SteamHissVolume(workingGolems);
            MusicLayers = AudioRules.MusicLayers(count, MusicLayers);
        }

        /// <summary>A golem's work sound, once per stroke (<see cref="GolemWorkMotion.SoundBeats"/>).</summary>
        private void HearWork(GolemEntity golem, float fraction)
        {
            int beats = GolemWorkMotion.SoundBeats(golem, fraction, out GolemWorkMotion.Motion motion);
            int step = golem.Program?.CurrentStepIndex ?? -1;
            bool seen = _beats.TryGetValue(golem, out (int Step, int Beats) last);
            _beats[golem] = (step, beats);
            if (!seen || beats <= 0)
            {
                return;
            }
            // Up within a step; or a new step, or the same step begun again (a one-step
            // program's next cycle), that has already landed a stroke.
            if (last.Step != step || beats != last.Beats)
            {
                Play(WorkSlot(motion), new Vector2(golem.Cell.x, golem.Cell.y));
            }
        }

        /// <summary>
        /// The stall chime (§0 invariant 4): once when a golem turns red, never for grey waiting
        /// or blue no-steam, and not again until it has run again, so a retried stall, or one
        /// flickering between red and grey, chimes once. A chime the copy cap drops isn't
        /// retried: that golem's stall is heard as the one already ringing.
        /// </summary>
        private void HearMood(GolemEntity golem, int slot)
        {
            // Only a stalled program can be red, so the mood is classified only for those.
            GolemState state = golem.Program?.State ?? GolemState.Idle;
            if (state == GolemState.Running)
            {
                _chimed.Remove(golem);
                _classified.Remove(golem);
                return;
            }
            if (state != GolemState.Stalled || _chimed.Contains(golem))
            {
                return;
            }
            // Classified on the golem's turn, as the views do (GolemMoodRules.ReclassifyEveryFrames):
            // a chime a few frames late is the same chime.
            if (!GolemMoodRules.ShouldReclassify(_updates, slot, _classified.Add(golem)))
            {
                return;
            }
            if (golem.Mood == GolemMood.Stalled && _chimed.Add(golem))
            {
                Play(AudioRules.StallChime, new Vector2(golem.Cell.x, golem.Cell.y));
            }
        }

        private void Prune(IReadOnlyList<GolemEntity> golems)
        {
            var live = new HashSet<GolemEntity>(golems);
            foreach (GolemEntity golem in new List<GolemEntity>(_beats.Keys))
            {
                if (!live.Contains(golem) || golem.IsRemoved)
                {
                    _beats.Remove(golem);
                    _chimed.Remove(golem);
                    _classified.Remove(golem);
                }
            }
        }

        /// <summary>The slot a work motion sounds: <c>work.stamp</c>, <c>work.fast-stamp</c>, ...</summary>
        public static string WorkSlot(GolemWorkMotion.Motion motion)
        {
            switch (motion)
            {
                case GolemWorkMotion.Motion.Stamp: return "work.stamp";
                case GolemWorkMotion.Motion.FastStamp: return "work.fast-stamp";
                case GolemWorkMotion.Motion.Tug: return "work.tug";
                case GolemWorkMotion.Motion.Heave: return "work.heave";
                case GolemWorkMotion.Motion.Lunge: return "work.lunge";
                case GolemWorkMotion.Motion.Launch: return "work.launch";
                default: return null;
            }
        }

        // --- Events on the bus -------------------------------------------------------------

        private bool _attached;

        /// <summary>Starts hearing the event bus (the Clock Tower's stages). Pair with <see cref="Detach"/>.</summary>
        public void Attach()
        {
            if (_attached)
            {
                return;
            }
            _attached = true;
            EventBus.ClockTowerStageCompleted += OnStageCompleted;
            EventBus.Contract += OnContract;
            EventBus.CivicStageCompleted += OnCivicStage;
        }

        public void Detach()
        {
            if (!_attached)
            {
                return;
            }
            _attached = false;
            EventBus.ClockTowerStageCompleted -= OnStageCompleted;
            EventBus.Contract -= OnContract;
            EventBus.CivicStageCompleted -= OnCivicStage;
        }

        private void OnStageCompleted(ClockTowerStageCompletedEvent e) => Play(AudioRules.TowerFanfare);

        // A civic stage built rings the claim chime too, from the same slot: the tower's fanfare
        // stays the tower's.
        private void OnCivicStage(CivicStageCompletedEvent e) => Play(AudioRules.Claim);

        // A contract done rings the claim chime (docs/contracts-design.md §5): a reward earned,
        // from an existing slot. Posting and taking one are quiet.
        private void OnContract(ContractEvent e)
        {
            if (e.Kind == ContractEventKind.Completed || e.Kind == ContractEventKind.OrderCompleted)
            {
                Play(AudioRules.Claim);
            }
        }
    }
}
