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

        public const string MenuToken = "[[menu]]";
        public const string CancelToken = "[[cancel]]";
        public const string GuideToken = "[[guide]]";

        // The pad's buttons by the Steam Deck's own labels (docs/steam-deck-design.md D7): the face
        // buttons A B X Y, the bumpers L1 R1, the triggers L2 R2, and Menu and View in the middle.
        public const string PadA = "A";
        public const string PadB = "B";
        public const string PadX = "X";
        public const string PadY = "Y";
        public const string PadL1 = "L1";
        public const string PadR1 = "R1";
        public const string PadL2 = "L2";
        public const string PadR2 = "R2";
        public const string PadMenu = "Menu";
        public const string PadView = "View";
        public const string PadLeftStick = "the left stick";
        /// <summary>The left stick pressed in: the guide's, as F1 is the keyboard's.</summary>
        public const string PadL3 = "L3";

        private static bool _pad;

        /// <summary>
        /// Whether the names are the pad's: the Godot layer sets it whenever the last device used
        /// changes, so every prompt, hint and guide step names the buttons in the player's hands.
        /// Setting a new value bumps <see cref="Version"/>.
        /// </summary>
        public static bool Pad
        {
            get => _pad;
            set
            {
                if (_pad != value)
                {
                    _pad = value;
                    Version++;
                }
            }
        }

        public static string Interact => Pad ? PadA : First(InteractAction, "E");
        public static string Carry => Pad ? PadX : First(CarryAction, "G");
        public static string Inventory => Pad ? PadY : First(InventoryAction, "I");
        public static string Rotate => Pad ? PadR1 : First(RotateAction, "R");

        /// <summary>The menu's key: Tab, which is reserved, or the pad's Menu button.</summary>
        public static string Menu => Pad ? PadMenu : "Tab";

        /// <summary>What puts a held building down and closes a screen: Escape, or the pad's B.</summary>
        public static string Cancel => Pad ? PadB : "Escape";

        /// <summary>What puts the guide away and brings it back: F1, or the pad's L3.</summary>
        public static string Guide => Pad ? PadL3 : "F1";

        /// <summary>The right stick pressed in: the playtest kit's skip, as F9 is the keyboard's.</summary>
        public const string PadR3 = "R3";

        /// <summary>What runs the playtest kit through a chapter (playtest mode only): F9, or the pad's R3.</summary>
        public static string Kit => Pad ? PadR3 : "F9";

        /// <summary>
        /// The four movement keys as one word when each is a single letter ("WASD", "IJKL"),
        /// else joined ("Up/Left/Down/Right"); with the pad, the left stick.
        /// </summary>
        public static string Move
        {
            get
            {
                if (Pad)
                {
                    return PadLeftStick;
                }
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

        /// <summary>
        /// Replaces the key tokens in <paramref name="text"/> with the current key names, and with
        /// the pad in use rewords what the mouse does (<see cref="PadPhrasing"/>): "click", "drag".
        /// </summary>
        public static string Fill(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }
            if (text.IndexOf("[[", System.StringComparison.Ordinal) >= 0)
            {
                text = text
                    .Replace(InteractToken, Interact)
                    .Replace(CarryToken, Carry)
                    .Replace(InventoryToken, Inventory)
                    .Replace(MoveToken, Move)
                    .Replace(RotateToken, Rotate)
                    .Replace(MenuToken, Menu)
                    .Replace(CancelToken, Cancel)
                    .Replace(GuideToken, Guide);
            }
            return Pad ? PadPhrasing.Apply(text) : text;
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
