using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using GolemFactory.Golems;
using GolemFactory.Player;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    // Rotating and repositioning a placed golem -- the two moves that turn facing from a fact
    // about where a golem happened to be built into an actual decision.
    //
    // Both of these pin bugs found by playing the loop, not by reading the code:
    //   * rotation used to require the golem to win the combined [E] interaction pick, so
    //     standing next to a golem that was (as they always are) beside its node refused with
    //     "no golem in range";
    //   * repositioning used to be "summon the nearest golem to my tile", which with two golems
    //     in play reliably moved the wrong one -- the already-placed golem next to the
    //     destination beat the new one still standing at the station.
    public class GolemRepositioningTests
    {
        private readonly List<GameObject> _spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _spawned)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }
            _spawned.Clear();
        }

        private GameObject NewObject(string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            _spawned.Add(go);
            return go;
        }

        private GolemEntity NewGolem(string id, Vector2Int cell, Facing facing, Vector3 position)
        {
            GameObject go = NewObject(id, position);
            GolemEntity golem = go.AddComponent<GolemEntity>();
            golem.Configure(id, null);
            golem.SetPlacement(cell, facing);
            return golem;
        }

        private PlayerInteractor NewPlayerAt(Vector3 position, GridMapHolder gridMap)
        {
            GameObject go = NewObject("Player", position);
            PlayerInteractor interactor = go.AddComponent<PlayerInteractor>();
            // Deliberately NOT calling Configure(...): its first parameter is an
            // InputActionAsset, and this test assembly does not reference Unity.InputSystem.
            // The serialized defaults (no actions, 1.5 interact range) are exactly what these
            // tests want anyway, and every method under test is driven directly.
            interactor.ConfigureGolemPlacement(gridMap, new Vector2(1f, 0.5f));
            interactor.RefreshInteractables();
            return interactor;
        }

        // --- Rotation ---------------------------------------------------------------------

        [Test]
        public void Rotate_TurnsTheNearbyGolemOneStepClockwise()
        {
            GolemEntity golem = NewGolem("G1", new Vector2Int(0, 0), Facing.North, Vector3.zero);
            PlayerInteractor interactor = NewPlayerAt(new Vector3(0.2f, 0f, 0f), null);

            Assert.IsTrue(interactor.RotateNearestGolem());

            Assert.AreEqual(Facing.East, golem.Facing);
            Assert.AreEqual(new Vector2Int(0, 0), golem.Cell, "rotation must not move the golem");
        }

        [Test]
        public void Rotate_WorksEvenWhenAResourceNodeIsTheNearestInteractable()
        {
            // The real bug. A golem is always placed beside the node it pulls from, so the node
            // usually wins the combined [E] pick -- and rotation used to key off that pick.
            GolemEntity golem = NewGolem("G1", new Vector2Int(0, 0), Facing.North, new Vector3(0.4f, 0f, 0f));
            GameObject nodeGo = NewObject("ScrapNodeMarker", new Vector3(0.05f, 0f, 0f));
            nodeGo.AddComponent<SpriteRenderer>();
            nodeGo.AddComponent<ResourceNodeMarker>();

            PlayerInteractor interactor = NewPlayerAt(Vector3.zero, null);

            Assert.IsTrue(interactor.RotateNearestGolem(),
                "a node standing closer than the golem blocked rotation entirely");
            Assert.AreEqual(Facing.East, golem.Facing);
        }

        [Test]
        public void Rotate_WithNoGolemInRange_Refuses()
        {
            NewGolem("G1", new Vector2Int(9, 9), Facing.North, new Vector3(40f, 0f, 0f));
            PlayerInteractor interactor = NewPlayerAt(Vector3.zero, null);

            Assert.IsFalse(interactor.RotateNearestGolem());
        }

        [Test]
        public void RotatingFourTimes_ReturnsToTheOriginalFacing()
        {
            GolemEntity golem = NewGolem("G1", new Vector2Int(0, 0), Facing.North, Vector3.zero);
            PlayerInteractor interactor = NewPlayerAt(new Vector3(0.2f, 0f, 0f), null);

            for (int i = 0; i < 4; i++)
            {
                interactor.RotateNearestGolem();
            }

            Assert.AreEqual(Facing.North, golem.Facing);
        }

        // --- Carry / drop -------------------------------------------------------------------

        [Test]
        public void CarryThenDrop_MovesTheGolemToThePlayersCell()
        {
            GolemEntity golem = NewGolem("G1", new Vector2Int(5, 5), Facing.East, Vector3.zero);
            PlayerInteractor interactor = NewPlayerAt(new Vector3(0.2f, 0f, 0f), null);

            Assert.IsTrue(interactor.TryPickUpNearestGolem());
            Assert.AreSame(golem, interactor.CarriedGolem);
            Assert.IsTrue(golem.IsHeld, "a carried golem must stop running");

            // Walk to the destination tile and set it down.
            var converter = new GridCoordinateConverter(new Vector2(1f, 0.5f));
            var destination = new Vector2Int(2, -3);
            interactor.transform.position = converter.CellToWorldCenter(destination);

            Assert.IsTrue(interactor.TryDropCarriedGolem());
            Assert.AreEqual(destination, golem.Cell);
            Assert.AreEqual(Facing.East, golem.Facing, "dropping must preserve facing");
            Assert.IsFalse(golem.IsHeld);
            Assert.IsNull(interactor.CarriedGolem);
        }

        [Test]
        public void CarryPicksUpTheGolemYouAreStandingBeside_NotTheOneNearestTheDestination()
        {
            // The disambiguation that "summon the nearest golem here" got wrong: the player is
            // at the station with a new golem, while an already-placed golem sits elsewhere.
            GolemEntity atStation = NewGolem("New", new Vector2Int(6, 1), Facing.North, new Vector3(0.2f, 0f, 0f));
            GolemEntity alreadyPlaced = NewGolem("Placed", new Vector2Int(0, -4), Facing.North, new Vector3(9f, 0f, 0f));
            PlayerInteractor interactor = NewPlayerAt(Vector3.zero, null);

            Assert.IsTrue(interactor.TryPickUpNearestGolem());

            Assert.AreSame(atStation, interactor.CarriedGolem, "picked up the wrong golem");
            Assert.IsFalse(alreadyPlaced.IsHeld);
            Assert.AreEqual(new Vector2Int(0, -4), alreadyPlaced.Cell, "the placed golem was disturbed");
        }

        [Test]
        public void DroppingOntoAnOccupiedCell_IsRefusedAndKeepsCarrying()
        {
            GameObject holderGo = NewObject("GridMap", Vector3.zero);
            GridMapHolder gridMap = holderGo.AddComponent<GridMapHolder>();

            GolemEntity golem = NewGolem("G1", new Vector2Int(5, 5), Facing.North, Vector3.zero);
            PlayerInteractor interactor = NewPlayerAt(new Vector3(0.2f, 0f, 0f), gridMap);
            Assert.IsTrue(interactor.TryPickUpNearestGolem());

            var converter = new GridCoordinateConverter(new Vector2(1f, 0.5f));
            var blocked = new Vector2Int(1, 1);
            gridMap.Map.TryOccupy(blocked, new object());
            interactor.transform.position = converter.CellToWorldCenter(blocked);

            Assert.IsFalse(interactor.TryDropCarriedGolem());
            Assert.AreSame(golem, interactor.CarriedGolem, "a refused drop should keep the golem in hand");
            Assert.IsTrue(golem.IsHeld);
            Assert.AreEqual(new Vector2Int(5, 5), golem.Cell, "a refused drop moved the golem anyway");
        }

        [Test]
        public void ToggleCarry_PicksUpThenSetsDown()
        {
            GolemEntity golem = NewGolem("G1", new Vector2Int(5, 5), Facing.North, Vector3.zero);
            PlayerInteractor interactor = NewPlayerAt(new Vector3(0.2f, 0f, 0f), null);

            Assert.IsTrue(interactor.ToggleCarryGolem());
            Assert.AreSame(golem, interactor.CarriedGolem);

            Assert.IsTrue(interactor.ToggleCarryGolem());
            Assert.IsNull(interactor.CarriedGolem);
        }

        [Test]
        public void AHeldGolemDoesNotRun()
        {
            // Its Cell is stale by definition while it is in the player's hands, so letting it
            // tick would move items between two tiles it is no longer standing between.
            var endpointsGo = NewObject("Endpoints", Vector3.zero);
            SpatialEndpointRegistryHolder endpoints = endpointsGo.AddComponent<SpatialEndpointRegistryHolder>();
            var source = new GolemFactory.Economy.StorageBuffer("Source");
            source.Deposit(GolemFactory.Economy.ItemType.Scrap, 5);
            var destination = new GolemFactory.Economy.StorageBuffer("Dest");
            endpoints.Registry.Register(new Vector2Int(0, -1), new StorageBufferEndpoint(source));
            endpoints.Registry.Register(new Vector2Int(0, 1), new StorageBufferEndpoint(destination));

            GameObject go = NewObject("Golem", Vector3.zero);
            GolemEntity golem = go.AddComponent<GolemEntity>();
            var logicCore = ScriptableObject.CreateInstance<GolemFactory.PunchCards.LogicCoreDefinition>();
            logicCore.triggerType = GolemFactory.PunchCards.TriggerType.AlwaysOn;
            var step = ScriptableObject.CreateInstance<GolemFactory.PunchCards.AppendageActionDefinition>();
            step.actionType = GolemFactory.PunchCards.AppendageActionType.Haul;
            golem.Program.logicCore = logicCore;
            golem.Program.appendages.Add(step);
            golem.ConfigureSpatial(endpoints, Vector2Int.zero, Facing.North);

            golem.SetHeld(true);
            for (int tick = 0; tick < 5; tick++)
            {
                golem.Tick(tick);
            }
            Assert.AreEqual(0, destination.GetQuantity(GolemFactory.Economy.ItemType.Scrap),
                "a golem being carried kept hauling");

            golem.SetHeld(false);
            golem.Tick(5);
            Assert.AreEqual(1, destination.GetQuantity(GolemFactory.Economy.ItemType.Scrap),
                "it did not resume once set down");
        }
    }
}
