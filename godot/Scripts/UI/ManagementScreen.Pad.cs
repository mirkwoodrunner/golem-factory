using System.Collections.Generic;
using System.Linq;
using Godot;
using GolemFactory.UI;
using Stop = GolemFactory.Nodes.PadFocus.Stop;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The Management screen by pad (docs/steam-deck-design.md D6). Its tabs are built by several
    /// nodes, and most hold ordinary controls, so the ring is generic: its stops are the tab
    /// buttons, Close, and every visible button and slider in the shown tab
    /// (<see cref="PadFocus.ControlsIn"/>). LB and RB change tab. A presses a button (a checkbox
    /// ticks); LT and RT step a slider, as they turn the Workbench's dials. The right stick scrolls
    /// the tab: a long list, or the Ledger's chart, which a mouse drags. B, or Start as it opened
    /// it, closes.
    ///
    /// <para>
    /// Settings' key buttons are left out: capturing a key is the keyboard's (by design, with a
    /// pad the keys don't matter), and a pad press there would wait for a key the pad can't give.
    /// </para>
    /// </summary>
    public partial class ManagementScreen
    {
        public const string StopTab = "Tab";
        public const string StopClose = "Close";
        public const string StopItem = "Item";

        private PadFocus _pad;
        private Button _closeButton;
        private ulong _padOpenedFrame;

        // --- For scenarios ----------------------------------------------------------------------
        public bool PadRingVisible => _pad.RingVisible;
        public Rect2 PadRingRect => _pad.RingRect;
        public string PadFocusKind => _pad.Kind;
        public int PadFocusIndex => _pad.Index;
        public Control PadFocusControl => _pad.Current(PadStops())?.Control;

        /// <summary>Every control the ring can rest on now, for a scenario.</summary>
        public IEnumerable<Control> PadStopControls => PadStops().Select(s => s.Control);

        private void PadOpened()
        {
            _pad ??= new PadFocus(_root) { Default = DefaultStop };
            _pad.Reset();
            _padOpenedFrame = Engine.GetProcessFrames();
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
            // The Start that opened the screen is still "just pressed" in the frame it opens.
            bool opening = Engine.GetProcessFrames() <= _padOpenedFrame + 1;
            if (!opening && (Input.IsActionJustPressed(BuildCursorNode.CancelAction) || Input.IsActionJustPressed(ToggleAction)))
            {
                Close();
                return;
            }
            if (!opening)
            {
                if (Input.IsActionJustPressed(PadBindings.BuildNext))
                {
                    StepTab(-1);
                }
                if (Input.IsActionJustPressed(BuildCursorNode.RotateAction))
                {
                    StepTab(+1);
                }
                if (_pad.Current(PadStops()) is Stop at)
                {
                    if (Input.IsActionJustPressed(PlayerNode.Interact))
                    {
                        PadFocus.Press(at.Control);
                    }
                    PadFocus.StepSlider(at.Control);
                    if (!IsOpen)
                    {
                        return;
                    }
                }
            }
            if (_tabContent.TryGetValue(_tabs.ActiveTab, out Control content))
            {
                PadFocus.ScrollWithStick(content, delta);
            }
            _pad.Update(PadStops(), delta);
        }

        /// <summary>The tab before or after the shown one in the bar, skipping hidden tabs; the ring goes into it.</summary>
        private void StepTab(int step)
        {
            List<ManagementTab> tabs = ManagementTabs.BarOrder.Where(t => _tabButtons.TryGetValue(t, out Button b) && b.Visible).ToList();
            int at = tabs.IndexOf(_tabs.ActiveTab);
            if (tabs.Count == 0 || at < 0)
            {
                return;
            }
            SelectTab(tabs[((at + step) % tabs.Count + tabs.Count) % tabs.Count]);
            _pad.Reset(); // the ring starts in the new tab
        }

        private List<Stop> PadStops()
        {
            var stops = new List<Stop>();
            int k = 0;
            foreach (ManagementTab tab in ManagementTabs.BarOrder)
            {
                if (_tabButtons.TryGetValue(tab, out Button button) && button.IsVisibleInTree())
                {
                    stops.Add(new Stop(StopTab, k, button));
                }
                k++;
            }
            stops.Add(new Stop(StopClose, 0, _closeButton));
            if (_tabContent.TryGetValue(_tabs.ActiveTab, out Control content))
            {
                HashSet<Control> keys = Settings != null ? new HashSet<Control>(Settings.KeyButtons.Values) : new HashSet<Control>();
                stops.AddRange(PadFocus.ControlsIn(content, StopItem, keys.Contains));
            }
            return stops;
        }

        /// <summary>The first item in the tab, else the shown tab's button.</summary>
        private Stop? DefaultStop(List<Stop> stops) =>
            stops.Where(s => s.Kind == StopItem).Cast<Stop?>().FirstOrDefault()
            ?? PadFocus.Find(stops, StopTab, ManagementTabs.BarOrder.ToList().IndexOf(_tabs.ActiveTab));
    }
}
