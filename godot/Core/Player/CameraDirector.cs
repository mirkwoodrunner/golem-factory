using System;
using System.Collections.Generic;
using GolemFactory.Compat;

namespace GolemFactory.Player
{
    /// <summary>What the camera is looking at: the player, a stopped golem, the Clock Tower's event, or the title's workshop.</summary>
    public enum CameraFocus
    {
        Player,
        Alert,
        TowerEvent,

        /// <summary>The workshop behind the title screen (docs/menus-design.md §6): a slow drift over it.</summary>
        Showcase,
    }

    /// <summary>Where the Clock Tower's stage event is (docs/game-feel-design.md §3).</summary>
    public enum TowerEventPhase
    {
        None,

        /// <summary>The camera glides to the tower.</summary>
        GlideIn,

        /// <summary>At the tower: the bell, then the new stage shown.</summary>
        Reveal,

        /// <summary>The camera glides back to the player; the player has control again.</summary>
        GlideBack,
    }

    /// <summary>
    /// The camera's decisions (docs/game-feel-design.md §2–§3, decisions 2–4): the fixed zoom
    /// levels it glides between, what it looks at, and the Clock Tower's stage event. Pure, so a
    /// test drives it; <c>CameraRigNode</c> feeds it input and moves the camera where it says.
    /// Positions are cells in Core's frame.
    /// </summary>
    public sealed class CameraDirector
    {
        /// <summary>
        /// The zoom levels, as orthographic sizes (half the view's height, in cells), closest
        /// first: Close (3) and Wide (15), the limits the art was checked at; Normal (10), the
        /// starting view; and Near (5.625), one art pixel to one screen pixel at 720p.
        /// </summary>
        public static readonly float[] ZoomLevels = { 3f, 5.625f, 10f, 15f };

        public static readonly string[] ZoomNames = { "Close", "Near", "Normal", "Wide" };

        public const int DefaultZoomLevel = 2;

        /// <summary>How fast a zoom glides to its level: the share of the gap closed per second, as Follow's.</summary>
        public const float ZoomGlideSpeed = 8f;

        /// <summary>How fast the camera glides to a focus that isn't the player (slower than following, so it reads as a move).</summary>
        public const float FocusGlideSpeed = 3.5f;

        // The tower's event, in seconds: about four, with the factory running throughout (decision 4).
        public const float TowerGlideInSeconds = 1.2f;
        public const float TowerRevealSeconds = 2.4f;
        public const float TowerGlideBackSeconds = 0.8f;

        /// <summary>When in the reveal the new stage appears (after the bell has struck).</summary>
        public const float TowerRevealAt = 0.6f;

        /// <summary>The title's view: Near, so the golems read at work.</summary>
        public const int ShowcaseZoomLevel = 1;

        /// <summary>
        /// The title's drift around the workshop's middle, in cells either way, and the seconds
        /// each sway takes: slow, and out of step, so it never visibly repeats.
        /// </summary>
        public const float ShowcaseDriftX = 1.5f;
        public const float ShowcaseDriftY = 0.75f;
        public const float ShowcaseSwayXSeconds = 47f;
        public const float ShowcaseSwayYSeconds = 31f;

        private float _showcaseClock;

        private readonly List<string> _alertOrder = new List<string>();
        private string _alertGolem;
        private float _eventClock;

        public int ZoomLevel { get; private set; } = DefaultZoomLevel;

        /// <summary>
        /// The player's Reduced motion (<see cref="World.MotionPreference"/>): with it the camera
        /// cuts instead of gliding. <see cref="World.SandboxWorld"/> hands in its own.
        /// </summary>
        public World.MotionPreference Motion { get; set; } = new World.MotionPreference();

        /// <summary>The size the camera glides to.</summary>
        public float TargetSize => ZoomLevels[ZoomLevel];

        public CameraFocus Focus { get; private set; } = CameraFocus.Player;

        /// <summary>The golem the camera is on while <see cref="Focus"/> is Alert.</summary>
        public string AlertGolemId => Focus == CameraFocus.Alert ? _alertGolem : null;

