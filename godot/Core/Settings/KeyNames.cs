using System.Collections.Generic;

namespace GolemFactory.Settings
{
    /// <summary>
    /// The key names player text shows, read from the player's current <see cref="KeyBindings"/>
    /// (docs/menus-design.md step 1), so a rebind reaches every prompt, hint and guide step. The
    /// Godot layer points <see cref="Bindings"/> at the loaded settings; with nothing set, the
    /// defaults (E, G, I, WASD) are shown.
    ///
    /// <para>
    /// Text built on demand reads <see cref="Interact"/> and the rest directly. Text built once and
    /// kept (the guide's steps) holds the tokens (<see cref="InteractToken"/> etc.) and is passed
    /// through <see cref="Fill"/> each time it is read.
    /// </para>
    /// </summary>
    public static class KeyNames
    {
        public const string InteractAction = "interact";
        public const string CarryAction = "golem_carry";
        public const string InventoryAction = "inventory";
        public const string RotateAction = "build_rotate";

        public const string InteractToken = "[[interact]]";
        public const string CarryToken = "[[carry]]";
        public const string InventoryToken = "[[inventory]]";
        public const string MoveToken = "[[move]]";
        public const string RotateToken = "[[rotate]]";

        private static KeyBindings _bindings;

        /// <summary>The bindings shown. Setting null restores the defaults. Setting bumps <see cref="Version"/>.</summary>
        public static KeyBindings Bindings
        {
            get => _bindings ??= KeyBindings.Defaults();
            set
            {
                _bindings = value;
                Version++;
            }
        }

        /// <summary>
        /// Changes whenever the bindings are set (the Godot layer sets them after every rebind), so
        /// text drawn once and kept (the guide panel) knows to draw again.
        /// </summary>
        public static int Version { get; private set; }

        public static string Interact => First(InteractAction, "E");
        public static string Carry => First(CarryAction, "G");
        public static string Inventory => First(InventoryAction, "I");
        public static string Rotate => First(RotateAction, "R");

        /// <summary>
        /// The four movement keys as one word when each is a single letter ("WASD", "IJKL"),
        /// else joined ("Up/Left/Down/Right").
        /// </summary>
        public static string Move
        {
            get
            {
                string[] keys =
                {
                    First("move_up", "W"), First("move_left", "A"), First("move_down", "S"), First("move_right", "D"),
                };
                foreach (string k in keys)
                {
                    if (k.Length != 1)
                    {
                        return string.Join("/", keys);
                    }
                }
                return string.Concat(keys);
            }
        }

        /// <summary>Replaces the key tokens in <paramref name="text"/> with the current key names.</summary>
        public static string Fill(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf("[[", System.StringComparison.Ordinal) < 0)
            {
                return text;
            }
            return text
                .Replace(InteractToken, Interact)
                .Replace(CarryToken, Carry)
                .Replace(InventoryToken, Inventory)
                .Replace(MoveToken, Move)
                .Replace(RotateToken, Rotate);
        }

        private static string First(string action, string fallback)
        {
            IReadOnlyList<string> keys = Bindings.Get(action);
            return keys.Count > 0 ? Display(keys[0]) : fallback;
        }

        /// <summary>A Godot key name as the player reads it ("Space", "Shift", "1" for "Key1").</summary>
        public static string Display(string keyName)
        {
            if (keyName.Length == 4 && keyName.StartsWith("Key") && char.IsDigit(keyName[3]))
            {
                return keyName.Substring(3);
            }
            return keyName;
        }
    }
}
