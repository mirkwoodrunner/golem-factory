using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.UI;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The Workbench by pad (docs/steam-deck-design.md D6.W, the owner's answers): a focus ring
    /// moved over the screen's stops by the d-pad or the left stick, and A, X and the shoulders on
    /// what it rests on. A on a vault card sends it to the next socket that takes it
    /// (<see cref="WorkbenchSession.SendToNextSocket"/>, the mouse's own drag verbs); X on a
    /// socketed card takes it out; LB and RB change a Haul's good, LT and RT a batch size; A on
    /// a chassis, the lever, PATENT or CLOSE presses it; B closes the screen.
    ///
    /// <para>
    /// The focus is this node's own, not Godot's: the cards are rebuilt whenever the session's
    /// Version moves, and Godot's focus would be lost with every rebuilt control. A stop is kept
    /// by what it is (the third vault card, STEP 2, the lever), so a rebuild keeps it. Moving is
    /// by the stops' positions on screen, except within the vault, which steps through its list.
    /// It follows the guide's hint: the screen opens on the glowing card, and after a send the
    /// ring moves to the next one.
    /// </para>
    /// </summary>
    public partial class WorkbenchScreen
    {
        /// <summary>The direction held: one step on the push, then this long before it repeats...</summary>
        public const double PadFirstRepeat = 0.3;
        /// <summary>...then a step this often.</summary>
        public const double PadRepeat = 0.12;

        // Cream, solid and wider than the hint's pulsing brass frame, so the two never read as one.
        private static readonly Color PadRingColor = new Color(0.98f, 0.95f, 0.86f);
        private const int PadRingBorder = 3;
        private const float PadRingGrow = 7f;

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

        private readonly struct PadStop
        {
            public readonly PadStopKind Kind;
            public readonly int Index;
            public readonly Control Control;

            public PadStop(PadStopKind kind, int index, Control control)
            {
                Kind = kind;
                Index = index;
                Control = control;
            }
        }

        private Panel _padRing;
        private PadStopKind _padKind = PadStopKind.Vault;
        private int _padIndex;
        private bool _padHasFocus;
        private bool _padFollowHint;
        private bool _padGlyphs;
        private double _padHeldFor = -1;
        private double _padNextStep;
        private ulong _padOpenedFrame;

        // --- For scenarios ----------------------------------------------------------------------

        /// <summary>Whether the ring is drawn this frame.</summary>
        public bool PadRingVisible => _padRing.Visible;

        /// <summary>The ring's rectangle on screen.</summary>
        public Rect2 PadRingRect => new Rect2(_padRing.GlobalPosition, _padRing.Size);

        /// <summary>What the ring rests on: its kind, and its index among that kind.</summary>
        public PadStopKind PadFocusKind => _padKind;
        public int PadFocusIndex => _padIndex;

        /// <summary>The control the ring rests on, or null.</summary>
        public Control PadFocusControl => _padHasFocus && FindStop(PadStops(), _padKind, _padIndex) is PadStop s ? s.Control : null;

        // --- Wiring from the screen -------------------------------------------------------------

        private void BuildPadRing()
        {
            _padRing = new Panel { Name = "PadFocusRing", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
            _padRing.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                DrawCenter = false,
                BorderWidthLeft = PadRingBorder, BorderWidthTop = PadRingBorder,
                BorderWidthRight = PadRingBorder, BorderWidthBottom = PadRingBorder,
                BorderColor = PadRingColor,
                CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
            });
            _root.AddChild(_padRing);
        }

        /// <summary>A new opening: the ring goes to the hinted card (or the first vault card).</summary>
        private void PadOpened()
        {
            _padHasFocus = false;
            _padFollowHint = true;
            _padHeldFor = -1;
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
                _padRing.Visible = false;
                _padHeldFor = -1;
                return;
            }

            List<PadStop> stops = PadStops();
            if (_padFollowHint && HintStop(stops) is PadStop hinted)
            {
                Focus(hinted);
                _padFollowHint = false;
            }
            else if (_padFollowHint && _shownHint.Kind == WorkbenchHintKind.None)
            {
                _padFollowHint = false; // no guide here: the ring stays where it is
            }
            if (!_padHasFocus || FindStop(stops, _padKind, _padIndex) == null)
            {
                Fallback(stops);
            }

            // The A that opened the screen (at the station or a golem) is still "just pressed" in
            // the frame it opens, and must not also send the card the ring lands on.
            bool opening = Engine.GetProcessFrames() <= _padOpenedFrame + 1;
            if (!opening && Input.IsActionJustPressed(BuildCursorNode.CancelAction))
            {
                Close();
                return;
            }
            if (!opening)
            {
                PadButtons(stops);
            }
            if (!IsOpen)
            {
                return;
            }
            PadMove(stops, delta);
            DrawPadRing(stops);
        }

        // --- The stops --------------------------------------------------------------------------

        private List<PadStop> PadStops()
        {
            var stops = new List<PadStop> { new PadStop(PadStopKind.Close, 0, _close), new PadStop(PadStopKind.Trigger, 0, _triggerRow.Socket) };
            for (int i = 0; i < _stepRows.Count; i++)
            {
                if (_stepRows[i].Visible)
                {
                    stops.Add(new PadStop(PadStopKind.Step, i, _stepRows[i].Socket));
                }
            }
            int k = 0;
            foreach (Button chassis in _chassisList.GetChildren().OfType<Button>())
            {
                if (chassis.IsVisibleInTree())
                {
                    stops.Add(new PadStop(PadStopKind.Chassis, k, chassis));
                }
                k++;
            }
            k = 0;
            foreach (WorkbenchCardControl card in _vault.GetChildren().OfType<WorkbenchCardControl>())
            {
                if (!card.IsQueuedForDeletion())
                {
                    stops.Add(new PadStop(PadStopKind.Vault, k++, card));
                }
            }
            stops.Add(new PadStop(PadStopKind.Lever, 0, _lever));
            stops.Add(new PadStop(PadStopKind.Patent, 0, _patent));
            return stops;
        }

        private static PadStop? FindStop(List<PadStop> stops, PadStopKind kind, int index)
        {
            foreach (PadStop stop in stops)
            {
                if (stop.Kind == kind && stop.Index == index)
                {
                    return stop;
                }
            }
            return null;
        }

        private void Focus(PadStop stop)
        {
            _padKind = stop.Kind;
            _padIndex = stop.Index;
            _padHasFocus = true;
        }

        /// <summary>A stop that went (a step hidden, the vault shorter): the nearest of its kind, else the first card.</summary>
        private void Fallback(List<PadStop> stops)
        {
            if (_padHasFocus)
            {
                PadStop? same = stops.Where(s => s.Kind == _padKind).OrderBy(s => System.Math.Abs(s.Index - _padIndex)).Cast<PadStop?>().FirstOrDefault();
                if (same != null)
                {
                    Focus(same.Value);
                    return;
                }
            }
            PadStop first = stops.FirstOrDefault(s => s.Kind == PadStopKind.Vault);
            Focus(first.Control != null ? first : stops.First(s => s.Kind == PadStopKind.Trigger));
        }

        /// <summary>The stop the guide's hint points the player to next: its glowing card, or the socket or lever it lights.</summary>
        private PadStop? HintStop(List<PadStop> stops)
        {
            WorkbenchHintTarget hint = _shownHint;
            switch (hint.Kind)
            {
                case WorkbenchHintKind.VaultCard:
                    Control card = HintedVaultCard(hint);
                    return card == null ? null : stops.Where(s => s.Control == card).Cast<PadStop?>().FirstOrDefault();
                case WorkbenchHintKind.Lever:
                    return FindStop(stops, PadStopKind.Lever, 0);
                case WorkbenchHintKind.GoodPicker:
                case WorkbenchHintKind.QuantityDial:
                case WorkbenchHintKind.DragOut:
                    return hint.Zone.Kind == DropZoneKind.LogicCore ? FindStop(stops, PadStopKind.Trigger, 0) : FindStop(stops, PadStopKind.Step, hint.Zone.AppendageIndex);
                default:
                    return null;
            }
        }

        // --- The buttons ------------------------------------------------------------------------

        private void PadButtons(List<PadStop> stops)
        {
            if (FindStop(stops, _padKind, _padIndex) is not PadStop at)
            {
                return;
            }
            if (Input.IsActionJustPressed(PlayerNode.Interact))
            {
                Press(at);
            }
            if (at.Kind != PadStopKind.Trigger && at.Kind != PadStopKind.Step)
            {
                return;
            }
            int slot = at.Kind == PadStopKind.Step ? at.Index : -1;
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
            // The dials leave the ring where it is: the player is turning this card's dial, and the
            // hint (often the lever, once the cards match) mustn't pull the ring off it mid-turn.
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

        private void Press(PadStop at)
        {
            switch (at.Kind)
            {
                case PadStopKind.Vault:
                    if (at.Control is WorkbenchCardControl card)
                    {
                        WorkbenchZone? zone = _session.SendToNextSocket(card.CardRef);
                        if (zone != null)
                        {
                            _landingZone = zone; // the same landing as a drop
                            _padFollowHint = true;
                        }
                    }
                    break;
                case PadStopKind.Chassis:
                case PadStopKind.Lever:
                case PadStopKind.Patent:
                case PadStopKind.Close:
                    if (at.Control is BaseButton button && !button.Disabled)
                    {
                        button.EmitSignal(BaseButton.SignalName.Pressed);
                        _padFollowHint = at.Kind != PadStopKind.Close;
                    }
                    break;
            }
        }

        // --- Moving -----------------------------------------------------------------------------

        private void PadMove(List<PadStop> stops, double delta)
        {
            // The d-pad (ui_*) and the left stick (the move actions); the keyboard's arrows would
            // move it too, but a key hands the screen back to the mouse first.
            Vector2 held = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down")
                + Input.GetVector(PlayerNode.MoveLeft, PlayerNode.MoveRight, PlayerNode.MoveUp, PlayerNode.MoveDown);
            Vector2 dir = Mathf.Abs(held.X) >= Mathf.Abs(held.Y)
                ? new Vector2(Mathf.Abs(held.X) >= 0.5f ? Mathf.Sign(held.X) : 0f, 0f)
                : new Vector2(0f, Mathf.Abs(held.Y) >= 0.5f ? Mathf.Sign(held.Y) : 0f);
            if (dir == Vector2.Zero)
            {
                _padHeldFor = -1;
                return;
            }
            if (_padHeldFor < 0)
            {
                _padHeldFor = 0;
                _padNextStep = PadFirstRepeat;
            }
            else
            {
                _padHeldFor += delta;
                if (_padHeldFor < _padNextStep)
                {
                    return;
                }
                _padNextStep += PadRepeat;
            }
            if (FindStop(stops, _padKind, _padIndex) is PadStop from && Neighbour(stops, from, dir) is PadStop to)
            {
                Focus(to);
                _padFollowHint = false; // the player is choosing now
            }
        }

        /// <summary>
        /// The stop <paramref name="dir"/> leads to from <paramref name="from"/>. Up and down within
        /// the vault step through its list (scrolling); otherwise the nearest stop that way on
        /// screen, preferring one in line, and a vault card only if it's in the vault's window.
        /// </summary>
        private PadStop? Neighbour(List<PadStop> stops, PadStop from, Vector2 dir)
        {
            if (from.Kind == PadStopKind.Vault && dir.X == 0f)
            {
                PadStop? next = FindStop(stops, PadStopKind.Vault, from.Index + (int)dir.Y);
                if (next != null)
                {
                    return next;
                }
            }
            Rect2 view = _vaultScroll.GetGlobalRect();
            Vector2 a = from.Control.GetGlobalRect().GetCenter();
            Vector2 across = new Vector2(dir.Y, dir.X);
            PadStop? best = null;
            float bestScore = float.MaxValue;
            foreach (PadStop s in stops)
            {
                if (s.Kind == from.Kind && s.Index == from.Index)
                {
                    continue;
                }
                Rect2 rect = s.Control.GetGlobalRect();
                if (s.Kind == PadStopKind.Vault && !view.Intersects(rect))
                {
                    continue;
                }
                Vector2 d = rect.GetCenter() - a;
                float along = d.Dot(dir);
                if (along <= 4f)
                {
                    continue;
                }
                float score = along + 2f * Mathf.Abs(d.Dot(across));
                if (score < bestScore)
                {
                    bestScore = score;
                    best = s;
                }
            }
            return best;
        }

        // --- The ring ---------------------------------------------------------------------------

        private void DrawPadRing(List<PadStop> stops)
        {
            if (FindStop(stops, _padKind, _padIndex) is not PadStop at)
            {
                _padRing.Visible = false;
                return;
            }
            if (at.Kind == PadStopKind.Vault)
            {
                // Asked every frame it's focused: a card rebuilt or laid out late still comes into view.
                Rect2 view = _vaultScroll.GetGlobalRect();
                Rect2 card = at.Control.GetGlobalRect();
                if (card.Position.Y < view.Position.Y - 1f || card.End.Y > view.End.Y + 1f)
                {
                    _vaultScroll.EnsureControlVisible(at.Control);
                }
            }
            Rect2 rect = at.Control.GetGlobalRect().Grow(PadRingGrow);
            _padRing.Visible = true;
            _padRing.GlobalPosition = rect.Position;
            _padRing.Size = rect.Size;
        }
    }
}
