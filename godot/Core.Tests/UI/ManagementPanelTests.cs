using System;
using System.Linq;
using GolemFactory.UI;
using NUnit.Framework;

namespace GolemFactory.Tests.UI
{
    /// <summary>
    /// Unity's PlayMode ManagementPanelTests, ported onto Core's <see cref="ManagementTabs"/> and
    /// <see cref="ScreenCoordinator"/> (G8). "Content shown" and "button lit" are the questions
    /// the screen asks of the tab state each frame; the `management` scenario checks the Godot
    /// screen draws what they answer.
    /// </summary>
    public class ManagementPanelTests
    {
        private static readonly ManagementTab[] AllTabs = (ManagementTab[])Enum.GetValues(typeof(ManagementTab));

        [Test]
        public void Toggle_OpensAndCloses()
        {
            var tabs = new ManagementTabs();
            Assert.IsFalse(tabs.IsOpen);
            tabs.Toggle();
            Assert.IsTrue(tabs.IsOpen);
            tabs.Toggle();
            Assert.IsFalse(tabs.IsOpen);
        }

        [Test]
        public void Open_ClosesWorkbenchAndConstructionPanel()
        {
            var coordinator = new ScreenCoordinator();
            var workbench = new StubScreen(coordinator);
            var construction = new StubScreen(coordinator);
            var management = new StubScreen(coordinator);
            workbench.Open();
            construction.Open();

            management.Open();

            Assert.IsFalse(workbench.IsOpen);
            Assert.IsFalse(construction.IsOpen);
        }

        [Test]
        public void SelectTab_ActivatesOnlyTheChosenTabContent()
        {
            var tabs = new ManagementTabs();
            tabs.SelectTab(ManagementTab.Patents);

            Assert.AreEqual(ManagementTab.Patents, tabs.ActiveTab);
            CollectionAssert.AreEqual(new[] { ManagementTab.Patents }, AllTabs.Where(tabs.IsContentShown));
        }

        [Test]
        public void SelectTab_HighlightsExactlyOneTabButton()
        {
            var tabs = new ManagementTabs();
            tabs.SelectTab(ManagementTab.AssemblyLine);
            CollectionAssert.AreEqual(new[] { ManagementTab.AssemblyLine }, AllTabs.Where(tabs.IsHighlighted));

            tabs.SelectTab(ManagementTab.SaveLoad);
            CollectionAssert.AreEqual(new[] { ManagementTab.SaveLoad }, AllTabs.Where(tabs.IsHighlighted),
                "the tint is the only thing telling the player which tab they are on");
        }

        private sealed class StubScreen : IClosableScreen
        {
            private readonly ScreenCoordinator _coordinator;
            public StubScreen(ScreenCoordinator c) { _coordinator = c; c.Register(this); }
            public bool IsOpen { get; private set; }
            public void Open() { _coordinator.Opening(this); IsOpen = true; }
            public void Close() => IsOpen = false;
        }
    }
}
