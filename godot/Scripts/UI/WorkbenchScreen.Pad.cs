using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.UI;
using Stop = GolemFactory.Nodes.PadFocus.Stop;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The Workbench by pad (docs/steam-deck-design.md D6.W, the owner's answers): a focus ring
    /// (<see cref="PadFocus"/>) moved over the screen's stops by the d-pad or the left stick, and A,
    /// X and the shoulders on what it rests on. A on a vault card sends it to the next socket that
    /// takes it (<see cref="WorkbenchSession.SendToNextSocket"/>, the mouse's own drag verbs); X on
    /// a socketed card takes it out; LB and RB change a Haul's good, LT and RT a batch size; A on a
    /// chassis, the lever, PATENT or CLOSE presses it; B closes the screen.
    ///
    /// <para>
    /// The ring follows the guide's hint: the screen opens on the glowing card, and after a send or
    /// a removal the ring moves to the next one. Not after a dial: the player is turning it, and
    /// the hint (often the lever, once the cards match) mustn't pull the ring off it mid-turn.
    /// </para>
    /// </summary>
    public partial class WorkbenchScreen
    {
        /// <summary>The kinds of stop the ring rests on.</summary>
        public enum PadStopKind
        {
            Close,
            Trigger,
            Step,
            Chassis,
            Vault,
            Lever,
            Patent,
        }

        private PadFocus _pad;
        private bool _padFollowHint;
        private bool _padGlyphs;
        private ulong _padOpenedFrame;

        // --- For scenarios ----------------------------------------------------------------------

        /// <summary>Whether the ring is drawn this frame.</summary>
        public bool PadRingVisible => _pad.RingVisible;

        /// <summary>The ring's rectangle on screen.</summary>
        public Rect2 PadRingRect => _pad.RingRect;

        /// <summary>What the ring rests on: its kind, and its index among that kind.</summary>
        public PadStopKind PadFocusKind => Enum.TryParse(_pad.Kind, out PadStopKind kind) ? kind : PadStopKind.Vault;
        public int PadFocusIndex => _pad.Index;

        /// <summary>The control the ring rests on, or null.</summary>
        public Control PadFocusControl => _pad.Current(PadStops())?.Control;

        // --- Wiring from the screen -------------------------------------------------------------

        private void BuildPadRing()
        {
            _pad = new PadFocus(_root)
            {
                // No hint to follow: the first vault card, else the trigger.
                Default = stops => stops.Where(s => s.Kind == nameof(PadStopKind.Vault)).Cast<Stop?>().FirstOrDefault()
                    ?? PadFocus.Find(stops, nameof(PadStopKind.Trigger), 0),
            };
        }

        /// <summary>A new opening: the ring goes to the hinted card (or the first vault card).</summary>
        private void PadOpened()
        {
            _pad.Reset();
            _padFollowHint = true;
            _padOpenedFrame = Engine.GetProcessFrames();
        }

        /// <summary>A pad's labels on a socketed card's picker and dial, else the mouse's arrows.</summary>
        private string Glyph(string mouse, string pad) => _padGlyphs ? pad : mouse;

        /// <summary>
        /// While the screen is open, every pad event is the Workbench's: LB must not also step the
        /// build menu behind it, nor the d-pad zoom the camera. The actions are read by polling in
        /// <see cref="PadProcess"/>, which marking the event handled doesn't change.
        /// </summary>
        public override void _Input(InputEvent e)
        {
            if (IsOpen && (e is InputEventJoypadButton || e is InputEventJoypadMotion))
            {
                GetViewport().SetInputAsHandled();
            }
        }

        /// <summary>Per frame, after the hint is drawn: the glyphs, the buttons, the move and the ring.</summary>
        private void PadProcess(double delta)
        {
            bool pad = InputDevices.PadInUse;
            // Not mid-drag: a rebuild drops the card table a mouse drag's id points into.
            if (pad != _padGlyphs && _draggingId < 0)
            {
                _padGlyphs = pad;
                Rebuild(); // the picker's and dial's labels name the pad's buttons, or the mouse's arrows
            }
            if (!pad)
            {
                _pad.Hide();
                return;
            }

            List<Stop> stops = PadStops();
            if (_padFollowHint && HintStop(stops) is Stop hinted)
            {
                _pad.Focus(hinted);
                _padFollowHint = false;
            }
            else if (_padFollowHint && _shownHint.Kind == WorkbenchHintKind.None)
            {
                _padFollowHint = false; // no guide here: the ring stays where it is
            }

            // The A that opened the screen (at the station or a golem) is still "just pressed" in
            // the frame it opens, and must not also send the card the ring lands on.
            bool opening = Engine.GetProcessFrames() <= _padOpenedFrame + 1;
            if (!opening && Input.IsActionJustPressed(BuildCursorNode.CancelAction))
            {
                Close();
                return;
            }
            if (!opening && _pad.Current(stops) is Stop at)
            {
                PadButtons(at);
            }
            if (!IsOpen)
            {
                return;
            }
            _pad.Update(PadStops(), delta);
            if (_pad.Moved)
            {
                _padFollowHint = false; // the player is choosing now
            }
        }

        // --- The stops --------------------------------------------------------------------------

        private List<Stop> PadStops()
        {
            var stops = new List<Stop>
            {
                new Stop(nameof(PadStopKind.Close), 0, _close),
                new Stop(nameof(PadStopKind.Trigger), 0, _triggerRow.Socket),
            };
            for (int i = 0; i < _stepRows.Count; i++)
            {
                if (_stepRows[i].Visible)
                {
                    stops.Add(new Stop(nameof(PadStopKind.Step), i, _stepRows[i].Socket));
                }
            }
            int k = 0;
            foreach (Button chassis in _chassisList.GetChildren().OfType<Button>())
            {
                if (chassis.IsVisibleInTree())
                {
                    stops.Add(new Stop(nameof(PadStopKind.Chassis), k, chassis));
                }
                k++;
            }
            k = 0;
            foreach (WorkbenchCardControl card in _vault.GetChildren().OfType<WorkbenchCardControl>())
            {
                if (!card.IsQueuedForDeletion())
                {
                    stops.Add(new Stop(nameof(PadStopKind.Vault), k++, card, _vaultScroll));
                }
            }
            stops.Add(new Stop(nameof(PadStopKind.Lever), 0, _lever));
            stops.Add(new Stop(nameof(PadStopKind.Patent), 0, _patent));
            return stops;
        }

        /// <summary>The stop the guide's hint points the player to next: its glowing card, or the socket or lever it lights.</summary>
        private Stop? HintStop(List<Stop> stops)
        {
            WorkbenchHintTarget hint = _shownHint;
            switch (hint.Kind)
            {
                case WorkbenchHintKind.VaultCard:
                    Control card = HintedVaultCard(hint);
                    return card == null ? null : stops.Where(s => s.Control == card).Cast<Stop?>().FirstOrDefault();
                case WorkbenchHintKind.Lever:
                    return PadFocus.Find(stops, nameof(PadStopKind.Lever), 0);
                case WorkbenchHintKind.GoodPicker:
                case WorkbenchHintKind.QuantityDial:
                case WorkbenchHintKind.DragOut:
                    return hint.Zone.Kind == DropZoneKind.LogicCore
                        ? PadFocus.Find(stops, nameof(PadStopKind.Trigger), 0)
                        : PadFocus.Find(stops, nameof(PadStopKind.Step), hint.Zone.AppendageIndex);
                default:
                    return null;
            }
        }

        // --- The buttons ------------------------------------------------------------------------

        private void PadButtons(Stop at)
        {
            if (Input.IsActionJustPressed(PlayerNode.Interact))
            {
                Press(at);
            }
            bool trigger = at.Kind == nameof(PadStopKind.Trigger);
            if (!trigger && at.Kind != nameof(PadStopKind.Step))
            {
                return;
            }
            int slot = trigger ? -1 : at.Index;
            if (Input.IsActionJustPressed(PlayerNode.Carry))
            {
                // X takes the card out, as dragging it off its socket does.
                if (slot < 0 && _session.DraftLogicCore != null)
                {
                    _session.RemoveFromSlot(WorkbenchCardRef.FromTriggerSocket(_session.DraftLogicCore));
                }
                else if (slot >= 0 && _session.DraftAppendageAt(slot) is PunchCards.AppendageActionDefinition card)
                {
                    _session.RemoveFromSlot(WorkbenchCardRef.FromSocket(card, slot));
                }
                _padFollowHint = true;
            }
            if (slot < 0)
            {
                return;
            }
            // The dials leave the ring where it is (see the class summary).
            if (Input.IsActionJustPressed(PadBindings.BuildNext))
            {
                _session.CycleDraftItemType(slot, -1);
            }
            if (Input.IsActionJustPressed(BuildCursorNode.RotateAction))
            {
                _session.CycleDraftItemType(slot, +1);
            }
            if (Input.IsActionJustPressed(PadBindings.BuildDemolish))
            {
                _session.AdjustDraftQuantity(slot, -1);
            }
            if (Input.IsActionJustPressed(PadBindings.BuildPlace))
            {
                _session.AdjustDraftQuantity(slot, +1);
            }
        }

        private void Press(Stop at)
        {
            if (at.Kind == nameof(PadStopKind.Vault))
            {
                if (at.Control is WorkbenchCardControl card)
                {
                    WorkbenchZone? zone = _session.SendToNextSocket(card.CardRef);
                    if (zone != null)
                    {
                        _landingZone = zone; // the same landing as a drop
                        _padFollowHint = true;
                    }
                }
                return;
            }
            if (at.Control is BaseButton button && !button.Disabled)
            {
                button.EmitSignal(BaseButton.SignalName.Pressed);
                _padFollowHint = at.Kind != nameof(PadStopKind.Close);
            }
        }
    }
}
