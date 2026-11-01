using GolemFactory.Compat;

namespace GolemFactory.World
{
    /// <summary>
    /// Every game-feel curve in one place (docs/game-feel-design.md §4), like
    /// <see cref="LightingRules"/>: pure, timed in real seconds since the moment, so a test pins
    /// each one's shape and that it ends where it began. Nodes read these and draw; none keeps a
    /// curve of its own. Each returns the rest pose past its end.
    /// </summary>
    public static class FeelRules
    {
        // --- A building placed or demolished -------------------------------------------------

        public const float PlaceSeconds = 0.32f;

        /// <summary>
        /// A building landing: dropped in a little tall, squashed as it lands, a spring back.
        /// (scaleX, scaleY) about its feet.
        /// </summary>
        public static (float x, float y) PlaceSquash(float age)
        {
            if (age < 0f || age >= PlaceSeconds)
            {
                return (1f, 1f);
            }
            float t = age / PlaceSeconds;
            if (t < 0.25f)
            {
                float u = t / 0.25f; // tall to squashed
                return (Mathf.Lerp(0.92f, 1.14f, u), Mathf.Lerp(1.10f, 0.84f, u));
            }
            float r = (t - 0.25f) / 0.75f; // a damped spring back to rest
            float wobble = Mathf.Cos(r * Mathf.PI * 2.5f) * (1f - r) * (1f - r);
            return (1f + 0.14f * wobble, 1f - 0.16f * wobble);
        }

        public const float DemolishSeconds = 0.24f;

        /// <summary>A building coming down: a quick squash flat and gone. (scaleX, scaleY, alpha).</summary>
        public static (float x, float y, float alpha) DemolishSquash(float age)
        {
            if (age < 0f)
            {
                return (1f, 1f, 1f);
            }
            if (age >= DemolishSeconds)
            {
                return (1.2f, 0f, 0f);
            }
            float t = age / DemolishSeconds;
            return (1f + 0.2f * t, 1f - t * t, 1f - t * t * t);
        }

        // --- Dust, sparks, puffs ------------------------------------------------------------

        public const float DustSeconds = 0.6f;

        /// <summary>The build dust round a building's feet: swells, drifts out and up, fades.</summary>
        public static (float scale, float rise, float spread, float alpha) Dust(float age)
        {
            if (age < 0f || age >= DustSeconds)
            {
                return (0f, 0f, 0f, 0f);
            }
            float t = age / DustSeconds;
            float ease = 1f - (1f - t) * (1f - t);
            return (Mathf.Lerp(0.6f, 1.3f, ease), 0.12f * ease, 0.45f * ease, t < 0.1f ? t / 0.1f * 0.85f : 0.85f * (1f - (t - 0.1f) / 0.9f));
        }

        public const float SparkSeconds = 0.28f;

        /// <summary>The Overclocker's sparks on a stamp: a hot flash that flies out and dims fast.</summary>
        public static (float scale, float rise, float alpha) Spark(float age)
        {
            if (age < 0f || age >= SparkSeconds)
            {
                return (0f, 0f, 0f);
            }
            float t = age / SparkSeconds;
            return (Mathf.Lerp(0.7f, 1.2f, t), 0.18f * t, 1f - t * t);
        }

        /// <summary>A burning boiler puffs from its chimney this often, in seconds of factory time.</summary>
        public const float BoilerPuffInterval = 1.4f;

        // --- A good landing in a crate --------------------------------------------------------

        public const float CratePopSeconds = 0.36f;

        /// <summary>The crate's bump as a good lands: a small squash and back. (scaleX, scaleY).</summary>
        public static (float x, float y) CrateBump(float age)
        {
            if (age < 0f || age >= CratePopSeconds)
            {
                return (1f, 1f);
            }
            float t = age / CratePopSeconds;
            float s = Mathf.Sin(t * Mathf.PI) * (1f - t);
            return (1f + 0.08f * s, 1f - 0.10f * s);
        }

        /// <summary>
        /// The good itself, popping out of the crate's lid: up, a hang, and fading back in.
        /// (rise in cells, scale, alpha).
        /// </summary>
        public static (float rise, float scale, float alpha) GoodPop(float age)
        {
            if (age < 0f || age >= CratePopSeconds)
            {
                return (0f, 0f, 0f);
            }
            float t = age / CratePopSeconds;
            float arc = 4f * t * (1f - t);
            return (0.45f * arc, Mathf.Lerp(0.6f, 0.9f, Mathf.Min(1f, t * 3f)), t < 0.7f ? 1f : 1f - (t - 0.7f) / 0.3f);
        }

        /// <summary>One crate pops at most this often, so a crate a belt fills doesn't flicker.</summary>
        public const float CratePopMinInterval = 0.25f;

        // --- The Workbench --------------------------------------------------------------------

        public const float CardLandSeconds = 0.22f;

        /// <summary>
        /// A card landing in a socket with weight: it drops the last few pixels, squashes, and
        /// settles. (drop in pixels, scaleX, scaleY), about the card's centre.
        /// </summary>
        public static (float drop, float x, float y) CardLand(float age)
        {
            if (age < 0f || age >= CardLandSeconds)
            {
                return (0f, 1f, 1f);
            }
            float t = age / CardLandSeconds;
            if (t < 0.3f)
            {
                float u = t / 0.3f;
                return (-6f * (1f - u * u), 1.06f - 0.06f * u, 1.06f - 0.06f * u);
            }
            float r = (t - 0.3f) / 0.7f;
            float squash = Mathf.Sin(r * Mathf.PI) * (1f - r);
            return (0f, 1f + 0.06f * squash, 1f - 0.08f * squash);
        }

        /// <summary>How fast the diagnostic tape prints a new line, in characters a second.</summary>
        public const float TapeCharsPerSecond = 60f;

        /// <summary>How much of a <paramref name="length"/>-character line the tape has printed <paramref name="age"/> seconds in.</summary>
        public static int TapePrinted(float age, int length) =>
            age < 0f ? 0 : Mathf.Min(length, Mathf.FloorToInt(age * TapeCharsPerSecond));

        // --- The Clock Tower's reveal -------------------------------------------------------

        public const float TowerRevealSeconds = 0.5f;

        /// <summary>The tower as its new stage appears: a swell and settle, and a warm flash. (scale, flash 0-1).</summary>
        public static (float scale, float flash) TowerReveal(float age)
        {
            if (age < 0f || age >= TowerRevealSeconds)
            {
                return (1f, 0f);
            }
            float t = age / TowerRevealSeconds;
            return (1f + 0.05f * Mathf.Sin(t * Mathf.PI) * (1f - t), (1f - t) * (1f - t));
        }
    }
}
