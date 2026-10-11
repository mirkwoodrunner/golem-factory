using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Buildings;
using GolemFactory.Compat;
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
        // The world the interactor is handed, and where each thing stands in it. In Unity each
        // of these was a GameObject the interactor found by scene scan, standing at its
        // transform's position -- which, for a golem, need not be its cell.
        private readonly Dictionary<object, Vector3> _positions = new Dictionary<object, Vector3>();
        private readonly List<GolemEntity> _golems = new List<GolemEntity>();
        private readonly List<PlaceableBuilding> _buildings = new List<PlaceableBuilding>();
        private readonly List<ResourceNodeMarker> _markers = new List<ResourceNodeMarker>();

        // NUnit reuses one fixture instance for every test, so the world must be emptied
        // between them -- Unity's TearDown destroyed every GameObject the test made.
        [TearDown]
        public void TearDown()
        {
            _positions.Clear();
            _golems.Clear();
            _buildings.Clear();
            _markers.Clear();
        }

        private GolemEntity NewGolem(string id, Vector2Int cell, Facing facing, Vector3 position)
        {
            GolemEntity golem = new GolemEntity();
            golem.Configure(id, null);
            golem.SetPlacement(cell, facing);
            _golems.Add(golem);
            _positions[golem] = position;
            return golem;
        }

        private GolemConstructionStation NewStation(Vector3 position)
        {
            var building = new PlaceableBuilding();
            GolemConstructionStation station = building.AddPart(new GolemConstructionStation());
            _buildings.Add(building);
            _positions[station] = position;
            return station;
        }

        private ResourceNodeMarker NewMarker(Vector3 position)
        {
            var marker = new ResourceNodeMarker { Position = position };
            _markers.Add(marker);
            return marker;
        }

        private PlayerInteractor NewPlayerAt(Vector3 position, GridMap gridMap)
        {
            PlayerInteractor interactor = new PlayerInteractor { Position = position };
            interactor.ConfigureGolemPlacement(gridMap, new Vector2(1f, 0.5f));
            interactor.ConfigureWorld(() => _markers, () => _buildings, () => _golems);
            interactor.ConfigurePositions(thing => _positions.TryGetValue(thing, out Vector3 p) ? p : (Vector3?)null);
            interactor.RefreshInteractables();
            return interactor;
        }

        // --- Telling the player the keys exist --------------------------------------------
        // The third bug of the same family as the two above, and the one that made carrying
        // look unimplemented rather than merely awkward: a freshly built golem stands on the
        // tile its station faces, so at the spot the player is standing when it appears the
        // STATION wins the [E] pick -- and the golem's own caption, the only line in the game
        // that has ever mentioned [G], is not the line being drawn.

        [Test]
        public void ThePromptOffersToCarryTheGolem_EvenWhenItsStationWonThePick()
        {
            GolemEntity golem = NewGolem("G1", new Vector2Int(0, 0), Facing.North, new Vector3(0.4f, 0f, 0f));
            NewStation(new Vector3(0.05f, 0f, 0f));

            PlayerInteractor interactor = NewPlayerAt(Vector3.zero, null);
            interactor.RefreshAffordance();

            Assert.AreEqual(InteractionKind.Construct, interactor.CurrentPick.Kind,
                "the station is nearer -- that is the whole premise of this test");
            StringAssert.Contains("[G]", interactor.CurrentPrompt,
                "standing at the station is where the player learns the golem can be moved");
            StringAssert.Contains(golem.GolemId, interactor.CurrentPrompt,
                "with several golems about, the aside has to name the one [G] would lift");
        }

        [Test]
        public void ThePromptDoesNotRepeatTheAsideOnTheGolemsOwnCaption()
        {
            NewGolem("G1", new Vector2Int(0, 0), Facing.North, new Vector3(0.2f, 0f, 0f));
            PlayerInteractor interactor = NewPlayerAt(Vector3.zero, null);

            interactor.RefreshAffordance();

            Assert.AreEqual(InteractionKind.Program, interactor.CurrentPick.Kind);
            StringAssert.Contains("[G]", interactor.CurrentPrompt);
            Assert.AreEqual(
                1, CountOccurrences(interactor.CurrentPrompt, "[G]"),
                "the golem's own caption already says [G]; appending the aside would say it twice");
        }

        [Test]
        public void ThePromptSaysHowToPutTheGolemDownWhileCarryingIt()
        {
            NewGolem("G1", new Vector2Int(0, 0), Facing.North, new Vector3(0.2f, 0f, 0f));
            PlayerInteractor interactor = NewPlayerAt(Vector3.zero, null);

            Assert.IsTrue(interactor.TryPickUpNearestGolem());
            interactor.RefreshAffordance();

            StringAssert.Contains("[G]", interactor.CurrentPrompt);
            StringAssert.DoesNotContain("carry", interactor.CurrentPrompt,
                "a player already holding a golem must not be told to pick it up");
        }

        private static int CountOccurrences(string text, string needle)
        {
            int count = 0;
            for (int i = text.IndexOf(needle); i >= 0; i = text.IndexOf(needle, i + needle.Length))
            {
                count++;
            }

            return count;
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
            NewMarker(new Vector3(0.05f, 0f, 0f));

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
            interactor.Position = converter.CellToWorldCenter(destination);

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
            var gridMap = new GridMap();

            GolemEntity golem = NewGolem("G1", new Vector2Int(5, 5), Facing.North, Vector3.zero);
            PlayerInteractor interactor = NewPlayerAt(new Vector3(0.2f, 0f, 0f), gridMap);
            Assert.IsTrue(interactor.TryPickUpNearestGolem());

            var converter = new GridCoordinateConverter(new Vector2(1f, 0.5f));
            var blocked = new Vector2Int(1, 1);
            gridMap.TryOccupy(blocked, new object());
            interactor.Position = converter.CellToWorldCenter(blocked);

            Assert.IsFalse(interactor.TryDropCarriedGolem());
            Assert.AreSame(golem, interactor.CarriedGolem, "a refused drop should keep the golem in hand");
            Assert.IsTrue(golem.IsHeld);
            Assert.AreEqual(new Vector2Int(5, 5), golem.Cell, "a refused drop moved the golem anyway");
        }

        // --- The landing preview -------------------------------------------------------------
        // The carried golem is drawn in the player's hands, half a tile above the tile it lands
        // on, so the floor outlines that tile instead. The outline reads CarryDropCell, and so
        // does the drop: these pin that the two can never disagree.

        [TestCase(2.0f, -3.0f)]
        [TestCase(2.49f, -2.76f)]   // just inside a tile's edges
        [TestCase(2.51f, -2.74f)]   // just across them
        public void TheLandingTile_IsTheTileTheGolemIsSetDownOn(float x, float y)
        {
            GolemEntity golem = NewGolem("G1", new Vector2Int(5, 5), Facing.East, Vector3.zero);
            PlayerInteractor interactor = NewPlayerAt(new Vector3(0.2f, 0f, 0f), new GridMap());
            Assert.IsNull(interactor.CarryDropCell, "nothing in hand, nothing to preview");
            Assert.IsTrue(interactor.TryPickUpNearestGolem());

            interactor.Position = new Vector3(x, y, 0f);
            Vector2Int? previewed = interactor.CarryDropCell;
            Assert.IsNotNull(previewed);
            Assert.IsFalse(interactor.CarryDropBlocked);

            Assert.IsTrue(interactor.TryDropCarriedGolem());
            Assert.AreEqual(previewed.Value, golem.Cell);
            Assert.IsNull(interactor.CarryDropCell);
        }

        [Test]
        public void TheLandingTile_IsBlocked_WhereTheDropWouldBeRefused()
        {
            var gridMap = new GridMap();
            NewGolem("G1", new Vector2Int(5, 5), Facing.North, Vector3.zero);
            PlayerInteractor interactor = NewPlayerAt(new Vector3(0.2f, 0f, 0f), gridMap);
            Assert.IsTrue(interactor.TryPickUpNearestGolem());

            var converter = new GridCoordinateConverter(new Vector2(1f, 0.5f));
            var pipe = new Vector2Int(1, 1);
            gridMap.TryOccupy(pipe, new object());
            interactor.Position = converter.CellToWorldCenter(pipe);

            Assert.AreEqual(pipe, interactor.CarryDropCell);
            Assert.IsTrue(interactor.CarryDropBlocked, "red before the press, not only a refusal after it");
            Assert.IsFalse(interactor.TryDropCarriedGolem());

            interactor.Position = converter.CellToWorldCenter(new Vector2Int(2, 1));
            Assert.IsFalse(interactor.CarryDropBlocked);
        }

        [Test]
        public void TheLandingTile_IsBlocked_WhereAnotherGolemStands()
        {
            // Golems are not GridMap occupants, so the drop never saw them: two golems ended up
            // on one tile, sharing a source and a target, under a preview that said it was fine.
            GolemEntity carried = NewGolem("Carried", new Vector2Int(5, 5), Facing.North, Vector3.zero);
            GolemEntity standing = NewGolem("Standing", new Vector2Int(1, 1), Facing.North, new Vector3(9f, 0f, 0f));
            PlayerInteractor interactor = NewPlayerAt(new Vector3(0.2f, 0f, 0f), new GridMap());
            Assert.IsTrue(interactor.TryPickUpNearestGolem());
            Assert.AreSame(carried, interactor.CarriedGolem);

            interactor.Position = new GridCoordinateConverter(new Vector2(1f, 0.5f)).CellToWorldCenter(standing.Cell);
            Assert.IsTrue(interactor.CarryDropBlocked, "red over another golem");
            Assert.IsFalse(interactor.TryDropCarriedGolem());
            StringAssert.Contains("Standing", interactor.LastStatusMessage);
            Assert.AreEqual(new Vector2Int(5, 5), carried.Cell, "still in hand, not moved");
        }

        [Test]
        public void R_TurnsTheCarriedGolem_EvenWithAnotherGolemNearer()
        {
            GolemEntity carried = NewGolem("Carried", new Vector2Int(5, 5), Facing.North, new Vector3(0.3f, 0f, 0f));
            PlayerInteractor interactor = NewPlayerAt(new Vector3(0.2f, 0f, 0f), null);
            Assert.IsTrue(interactor.TryPickUpNearestGolem());
            Assert.AreSame(carried, interactor.CarriedGolem);

            // A placed golem right at the player's feet: the nearest golem, and not the one in hand.
            GolemEntity placed = NewGolem("Placed", new Vector2Int(0, 0), Facing.North, new Vector3(0.2f, 0f, 0f));
            interactor.RefreshInteractables();

            Assert.IsTrue(interactor.RotateKey());
            Assert.AreEqual(Facing.East, carried.Facing, "R turns what you are holding");
            Assert.AreEqual(Facing.North, placed.Facing, "and leaves the golem on the floor alone");
            Assert.IsTrue(carried.IsHeld, "turning it does not set it down");
        }

        [Test]
        public void TurningACarriedGolem_DoesNotPutItBackOnABoiler()
        {
            var steam = new GolemFactory.Steam.SteamNetwork();
            GolemEntity golem = new GolemEntity();
            golem.Configure("G1", null);
            golem.ConfigureSteam(steam);
            golem.SetPlacement(new Vector2Int(0, 0), Facing.North);
            Assert.IsTrue(steam.HasConsumer("G1"), "precondition: standing, it draws steam");

            golem.SetHeld(true);
            Assert.IsFalse(steam.HasConsumer("G1"), "precondition: carried, it does not");

            golem.SetPlacement(golem.Cell, Facing.East); // what R does to a carried golem
            Assert.IsFalse(steam.HasConsumer("G1"),
                "a carried golem held one of a boiler's 8 slots after being turned");

            golem.SetPlacement(new Vector2Int(3, 0), Facing.East);
            golem.SetHeld(false);
            Assert.IsTrue(steam.HasConsumer("G1"), "set down, it draws steam again");
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
            var endpoints = new SpatialEndpointRegistry();
            var source = new GolemFactory.Economy.StorageBuffer("Source");
            source.Deposit(GolemFactory.Economy.ItemType.Scrap, 5);
            var destination = new GolemFactory.Economy.StorageBuffer("Dest");
            endpoints.Register(new Vector2Int(0, -1), new StorageBufferEndpoint(source));
            endpoints.Register(new Vector2Int(0, 1), new StorageBufferEndpoint(destination));

            GolemEntity golem = new GolemEntity();
            var logicCore = new GolemFactory.PunchCards.LogicCoreDefinition();
            logicCore.triggerType = GolemFactory.PunchCards.TriggerType.AlwaysOn;
            var step = new GolemFactory.PunchCards.AppendageActionDefinition();
            step.actionType = GolemFactory.PunchCards.AppendageActionType.Haul;
            golem.Program.logicCore = logicCore;
            golem.Program.appendages.Add(step);
            golem.ConfigureSpatial(endpoints, Vector2Int.zero, Facing.North);

            golem.SetHeld(true);
            for (int tick = 0; tick < 5; tick++)
            {
                golem.Tick(tick);
            }
            // Asserts against the SOURCE, not the destination, because a spatially placed Haul
            // now pulls into the golem's own input stock rather than straight through to the
            // tile in front (progression-design section 2) -- the destination would stay empty
            // for several more ticks even on a golem that is running perfectly.
            Assert.AreEqual(5, source.GetQuantity(GolemFactory.Economy.ItemType.Scrap),
                "a golem being carried kept hauling");

            golem.SetHeld(false);
            golem.Tick(5);
            Assert.AreEqual(4, source.GetQuantity(GolemFactory.Economy.ItemType.Scrap),
                "it did not resume once set down");
            Assert.AreEqual(1, golem.Inventory.GetInput(GolemFactory.Economy.ItemType.Scrap));
        }
    }
}
