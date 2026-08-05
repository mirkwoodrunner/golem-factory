using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.Player;
using GolemFactory.PunchCards;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Tests.PlayMode
{
    // Needs PlayMode since PlayerInteractor.OnEnable (which calls RefreshInteractables and
    // enables the Interact action) doesn't run outside Play Mode -- same gotcha as
    // PlayerControllerTests.
    public class PlayerInteractorTests
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

        private (PlayerInteractor interactor, StorageBufferRegistryHolder stockpile) Build()
        {
            _root = new GameObject("Root");

            var interactor = new GameObject("Interactor").AddComponent<PlayerInteractor>();
            interactor.transform.SetParent(_root.transform);

            var stockpile = new GameObject("Stockpile").AddComponent<StorageBufferRegistryHolder>();
            stockpile.transform.SetParent(_root.transform);

            interactor.Configure(null, interactRange: 1.5f, stockpile, "FactoryStockpile", null, null);
            return (interactor, stockpile);
        }

        private ResourceNodeMarker MakeMarker(Vector3 position, string nodeId)
        {
            var nodeRegistryHolder = new GameObject("Nodes").AddComponent<ResourceNodeRegistryHolder>();
            nodeRegistryHolder.transform.SetParent(_root.transform);
            nodeRegistryHolder.Registry.Register(new ResourceNode(nodeId, ItemType.Scrap));

            var marker = new GameObject("Marker", typeof(SpriteRenderer)).AddComponent<ResourceNodeMarker>();
            marker.transform.SetParent(_root.transform);
            marker.transform.position = position;
            marker.Configure(nodeRegistryHolder, nodeId);
            return marker;
        }

        [UnityTest]
        public IEnumerator Interact_NodeInRange_HarvestsAndDepositsIntoStockpile()
        {
            (PlayerInteractor interactor, StorageBufferRegistryHolder stockpile) = Build();
            MakeMarker(new Vector3(0.5f, 0f, 0f), "ScrapNode");
            yield return null;
            interactor.RefreshInteractables();

            bool result = interactor.Interact();

            Assert.IsTrue(result);
            Assert.AreEqual(1, stockpile.Registry.GetOrCreate("FactoryStockpile").GetQuantity(ItemType.Scrap));
        }

        [UnityTest]
        public IEnumerator Interact_NodeOutOfRange_FailsWithoutDepositing()
        {
            (PlayerInteractor interactor, StorageBufferRegistryHolder stockpile) = Build();
            MakeMarker(new Vector3(5f, 0f, 0f), "ScrapNode");
            yield return null;
            interactor.RefreshInteractables();

            bool result = interactor.Interact();

            Assert.IsFalse(result);
            Assert.AreEqual(0, stockpile.Registry.GetOrCreate("FactoryStockpile").GetQuantity(ItemType.Scrap));
        }

        [UnityTest]
        public IEnumerator Interact_NoInteractablesInRange_FailsWithStatusMessage()
        {
            (PlayerInteractor interactor, _) = Build();
            yield return null;
            interactor.RefreshInteractables();

            bool result = interactor.Interact();

            Assert.IsFalse(result);
            Assert.IsNotEmpty(interactor.LastStatusMessage);
        }

        [UnityTest]
        public IEnumerator Interact_StationInRange_OpensConstructionPanel()
        {
            _root = new GameObject("Root");
            var interactor = new GameObject("Interactor").AddComponent<PlayerInteractor>();
            interactor.transform.SetParent(_root.transform);

            var panel = new GameObject("Panel").AddComponent<GolemConstructionPanel>();
            panel.transform.SetParent(_root.transform);

            interactor.Configure(null, interactRange: 1.5f, null, "FactoryStockpile", panel, null);

            var stationGo = new GameObject("Station", typeof(PlaceableBuilding));
            stationGo.transform.SetParent(_root.transform);
            stationGo.transform.position = new Vector3(0.5f, 0f, 0f);
            var station = stationGo.AddComponent<GolemConstructionStation>();
            station.Configure(new ChassisDefinition[0], null, null, null, null, null, null, "FactoryStockpile");
            yield return null;
            interactor.RefreshInteractables();

            bool result = interactor.Interact();

            Assert.IsTrue(result);
            Assert.IsTrue(panel.IsOpen);
        }

        // --- Affordance ---------------------------------------------------------------
        // Before this pass nothing on screen indicated what was interactable, what was in
        // range, or what Interact would do. These assert what the player is actually told.

        [UnityTest]
        public IEnumerator Affordance_NodeInRange_IsReadyAndNamesTheAction()
        {
            (PlayerInteractor interactor, _) = Build();
            MakeMarker(new Vector3(0.5f, 0f, 0f), "ScrapNode");
            yield return null;
            interactor.RefreshInteractables();

            interactor.RefreshAffordance();

            Assert.AreEqual(InteractionAffordance.Ready, interactor.CurrentAffordance);
            Assert.AreEqual(InteractionKind.Harvest, interactor.CurrentPick.Kind);
            StringAssert.StartsWith("[E]", interactor.CurrentPrompt);
            StringAssert.Contains("Harvest", interactor.CurrentPrompt);
            // An infinite node must say so rather than printing a raw -1.
            StringAssert.Contains("unlimited", interactor.CurrentPrompt);
        }

        [UnityTest]
        public IEnumerator Affordance_NodeJustOutOfRange_SaysMoveCloserInsteadOfOfferingTheKey()
        {
            (PlayerInteractor interactor, _) = Build();
            MakeMarker(new Vector3(3f, 0f, 0f), "ScrapNode");
            yield return null;
            interactor.RefreshInteractables();

            interactor.RefreshAffordance();

            Assert.AreEqual(InteractionAffordance.OutOfRange, interactor.CurrentAffordance);
            StringAssert.DoesNotContain("[E]", interactor.CurrentPrompt);
            StringAssert.StartsWith("Move closer", interactor.CurrentPrompt);
        }

        [UnityTest]
        public IEnumerator Affordance_FarAwayNode_ShowsNothing()
        {
            (PlayerInteractor interactor, _) = Build();
            MakeMarker(new Vector3(40f, 0f, 0f), "ScrapNode");
            yield return null;
            interactor.RefreshInteractables();

            interactor.RefreshAffordance();

            Assert.AreEqual(InteractionAffordance.Hidden, interactor.CurrentAffordance);
            Assert.IsEmpty(interactor.CurrentPrompt);
        }

        [UnityTest]
        public IEnumerator Affordance_DepletedNode_SaysDepletedBeforeThePlayerPressesAnything()
        {
            _root = new GameObject("Root");
            var interactor = new GameObject("Interactor").AddComponent<PlayerInteractor>();
            interactor.transform.SetParent(_root.transform);
            interactor.Configure(null, interactRange: 1.5f, null, "FactoryStockpile", null, null);

            var nodes = new GameObject("Nodes").AddComponent<ResourceNodeRegistryHolder>();
            nodes.transform.SetParent(_root.transform);
            nodes.Registry.Register(new ResourceNode("Spent", ItemType.Scrap, 0));

            var marker = new GameObject("Marker", typeof(SpriteRenderer)).AddComponent<ResourceNodeMarker>();
            marker.transform.SetParent(_root.transform);
            marker.transform.position = new Vector3(0.5f, 0f, 0f);
            marker.Configure(nodes, "Spent");
            yield return null;
            interactor.RefreshInteractables();

            interactor.RefreshAffordance();

            Assert.AreEqual(InteractionAffordance.Unavailable, interactor.CurrentAffordance);
            StringAssert.Contains("depleted", interactor.CurrentPrompt);
            StringAssert.DoesNotContain("[E]", interactor.CurrentPrompt);
        }

        // The affordance is a world-space overlay; a full screen owns the player's attention
        // and dims the world behind it, so anything still drawing under that dim is misplaced.
        [UnityTest]
        public IEnumerator Affordance_HidesWhileAFullScreenIsOpen()
        {
            _root = new GameObject("Root");
            var interactor = new GameObject("Interactor").AddComponent<PlayerInteractor>();
            interactor.transform.SetParent(_root.transform);

            var workbench = new GameObject("Workbench").AddComponent<WorkbenchController>();
            workbench.transform.SetParent(_root.transform);
            interactor.Configure(null, interactRange: 1.5f, null, "FactoryStockpile", null, workbench);

            MakeMarker(new Vector3(0.5f, 0f, 0f), "ScrapNode");
            yield return null;
            interactor.RefreshInteractables();

            interactor.RefreshAffordance();
            Assert.AreEqual(InteractionAffordance.Ready, interactor.CurrentAffordance);

            workbench.Open();
            interactor.RefreshAffordance();
            Assert.AreEqual(InteractionAffordance.Hidden, interactor.CurrentAffordance);

            workbench.Close();
            interactor.RefreshAffordance();
            Assert.AreEqual(InteractionAffordance.Ready, interactor.CurrentAffordance);
        }

        // Regression for the selection bug the extraction to InteractionTargeting exposed:
        // the old inline version checked node markers first and acted on the first kind with
        // anything in range, so a node at the edge of range beat a station underfoot.
        [UnityTest]
        public IEnumerator Interact_PicksTheGenuinelyNearestTarget_NotTheFirstKindInRange()
        {
            _root = new GameObject("Root");
            var interactor = new GameObject("Interactor").AddComponent<PlayerInteractor>();
            interactor.transform.SetParent(_root.transform);

            var panel = new GameObject("Panel").AddComponent<GolemConstructionPanel>();
            panel.transform.SetParent(_root.transform);
            interactor.Configure(null, interactRange: 1.5f, null, "FactoryStockpile", panel, null);

            MakeMarker(new Vector3(1.4f, 0f, 0f), "ScrapNode");

            var stationGo = new GameObject("Station", typeof(PlaceableBuilding));
            stationGo.transform.SetParent(_root.transform);
            stationGo.transform.position = new Vector3(0.2f, 0f, 0f);
            var station = stationGo.AddComponent<GolemConstructionStation>();
            station.Configure(new ChassisDefinition[0], null, null, null, null, null, null, "FactoryStockpile");
            yield return null;
            interactor.RefreshInteractables();

            bool result = interactor.Interact();

            Assert.IsTrue(result);
            Assert.IsTrue(panel.IsOpen, "The station was much closer than the node and should have won.");
        }

        [UnityTest]
        public IEnumerator Harvest_DrainsTheNodeVisualsAsWellAsTheQuantity()
        {
            _root = new GameObject("Root");
            var interactor = new GameObject("Interactor").AddComponent<PlayerInteractor>();
            interactor.transform.SetParent(_root.transform);

            var stockpile = new GameObject("Stockpile").AddComponent<StorageBufferRegistryHolder>();
            stockpile.transform.SetParent(_root.transform);
            interactor.Configure(null, interactRange: 1.5f, stockpile, "FactoryStockpile", null, null);

            var nodes = new GameObject("Nodes").AddComponent<ResourceNodeRegistryHolder>();
            nodes.transform.SetParent(_root.transform);
            nodes.Registry.Register(new ResourceNode("Small", ItemType.Scrap, 2));

            var markerGo = new GameObject("Marker", typeof(SpriteRenderer));
            markerGo.transform.SetParent(_root.transform);
            markerGo.transform.position = new Vector3(0.5f, 0f, 0f);
            var marker = markerGo.AddComponent<ResourceNodeMarker>();
            marker.Configure(nodes, "Small");
            yield return null;
            interactor.RefreshInteractables();

            var renderer = markerGo.GetComponent<SpriteRenderer>();
            float fullLuminance = renderer.color.grayscale;

            Assert.IsTrue(interactor.Interact());
            Assert.IsTrue(interactor.Interact());

            Assert.IsTrue(marker.IsDepleted);
            marker.RefreshVisualState();
            Assert.Less(renderer.color.grayscale, fullLuminance,
                "A spent node has to look spent, not merely report a status string.");

            // ...and the third attempt fails rather than minting free resources.
            Assert.IsFalse(interactor.Interact());
            Assert.AreEqual(2, stockpile.Registry.GetOrCreate("FactoryStockpile").GetQuantity(ItemType.Scrap));
        }

        [UnityTest]
        public IEnumerator Interact_GolemInRange_RetargetsWorkbench()
        {
            _root = new GameObject("Root");
            var interactor = new GameObject("Interactor").AddComponent<PlayerInteractor>();
            interactor.transform.SetParent(_root.transform);

            var workbench = new GameObject("Workbench").AddComponent<WorkbenchController>();
            workbench.transform.SetParent(_root.transform);

            interactor.Configure(null, interactRange: 1.5f, null, "FactoryStockpile", null, workbench);

            var golem = new GameObject("Golem").AddComponent<GolemEntity>();
            golem.transform.SetParent(_root.transform);
            golem.transform.position = new Vector3(0.5f, 0f, 0f);
            golem.Configure("Golem", null);
            yield return null;
            interactor.RefreshInteractables();

            bool result = interactor.Interact();

            Assert.IsTrue(result);
            Assert.IsTrue(workbench.IsOpen);
        }
    }
}