        public TowerEventPhase TowerPhase { get; private set; }

        /// <summary>The stage the event is for: its number and name, for the banner.</summary>
        public int TowerStageNumber { get; private set; }
        public string TowerStageName { get; private set; }

        /// <summary>True once the reveal has struck: the new stage shows from then on.</summary>
        public bool TowerRevealed { get; private set; }

        /// <summary>The middle of the title's workshop, in cells, while <see cref="Focus"/> is Showcase.</summary>
        public Vector2 ShowcaseCentre { get; private set; }

        /// <summary>The title is up: the camera drifts over <paramref name="centre"/> at Near.</summary>
        public void BeginShowcase(Vector2 centre)
        {
            Focus = CameraFocus.Showcase;
            ShowcaseCentre = centre;
            ZoomLevel = ShowcaseZoomLevel;
            _showcaseClock = 0f;
            _alertGolem = null;
        }

        /// <summary>One frame of the drift, in real seconds.</summary>
        public void UpdateShowcase(float deltaTime)
        {
            if (Focus == CameraFocus.Showcase)
            {
                _showcaseClock += deltaTime;
            }
        }

        /// <summary>
        /// Where the title's camera looks this frame, in cells: the workshop's middle swaying
        /// slowly; held still on Reduced motion.
        /// </summary>
        public Vector2 ShowcasePoint
        {
            get
            {
                if (!Motion.CameraGlides)
                {
                    return ShowcaseCentre;
                }
                const float tau = 2f * (float)Math.PI;
                return new Vector2(
                    ShowcaseCentre.x + ShowcaseDriftX * (float)Math.Sin(tau * _showcaseClock / ShowcaseSwayXSeconds),
                    ShowcaseCentre.y + ShowcaseDriftY * (float)Math.Sin(tau * _showcaseClock / ShowcaseSwayYSeconds));
            }
        }

        /// <summary>
        /// One wheel notch: positive zooms in a level, negative out, clamped at the ends.
        /// Returns whether the level changed.
        /// </summary>
        public bool ZoomStep(int notches)
        {
            int next = Mathf.Clamp(ZoomLevel - Math.Sign(notches), 0, ZoomLevels.Length - 1);
            bool changed = next != ZoomLevel;
            ZoomLevel = next;
            return changed;
        }

        /// <summary>The level nearest <paramref name="size"/>, for a scenario or a load that sets a size directly.</summary>
        public void SnapZoomTo(float size)
        {
            int best = 0;
            for (int i = 1; i < ZoomLevels.Length; i++)
            {
                if (Math.Abs(ZoomLevels[i] - size) < Math.Abs(ZoomLevels[best] - size))
                {
                    best = i;
                }
            }
            ZoomLevel = best;
        }

        /// <summary>One frame of the zoom's glide toward its level.</summary>
        public float GlideZoom(float size, float deltaTime) =>
            Motion.CameraGlides ? Approach(size, TargetSize, deltaTime, ZoomGlideSpeed) : TargetSize;

        /// <summary>
        /// The alert strip was clicked: the camera goes to the next stopped golem, worst first
        /// (<paramref name="stoppedWorstFirst"/>, the strip's order), wrapping round. Returns the
        /// golem's id, or null with none stopped. Ignored during the tower's event.
        /// </summary>
        public string NextAlert(IReadOnlyList<string> stoppedWorstFirst)
        {
            if (TowerPhase == TowerEventPhase.GlideIn || TowerPhase == TowerEventPhase.Reveal ||
                Focus == CameraFocus.Showcase || stoppedWorstFirst == null || stoppedWorstFirst.Count == 0)
            {
                return null;
            }
            int index = Focus == CameraFocus.Alert && _alertGolem != null
                ? IndexOf(stoppedWorstFirst, _alertGolem) + 1
                : 0;
            _alertGolem = stoppedWorstFirst[index % stoppedWorstFirst.Count];
            Focus = CameraFocus.Alert;
            return _alertGolem;
        }

