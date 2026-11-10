using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// A screen's focus ring for the pad (docs/steam-deck-design.md D6): which stop it rests on,
    /// moving it by the d-pad or the left stick, and drawing it. The screen lists its stops each
    /// frame and decides what its buttons do with <see cref="Current"/>.
    ///
    /// <para>
    /// Not Godot's focus: screens rebuild their controls from data (a crate's rows, the vault's
    /// cards), and Godot's focus is lost with every rebuilt control. A stop is kept by its kind and
    /// index (the third row of the crate, STEP 2), so a rebuild keeps it. The ring moves to the
    /// nearest stop on screen the way pressed, preferring one in line. Within a scrolling list, up
    /// and down step through the list and scroll it; from outside, the ring enters only a list item
    /// in its window.
    /// </para>
    /// </summary>
    public sealed class PadFocus
    {
        /// <summary>The direction held: one step on the push, then this long before it repeats...</summary>
        public const double FirstRepeat = 0.3;
        /// <summary>...then a step this often.</summary>
        public const double Repeat = 0.12;

        // Cream, solid and wider than the guide's pulsing brass frames, so the two never read as one.
        private static readonly Color RingColor = new Color(0.98f, 0.95f, 0.86f);
        private const int RingBorder = 3;
        private const float RingGrow = 7f;

        /// <summary>One place the ring can rest: its kind and index, its control, and the list it scrolls in.</summary>
        public readonly struct Stop
        {
            public readonly string Kind;
            public readonly int Index;
            public readonly Control Control;
            public readonly ScrollContainer List;
            /// <summary>The list's columns: up and down within it step by this many (a grid of options).</summary>
            public readonly int Columns;

            public Stop(string kind, int index, Control control, ScrollContainer list = null, int columns = 1)
            {
                Kind = kind;
                Index = index;
                Control = control;
                List = list;
                Columns = columns;
            }
        }

        private readonly Panel _ring;
        private double _heldFor = -1;
        private double _nextStep;

        public PadFocus(Control parent)
        {
            _ring = new Panel { Name = "PadFocusRing", MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
            _ring.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                DrawCenter = false,
                BorderWidthLeft = RingBorder, BorderWidthTop = RingBorder, BorderWidthRight = RingBorder, BorderWidthBottom = RingBorder,
                BorderColor = RingColor,
                CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
            });
            parent.AddChild(_ring);
        }

        /// <summary>What the ring rests on.</summary>
        public string Kind { get; private set; }
        public int Index { get; private set; }
        public bool HasFocus { get; private set; }

        /// <summary>Whether the player moved the ring this frame.</summary>
        public bool Moved { get; private set; }

        /// <summary>Where the ring goes when it has none, or its stop went: the screen's choice, else the first stop.</summary>
        public Func<List<Stop>, Stop?> Default { get; set; }

        public bool RingVisible => _ring.Visible;
        public Rect2 RingRect => new Rect2(_ring.GlobalPosition, _ring.Size);

        public void Focus(Stop stop)
        {
            Kind = stop.Kind;
            Index = stop.Index;
            HasFocus = true;
        }

        /// <summary>Forgets the stop, as a screen opens: the next update puts the ring at <see cref="Default"/>.</summary>
        public void Reset()
        {
            HasFocus = false;
            _heldFor = -1;
        }

        public static Stop? Find(List<Stop> stops, string kind, int index)
        {
            foreach (Stop stop in stops)
            {
                if (stop.Kind == kind && stop.Index == index)
                {
                    return stop;
                }
            }
            return null;
        }

        public Stop? Current(List<Stop> stops) => HasFocus ? Find(stops, Kind, Index) : null;

        /// <summary>Hides the ring and forgets a held direction: the pad isn't in use.</summary>
        public void Hide()
        {
            _ring.Visible = false;
            _heldFor = -1;
        }

        /// <summary>
        /// Per frame while the pad is in use: keeps the ring on a stop that exists, moves it if a
        /// direction is held (and <paramref name="move"/>), scrolls its list to it, and draws it.
        /// </summary>
        public void Update(List<Stop> stops, double delta, bool move = true)
        {
            Moved = false;
            if (Current(stops) == null)
            {
                Fallback(stops);
            }
            if (move)
            {
                Step(stops, delta);
            }
            Draw(stops);
        }

        private void Fallback(List<Stop> stops)
        {
            if (HasFocus)
            {
                Stop? same = stops.Where(s => s.Kind == Kind).OrderBy(s => Math.Abs(s.Index - Index)).Cast<Stop?>().FirstOrDefault();
                if (same != null)
                {
                    Focus(same.Value);
                    return;
                }
            }
            Stop? chosen = Default?.Invoke(stops) ?? (stops.Count > 0 ? stops[0] : null);
            if (chosen != null)
            {
                Focus(chosen.Value);
            }
            else
            {
                HasFocus = false;
            }
        }

        private void Step(List<Stop> stops, double delta)
        {
            // The d-pad (ui_*) and the left stick (the move actions). The keyboard's arrows would
            // move it too, but a key hands the screen back to the mouse first.
            Vector2 held = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down")
                + Input.GetVector(PlayerNode.MoveLeft, PlayerNode.MoveRight, PlayerNode.MoveUp, PlayerNode.MoveDown);
            Vector2 dir = Mathf.Abs(held.X) >= Mathf.Abs(held.Y)
                ? new Vector2(Mathf.Abs(held.X) >= 0.5f ? Mathf.Sign(held.X) : 0f, 0f)
                : new Vector2(0f, Mathf.Abs(held.Y) >= 0.5f ? Mathf.Sign(held.Y) : 0f);
            if (dir == Vector2.Zero)
            {
                _heldFor = -1;
                return;
            }
            if (_heldFor < 0)
            {
                _heldFor = 0;
                _nextStep = FirstRepeat;
            }
            else
            {
                _heldFor += delta;
                if (_heldFor < _nextStep)
                {
                    return;
                }
                _nextStep += Repeat;
            }
            if (Current(stops) is Stop from && Neighbour(stops, from, dir) is Stop to)
            {
                Focus(to);
                Moved = true;
            }
        }

        /// <summary>
        /// The stop <paramref name="dir"/> leads to from <paramref name="from"/>: within a list, up
        /// and down step through it (a row of a grid at a time); otherwise the nearest stop that way on screen, preferring one
        /// in line, and a list item only if it's in its list's window.
        /// </summary>
        private static Stop? Neighbour(List<Stop> stops, Stop from, Vector2 dir)
        {
            if (from.List != null && dir.X == 0f && Find(stops, from.Kind, from.Index + (int)dir.Y * from.Columns) is Stop next)
            {
                return next;
            }
            Vector2 a = from.Control.GetGlobalRect().GetCenter();
            var across = new Vector2(dir.Y, dir.X);
            Stop? best = null;
            float bestScore = float.MaxValue;
            foreach (Stop s in stops)
            {
                if (s.Kind == from.Kind && s.Index == from.Index)
                {
                    continue;
                }
                Rect2 rect = s.Control.GetGlobalRect();
                if (s.List != null && !s.List.GetGlobalRect().Intersects(rect))
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

        private void Draw(List<Stop> stops)
        {
            if (Current(stops) is not Stop at)
            {
                _ring.Visible = false;
                return;
            }
            if (at.List != null)
            {
                // Asked every frame it's focused: a row rebuilt or laid out late still comes into view.
                Rect2 view = at.List.GetGlobalRect();
                Rect2 rect = at.Control.GetGlobalRect();
                if (rect.Position.Y < view.Position.Y - 1f || rect.End.Y > view.End.Y + 1f)
                {
                    at.List.EnsureControlVisible(at.Control);
                }
            }
            Rect2 ring = at.Control.GetGlobalRect().Grow(RingGrow);
            _ring.Visible = true;
            _ring.GlobalPosition = ring.Position;
            _ring.Size = ring.Size;
        }
    }
}
