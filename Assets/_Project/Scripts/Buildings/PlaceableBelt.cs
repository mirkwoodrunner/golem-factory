using System.Collections.Generic;
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

        // The lane's own renderer, and it is deliberately a CHILD rather than this object's own.
        //
        // A lane sprite has to be rotated to point along the belt, and rotating the root would
        // take the cargo with it: BeltSegmentVisual sets each item slot's world POSITION but
        // never its rotation, so every crate on a north-running belt would ride on its side. The
        // direction arrow and the two lane markers would double-rotate for the same reason. One
        // child renderer turns, everything else stays upright, and no other component has to
        // know the lane turns at all.
        [SerializeField] private SpriteRenderer laneRenderer;

        // The three pictures a belt tile can be, all authored running EAST -- straight, and the
        // two corners named for which way the item turns. Left null on a prefab that has not
        // been re-authored, in which case the lane keeps whatever sprite it has and only the
        // rotation applies; that is exactly the straight-only belt the game drew before.
        [SerializeField] private Sprite straightSprite;
        [SerializeField] private Sprite cornerLeftSprite;
        [SerializeField] private Sprite cornerRightSprite;

        // Cargo sprites live on the prefab because they are plain assets. The ConveyorSystem
        // holder cannot: it is a scene object, and a prefab field pointing into a scene (or
        // another prefab) resolves to null on instantiation -- the cross-prefab-reference trap
        // in the architecture notes. It is therefore passed in at placement time instead.
        [SerializeField] private BeltSegmentVisual.ItemSpriteBinding[] itemSprites;

        // Shown for a good with no binding above. WITHOUT IT AN UNBOUND GOOD IS INVISIBLE, not
        // merely unrecognisable: BeltSegmentVisual.ResolveSprite ends in `return itemSprite`, so
        // a miss hands the renderer null and the item rides the belt as empty space. That is
        // exactly how twenty-one of the twenty-four goods went unnoticed. Deliberately the ghost
        // placeholder rather than a real good's icon -- a wrong-but-visible marker gets found in
        // ten seconds, where borrowing Scrap's icon would just relabel the bug.
        [SerializeField] private Sprite fallbackItemSprite;
        [SerializeField] private Material itemMaterial;

        private BeltSegment _segment;
        private Facing _facing = Facing.North;
        private BeltSegmentVisual _cargoVisual;

        /// <summary>The lane this tile renders, or null before BeltNetwork has assigned one.</summary>
        public BeltSegment Segment => _segment;

        public Facing Facing => _facing;

        /// <summary>The piece currently drawn. Exposed so a test can read the picture.</summary>
        public BeltShape Shape { get; private set; } = BeltShape.Straight;

        /// <summary>
        /// Told to this tile once BeltNetwork has actually registered the lane. Separate from
        /// placement so a belt that failed to register never renders as though it succeeded.
        /// </summary>
        public void BindSegment(
            BeltSegment segment, Facing facing, ConveyorSystemHolder conveyorHolder, Vector2 cellSize)
        {
            _segment = segment;
            _facing = facing;
            _cellSize = cellSize;
            ApplyFacingVisual(cellSize);
            BuildCargoVisual(conveyorHolder, cellSize);
        }

        /// <summary>
        /// Re-points an already-placed belt without disturbing its lane. Used by a click-and-drag
        /// run, which only learns which way a belt should face once the drag reaches the NEXT
        /// cell -- so the belt under the cursor is laid, and then turned when the run goes on.
        ///
        /// <para>
        /// The <see cref="BeltSegment"/> is untouched on purpose: re-laying the belt to turn it
        /// would mint a new segment id and drop whatever was riding it, which during a drag is
        /// nothing but after one is the player's goods.
        /// </para>
        /// </summary>
        public void Reface(Facing facing)
        {
            _facing = facing;
            ApplyFacingVisual(_cellSize);
            if (_cargoVisual != null)
            {
                PositionLaneMarkers(_cellSize);
                _cargoVisual.RelocateLane();
            }
        }

        private Vector2 _cellSize = FacingVisuals.DefaultCellSize;
        private Transform _laneStart;
        private Transform _laneEnd;

        private void Awake() => ApplyFacingVisual(_cellSize);

        // Rotates the lane and its arrow to point along the belt. The rotation is in *screen*
        // space, and it still goes through FacingVisuals rather than hardcoding the 90-degree
        // steps that top-down happens to make correct. Under isometric those two answers
        // disagreed (north rendered up-and-left) and an arrow that assumed otherwise contradicted
        // the belt it sat on; asking the projection is what makes this file right under either.
        private void ApplyFacingVisual(Vector2 cellSize)
        {
            var rotation = Quaternion.Euler(0f, 0f, FacingVisuals.ScreenAngleDegrees(_facing, cellSize));
            if (directionArrow != null)
            {
                directionArrow.transform.localRotation = rotation;
            }

            if (laneRenderer != null)
            {
                laneRenderer.transform.localRotation = rotation;
            }
        }

        // --- Corner pieces ------------------------------------------------------------------

        /// <summary>
        /// Repoints this tile's lane sprite at whatever is currently feeding it: straight if it
        /// is entered from behind, from nowhere, or from more than one place, and the matching
        /// corner if exactly one neighbour turns into it.
        ///
        /// <para>
        /// <b>The shape is read off the LINKS, never the other way round.</b> It asks the
        /// segments which of them actually hand items to this one, so a corner can only ever be
        /// drawn where cargo genuinely turns -- the picture cannot drift from the routing,
        /// because the routing is its only input. <c>BeltPlacementRules</c> stays the sole
        /// authority on what links; <c>BeltShapeRules</c> only names the resulting picture.
        /// </para>
        /// </summary>
        public void RefreshShape(BeltNetworkHolder networkHolder)
        {
            if (laneRenderer == null || networkHolder == null || _segment == null)
            {
                return;
            }

            Vector2Int cell = GetComponent<PlaceableBuilding>().Cell;
            _entryScratch.Clear();
            BeltNetwork network = networkHolder.Network;
            for (int i = 0; i < SideScanOrder.Length; i++)
            {
                Facing side = SideScanOrder[i];
                PlacedBelt neighbour;
                if (!network.TryGetBelt(FacingUtility.TargetCell(cell, side), out neighbour))
                {
                    continue;
                }

                // "Does this neighbour hand items to me" -- asked of the lane graph itself
                // rather than re-derived from facings, which is what keeps a splitter (no facing
                // of its own) and a plain belt answering the same question the same way.
                if (Contains(neighbour.Segment.Outputs, _segment))
                {
                    _entryScratch.Add(BeltShapeRules.EntryDirectionFromSide(side));
                }
            }

            Shape = BeltShapeRules.Resolve(_facing, _entryScratch);
            Sprite piece = SpriteFor(Shape);
            if (piece != null)
            {
                laneRenderer.sprite = piece;
            }
        }

        private Sprite SpriteFor(BeltShape shape)
        {
            switch (shape)
            {
                case BeltShape.CornerLeft:
                    return cornerLeftSprite;
                case BeltShape.CornerRight:
                    return cornerRightSprite;
                default:
                    return straightSprite;
            }
        }

        private static bool Contains(IReadOnlyList<BeltSegment> segments, BeltSegment wanted)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                if (ReferenceEquals(segments[i], wanted))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Test/bootstrap wiring for the lane renderer and its three pieces, matching the
        /// project's <c>Configure(...)</c> idiom rather than requiring Inspector-authored state.
        /// </summary>
        public void ConfigurePieces(
            SpriteRenderer lane, Sprite straight, Sprite cornerLeft, Sprite cornerRight)
        {
            laneRenderer = lane;
            straightSprite = straight;
            cornerLeftSprite = cornerLeft;
            cornerRightSprite = cornerRight;
        }

        private static readonly Facing[] SideScanOrder =
        {
            Facing.North, Facing.East, Facing.South, Facing.West,
        };

        private readonly List<Facing> _entryScratch = new List<Facing>(4);

        // The live roster and the sweep over it, for the same reason PlaceableSteamPipe has one:
        // laying a belt changes the picture on up to five cells, and this project has no
        // cell-to-component lookup to find the other four with.
        private static readonly List<PlaceableBelt> Live = new List<PlaceableBelt>();

        private void OnEnable() => Live.Add(this);

        private void OnDisable() => Live.Remove(this);

        /// <summary>Repoints every live belt. A no-op with no network wired.</summary>
        public static void RefreshAllShapes(BeltNetworkHolder networkHolder)
        {
            if (networkHolder == null)
            {
                return;
            }

            for (int i = 0; i < Live.Count; i++)
            {
                if (Live[i] != null)
                {
                    Live[i].RefreshShape(networkHolder);
                }
            }
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

            var startGo = new GameObject("LaneStart");
            startGo.transform.SetParent(transform, false);
            _laneStart = startGo.transform;

            var endGo = new GameObject("LaneEnd");
            endGo.transform.SetParent(transform, false);
            _laneEnd = endGo.transform;
            PositionLaneMarkers(cellSize);

            var visualGo = new GameObject("CargoVisual");
            visualGo.transform.SetParent(transform, false);
            _cargoVisual = visualGo.AddComponent<BeltSegmentVisual>();
            _cargoVisual.ConfigureCargoOnly(
                conveyorHolder, _segment.SegmentId, _laneStart, _laneEnd,
                itemSprites, itemMaterial, fallbackItemSprite);

            // The one cue this belt already draws, lent to the cargo visual as its jam lamp.
            // Without it a placed belt has no flow readout at all: the scrolling-arrow channel
            // that carries "backed up" on a long lane does not exist on a single tile, so a
            // jammed belt was distinguishable from a working one only by staring at the cargo.
            _cargoVisual.ConfigureFlowSignalTarget(directionArrow);
        }

        // The lane runs across the cell, entering at the back edge and leaving at the front, so
        // an item's travel visually matches the tile-to-tile handoff it represents. Cargo keeps
        // to that straight line even on a corner piece: an item cutting the bend is a far
        // smaller lie than an item that arrives at the wrong edge, and BeltSegment has one
        // Progress per item, not a path.
        private void PositionLaneMarkers(Vector2 cellSize)
        {
            if (_laneStart == null || _laneEnd == null)
            {
                return;
            }

            Vector2 direction = FacingVisuals.ScreenDirection(_facing, cellSize);
            var offset = new Vector3(direction.x * 0.5f, direction.y * 0.5f, 0f);
            _laneStart.localPosition = -offset;
            _laneEnd.localPosition = offset;
        }
    }
}
