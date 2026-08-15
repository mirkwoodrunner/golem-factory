using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using GolemFactory.Player;
using GolemFactory.World;

namespace GolemFactory.Tests.PlayMode
{
    // PlayMode because ArtificerWalkAnimator has no [ExecuteAlways]: its Awake and, more to the
    // point, its LateUpdate never run outside Play Mode -- the same gotcha PlayerControllerTests
    // and the M7 Signal-trigger tests are here for.
    public class ArtificerWalkAnimatorTests
    {
        private GameObject _root;
        private Sprite[] _frames;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }

            if (_frames != null)
            {
                foreach (Sprite s in _frames)
                {
                    if (s != null)
                    {
                        Object.DestroyImmediate(s.texture);
                        Object.DestroyImmediate(s);
                    }
                }

                _frames = null;
            }
        }

        // Sixteen throwaway sprites. They only have to be distinguishable from one another, so the
        // test can name the exact frame on screen rather than asserting "something changed".
        private Sprite[] BuildFrames()
        {
            int count = ArtificerWalkAnimation.DirectionCount * ArtificerWalkAnimation.FramesPerDirection;
            var frames = new Sprite[count];
            for (int i = 0; i < count; i++)
            {
                var tex = new Texture2D(4, 4);
                frames[i] = Sprite.Create(tex, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0f), 64f);
                frames[i].name = "frame_" + i;
            }

            return frames;
        }

        private ArtificerWalkAnimator Build(out PlayerController controller, out SpriteRenderer renderer)
        {
            _root = new GameObject("Player", typeof(SpriteRenderer));
            renderer = _root.GetComponent<SpriteRenderer>();
            controller = _root.AddComponent<PlayerController>();
            controller.Configure(null, moveSpeed: 4f);

            var animator = _root.AddComponent<ArtificerWalkAnimator>();
            _frames = BuildFrames();
            animator.Configure(_frames, controller, ArtificerWalkAnimation.DefaultStrideLength);
            return animator;
        }

        private static int IndexOf(Sprite[] frames, Sprite sprite)
        {
            for (int i = 0; i < frames.Length; i++)
            {
                if (ReferenceEquals(frames[i], sprite))
                {
                    return i;
                }
            }

            return -1;
        }

        [UnityTest]
        public IEnumerator StandingStill_ShowsTheStandingFrame()
        {
            ArtificerWalkAnimator animator = Build(out PlayerController controller, out SpriteRenderer renderer);
            yield return null;
            yield return null;

            Assert.AreEqual(
                ArtificerWalkAnimation.ComputeSpriteIndex(ArtificerFacing.Down, ArtificerWalkAnimation.StandingFrameIndex),
                IndexOf(_frames, renderer.sprite));
        }

        [UnityTest]
        public IEnumerator WalkingRight_UsesTheRightRow()
        {
            ArtificerWalkAnimator animator = Build(out PlayerController controller, out SpriteRenderer renderer);
            yield return null;

            controller.MoveBy(Vector2.right, 0.1f);
            yield return null;

            Assert.AreEqual(ArtificerFacing.Right, animator.Facing);
            Assert.GreaterOrEqual(IndexOf(_frames, renderer.sprite), 8);
            Assert.LessOrEqual(IndexOf(_frames, renderer.sprite), 11);
        }

        [UnityTest]
        public IEnumerator WalkingLeft_UsesTheLeftRow_NotAFlippedRightRow()
        {
            // The art's own constraint: the two profile rows are drawn separately, so facing left
            // must land in the left row and must never be delivered as flipX on a right-row frame.
            ArtificerWalkAnimator animator = Build(out PlayerController controller, out SpriteRenderer renderer);
            yield return null;

            controller.MoveBy(Vector2.left, 0.1f);
            yield return null;

            Assert.AreEqual(ArtificerFacing.Left, animator.Facing);
            int index = IndexOf(_frames, renderer.sprite);
            Assert.GreaterOrEqual(index, 4);
            Assert.LessOrEqual(index, 7);
            Assert.IsFalse(renderer.flipX, "left must be its own art, never a mirrored right");
        }

        [UnityTest]
        public IEnumerator WalkingUpAndDown_UseTheirOwnRows()
        {
            ArtificerWalkAnimator animator = Build(out PlayerController controller, out SpriteRenderer renderer);
            yield return null;

            controller.MoveBy(Vector2.up, 0.1f);
            yield return null;
            Assert.AreEqual(ArtificerFacing.Up, animator.Facing);
            Assert.GreaterOrEqual(IndexOf(_frames, renderer.sprite), 12);

            controller.MoveBy(Vector2.down, 0.1f);
            yield return null;
            Assert.AreEqual(ArtificerFacing.Down, animator.Facing);
            Assert.LessOrEqual(IndexOf(_frames, renderer.sprite), 3);
        }

        [UnityTest]
        public IEnumerator Walking_AdvancesThroughFrames()
        {
            ArtificerWalkAnimator animator = Build(out PlayerController controller, out SpriteRenderer renderer);
            yield return null;

            var seen = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < 12; i++)
            {
                // A third of a stride per step, so the cycle is walked rather than jumped.
                controller.MoveBy(Vector2.right, ArtificerWalkAnimation.DefaultStrideLength / 3f / 4f);
                yield return null;
                seen.Add(IndexOf(_frames, renderer.sprite));
            }

            Assert.GreaterOrEqual(seen.Count, 4, "the cycle should visit all four frames of the row");
        }

        [UnityTest]
        public IEnumerator StoppingMidStride_ReturnsToTheStandingFrame()
        {
            // The stop case the whole distance-driven design exists for: no frozen mid-stride pose.
            ArtificerWalkAnimator animator = Build(out PlayerController controller, out SpriteRenderer renderer);
            yield return null;

            controller.MoveBy(Vector2.right, 0.1f);
            controller.MoveBy(Vector2.right, 0.1f);
            yield return null;

            controller.MoveBy(Vector2.zero, 0.1f);
            yield return null;

            Assert.AreEqual(
                ArtificerWalkAnimation.ComputeSpriteIndex(ArtificerFacing.Right, ArtificerWalkAnimation.StandingFrameIndex),
                IndexOf(_frames, renderer.sprite),
                "stopping should stand, still facing the way he was walking");
        }

        [UnityTest]
        public IEnumerator BlockedByFloorBounds_DoesNotAnimate()
        {
            // Input held, displacement zero. This is the skate the design is built to prevent, and
            // it is why the frame comes from travel rather than from what the player is pressing.
            ArtificerWalkAnimator animator = Build(out PlayerController controller, out SpriteRenderer renderer);
            var converter = new GridCoordinateConverter(new Vector2(1f, 1f));
            controller.SetFloorBounds(converter, 12);
            yield return null;

            // Park him hard against the eastern bound, then keep pushing east.
            controller.transform.position = converter.CellToWorldCenter(new Vector2Int(20, 0));
            controller.MoveBy(Vector2.zero, 0f);
            yield return null;

            for (int i = 0; i < 5; i++)
            {
                controller.MoveBy(Vector2.right, 0.1f);
                yield return null;

                Assert.AreEqual(
                    ArtificerWalkAnimation.ComputeSpriteIndex(ArtificerFacing.Right, ArtificerWalkAnimation.StandingFrameIndex),
                    IndexOf(_frames, renderer.sprite),
                    "pressing into a bound must face him at it without walking him on the spot");
            }
        }

        [UnityTest]
        public IEnumerator UnwiredFrames_LeaveTheSpriteAlone()
        {
            // A half-wired array should not blank him out; the existing sprite stays.
            _root = new GameObject("Player", typeof(SpriteRenderer));
            var renderer = _root.GetComponent<SpriteRenderer>();
            var tex = new Texture2D(4, 4);
            Sprite placeholder = Sprite.Create(tex, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0f), 64f);
            renderer.sprite = placeholder;

            var controller = _root.AddComponent<PlayerController>();
            controller.Configure(null, moveSpeed: 4f);
            var animator = _root.AddComponent<ArtificerWalkAnimator>();
            animator.Configure(new Sprite[16], controller, ArtificerWalkAnimation.DefaultStrideLength);

            yield return null;
            controller.MoveBy(Vector2.right, 0.1f);
            yield return null;

            Assert.AreSame(placeholder, renderer.sprite);

            Object.DestroyImmediate(placeholder);
            Object.DestroyImmediate(tex);
        }
    }
}
