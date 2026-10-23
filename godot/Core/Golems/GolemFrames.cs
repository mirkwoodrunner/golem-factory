using System;
using GolemFactory.Compat;
using GolemFactory.PunchCards;
using GolemFactory.World;

namespace GolemFactory.Golems
{
    /// <summary>Which picture of a chassis a frame is (docs/art-style-bible.md §4.1).</summary>
    public enum GolemFrameState
    {
        /// <summary>The resting body, and the idle pulse's first frame.</summary>
        Base = 0,

        /// <summary>The idle pulse's lit frame (<c>idle_1</c>).</summary>
        Idle,

        /// <summary>The signature verb's key poses: wind-up, impact, recover (<c>work_0</c>–<c>work_2</c>).</summary>
        Work,

        /// <summary>Stalled or Starved: slumped.</summary>
        Stall,

        /// <summary>In the player's hands.</summary>
        Carry,
    }

    /// <summary>One frame of a chassis: its state, its index within the state, and its facing.</summary>
    public readonly struct GolemFrameKey : IEquatable<GolemFrameKey>
    {
        public readonly GolemFrameState State;
        public readonly int Index;
        public readonly Facing Facing;

        public GolemFrameKey(GolemFrameState state, int index, Facing facing)
        {
            State = state;
            Index = index;
            Facing = facing;
        }

        public static GolemFrameKey Base(Facing facing) => new GolemFrameKey(GolemFrameState.Base, 0, facing);

        public GolemFrameKey WithFacing(Facing facing) => new GolemFrameKey(State, Index, facing);

        public bool Equals(GolemFrameKey other) => State == other.State && Index == other.Index && Facing == other.Facing;

        public override bool Equals(object obj) => obj is GolemFrameKey other && Equals(other);

        public override int GetHashCode() => ((int)State * 16 + Index) * 4 + (int)Facing;

        public override string ToString() => $"{State} {Index} {Facing}";
    }

    /// <summary>
    /// Which picture a golem shows, and what file holds it (docs/art-style-bible.md §4.1–§4.4).
    /// Pure rules: <c>GolemNode</c> asks <see cref="FrameFor"/> each frame, resolves the key to a
    /// file through <see cref="Resolve"/> (with <c>ResourceLoader.Exists</c> as the predicate),
    /// and only sets its body's texture. Frames ride on top of the transform motion
    /// (<see cref="GolemWorkMotion"/>), never instead of it.
    ///
    /// <para>
    /// A missing frame falls back, one step at a time: the frame in its facing, then facing
    /// south, then the base in the asked facing, then the south base (the data's
    /// <c>chassisSprite</c>), then <see cref="FallbackSprite"/>. So art lands one chassis, one
    /// facing, one frame at a time, and with only the south bases (today) every lookup is the
    /// south base.
    /// </para>
    /// </summary>
    public static class GolemFrames
    {
        /// <summary>The last resort, a generic copper golem (also a golem with no chassis yet).</summary>
        public const string FallbackSprite = "golem_generic_copper.png";

        /// <summary>The idle pulse: the base for this long, wall-clock seconds...</summary>
        public const float IdleRestSeconds = 1.6f;

        /// <summary>...then the lit frame (<c>idle_1</c>) for this long.</summary>
        public const float IdleLitSeconds = 0.2f;

        /// <summary>Where a stroke's wind-up ends and its impact pose shows: <see cref="GolemWorkMotion.StampImpact"/>.</summary>
        public const float ImpactStart = GolemWorkMotion.StampImpact;

        /// <summary>Where the impact pose gives way to the recovery.</summary>
        public const float RecoverStart = 0.80f;

        /// <summary>Work frames per signature stroke: wind-up, impact, recover.</summary>
        public const int WorkFrames = 3;

        /// <summary>
        /// Each chassis's signature verb, the one motion its work frames are drawn for
        /// (art-style-bible.md §4.1): every other verb shows the base, moved by the transform
        /// motion as before. Keyed by chassis name, as <see cref="GolemWorkMotion.MotionFor"/> is.
        /// </summary>
        public static GolemWorkMotion.Motion SignatureMotion(string chassisName)
        {
            switch (chassisName)
            {
                case "ClockworkScavenger": return GolemWorkMotion.Motion.Tug;
                case "BrassPresser": return GolemWorkMotion.Motion.Stamp;
                case "MainspringOverclocker": return GolemWorkMotion.Motion.FastStamp;
                case "AetherHauler": return GolemWorkMotion.Motion.Heave;
                case "ZeppelinFreightLoader": return GolemWorkMotion.Motion.Launch;
                default: return GolemWorkMotion.Motion.None;
            }
        }

        /// <summary>
        /// The motion whose work frames play for this step: its motion when that is the chassis's
        /// signature verb, otherwise <see cref="GolemWorkMotion.Motion.None"/>.
        /// </summary>
        public static GolemWorkMotion.Motion WorkMotionFor(string chassisName, AppendageActionType action)
        {
            GolemWorkMotion.Motion motion = GolemWorkMotion.MotionFor(chassisName, action);
            return motion != GolemWorkMotion.Motion.None && motion == SignatureMotion(chassisName)
                ? motion
                : GolemWorkMotion.Motion.None;
        }

