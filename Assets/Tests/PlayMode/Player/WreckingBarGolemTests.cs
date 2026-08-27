using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.Player;
using GolemFactory.PunchCards;
using GolemFactory.World;

namespace GolemFactory.Tests.PlayMode
{
    /// <summary>
    /// The wrecking bar's golem branch. Driven through a FAKE <see cref="IGolemDismantler"/> on
    /// purpose: what the refund is worth belongs to <c>GolemDismantleTests</c>, and what this
    /// tool does belongs here. Splitting them is what stops a change to one silently rewriting
    /// the other's expectations.
    /// </summary>
    public class WreckingBarGolemTests
    {
        private GameObject _root;

        private sealed class FakeDismantler : IGolemDismantler
        {
            public bool Succeeds = true;
            public string Refusal = "nope";
            public GolemEntity LastAsked;
            public int Calls;

            public bool TryDismantleGolem(
                GolemEntity golem, out IReadOnlyList<RecipeIngredient> refunded, out string refusalReason)
            {
                Calls++;
                LastAsked = golem;
                refunded = new List<RecipeIngredient> { new RecipeIngredient(ItemType.Scrap, 12) };
                refusalReason = Succeeds ? "" : Refusal;
                return Succeeds;
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        private (BuildModeController build, FakeDismantler dismantler, GridMapHolder grid) Build()
        {
            _root = new GameObject("Root");

            var grid = new GameObject("Grid").AddComponent<GridMapHolder>();
            grid.transform.SetParent(_root.transform);

            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.SetParent(_root.transform);

            var build = new GameObject("BuildMode").AddComponent<BuildModeController>();
            build.transform.SetParent(_root.transform);
            build.Configure(camera, grid, null, new Vector2(1f, 1f));

            var dismantler = new FakeDismantler();
            build.ConfigureGolemDismantling(dismantler);

            return (build, dismantler, grid);
        }

        private GolemEntity GolemAt(Vector2Int cell)
        {
            var golem = new GameObject("Golem").AddComponent<GolemEntity>();
            golem.transform.SetParent(_root.transform);
            golem.Configure("Golem", null);
            golem.SetPlacement(cell, Facing.North);
            return golem;
        }

        [UnityTest]
        public IEnumerator DemolishClick_OnAGolemsTile_AsksTheDismantlerForThatGolem()
        {
            var (build, dismantler, _) = Build();
            var cell = new Vector2Int(3, 4);
            GolemEntity golem = GolemAt(cell);
            build.EnterDemolishMode();
            yield return null;

            build.PlaceOrRemove(cell);

            Assert.AreEqual(1, dismantler.Calls);
            Assert.AreSame(golem, dismantler.LastAsked);
        }

        [UnityTest]
        public IEnumerator DemolishClick_OnAGolemsTile_NoLongerSaysThatTileIsNotABuilding()
        {
            var (build, _, _) = Build();
            var cell = new Vector2Int(3, 4);
            GolemAt(cell);
            build.EnterDemolishMode();
            yield return null;

            build.PlaceOrRemove(cell);

            // The message this replaces was the placeholder that shipped before golems were
            // removable: the wrecking bar recognised a golem and then refused to touch it.
            StringAssert.DoesNotContain("not a building", build.LastStatusMessage);
        }

        [UnityTest]
        public IEnumerator DemolishClick_RefusedByTheDismantler_ShowsItsReasonRatherThanItsOwn()
        {
            var (build, dismantler, _) = Build();
            dismantler.Succeeds = false;
            dismantler.Refusal = "Put Golem down before dismantling it.";
            var cell = new Vector2Int(3, 4);
            GolemAt(cell);
            build.EnterDemolishMode();
            yield return null;

            build.PlaceOrRemove(cell);

            // The tool owns the cursor and the popup; the reason belongs to whoever refused.
            Assert.AreEqual("Put Golem down before dismantling it.", build.LastStatusMessage);
        }

        [UnityTest]
        public IEnumerator DemolishClick_OnEmptyGround_StillSaysNothingHere()
        {
            var (build, dismantler, _) = Build();
            GolemAt(new Vector2Int(3, 4));
            build.EnterDemolishMode();
            yield return null;

            build.PlaceOrRemove(new Vector2Int(9, 9));

            Assert.AreEqual(0, dismantler.Calls, "an empty tile must not reach the dismantler");
        }

        [UnityTest]
        public IEnumerator PlacementMode_ClickingAGolemsTile_DoesNotDismantleIt()
        {
            var (build, dismantler, _) = Build();
            var cell = new Vector2Int(3, 4);
            GolemAt(cell);
            // NOT in demolish mode: no wrecking bar, no removal. Every click is gated on a tool
            // being in hand, and dismantling is the wrecking bar's job alone.
            yield return null;

            build.PlaceOrRemove(cell);

            Assert.AreEqual(0, dismantler.Calls);
        }

        [UnityTest]
        public IEnumerator NoDismantlerWired_AGolemsTileBehavesExactlyAsItDidBefore()
        {
            var (build, _, _) = Build();
            build.ConfigureGolemDismantling(null);
            var cell = new Vector2Int(3, 4);
            GolemAt(cell);
            build.EnterDemolishMode();
            yield return null;

            build.PlaceOrRemove(cell);

            // Optional in the same additive way every other holder on this component is: a scene
            // that never wires one keeps the old behaviour rather than throwing.
            Assert.IsFalse(build.HasRemovableThing(cell));
        }

        [UnityTest]
        public IEnumerator TheGhostLightsUpOverAGolem_NotJustOverABuilding()
        {
            var (build, _, _) = Build();
            var cell = new Vector2Int(3, 4);
            GolemAt(cell);

            // A wrecking bar that stayed inert steel over a golem would say "this click does
            // nothing" about a click that now removes it.
            Assert.IsTrue(build.HasRemovableThing(cell));
            Assert.AreEqual(
                BuildGhostState.Removable,
                BuildGhostVisuals.ClassifyRemoval(build.HasRemovableThing(cell)));
            yield return null;
        }

        [UnityTest]
        public IEnumerator TryFindGolemAt_AnEmptyCell_FindsNothing()
        {
            var (build, _, _) = Build();
            GolemAt(new Vector2Int(3, 4));

            GolemEntity found;
            Assert.IsFalse(build.TryFindGolemAt(new Vector2Int(4, 4), out found));
            Assert.IsNull(found);
            yield return null;
        }
    }
}
