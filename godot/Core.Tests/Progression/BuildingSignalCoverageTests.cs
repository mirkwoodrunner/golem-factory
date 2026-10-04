using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Buildings;
using GolemFactory.Golems;
using GolemFactory.Progression;
using GolemFactory.World;

namespace GolemFactory.Tests.PlayMode
{
    /// <summary>
    /// Every node on the Artificer's Ledger that is signalled by a BUILDING must have something
    /// that actually records it.
    ///
    /// <para>
    /// <b>This suite exists because three of them did not.</b> <c>TechTreeCatalog</c> named nine
    /// building signals and <c>TechTreeProgressTracker.SweepBuildings</c> listed six kinds, so
    /// <c>bldg.slagheap</c>, <c>bldg.freightmast</c> and <c>bldg.floorexpansion</c> could never
    /// light -- for as long as those features had shipped. A player who built a Slag Heap was
    /// told by the Ledger that they had not.
    /// </para>
    ///
    /// <para>
    /// Nothing static could have caught it -- the catalog and the sweep are two hand-written
    /// lists that never mention each other. So this stands one of every placeable in the world,
    /// polls, and demands the whole set.
    /// </para>
    ///
    /// <para>
    /// Ported from Unity's PlayMode suite: each placeable is a building with that part (Unity:
    /// a GameObject with PlaceableBuilding plus the component), handed to the tracker as its
    /// world. Unity's OnEnable frame is Attach().
    /// </para>
    /// </summary>
    public class BuildingSignalCoverageTests
    {
        private readonly List<PlaceableBuilding> _buildings = new List<PlaceableBuilding>();
        private readonly List<TechTreeProgressTracker> _trackers = new List<TechTreeProgressTracker>();

        [TearDown]
        public void TearDown()
        {
            foreach (TechTreeProgressTracker tracker in _trackers)
            {
                tracker.Detach();
            }
            _trackers.Clear();
            _buildings.Clear();
        }

        private T AddBuilding<T>(T part) where T : IBuildingPart
        {
            var building = new PlaceableBuilding { name = typeof(T).Name };
            building.AddPart(part);
            _buildings.Add(building);
            return part;
        }

        private TechTreeProgressTracker NewTracker()
        {
            var tracker = new TechTreeProgressTracker();
            tracker.ConfigureWorld(() => new GolemEntity[0], () => _buildings);
            _trackers.Add(tracker);
            return tracker;
        }

        [Test]
        public void EveryBuildingSignalledNodeCanActuallyBeLit()
        {
            // One of everything the catalog could possibly be talking about.
            AddBuilding(new HandCrankBench());
            AddBuilding(new PlaceableBoiler());
            AddBuilding(new PlaceableSteamPipe());
            AddBuilding(new PlaceableBelt());
            AddBuilding(new PlaceableDepot());
            AddBuilding(new PlaceableClockTower());
            AddBuilding(new PlaceableSlagHeap());
            AddBuilding(new PlaceableFreightMast());
            AddBuilding(new PlaceableScrapRecycler());

            // Floor Expansion is the one with no part to count: what the player buys is rows of
            // floor, so the observable fact is that the room has grown past its starting shape.
            var bounds = new FloorBounds(FloorLayout.HalfExtent, FloorLayout.DefaultNorthExtent);

            TechTreeProgressTracker tracker = NewTracker();
            tracker.ConfigureFloorBounds(bounds);

            tracker.Attach();

            bounds.Expand(bounds.RemainingRows > 0 ? 1 : 0);
            tracker.Poll();

            var missing = new List<string>();
            foreach (TechTreeNode node in TechTreeCatalog.Nodes)
            {
                if (node.Signal != TechTreeUnlockSignal.Building)
                {
                    continue;
                }

                if (!tracker.Ledger.HasBuilding(node.SignalId))
                {
                    missing.Add(node.Id + " (signal \"" + node.SignalId + "\")");
                }
            }

            CollectionAssert.IsEmpty(
                missing,
                "these Ledger nodes name a building signal nothing records, so they can never " +
                "light: " + string.Join(", ", missing));
        }

        [Test]
        public void AnEmptySceneLightsNoBuildingNode()
        {
            // The other direction, so the test above cannot pass by the ledger simply saying yes
            // to everything.
            TechTreeProgressTracker tracker = NewTracker();

            tracker.Attach();
            tracker.Poll();

            foreach (TechTreeNode node in TechTreeCatalog.Nodes)
            {
                if (node.Signal == TechTreeUnlockSignal.Building)
                {
                    Assert.IsFalse(
                        tracker.Ledger.HasBuilding(node.SignalId),
                        node.Id + " lit with nothing built");
                }
            }
        }
    }
}