        /// <summary>
        /// How far through its current stroke a motion is, 0..1, from step progress (ticks done
        /// plus the clock's tick fraction): the repeating strokes (stamps, tugs) per stroke, the
        /// one-move verbs (heave, lunge, launch) over the whole step, as
        /// <see cref="GolemWorkMotion"/> times them. Progress, not time, so a paused clock holds it.
        /// </summary>
        public static float StrokePhase(GolemWorkMotion.Motion motion, float progressTicks, int durationTicks)
        {
            if (durationTicks <= 0 || progressTicks <= 0f)
            {
                return 0f;
            }
            float progress = Mathf.Clamp01(progressTicks / durationTicks);
            switch (motion)
            {
                case GolemWorkMotion.Motion.Stamp:
                    return Repeat(progress * GolemWorkMotion.StrokeCount(durationTicks, GolemWorkMotion.StampStrokeTicks));
                case GolemWorkMotion.Motion.FastStamp:
                    return Repeat(progress * GolemWorkMotion.StrokeCount(durationTicks, GolemWorkMotion.FastStampStrokeTicks));
                case GolemWorkMotion.Motion.Tug:
                    return Repeat(progress * GolemWorkMotion.StrokeCount(durationTicks, GolemWorkMotion.TugStrokeTicks));
                default:
                    return progress;
            }
        }

        private static float Repeat(float value)
        {
            float r = value - (float)Math.Floor(value);
            return r >= 1f ? 0f : r;
        }

        /// <summary>
        /// The frame a golem shows (art-style-bible.md §4.1). In order: carried shows the carry
        /// frame; Stalled or Starved the stall frame; a signature stroke (<paramref name="motion"/>
        /// not None) its work frame by <paramref name="strokePhase"/> (wind-up below
        /// <see cref="ImpactStart"/>, impact below <see cref="RecoverStart"/>, then recover);
        /// anything else idles: the base for <see cref="IdleRestSeconds"/>, the lit frame for
        /// <see cref="IdleLitSeconds"/>, on wall-clock <paramref name="time"/>. Each in
        /// <paramref name="facing"/>.
        /// </summary>
        public static GolemFrameKey FrameFor(GolemMood mood, GolemWorkMotion.Motion motion, float strokePhase,
            bool carried, float time, Facing facing)
        {
            if (carried)
            {
                return new GolemFrameKey(GolemFrameState.Carry, 0, facing);
            }
            if (mood == GolemMood.Stalled || mood == GolemMood.Starved)
            {
                return new GolemFrameKey(GolemFrameState.Stall, 0, facing);
            }
            if (motion != GolemWorkMotion.Motion.None)
            {
                int index = strokePhase < ImpactStart ? 0 : strokePhase < RecoverStart ? 1 : 2;
                return new GolemFrameKey(GolemFrameState.Work, index, facing);
            }
            float cycle = IdleRestSeconds + IdleLitSeconds;
            float t = time - cycle * (float)Math.Floor(time / cycle);
            return t < IdleRestSeconds
                ? GolemFrameKey.Base(facing)
                : new GolemFrameKey(GolemFrameState.Idle, 1, facing);
        }

        /// <summary>
        /// A chassis's file stem: its <c>chassisSprite</c> without ".png"; null for none.
        /// </summary>
        public static string Stem(string chassisSprite)
        {
            if (string.IsNullOrEmpty(chassisSprite))
            {
                return null;
            }
            return chassisSprite.EndsWith(".png", StringComparison.Ordinal)
                ? chassisSprite.Substring(0, chassisSprite.Length - 4)
                : chassisSprite;
        }

        /// <summary>
        /// The file of a frame (art-style-bible.md §4.3): the south base is <c>&lt;stem&gt;.png</c>,
        /// every other frame <c>&lt;stem&gt;_&lt;state&gt;[_&lt;n&gt;]_&lt;facing&gt;.png</c>, e.g.
        /// <c>chassis_brass_presser_work_1_east.png</c>.
        /// </summary>
        public static string FileName(string stem, GolemFrameKey key)
        {
            if (key.State == GolemFrameState.Base && key.Facing == Facing.South)
            {
                return stem + ".png";
            }
            return $"{stem}_{StateName(key)}_{FacingName(key.Facing)}.png";
        }

        private static string StateName(GolemFrameKey key)
        {
            switch (key.State)
            {
                case GolemFrameState.Base: return "base";
                case GolemFrameState.Idle: return "idle_" + key.Index;
                case GolemFrameState.Work: return "work_" + key.Index;
                case GolemFrameState.Stall: return "stall";
                case GolemFrameState.Carry: return "carry";
                default: throw new ArgumentOutOfRangeException(nameof(key), key.State, "no file name for this state");
            }
        }

        public static string FacingName(Facing facing)
        {
            switch (facing)
            {
                case Facing.North: return "north";
                case Facing.East: return "east";
                case Facing.South: return "south";
                case Facing.West: return "west";
                default: throw new ArgumentOutOfRangeException(nameof(facing), facing, "not a facing");
            }
        }

        /// <summary>
        /// The file to draw for <paramref name="key"/>, walking the fallback until
        /// <paramref name="exists"/> says yes (art-style-bible.md §4.4): the frame; the frame
        /// facing south; the base in the asked facing; the south base; then
        /// <see cref="FallbackSprite"/>, which is returned unasked. A null stem (no chassis) is
        /// the fallback.
        /// </summary>
        public static string Resolve(string stem, GolemFrameKey key, Func<string, bool> exists)
        {
            if (string.IsNullOrEmpty(stem))
            {
                return FallbackSprite;
            }
            GolemFrameKey[] chain =
            {
                key,
                key.WithFacing(Facing.South),
                GolemFrameKey.Base(key.Facing),
                GolemFrameKey.Base(Facing.South),
            };
            foreach (GolemFrameKey candidate in chain)
            {
                string name = FileName(stem, candidate);
                if (exists(name))
                {
                    return name;
                }
            }
            return FallbackSprite;
        }
    }
}
