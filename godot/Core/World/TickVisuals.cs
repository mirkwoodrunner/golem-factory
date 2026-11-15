using System;
using GolemFactory.Simulation;

namespace GolemFactory.World
{
    /// <summary>
    /// What the shaders are told about the factory's clock (the node layer's
    /// <c>TickVisualsNode</c> hands these to the renderer as global shader parameters, once a
    /// frame). Like <see cref="FeelRules"/> and <see cref="LightingRules"/>, the numbers are
    /// Core's so a test can pin them; a shader only turns a phase into a picture.
    ///
    /// <para>
    /// Every phase is read off <see cref="SimulationClock.CurrentTick"/> and
    /// <see cref="SimulationClock.TickFraction"/>, so it stands still while the clock is paused
    /// and runs at the clock's speed. Nothing here writes to the clock, and nothing in the
    /// simulation may read a phase: <c>TickFraction</c> is presentation only.
    /// </para>
    ///
    /// <para>
    /// A phase is wrapped before it becomes a <c>float</c>: a tick count in the millions has no
    /// fraction left in single precision. Every period a shader divides the tick phase by must
    /// therefore divide <see cref="PhaseWrapTicks"/>, or its picture jumps at the wrap.
    /// </para>
    /// </summary>
    public static class TickVisuals
    {
        /// <summary>The tick phase runs 0..this and starts again.</summary>
        public const int PhaseWrapTicks = 1024;

        /// <summary>A clockwork gear's full turn, in ticks; it steps once a tick.</summary>
        public const int GearTicksPerTurn = 64;

        /// <summary>A steam glint travels <see cref="ConduitPulseCells"/> cells of hot pipe in this many ticks.</summary>
        public const int ConduitPulseTicks = 32;

        /// <summary>The spacing of the glints along a hot pipe, in cells.</summary>
        public const int ConduitPulseCells = 4;

        /// <summary>The Workbench's tape feeds one sprocket hole in this many ticks.</summary>
        public const int TapeTicksPerHole = 4;

        /// <summary>Ticks since the last wrap, with the fraction of the tick in progress: [0, <see cref="PhaseWrapTicks"/>].</summary>
        public static float TickPhase(SimulationClock clock) =>
            clock == null ? 0f : TickPhase(clock.CurrentTick, clock.TickFraction);

        public static float TickPhase(long tick, float tickFraction)
        {
            long wrapped = tick % PhaseWrapTicks;
            if (wrapped < 0)
            {
                wrapped += PhaseWrapTicks;
            }
            return wrapped + Clamp01(tickFraction);
        }

        /// <summary>
        /// How far along its cell a belt's surface has run, in [0, 1): the same travel its cargo
        /// makes (<paramref name="stepPerTick"/> of a segment <paramref name="segmentLengthTicks"/>
        /// long each tick), so the slats keep pace with the goods on them.
        /// </summary>
        public static float BeltPhase(SimulationClock clock, float stepPerTick, int segmentLengthTicks) =>
            clock == null ? 0f : BeltPhase(clock.CurrentTick, clock.TickFraction, stepPerTick, segmentLengthTicks);

        public static float BeltPhase(long tick, float tickFraction, float stepPerTick, int segmentLengthTicks)
        {
            if (segmentLengthTicks <= 0 || stepPerTick <= 0f)
            {
                return 0f;
            }
            // In doubles: the product outgrows a float's fraction long before a save does.
            double cells = (tick + (double)Clamp01(tickFraction)) * stepPerTick / segmentLengthTicks;
            double phase = cells - Math.Floor(cells);
            return phase >= 1.0 ? 0f : (float)phase;
        }

        /// <summary>
        /// The clock's rate as the shaders see it: ticks per real second, zero while paused.
        /// </summary>
        public static float TickRate(SimulationClock clock) =>
            clock == null || clock.State != ClockState.Running ? 0f : Math.Max(0f, clock.TicksPerSecond * clock.Speed);

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }

    /// <summary>
    /// The build grid's two short curves (the node layer's <c>GridOverlayNode</c> and
    /// <c>grid_overlay.gdshader</c>): how the brackets close on a cell the cursor has just snapped
    /// to, and how one placement state's colour gives way to the next. Real seconds, ending at
    /// rest, as every feel curve does (docs/game-feel-design.md §0).
    /// </summary>
    public static class GridOverlayRules
    {
        /// <summary>How far around the cursor the grid shows, in cells.</summary>
        public const float ReachCells = 4.5f;

        /// <summary>The brackets start this many pixels out from the cell and close onto it.</summary>
        public const float SnapPixels = 5f;

        public const float SnapSeconds = 0.14f;

        /// <summary>A change of placement state (valid, blocked, can't pay) blends over this long.</summary>
        public const float BlendSeconds = 0.12f;

        /// <summary>The brackets' distance out from the cell, <paramref name="age"/> seconds after the cursor reached it.</summary>
        public static float SnapOffset(float age, bool motion = true)
        {
            if (!motion || age < 0f || age >= SnapSeconds)
            {
                return 0f;
            }
            float left = 1f - age / SnapSeconds;
            return SnapPixels * left * left;
        }

        /// <summary>How much of the new state's colour shows, <paramref name="age"/> seconds after the state changed.</summary>
        public static float Blend(float age, bool motion = true)
        {
            if (!motion || age < 0f || age >= BlendSeconds)
            {
                return 1f;
            }
            float t = age / BlendSeconds;
            return t * t * (3f - 2f * t);
        }
    }
}