        /// <summary>
        /// The player walked (or the golem looked at is gone): the camera goes back to them.
        /// During the tower's glide-in or reveal, any input skips the event instead.
        /// </summary>
        public void PlayerMoved()
        {
            if (TowerPhase == TowerEventPhase.GlideIn || TowerPhase == TowerEventPhase.Reveal)
            {
                SkipTowerEvent();
                return;
            }
            if (Focus == CameraFocus.Alert)
            {
                Focus = CameraFocus.Player;
                _alertGolem = null;
            }
        }

        /// <summary>
        /// A Clock Tower stage completed: the event begins (decision 4). Not over the title: its
        /// camera stays on the workshop, and nobody is playing to be shown the stage.
        /// </summary>
        public void BeginTowerEvent(int stageNumber, string stageName)
        {
            if (Focus == CameraFocus.Showcase)
            {
                return;
            }
            TowerStageNumber = stageNumber;
            TowerStageName = stageName;
            TowerPhase = TowerEventPhase.GlideIn;
            TowerRevealed = false;
            Focus = CameraFocus.TowerEvent;
            _alertGolem = null;
            _eventClock = 0f;
        }

        /// <summary>Any key during the event: straight to the end, the stage shown, the camera back with the player.</summary>
        public void SkipTowerEvent()
        {
            if (TowerPhase == TowerEventPhase.None)
            {
                return;
            }
            TowerRevealed = true;
            TowerPhase = TowerEventPhase.GlideBack;
            Focus = CameraFocus.Player;
            _eventClock = TowerGlideInSeconds + TowerRevealSeconds;
        }

        /// <summary>
        /// One frame of the event, in real seconds (the factory keeps working: decision 4).
        /// Returns true on the frame the reveal strikes, for the bell and the dust.
        /// </summary>
        public bool UpdateTowerEvent(float deltaTime)
        {
            if (TowerPhase == TowerEventPhase.None)
            {
                return false;
            }
            _eventClock += deltaTime;
            bool struck = false;
            if (TowerPhase == TowerEventPhase.GlideIn && _eventClock >= TowerGlideInSeconds)
            {
                TowerPhase = TowerEventPhase.Reveal;
            }
            if (TowerPhase == TowerEventPhase.Reveal && !TowerRevealed && _eventClock >= TowerGlideInSeconds + TowerRevealAt)
            {
                TowerRevealed = true;
                struck = true;
            }
            if (TowerPhase == TowerEventPhase.Reveal && _eventClock >= TowerGlideInSeconds + TowerRevealSeconds)
            {
                TowerPhase = TowerEventPhase.GlideBack;
                Focus = CameraFocus.Player;
            }
            if (TowerPhase == TowerEventPhase.GlideBack &&
                _eventClock >= TowerGlideInSeconds + TowerRevealSeconds + TowerGlideBackSeconds)
            {
                TowerPhase = TowerEventPhase.None;
            }
            return struck;
        }

        /// <summary>
        /// How fast the camera moves this frame: following the player, or gliding to a focus.
        /// Infinite on Reduced motion: <see cref="CameraRigRules.Follow"/> then cuts to the target.
        /// </summary>
        public float GlideSpeed => !Motion.CameraGlides || Focus == CameraFocus.Showcase
            ? float.PositiveInfinity // the showcase's drift is its own smooth path
            : Focus == CameraFocus.Player && TowerPhase != TowerEventPhase.GlideBack
                ? CameraRigRules.FollowLerpSpeed
                : FocusGlideSpeed;

        private static float Approach(float value, float target, float deltaTime, float speed)
        {
            float t = 1f - (float)Math.Exp(-speed * deltaTime);
            return value + (target - value) * t;
        }

        private static int IndexOf(IReadOnlyList<string> list, string id)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == id)
                {
                    return i;
                }
            }
            return -1; // gone from the list: the next is its first
        }
    }

    /// <summary>The Clock Tower event's banner: "Stage 2 complete · The Movement".</summary>
    public static class TowerBanner
    {
        public static string Text(int stageNumber, string stageName) =>
            string.IsNullOrEmpty(stageName)
                ? $"Clock Tower: stage {stageNumber} complete"
                : $"Stage {stageNumber} complete · {stageName}";
    }
}
