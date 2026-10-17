using GolemFactory.Compat;
using GolemFactory.PunchCards;

namespace GolemFactory.Golems
{
    /// <summary>
    /// The motion a golem makes WHILE a step runs, so a working golem looks different from an
    /// idle one. Pure, like <see cref="GolemAnimationUtility"/>: the node passes the step and how
    /// far through it the golem is, and applies the pose this returns to the body sprite.
    ///
    /// <para>
    /// Driven by step progress, never by wall-clock time. The motion pauses with the clock,
    /// speeds up with it, and its last stroke lands on the tick the step finishes, so the motion
    /// tells the player when the output will appear.
    /// </para>
    ///
    /// <para>
    /// The motion follows the VERB, so every golem hauling looks like it is hauling, and each
    /// chassis's signature verb has its own: the Presser stamps, the Overclocker stamps faster
    /// and glows, the Scavenger tugs at the node behind it, the Zeppelin crouches and hops. Refine
    /// and LoadIntoBuffer (the id-routed verbs) keep the mood bob.
    /// </para>
    /// </summary>
    public static class GolemWorkMotion
    {
        public enum Motion
        {
            None,

            /// <summary>Assemble: wind up tall, slam down squashed, spring back.</summary>
            Stamp,

            /// <summary>The Overclocker's Assemble and Repeat: a quicker stamp that glows.</summary>
            FastStamp,

            /// <summary>Extract: reach back to the node behind and yank, once per unit.</summary>
            Tug,

            /// <summary>Haul: lean back over the tile behind and straighten up under the load.</summary>
            Heave,

            /// <summary>Push: rock back, then thrust forward at the tile in front.</summary>
            Lunge,

            /// <summary>FreightLaunch: crouch lower and lower, then hop as the cargo flies.</summary>
            Launch,
        }

        /// <summary>Ticks per stamping stroke: 0.8 s at 1x, so a 4 s recipe stamps five times.</summary>
        public const int StampStrokeTicks = 8;

        /// <summary>The Overclocker's stroke: 0.5 s, so it reads as the faster machine.</summary>
        public const int FastStampStrokeTicks = 5;

        /// <summary>Ticks per tug: an extraction's setup plus a unit is about one pull.</summary>
        public const int TugStrokeTicks = 7;

        /// <summary>Where in a stroke the stamp lands (the rest is wind-up and recovery).</summary>
        public const float StampImpact = 0.62f;

        /// <summary>Where in a launch the Zeppelin leaves the floor.</summary>
        public const float LaunchTakeoff = 0.7f;

        private const float WindUpStretch = 0.05f;
        private const float WindUpLift = 0.03f;
        private const float ImpactSquash = 0.14f;

        // Bigger than the Hauler's lean: the Scavenger is thin, and a small lean on it is lost.
        private const float TugReach = 0.11f;
        private const float TugTilt = 0.24f;
        private const float HeaveReach = 0.08f;
        private const float HeaveTilt = 0.18f;
        private const float LungeReach = 0.12f;
        private const float LungeTilt = 0.16f;
        private const float LaunchCrouch = 0.16f;
        private const float LaunchHop = 0.3f;

        public readonly struct Pose
        {
            /// <summary>Horizontal and vertical scale about the feet (1 = the drawn sprite).</summary>
            public readonly float ScaleX;
            public readonly float ScaleY;

            /// <summary>Lift off the floor, in cells (+ is up).</summary>
            public readonly float Lift;

            /// <summary>True on the frames just after the stamp lands, for an impact effect.</summary>
            public readonly bool Impact;

            /// <summary>Shift along the golem's facing, in cells (+ toward the tile in front).</summary>
            public readonly float Forward;

            /// <summary>
            /// Lean about the feet, in radians (+ leans toward the tile in front). The node draws
            /// it only when the golem faces east or west; facing north or south, a lean is
            /// toward or away from the camera, and <see cref="Forward"/> carries it alone.
            /// </summary>
            public readonly float Tilt;

            /// <summary>How hot the golem glows, 0 (not at all) to 1.</summary>
            public readonly float Glow;

            public Pose(float scaleX, float scaleY, float lift, bool impact)
                : this(scaleX, scaleY, lift, impact, 0f, 0f, 0f)
            {
            }

            public Pose(float scaleX, float scaleY, float lift, bool impact, float forward, float tilt, float glow)
            {
                ScaleX = scaleX;
                ScaleY = scaleY;
                Lift = lift;
                Impact = impact;
                Forward = forward;
                Tilt = tilt;
                Glow = glow;
            }

            public static Pose Rest => new Pose(1f, 1f, 0f, false);
        }

