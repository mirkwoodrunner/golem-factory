using System;
using System.Collections.Generic;
using Godot;
using GolemFactory.Settings;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// Loads, saves and applies <see cref="GameSettings"/> (docs/menus-design.md step 1): section
    /// "game" of <c>user://settings.cfg</c>, beside AudioNode's "audio" section. The Core object
    /// holds the rules; this maps it onto ConfigFile, the window, the content scale and the InputMap.
    ///
    /// <para>
    /// Under <c>--scenario</c> or <c>--demo</c> the player's file is never read or written: every run
    /// starts from the defaults, and a save goes to <see cref="ScenarioPath"/>, so the `settings`
    /// scenario can check a reload without touching the player's settings.
    /// </para>
    /// </summary>
    public static class GameSettingsStore
    {
        public const string Section = "game";
        public const string ScenarioPath = "user://scenario-settings.cfg";

        public static GameSettings Current { get; private set; } = new GameSettings();
        private static bool _loaded;

        /// <summary>False under --scenario / --demo (see the summary).</summary>
        public static bool Persist { get; } =
            Array.IndexOf(OS.GetCmdlineUserArgs(), "--scenario") < 0 && Array.IndexOf(OS.GetCmdlineUserArgs(), "--demo") < 0;

        /// <summary>The file saves go to.</summary>
        public static string Path => Persist ? AudioNode.SettingsPath : ScenarioPath;

        /// <summary>The settings, read from the player's file the first time (defaults under a scenario).</summary>
        public static GameSettings Load()
        {
            if (_loaded)
            {
                return Current;
            }
            _loaded = true;
            if (Persist)
            {
                Current = ReadFile() ?? new GameSettings();
            }
            return Current;
        }

        /// <summary>A key name Godot knows and can bind (not None or Unknown).</summary>
        public static bool IsKey(string name) =>
            Enum.TryParse(name, out Key key) && key != Key.None && key != Key.Unknown;

        /// <summary>A fresh read of <see cref="Path"/>'s "game" section, as a restart would see it; null with none.</summary>
        public static GameSettings ReadFile()
        {
            var config = new ConfigFile();
            if (config.Load(Path) != Error.Ok || !config.HasSection(Section))
            {
                return null;
            }
            var pairs = new Dictionary<string, string>();
            foreach (string key in config.GetSectionKeys(Section))
            {
                pairs[key] = config.GetValue(Section, key).AsString();
            }
            GameSettings read = GameSettings.FromPairs(pairs);
            read.Keys.Sanitize(IsKey); // a name this engine can't bind would leave an action dead
            return read;
        }

        public static void Save()
        {
            var config = new ConfigFile();
            config.Load(Path); // keep the audio section
            foreach (KeyValuePair<string, string> pair in Current.ToPairs())
            {
                config.SetValue(Section, pair.Key, pair.Value);
            }
            config.Save(Path);
        }

        private static Window _watched;

        /// <summary>
        /// Applies display mode, UI scale and key bindings. Safe to call again: the window's mode
        /// changes only when it is in the other category (so a maximized window stays maximized).
        /// The scale is <see cref="GameSettings.EffectiveUiScale"/> for the window's size, and is
        /// worked out again whenever the window is resized.
        /// </summary>
        public static void Apply(Window window)
        {
            Load();
            if (window != null && window != _watched)
            {
                _watched = window;
                window.SizeChanged += () => ApplyScale(window);
            }
            if (window != null)
            {
                bool full = window.Mode is Window.ModeEnum.Fullscreen or Window.ModeEnum.ExclusiveFullscreen;
                bool wantFull = Current.Display == DisplayMode.Fullscreen;
                if (full != wantFull)
                {
                    // Borderless, not exclusive: alt-tab and overlays behave, which suits a game
                    // played in long, calm sessions.
                    window.Mode = wantFull ? Window.ModeEnum.Fullscreen : Window.ModeEnum.Windowed;
                }
                ApplyScale(window);
            }
            ApplyKeys();
        }

        /// <summary>The scale this window shows now (see <see cref="GameSettings.EffectiveUiScale"/>).</summary>
        public static float EffectiveScale(Window window) =>
            window == null ? Current.UiScale : Current.EffectiveUiScale(window.Size.X, window.Size.Y);

        private static void ApplyScale(Window window)
        {
            float scale = EffectiveScale(window);
            if (!Mathf.IsEqualApprox(window.ContentScaleFactor, scale))
            {
                window.ContentScaleFactor = scale;
            }
        }

        /// <summary>
        /// Rewrites every rebindable action's KEY events from the bindings (registering one a node
        /// has not yet, whose own registration then finds it present and leaves it), and points
        /// player text's <see cref="KeyNames"/> at them. Other events on an action (a mouse button,
        /// a future gamepad's) are left alone, so input stays controller-ready (decision 10).
        /// </summary>
        public static void ApplyKeys()
        {
            KeyNames.Bindings = Current.Keys;
            foreach (string action in KeyBindings.Actions())
            {
                if (!InputMap.HasAction(action))
                {
                    InputMap.AddAction(action);
                }
                foreach (InputEvent e in InputMap.ActionGetEvents(action))
                {
                    if (e is InputEventKey)
                    {
                        InputMap.ActionEraseEvent(action, e);
                    }
                }
                foreach (string name in Current.Keys.Get(action))
                {
                    if (Enum.TryParse(name, out Key key))
                    {
                        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
                    }
                }
            }
        }

        /// <summary>The scenario's reset: defaults, applied, and its scratch file removed.</summary>
        public static void ResetForScenario(Window window)
        {
            if (Persist)
            {
                return;
            }
            Current = new GameSettings();
            _loaded = true;
            DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(ScenarioPath));
            Apply(window);
        }
    }
}
