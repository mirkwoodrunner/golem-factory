using Godot;
using GolemFactory.World;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// The global shader parameters every tick-synchronised shader reads (<c>res://shaders/</c>),
    /// declared in <c>project.godot</c>'s <c>[shader_globals]</c> and set here, once a frame, from
    /// Core's <see cref="TickVisuals"/>. This is the one way the clock reaches the renderer: no
    /// shader reads <c>TIME</c>, so a paused factory is a still picture and a faster clock a
    /// faster one.
    ///
    /// <para>
    /// A parameter is only sent when it has changed, so a paused game sends nothing; and the
    /// names are held as <see cref="StringName"/>s, so a frame allocates nothing.
    /// </para>
    /// </summary>
    public static class TickShaderGlobals
    {
        public const string TickPhaseName = "sim_tick_phase";
        public const string BeltPhaseName = "sim_belt_phase";
        public const string MotionName = "sim_motion";

        /// <summary>The names, for a scenario to check against the project's settings.</summary>
        public static readonly string[] Names = { TickPhaseName, BeltPhaseName, MotionName };

        private static readonly StringName TickPhaseParameter = TickPhaseName;
        private static readonly StringName BeltPhaseParameter = BeltPhaseName;
        private static readonly StringName MotionParameter = MotionName;

        /// <summary>What the shaders were last told, for a scenario.</summary>
        public static float TickPhase { get; private set; } = float.NaN;
        public static float BeltPhase { get; private set; } = float.NaN;
        public static float Motion { get; private set; } = float.NaN;

        /// <summary>How many parameters the last <see cref="Push"/> sent: none while nothing moved.</summary>
        public static int LastSent { get; private set; }

        /// <summary>Forgets what was sent, so the next <see cref="Push"/> sends everything: a new scene's first frame.</summary>
        public static void Reset()
        {
            TickPhase = BeltPhase = Motion = float.NaN;
        }

        /// <summary>Tells the shaders where the clock is. Reads the world; changes nothing in it.</summary>
        public static void Push(SandboxWorld world)
        {
            LastSent = 0;
            float motion = world.Motion.TickMotion ? 1f : 0f;
            float tick = TickVisuals.TickPhase(world.Clock);
            float belt = TickVisuals.BeltPhase(world.Clock, world.Conveyor.StepPerTick, world.Belts.SegmentLengthTicks);

            // Exact compares on purpose: "the same number as last frame" is the question.
            if (tick != TickPhase)
            {
                TickPhase = tick;
                RenderingServer.GlobalShaderParameterSet(TickPhaseParameter, tick);
                LastSent++;
            }
            if (belt != BeltPhase)
            {
                BeltPhase = belt;
                RenderingServer.GlobalShaderParameterSet(BeltPhaseParameter, belt);
                LastSent++;
            }
            if (motion != Motion)
            {
                Motion = motion;
                RenderingServer.GlobalShaderParameterSet(MotionParameter, motion);
                LastSent++;
            }
        }
    }
}
