using System;
using System.Collections.Generic;
using System.Globalization;

namespace GolemFactory.Settings
{
    public enum DisplayMode { Windowed, Fullscreen }

    /// <summary>
    /// The player's non-audio settings (docs/menus-design.md step 1). Engine-free: the Godot layer
    /// maps <see cref="ToPairs"/> / <see cref="FromPairs"/> onto <c>user://settings.cfg</c> and
    /// applies the values. Never part of a save. A bad or missing value falls back to its default.
    /// </summary>
    public sealed class GameSettings
    {
        public const float MinUiScale = 0.75f;
        public const float MaxUiScale = 2f;

        public DisplayMode Display = DisplayMode.Windowed;
        public float UiScale = 1f;
        public bool ReducedMotion;
        public bool ColorBlindMarks;
        public KeyBindings Keys = KeyBindings.Defaults();

        public void SetUiScale(float value) =>
            UiScale = Math.Clamp(float.IsNaN(value) ? 1f : value, MinUiScale, MaxUiScale);

        public Dictionary<string, string> ToPairs()
        {
            var pairs = new Dictionary<string, string>
            {
                ["display"] = Display.ToString(),
                ["ui_scale"] = UiScale.ToString("0.##", CultureInfo.InvariantCulture),
                ["reduced_motion"] = ReducedMotion ? "1" : "0",
                ["colorblind_marks"] = ColorBlindMarks ? "1" : "0",
            };
            foreach (KeyValuePair<string, string> pair in Keys.ToPairs())
            {
                pairs["key." + pair.Key] = pair.Value;
            }
            return pairs;
        }

        public static GameSettings FromPairs(IReadOnlyDictionary<string, string> pairs)
        {
            var s = new GameSettings();
            if (pairs == null)
            {
                return s;
            }
            if (pairs.TryGetValue("display", out string d) && Enum.TryParse(d, out DisplayMode mode))
            {
                s.Display = mode;
            }
            if (pairs.TryGetValue("ui_scale", out string u) &&
                float.TryParse(u, NumberStyles.Float, CultureInfo.InvariantCulture, out float scale))
            {
                s.SetUiScale(scale);
            }
            s.ReducedMotion = pairs.TryGetValue("reduced_motion", out string r) && r == "1";
            s.ColorBlindMarks = pairs.TryGetValue("colorblind_marks", out string c) && c == "1";
            foreach (KeyValuePair<string, string> pair in pairs)
            {
                if (pair.Key.StartsWith("key.", StringComparison.Ordinal))
                {
                    s.Keys.Set(pair.Key.Substring(4),
                        new List<string>(pair.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)));
                }
            }
            return s;
        }
    }

    /// <summary>
    /// Action name to key names (Godot <c>Key</c> enum names, kept as strings so Core needs no
    /// engine). Rebinding a key already used by another action swaps: that action takes the key
    /// the rebound one gave up, so no action is left without a key and no key is bound twice.
    /// </summary>
    public sealed class KeyBindings
    {
        private static readonly (string Action, string[] Keys)[] DefaultTable =
        {
            ("move_left", new[] { "A", "Left" }),
            ("move_right", new[] { "D", "Right" }),
            ("move_up", new[] { "W", "Up" }),
            ("move_down", new[] { "S", "Down" }),
            ("interact", new[] { "E" }),
            ("golem_carry", new[] { "G" }),
            ("inventory", new[] { "I" }),
        };

        private readonly Dictionary<string, List<string>> _keys = new Dictionary<string, List<string>>();

        public static KeyBindings Defaults()
        {
            var b = new KeyBindings();
            foreach ((string action, string[] keys) in DefaultTable)
            {
                b._keys[action] = new List<string>(keys);
            }
            return b;
        }

        public static IEnumerable<string> Actions()
        {
            foreach ((string action, _) in DefaultTable)
            {
                yield return action;
            }
        }

        public IReadOnlyList<string> Get(string action) =>
            _keys.TryGetValue(action, out List<string> k) ? k : Array.Empty<string>();

        /// <summary>Sets an action's keys. An unknown action or an empty list is ignored.</summary>
        public void Set(string action, List<string> keys)
        {
            if (keys.Count > 0 && _keys.ContainsKey(action))
            {
                _keys[action] = keys;
            }
        }

        /// <summary>
        /// Makes <paramref name="key"/> the first key of <paramref name="action"/>. Whichever other
        /// action held the key takes the first key the rebound action gave up.
        /// </summary>
        public void Rebind(string action, string key)
        {
            if (!_keys.TryGetValue(action, out List<string> mine))
            {
                return;
            }
            string old = mine[0];
            foreach (KeyValuePair<string, List<string>> other in _keys)
            {
                int at = other.Value.IndexOf(key);
                if (other.Key != action && at >= 0)
                {
                    other.Value[at] = old;
                }
            }
            mine.Remove(key);
            mine.RemoveAll(k => k == old);
            mine.Insert(0, key);
            if (mine.Count == 1 && old != key && !AnyHolds(old))
            {
                mine.Add(old);
            }
        }

        private bool AnyHolds(string key)
        {
            foreach (List<string> keys in _keys.Values)
            {
                if (keys.Contains(key))
                {
                    return true;
                }
            }
            return false;
        }

        public Dictionary<string, string> ToPairs()
        {
            var pairs = new Dictionary<string, string>();
            foreach (KeyValuePair<string, List<string>> a in _keys)
            {
                pairs[a.Key] = string.Join(",", a.Value);
            }
            return pairs;
        }
    }
}
