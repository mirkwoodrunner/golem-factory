using System.Collections.Generic;
using Godot;
using Stop = GolemFactory.Nodes.PadFocus.Stop;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The City Office's notice by pad (docs/steam-deck-design.md D6): the ring
    /// (<see cref="PadFocus"/>) walks the rows to press (take the contract, collect the shelf, a
    /// rung on a cart), scrolling them; A presses the row, as a click does, greyed ones saying why.
    /// B or Y closes; the keyboard's E still closes too, but a pad's A presses the row.
    /// </summary>
    public partial class ContractScreen
    {
        public const string StopRow = "Row";

        private PadFocus _pad;
        private ulong _padOpenedFrame;

        // --- For scenarios ----------------------------------------------------------------------
        public bool PadRingVisible => _pad != null && _pad.RingVisible;
        public Rect2 PadRingRect => _pad.RingRect;
        public int PadFocusIndex => _pad.Index;

        private void PadOpened()
        {
            _pad ??= new PadFocus(_root);
            _pad.Reset();
            _padOpenedFrame = Engine.GetProcessFrames();
        }

        /// <summary>While the notice is open every pad event is its own, read by polling in <see cref="PadProcess"/>.</summary>
        public override void _Input(InputEvent e)
        {
            if (IsOpen && PadFocus.IsPadEvent(e))
            {
                GetViewport().SetInputAsHandled();
            }
        }

        private void PadProcess(double delta)
        {
            if (_pad == null)
            {
                return;
            }
            if (!InputDevices.PadInUse)
            {
                _pad.Hide();
                return;
            }
            // The A that opened the notice at the office is still "just pressed" in the frame it opens.
            bool opening = Engine.GetProcessFrames() <= _padOpenedFrame + 1;
            if (!opening && (Input.IsActionJustPressed(BuildCursorNode.CancelAction) || Input.IsActionJustPressed(PlayerNode.Inventory)))
            {
                Close();
                return;
            }
            if (!opening && Input.IsActionJustPressed(PlayerNode.Interact) && _pad.Current(PadStops()) is Stop at)
            {
                PadFocus.Press(at.Control);
                if (!IsOpen)
                {
                    return;
                }
            }
            _pad.Update(PadStops(), delta);
        }

        private List<Stop> PadStops()
        {
            var stops = new List<Stop>();
            var scroll = _content.GetParent()?.GetParent() as ScrollContainer;
            for (int i = 0; i < _rows.Count; i++)
            {
                Button button = _rows[i].button;
                if (IsInstanceValid(button) && !button.IsQueuedForDeletion())
                {
                    stops.Add(new Stop(StopRow, i, button, scroll));
                }
            }
            return stops;
        }
    }
}
