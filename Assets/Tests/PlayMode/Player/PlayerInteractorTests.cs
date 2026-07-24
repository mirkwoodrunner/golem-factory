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
        }
    }
}
