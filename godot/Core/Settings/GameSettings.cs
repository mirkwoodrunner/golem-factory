using System;
using System.Collections.Generic;
using System.Globalization;

namespace GolemFactory.Settings
{
    public enum DisplayMode { Windowed, Fullscreen }

    public enum RebindResult { Bound, Swapped, Unchanged, Reserved, Unknown, Cancelled }

    /// <summary>
    /// The Settings tab's "press a key" state: <see cref="Begin"/> on a click, then the next key
    /// press goes to <see cref="Press"/>, never to the game. Escape cancels. Kept in Core so the
    /// rules are tested; the node only routes the key here while <see cref="Listening"/> is set.
    /// </summary>
    public sealed class KeyCapture
    {
        private readonly KeyBindings _bindings;

        public KeyCapture(KeyBindings bindings) => _bindings = bindings;

        /// <summary>The action waiting for a key, or null.</summary>
        public string Listening { get; private set; }

        public void Begin(string action) => Listening = action;

        public void Cancel() => Listening = null;

        /// <summary>
        /// The key pressed while listening. A reserved key keeps listening, so the player can try
        /// another; anything else ends it.
        /// </summary>
        public RebindResult Press(string keyName)
        {
            if (Listening == null)
            {
                return RebindResult.Unknown;
            }
            if (keyName == "Escape")
            {
                Listening = null;
                return RebindResult.Cancelled;
            }
            RebindResult result = _bindings.Rebind(Listening, keyName);
            if (result != RebindResult.Reserved)
            {
                Listening = null;
            }
            return result;
        }
    }

    /// <summary>
    /// The player's non-audio settings (docs/menus-design.md step 1). Engine-free: the Godot layer
    /// maps <see cref="ToPairs"/> / <see cref="FromPairs"/> onto <c>user://settings.cfg</c> and
    /// applies the values. Never part of a save. A bad or missing value falls back to its default.
    /// </summary>
    public sealed class GameSettings
    {
        /// <summary>
        /// 1: below it the screens' 12-14 px text falls under what the font draws legibly (seen in
        /// a 0.75 frame). A window smaller than the layout is still fitted down (<see cref="FitScale"/>).
        /// </summary>
        public const float MinUiScale = 1f;
        public const float MaxUiScale = 2f;

        public DisplayMode Display = DisplayMode.Windowed;
        public float UiScale = 1f;
        public bool ReducedMotion;
        public bool ColorBlindMarks;
        public KeyBindings Keys = KeyBindings.Defaults();

        /// <summary>The canvas every screen is laid out for (the Unity prefabs' reference, Ugui).</summary>
        public const int LayoutWidth = 1280;
        public const int LayoutHeight = 720;

        /// <summary>The smallest scale a tiny window is fitted down to.</summary>
        public const float MinFitScale = 0.5f;

        public void SetUiScale(float value) =>
            UiScale = Math.Clamp(float.IsNaN(value) ? 1f : value, MinUiScale, MaxUiScale);

        /// <summary>
        /// The largest scale at which a <paramref name="width"/> × <paramref name="height"/> window
        /// still shows the whole <see cref="LayoutWidth"/> × <see cref="LayoutHeight"/> canvas the
        /// screens are built for (fixed pixel layouts: a smaller canvas cuts them off).
        /// </summary>
        public static float FitScale(int width, int height) =>
            width <= 0 || height <= 0
                ? 1f
                : Math.Max(MinFitScale, Math.Min(width / (float)LayoutWidth, height / (float)LayoutHeight));

        /// <summary>
        /// The scale the window uses: the player's <see cref="UiScale"/>, capped by
        /// <see cref="FitScale"/>. A bigger window lets a bigger setting through; a window smaller
        /// than the layout is fitted down even at a setting of 1.
        /// </summary>
        public float EffectiveUiScale(int width, int height) => Math.Min(UiScale, FitScale(width, height));

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
            s.Keys.Sanitize(_ => true); // reserved and doubled keys; the Godot layer also checks names
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
            ("build_rotate", new[] { "R" }),
        };

        /// <summary>
        /// Keys the game uses outside these actions (Godot <c>Key</c> names): Tab opens Management,
        /// Escape cancels, F1 and F9 are the guide's, the build menu's rows are the digits, Minus,
        /// Equal and X (<c>BuildMenuNode.KeyText</c>), and the modifiers change a click (Shift-click
        /// moves all that fits). A rebind to one is refused, and a loaded file holding one is fixed.
        /// </summary>
        public static readonly IReadOnlySet<string> Reserved = new HashSet<string>
        {
            "Tab", "Escape", "F1", "F9", "Minus", "Equal", "X",
            "Key0", "Key1", "Key2", "Key3", "Key4", "Key5", "Key6", "Key7", "Key8", "Key9",
            "Shift", "Ctrl", "Alt", "Meta",
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
        /// Makes <paramref name="key"/> the first key of <paramref name="action"/>, replacing the
        /// old first key (a second key, like an arrow, stays). Whichever other action held the key
        /// takes the old first key in its place, so no action is left without one. A reserved key
        /// or an unknown action changes nothing.
        /// </summary>
        public RebindResult Rebind(string action, string key)
        {
            if (string.IsNullOrEmpty(key) || !_keys.TryGetValue(action, out List<string> mine))
            {
                return RebindResult.Unknown;
            }
            if (Reserved.Contains(key))
            {
                return RebindResult.Reserved;
            }
            if (mine[0] == key)
            {
                return RebindResult.Unchanged;
            }
            string swappedWith = null;
            string old = mine[0];
            foreach (KeyValuePair<string, List<string>> other in _keys)
            {
                int at = other.Value.IndexOf(key);
                if (other.Key != action && at >= 0)
                {
                    other.Value[at] = old;
                    swappedWith = other.Key;
                }
            }
            // The new key replaces the first one; a second key (the arrows) stays.
            mine.Remove(key);
            mine.RemoveAll(k => k == old);
            mine.Insert(0, key);
            return swappedWith == null ? RebindResult.Bound : RebindResult.Swapped;
        }

        /// <summary>
        /// Makes loaded bindings safe to use (a settings file can be hand-edited or from an older
        /// version): drops a key that <paramref name="isKey"/> doesn't know, a reserved key, and a
        /// key an earlier action (in <see cref="Actions"/> order) already has. If that leaves any
        /// action without a key, every binding goes back to the defaults. True if anything changed.
        /// </summary>
        public bool Sanitize(Func<string, bool> isKey)
        {
            bool changed = false;
            var taken = new HashSet<string>();
            foreach (string action in Actions())
            {
                List<string> keys = _keys[action];
                int before = keys.Count;
                keys.RemoveAll(k => !isKey(k) || Reserved.Contains(k) || taken.Contains(k));
                changed |= keys.Count != before;
                taken.UnionWith(keys);
            }
            foreach (List<string> keys in _keys.Values)
            {
                if (keys.Count == 0)
                {
                    KeyBindings defaults = Defaults();
                    foreach (string action in Actions())
                    {
                        _keys[action] = new List<string>(defaults._keys[action]);
                    }
                    return true;
                }
            }
            return changed;
        }

        /// <summary>The action that has <paramref name="key"/>, or null.</summary>
        public string ActionWith(string key)
        {
            foreach (KeyValuePair<string, List<string>> a in _keys)
            {
                if (a.Value.Contains(key))
                {
                    return a.Key;
                }
            }
            return null;
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
