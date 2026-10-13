namespace GolemFactory.ClockTower
{
    /// <summary>
    /// Which picture the Clock Tower wears (G10): the roped-off site, then one picture per stage
    /// the player has completed, up to the finished tower. All six pictures share one 192x640
    /// canvas so they swap in place as the tower grows. They are an AI redraw (ComfyUI) that
    /// replaced <c>Tools/Art/generate_clock_tower_art.py</c>'s 192x448 originals, which is why
    /// that generator now refuses to write into <c>godot/art/</c>.
    /// </summary>
    public static class ClockTowerArt
    {
        public const string Site = "clock_tower_site";

        /// <summary>The pictures run stage0 (open, nothing built) to stage4 (finished).</summary>
        public const int FinalPicture = 4;

        /// <summary>The canvas: three cells wide, ten tall, bottom row on the footprint's south edge.</summary>
        public const int CanvasWidth = 192;
        public const int CanvasHeight = 640;

        public static string SpriteFor(ClockTowerSite site)
        {
            if (site == null)
            {
                return Site;
            }
            if (site.IsComplete)
            {
                return Stage(FinalPicture);
            }
            return site.IsOpen ? Stage(site.StageIndex) : Site;
        }

        /// <summary>The picture after <paramref name="completed"/> finished stages, capped at the last.</summary>
        public static string Stage(int completed) =>
            "clock_tower_stage" + (completed < 0 ? 0 : completed > FinalPicture ? FinalPicture : completed);
    }
}
