using System.Collections.Generic;
using GolemFactory.Belts;
using GolemFactory.Compat;
using GolemFactory.World;

namespace GolemFactory.Buildings
{
    /// <summary>
    /// A belt: the building half of one <see cref="BeltSegment"/> that <see cref="BeltNetwork"/>
    /// created when the belt was placed. Ported from Unity's sibling component (G2b) -- the
    /// logic half only. Unity's version also owned the lane sprite, arrow and cargo visual;
    /// those are the Godot layer's now, drawn from <see cref="Segment"/>, <see cref="Facing"/>
    /// and <see cref="Shape"/>.
    ///
    /// <para>
    /// <b>A belt's PICTURE is derived from its links, never the other way round.</b>
    /// <see cref="RefreshShape"/> asks the lane graph which segments actually feed this one
    /// (<c>Segment.Outputs</c>), not the neighbours' facings, so a splitter -- which has no
    /// facing -- answers the same question the same way. <see cref="BeltShapeRules"/> owns the
    /// answer; nothing here can change a routing decision.
    /// </para>
    /// </summary>
    public sealed class PlaceableBelt : IBuildingPart
    {
        // Fixed order, so a tie (fed from two sides) resolves the same way every time.
        private static readonly Facing[] SideScanOrder =
        {
            Facing.North, Facing.East, Facing.South, Facing.West,
        };

        private readonly List<Facing> _entryScratch = new List<Facing>(4);

        public BeltSegment Segment { get; private set; }
        public Facing Facing { get; private set; } = Facing.North;
        public BeltShape Shape { get; private set; } = BeltShape.Straight;

        public IBuildingPart CloneForInstance() => new PlaceableBelt();

        /// <summary>Binds the segment BeltNetwork created for this belt's cell.</summary>
        public void BindSegment(BeltSegment segment, Facing facing)
        {
            Segment = segment;
            Facing = facing;
        }

        /// <summary>
        /// Turns a belt that is already laid, keeping its lane and its cargo. Click-and-drag
        /// uses it: a run only learns which way a belt should point once the drag reaches the
        /// NEXT cell. The caller turns the lane graph (<c>BeltNetwork.TrySetFacing</c>) first;
        /// this is only the building's own record of it.
        /// </summary>
        public void Reface(Facing facing) => Facing = facing;

        /// <summary>Re-derives <see cref="Shape"/> from which neighbours feed this belt.</summary>
        public void RefreshShape(BeltNetwork network, Vector2Int cell)
        {
            if (network == null || Segment == null)
            {
                return;
            }

            _entryScratch.Clear();
            for (int i = 0; i < SideScanOrder.Length; i++)
            {
                Facing side = SideScanOrder[i];
                if (!network.TryGetBelt(FacingUtility.TargetCell(cell, side), out PlacedBelt neighbour))
                {
                    continue;
                }

                if (Contains(neighbour.Segment.Outputs, Segment))
                {
                    _entryScratch.Add(BeltShapeRules.EntryDirectionFromSide(side));
                }
            }

            Shape = BeltShapeRules.Resolve(Facing, _entryScratch);
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
    }
}
