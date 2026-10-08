using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.Player;
using GolemFactory.PunchCards;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Tests.PlayMode
{
    // Ported from Unity's PlayMode suite, which needed Play mode only for
    // PlayerInteractor.OnEnable -- Attach() now, called where Unity's first frame ran it.
    // Setup only: the world is handed over (ConfigureWorld), each thing stands where its
    // GameObject stood (ConfigurePositions), and the panels are recording fakes.
    public class PlayerInteractorTests
    {
        // The world the interactor scans -- what Unity found with FindObjectsByType.
        private readonly List<ResourceNodeMarker> _markers = new List<ResourceNodeMarker>();
        private readonly List<PlaceableBuilding> _buildings = new List<PlaceableBuilding>();
        private readonly List<GolemEntity> _golems = new List<GolemEntity>();
        private readonly Dictionary<object, Vector3> _positions = new Dictionary<object, Vector3>();
        private readonly List<PlayerInteractor> _attached = new List<PlayerInteractor>();

        [TearDown]
        public void TearDown()
        {
            foreach (PlayerInteractor interactor in _attached)
            {
                interactor.Detach();
            }

            // NUnit reuses one fixture instance for every test; Unity's TearDown destroyed
            // every GameObject, so the world is emptied here.
            _attached.Clear();
            _markers.Clear();
            _buildings.Clear();
            _golems.Clear();
            _positions.Clear();
        }

        // The panels these tests opened were the real UGUI ones, read only for IsOpen. The
        // screens are interfaces now (the real ones arrive with G7/G8), so these record it.
        private sealed class FakeConstructionScreen : IConstructionScreen
        {
            public bool IsOpen { get; private set; }
            public void Open(GolemConstructionStation station) => IsOpen = true;
        }

        private sealed class FakeWorkbench : IWorkbenchScreen
        {
            public bool IsOpen { get; private set; }
            public GolemEntity TargetGolem { get; private set; }
            public void Open() => IsOpen = true;
            public void Close() => IsOpen = false;
            public void RetargetGolem(GolemEntity golem) => TargetGolem = golem;
        }

        private PlayerInteractor NewInteractor(
            StorageBufferRegistry stockpile, IConstructionScreen panel = null, IWorkbenchScreen workbench = null)
        {
            var interactor = new PlayerInteractor();
            interactor.Configure(1.5f, stockpile, "FactoryStockpile", panel, workbench);
            interactor.ConfigureWorld(() => _markers, () => _buildings, () => _golems);
            interactor.ConfigurePositions(thing => _positions.TryGetValue(thing, out Vector3 p) ? p : (Vector3?)null);
            _attached.Add(interactor);
            return interactor;
        }

        private (PlayerInteractor interactor, StorageBufferRegistry stockpile) Build()
        {
            var stockpile = new StorageBufferRegistry();
            return (NewInteractor(stockpile), stockpile);
        }

        private ResourceNodeMarker MakeMarker(Vector3 position, string nodeId, int quantity = ResourceNode.Infinite)
        {
            var nodes = new ResourceNodeRegistry();
            nodes.Register(new ResourceNode(nodeId, ItemType.Scrap, quantity));

            var marker = new ResourceNodeMarker { Position = position };
            marker.Configure(nodes, nodeId);
            _markers.Add(marker);
            return marker;
        }

        private GolemConstructionStation MakeStation(Vector3 position)
        {
            var building = new PlaceableBuilding();
            GolemConstructionStation station = building.AddPart(new GolemConstructionStation());
            station.Configure(new ChassisDefinition[0], null, null, null, null, null, null, "FactoryStockpile");
            _buildings.Add(building);
            _positions[station] = position;
            return station;
        }

        [Test]
        public void Interact_NodeInRange_HarvestsAndDepositsIntoStockpile()
        {
            (PlayerInteractor interactor, StorageBufferRegistry stockpile) = Build();
            MakeMarker(new Vector3(0.5f, 0f, 0f), "ScrapNode");
            interactor.Attach(); // Unity: the OnEnable frame
            interactor.RefreshInteractables();

            bool result = interactor.Interact();

            Assert.IsTrue(result);
            Assert.AreEqual(1, stockpile.GetOrCreate("FactoryStockpile").GetQuantity(ItemType.Scrap));
        }

        [Test]
        public void Interact_NodeOutOfRange_FailsWithoutDepositing()
        {
            (PlayerInteractor interactor, StorageBufferRegistry stockpile) = Build();
            MakeMarker(new Vector3(5f, 0f, 0f), "ScrapNode");
            interactor.Attach(); // Unity: the OnEnable frame
            interactor.RefreshInteractables();

            bool result = interactor.Interact();

            Assert.IsFalse(result);
            Assert.AreEqual(0, stockpile.GetOrCreate("FactoryStockpile").GetQuantity(ItemType.Scrap));
        }

        [Test]
        public void Interact_NoInteractablesInRange_FailsWithStatusMessage()
        {
            (PlayerInteractor interactor, _) = Build();
            interactor.Attach(); // Unity: the OnEnable frame
            interactor.RefreshInteractables();

            bool result = interactor.Interact();

            Assert.IsFalse(result);
            Assert.IsNotEmpty(interactor.LastStatusMessage);
        }

        [Test]
        public void Interact_StationInRange_OpensConstructionPanel()
        {
            var panel = new FakeConstructionScreen();
            PlayerInteractor interactor = NewInteractor(null, panel);
            MakeStation(new Vector3(0.5f, 0f, 0f));
            interactor.Attach(); // Unity: the OnEnable frame
            interactor.RefreshInteractables();

            bool result = interactor.Interact();

            Assert.IsTrue(result);
            Assert.IsTrue(panel.IsOpen);
        }

        // --- Affordance ---------------------------------------------------------------
        // Before this pass nothing on screen indicated what was interactable, what was in
        // range, or what Interact would do. These assert what the player is actually told.

        [Test]
        public void Affordance_NodeInRange_IsReadyAndNamesTheAction()
        {
            (PlayerInteractor interactor, _) = Build();
            MakeMarker(new Vector3(0.5f, 0f, 0f), "ScrapNode");
            interactor.Attach(); // Unity: the OnEnable frame
            interactor.RefreshInteractables();

            interactor.RefreshAffordance();

            Assert.AreEqual(InteractionAffordance.Ready, interactor.CurrentAffordance);
            Assert.AreEqual(InteractionKind.Harvest, interactor.CurrentPick.Kind);
            StringAssert.StartsWith("[E]", interactor.CurrentPrompt);
            StringAssert.Contains("Harvest", interactor.CurrentPrompt);
            // An infinite node must say so rather than printing a raw -1.
            StringAssert.Contains("unlimited", interactor.CurrentPrompt);
        }

        [Test]
        public void Affordance_NodeJustOutOfRange_SaysMoveCloserInsteadOfOfferingTheKey()
        {
            (PlayerInteractor interactor, _) = Build();
            MakeMarker(new Vector3(3f, 0f, 0f), "ScrapNode");
            interactor.Attach(); // Unity: the OnEnable frame
            interactor.RefreshInteractables();

            interactor.RefreshAffordance();

            Assert.AreEqual(InteractionAffordance.OutOfRange, interactor.CurrentAffordance);
            StringAssert.DoesNotContain("[E]", interactor.CurrentPrompt);
            StringAssert.StartsWith("Move closer", interactor.CurrentPrompt);
        }

        [Test]
        public void Affordance_FarAwayNode_ShowsNothing()
        {
            (PlayerInteractor interactor, _) = Build();
            MakeMarker(new Vector3(40f, 0f, 0f), "ScrapNode");
            interactor.Attach(); // Unity: the OnEnable frame
            interactor.RefreshInteractables();

            interactor.RefreshAffordance();

            Assert.AreEqual(InteractionAffordance.Hidden, interactor.CurrentAffordance);
            Assert.IsEmpty(interactor.CurrentPrompt);
        }

        [Test]
        public void Affordance_DepletedNode_SaysDepletedBeforeThePlayerPressesAnything()
        {
            PlayerInteractor interactor = NewInteractor(null);
            MakeMarker(new Vector3(0.5f, 0f, 0f), "Spent", quantity: 0);
            interactor.Attach(); // Unity: the OnEnable frame
            interactor.RefreshInteractables();

            interactor.RefreshAffordance();

            Assert.AreEqual(InteractionAffordance.Unavailable, interactor.CurrentAffordance);
            StringAssert.Contains("depleted", interactor.CurrentPrompt);
            StringAssert.DoesNotContain("[E]", interactor.CurrentPrompt);
        }

        // The affordance is a world-space overlay; a full screen owns the player's attention
        // and dims the world behind it, so anything still drawing under that dim is misplaced.
        [Test]
        public void Affordance_HidesWhileAFullScreenIsOpen()
        {
            var workbench = new FakeWorkbench();
            PlayerInteractor interactor = NewInteractor(null, workbench: workbench);

            MakeMarker(new Vector3(0.5f, 0f, 0f), "ScrapNode");
            interactor.Attach(); // Unity: the OnEnable frame
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
        [Test]
        public void Interact_PicksTheGenuinelyNearestTarget_NotTheFirstKindInRange()
        {
            var panel = new FakeConstructionScreen();
            PlayerInteractor interactor = NewInteractor(null, panel);

            MakeMarker(new Vector3(1.4f, 0f, 0f), "ScrapNode");
            MakeStation(new Vector3(0.2f, 0f, 0f));
            interactor.Attach(); // Unity: the OnEnable frame
            interactor.RefreshInteractables();

            bool result = interactor.Interact();

            Assert.IsTrue(result);
            Assert.IsTrue(panel.IsOpen, "The station was much closer than the node and should have won.");
        }

        [Test]
        public void Harvest_DrainsTheNodeVisualsAsWellAsTheQuantity()
        {
            var stockpile = new StorageBufferRegistry();
            PlayerInteractor interactor = NewInteractor(stockpile);
            ResourceNodeMarker marker = MakeMarker(new Vector3(0.5f, 0f, 0f), "Small", quantity: 2);
            interactor.Attach(); // Unity: the OnEnable frame
            interactor.RefreshInteractables();

            // Unity read the marker sprite's colour; the marker now holds the same readout.
            float fullLuminance = marker.Visual.Tint.grayscale;

            Assert.IsTrue(interactor.Interact());
            Assert.IsTrue(interactor.Interact());

            Assert.IsTrue(marker.IsDepleted);
            marker.RefreshVisualState();
            Assert.Less(marker.Visual.Tint.grayscale, fullLuminance,
                "A spent node has to look spent, not merely report a status string.");

            // ...and the third attempt fails rather than minting free resources.
            Assert.IsFalse(interactor.Interact());
            Assert.AreEqual(2, stockpile.GetOrCreate("FactoryStockpile").GetQuantity(ItemType.Scrap));
        }

        [Test]
        public void Interact_GolemInRange_RetargetsWorkbench()
        {
            var workbench = new FakeWorkbench();
            PlayerInteractor interactor = NewInteractor(null, workbench: workbench);

            var golem = new GolemEntity();
            golem.Configure("Golem", null);
            _golems.Add(golem);
            _positions[golem] = new Vector3(0.5f, 0f, 0f);
            interactor.Attach(); // Unity: the OnEnable frame
            interactor.RefreshInteractables();

            bool result = interactor.Interact();

            Assert.IsTrue(result);
            Assert.IsTrue(workbench.IsOpen);
        }
    }
}
