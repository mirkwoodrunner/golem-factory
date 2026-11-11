using Godot;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// Battery (docs/steam-deck-design.md D8.3): while the window doesn't have focus (the Steam
    /// overlay or the Deck's quick-access menu over it, or another window on a desktop), the game
    /// draws at <see cref="BackgroundFps"/>. The tick clock catches up every tick a slow frame owes
    /// (SimulationClockTests), so the factory runs on at its own speed. Focus back, and the cap
    /// goes back to what it was. The player adds one.
    /// </summary>
    public partial class BackgroundFrameCap : Node
    {
        /// <summary>The frame rate while unfocused. <b>TUNING.</b></summary>
        public const int BackgroundFps = 10;

        private int _focusedMaxFps;
        private bool _capped;

        /// <summary>Whether the cap is on, for a scenario.</summary>
        public bool Capped => _capped;

        /// <summary>
        /// A scenario run is often launched without the window's focus, and capped it would draw at
        /// 10 fps: windowed captures crawl and `perf` measures the cap. So the cap stays off under a
        /// scenario, unless the scenario testing it turns this on.
        /// </summary>
        public static bool EvenUnderScenario { get; set; }

        public override void _Notification(int what)
        {
            if (PlaytestNode.UnderScenario && !EvenUnderScenario)
            {
                return;
            }
            if (what == NotificationApplicationFocusOut && !_capped)
            {
                _capped = true;
                _focusedMaxFps = Engine.MaxFps;
                Engine.MaxFps = BackgroundFps;
            }
            else if (what == NotificationApplicationFocusIn && _capped)
            {
                _capped = false;
                Engine.MaxFps = _focusedMaxFps;
            }
        }

        public override void _ExitTree()
        {
            if (_capped)
            {
                _capped = false;
                Engine.MaxFps = _focusedMaxFps; // a reload under the cap must not keep it
            }
        }
    }
}
