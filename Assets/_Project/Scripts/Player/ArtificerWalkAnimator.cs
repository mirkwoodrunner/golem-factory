using UnityEngine;

namespace GolemFactory.Player
{
    // The thin half of the walk cycle: reads how far the Artificer actually moved, asks
    // ArtificerWalkAnimation which frame that is, and assigns it. All the arithmetic lives in that
    // pure class -- same split as YSortSpriteRenderer over YSortUtility, and GolemVisual over
    // GolemAnimationUtility.
    //
    // LateUpdate, not Update, because PlayerController moves the transform in Update and this has
    // to read the position it landed on. It shares that with YSortSpriteRenderer; the two touch
    // different properties of the SpriteRenderer, so their relative order does not matter.
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class ArtificerWalkAnimator : MonoBehaviour
    {
        // Sixteen frames, laid out Down, Left, Right, Up -- the order ArtificerFacing declares, and
        // the order ArtificerWalkAnimation.ComputeSpriteIndex flattens to. One flat array rather
        // than four, so the per-frame path is a single index and never allocates.
        [SerializeField] private Sprite[] _frames = new Sprite[
            ArtificerWalkAnimation.DirectionCount * ArtificerWalkAnimation.FramesPerDirection];

        [SerializeField] private float _strideLength = ArtificerWalkAnimation.DefaultStrideLength;

        // Supplies the player's INTENT. Facing comes from what they are pressing, while the frame
        // comes from what actually happened -- so walking into a wall turns him to face it and
        // then leaves his legs still, instead of either ignoring the input or skating on the spot.
        [SerializeField] private PlayerController _playerController;

        private SpriteRenderer _spriteRenderer;
        private Vector3 _lastPosition;
        private float _distanceTravelled;
        private ArtificerFacing _facing = ArtificerFacing.Down;

        public ArtificerFacing Facing => _facing;
        public float DistanceTravelled => _distanceTravelled;

        // Mirrors PlayerController.Configure: lets tests and bootstrap wire this without the
        // Inspector. Note the Editor authoring pass cannot use it -- a private [SerializeField]
        // only reaches disk through SerializedObject.
        public void Configure(Sprite[] frames, PlayerController playerController, float strideLength)
        {
            _frames = frames;
            _playerController = playerController;
            _strideLength = strideLength;
        }

        private void Awake()
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
            if (_playerController == null)
            {
                _playerController = GetComponent<PlayerController>();
            }

            _lastPosition = transform.position;
        }

        private void OnEnable()
        {
            // Re-baselined on enable so a teleport or a respawn while disabled doesn't arrive as
            // one enormous frame of "travel" and spin the cycle.
            _lastPosition = transform.position;
        }

        private void LateUpdate()
        {
            if (_spriteRenderer == null || !HasFrames())
            {
                return;
            }

            Vector3 position = transform.position;
            float distanceThisFrame = new Vector2(position.x - _lastPosition.x, position.y - _lastPosition.y).magnitude;
            _lastPosition = position;

            _distanceTravelled = ArtificerWalkAnimation.AdvanceDistance(
                _distanceTravelled, distanceThisFrame, _strideLength);

            if (_playerController != null)
            {
                Vector2 intent = _playerController.LastMoveInput;
                _facing = ArtificerWalkAnimation.ComputeFacing(intent.x, intent.y, _facing);
            }

            int frame = ArtificerWalkAnimation.IsWalking(distanceThisFrame)
                ? ArtificerWalkAnimation.ComputeFrameIndex(_distanceTravelled, _strideLength)
                : ArtificerWalkAnimation.StandingFrameIndex;

            Sprite next = _frames[ArtificerWalkAnimation.ComputeSpriteIndex(_facing, frame)];
            if (next != null && !ReferenceEquals(next, _spriteRenderer.sprite))
            {
                _spriteRenderer.sprite = next;
            }
        }

        // A half-wired array would otherwise blank the Artificer out on whichever frame is missing,
        // which reads as a flicker rather than as the wiring mistake it is.
        private bool HasFrames()
        {
            if (_frames == null
                || _frames.Length != ArtificerWalkAnimation.DirectionCount * ArtificerWalkAnimation.FramesPerDirection)
            {
                return false;
            }

            for (int i = 0; i < _frames.Length; i++)
            {
                if (_frames[i] == null)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
