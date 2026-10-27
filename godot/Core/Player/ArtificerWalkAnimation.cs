using System;

namespace GolemFactory.Player
{
    // Which of the four independently-drawn walk rows is showing.
    //
    // These are NOT interchangeable by mirroring: docs/open-items.md records that the two
    // profile rows are drawn separately (apron detail and hair differ), and a pixel diff of
    // the source frames puts left against a mirrored right at ~38% of the sprite. Left and
    // Right are distinct art, so both get their own row -- never derive one with flipX.
    //
    // Serialized nowhere today, but the values are explicit so a future save file can hold
    // one without the ordering becoming a silent trap.
    public enum ArtificerFacing
    {
        Down = 0,
        Left = 1,
        Right = 2,
        Up = 3,
    }

    // Pure frame-selection math for the Artificer's walk cycle, engine-free so it's unit-testable
    // without a scene -- same split as PlayerMovement.ComputeDisplacement and
    // Golems/GolemAnimationUtility: plain static functions here, a thin MonoBehaviour applies them.
    //
    // Frames advance on DISTANCE TRAVELLED, not wall time. That is the whole point: a wall-clock
    // cycle keeps stepping while the player is standing still or shoved up against a wall, which
    // reads as skating. Driving it from actual displacement means the feet can only move when the
    // Artificer does.
    public static class ArtificerWalkAnimation
    {
        public const int DirectionCount = 4;
        public const int FramesPerDirection = 4;

        // Shown whenever the Artificer isn't moving. Deliberately a named constant rather than
        // "hold the last walking frame", which leaves him frozen mid-stride on every stop.
        public const int StandingFrameIndex = 0;

        // World units of travel per frame step. At the authored PPU of 64 the Artificer is one
        // world unit wide, so this is a stride of just over a third of his own width -- fast
        // enough to read as walking at the 4 u/s default move speed without buzzing.
        public const float DefaultStrideLength = 0.35f;

        // Below this much movement in a frame he counts as standing. Not a tuning knob -- it only
        // has to sit above float noise in a transform subtraction and below the ~0.067 units a
        // 4 u/s walk covers in a 60fps frame, and the gap between those is four orders of
        // magnitude. Walking into a wall yields exactly zero, which is the case that matters.
        public const float MovementEpsilon = 1e-5f;

        // distanceTravelled is cumulative and only ever grows, so the cycle carries across a
        // direction change instead of snapping back to frame 0 every time the player turns.
        public static int ComputeFrameIndex(float distanceTravelled, float strideLength)
        {
            // A zero or negative stride would divide by zero or run the cycle backwards; a
            // negative distance means a caller fed us a magnitude that wasn't one. Both resolve
            // to standing rather than throwing, matching how ComputeShakeOffset treats a zero
            // duration.
            if (strideLength <= 0f || distanceTravelled <= 0f || float.IsNaN(distanceTravelled))
            {
                return StandingFrameIndex;
            }

            if (float.IsInfinity(distanceTravelled))
            {
                return StandingFrameIndex;
            }

            double steps = Math.Floor(distanceTravelled / strideLength);
            int index = (int)(steps % FramesPerDirection);
            return index < 0 ? index + FramesPerDirection : index;
        }

        // True when the Artificer covered real ground this frame, which is what separates walking
        // from leaning on a wall. Held input is deliberately NOT the test: the player can hold a
        // direction against a wall or a floor bound indefinitely, and animating that is the skate.
        public static bool IsWalking(float distanceThisFrame) =>
            distanceThisFrame > MovementEpsilon;

        // Accumulates travel, wrapped to one full cycle so the total stays small forever. A raw
        // running total works fine for an hour and then starts losing frames to float precision --
        // by a million world units the gap between representable values is a sizeable fraction of a
        // stride. Wrapping costs one modulo and makes the cycle exact for any session length.
        public static float AdvanceDistance(float distanceTravelled, float distanceThisFrame, float strideLength)
        {
            if (strideLength <= 0f)
            {
                return 0f;
            }

            if (distanceThisFrame <= 0f || float.IsNaN(distanceThisFrame) || float.IsInfinity(distanceThisFrame))
            {
                distanceThisFrame = 0f;
            }

            float cycleLength = strideLength * FramesPerDirection;
            float total = distanceTravelled + distanceThisFrame;
            if (total < 0f)
            {
                return 0f;
            }

            return total % cycleLength;
        }

        // Facing follows the last NON-ZERO movement vector: with no input we keep facing whichever
        // way we were, rather than snapping to a default direction the moment the player lets go.
        //
        // Axis dominance decides diagonals, and an exact |x| == |y| tie resolves to the horizontal
        // row on purpose -- the profile art carries far more of the Artificer's silhouette than the
        // front/back rows do, so a diagonal reads better as a side view than a back view.
        public static ArtificerFacing ComputeFacing(float moveX, float moveY, ArtificerFacing previous)
        {
            if (moveX == 0f && moveY == 0f)
            {
                return previous;
            }

            if (Math.Abs(moveX) >= Math.Abs(moveY))
            {
                return moveX >= 0f ? ArtificerFacing.Right : ArtificerFacing.Left;
            }

            return moveY >= 0f ? ArtificerFacing.Up : ArtificerFacing.Down;
        }

        // Flattens (row, frame) into an index over the 16 frames laid out Down, Left, Right, Up --
        // the order the ArtificerFacing values already declare, so the component can hold one flat
        // array built once at Awake instead of a jagged one rebuilt per frame.
        public static int ComputeSpriteIndex(ArtificerFacing facing, int frameIndex)
        {
            int row = (int)facing;
            if (row < 0 || row >= DirectionCount)
            {
                row = (int)ArtificerFacing.Down;
            }

            int frame = frameIndex % FramesPerDirection;
            if (frame < 0)
            {
                frame += FramesPerDirection;
            }

            return (row * FramesPerDirection) + frame;
        }
    }
}
