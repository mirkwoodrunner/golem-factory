using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.UI;
using Stop = GolemFactory.Nodes.PadFocus.Stop;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The title by pad (docs/steam-deck-design.md D6). The menu and the Load view keep their own
    /// selection (Core's <see cref="Menus.TitleMenu"/> and the slot board), which the keyboard
    /// already walks; the pad walks the same selection by polling, with the ring's repeat
    /// (<see cref="PadFocus.DirectionRepeat"/>), so a held stick steps once and then repeats
    /// rather than once per motion event. Up and down choose, A picks, B goes back; on Load, left
    /// and right switch Campaign and Creative. The Settings view is Management's tab, walked by
    /// the generic ring (<see cref="PadFocus.ControlsIn"/>, key buttons left out), with Back.
    /// </summary>
    public partial class TitleScreen
    {
        public const string MainHint = "Up and Down to choose  ·  Enter to pick";
        public const string PadMainHint = "Up and Down to choose  ·  A to pick";
        public const string LoadHint = "Up and Down to choose  ·  Left and Right for Campaign or Creative  ·  Enter to load  ·  Escape to go back";
        public const string PadLoadHint = "Up and Down to choose  ·  Left and Right for Campaign or Creative  ·  A to load  ·  B to go back";
        public const string StopItem = "Item";

        private Label _mainHint;
        private Label _loadHint;
        private PadFocus _pad;
        private readonly PadFocus.DirectionRepeat _padRepeat = new PadFocus.DirectionRepeat();
        private bool _padInSettings;

        // --- For scenarios ----------------------------------------------------------------------
        public string MainHintText => _mainHint?.Text ?? "";
        public string LoadHintText => _loadHint?.Text ?? "";
        public bool PadRingVisible => _pad != null && _pad.RingVisible;
        public Control PadFocusControl => _pad?.Current(SettingsStops())?.Control;

        private void PadProcess(double delta)
        {
            if (!_shown || _leaving || _root == null)
            {
                return;
            }
            bool pad = InputDevices.PadInUse;
            _mainHint.Text = pad ? PadMainHint : MainHint;
            _loadHint.Text = pad ? PadLoadHint : LoadHint;
            _pad ??= new PadFocus(_root);
            if (!pad)
            {
                _pad.Hide();
                _padRepeat.Reset();
                return;
            }

            if (SettingsOpen)
            {
                if (!_padInSettings)
                {
                    _padInSettings = true;
                    _pad.Reset(); // the ring starts on Settings' first control
                }
                if (Input.IsActionJustPressed(BuildCursorNode.CancelAction))
                {
                    CloseSettings();
                    return;
                }
                if (_pad.Current(SettingsStops()) is Stop at)
                {
                    if (Input.IsActionJustPressed(PlayerNode.Interact))
                    {
                        PadFocus.Press(at.Control); // Back closes the view
                        if (!SettingsOpen)
                        {
                            return;
                        }
                    }
                    PadFocus.StepSlider(at.Control);
                }
                PadFocus.ScrollWithStick(_settingsView, delta);
                _pad.Update(SettingsStops(), delta);
                return;
            }
            _padInSettings = false;
            _pad.Hide();

            Vector2 step = _padRepeat.Step(delta);
            bool pick = Input.IsActionJustPressed(PlayerNode.Interact);
            if (LoadOpen)
            {
                if (Input.IsActionJustPressed(BuildCursorNode.CancelAction))
                {
                    CloseLoad();
                }
                else if (step.X != 0f)
                {
                    ShowKind(!LoadingCreative);
                }
                else if (step.Y != 0f)
                {
                    SlotBoard.Move((int)step.Y);
                    LoadSlots.Refresh();
                }
                else if (pick)
                {
                    PickSlot(SlotBoard.Selected);
                }
                return;
            }
            if (step.Y != 0f)
            {
                _menu.Move((int)step.Y);
                ShowSelection();
            }
            else if (pick)
            {
                Pick(_menu.Selected);
            }
        }

        private List<Stop> SettingsStops()
        {
            HashSet<Control> keys = Settings != null ? new HashSet<Control>(Settings.KeyButtons.Values) : new HashSet<Control>();
            return _settingsView == null ? new List<Stop>() : PadFocus.ControlsIn(_settingsView, StopItem, keys.Contains).ToList();
        }
    }
}
