using System.Collections.Generic;
using UnityEngine;
using GolemFactory.Belts;

namespace GolemFactory.World
{
    /// <summary>One placed belt: where it is, which way it runs, and the lane backing it.</summary>
    public sealed class PlacedBelt
    {
        public PlacedBelt(Vector2Int cell, Facing facing, BeltSegment segment)
        {
            Cell = cell;
            Facing = facing;
            Segment = segment;
        }

        public Vector2Int Cell { get; }
        public Facing Facing { get; }
        public BeltSegment Segment { get; }
    }

    // The player-placeable belt layer: a plain-C# manager (owned by a thin BeltNetworkHolder,
    // per the Holder pattern) that wraps BeltSegment from the OUTSIDE. Belts/ gains no
    // reference to World/, Golems/ or the spatial layer -- this class only ever calls
    // BeltSegment's and ConveyorSystem's existing public surface.
    //
    // Each placed belt is one cell long and does three things at once:
    //   * registers a BeltSegment with the ConveyorSystem, so it is ticked and advances items;
    //   * registers a BeltSegmentEndpoint on its cell, so a golem facing it can push/pull;
    //   * auto-chains to the belt it points into, so a run of belts behaves as one lane.
    public sealed class BeltNetwork
    {
        private readonly Dictionary<Vector2Int, PlacedBelt> _belts = new Dictionary<Vector2Int, PlacedBelt>();

        private ConveyorSystem _conveyor;
        private SpatialEndpointRegistry _endpoints;
        private int _segmentLengthTicks = 4;
        private int _nextSegmentNumber = 1;

        public int Count => _belts.Count;
        public IEnumerable<PlacedBelt> Belts => _belts.Values;

        /// <summary>
        /// Wires the two systems a placed belt has to publish itself into. Both are optional:
        /// with no ConveyorSystem the lane never ticks, with no SpatialEndpointRegistry it is
        /// invisible to facing-based routing. Following the Configure(...) idiom so tests and
        /// bootstraps can wire this without Inspector state.
        /// </summary>
        public void Configure(ConveyorSystem conveyor, SpatialEndpointRegistry endpoints, int segmentLengthTicks)
        {
            _conveyor = conveyor;
            _endpoints = endpoints;
            _segmentLengthTicks = Mathf.Max(1, segmentLengthTicks);
        }

        public bool TryGetBelt(Vector2Int cell, out PlacedBelt belt) => _belts.TryGetValue(cell, out belt);

        public bool HasBelt(Vector2Int cell) => _belts.ContainsKey(cell);

        /// <summary>
        /// Lays one belt. Fails on a cell that already carries a belt rather than silently
        /// replacing it, which would strand the old segment's items.
        /// </summary>
        public bool TryPlace(Vector2Int cell, Facing facing, out PlacedBelt placed)
        {
            placed = null;
            if (_belts.ContainsKey(cell))
            {
                return false;
            }

            // Cell-stamped id, so a segment is identifiable in a stall message ("Belt(3,-2)")
            // without the player ever having authored a name. The counter only breaks ties if a
            // cell is ever reused after a removal, keeping ids unique across the session.
            string segmentId = "Belt(" + cell.x + "," + cell.y + ")#" + _nextSegmentNumber;
            _nextSegmentNumber++;

            var segment = new BeltSegment(segmentId, _segmentLengthTicks);
            placed = new PlacedBelt(cell, facing, segment);
            _belts[cell] = placed;

            if (_conveyor != null)
            {
                _conveyor.Register(segment);
            }

            if (_endpoints != null)
            {
                _endpoints.Register(cell, new BeltSegmentEndpoint(segment));
            }

            Relink();
            return true;
        }

        /// <summary>
        /// Pulls one belt back up, unlinking it cleanly. Whatever was riding that belt is lost
        /// with it -- deliberate, and the honest outcome of tearing up a conveyor.
        /// </summary>
        public bool TryRemove(Vector2Int cell)
        {
            PlacedBelt placed;
            if (!_belts.TryGetValue(cell, out placed))
            {
                return false;
            }

            _belts.Remove(cell);

            if (_conveyor != null)
            {
                _conveyor.Unregister(placed.Segment.SegmentId);
            }

            // Only clear the endpoint if it is still this belt's. Guards the case where
            // something else has since claimed the cell.
            IItemEndpoint current;
            if (_endpoints != null && _endpoints.TryGetEndpoint(cell, out current))
            {
                var beltEndpoint = current as BeltSegmentEndpoint;
                if (beltEndpoint != null && beltEndpoint.Segment == placed.Segment)
                {
                    _endpoints.Unregister(cell);
                }
            }

            // Its own outgoing link goes with it, and -- the part that actually matters --
            // Relink drops any upstream belt's Next that still pointed at the removed segment.
            // A stale Next is worse than a dead end: ConveyorSystem.Tick would keep handing
            // items to a segment that is no longer registered, so it never advances and never
            // hands on. Items would vanish into a lane that still accepts them.
            placed.Segment.Next = null;
            Relink();
            return true;
        }

        public void Clear()
        {
            // Snapshot the keys: TryRemove mutates the dictionary as it goes.
            var cells = new List<Vector2Int>(_belts.Keys);
            foreach (Vector2Int cell in cells)
            {
                TryRemove(cell);
            }
        }

        // Recomputed wholesale rather than patched incrementally, matching the "always
        // re-render from data" idiom WorkbenchController.RebuildUI and BeltSegmentVisual use.
        // A belt run is tiny and this runs only on place/remove, so the simplicity is free --
        // and it makes a stale link structurally impossible rather than a case to remember.
        private void Relink()
        {
            foreach (PlacedBelt belt in _belts.Values)
            {
                belt.Segment.Next = null;

                PlacedBelt downstream;
                Vector2Int ahead = FacingUtility.TargetCell(belt.Cell, belt.Facing);
                if (_belts.TryGetValue(ahead, out downstream) &&
                    BeltPlacementRules.ShouldLink(belt.Cell, belt.Facing, downstream.Cell, downstream.Facing))
                {
                    belt.Segment.Next = downstream.Segment;
                }
            }
        }
    }
}
