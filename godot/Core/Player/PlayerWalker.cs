using GolemFactory.World;

namespace GolemFactory.Player
{
    /// <summary>
    /// The player's per-frame walk: Unity's PlayerController.MoveBy and ArtificerWalkAnimator,
    /// extracted (milestone G4) so the Node only reads input and draws the frame this names.
    ///
    /// <para>
    /// Analog, not grid-locked (only golems are). With floor bounds set the walk is clamped to
    /// the world (workshop + street); without them it is unbounded, as Unity's Main.unity player
    /// was. The animation frame comes from DISTANCE ACTUALLY TRAVELLED, never from input: pressing
    /// into a wall faces the wall without walking on the spot, and stopping mid-stride stands.
    /// </para>
    /// </summary>
    public sealed class PlayerWalker
    {
        private GridCoordinateConverter _boundsConverter;
        private bool _bounded;
        private int _halfExtent;
        private int _streetDepth;
        private int _northExtent;
        private float _distanceTravelled;

        public PlayerWalker(float moveSpeed = 4f, float strideLength = ArtificerWalkAnimation.DefaultStrideLength)
        {
            MoveSpeed = moveSpeed;
            StrideLength = strideLength;
            SpriteIndex = ArtificerWalkAnimation.ComputeSpriteIndex(Facing, ArtificerWalkAnimation.StandingFrameIndex);
        }

        /// <summary>Cells per second, Unity's PlayerController default (4).</summary>
        public float MoveSpeed { get; set; }

        public float StrideLength { get; }

        /// <summary>World position, Unity's frame (+y north). Settable, as a transform was.</summary>
        public Compat.Vector3 Position { get; set; }

        public ArtificerFacing Facing { get; private set; } = ArtificerFacing.Down;

        /// <summary>Index into the sixteen walk frames (<see cref="ArtificerWalkAnimation.ComputeSpriteIndex"/>).</summary>
        public int SpriteIndex { get; private set; }

        public bool HasFloorBounds => _bounded;

        /// <summary>Clamp every move to the world. <paramref name="northExtent"/> follows Floor Expansion.</summary>
        public void SetFloorBounds(
            GridCoordinateConverter converter, int halfExtent,
            int streetDepth = FloorLayout.StreetDepth, int? northExtent = null)
        {
            _boundsConverter = converter;
            _bounded = true;
            _halfExtent = halfExtent;
            _streetDepth = streetDepth;
            _northExtent = northExtent ?? halfExtent;
        }

        /// <summary>Moves the north wall the clamp respects (a Floor Expansion purchase).</summary>
        public void SetNorthExtent(int northExtent) => _northExtent = northExtent;

        /// <summary>One frame: move by <paramref name="input"/>, clamp, then pick the frame.</summary>
        public void MoveBy(Compat.Vector2 input, float deltaTime)
        {
            Compat.Vector3 before = Position;
            Compat.Vector3 next = Position + PlayerMovement.ComputeDisplacement(input, MoveSpeed, deltaTime);
            if (_bounded)
            {
                next = FloorLayout.ClampToFloor(next, _boundsConverter, _halfExtent, _streetDepth, _northExtent);
            }
            Position = next;

            float moved = (next - before).magnitude;
            Facing = ArtificerWalkAnimation.ComputeFacing(input.x, input.y, Facing);
            _distanceTravelled = ArtificerWalkAnimation.AdvanceDistance(_distanceTravelled, moved, StrideLength);
            int frame = ArtificerWalkAnimation.IsWalking(moved)
                ? ArtificerWalkAnimation.ComputeFrameIndex(_distanceTravelled, StrideLength)
                : ArtificerWalkAnimation.StandingFrameIndex;
            SpriteIndex = ArtificerWalkAnimation.ComputeSpriteIndex(Facing, frame);
        }
    }
}
