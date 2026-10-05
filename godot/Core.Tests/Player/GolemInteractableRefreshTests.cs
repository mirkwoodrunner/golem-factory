using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Buildings;
using GolemFactory.Compat;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.Player;
using GolemFactory.World;

namespace GolemFactory.Tests.PlayMode
{
    // THE BUG THAT MADE CARRYING LOOK UNIMPLEMENTED, and the reason it went unnoticed for so
    // long. PlayerInteractor caches its interactables once, because RefreshAffordance runs every
    // frame and a world scan must not. Nothing ever invalidated that cache -- and Sandbox starts
    // with ZERO golems, because every golem in the game is built by the player. So the cached
    // array stayed empty for the entire session: [G] would not carry, [R] would not turn, and [E]
    // would not re-open the Workbench, all while the player stood on the golem's own tile.
    //
    // They drive the EVENT rather than calling RefreshInteractables() by hand, because calling it
    // by hand is precisely what hid the bug.
    //
    // Ported from Unity's PlayMode suite, which needed Play mode only because the subscription
    // lived in OnEnable. It is Attach()/Detach() now: Unity's first `yield return null` (OnEnable
    // ran) is Attach(), and `enabled = false/true` is Detach()/Attach(). The world the interactor
    // scans is the golem list below, read only when it takes a snapshot -- the same snapshot
    // semantics FindObjectsByType had, so each "precondition" assert still has teeth.
    public class GolemInteractableRefreshTests
    {
        private readonly List<GolemEntity> _golems = new List<GolemEntity>();
        private readonly List<PlayerInteractor> _interactors = new List<PlayerInteractor>();

        [TearDown]
        public void TearDown()
        {
            // The bus is static: an interactor left attached would hear the next test's events.
            foreach (PlayerInteractor interactor in _interactors)
            {
                interactor.Detach();
            }
            _interactors.Clear();
            _golems.Clear();
        }

        private PlayerInteractor NewInteractor()
        {
            var interactor = new PlayerInteractor();
            interactor.ConfigureWorld(() => new ResourceNodeMarker[0], () => new PlaceableBuilding[0], () => _golems);
            _interactors.Add(interactor);
            return interactor;
        }

        // Built AFTER the interactor has taken its snapshot, as every real golem is. Unity stood
        // it at (0.2, 0, 0) on cell (0,0); its cell centre (0,0) is just as close to the player.
        private GolemEntity NewGolemNearby(string id)
        {
            GolemEntity golem = new GolemEntity();
            golem.Configure(id, null);
            golem.SetPlacement(Vector2Int.zero, Facing.North);
            _golems.Add(golem);
            return golem;
        }

        [Test]
        public void AGolemThatAppearsAfterStartupBecomesCarryable()
        {
            PlayerInteractor interactor = NewInteractor();
            interactor.Attach(); // Unity: OnEnable -- snapshot taken, subscription made.

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

        [Test]
        public void AGolemThatAppearsAfterStartupBecomesRotatableAndProgrammable()
        {
            PlayerInteractor interactor = NewInteractor();
            interactor.Attach();

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

        [Test]
        public void ADisabledInteractorStopsListeningAndRescansOnReEnable()
        {
            // The bus is STATIC, so an interactor that failed to unsubscribe would keep
            // re-scanning on behalf of a scene that is gone.
            PlayerInteractor interactor = NewInteractor();
            interactor.Attach();

            interactor.Detach(); // Unity: enabled = false
            NewGolemNearby("G1");
            EventBus.Publish(new WorldInteractablesChangedEvent("test"));

            Assert.IsFalse(
                interactor.TryPickUpNearestGolem(),
                "a disabled interactor must not still be handling world-changed events");

            interactor.Attach(); // Unity: enabled = true -- OnEnable re-scans.
            Assert.IsTrue(
                interactor.TryPickUpNearestGolem(),
                "re-enabling must pick up everything that appeared while it was off");
        }
    }
}
