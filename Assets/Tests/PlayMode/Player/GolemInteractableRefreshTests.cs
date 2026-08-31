using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.Player;
using GolemFactory.World;

namespace GolemFactory.Tests.PlayMode
{
    // THE BUG THAT MADE CARRYING LOOK UNIMPLEMENTED, and the reason it went unnoticed for so
    // long. PlayerInteractor caches its interactables with FindObjectsByType once, in OnEnable,
    // because RefreshAffordance runs every frame and FindObjectsByType must not. Nothing ever
    // invalidated that cache -- and Sandbox.unity starts with ZERO golems, because every golem
    // in the game is built by the player. So the cached array stayed empty for the entire
    // session: [G] would not carry, [R] would not turn, and [E] would not re-open the Workbench,
    // all while the player stood on the golem's own tile. Placed depots, boilers and stations
    // were unreachable for exactly the same reason.
    //
    // PLAYMODE, NOT EDITMODE, and that is load-bearing rather than incidental: the subscription
    // lives in OnEnable, which does not run outside Play mode (see CLAUDE.md's [ExecuteAlways]
    // gotcha). An EditMode version of these tests fails against a working fix, which is exactly
    // what happened when they were first written.
    //
    // They drive the EVENT rather than calling RefreshInteractables() by hand, because calling
    // it by hand is precisely what hid the bug: the live verification of the prompt work did so
    // immediately after building a golem, and so exercised a door the game never opens.
    public class GolemInteractableRefreshTests
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

        private PlayerInteractor NewInteractor()
        {
            _root = new GameObject("Root");
            PlayerInteractor interactor = new GameObject("Interactor").AddComponent<PlayerInteractor>();
            interactor.transform.SetParent(_root.transform);
            return interactor;
        }

        // Built AFTER the interactor has taken its snapshot, as every real golem is.
        private GolemEntity NewGolemNearby(string id)
        {
            var go = new GameObject(id);
            go.transform.SetParent(_root.transform);
            go.transform.position = new Vector3(0.2f, 0f, 0f);
            GolemEntity golem = go.AddComponent<GolemEntity>();
            golem.Configure(id, null);
            golem.SetPlacement(Vector2Int.zero, Facing.North);
            return golem;
        }

        [UnityTest]
        public IEnumerator AGolemThatAppearsAfterStartupBecomesCarryable()
        {
            PlayerInteractor interactor = NewInteractor();
            yield return null; // OnEnable: snapshot taken, subscription made.

            GolemEntity golem = NewGolemNearby("G1");

            Assert.IsFalse(
                interactor.TryPickUpNearestGolem(),
                "precondition: the startup snapshot cannot know about it yet");

            EventBus.Publish(new WorldInteractablesChangedEvent("test"));

            Assert.IsTrue(
                interactor.TryPickUpNearestGolem(),
                "a golem built during play must be carryable without reloading the scene");
            Assert.AreSame(golem, interactor.CarriedGolem);
        }

        [UnityTest]
        public IEnumerator AGolemThatAppearsAfterStartupBecomesRotatableAndProgrammable()
        {
            PlayerInteractor interactor = NewInteractor();
            yield return null;

            GolemEntity golem = NewGolemNearby("G1");
            EventBus.Publish(new WorldInteractablesChangedEvent("test"));

            Assert.IsTrue(interactor.RotateNearestGolem(), "[R] was refusing for the same reason [G] was");
            Assert.AreEqual(Facing.East, golem.Facing);

            interactor.RefreshAffordance();
            Assert.AreEqual(
                InteractionKind.Program, interactor.CurrentPick.Kind,
                "it must also be reachable by the [E] pick, or it can never be re-programmed");
            StringAssert.Contains("[G]", interactor.CurrentPrompt);
        }

        [UnityTest]
        public IEnumerator ADisabledInteractorStopsListeningAndRescansOnReEnable()
        {
            // The bus is STATIC, so an interactor that failed to unsubscribe would keep
            // re-scanning on behalf of a scene that is gone.
            PlayerInteractor interactor = NewInteractor();
            yield return null;

            interactor.enabled = false;
            NewGolemNearby("G1");
            EventBus.Publish(new WorldInteractablesChangedEvent("test"));

            Assert.IsFalse(
                interactor.TryPickUpNearestGolem(),
                "a disabled interactor must not still be handling world-changed events");

            interactor.enabled = true; // OnEnable re-scans.
            Assert.IsTrue(
                interactor.TryPickUpNearestGolem(),
                "re-enabling must pick up everything that appeared while it was off");
        }
    }
}
