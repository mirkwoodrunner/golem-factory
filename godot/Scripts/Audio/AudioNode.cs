using System;
using System.Collections.Generic;
using Godot;
using GolemFactory.Audio;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// Plays what Core's <see cref="AudioDirector"/> decided (docs/audio-design.md §0 invariant 2):
    /// it makes the buses, loads every slot in <c>data/audio.json</c>, keeps the loops running at
    /// the levels the director reads, and each frame starts the sounds it queued. It decides
    /// nothing itself. <see cref="WorldNode"/> adds it, so every scene with a world has one.
    ///
    /// <para>
    /// Under <c>--scenario</c> (and <c>--demo</c>) the Master bus is muted and the player's
    /// settings file is neither read nor written (invariant 6): a scenario checks what was asked
    /// to play, through the director, never what was heard.
    /// </para>
    /// </summary>
    public partial class AudioNode : Node
    {
        public const string GroupName = "golem_audio";

        /// <summary>The player's volume settings: <c>user://settings.cfg</c>, kept apart from the save.</summary>
        public const string SettingsPath = "user://settings.cfg";

        private const string Section = "audio";

        /// <summary>How fast the hiss follows the boilers' load, in full swings per second.</summary>
        private const float HissFollowRate = 0.8f;

        private readonly Dictionary<string, AudioStream> _streams = new Dictionary<string, AudioStream>();
        private readonly Dictionary<string, AudioStreamPlayer> _flat = new Dictionary<string, AudioStreamPlayer>();
        private readonly Dictionary<string, AudioStreamPlayer> _loops = new Dictionary<string, AudioStreamPlayer>();
        private readonly List<string> _missing = new List<string>();
        private readonly float[] _layerLevels = new float[AudioRules.MusicLayerCount];
        private AudioDirector _director;
        private float _hiss;

        /// <summary>Master, Music and Effects, 0 to 1 (§5, decision 4). Effects covers ambience and UI too.</summary>
        public float MasterVolume { get; private set; } = 1f;
        public float MusicVolume { get; private set; } = 0.7f;
        public float EffectsVolume { get; private set; } = 1f;
        public bool Muted { get; private set; }

        /// <summary>True under a scenario or demo: silent, and the settings file untouched.</summary>
        public bool Silent { get; private set; }

        /// <summary>Slots whose file is missing; empty when every slot loaded.</summary>
        public IReadOnlyList<string> MissingSlots => _missing;

        /// <summary>How many slots loaded a file.</summary>
        public int LoadedSlots => _streams.Count;

        /// <summary>The bus names, Master's children first: Music, Ambience, Effects, UI.</summary>
        public static readonly string[] Buses = { "Music", "Effects", "Ambience", "UI" };

        public static AudioNode Find(Node from) => from.GetTree().GetFirstNodeInGroup(GroupName) as AudioNode;

        public override void _EnterTree() => AddToGroup(GroupName);

        public override void _Ready()
        {
            string[] args = OS.GetCmdlineUserArgs();
            Silent = Array.IndexOf(args, "--scenario") >= 0 || Array.IndexOf(args, "--demo") >= 0
                || Array.IndexOf(args, "--spike-demo") >= 0 || Array.IndexOf(args, "--spike-check") >= 0;

            _director = WorldNode.Find(this)?.Sandbox?.Audio;
            EnsureBuses();
            if (!Silent)
            {
                LoadSettings();
            }
            ApplyVolumes();
            if (_director == null)
            {
                return;
            }

            foreach (AudioSlotDefinition slot in _director.Slots.Values)
            {
                Load(slot);
            }
        }

        /// <summary>
        /// The buses, made in code like the input actions (§5): Music and Effects under Master,
        /// Ambience and UI under Effects, so the Effects slider covers every sound but the music.
        /// </summary>
        public static void EnsureBuses()
        {
            foreach (string name in Buses)
            {
                if (AudioServer.GetBusIndex(name) < 0)
                {
                    AudioServer.AddBus();
                    AudioServer.SetBusName(AudioServer.BusCount - 1, name);
                }
            }
            AudioServer.SetBusSend(AudioServer.GetBusIndex("Music"), "Master");
            AudioServer.SetBusSend(AudioServer.GetBusIndex("Effects"), "Master");
            AudioServer.SetBusSend(AudioServer.GetBusIndex("Ambience"), "Effects");
            AudioServer.SetBusSend(AudioServer.GetBusIndex("UI"), "Effects");
        }

        private void Load(AudioSlotDefinition slot)
        {
            if (string.IsNullOrEmpty(slot.path) || !ResourceLoader.Exists(slot.path))
            {
                _missing.Add(slot.name);
                GD.PushError($"audio.json: {slot.name} has no file at {slot.path}");
                return;
            }
            var stream = GD.Load<AudioStream>(slot.path);
            if (slot.loop && stream is AudioStreamWav wav)
            {
                wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
                wav.LoopBegin = 0;
                wav.LoopEnd = (int)Math.Round(wav.GetLength() * wav.MixRate);
            }
            _streams[slot.name] = stream;
            _director.SetLength(slot.name, (float)stream.GetLength());

            if (slot.loop)
            {
                // A loop plays from the start and never stops; its level is all that changes.
                var player = new AudioStreamPlayer { Name = "Loop" + _loops.Count, Stream = stream, Bus = slot.bus.ToString() };
                AddChild(player);
                player.VolumeDb = -80f;
                player.Play();
                _loops[slot.name] = player;
            }
            else if (!slot.positional)
            {
                var player = new AudioStreamPlayer
                {
                    Name = "Flat" + _flat.Count,
                    Stream = stream,
                    Bus = slot.bus.ToString(),
                    VolumeDb = slot.volumeDb,
                    MaxPolyphony = Math.Max(1, slot.maxCopies),
                };
                AddChild(player);
                _flat[slot.name] = player;
            }
        }

        public override void _Process(double delta)
        {
            if (_director == null)
            {
                return;
            }
            float seconds = (float)delta;
            UpdateView();
            UpdateLoops(seconds);
            foreach (AudioRequest request in _director.Drain())
            {
                Start(request);
            }
        }

        /// <summary>Tells the director what the camera sees, in cells, for the off-screen cut.</summary>
        private void UpdateView()
        {
            Camera2D camera = GetViewport()?.GetCamera2D();
            if (camera == null)
            {
                return;
            }
            Vector2 half = GetViewport().GetVisibleRect().Size / camera.Zoom / 2f;
            _director.SetView(
                GridConversions.WorldToPoint(camera.GetScreenCenterPosition()),
                new Compat.Vector2(half.X / GridConversions.CellPixels, half.Y / GridConversions.CellPixels));
        }

        private void UpdateLoops(float seconds)
        {
            _hiss = Mathf.MoveToward(_hiss, _director.SteamHiss, HissFollowRate * seconds);
            float fade = seconds / AudioRules.LayerFadeSeconds;
            for (int i = 0; i < _layerLevels.Length; i++)
            {
                _layerLevels[i] = Mathf.MoveToward(_layerLevels[i], i < _director.MusicLayers ? 1f : 0f, fade);
            }

            foreach (KeyValuePair<string, AudioStreamPlayer> entry in _loops)
            {
                AudioSlotDefinition slot = _director.Slots[entry.Key];
                float level = 1f;
                if (entry.Key == AudioRules.Steam)
                {
                    level = _hiss;
                }
                else
                {
                    for (int i = 0; i < _layerLevels.Length; i++)
                    {
                        if (entry.Key == AudioRules.MusicLayer(i + 1))
                        {
                            level = _layerLevels[i];
                        }
                    }
                }
                entry.Value.VolumeDb = slot.volumeDb + AudioRules.SliderToDb(level);
            }
        }

        private void Start(AudioRequest request)
        {
            if (!_streams.TryGetValue(request.Slot, out AudioStream stream))
            {
                return;
            }
            if (!request.HasPosition)
            {
                if (_flat.TryGetValue(request.Slot, out AudioStreamPlayer flat))
                {
                    flat.Play();
                }
                return;
            }

            // A positional sound stands where its golem does; the camera is the listener, so
            // Godot pans and quiets it toward the screen's edge.
            AudioSlotDefinition slot = _director.Slots[request.Slot];
            Vector2 view = GetViewport()?.GetVisibleRect().Size ?? new Vector2(1280f, 720f);
            float zoom = GetViewport()?.GetCamera2D()?.Zoom.X ?? 1f;
            var player = new AudioStreamPlayer2D
            {
                Stream = stream,
                Bus = slot.bus.ToString(),
                VolumeDb = slot.volumeDb,
                MaxDistance = view.X / Mathf.Max(0.01f, zoom) * 0.9f,
                Attenuation = 1.5f,
                // This node isn't a Node2D, so a child's position is its world position.
                Position = GridConversions.PointToWorld(request.Position),
            };
            AddChild(player);
            player.Finished += player.QueueFree;
            player.Play();
        }

        // --- Volume controls (decision 4) --------------------------------------------------

        public void SetVolumes(float master, float music, float effects, bool muted)
        {
            MasterVolume = Mathf.Clamp(master, 0f, 1f);
            MusicVolume = Mathf.Clamp(music, 0f, 1f);
            EffectsVolume = Mathf.Clamp(effects, 0f, 1f);
            Muted = muted;
            ApplyVolumes();
            if (!Silent)
            {
                SaveSettings();
            }
        }

        private void ApplyVolumes()
        {
            int master = AudioServer.GetBusIndex("Master");
            AudioServer.SetBusVolumeDb(master, AudioRules.SliderToDb(MasterVolume));
            AudioServer.SetBusMute(master, Muted || Silent);
            AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex("Music"), AudioRules.SliderToDb(MusicVolume));
            AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex("Effects"), AudioRules.SliderToDb(EffectsVolume));
        }

        private void LoadSettings()
        {
            var config = new ConfigFile();
            if (config.Load(SettingsPath) != Error.Ok)
            {
                return;
            }
            MasterVolume = Mathf.Clamp((float)config.GetValue(Section, "master", MasterVolume), 0f, 1f);
            MusicVolume = Mathf.Clamp((float)config.GetValue(Section, "music", MusicVolume), 0f, 1f);
            EffectsVolume = Mathf.Clamp((float)config.GetValue(Section, "effects", EffectsVolume), 0f, 1f);
            Muted = (bool)config.GetValue(Section, "muted", Muted);
        }

        private void SaveSettings()
        {
            var config = new ConfigFile();
            config.Load(SettingsPath); // keep any other section
            config.SetValue(Section, "master", MasterVolume);
            config.SetValue(Section, "music", MusicVolume);
            config.SetValue(Section, "effects", EffectsVolume);
            config.SetValue(Section, "muted", Muted);
            config.Save(SettingsPath);
        }
    }
}
