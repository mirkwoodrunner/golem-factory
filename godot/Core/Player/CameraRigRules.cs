using GolemFactory.Compat;

namespace GolemFactory.Player
{
    /// <summary>
    /// The camera's two rules, extracted from Unity's CameraRigController (milestone G4) in the
    /// project's "pure math, thin applier" idiom: it eases toward the player rather than snapping,
    /// and zoom is a clamped orthographic size -- the half-height of the view, in cells.
    /// </summary>
    public static class CameraRigRules
    {
        /// <summary>Unity's _followLerpSpeed: the fraction of the gap closed per second.</summary>
        public const float FollowLerpSpeed = 5f;

        /// <summary>Unity's _zoomSpeed, in orthographic-size units per second of input.</summary>
        public const float ZoomSpeed = 5f;

        public const float MinOrthographicSize = 3f;
        public const float MaxOrthographicSize = 15f;

        /// <summary>Sandbox.unity's camera started at orthographic size 10.</summary>
        public const float DefaultOrthographicSize = 10f;

        /// <summary>
        /// One frame of following, exponential ease: the camera closes 1 - e^(-speed * dt) of the
        /// gap. Unity's Lerp(camera, target, speed * dt) matched it at 60 FPS but lurched after a
        /// hitch, since a long frame closed a linear share of the gap; this is frame-rate
        /// independent and never overshoots.
        /// </summary>
        public static Vector2 Follow(Vector2 camera, Vector2 target, float deltaTime, float speed = FollowLerpSpeed)
        {
            float t = 1f - (float)System.Math.Exp(-speed * deltaTime);
            return new Vector2(camera.x + (target.x - camera.x) * t, camera.y + (target.y - camera.y) * t);
        }

        /// <summary>One frame of zooming: positive input zooms in (a smaller size), clamped.</summary>
        public static float Zoom(float size, float input, float deltaTime, float speed = ZoomSpeed) =>
            input == 0f
                ? size
                : Mathf.Clamp(size - input * speed * deltaTime, MinOrthographicSize, MaxOrthographicSize);
    }
}
