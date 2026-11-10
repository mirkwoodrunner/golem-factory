using System;
using System.Collections.Generic;
using Godot;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The gamepad's bindings (docs/steam-deck-design.md D4): a pad event added to each action
    /// beside its keys, through the same InputMap, so a press on either device is the same
    /// action. Nothing here changes a rule (the doc's invariant 1); every verb the pad has, the
    /// keyboard and mouse have too.
    ///
    /// <para>
    /// The actions belong to several nodes (the player's, the build cursor's, the camera's, the
    /// menu's), and each registers its keys in its own <c>_Ready</c>. So the pad events are added by
    /// <see cref="AddTo"/> from each of those registrations, and again by <see cref="RegisterAll"/>
    /// once the player is ready: adding is idempotent, and an action no node has yet is left for
    /// its owner (registering it here would leave the owner's keys out, since owners skip an action
    /// that already exists). <see cref="GameSettingsStore.ApplyKeys"/> rewrites only key events, so
    /// a rebind keeps the pad's.
    /// </para>
    /// </summary>
    public static class PadBindings
    {
        // Actions only the pad needs a name for: their keyboard and mouse halves already exist as
        // the build menu's keys, a click on the alert strip and the HUD's pause button.
        public const string BuildNext = "build_next";
        public const string BuildDemolish = "build_demolish";
        public const string BuildPlace = "build_place";
        public const string CursorLeft = "cursor_left";
        public const string CursorRight = "cursor_right";
        public const string CursorUp = "cursor_up";
        public const string CursorDown = "cursor_down";
        public const string AlertNext = "alert_next";
        /// <summary>The d-pad's left takes the pad into the playtest card (a click answers it with the mouse).</summary>
        public const string CardAnswer = "card_answer";
        /// <summary>Pause and play the clock: Space on the keyboard (reserved), Back on a pad.</summary>
        public const string Pause = "game_pause";

        /// <summary>Stick and trigger dead zone (the doc's D4.4: 0.2 to 0.3).</summary>
        public const float StickDeadzone = 0.25f;

        /// <summary>The right stick and the triggers need a firmer push: they step a cursor or click.</summary>
        public const float FirmDeadzone = 0.5f;

        private static readonly Dictionary<string, InputEvent[]> Table = new Dictionary<string, InputEvent[]>
        {
            [PlayerNode.MoveLeft] = new InputEvent[] { Axis(JoyAxis.LeftX, -1f) },
            [PlayerNode.MoveRight] = new InputEvent[] { Axis(JoyAxis.LeftX, 1f) },
            [PlayerNode.MoveUp] = new InputEvent[] { Axis(JoyAxis.LeftY, -1f) }, // a stick's up is -y
            [PlayerNode.MoveDown] = new InputEvent[] { Axis(JoyAxis.LeftY, 1f) },
            [PlayerNode.Interact] = new InputEvent[] { Button(JoyButton.A) },
            [PlayerNode.Carry] = new InputEvent[] { Button(JoyButton.X) },
            [PlayerNode.Inventory] = new InputEvent[] { Button(JoyButton.Y) },
            [BuildCursorNode.CancelAction] = new InputEvent[] { Button(JoyButton.B) },
            [BuildCursorNode.RotateAction] = new InputEvent[] { Button(JoyButton.RightShoulder) },
            [CameraRigNode.ZoomIn] = new InputEvent[] { Button(JoyButton.DpadUp) },
            [CameraRigNode.ZoomOut] = new InputEvent[] { Button(JoyButton.DpadDown) },
            [ManagementScreen.ToggleAction] = new InputEvent[] { Button(JoyButton.Start) },
            [BuildNext] = new InputEvent[] { Button(JoyButton.LeftShoulder) },
            [BuildDemolish] = new InputEvent[] { Axis(JoyAxis.TriggerLeft, 1f) },
            [BuildPlace] = new InputEvent[] { Axis(JoyAxis.TriggerRight, 1f) },
            [CursorLeft] = new InputEvent[] { Axis(JoyAxis.RightX, -1f) },
            [CursorRight] = new InputEvent[] { Axis(JoyAxis.RightX, 1f) },
            [CursorUp] = new InputEvent[] { Axis(JoyAxis.RightY, -1f) },
            [CursorDown] = new InputEvent[] { Axis(JoyAxis.RightY, 1f) },
            [AlertNext] = new InputEvent[] { Button(JoyButton.DpadRight) },
            [CardAnswer] = new InputEvent[] { Button(JoyButton.DpadLeft) },
            [Pause] = new InputEvent[] { Button(JoyButton.Back), Key(Godot.Key.Space) },
        };

        /// <summary>The actions that have a pad binding, for a scenario to check none is missing.</summary>
        public static IEnumerable<string> Actions => Table.Keys;

        /// <summary>
        /// Registers the pad's own actions (those no other node owns) and adds the pad events to
        /// every action that exists. Called by the player once it is ready.
        /// </summary>
        public static void RegisterAll()
        {
            foreach (string own in new[] { BuildNext, BuildDemolish, BuildPlace, CursorLeft, CursorRight, CursorUp, CursorDown, AlertNext, CardAnswer, Pause })
            {
                if (!InputMap.HasAction(own))
                {
                    InputMap.AddAction(own);
                }
            }
            foreach (string action in Table.Keys)
            {
                AddTo(action);
            }
        }

        /// <summary>Adds <paramref name="action"/>'s pad events if it exists and lacks them. Safe to call again.</summary>
        public static void AddTo(string action)
        {
            if (!InputMap.HasAction(action) || !Table.TryGetValue(action, out InputEvent[] events))
            {
                return;
            }
            foreach (InputEvent e in events)
            {
                if (!InputMap.ActionHasEvent(action, e))
                {
                    InputMap.ActionAddEvent(action, e);
                }
            }
            bool firm = action == BuildPlace || action == BuildDemolish || action.StartsWith("cursor_", StringComparison.Ordinal);
            InputMap.ActionSetDeadzone(action, firm ? FirmDeadzone : StickDeadzone);
        }

        private static InputEventJoypadButton Button(JoyButton button) => new InputEventJoypadButton { ButtonIndex = button, Device = -1 };

        private static InputEventJoypadMotion Axis(JoyAxis axis, float direction) =>
            new InputEventJoypadMotion { Axis = axis, AxisValue = direction, Device = -1 };

        private static InputEventKey Key(Key key) => new InputEventKey { PhysicalKeycode = key };
    }

    /// <summary>Which kind of device the player used last.</summary>
    public enum InputDeviceKind
    {
        KeyboardMouse,
        Pad,
    }

    /// <summary>
    /// The last device used (the doc's D4.5). Node-layer only: Core never sees it. It decides
    /// whether the build cursor follows the mouse or the right stick, and whether the pointer is
    /// shown; D7's prompts will read it too.
    /// </summary>
    public static class InputDevices
    {
        /// <summary>A stick resting a little off centre is not the player picking the pad up.</summary>
        public const float StickNoise = 0.3f;

        /// <summary>A pointer nudged by a hand on the desk is not the player picking the mouse up, in pixels.</summary>
        public const float MouseNoise = 4f;

        public static InputDeviceKind Last { get; private set; } = InputDeviceKind.KeyboardMouse;

        public static bool PadInUse => Last == InputDeviceKind.Pad;

        /// <summary>Raised when <see cref="Last"/> changes.</summary>
        public static event Action<InputDeviceKind> Changed;

        /// <summary>Notes the device <paramref name="e"/> came from, if it says anything.</summary>
        public static void Observe(InputEvent e)
        {
            InputDeviceKind? kind = e switch
            {
                InputEventJoypadButton => InputDeviceKind.Pad,
                InputEventJoypadMotion m when Mathf.Abs(m.AxisValue) >= StickNoise => InputDeviceKind.Pad,
                InputEventKey => InputDeviceKind.KeyboardMouse,
                InputEventMouseButton => InputDeviceKind.KeyboardMouse,
                InputEventMouseMotion m when m.Relative.Length() >= MouseNoise => InputDeviceKind.KeyboardMouse,
                _ => null,
            };
            if (kind is InputDeviceKind k && k != Last)
            {
                Last = k;
                Changed?.Invoke(k);
            }
        }

        /// <summary>Back to the keyboard and mouse, as a scenario starts.</summary>
        public static void Reset()
        {
            Last = InputDeviceKind.KeyboardMouse;
            OverlayHoldsPad = false;
        }

        /// <summary>
        /// An overlay that isn't a full screen (the playtest card) has taken the pad: the world's
        /// polling (walking, the right stick's cursor) ignores it until the overlay lets go.
        /// </summary>
        public static bool OverlayHoldsPad { get; set; }
    }

    /// <summary>
    /// Watches every event for <see cref="InputDevices"/>, before the GUI or any node handles it.
    /// The player adds one. Hides the pointer while the pad is in use, so a mouse left resting over
    /// the world doesn't show a cursor the pad isn't moving.
    /// </summary>
    public partial class InputDeviceNode : Node
    {
        public override void _Ready()
        {
            InputDevices.Changed += OnChanged;
        }

        public override void _ExitTree()
        {
            InputDevices.Changed -= OnChanged;
        }

        public override void _Input(InputEvent e) => InputDevices.Observe(e);

        /// <summary>The pointer mode last asked for: headless runs ignore <c>Input.MouseMode</c>, so a scenario reads this.</summary>
        public static Input.MouseModeEnum AppliedMode { get; private set; } = Input.MouseModeEnum.Visible;

        private static void OnChanged(InputDeviceKind kind)
        {
            AppliedMode = kind == InputDeviceKind.Pad ? Input.MouseModeEnum.Hidden : Input.MouseModeEnum.Visible;
            Input.MouseMode = AppliedMode;
        }
    }
}
