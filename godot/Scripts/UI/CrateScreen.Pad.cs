using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.UI;
using Stop = GolemFactory.Nodes.PadFocus.Stop;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The Crate screen by pad (docs/steam-deck-design.md D6.2): the ring
    /// (<see cref="PadFocus"/>) over the crate's rows, the pack's rows, Take all and the label
    /// button. On a row, A moves one across and X moves what fits (the click and the Shift-click).
    /// A on the label button opens the picker with the ring on the crate's label; A on an option
    /// sets it. B closes the picker, else the screen; Y closes the screen, as it opened it.
    /// </summary>
    public partial class CrateScreen
    {
        public const string PadHint = "A moves one · X moves what fits";

        public const string StopCrate = "Crate";
        public const string StopPack = "Pack";
        public const string StopTakeAll = "TakeAll";
        public const string StopLabel = "Label";
        public const string StopOption = "Option";

        private const int PickerColumns = 8;

        private PadFocus _pad;
        private ulong _padOpenedFrame;
        private bool _padToPicker;
        private bool _padToLabel;

        // --- For scenarios ----------------------------------------------------------------------
        public bool PadRingVisible => _pad.RingVisible;
        public Rect2 PadRingRect => _pad.RingRect;
        public string PadFocusKind => _pad.Kind;
        public int PadFocusIndex => _pad.Index;
        public string HintText => _hint.Text;
        public IReadOnlyList<(string good, Button row)> CrateRowsInOrder => _cratePanel.OrderedRows;
        public IReadOnlyList<(string good, Button row)> PackRowsInOrder => _packPanel.OrderedRows;

        private void PadOpened()
        {
            _pad.Reset();
            _padToPicker = _padToLabel = false;
            _padOpenedFrame = Engine.GetProcessFrames();
        }

        /// <summary>The picker opened or closed: the ring follows it in, and back to the label button.</summary>
        private void PadPickerShown(bool show)
        {
            _padToPicker = show;
            _padToLabel = !show && IsOpen;
        }

        /// <summary>While the screen is open every pad event is its own, read by polling in <see cref="PadProcess"/>.</summary>
        public override void _Input(InputEvent e)
        {
            if (IsOpen && (e is InputEventJoypadButton || e is InputEventJoypadMotion))
            {
                GetViewport().SetInputAsHandled();
            }
        }

        private void PadProcess(double delta)
        {
            bool pad = InputDevices.PadInUse;
            _hint.Text = pad ? PadHint : Hint;
            if (!pad)
            {
                _pad.Hide();
                return;
            }
            // Y (or A at a crate) that opened the screen is still "just pressed" in the frame it opens.
            bool opening = Engine.GetProcessFrames() <= _padOpenedFrame + 1;
            if (!opening && Input.IsActionJustPressed(BuildCursorNode.CancelAction))
            {
                if (PickerOpen)
                {
                    ShowPicker(false);
                }
                else
                {
                    Close();
                    return;
                }
            }
            if (!opening && Input.IsActionJustPressed(PlayerNode.Inventory))
            {
                Close();
                return;
            }

            List<Stop> stops = PadStops();
            if (_padToPicker)
            {
                _padToPicker = false;
                int current = _options.FindIndex(o => GolemFactory.Economy.DepotFilterOptions.Same(o.good, Crate.FilterItemType));
                if (PadFocus.Find(stops, StopOption, current < 0 ? 0 : current) is Stop option)
                {
                    _pad.Focus(option);
                }
            }
            if (_padToLabel)
            {
                _padToLabel = false;
                if (PadFocus.Find(stops, StopLabel, 0) is Stop label)
                {
                    _pad.Focus(label);
                }
            }
            if (!opening && _pad.Current(stops) is Stop at)
            {
                PadButtons(at);
                if (!IsOpen)
                {
                    return;
                }
            }
            _pad.Update(PadStops(), delta);

            // The option under the ring is named on the status line, as the mouse's hover names it.
            string named = PickerOpen && _pad.Kind == StopOption && _pad.Index < _options.Count
                ? CrateScreenReadout.OptionName(_options[_pad.Index].good) : null;
            if (named != null && named != _padNamedOption)
            {
                _status.Text = named;
            }
            _padNamedOption = named;
        }

        private string _padNamedOption;

        private List<Stop> PadStops()
        {
            var stops = new List<Stop>();
            if (Crate.CanLabel)
            {
                stops.Add(new Stop(StopLabel, 0, _labelButton));
            }
            if (PickerOpen)
            {
                for (int i = 0; i < _options.Count; i++)
                {
                    stops.Add(new Stop(StopOption, i, _options[i].button, _picker, PickerColumns));
                }
            }
            else
            {
                AddRows(stops, StopCrate, _cratePanel);
            }
            stops.Add(new Stop(StopTakeAll, 0, TakeAllButton));
            AddRows(stops, StopPack, _packPanel);
            return stops;
        }

        private static void AddRows(List<Stop> stops, string kind, InventoryPanel panel)
        {
            for (int i = 0; i < panel.OrderedRows.Count; i++)
            {
                Button row = panel.OrderedRows[i].row;
                if (GodotObject.IsInstanceValid(row) && !row.IsQueuedForDeletion())
                {
                    stops.Add(new Stop(kind, i, row, panel.Scroll));
                }
            }
        }

        /// <summary>The crate's first row, else the pack's, else Take all.</summary>
        private static Stop? DefaultStop(List<Stop> stops) =>
            stops.Where(s => s.Kind == StopCrate).Cast<Stop?>().FirstOrDefault()
            ?? stops.Where(s => s.Kind == StopPack).Cast<Stop?>().FirstOrDefault()
            ?? PadFocus.Find(stops, StopTakeAll, 0);

        private void PadButtons(Stop at)
        {
            bool one = Input.IsActionJustPressed(PlayerNode.Interact);
            bool fits = Input.IsActionJustPressed(PlayerNode.Carry);
            if (!one && !fits)
            {
                return;
            }
            switch (at.Kind)
            {
                case StopCrate:
                    TakeClicked(_cratePanel.OrderedRows[at.Index].good, fits);
                    break;
                case StopPack:
                    PutClicked(_packPanel.OrderedRows[at.Index].good, fits);
                    break;
                case StopTakeAll:
                case StopLabel:
                case StopOption:
                    if (one && at.Control is BaseButton button && !button.Disabled)
                    {
                        button.EmitSignal(BaseButton.SignalName.Pressed);
                    }
                    break;
            }
        }
    }
}
