using System.Linq;
using GolemFactory.Buildings;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Events;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.World;
using NUnit.Framework;
using static GolemFactory.Tests.EditMode.CrateWorld;
using Vector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// Phase 1's "Done when" (docs/roadmap.md): <i>a badly laid-out factory runs visibly worse
    /// than a good one, and belts get built because a line needs them.</i> Each case composes the
    /// real Sandbox, builds the same golems through the station, powers them from boilers loaded
    /// by hand, and runs the clock for five minutes of game time.
    ///
    /// <para>
    /// The line: a supply crate S (topped up every tick, standing in for whatever feeds it, since
    /// a stall's stock is finite), a feeder Scavenger F hauling Scrap out of S into the crate in
    /// front of it, and a converter Presser C running Scrap Reclamation (1 Scrap, 1 Iron Plate)
    /// from the crate behind it into an output crate O. Production is what C pushed into O.
    /// </para>
    /// </summary>
    public class LayoutMattersTests
    {
        private const int RunTicks = 3000;       // five minutes at 10 ticks a second
        private const int CollectEvery = 600;    // the player empties the output once a minute

        // Clear of the starter bench (0,3) and station (3,-1), inside the starting floor.
        private static readonly Vector2Int Origin = new Vector2Int(-9, 4);

        private static Vector2Int At(int dx, int dy) => new Vector2Int(Origin.x + dx, Origin.y + dy);

        private sealed class Line
        {
            public SandboxWorld World;
            public ICrate Supply;
            public ICrate FeederOut;     // the crate F pushes into (null when F feeds a belt)
            public ICrate ConverterIn;
            public ICrate ConverterOut;
            public GolemEntity Feeder;
            public GolemEntity Converter;
            public GolemEntity Unloader;
        }

        // --- Fixtures ------------------------------------------------------------------------

        private static void GiveCost(SandboxWorld world, System.Collections.Generic.IEnumerable<RecipeIngredient> cost)
        {
            foreach (RecipeIngredient c in cost)
            {
                Give(world, c.itemType, c.quantity);
            }
        }

        private static PlaceableBuilding PlacePaid(SandboxWorld world, string key, Vector2Int cell, Facing facing = Facing.North)
        {
            PlaceableBuilding prefab = world.Placeables.Single(p => p.Key == key).Prefab;
            GiveCost(world, prefab.Cost);
            world.Build.SetActivePrefab(prefab);
            while (world.Build.PlacementFacing != facing)
            {
                world.Build.RotatePlacement();
            }
            world.Build.PlaceOrRemove(cell);
            world.Build.CancelPlacement();
            return world.Build.Buildings.Single(b => !b.IsRemoved && b.Cell == cell);
        }

        private static void PlaceBoiler(SandboxWorld world, Vector2Int cell)
        {
            PlaceableBoiler boiler = PlacePaid(world, "BoilerPrefab", cell).GetPart<PlaceableBoiler>();
            Give(world, ItemType.Coke, 100);
            Assert.IsTrue(world.Interactor.TryRefuelBoiler(boiler), world.Interactor.LastStatusMessage);
        }

        private static GolemEntity BuildGolem(SandboxWorld world, string chassisName, Vector2Int cell, Facing facing,
            params string[] cards)
        {
            ChassisDefinition chassis = world.Definitions.Chassis[chassisName];
            GiveCost(world, chassis.cost);
            Assert.IsTrue(world.StarterStation.TryConstructGolem(chassis, out GolemEntity golem));
            golem.Program.logicCore = world.Definitions.LogicCores["AlwaysOnCore"];
            foreach (string card in cards)
            {
                Assert.IsTrue(golem.Program.TryAddAppendage(world.Definitions.Appendages[card]), card);
            }
            golem.SetPlacement(cell, facing);
            return golem;
        }

        private static GolemEntity Feeder(SandboxWorld world, Vector2Int cell, Facing facing) =>
            BuildGolem(world, "ClockworkScavenger", cell, facing, "HaulScrap", "PushOutput");

        private static GolemEntity Converter(SandboxWorld world, Vector2Int cell) =>
            BuildGolem(world, "BrassPresser", cell, Facing.North, "HaulScrap", "AssembleScrapReclamation", "PushOutput");

        private static ICrate DepotAt(SandboxWorld world, Vector2Int cell) =>
            PlacePaid(world, "DepotPrefab", cell).GetPart<ICrate>();

        /// <summary>The good layout: S, F, A, C, O in one column. F's crate is C's crate.</summary>
        private static Line Good()
        {
            var line = new Line { World = Compose() };
            SandboxWorld w = line.World;
            line.Supply = DepotAt(w, At(0, 0));
            line.FeederOut = DepotAt(w, At(0, 2));
            line.ConverterIn = line.FeederOut;
            line.ConverterOut = DepotAt(w, At(0, 4));
            PlaceBoiler(w, At(1, 1));
            PlaceBoiler(w, At(1, 3));
            line.Feeder = Feeder(w, At(0, 1), Facing.North);
            line.Converter = Converter(w, At(0, 3));
            return line;
        }

        /// <summary>
        /// The bad layout: the same F and C, but F's crate A stands six tiles from C's crate B,
        /// and nothing joins them.
        /// </summary>
        private static Line Bad()
        {
            var line = new Line { World = Compose() };
            SandboxWorld w = line.World;
            line.Supply = DepotAt(w, At(0, 0));
            line.FeederOut = DepotAt(w, At(0, 2));
            line.ConverterIn = DepotAt(w, At(6, 2));
            line.ConverterOut = DepotAt(w, At(6, 4));
            PlaceBoiler(w, At(1, 1));
            PlaceBoiler(w, At(7, 3));
            line.Feeder = Feeder(w, At(0, 1), Facing.North);
            line.Converter = Converter(w, At(6, 3));
            return line;
        }

        /// <summary>
        /// The bad layout's two stations, joined by a belt: F pushes onto a run of five belts
        /// heading east, and an unloader Scavenger U at its end hauls into C's crate B.
        /// </summary>
        private static Line Belted()
        {
            var line = new Line { World = Compose() };
            SandboxWorld w = line.World;
            line.Supply = DepotAt(w, At(0, 0));
            line.ConverterIn = DepotAt(w, At(6, 2));
            line.ConverterOut = DepotAt(w, At(6, 4));
            for (int dx = 0; dx <= 4; dx++)
            {
                PlacePaid(w, "BeltPrefab", At(dx, 2), Facing.East);
            }
            PlaceBoiler(w, At(1, 1));
            PlaceBoiler(w, At(5, 1));
            PlaceBoiler(w, At(7, 3));
            line.Feeder = Feeder(w, At(0, 1), Facing.North);
            line.Unloader = Feeder(w, At(5, 2), Facing.East);
            line.Converter = Converter(w, At(6, 3));
            return line;
        }

        /// <summary>
        /// Runs <paramref name="line"/> for <see cref="RunTicks"/> and returns the Iron Plate C made.
        /// <paramref name="collect"/>: the player empties O once a minute. <paramref name="ferry"/>:
        /// the player also carries A's Scrap to B once a minute, the hand-truck between stations.
        /// </summary>
        private static int Run(Line line, bool collect = true, bool ferry = false)
        {
            SandboxWorld w = line.World;
            StorageBuffer pack = w.Wallet.Pack;
            pack.ClearContents(); // what was left from building, spent
            w.Clock.Play();
            long start = w.Clock.CurrentTick;
            long nextVisit = start + CollectEvery;
            while (w.Clock.CurrentTick - start < RunTicks)
            {
                line.Supply.Buffer.Deposit(ItemType.Scrap, line.Supply.Buffer.RoomFor(ItemType.Scrap));
                w.Advance(1f / w.Clock.TicksPerSecond);
                if (w.Clock.CurrentTick >= nextVisit)
                {
                    nextVisit += CollectEvery;
                    if (collect)
                    {
                        CrateTransfer.Take(line.ConverterOut, pack, ItemType.IronPlate, int.MaxValue);
                        pack.ClearContents(); // carried off and spent
                    }
                    if (ferry)
                    {
                        CrateTransfer.Take(line.FeederOut, pack, ItemType.Scrap, int.MaxValue);
                        CrateTransfer.Put(pack, line.ConverterIn, ItemType.Scrap, int.MaxValue);
                        pack.ClearContents();
                    }
                }
            }
            return line.ConverterOut.Buffer.TotalDeposited(ItemType.IronPlate);
        }

        // --- Layout ----------------------------------------------------------------------------

        [Test]
        public void AGoodLayout_OutproducesABadOne_WithTheSameGolemsAndRecipe()
        {
            Line good = Good();
            int goodPlates = Run(good);
            Line bad = Bad();
            int badPlates = Run(bad);
            Line ferried = Bad();
            int ferriedPlates = Run(ferried, ferry: true);
            TestContext.Out.WriteLine($"five minutes: good {goodPlates}, bad {badPlates}, bad ferried by hand once a minute {ferriedPlates}");

            Assert.GreaterOrEqual(goodPlates, 60, "a straight line keeps its converter busy");
            Assert.AreEqual(0, badPlates, "no golem reaches a crate it isn't standing beside");
            // Measured 103 / 0 / 82. A player who carries A's Scrap to B once a minute recovers
            // most of it (the converter, at 1 plate per ~29 ticks, is the line's bottleneck), but
            // pays for it in walking, and still falls short of the line.
            Assert.Less(ferriedPlates * 10, goodPlates * 9, "hand-carrying between the stations does worse than the line");

            // And the bad layout says why, on both golems, naming the crates.
            Assert.AreEqual(StallReason.BufferEmpty, bad.Converter.StallReason);
            Assert.AreEqual(bad.ConverterIn.CrateId, bad.Converter.StallResourceId, "C: no input in its crate");
            Assert.AreEqual(StallReason.BeltFull, bad.Feeder.StallReason);
            Assert.AreEqual(bad.FeederOut.CrateId, bad.Feeder.StallResourceId, "F: its crate is full");
            Assert.AreEqual(50, bad.FeederOut.Buffer.GetQuantity(ItemType.Scrap));
        }

        [Test]
        public void AnOutputCrateNobodyEmpties_StopsTheLineAtFifty_AndSaysWhichCrate()
        {
            int collected = Run(Good());
            Line neglected = Good();
            int stopped = Run(neglected, collect: false);
            TestContext.Out.WriteLine($"five minutes: output collected once a minute {collected}, never collected {stopped}");

            Assert.AreEqual(50, stopped, "the crate's cap");
            // Measured 103 / 50.
            Assert.Greater(collected * 2, stopped * 3, "a line whose output is carried off runs on");
            Assert.AreEqual(StallReason.BeltFull, neglected.Converter.StallReason);
            Assert.AreEqual(neglected.ConverterOut.CrateId, neglected.Converter.StallResourceId);
        }

        // --- Belts get built because a line needs them -------------------------------------------

        [Test]
        public void TwoSeparatedStations_ProduceNothingWithoutABelt_AndRunWithOne()
        {
            int without = Run(Bad());
            Line belted = Belted();
            int with = Run(belted);
            TestContext.Out.WriteLine($"five minutes: separated stations {without}, joined by a belt and an unloader {with}");

            // Measured 0 / 101.
            Assert.AreEqual(0, without);
            Assert.GreaterOrEqual(with, 60, "the belt carries the feeder's Scrap to the converter");
        }
    }
}
