using System.Collections.Generic;
using System.Linq;
using Godot;
using Stop = GolemFactory.Nodes.PadFocus.Stop;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The station's panel by pad (docs/steam-deck-design.md D6): the ring (<see cref="PadFocus"/>)
    /// over the chassis rows, the patent rows and Close. A builds the row it rests on, as a click
    /// does; on a greyed row it says what's short instead. B closes. The keyboard's E still closes
    /// too; a pad's A doesn't, since it presses the row.
    /// </summary>
    public partial class ConstructionPanelNode
    {
        public const string StopChassis = "Chassis";
        public const string StopPatent = "Patent";
        public const string StopClose = "Close";

        private PadFocus _pad;
        private ulong _padOpenedFrame;

        // --- For scenarios ----------------------------------------------------------------------
        public bool PadRingVisible => _pad.RingVisible;
        public Rect2 PadRingRect => _pad.RingRect;
        public string PadFocusKind => _pad.Kind;
        public int PadFocusIndex => _pad.Index;

        private void PadOpened()
        {
            _pad.Reset();
            _padOpenedFrame = Engine.GetProcessFrames();
        }

        /// <summary>While the panel is open every pad event is its own, read by polling in <see cref="PadProcess"/>.</summary>
        public override void _Input(InputEvent e)
        {
            if (IsOpen && (e is InputEventJoypadButton || e is InputEventJoypadMotion))
            {
                GetViewport().SetInputAsHandled();
            }
        }

        private void PadProcess(double delta)
        {
            if (!InputDevices.PadInUse)
            {
                _pad.Hide();
                return;
            }
            List<Stop> stops = PadStops();
            // The A that opened the panel at the station is still "just pressed" in the frame it opens.
            bool opening = Engine.GetProcessFrames() <= _padOpenedFrame + 1;
            if (!opening && Input.IsActionJustPressed(BuildCursorNode.CancelAction))
            {
                Close();
                return;
            }
            if (!opening && Input.IsActionJustPressed(PlayerNode.Interact) && _pad.Current(stops) is Stop at)
            {
                Press(at);
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
            for (int i = 0; i < _rows.Count; i++)
            {
                if (GodotObject.IsInstanceValid(_rows[i].row) && !_rows[i].row.IsQueuedForDeletion())
                {
                    stops.Add(new Stop(StopChassis, i, _rows[i].row));
                }
            }
            for (int i = 0; i < _patentRows.Count; i++)
            {
                if (GodotObject.IsInstanceValid(_patentRows[i].row) && !_patentRows[i].row.IsQueuedForDeletion())
                {
                    stops.Add(new Stop(StopPatent, i, _patentRows[i].row, _patentScroll));
                }
            }
            stops.Add(new Stop(StopClose, 0, _close));
            return stops;
        }

        /// <summary>The first row the stock pays for, else the first row.</summary>
        private static Stop? DefaultStop(List<Stop> stops) =>
            stops.Where(s => s.Kind == StopChassis && s.Control is Button { Disabled: false }).Cast<Stop?>().FirstOrDefault()
            ?? stops.Where(s => s.Kind == StopChassis).Cast<Stop?>().FirstOrDefault()
            ?? (stops.Count > 0 ? stops[0] : null);

        private void Press(Stop at)
        {
            if (at.Control is not Button button)
            {
                return;
            }
            if (!button.Disabled)
            {
                button.EmitSignal(BaseButton.SignalName.Pressed);
                return;
            }
            // A greyed row says what it lacks where the player looks, as the build would refuse.
            if (at.Kind == StopChassis)
            {
                TryConstruct(_rows[at.Index].chassis);
            }
            else if (at.Kind == StopPatent)
            {
                TryConstructFromPatent(_patentRows[at.Index].blueprint);
            }
        }
    }
}
