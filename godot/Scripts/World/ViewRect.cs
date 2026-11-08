using Godot;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// What the camera shows this frame, in world pixels, two cells wider on every side
    /// (docs/region-design.md §4, docs/performance-report-endless.md). A golem or building node
    /// outside it skips its per-frame presentation (the picture, the badge, the motion) and catches
    /// up when it comes back into view; the simulation never skips. Refreshed once a frame by
    /// <see cref="WorldNode"/>, the scene's first node, before any view runs.
    /// </summary>
    public static class ViewRect
    {
        /// <summary>The margin past the screen's edge that still counts as in view, in cells: a tall sprite's head pokes in.</summary>
        public const int MarginCells = 3;

        private static Rect2 _rect;
        private static bool _known;

        /// <summary>Whether off-screen skipping is on: the `perf` scenario turns it off to measure without it.</summary>
        public static bool Enabled { get; set; } = true;

        public static void Refresh(Viewport viewport)
        {
            if (viewport == null)
            {
                _known = false;
                return;
            }
            Transform2D toWorld = viewport.GetCanvasTransform().AffineInverse();
            Rect2 screen = viewport.GetVisibleRect();
            Vector2 a = toWorld * screen.Position;
            Vector2 b = toWorld * screen.End;
            float margin = MarginCells * GridConversions.CellPixels;
            _rect = new Rect2(a, b - a).Abs().Grow(margin);
            _known = true;
        }

        /// <summary>Whether a node at <paramref name="worldPosition"/> is in view, or near enough; true while the view is unknown.</summary>
        public static bool Shows(Vector2 worldPosition) => !Enabled || !_known || _rect.HasPoint(worldPosition);
    }
}
