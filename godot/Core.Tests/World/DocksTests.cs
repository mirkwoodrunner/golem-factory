using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Diagnostics;
using GolemFactory.Economy;
using GolemFactory.Player;
using GolemFactory.PunchCards;
using GolemFactory.Supply;
using GolemFactory.Tests.EditMode;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Tests.World
{
    /// <summary>docs/region-design.md: the Docks, east of the street, open from the first Zeppelin.</summary>
    public class DocksTests
    {
        private SandboxWorld _world;

        [SetUp]
        public void SetUp() => _world = CrateWorld.Compose();

        [TearDown]
        public void TearDown() => _world.Dispose();

        private static readonly Vector2Int LaneCell = new Vector2Int(Docks.LaneWest + 1, -16);
        private static readonly Vector2Int FloorCell = new Vector2Int(Docks.West + 6, -10);

        private void BuildAZeppelin() => _world.TechTree.Ledger.RecordChassis(SandboxWorld.ZeppelinChassis);

        private Vector3 WalkTo(Vector3 from, Vector3 to)
        {
            var walker = new PlayerWalker(10f) { Position = from };
            walker.SetFloorBounds(new GridCoordinateConverter(new Vector2(1f, 1f)), _world.Bounds.HalfExtent, FloorLayout.StreetDepth, _world.Bounds.NorthExtent);
            walker.DocksNorth = () => _world.Bounds.DocksNorth;
            for (int i = 0; i < 2000; i++)
            {
                Vector3 step = to - walker.Position;
                if (step.magnitude < 0.05f)
                {
                    break;
                }
                walker.MoveBy(new Vector2(step.x, step.y).normalized, 0.05f);
            }
            return walker.Position;
        }

        [Test]
        public void TheLayout_IsEastOfTheStreet_WithAQuayOfEight()
        {
            Assert.AreEqual(FloorLayout.StreetHalfExtent + 1, Docks.LaneWest, "the lane starts where the street ends");
            Assert.AreEqual(8, Docks.QuaySpots().Count());
            Assert.IsTrue(Docks.QuaySpots().All(c => Docks.InFloor(c, Docks.StartNorth)), "the quay is the floor's south row");
            Assert.IsFalse(Docks.Cells(Docks.StartNorth).Any(c => FloorLayout.IsInsideWorld(c)), "no cell is both the Docks and the old world");
            Assert.AreEqual(Docks.Closed, _world.Bounds.DocksNorth);
            Assert.AreEqual(8, SandboxLayout.Walls(FloorLayout.DefaultNorthExtent, Docks.Closed).Count(p => p.Name.StartsWith("DocksLaneEnd_")), "shut, the lane ends in a wall");
            Assert.AreEqual(0, SandboxLayout.Walls(FloorLayout.DefaultNorthExtent, Docks.StartNorth).Count(p => p.Name.StartsWith("DocksLaneEnd_")), "open, it runs on");
        }

        [Test]
        public void BeforeTheZeppelin_TheDocksAreNotGround()
        {
            _world.Advance(0.1f);
            Assert.IsFalse(_world.Build.IsCellBuildable(LaneCell));
            Assert.IsFalse(_world.Build.IsCellBuildable(FloorCell));
            Vector3 stopped = WalkTo(new Vector3(30f, -16f, 0f), new Vector3(FloorCell.x, FloorCell.y, 0f));
            Assert.LessOrEqual(stopped.x, FloorLayout.StreetHalfExtent + 0.01f, "the street's end is the end");

            CartSpot quay = _world.Supply.SpotAt(Docks.QuaySpots().First());
            CrateWorld.Give(_world, ItemType.Scrap, 100);
            SupplyResult refused = _world.Supply.Add(quay, ItemType.Scrap);
            Assert.IsFalse(refused.Done);
            Assert.AreEqual("Roped off until the Docks open.", refused.Message);
            Assert.IsFalse(_world.AssemblyLineBoard.Rows().Any(r => r.Kind == AssemblyLineRowKind.Docks), "no Docks row yet");
        }

        [Test]
        public void TheFirstZeppelin_OpensThem_ToWalkBuildAndAddBarges()
        {
            BuildAZeppelin();
            Assert.AreEqual(Docks.StartNorth, _world.Bounds.DocksNorth);
            Assert.IsTrue(_world.Build.IsCellBuildable(LaneCell), "a belt can run down the lane");
            Assert.IsTrue(_world.Build.IsCellBuildable(FloorCell));
            Assert.IsFalse(_world.Build.IsCellBuildable(new Vector2Int(Docks.West + 6, Docks.StartNorth + 1)), "north of the bought floor is not");

            Vector3 arrived = WalkTo(new Vector3(30f, -16f, 0f), new Vector3(FloorCell.x, FloorCell.y, 0f));
            Assert.GreaterOrEqual(arrived.x, Docks.West, "walked down the lane onto the Docks' floor");
            Assert.Less((arrived - new Vector3(FloorCell.x, FloorCell.y, 0f)).magnitude, 0.6f, "within a step of where it was going");

            CartSpot quay = _world.Supply.SpotAt(Docks.QuaySpots().First());
            Assert.IsTrue(_world.GrantCart(quay.Cell, ItemType.Coal).Done, "a barge on the quay");
            Assert.IsTrue(CrateWorld.PlaceDepot(_world, FloorCell) != null, "and a depot on the floor");
            Assert.AreEqual("The Docks", Docks.RegionName(FloorCell, _world.Bounds.DocksNorth), "the HUD names the region on its floor");
            Assert.AreEqual("", Docks.RegionName(LaneCell, _world.Bounds.DocksNorth), "and not in the lane");
        }

        [Test]
        public void TheFloor_IsBoughtNorth_TwoRowsAtATime_ToItsEdge()
        {
            BuildAZeppelin();
            AssemblyLineRow row = _world.AssemblyLineBoard.Rows().Single(r => r.Kind == AssemblyLineRowKind.Docks);
            StringAssert.Contains("+2 rows north", row.Text);
            int purchases = 0;
            while (_world.DocksExpansion.CanExpand)
            {
                EndgameLoad.Grant(_world, _world.DocksExpansion.NextCost());
                int before = _world.Bounds.DocksNorth;
                Assert.IsTrue(_world.AssemblyLineBoard.ExtendDocks(), _world.AssemblyLineBoard.Status);
                Assert.AreEqual(before + 2, _world.Bounds.DocksNorth);
                purchases++;
            }
            Assert.AreEqual(6, purchases);
            Assert.AreEqual(Docks.MaxNorth, _world.Bounds.DocksNorth);
            StringAssert.EndsWith("fully extended", _world.AssemblyLineBoard.Rows().Single(r => r.Kind == AssemblyLineRowKind.Docks).Text);
            EndgameLoad.Grant(_world, _world.DocksExpansion.NextCost());
            Assert.IsFalse(_world.DocksExpansion.TryPurchase(), "the land has run out");
        }

        [Test]
        public void ARowCostsItsPrice_AndAShortWalletIsRefused()
        {
            BuildAZeppelin();
            var price = _world.DocksExpansion.NextCost();
            Assert.IsFalse(_world.DocksExpansion.TryPurchase());
            StringAssert.StartsWith("Need ", _world.DocksExpansion.LastStatusMessage.Replace("Upgrade your pack", "Need "));
            EndgameLoad.Grant(_world, price);
            int plate = CrateWorld.Pack(_world, price[0].itemType);
            Assert.IsTrue(_world.DocksExpansion.TryPurchase());
            Assert.AreEqual(plate - price[0].quantity, CrateWorld.Pack(_world, price[0].itemType), "paid through the wallet");
        }

        [Test]
        public void ASave_KeepsTheRowsBought()
        {
            string path = Path.Combine(Path.GetTempPath(), "golem-factory-docks-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                BuildAZeppelin();
                EndgameLoad.Grant(_world, _world.DocksExpansion.NextCost());
                Assert.IsTrue(_world.DocksExpansion.TryPurchase());
                _world.SaveTo(path);
                SandboxWorld loaded = CrateWorld.Compose();
                try
                {
                    loaded.LoadFrom(path);
                    Assert.AreEqual(Docks.StartNorth + 2, loaded.Bounds.DocksNorth, "open, with its row");
                }
                finally
                {
                    loaded.Dispose();
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void TheRows_CostMoreEachTime_AndLessACellThanTheWorkshopsFifth()
        {
            // The user's call (2026-11-08): kept cheaper than the workshop's last rows, since the
            // walk is their cost, but rising, so the last row is the dearest (region-design §7).
            var raw = GolemFactory.Contracts.ContractGenerator.RawCosts(_world.Definitions.Recipes.Values);
            double Raw(List<RecipeIngredient> price) => price.Sum(p => raw[p.itemType] * p.quantity);
            List<List<RecipeIngredient>> rows = _world.Setup.docks.purchases;
            double fifth = Raw(_world.Setup.floorExpansion.purchases[4]) / (2 * 25);
            for (int i = 0; i < rows.Count; i++)
            {
                double perCell = Raw(rows[i]) / (_world.Setup.docks.rowsPerPurchase * (Docks.East - Docks.West + 1));
                Assert.Less(perCell, fifth, $"row {i + 1}: {perCell:F1} a cell against the workshop's fifth {fifth:F1}");
                if (i > 0)
                {
                    Assert.Greater(Raw(rows[i]), Raw(rows[i - 1]), $"row {i + 1} costs more than row {i}");
                }
            }
        }

        [Test]
        public void ShutTheGateSaysWhy_AndTheCityNotesTheMomentTheyOpen()
        {
            var atTheGate = new Vector2Int(FloorLayout.StreetHalfExtent, -16);
            Assert.AreEqual(Docks.ShutLine, Docks.RegionName(atTheGate, _world.Bounds.DocksNorth));
            Assert.AreEqual("", Docks.RegionName(new Vector2Int(0, -16), _world.Bounds.DocksNorth), "not down the street");
            _world.Advance(0.1f);
            Assert.IsFalse(_world.Alerts.Log.Entries.Any(e => e.IsNote && e.Text == Docks.OpenedNote));

            BuildAZeppelin();
            _world.Advance(0.1f);
            Assert.AreEqual("", Docks.RegionName(atTheGate, _world.Bounds.DocksNorth), "open: nothing to explain");
            Assert.AreEqual(1, _world.Alerts.Log.Entries.Count(e => e.IsNote && e.Text == Docks.OpenedNote), "noted once");
            _world.Advance(0.1f);
            Assert.AreEqual(1, _world.Alerts.Log.Entries.Count(e => e.IsNote && e.Text == Docks.OpenedNote), "and only once");
        }

        [Test]
        public void ALoad_ThatFindsThemOpen_NotesNothing()
        {
            SandboxWorld open = CreativeTests.Compose(creative: true);
            try
            {
                open.Advance(0.1f);
                Assert.IsFalse(open.Alerts.Log.Entries.Any(e => e.Text == Docks.OpenedNote));
            }
            finally
            {
                open.Dispose();
            }
        }

        [Test]
        public void AShutQuaySpot_IsNoTarget_AndAnOpenOneIs()
        {
            Vector2Int quay = Docks.QuaySpots().First();
            _world.Interactor.Position = new Vector3(quay.x, quay.y + 1, 0f);
            _world.Advance(0.1f);
            _world.Interactor.RefreshAffordance(); // what the player node does each frame
            InteractionPick shut = _world.Interactor.CurrentPick;
            Assert.IsFalse(shut.Kind == InteractionKind.Supply && shut.Distance < 2f, "the shut quay spot underfoot is no target (the nearest is a street spot, far off)");
            BuildAZeppelin();
            _world.Advance(0.1f);
            _world.Interactor.RefreshAffordance();
            Assert.AreEqual(InteractionKind.Supply, _world.Interactor.CurrentPick.Kind, _world.Interactor.CurrentPrompt);
            Assert.Less(_world.Interactor.CurrentPick.Distance, 2f, "the quay spot underfoot, now open");
        }

        [Test]
        public void CreativeOpensThem_FromTheStart()
        {
            SandboxWorld creative = CreativeTests.Compose(creative: true);
            try
            {
                Assert.AreEqual(Docks.StartNorth, creative.Bounds.DocksNorth);
                Assert.IsTrue(creative.DocksExpansion.TryPurchase(), "free");
            }
            finally
            {
                creative.Dispose();
            }
        }
    }
}
