using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using GolemFactory.Buildings;
using GolemFactory.PunchCards;
using GolemFactory.UI;

namespace GolemFactory.Tests.PlayMode
{
    // The overlap bug this pass fixed was pure wiring: in Sandbox.unity,
    // GolemConstructionPanel.workbenchController, .managementPanel and
    // WorkbenchController.constructionPanel were all null, so each screen only force-closed
    // whichever siblings a previous pass had happened to wire, and three screens could stack.
    // These tests assert the invariant from every entry point rather than trusting one scene's
    // serialized state to stay correct.
    //
    // PlayMode because ManagementPanel.Start()/GolemConstructionPanel.Awake() (which build and
    // hide the screens) don't run outside Play Mode -- the project-wide [ExecuteAlways] gotcha.
    public class HudScreenExclusivityTests
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

        private (WorkbenchController workbench, ManagementPanel management, GolemConstructionPanel construction, GolemConstructionStation station)
            Build()
        {
            _root = new GameObject("Root");

            var workbench = new GameObject("Workbench").AddComponent<WorkbenchController>();
            workbench.transform.SetParent(_root.transform);

            var management = new GameObject("Management").AddComponent<ManagementPanel>();
            management.transform.SetParent(_root.transform);

            var construction = new GameObject("Construction").AddComponent<GolemConstructionPanel>();
            construction.transform.SetParent(_root.transform);

            // Every direction of the mutual exclusion, which is exactly what Sandbox.unity was
            // missing on two of the three screens.
            workbench.ConfigureVisibility(null, null, management, construction);
            management.Configure(null, null, null, null, null, null, null, null, null, null,
                null, null, null, null, null, workbench, construction);
            construction.ConfigureVisibility(workbench, management);

            var stationGo = new GameObject("Station", typeof(PlaceableBuilding));
            stationGo.transform.SetParent(_root.transform);
            var station = stationGo.AddComponent<GolemConstructionStation>();
            station.Configure(new ChassisDefinition[0], null, null, null, null, null, null, "FactoryStockpile");

            return (workbench, management, construction, station);
        }

        private static void AssertNoOverlap(WorkbenchController w, ManagementPanel m, GolemConstructionPanel c)
        {
            Assert.IsFalse(HudScreenPolicy.HasOverlap(w.IsOpen, m.IsOpen, c.IsOpen),
                $"Two screens open at once: workbench={w.IsOpen} management={m.IsOpen} construction={c.IsOpen}");
        }

        [UnityTest]
        public IEnumerator OpeningConstruction_ClosesWorkbenchAndManagement()
        {
            var (workbench, management, construction, station) = Build();
            yield return null;

            workbench.Open();
            management.Open();
            construction.Open(station);

            Assert.IsTrue(construction.IsOpen);
            Assert.IsFalse(workbench.IsOpen);
            Assert.IsFalse(management.IsOpen);
            AssertNoOverlap(workbench, management, construction);
        }

        [UnityTest]
        public IEnumerator OpeningWorkbench_ClosesConstruction()
        {
            var (workbench, management, construction, station) = Build();
            yield return null;

            construction.Open(station);
            workbench.Open();

            Assert.IsTrue(workbench.IsOpen);
            Assert.IsFalse(construction.IsOpen);
            AssertNoOverlap(workbench, management, construction);
        }

        [UnityTest]
        public IEnumerator OpeningManagement_ClosesConstruction()
        {
            var (workbench, management, construction, station) = Build();
            yield return null;

            construction.Open(station);
            management.Open();

            Assert.IsTrue(management.IsOpen);
            Assert.IsFalse(construction.IsOpen);
            AssertNoOverlap(workbench, management, construction);
        }

        [UnityTest]
        public IEnumerator EveryOpenOrder_LeavesExactlyOneScreenUp()
        {
            var (workbench, management, construction, station) = Build();
            yield return null;

            for (int i = 0; i < 3; i++)
            {
                workbench.Open();
                AssertNoOverlap(workbench, management, construction);
                construction.Open(station);
                AssertNoOverlap(workbench, management, construction);
                management.Open();
                AssertNoOverlap(workbench, management, construction);
                construction.Open(station);
                AssertNoOverlap(workbench, management, construction);
                workbench.Open();
                AssertNoOverlap(workbench, management, construction);
                management.Open();
                AssertNoOverlap(workbench, management, construction);
            }
        }

        // The build menu lives on its own Canvas, so it cannot be sorted against the screens
        // above it -- it has to actually hide.
        [UnityTest]
        public IEnumerator BuildMenu_HidesWhileAnyScreenIsOpenAndReturnsAfterwards()
        {
            var (workbench, management, construction, station) = Build();

            var menuGo = new GameObject("BuildMenu", typeof(RectTransform));
            menuGo.transform.SetParent(_root.transform, false);
            var body = new GameObject("Panel", typeof(RectTransform));
            body.transform.SetParent(menuGo.transform, false);
            var menu = menuGo.AddComponent<BuildMenuPanel>();
            menu.ConfigureScreens(body, workbench, management, construction);
            yield return null;

            Assert.IsTrue(menu.IsBodyVisible, "Build menu should be visible with no screen open.");

            management.Open();
            yield return null;
            Assert.IsFalse(menu.IsBodyVisible);

            construction.Open(station);
            yield return null;
            Assert.IsFalse(menu.IsBodyVisible);

            workbench.Open();
            yield return null;
            Assert.IsFalse(menu.IsBodyVisible);

            workbench.Close();
            yield return null;
            Assert.IsTrue(menu.IsBodyVisible, "Build menu must come back once every screen is closed.");
        }
    }
}
