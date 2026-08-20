using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using GolemFactory.Buildings;
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
    /// building signals and <c>TechTreeProgressTracker.SweepBuildings</c> listed six component
    /// types, so <c>bldg.slagheap</c>, <c>bldg.freightmast</c> and <c>bldg.floorexpansion</c>
    /// could never light -- for as long as those features had shipped. §3z's claim that "nothing
    /// on it should read as planned any more, every node is a shipped feature" was true of the
    /// catalog and quietly false of the readout: a player who built a Slag Heap was told by the
    /// Ledger that they had not.
    /// </para>
    ///
    /// <para>
    /// Nothing static could have caught it -- the catalog and the sweep are two hand-written
    /// lists that never mention each other. So this stands one of every placeable in a scene,
    /// polls, and demands the whole set. PlayMode because the tracker's polling and the
    /// placeables' component wiring only behave in Play Mode.
    /// </para>
    /// </summary>
    public class BuildingSignalCoverageTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        private T AddBuilding<T>() where T : Component
        {
            var go = new GameObject(typeof(T).Name);
            go.transform.SetParent(_root.transform);
            go.AddComponent<PlaceableBuilding>();
            return go.AddComponent<T>();
        }

        [UnityTest]
        public IEnumerator EveryBuildingSignalledNodeCanActuallyBeLit()
        {
            _root = new GameObject("BuildingSignalCoverage");

            // One of everything the catalog could possibly be talking about.
            AddBuilding<HandCrankBench>();
            AddBuilding<PlaceableBoiler>();
            AddBuilding<PlaceableSteamPipe>();
            AddBuilding<PlaceableBelt>();
            AddBuilding<PlaceableDepot>();
            AddBuilding<PlaceableClockTower>();
            AddBuilding<PlaceableSlagHeap>();
            AddBuilding<PlaceableFreightMast>();
            AddBuilding<PlaceableScrapRecycler>();

            // Floor Expansion is the one with no component to count: what the player buys is rows
            // of floor, so the observable fact is that the room has grown past its starting shape.
            var boundsGo = new GameObject("FloorBounds");
            boundsGo.transform.SetParent(_root.transform);
            FloorBoundsHolder bounds = boundsGo.AddComponent<FloorBoundsHolder>();

            var trackerGo = new GameObject("Tracker");
            trackerGo.transform.SetParent(_root.transform);
            TechTreeProgressTracker tracker = trackerGo.AddComponent<TechTreeProgressTracker>();
            tracker.ConfigureFloorBounds(bounds);

            yield return null;

            bounds.Bounds.Expand(bounds.Bounds.RemainingRows > 0 ? 1 : 0);
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

        [UnityTest]
        public IEnumerator AnEmptySceneLightsNoBuildingNode()
        {
            // The other direction, so the test above cannot pass by the ledger simply saying yes
            // to everything.
            _root = new GameObject("BuildingSignalCoverage");
            var trackerGo = new GameObject("Tracker");
            trackerGo.transform.SetParent(_root.transform);
            TechTreeProgressTracker tracker = trackerGo.AddComponent<TechTreeProgressTracker>();

            yield return null;
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
