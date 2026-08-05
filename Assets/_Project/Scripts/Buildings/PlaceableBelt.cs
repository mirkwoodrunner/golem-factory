using UnityEngine;
using GolemFactory.Belts;
using GolemFactory.World;

namespace GolemFactory.Buildings
{
    // Marker + visual for one placed belt tile. Sibling component alongside PlaceableBuilding
    // (which is sealed, so this cannot subclass it) -- exactly the arrangement
    // GolemConstructionStation uses.
    //
    // This does NOT own the simulation side. BuildModeController hands the cell/facing to
    // BeltNetwork, which creates and registers the BeltSegment; this component is told the
    // result afterwards so it can render it. Keeping registration in one place is what stops a
    // belt existing visually but not logically (or the reverse).
    [RequireComponent(typeof(PlaceableBuilding))]
    public sealed class PlaceableBelt : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer directionArrow;

        // Cargo sprites live on the prefab because they are plain assets. The ConveyorSystem
        // holder cannot: it is a scene object, and a prefab field pointing into a scene (or
        // another prefab) resolves to null on instantiation -- the cross-prefab-reference trap
        // in the architecture notes. It is therefore passed in at placement time instead.
        [SerializeField] private BeltSegmentVisual.ItemSpriteBinding[] itemSprites;
        [SerializeField] private Material itemMaterial;

        private BeltSegment _segment;
        private Facing _facing = Facing.North;
        private BeltSegmentVisual _cargoVisual;

        /// <summary>The lane this tile renders, or null before BeltNetwork has assigned one.</summary>
        public BeltSegment Segment => _segment;

        public Facing Facing => _facing;

        /// <summary>
        /// Told to this tile once BeltNetwork has actually registered the lane. Separate from
        /// placement so a belt that failed to register never renders as though it succeeded.
        /// </summary>
        public void BindSegment(
            BeltSegment segment, Facing facing, ConveyorSystemHolder conveyorHolder, Vector2 cellSize)
        {
            _segment = segment;
            _facing = facing;
            ApplyFacingVisual(cellSize);
            BuildCargoVisual(conveyorHolder, cellSize);
        }

        private void Awake() => ApplyFacingVisual(FacingVisuals.DefaultCellSize);

        // Rotates the arrow to point along the lane. The rotation is in *screen* space, so it
        // uses the isometric angles from FacingVisuals rather than the grid's own 90-degree
        // steps -- north on this grid renders as up-and-right of centre, and an arrow that
        // ignored that would contradict the belt it sits on.
        private void ApplyFacingVisual(Vector2 cellSize)
        {
            if (directionArrow == null)
            {
                return;
            }

            directionArrow.transform.localRotation =
                Quaternion.Euler(0f, 0f, FacingVisuals.ScreenAngleDegrees(_facing, cellSize));
        }

        // Items riding a belt have no GameObject of their own (see the architecture note), so
        // without a BeltSegmentVisual a working belt is completely invisible -- which is the
        // readability failure this whole pass exists to fix.
        //
        // Only the CARGO half of BeltSegmentVisual is used: laneSprite/arrowSprite/rollerSprite
        // are deliberately left null (each is independently null-guarded in that class) because
        // this belt already draws its own one-cell plate and its own static direction arrow.
        // Letting it also stretch a lane strip and scroll its own arrows over a single tile
        // would draw three overlapping direction cues on one cell.
        private void BuildCargoVisual(ConveyorSystemHolder conveyorHolder, Vector2 cellSize)
        {
            if (_cargoVisual != null || conveyorHolder == null || _segment == null)
            {
                return;
            }

            // The lane runs across the cell, entering at the back edge and leaving at the front,
            // so an item's travel visually matches the tile-to-tile handoff it represents.
            Vector2 direction = FacingVisuals.ScreenDirection(_facing, cellSize);
            var offset = new Vector3(direction.x * 0.5f, direction.y * 0.5f, 0f);

            var startGo = new GameObject("LaneStart");
            startGo.transform.SetParent(transform, false);
            startGo.transform.localPosition = -offset;

            var endGo = new GameObject("LaneEnd");
            endGo.transform.SetParent(transform, false);
            endGo.transform.localPosition = offset;

            var visualGo = new GameObject("CargoVisual");
            visualGo.transform.SetParent(transform, false);
            _cargoVisual = visualGo.AddComponent<BeltSegmentVisual>();
            _cargoVisual.ConfigureCargoOnly(
                conveyorHolder, _segment.SegmentId, startGo.transform, endGo.transform,
                itemSprites, itemMaterial);
        }
    }
}
