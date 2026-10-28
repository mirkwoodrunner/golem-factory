using GolemFactory.Compat;

namespace GolemFactory.World
{
    /// <summary>
    /// Every lighting number in one place (docs/art-pipeline-design.md §6, decision 8: start
    /// subtle, then let the playtest's <c>light</c> question tune it). <c>ShellNode</c> and
    /// <c>BuildingsLayer</c> read these; no node writes its own. <c>LightingRulesTests</c> says
    /// whether a retune keeps the floor and its overlays readable, and <c>check_art.py</c> reads
    /// <see cref="Ambient"/> (keep the <c>new Color(...)</c> form) to check the golems' tints under it.
    /// </summary>
    public static class LightingRules
    {
        /// <summary>
        /// The room's tint, the <c>CanvasModulate</c>: warm, averaging 0.873. The HUD and the
        /// golem badges are on their own canvas layers and are not tinted.
        /// </summary>
        public static readonly Color Ambient = new Color(0.94f, 0.88f, 0.80f);

        /// <summary>The 0.95 grey the room had before, Unity's third setting: what <see cref="FloorLightnessSlack"/> is measured from.</summary>
        public static readonly Color PreviousAmbient = new Color(0.95f, 0.95f, 0.95f);

        // --- The wall sconces ----------------------------------------------------------------

        public static readonly Color SconceColor = new Color(1f, 0.72f, 0.42f);
        public const float SconceEnergy = 1.1f;
        public const float SconceRadiusCells = 5.5f;

        /// <summary>
        /// Unity's LampLightInset: the pool sits just inside the room, not at the painted flame. A
        /// sprite's height under top-down is fake elevation, and a light at the flame would put
        /// its core outside the room.
        /// </summary>
        public const float SconceInsetCells = -0.25f;

        /// <summary>Flat steps in a pool, so it reads as pixel art rather than an airbrush.</summary>
        public const int SconceBands = 5;

        /// <summary>
        /// The pool's steps, centre outwards: band <c>k</c> starts at radius <c>k / bands</c> (of
        /// the light's radius) and holds strength <c>(bands - k) / bands</c>, so its average
        /// matches the smooth linear fall-off it replaces. A last stop at radius 1 is zero, so
        /// nothing lights past the pool's edge.
        /// </summary>
        public static (float Offset, float Strength)[] SconceBandStops(int bands = SconceBands)
        {
            var stops = new (float, float)[bands + 1];
            for (int k = 0; k < bands; k++)
            {
                stops[k] = ((float)k / bands, (float)(bands - k) / bands);
            }
            stops[bands] = (1f, 0f);
            return stops;
        }

        // --- Boilers ---------------------------------------------------------------------------

        /// <summary>The fire ramp's light tone, #f0a040.</summary>
        public static readonly Color BoilerGlowColor = new Color(0.94f, 0.63f, 0.25f);
        public const float BoilerGlowRadiusCells = 1.5f;
        public const float BoilerGlowIdle = 0.35f;
        public const float BoilerGlowPerWorkingGolem = 0.08f;

        /// <summary>The most working golems the glow counts: a boiler powers no more (SteamNetwork.MaxGolemsPerBoiler).</summary>
        public const int BoilerGlowMaxGolems = 8;

        /// <summary>
        /// A boiler's firebox glow: none with no Coke (a boiler that runs dry goes dark at once,
        /// which reads as the Starved blue's cause), otherwise brighter the more golems it keeps
        /// working.
        /// </summary>
        public static float BoilerGlow(int cokeStock, int workingGolems)
        {
            if (cokeStock <= 0)
            {
                return 0f;
            }
            int counted = workingGolems < 0 ? 0 : workingGolems > BoilerGlowMaxGolems ? BoilerGlowMaxGolems : workingGolems;
            return BoilerGlowIdle + BoilerGlowPerWorkingGolem * counted;
        }

        /// <summary>
        /// Whether a pipe piece shows its hot variant: exactly where steam reaches, the answer the
        /// guide reads for "steam reaches here". The hot pictures are drawn with the buildings (S3).
        /// </summary>
        public static bool PipeHot(bool steamReaches) => steamReaches;

        // --- What LightingRulesTests holds a retune to (§6.4) ------------------------------------

        /// <summary>How much darker (OKLab lightness) the floor may get than under <see cref="PreviousAmbient"/>.</summary>
        public const float FloorLightnessSlack = 0.03f;

        /// <summary>A source or target tile over the floor differs from the floor by at least this (OKLab).</summary>
        public const float OverlayMinApart = 0.10f;

        /// <summary>A Stalled, Starved or Waiting badge over the floor differs from it by at least this (OKLab).</summary>
        public const float BadgeMinApart = 0.15f;

        /// <summary>No ambient channel goes below this.</summary>
        public const float AmbientMinChannel = 0.75f;
    }
}
