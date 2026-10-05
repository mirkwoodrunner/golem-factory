using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Belts;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.Player;
using GolemFactory.Steam;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    // Click-and-drag laying a RUN, driven through BuildModeController.ExtendDrag rather than
    // through a simulated pointer -- the same split every other BuildModeController suite makes,
    // and the reason the drag path itself is a pure static (BuildDragPathTests covers the math).
    //
    // PlayMode rather than EditMode because these are real components: PlaceableBelt and
    // PlaceableSteamPipe keep their live rosters in OnEnable, which -- with no [ExecuteAlways]
    // anywhere in this project -- runs only in Play mode.
    public class BuildDragRunTests
    {
        private static readonly Vector2 CellSize = new Vector2(1f, 1f);



        [Test]
        public void ADragLaysEveryCellItCrosses()
        {
            Rig rig = BuildRig(dragPlaceable: true);

            rig.Controller.PlaceOrRemove(new Vector2Int(0, 0));
            rig.Controller.BeginDrag(new Vector2Int(0, 0));
            rig.Controller.ExtendDrag(new Vector2Int(3, 0));

            for (int x = 0; x <= 3; x++)
            {
                Assert.IsTrue(rig.Grid.IsOccupied(new Vector2Int(x, 0)), "cell " + x);
            }
        }

        [Test]
        public void ADraggedRunPointsAlongItself()
        {
            // The half that matters. Every belt but the last only learns its facing once the
            // drag reaches the NEXT cell, so without re-facing, a run laid eastward would be a
            // line of belts all pointing whichever way R last happened to leave the ghost.
            Rig rig = BuildRig(dragPlaceable: true);
            rig.Controller.PlaceOrRemove(new Vector2Int(0, 0));
            rig.Controller.BeginDrag(new Vector2Int(0, 0));
            rig.Controller.ExtendDrag(new Vector2Int(2, 0));

            Assert.AreEqual(Facing.East, FacingAt(rig, new Vector2Int(0, 0)),
                "the anchor was never turned to face the run it started");
            Assert.AreEqual(Facing.East, FacingAt(rig, new Vector2Int(1, 0)));
            Assert.AreEqual(Facing.East, FacingAt(rig, new Vector2Int(2, 0)),
                "the last cell keeps the direction the run arrived in");
        }

        [Test]
        public void ADragThatTurns_TurnsTheCellItTurnedOn()
        {
            Rig rig = BuildRig(dragPlaceable: true);

            rig.Controller.PlaceOrRemove(new Vector2Int(0, 0));
            rig.Controller.BeginDrag(new Vector2Int(0, 0));
            rig.Controller.ExtendDrag(new Vector2Int(2, 2));

            // BuildDragPath walks x first, so the bend is at (2,0).
            Assert.AreEqual(Facing.East, FacingAt(rig, new Vector2Int(1, 0)));
            Assert.AreEqual(Facing.North, FacingAt(rig, new Vector2Int(2, 0)),
                "the corner cell must be re-faced when the run turns off it");
            Assert.AreEqual(Facing.North, FacingAt(rig, new Vector2Int(2, 2)));
        }

        [Test]
        public void ADragStopsAtAnObstruction_RatherThanSkippingIt()
        {
            // A run with a hole in it leaves the belt before the hole pointing at nothing, which
            // is a worse outcome than a run that visibly stopped short.
            Rig rig = BuildRig(dragPlaceable: true);
            rig.Grid.TryOccupy(new Vector2Int(2, 0), new object());

            rig.Controller.PlaceOrRemove(new Vector2Int(0, 0));
            rig.Controller.BeginDrag(new Vector2Int(0, 0));
            rig.Controller.ExtendDrag(new Vector2Int(4, 0));

            Assert.IsTrue(rig.Grid.IsOccupied(new Vector2Int(1, 0)));
            Assert.IsFalse(rig.Grid.IsOccupied(new Vector2Int(3, 0)), "the run jumped the wall");
            Assert.IsFalse(rig.Controller.IsDragging);
        }

        [Test]
        public void ADragAcrossAnExistingBuilding_DoesNotDemolishIt()
        {
            // PlaceOrRemove treats a click on an occupied cell as a demolition. A drag must not:
            // running a belt line past your own depot would otherwise eat the depot.
            Rig rig = BuildRig(dragPlaceable: true);
            var depot = new PlaceableBuilding { name = "Depot" };
            depot.Cell = new Vector2Int(2, 0);
            rig.Grid.TryOccupy(new Vector2Int(2, 0), depot);

            rig.Controller.PlaceOrRemove(new Vector2Int(0, 0));
            rig.Controller.BeginDrag(new Vector2Int(0, 0));
            rig.Controller.ExtendDrag(new Vector2Int(4, 0));

            Assert.IsTrue(rig.Grid.IsOccupied(new Vector2Int(2, 0)));
            Assert.IsTrue(!depot.IsRemoved, "the drag demolished a building it merely crossed");
        }

        [Test]
        public void APlaceableThatIsNotDragPlaceable_LaysOneAndOnlyOne()
        {
            // The flag fails safe: anything nobody has thought about places one at a time,
            // exactly as every placeable did before drag existed.
            Rig rig = BuildRig(dragPlaceable: false);

            rig.Controller.PlaceOrRemove(new Vector2Int(0, 0));
            rig.Controller.BeginDrag(new Vector2Int(0, 0));
            rig.Controller.ExtendDrag(new Vector2Int(3, 0));

            Assert.IsFalse(rig.Controller.IsDragging);
            Assert.IsFalse(rig.Grid.IsOccupied(new Vector2Int(1, 0)));
        }

        [Test]
        public void ADragThatWigglesBackOverItself_KeepsGoing()
        {
            Rig rig = BuildRig(dragPlaceable: true);

            rig.Controller.PlaceOrRemove(new Vector2Int(0, 0));
            rig.Controller.BeginDrag(new Vector2Int(0, 0));
            rig.Controller.ExtendDrag(new Vector2Int(2, 0));
            rig.Controller.ExtendDrag(new Vector2Int(1, 0));   // back over its own run
            rig.Controller.ExtendDrag(new Vector2Int(1, 2));   // and off in a new direction

            Assert.IsTrue(rig.Controller.IsDragging, "crossing its own run must not end the drag");
            Assert.IsTrue(rig.Grid.IsOccupied(new Vector2Int(1, 2)));
        }

        // --- The wrecking bar's run --------------------------------------------------------

        [Test]
        public void ADemolishDragClearsEveryBuildingItCrosses()
        {
            Rig rig = BuildRig(dragPlaceable: true);
            for (int x = 0; x <= 3; x++)
            {
                rig.Controller.PlaceOrRemove(new Vector2Int(x, 0));
            }

            rig.Controller.EnterDemolishMode();
            rig.Controller.PlaceOrRemove(new Vector2Int(0, 0));
            rig.Controller.BeginDrag(new Vector2Int(0, 0));
            rig.Controller.ExtendDrag(new Vector2Int(3, 0));

            for (int x = 0; x <= 3; x++)
            {
                Assert.IsFalse(rig.Grid.IsOccupied(new Vector2Int(x, 0)), "cell " + x);
            }
        }

        [Test]
        public void ADemolishDragCrossesEmptyFloorAndKeepsGoing()
        {
            // The deliberate asymmetry with a placement run, which stops at the first cell it
            // cannot use. A demolition has no continuity to preserve, and an L-shaped sweep
            // crosses empty floor as a matter of course -- stopping on the first gap would make
            // the tool useless for the one gesture it exists for.
            Rig rig = BuildRig(dragPlaceable: true);
            rig.Controller.PlaceOrRemove(new Vector2Int(0, 0));
            rig.Controller.PlaceOrRemove(new Vector2Int(3, 0));   // a gap at 1 and 2

            rig.Controller.EnterDemolishMode();
            rig.Controller.BeginDrag(new Vector2Int(-1, 0));
            rig.Controller.ExtendDrag(new Vector2Int(3, 0));

            Assert.IsTrue(rig.Controller.IsDragging, "a gap must not end a demolition sweep");
            Assert.IsFalse(rig.Grid.IsOccupied(new Vector2Int(0, 0)));
            Assert.IsFalse(rig.Grid.IsOccupied(new Vector2Int(3, 0)),
                "the sweep stopped at the gap instead of crossing it");
        }

        [Test]
        public void ADemolishDragRefundsWhatThePlayerPaidFor()
        {
            // Same DemolishBuilding the click calls, so the settled full-refund rule reaches the
            // sweep unchanged -- which is also what makes an over-long drag cost nothing.
            Rig rig = BuildRig(dragPlaceable: true);
            rig.Controller.ActivePrefab.ConfigureCost(5, 0);
            var stockpile = new StorageBufferRegistry();
            stockpile.Deposit("FactoryStockpile", ItemType.Scrap, 15);
            rig.Controller.ConfigureEconomy(
                stockpile, "FactoryStockpile", new[] { rig.Controller.ActivePrefab });

            for (int x = 0; x <= 2; x++)
            {
                rig.Controller.PlaceOrRemove(new Vector2Int(x, 0));
            }

            StorageBuffer buffer;
            stockpile.TryGetBuffer("FactoryStockpile", out buffer);
            Assert.AreEqual(0, buffer.GetQuantity(ItemType.Scrap), "precondition: all 15 spent");

            rig.Controller.EnterDemolishMode();
            rig.Controller.BeginDrag(new Vector2Int(-1, 0));
            rig.Controller.ExtendDrag(new Vector2Int(2, 0));

            Assert.AreEqual(15, buffer.GetQuantity(ItemType.Scrap),
                "a swept run must pay back in full, exactly as three clicks would");
        }

        [Test]
        public void ADemolishDragLeavesGolemsAlone()
        {
            // The one place the drag is NARROWER than the click it repeats. A swept building is
            // an undo away -- re-place it and it is identical. A dismantled golem hands back its
            // chassis and cargo and loses its PROGRAM, so it still costs one deliberate click.
            Rig rig = BuildRig(dragPlaceable: true);
            var dismantler = new CountingDismantler();
            rig.Controller.ConfigureGolemDismantling(dismantler);

            GolemEntity golem = new GolemEntity();
            // Cell is read-only; ConfigureSpatial is how a golem is told where it stands.
            golem.ConfigureSpatial(null, new Vector2Int(2, 0), Facing.North);
            // Standing in the world: Unity's TryFindGolemAt scanned the scene; Core is handed
            // the roster. Without this the golem is unfindable and the test proves nothing.
            rig.Controller.ConfigureGolemRoster(() => new[] { golem });

            rig.Controller.EnterDemolishMode();
            rig.Controller.BeginDrag(new Vector2Int(0, 0));
            rig.Controller.ExtendDrag(new Vector2Int(4, 0));

            Assert.AreEqual(0, dismantler.Calls, "a sweep asked to dismantle a golem");
            Assert.IsTrue(!golem.IsRemoved, "the sweep destroyed a golem it merely crossed");
        }

        [Test]
        public void AClickStillTakesAGolem()
        {
            // The counterpart of the test above: narrowing the DRAG must not have narrowed the
            // click, which is the only way a golem can be removed at all.
            Rig rig = BuildRig(dragPlaceable: true);
            var dismantler = new CountingDismantler();
            rig.Controller.ConfigureGolemDismantling(dismantler);

            GolemEntity golem = new GolemEntity();
            // Cell is read-only; ConfigureSpatial is how a golem is told where it stands.
            golem.ConfigureSpatial(null, new Vector2Int(2, 0), Facing.North);
            // Standing in the world: Unity's TryFindGolemAt scanned the scene; Core is handed
            // the roster. Without this the golem is unfindable and the test proves nothing.
            rig.Controller.ConfigureGolemRoster(() => new[] { golem });

            rig.Controller.EnterDemolishMode();
            rig.Controller.PlaceOrRemove(new Vector2Int(2, 0));

            Assert.AreEqual(1, dismantler.Calls);
        }

        [Test]
        public void ADragAfterSwitchingOffDemolish_LaysRatherThanSweeps()
        {
            // The drag half of the same bug. BeginDrag asks IsDemolishActive first, so a stale
            // wrecking bar turned every subsequent drag into a demolition sweep.
            Rig rig = BuildRig(dragPlaceable: true);
            PlaceableBuilding prefab = rig.Controller.ActivePrefab;

            rig.Controller.EnterDemolishMode();
            rig.Controller.SetActivePrefab(prefab);

            rig.Controller.PlaceOrRemove(new Vector2Int(0, 0));
            rig.Controller.BeginDrag(new Vector2Int(0, 0));
            rig.Controller.ExtendDrag(new Vector2Int(2, 0));

            for (int x = 0; x <= 2; x++)
            {
                Assert.IsTrue(rig.Grid.IsOccupied(new Vector2Int(x, 0)),
                    "cell " + x + " -- the drag swept instead of laying");
            }
        }

        // Counts what it is asked and dismantles nothing, so a test can tell "the sweep did not
        // ask" apart from "the sweep asked and the station refused" -- two very different bugs.
        private sealed class CountingDismantler : GolemFactory.Buildings.IGolemDismantler
        {
            public int Calls;

            public bool TryDismantleGolem(
                GolemEntity golem,
                out System.Collections.Generic.IReadOnlyList<GolemFactory.PunchCards.RecipeIngredient> refunded,
                out string refusalReason)
            {
                Calls++;
                refunded = new System.Collections.Generic.List<GolemFactory.PunchCards.RecipeIngredient>();
                refusalReason = "";
                return true;
            }
        }

        // --- Rig ---------------------------------------------------------------------------

        private struct Rig
        {
            public BuildModeController Controller;
            public GridMap Grid;
        }

        private static Facing FacingAt(Rig rig, Vector2Int cell)
        {
            object occupant;
            Assert.IsTrue(rig.Grid.TryGetOccupant(cell, out occupant), "nothing at " + cell);
            return ((PlaceableBuilding)occupant).Facing;
        }

        private Rig BuildRig(bool dragPlaceable)
        {

            var grid = new GridMap();

            var prefab = new PlaceableBuilding { name = "RunPrefab" };
            prefab.ConfigureDragPlaceable(dragPlaceable);

            var controller = new BuildModeController();
            controller.Configure(grid, prefab);

            return new Rig { Controller = controller, Grid = grid };
        }
    }

    // The corner and junction pieces, driven through the real BeltNetwork and SteamNetwork.
    //
    // These check the PICTURE, which is the one thing the shape rules' unit tests cannot: that
    // the components actually ask the networks about the right cells and land on the right piece.
    public class ConnectedShapeTests
    {


        [Test]
        public void APipeBetweenTwoOthers_DrawsAStraightRun()
        {
            SteamNetwork steam = NewSteamHolder();
            PlaceableSteamPipe pipe = NewPipe(steam, new Vector2Int(1, 0));
            steam.AddPipe(new Vector2Int(0, 0));
            steam.AddPipe(new Vector2Int(2, 0));

            pipe.RefreshShape(steam, Facing.North); // the building's facing: Unity's default, read via GetComponent

            Assert.AreEqual(PipeShape.Straight, pipe.Shape);
            Assert.AreEqual(Facing.East, pipe.ShapeOrientation);
        }

        [Test]
        public void APipeWithNeighboursOnThreeSides_DrawsATee()
        {
            SteamNetwork steam = NewSteamHolder();
            PlaceableSteamPipe pipe = NewPipe(steam, Vector2Int.zero);
            steam.AddPipe(new Vector2Int(0, 1));
            steam.AddPipe(new Vector2Int(0, -1));
            steam.AddPipe(new Vector2Int(1, 0));

            pipe.RefreshShape(steam, Facing.North); // the building's facing: Unity's default, read via GetComponent

            Assert.AreEqual(PipeShape.Tee, pipe.Shape);
            Assert.AreEqual(Facing.East, pipe.ShapeOrientation, "the stem points away from the gap");
        }

        [Test]
        public void APipeAgainstABoiler_JoinsOnToIt()
        {
            // A run that visibly stopped one cell short of the thing feeding it would read as
            // broken while being, to the flood fill, perfectly connected.
            SteamNetwork steam = NewSteamHolder();
            PlaceableSteamPipe pipe = NewPipe(steam, new Vector2Int(1, 0));
            steam.RegisterBoiler("B", new Vector2Int(0, 0), 0);

            pipe.RefreshShape(steam, Facing.North); // the building's facing: Unity's default, read via GetComponent

            Assert.AreEqual(PipeShape.End, pipe.Shape);
            Assert.AreEqual(Facing.West, pipe.ShapeOrientation, "the stub must point at the boiler");
        }

        [Test]
        public void ABeltFedFromItsFlank_DrawsACorner()
        {
            BeltNetwork belts = NewBeltHolder();

            // A belt at (0,0) running north, feeding one at (0,1) that runs east.
            PlacedBelt feeder;
            belts.TryPlace(new Vector2Int(0, 0), Facing.North, out feeder);
            PlacedBelt corner;
            belts.TryPlace(new Vector2Int(0, 1), Facing.East, out corner);

            PlaceableBelt view = NewBelt(new Vector2Int(0, 1), corner, Facing.East);
            view.RefreshShape(belts, new Vector2Int(0, 1));

            // Entered travelling north, leaving east: a right turn.
            Assert.AreEqual(BeltShape.CornerRight, view.Shape);
        }

        [Test]
        public void ABeltFedFromBehind_StaysStraight()
        {
            BeltNetwork belts = NewBeltHolder();
            PlacedBelt feeder;
            belts.TryPlace(new Vector2Int(0, 0), Facing.North, out feeder);
            PlacedBelt next;
            belts.TryPlace(new Vector2Int(0, 1), Facing.North, out next);

            PlaceableBelt view = NewBelt(new Vector2Int(0, 1), next, Facing.North);
            view.RefreshShape(belts, new Vector2Int(0, 1));

            Assert.AreEqual(BeltShape.Straight, view.Shape);
        }

        // --- Rig ---------------------------------------------------------------------------

        // Unity built a GameObject per pipe/belt with a SpriteRenderer, because the shape was
        // only observable through the sprite it picked. The parts now hold Shape as state, so
        // the rig is the parts alone; the assertions above read the same Shape they always did.
        private static SteamNetwork NewSteamHolder() => new SteamNetwork();

        private static BeltNetwork NewBeltHolder()
        {
            var network = new BeltNetwork();
            network.Configure(new ConveyorSystem(), new SpatialEndpointRegistry(), 4);
            return network;
        }

        private static PlaceableSteamPipe NewPipe(SteamNetwork steam, Vector2Int cell)
        {
            var pipe = new PlaceableSteamPipe();
            pipe.RegisterWithSteamNetwork(steam, cell);
            return pipe;
        }

        private static PlaceableBelt NewBelt(Vector2Int cell, PlacedBelt placed, Facing facing)
        {
            var belt = new PlaceableBelt();
            belt.BindSegment(placed.Segment, facing);
            return belt;
        }
    }
}