        /// <summary>How long one impact puff lives, in seconds.</summary>
        public const float PuffSeconds = 0.5f;

        /// <summary>
        /// One steam puff, <paramref name="age"/> seconds after the stamp landed: it pops out at
        /// the feet, drifts sideways and up, swells and fades. Past its life the puff is invisible.
        /// </summary>
        public static (float scale, float rise, float drift, float alpha) Puff(float age)
        {
            if (age < 0f || age >= PuffSeconds)
            {
                return (0f, 0f, 0f, 0f);
            }

            float t = age / PuffSeconds;
            float easeOut = 1f - (1f - t) * (1f - t);
            return (
                scale: Mathf.Lerp(0.8f, 1.6f, easeOut),
                rise: 0.15f * easeOut,
                drift: 0.2f + 0.35f * easeOut,
                alpha: t < 0.15f ? t / 0.15f * 0.9f : 0.9f * (1f - (t - 0.15f) / 0.85f));
        }

        /// <summary>The motion this golem makes for the step it is on.</summary>
        public static Motion MotionFor(string chassisName, AppendageActionType action)
        {
            switch (action)
            {
                case AppendageActionType.Assemble:
                    return chassisName == "MainspringOverclocker" ? Motion.FastStamp : Motion.Stamp;
                case AppendageActionType.Repeat:
                    return Motion.FastStamp; // only the Overclocker can hold the card
                case AppendageActionType.ExtractFromNode:
                    return Motion.Tug;
                case AppendageActionType.Haul:
                    return Motion.Heave;
                case AppendageActionType.Push:
                    return Motion.Lunge;
                case AppendageActionType.FreightLaunch:
                    return Motion.Launch;
                default:
                    return Motion.None;
            }
        }

        /// <summary>Whether this golem has a work motion for the step it is on.</summary>
        public static bool Animates(string chassisName, AppendageActionType action) =>
            MotionFor(chassisName, action) != Motion.None;

        /// <summary>The pose for <paramref name="motion"/> at this point in the step.</summary>
        public static Pose Evaluate(Motion motion, float progressTicks, int durationTicks)
        {
            switch (motion)
            {
                case Motion.Stamp: return Stamp(progressTicks, durationTicks);
                case Motion.FastStamp: return FastStamp(progressTicks, durationTicks);
                case Motion.Tug: return Tug(progressTicks, durationTicks);
                case Motion.Heave: return Heave(progressTicks, durationTicks);
                case Motion.Lunge: return Lunge(progressTicks, durationTicks);
                case Motion.Launch: return Launch(progressTicks, durationTicks);
                default: return Pose.Rest;
            }
        }

        /// <summary>
        /// How many strokes a step of this length makes: whole strokes only, at least one, so
        /// the last stroke ends exactly at the step's end.
        /// </summary>
        public static int StrokeCount(int durationTicks) => StrokeCount(durationTicks, StampStrokeTicks);

