namespace GolemFactory.World
{
    /// <summary>
    /// The player's Reduced motion setting (docs/menus-design.md §5), as one answer per kind of
    /// motion so every node asks the same question. Off, everything plays as
    /// docs/game-feel-design.md describes. On, motion that only decorates stops; motion that tells
    /// the player something stays, held still or reduced to a fade:
    /// <list type="bullet">
    ///   <item>Kept: golems' animation frames (what a golem is doing), the walk, mood tints and
    ///   badges, popups (they fade in place instead of rising), the tower's banner and its stage.</item>
    ///   <item>Stopped: particles (dust, sparks, steam puffs, a good's pop and glint), squashes and
    ///   bumps, a stuck golem's shake, a golem's idle bob and work sway (the Overclocker's pulsing glow with it), pulsing rings, the camera's glides
    ///   (it cuts instead), the tower's swell and flash, a card's landing, the tape's typing and
    ///   the lever's travel.</item>
    /// </list>
    /// Like every feel rule (game-feel-design §0), none of this changes what the simulation does.
    /// </summary>
    public sealed class MotionPreference
    {
        /// <summary>The setting. Set by the Godot layer from the player's settings.</summary>
        public bool Reduced { get; set; }

        /// <summary>Dust, sparks, steam puffs, and a good popping out of a crate with its glint.</summary>
        public bool Particles => !Reduced;

        /// <summary>A building's landing and demolition squash, a crate's bump, the tower's swell and flash.</summary>
        public bool Squash => !Reduced;

        /// <summary>A golem's shake on stopping.</summary>
        public bool Shake => !Reduced;

        /// <summary>A golem's idle bob, and its stretch, skew and lift at work. Its frames still play.</summary>
        public bool GolemSway => !Reduced;

        /// <summary>Breathing rings and pulsing outlines; on Reduced they sit at their middle strength.</summary>
        public bool Pulse => !Reduced;

        /// <summary>A popup's rise. It still fades where it appeared.</summary>
        public bool PopupRise => !Reduced;

        /// <summary>The camera's glides and its follow ease. On Reduced it cuts to where it is going.</summary>
        public bool CameraGlides => !Reduced;

        /// <summary>A card's landing, the tape typing its line, and the Engage lever's travel.</summary>
        public bool WorkbenchMotion => !Reduced;

        /// <summary>
        /// The strength a pulse holds when it can't breathe: the middle of its 0..1 range, so a
        /// ring stays as visible as it is on average.
        /// </summary>
        public const float StillPulse = 0.5f;

        /// <summary>A breathing value, or <see cref="StillPulse"/> on Reduced.</summary>
        public float Breathe(float breathing01) => Pulse ? breathing01 : StillPulse;
    }
}
