using System.Collections.Generic;
using System.Linq;
using Godot;
using Stop = GolemFactory.Nodes.PadFocus.Stop;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The playtest card by pad (docs/steam-deck-design.md D6). The card isn't a full screen: it
    /// sits over the world while the player plays, so the pad isn't the card's until the player
    /// asks. The d-pad's left (<see cref="PadBindings.CardAnswer"/>, free in the world) takes the
    /// pad into the card, and the counter says so. Then the ring walks the answers, the note and
    /// Skip; A picks the one it rests on; A on the note opens it for typing (the Deck's keyboard
    /// is Steam's); B steps back out of the note, else out of the card. While the card holds the
    /// pad, the world ignores it (<see cref="InputDevices.OverlayHoldsPad"/>).
    /// </summary>
    public partial class PlaytestNode
    {
        public const string CounterText = "PLAYTEST  ·  your call  ·  goes in your report";
        public const string PadCounterText = "PLAYTEST  ·  your call  ·  d-pad left to answer";

        public const string StopOption = "Option";
        public const string StopNote = "Note";
        public const string StopSkip = "Skip";

        private PadFocus _pad;
        private Label _counter;
        private bool _padHeld;
        private ulong _padTakenFrame;

        // --- For scenarios ----------------------------------------------------------------------
        /// <summary>Whether the card holds the pad.</summary>
        public bool PadHeld => _padHeld;
        public bool PadRingVisible => _pad.RingVisible;
        public Rect2 PadRingRect => _pad.RingRect;
        public string PadFocusKind => _pad.Kind;
        public int PadFocusIndex => _pad.Index;
        public string CounterLine => _counter?.Text ?? "";

        public override void _Input(InputEvent e)
        {
            if (_session == null || !(e is InputEventJoypadButton || e is InputEventJoypadMotion))
            {
                return;
            }
            if (_padHeld)
            {
                GetViewport().SetInputAsHandled(); // the card's, read by polling in PadProcess
            }
            else if (_card.Visible && e.IsActionPressed(PadBindings.CardAnswer))
            {
                Take();
                GetViewport().SetInputAsHandled();
            }
        }

        private void Take()
        {
            _padHeld = true;
            InputDevices.OverlayHoldsPad = true;
            _pad.Reset();
            _padTakenFrame = Engine.GetProcessFrames();
        }

        private void LetGo()
        {
            if (_note.HasFocus())
            {
                _note.ReleaseFocus();
            }
            _padHeld = false;
            InputDevices.OverlayHoldsPad = false;
            _pad.Hide();
        }

        private void PadProcess(double delta, bool shown)
        {
            bool pad = InputDevices.PadInUse;
            string counter = pad && !_padHeld ? PadCounterText : CounterText;
            if (_counter.Text != counter)
            {
                _counter.Text = counter;
            }
            if (!_padHeld)
            {
                return;
            }
            if (!shown || !pad)
            {
                LetGo(); // answered, hidden under a screen, or the mouse took over
                return;
            }
            // The d-pad's left that took the pad is still "just pressed" in this frame.
            if (Engine.GetProcessFrames() <= _padTakenFrame + 1)
            {
                _pad.Update(PadStops(), delta, move: false);
                return;
            }
            if (_note.HasFocus())
            {
                // Typing: only B, back to the ring.
                if (Input.IsActionJustPressed(BuildCursorNode.CancelAction))
                {
                    _note.ReleaseFocus();
                    DisplayServer.VirtualKeyboardHide();
                }
                _pad.Update(PadStops(), delta, move: false);
                return;
            }
            if (Input.IsActionJustPressed(BuildCursorNode.CancelAction))
            {
                LetGo();
                return;
            }
            List<Stop> stops = PadStops();
            if (Input.IsActionJustPressed(PlayerNode.Interact) && _pad.Current(stops) is Stop at)
            {
                if (at.Kind == StopNote)
                {
                    _note.GrabFocus();
                    DisplayServer.VirtualKeyboardShow(_note.Text);
                }
                else if (at.Control is BaseButton button)
                {
                    button.EmitSignal(BaseButton.SignalName.Pressed); // answers: the card moves on or hides
                    LetGo();
                    return;
                }
            }
            _pad.Update(PadStops(), delta);
        }

        private List<Stop> PadStops()
        {
            var stops = new List<Stop>();
            int i = 0;
            foreach (Button option in _options.GetChildren().OfType<Button>())
            {
                if (!option.IsQueuedForDeletion())
                {
                    stops.Add(new Stop(StopOption, i++, option));
                }
            }
            stops.Add(new Stop(StopNote, 0, _note));
            stops.Add(new Stop(StopSkip, 0, _skip));
            return stops;
        }
    }
}