        public static int StrokeCount(int durationTicks, int strokeTicks) =>
            Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(1, durationTicks) / (float)Mathf.Max(1, strokeTicks)));

        /// <summary>
        /// The stamp at <paramref name="progressTicks"/> (fractional: ticks done plus the clock's
        /// tick fraction) through a step of <paramref name="durationTicks"/>.
        /// </summary>
        public static Pose Stamp(float progressTicks, int durationTicks) =>
            Stamp(progressTicks, durationTicks, StampStrokeTicks);

        /// <summary>
        /// The Overclocker's stamp: the same stroke at <see cref="FastStampStrokeTicks"/>, and a
        /// glow that flares as each stamp lands and cools through the wind-up, so a Repeat batch
        /// reads as a machine running hot.
        /// </summary>
        public static Pose FastStamp(float progressTicks, int durationTicks)
        {
            Pose p = Stamp(progressTicks, durationTicks, FastStampStrokeTicks);
            if (!InStep(progressTicks, durationTicks, out float progress))
            {
                return p;
            }
            float u = Mathf.Repeat(progress * StrokeCount(durationTicks, FastStampStrokeTicks), 1f);
            float sinceImpact = Mathf.Repeat(u - StampImpact, 1f);
            float glow = 0.35f + 0.65f * (1f - sinceImpact) * (1f - sinceImpact);
            return new Pose(p.ScaleX, p.ScaleY, p.Lift, p.Impact, 0f, 0f, glow);
        }

        /// <summary>
        /// Extract: per stroke, reach back toward the node behind (eased in), then yank forward
        /// past upright and settle -- a pull on a rope, once per <see cref="TugStrokeTicks"/>.
        /// </summary>
        public static Pose Tug(float progressTicks, int durationTicks)
        {
            if (!InStep(progressTicks, durationTicks, out float progress))
            {
                return Pose.Rest;
            }
            float u = Mathf.Repeat(progress * StrokeCount(durationTicks, TugStrokeTicks), 1f);

            float lean; // -1 reached back, +0.3 yanked forward, 0 upright
            if (u < 0.5f)
            {
                float t = u / 0.5f;
                lean = -(1f - (1f - t) * (1f - t));
            }
            else if (u < 0.65f)
            {
                lean = Mathf.Lerp(-1f, 0.3f, (u - 0.5f) / 0.15f);
            }
            else
            {
                float t = (u - 0.65f) / 0.35f;
                lean = 0.3f * (1f - t) * (1f - t);
            }

            // The yank tightens the body a little, as if taking the strain.
            float strain = u >= 0.5f && u < 0.8f ? Mathf.Sin((u - 0.5f) / 0.3f * Mathf.PI) * 0.05f : 0f;
            return new Pose(1f + strain, 1f - strain, 0f, false, lean * TugReach, lean * TugTilt, 0f);
        }

        /// <summary>
        /// Haul: one move per step, however short. Lean back over the tile behind, scoop, and
        /// straighten up squashed under the weight, springing back as the step ends.
        /// </summary>
        public static Pose Heave(float progressTicks, int durationTicks)
        {
            if (!InStep(progressTicks, durationTicks, out float u))
            {
                return Pose.Rest;
            }

            if (u < 0.45f)
            {
                float t = u / 0.45f;
                float e = 1f - (1f - t) * (1f - t);
                return new Pose(1f, 1f - 0.03f * e, 0f, false, -HeaveReach * e, -HeaveTilt * e, 0f);
            }
            if (u < 0.7f)
            {
                // Straighten up with the load: back to upright, sinking into a squash.
                float t = (u - 0.45f) / 0.25f;
                float squash = Mathf.Lerp(0.03f, 0.09f, t);
                return new Pose(1f + squash * 0.6f, 1f - squash, 0f, false,
                    Mathf.Lerp(-HeaveReach, 0f, t), Mathf.Lerp(-HeaveTilt, 0f, t), 0f);
            }
            float r = (u - 0.7f) / 0.3f;
            float spring = (1f - r) * (1f - r) * 0.09f;
            return new Pose(1f + spring * 0.6f, 1f - spring, 0f, false, 0f, 0f, 0f);
        }

        /// <summary>
        /// Push: rock back onto the heels, then thrust forward at the tile in front -- the
        /// moment the stock leaves -- and return upright.
        /// </summary>
        public static Pose Lunge(float progressTicks, int durationTicks)
        {
            if (!InStep(progressTicks, durationTicks, out float u))
            {
                return Pose.Rest;
            }

            float lean;
            if (u < 0.3f)
            {
                float t = u / 0.3f;
                lean = -0.35f * t * t;
            }
            else if (u < 0.45f)
            {
                lean = Mathf.Lerp(-0.35f, 1f, (u - 0.3f) / 0.15f);
            }
            else
            {
                float t = (u - 0.45f) / 0.55f;
                lean = (1f - t) * (1f - t);
            }

            // Stretch into the thrust, a hint of squash on the rock back.
            float stretch = lean > 0f ? 0.04f * lean : 0.03f * lean;
            return new Pose(1f - stretch * 0.5f, 1f + stretch, 0f, false, lean * LungeReach, lean * LungeTilt, 0f);
        }

        /// <summary>
        /// FreightLaunch: crouch lower through the wind-up, hop off the floor at
        /// <see cref="LaunchTakeoff"/> (with a puff, as the cargo leaves), and land with a bounce.
        /// </summary>
        public static Pose Launch(float progressTicks, int durationTicks)
        {
            if (!InStep(progressTicks, durationTicks, out float u))
            {
                return Pose.Rest;
            }

            if (u < LaunchTakeoff)
            {
                float t = u / LaunchTakeoff;
                float e = t * t;
                // A quiver in the deepest part of the crouch: the spring is wound.
                float quiver = t > 0.6f ? Mathf.Sin(t * 60f) * 0.01f * (t - 0.6f) / 0.4f : 0f;
                return new Pose(1f + LaunchCrouch * 0.6f * e, 1f - LaunchCrouch * e + quiver, 0f, false);
            }

            const float air = 0.2f;
            if (u < LaunchTakeoff + air)
            {
                float t = (u - LaunchTakeoff) / air;
                float hop = 4f * t * (1f - t); // a parabola: up and down again
                float stretch = Mathf.Lerp(0.1f, 0f, t);
                return new Pose(1f - stretch * 0.5f, 1f + stretch, LaunchHop * hop, t < 0.3f);
            }

            float r = (u - LaunchTakeoff - air) / (1f - LaunchTakeoff - air);
            float land = Mathf.Sin(r * Mathf.PI) * 0.08f * (1f - r);
            return new Pose(1f + land * 0.6f, 1f - land, 0f, false);
        }

        private static Pose Stamp(float progressTicks, int durationTicks, int strokeTicks)
        {
            if (!InStep(progressTicks, durationTicks, out float progress))
            {
                return Pose.Rest;
            }

            float strokes = progress * StrokeCount(durationTicks, strokeTicks);
            float u = Mathf.Repeat(strokes, 1f);

            if (u < StampImpact)
            {
                // Wind-up: rise and stretch tall, eased in so the top of the lift lingers.
                float t = u / StampImpact;
                float e = t * t;
                return new Pose(1f - WindUpStretch * 0.6f * e, 1f + WindUpStretch * e, WindUpLift * e, false);
            }

            const float slam = 0.08f;
            if (u < StampImpact + slam)
            {
                // The slam: down from full stretch to full squash in a few frames.
                float t = (u - StampImpact) / slam;
                float sy = Mathf.Lerp(1f + WindUpStretch, 1f - ImpactSquash, t);
                float sx = Mathf.Lerp(1f - WindUpStretch * 0.6f, 1f + ImpactSquash * 0.7f, t);
                return new Pose(sx, sy, Mathf.Lerp(WindUpLift, 0f, t), false);
            }

            // Recovery: spring back from the squash, eased out, with a small overshoot.
            float r = (u - StampImpact - slam) / (1f - StampImpact - slam);
            float spring = (1f - r) * (1f - r);
            float overshoot = Mathf.Sin(r * Mathf.PI) * 0.02f;
            return new Pose(
                1f + ImpactSquash * 0.7f * spring - overshoot,
                1f - ImpactSquash * spring + overshoot,
                0f,
                r < 0.25f);
        }

        /// <summary>Progress through the step as a fraction, and false outside (0, 1).</summary>
        private static bool InStep(float progressTicks, int durationTicks, out float progress)
        {
            progress = Mathf.Clamp01(progressTicks / Mathf.Max(1, durationTicks));
            return progress > 0f && progress < 1f;
        }
    }
}
