using UnityEngine;

namespace GolemFactory.UI
{
    // Presentation constants and pointer math for a card being dragged across the
    // Workbench. Pure and scene-free (Vector2 only, no components) so the grab-offset
    // invariant is unit-testable, same idiom as GridCoordinateConverter.
    public static class WorkbenchDragVisuals
    {
        // A lifted card is bigger, tilted, and casts a shadow so it reads as "in hand"
        // rather than as a card that has glitched out of the list.
        public const float LiftScale = 1.06f;
        public const float TiltDegrees = -4f;
        public const float ShadowOffset = 6f;

        // The placeholder left behind in the source list so the vault doesn't reflow
        // upward the instant a drag starts.
        public const float GhostAlpha = 0.22f;

        // Socket tinting while a card is held: valid sockets warm up, the sockets that
        // would reject this card (e.g. the logic-core socket while an appendage is held)
        // visibly dim, and everything returns to its authored color on drop.
        public static readonly Color ValidSocketTint = new Color(0.42f, 0.86f, 0.55f, 1f);
        public static readonly Color InvalidSocketTint = new Color(0.30f, 0.24f, 0.22f, 1f);

        // Captured once at OnBeginDrag. Preserving it is what stops a card grabbed by its
        // edge from snapping its center to the cursor -- the card's 0.5/0.5 pivot meant
        // the old `_rect.position = eventData.position` always re-centered it under the
        // pointer on the first drag frame.
        public static Vector2 ComputeGrabOffset(Vector2 cardPosition, Vector2 pointerPosition) =>
            cardPosition - pointerPosition;

        public static Vector2 ComputeDraggedPosition(Vector2 pointerPosition, Vector2 grabOffset) =>
            pointerPosition + grabOffset;
    }
}
