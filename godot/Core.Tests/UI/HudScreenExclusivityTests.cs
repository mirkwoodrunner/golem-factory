using GolemFactory.UI;
using NUnit.Framework;

namespace GolemFactory.Tests.UI
{
    /// <summary>
    /// Unity's PlayMode HudScreenExclusivityTests, ported onto Core's
    /// <see cref="ScreenCoordinator"/> (G8). Unity's overlap bug was pure wiring -- each screen
    /// force-closed only the siblings a previous pass had happened to wire, so three screens
    /// could stack. These assert the invariant from every entry point. The three screens are
    /// stand-ins that report to the coordinator as they open, exactly as the Godot screens do;
    /// BuildMenu_HidesWhileAnyScreenIsOpenAndReturnsAfterwards is checked by the `management`
    /// scenario against the real build menu.
    /// </summary>
    public class HudScreenExclusivityTests
    {
        private sealed class Screen : IClosableScreen
        {
            private readonly ScreenCoordinator _coordinator;
            public Screen(ScreenCoordinator coordinator)
            {
                _coordinator = coordinator;
                coordinator.Register(this);
            }

            public bool IsOpen { get; private set; }

            public void Open()
            {
                _coordinator.Opening(this);
                IsOpen = true;
            }

            public void Close() => IsOpen = false;
        }

        private ScreenCoordinator _coordinator;
        private Screen _workbench;
        private Screen _management;
        private Screen _construction;

        [SetUp]
        public void SetUp()
        {
            _coordinator = new ScreenCoordinator();
            _workbench = new Screen(_coordinator);
            _management = new Screen(_coordinator);
            _construction = new Screen(_coordinator);
        }

        private void AssertNoOverlap() =>
            Assert.IsFalse(HudScreenPolicy.HasOverlap(_workbench.IsOpen, _management.IsOpen, _construction.IsOpen),
                "two full screens are up at once");

        [Test]
        public void OpeningConstruction_ClosesWorkbenchAndManagement()
        {
            _workbench.Open();
            _management.Open();
            _construction.Open();

            Assert.IsTrue(_construction.IsOpen);
            Assert.IsFalse(_workbench.IsOpen);
            Assert.IsFalse(_management.IsOpen);
            AssertNoOverlap();
        }

        [Test]
        public void OpeningWorkbench_ClosesConstruction()
        {
            _construction.Open();
            _workbench.Open();

            Assert.IsTrue(_workbench.IsOpen);
            Assert.IsFalse(_construction.IsOpen);
            AssertNoOverlap();
        }

        [Test]
        public void OpeningManagement_ClosesConstruction()
        {
            _construction.Open();
            _management.Open();

            Assert.IsTrue(_management.IsOpen);
            Assert.IsFalse(_construction.IsOpen);
            AssertNoOverlap();
        }

        [Test]
        public void EveryOpenOrder_LeavesExactlyOneScreenUp()
        {
            _workbench.Open();
            AssertNoOverlap();
            _construction.Open();
            AssertNoOverlap();
            _management.Open();
            AssertNoOverlap();
            _construction.Open();
            AssertNoOverlap();
            _workbench.Open();
            AssertNoOverlap();
            _management.Open();
            AssertNoOverlap();
            Assert.AreEqual(1, _coordinator.OpenCount);
        }
    }
}
