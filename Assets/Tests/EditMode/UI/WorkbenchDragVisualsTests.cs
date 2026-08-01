using NUnit.Framework;
using UnityEngine;
using GolemFactory.UI;

namespace GolemFactory.Tests.EditMode
{
    // The grab-offset invariant. WorkbenchCard.OnDrag used to assign
    // `_rect.position = eventData.position` straight, and cards have a 0.5/0.5 pivot, so
    // grabbing a card by its edge teleported its centre under the cursor on the very
    // first drag frame.
    public class WorkbenchDragVisualsTests
    {
        [Test]
        public void ComputeDraggedPosition_WithCapturedOffset_KeepsTheCardStillOnTheFirstFrame()
        {
            var cardPosition = new Vector2(400f, 300f);
            // Grabbed near the card's left edge, 120px left and 15px below its centre.
            var pointerAtGrab = new Vector2(280f, 285f);

            Vector2 offset = WorkbenchDragVisuals.ComputeGrabOffset(cardPosition, pointerAtGrab);
            Vector2 firstFrame = WorkbenchDragVisuals.ComputeDraggedPosition(pointerAtGrab, offset);

            // Pressing without moving must not move the card at all.
            Assert.AreEqual(cardPosition.x, firstFrame.x, 0.0001f);
            Assert.AreEqual(cardPosition.y, firstFrame.y, 0.0001f);
        }

        [Test]
        public void ComputeDraggedPosition_PointerMoves_CardFollowsByTheSameDelta()
        {
            var cardPosition = new Vector2(400f, 300f);
            var pointerAtGrab = new Vector2(280f, 285f);
            Vector2 offset = WorkbenchDragVisuals.ComputeGrabOffset(cardPosition, pointerAtGrab);

            var pointerNow = new Vector2(280f + 60f, 285f - 25f);
            Vector2 moved = WorkbenchDragVisuals.ComputeDraggedPosition(pointerNow, offset);

            Assert.AreEqual(cardPosition.x + 60f, moved.x, 0.0001f);
            Assert.AreEqual(cardPosition.y - 25f, moved.y, 0.0001f);
        }

        [Test]
        public void ComputeGrabOffset_GrabbedExactlyAtCentre_IsZero()
        {
            Vector2 offset = WorkbenchDragVisuals.ComputeGrabOffset(new Vector2(10f, 20f), new Vector2(10f, 20f));

            Assert.AreEqual(0f, offset.x, 0.0001f);
            Assert.AreEqual(0f, offset.y, 0.0001f);
        }

        [Test]
        public void LiftConstants_AreVisiblyDifferentFromARestingCard()
        {
            // A dragged card that looks identical to a resting one reads as a glitch, so
            // guard against these silently regressing back to no-ops.
            Assert.Greater(WorkbenchDragVisuals.LiftScale, 1f);
            Assert.AreNotEqual(0f, WorkbenchDragVisuals.TiltDegrees);
            Assert.Greater(WorkbenchDragVisuals.ShadowOffset, 0f);
            Assert.Less(WorkbenchDragVisuals.GhostAlpha, 1f);
            Assert.Greater(WorkbenchDragVisuals.GhostAlpha, 0f);
        }

        [Test]
        public void SocketTints_ValidAndInvalidAreDistinguishable()
        {
            Color valid = WorkbenchDragVisuals.ValidSocketTint;
            Color invalid = WorkbenchDragVisuals.InvalidSocketTint;

            float distance = Mathf.Abs(valid.r - invalid.r) + Mathf.Abs(valid.g - invalid.g) + Mathf.Abs(valid.b - invalid.b);
            Assert.Greater(distance, 0.5f, "valid/invalid socket tints must not be a near-invisible colour shift");
        }
    }
}
