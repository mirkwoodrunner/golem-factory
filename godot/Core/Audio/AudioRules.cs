using GolemFactory.Compat;

namespace GolemFactory.Audio
{
    /// <summary>
    /// The mix's numbers the data doesn't own (docs/audio-design.md §4), like
    /// <see cref="World.LightingRules"/>: pure, so a test pins them without an engine.
    /// </summary>
    public static class AudioRules
    {
        // --- Slot names code asks for ------------------------------------------------------

        public const string Workshop = "amb.workshop";
        public const string ClockTick = "amb.clock";
        public const string Steam = "amb.steam";
        public const string CardSnap = "ui.card-snap";
        public const string Engage = "ui.engage";
        public const string Patent = "ui.patent";
        public const string Claim = "ui.claim";
        public const string StallChime = "ui.stall-chime";
        public const string Place = "ui.place";
        public const string Demolish = "ui.demolish";
        public const string Collect = "ui.collect";
        public const string TowerFanfare = "music.tower-fanfare";

        /// <summary>The music layers, quietest first: <c>music.layer.1</c> to <c>.4</c>.</summary>
        public static string MusicLayer(int layer) => "music.layer." + layer;

        public const int MusicLayerCount = 4;

        // --- The steam hiss ------------------------------------------------------------------

        /// <summary>Working golems at which the boilers' hiss is at full volume.</summary>
        public const int FullHissAt = 24;

        /// <summary>
        /// The hiss's volume, 0 to 1, from the golems steam is powering this tick: silent with
        /// none, rising to full by <see cref="FullHissAt"/>. A square root, so the first few
        /// golems are heard and the last few don't make it loud.
        /// </summary>
        public static float SteamHissVolume(int workingGolems) =>
            workingGolems <= 0 ? 0f : Mathf.Sqrt(Mathf.Clamp01(workingGolems / (float)FullHissAt));

        // --- The music layers ----------------------------------------------------------------

        /// <summary>Golems the factory needs for each layer: 1 always, 2 from 4, 3 from 16, 4 from 48.</summary>
        public static readonly int[] LayerThresholds = { 0, 4, 16, 48 };

        /// <summary>A layer fades out only once the count falls below this share of its threshold.</summary>
        public const float LayerDropShare = 0.8f;

        /// <summary>How long a layer takes to fade in or out, in seconds.</summary>
        public const float LayerFadeSeconds = 4f;

        /// <summary>
        /// How many layers play for <paramref name="golems"/>, given <paramref name="current"/>
        /// play now: up as soon as a threshold is reached, down only below 80% of it, so one golem
        /// wrecked at the threshold doesn't drop a layer (§4).
        /// </summary>
        public static int MusicLayers(int golems, int current)
        {
            int layers = Mathf.Clamp(current, 1, MusicLayerCount);
            while (layers < MusicLayerCount && golems >= LayerThresholds[layers])
            {
                layers++;
            }
            while (layers > 1 && golems < LayerThresholds[layers - 1] * LayerDropShare)
            {
                layers--;
            }
            return layers;
        }

        // --- Where a sound is heard ----------------------------------------------------------

        /// <summary>How far past the screen's edge a sound still plays, as a share of the screen's width.</summary>
        public const float OffScreenMargin = 0.25f;

        /// <summary>
        /// Whether a sound at <paramref name="position"/> plays, for a view centred on
        /// <paramref name="viewCentre"/> with half-size <paramref name="viewHalfSize"/> (all in
        /// cells): on screen, or within <see cref="OffScreenMargin"/> of a screen width past an
        /// edge. Panning and quieting toward the edge are the engine's (§5).
        /// </summary>
        public static bool Audible(Vector2 position, Vector2 viewCentre, Vector2 viewHalfSize)
        {
            float margin = 2f * viewHalfSize.x * OffScreenMargin;
            return Mathf.Abs(position.x - viewCentre.x) <= viewHalfSize.x + margin
                && Mathf.Abs(position.y - viewCentre.y) <= viewHalfSize.y + margin;
        }

        // --- Volume controls -----------------------------------------------------------------

        /// <summary>A slider's 0-1 as a bus level in dB: 1 is 0 dB, 0 is silence.</summary>
        public static float SliderToDb(float slider) =>
            slider <= 0.0001f ? -80f : 20f * System.MathF.Log10(Mathf.Clamp01(slider));
    }
}
