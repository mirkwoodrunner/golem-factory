using System;
using System.Collections.Generic;
using Godot;
using GolemFactory.Settings;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// Loads, saves and applies <see cref="GameSettings"/> in <c>user://settings.cfg</c>, section
    /// "game" (the audio section is AudioNode's). The Core object holds the rules; this maps it
    /// onto ConfigFile, the window, the content scale and the InputMap. A scenario run passes
    /// <c>persist: false</c> so it never touches the player's file.
    /// </summary>
    public static class GameSettingsStore
    {
        private const string Section = "game";

        public static GameSettings Current { get; private set; } = new GameSettings();
        private static bool _loaded;

        /// <summary>False under --scenario / --demo: nothing is read from or written to the player's file.</summary>
        public static bool Persist { get; } =
            Array.IndexOf(OS.GetCmdlineUserArgs(), "--scenario") < 0 && Array.IndexOf(OS.GetCmdlineUserArgs(), "--demo") < 0;

        public static GameSettings Load()
        {
            if (_loaded)
            {
                return Current;
            }
            _loaded = true;
            if (!Persist)
            {
                return Current;
            }
            var config = new ConfigFile();
            if (config.Load(AudioNode.SettingsPath) == Error.Ok && config.HasSection(Section))
            {
                var pairs = new Dictionary<string, string>();
                foreach (string key in config.GetSectionKeys(Section))
                {
                    pairs[key] = config.GetValue(Section, key).AsString();
                }
                Current = GameSettings.FromPairs(pairs);
            }
            return Current;
        }

        public static void Save()
        {
            if (!Persist)
            {
                return;
            }
            var config = new ConfigFile();
            config.Load(AudioNode.SettingsPath); // keep the audio section
            foreach (KeyValuePair<string, string> pair in Current.ToPairs())
            {
                config.SetValue(Section, pair.Key, pair.Value);
            }
            config.Save(AudioNode.SettingsPath);
        }

        /// <summary>Applies display mode, UI scale and key bindings. Safe to call again.</summary>
        public static void Apply(Window window)
        {
            Load();
            if (window != null && Persist)
            {
                window.Mode = Current.Display == DisplayMode.Fullscreen
                    ? Window.ModeEnum.ExclusiveFullscreen
                    : Window.ModeEnum.Windowed;
                window.ContentScaleFactor = Current.UiScale;
            }
            ApplyKeys();
        }

        /// <summary>Rewrites each registered action's key events from the bindings.</summary>
        public static void ApplyKeys()
        {
            foreach (string action in KeyBindings.Actions())
            {
                if (!InputMap.HasAction(action))
                {
                    continue;
                }
                InputMap.ActionEraseEvents(action);
                foreach (string name in Current.Keys.Get(action))
                {
                    if (Enum.TryParse(name, out Key key))
                    {
                        InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
                    }
                }
            }
        }
    }
}
