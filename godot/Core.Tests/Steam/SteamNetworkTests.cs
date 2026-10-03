using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Steam;

namespace GolemFactory.Tests.EditMode
{
    // Steam power's simulation half (docs/progression-design.md §3.1): who gets steam, who pays
    // for it, and how much. Plain C#, so every one of these runs without a scene.
    //
    // The determinism cases carry the most weight here. §3.1 is silent on all three questions
    // (which 8, which boiler, and how to divide a fractional burn), and an unstable answer to
    // any of them makes the mechanic unplayable rather than merely imprecise.
    public class SteamNetworkTests
    {
        private SteamNetwork _network;

        [SetUp]
        public void SetUp() => _network = new SteamNetwork();

        private static Vector2Int C(int x, int y) => new Vector2Int(x, y);

        /// <summary>Runs the network for n ticks starting at tick 1.</summary>
        private void Run(int ticks, long startTick = 1)
        {
            for (long t = startTick; t < startTick + ticks; t++)
            {
                _network.Tick(t);
            }
        }

        // --- Adjacency and reach --------------------------------------------------------------

        [Test]
        public void AGolemNextToTheBoiler_IsPowered_AndOneDiagonallyAwayIsNot()
        {
            _network.RegisterBoiler("B", C(0, 0), 100);
            _network.RegisterConsumer("Adjacent", C(0, 1));
            _network.RegisterConsumer("Diagonal", C(1, 1));

            Assert.IsTrue(_network.IsPowered("Adjacent", 1));
            Assert.IsFalse(_network.IsPowered("Diagonal", 1));
        }

        [Test]
        public void APipeRun_ExtendsPowerToTheFarEnd()
        {
            _network.RegisterBoiler("B", C(0, 0), 100);
            _network.AddPipe(C(1, 0));
            _network.AddPipe(C(2, 0));
            _network.AddPipe(C(3, 0));
            _network.RegisterConsumer("Far", C(4, 0));

            Assert.IsTrue(_network.IsPowered("Far", 1));
        }

        [Test]
        public void RemovingAPipeMidChain_UnpowersEverythingPastTheBreak()
        {
            // progression-design §9 Phase 6: "a stage bar freezes because one zinc extractor
            // lost steam when a pipe was paved over". If reach were cached rather than
            // re-derived, this beat would silently never happen.
            _network.RegisterBoiler("B", C(0, 0), 100);
            for (int x = 1; x <= 5; x++)
            {
                _network.AddPipe(C(x, 0));
            }

            _network.RegisterConsumer("Near", C(1, 1));
            _network.RegisterConsumer("Far", C(6, 0));

            Assert.IsTrue(_network.IsPowered("Near", 1));
            Assert.IsTrue(_network.IsPowered("Far", 1));

            Assert.IsTrue(_network.RemovePipe(C(3, 0)), "pipe was there to remove");

            Assert.IsTrue(_network.IsPowered("Near", 2), "this side of the break still has steam");
            Assert.IsFalse(_network.IsPowered("Far", 2), "everything past the break goes dark");
        }

        [Test]
        public void RemovingTheBoiler_UnpowersEveryoneOnItsNetwork()
        {
            _network.RegisterBoiler("B", C(0, 0), 100);
            _network.AddPipe(C(1, 0));
            _network.RegisterConsumer("G", C(2, 0));
            Assert.IsTrue(_network.IsPowered("G", 1));

            _network.RemoveBoiler("B");

            Assert.IsFalse(_network.IsPowered("G", 2));
        }

        // --- Determinism decision 1: WHICH 8 --------------------------------------------------

        [Test]
        public void ABoilerPowersAtMostEightGolems()
        {
            _network.RegisterBoiler("B", C(0, 0), 1000);
            // A pipe run past the boiler puts far more than 8 tiles in reach of it.
            for (int x = -6; x <= 6; x++)
            {
                _network.AddPipe(C(x, 1));
            }

            for (int i = 0; i < 12; i++)
            {
                _network.RegisterConsumer("G" + i, C(i - 6, 2));
            }

            int powered = 0;
            for (int i = 0; i < 12; i++)
            {
                if (_network.IsPowered("G" + i, 1))
                {
                    powered++;
                }
            }

            Assert.AreEqual(SteamNetwork.MaxGolemsPerBoiler, powered, "§3.1: at most 8");
        }

        [Test]
        public void WhichEightAreChosen_IsStableAcrossTicks_AndIsTheLowestCellsInOrder()
        {
            _network.RegisterBoiler("B", C(0, 0), 10000);
            for (int x = -6; x <= 6; x++)
            {
                _network.AddPipe(C(x, 1));
            }

            // 12 candidates in a row at y=2, all reachable from the pipe run at y=1.
            var ids = new List<string>();
            for (int x = -6; x <= 5; x++)
            {
                string id = "G" + (x + 6);
                _network.RegisterConsumer(id, C(x, 2));
                ids.Add(id);
            }

            var poweredAtTickOne = new List<string>();
            foreach (string id in ids)
            {
                if (_network.IsPowered(id, 1))
                {
                    poweredAtTickOne.Add(id);
                }
            }

            Assert.AreEqual(8, poweredAtTickOne.Count);

            // The order is cell order (x ascending), so it is the WESTMOST eight -- an arbitrary
            // but fixed rule. What matters is that it is derived from layout, never from a
            // Dictionary bucket or the order the player built in.
            CollectionAssert.AreEqual(
                new[] { "G0", "G1", "G2", "G3", "G4", "G5", "G6", "G7" }, poweredAtTickOne);

            // ...and it does not move. An unstable choice would flicker golems between powered
            // and NoSteam every tick, which is unplayable and untestable.
            for (long tick = 2; tick <= 40; tick++)
            {
                _network.Tick(tick);
                foreach (string id in ids)
                {
                    bool expected = poweredAtTickOne.Contains(id);
                    Assert.AreEqual(expected, _network.IsPowered(id, tick),
                        id + " changed power state on tick " + tick);
                }
            }
        }

        [Test]
        public void TheChosenEight_DoNotDependOnRegistrationOrder()
        {
            // Two identically-built factories, assembled in opposite orders, must behave
            // identically -- so registration order must not leak into the choice.
            var forwards = new SteamNetwork();
            var backwards = new SteamNetwork();

            forwards.RegisterBoiler("B", C(0, 0), 10000);
            backwards.RegisterBoiler("B", C(0, 0), 10000);
            for (int x = -6; x <= 6; x++)
            {
                forwards.AddPipe(C(x, 1));
                backwards.AddPipe(C(x, 1));
            }

            for (int x = -6; x <= 5; x++)
            {
                forwards.RegisterConsumer("G" + (x + 6), C(x, 2));
            }

            for (int x = 5; x >= -6; x--)
            {
                backwards.RegisterConsumer("G" + (x + 6), C(x, 2));
            }

            for (int i = 0; i < 12; i++)
            {
                string id = "G" + i;
                Assert.AreEqual(forwards.IsPowered(id, 1), backwards.IsPowered(id, 1),
                    id + " disagreed between the two build orders");
            }
        }

        // --- Determinism decision 2: TWO BOILERS ON ONE NETWORK -------------------------------

        [Test]
        public void TwoBoilersSharingAPipeNetwork_PowerSixteenBetweenThem_WithoutDoubleClaiming()
        {
            _network.RegisterBoiler("West", C(-8, 0), 10000);
            _network.RegisterBoiler("East", C(8, 0), 10000);
            for (int x = -7; x <= 7; x++)
            {
                _network.AddPipe(C(x, 0));
            }

            // 20 golems along the pipe run: more than one boiler can serve, fewer than two can.
            for (int i = 0; i < 20; i++)
            {
                _network.RegisterConsumer("G" + i, C(i - 8, 1));
            }

            int powered = 0;
            var claimedBy = new Dictionary<string, string>();
            for (int i = 0; i < 20; i++)
            {
                string boilerId;
                if (_network.TryGetPoweringBoiler("G" + i, 1, out boilerId))
                {
                    powered++;
                    claimedBy["G" + i] = boilerId;
                }
            }

            Assert.AreEqual(2 * SteamNetwork.MaxGolemsPerBoiler, powered,
                "two boilers, 8 each, nobody claimed twice");

            // Boilers are walked in cell order, so the westmost boiler claims first from the
            // golem order (also cell order): West takes the first 8, East takes the next 8.
            Assert.AreEqual("West", claimedBy["G0"]);
            Assert.AreEqual("West", claimedBy["G7"]);
            Assert.AreEqual("East", claimedBy["G8"]);
            Assert.AreEqual("East", claimedBy["G15"]);
            Assert.IsFalse(claimedBy.ContainsKey("G16"), "the overflow is genuinely unpowered");
        }

        [Test]
        public void AddingASecondBoiler_DoesNotReshuffleWhoTheFirstWasAlreadyPowering()
        {
            _network.RegisterBoiler("West", C(-8, 0), 10000);
            for (int x = -7; x <= 7; x++)
            {
                _network.AddPipe(C(x, 0));
            }

            for (int i = 0; i < 20; i++)
            {
                _network.RegisterConsumer("G" + i, C(i - 8, 1));
            }

            var before = new List<string>();
            for (int i = 0; i < 20; i++)
            {
                string boilerId;
                if (_network.TryGetPoweringBoiler("G" + i, 1, out boilerId))
                {
                    before.Add("G" + i);
                }
            }

            _network.RegisterBoiler("East", C(8, 0), 10000);

            foreach (string id in before)
            {
                string boilerId;
                Assert.IsTrue(_network.TryGetPoweringBoiler(id, 2, out boilerId));
                Assert.AreEqual("West", boilerId,
                    id + " was handed to a different boiler by a build elsewhere");
            }
        }

        // --- Determinism decision 3: FRACTIONAL BURN ------------------------------------------

        [Test]
        public void OneGolem_BurnsExactlyOneCokePerHundredTicks()
        {
            SteamBoiler boiler = _network.RegisterBoiler("B", C(0, 0), 240);
            _network.RegisterConsumer("G", C(0, 1));

            Run(100);

            Assert.AreEqual(239, boiler.CokeStock, "§3.1: 1 Coke per powered golem per 10 s");
            Assert.AreEqual(0, boiler.BurnAccumulator, "and it lands exactly on the boundary");
        }

        [Test]
        public void FiveGolems_BurnFiveCokeInOneHundredTicks_AndFiftyInOneThousand()
        {
            SteamBoiler boiler = _network.RegisterBoiler("B", C(0, 0), 1000);
            for (int x = -1; x <= 3; x++)
            {
                _network.AddPipe(C(x, 1));
            }

            for (int i = 0; i < 5; i++)
            {
                _network.RegisterConsumer("G" + i, C(i - 1, 2));
            }

            Assert.AreEqual(5, _network.PoweredGolemCount(1), "precondition: all five have steam");

            Run(100);
            Assert.AreEqual(1000 - 5, boiler.CokeStock, "5 golem-ticks x 100 = 500 = 5 Coke");

            Run(900, 101);
            Assert.AreEqual(1000 - 50, boiler.CokeStock, "1000 ticks is 100 s, so 50 Coke");
        }

        [Test]
        public void TheRemainderCarries_RatherThanBeingLostOrDoubleCharged()
        {
            // 3 golems: 3 golem-ticks per tick, so a Coke every 33 1/3 ticks. Nothing here
            // divides evenly, which is exactly the case a float accumulator gets wrong.
            SteamBoiler boiler = _network.RegisterBoiler("B", C(0, 0), 1000);
            _network.AddPipe(C(0, 1));
            _network.RegisterConsumer("A", C(0, 2));
            _network.RegisterConsumer("B2", C(-1, 1));
            _network.RegisterConsumer("C", C(1, 1));
            Assert.AreEqual(3, _network.PoweredGolemCount(1));

            Run(33);
            Assert.AreEqual(1000, boiler.CokeStock, "99 golem-ticks is not yet a whole Coke");
            Assert.AreEqual(99, boiler.BurnAccumulator);

            Run(1, 34);
            Assert.AreEqual(999, boiler.CokeStock, "the 34th tick crosses 100");
            Assert.AreEqual(2, boiler.BurnAccumulator, "and 2 golem-ticks carry into the next");

            // Over a long run the carry must keep the total exactly proportional: 3 golems for
            // 3000 ticks is 9000 golem-ticks = 90 Coke, with nothing lost to rounding.
            Run(2966, 35);
            Assert.AreEqual(1000 - 90, boiler.CokeStock);
        }

        [Test]
        public void AnIdleBoiler_BurnsNothing()
        {
            // §3.1: "an idle boiler burns nothing, so the starting Coke stock is a budget the
            // player spends by building rather than a hidden timer they cannot affect." This is
            // the property a flat per-boiler burn would destroy.
            SteamBoiler boiler = _network.RegisterBoiler("B", C(0, 0), 240);

            Run(5000);

            Assert.AreEqual(240, boiler.CokeStock);
            Assert.AreEqual(0, boiler.BurnAccumulator);
        }

        [Test]
        public void AGolemOutOfReach_CostsNothing()
        {
            SteamBoiler boiler = _network.RegisterBoiler("B", C(0, 0), 240);
            _network.RegisterConsumer("Distant", C(50, 50));

            Run(1000);

            Assert.AreEqual(240, boiler.CokeStock, "you only pay for golems you actually power");
        }

        // --- Fuel exhaustion -------------------------------------------------------------------

        [Test]
        public void WhenTheCokeRunsOut_TheBoilerPowersNothing_AndTheStockNeverGoesNegative()
        {
            SteamBoiler boiler = _network.RegisterBoiler("B", C(0, 0), 2);
            _network.RegisterConsumer("G", C(0, 1));

            Assert.IsTrue(_network.IsPowered("G", 1), "powered while there is fuel");

            Run(200);

            Assert.AreEqual(0, boiler.CokeStock);
            Assert.IsFalse(_network.IsPowered("G", 201), "no running on credit");

            // Keep ticking: a dry boiler must not dig a hole, and must not accrue upkeep for
            // golems it is no longer powering.
            Run(1000, 201);
            Assert.AreEqual(0, boiler.CokeStock);
            Assert.AreEqual(0, boiler.PoweredGolemCount);
        }

        [Test]
        public void RefuellingADryBoiler_BringsItsGolemsBack()
        {
            SteamBoiler boiler = _network.RegisterBoiler("B", C(0, 0), 1);
            _network.RegisterConsumer("G", C(0, 1));
            Run(150);
            Assert.IsFalse(_network.IsPowered("G", 200));

            boiler.AddCoke(50);

            Assert.IsTrue(_network.IsPowered("G", 201));
        }

        // --- The number §10's soft-lock audit rests on ------------------------------------------

        [Test]
        public void SevenGolems_ConsumeFortyTwoCokePerMinute()
        {
            // progression-design §10, "Coke death spiral under proportional burn": a coal cluster
            // of 1 extractor + 4 cokers + 2 loaders = 7 golems produces 140 Coke/min and consumes
            // 42, a 3.3:1 ratio, "convergent at every scale". If a retune ever breaks this
            // arithmetic the design's whole no-runaway argument goes with it, so it is pinned
            // here rather than left implicit in the constants.
            SteamBoiler boiler = _network.RegisterBoiler("B", C(0, 0), 1000);
            for (int x = -1; x <= 5; x++)
            {
                _network.AddPipe(C(x, 1));
            }

            for (int i = 0; i < 7; i++)
            {
                _network.RegisterConsumer("G" + i, C(i - 1, 2));
            }

            Assert.AreEqual(7, _network.PoweredGolemCount(1), "precondition: all seven have steam");

            // 1 minute at 10 ticks/sec.
            Run(600);

            Assert.AreEqual(42, 1000 - boiler.CokeStock,
                "7 golems x 6 Coke/min = 42 Coke/min -- §10's convergence check");

            // And the gauge must quote the same figure the simulation actually charges.
            Assert.AreEqual(42, SteamGaugeUtility.BurnPerMinute(7));
        }

        [Test]
        public void EightyGolemsOnTenBoilers_MatchTheDesignsPerGolemRate()
        {
            // §3.1's headline: ~96 golems is ~576 Coke/min of upkeep. Scaled to a round 80/10
            // here so every boiler is exactly at its cap and the arithmetic is unambiguous.
            for (int b = 0; b < 10; b++)
            {
                int origin = b * 20;
                _network.RegisterBoiler("B" + b, C(origin, 0), 10000);

                // A boiler alone reaches only its four neighbours, so each gets an 8-cell pipe
                // spur with a golem beside every cell of it. 20 apart, so no boiler's reach can
                // touch another's and the per-boiler cap is what is being measured.
                for (int k = -3; k <= 4; k++)
                {
                    _network.AddPipe(C(origin + k, 1));
                    _network.RegisterConsumer("B" + b + "G" + (k + 3), C(origin + k, 2));
                }
            }

            Assert.AreEqual(80, _network.PoweredGolemCount(1),
                "precondition: every boiler is exactly at its 8-golem cap");

            int before = _network.TotalCokeStock;
            Run(600);

            Assert.AreEqual(80 * 6, before - _network.TotalCokeStock, "80 golems x 6 Coke/min");
        }

        // --- Housekeeping -----------------------------------------------------------------------

        [Test]
        public void UnregisteringAConsumer_StopsItsUpkeepImmediately()
        {
            SteamBoiler boiler = _network.RegisterBoiler("B", C(0, 0), 1000);
            _network.RegisterConsumer("G", C(0, 1));
            Run(100);
            Assert.AreEqual(999, boiler.CokeStock);

            _network.UnregisterConsumer("G");
            Run(1000, 101);

            Assert.AreEqual(999, boiler.CokeStock, "a removed golem costs nothing");
        }

        [Test]
        public void MovingAGolemOffThePipe_UnpowersIt()
        {
            _network.RegisterBoiler("B", C(0, 0), 1000);
            _network.RegisterConsumer("G", C(0, 1));
            Assert.IsTrue(_network.IsPowered("G", 1));

            _network.RegisterConsumer("G", C(9, 9));

            Assert.IsFalse(_network.IsPowered("G", 2), "the grid is keyed by cell, so moving matters");
            Assert.AreEqual(1, _network.ConsumerCount, "and re-registering does not duplicate it");
        }

        [Test]
        public void TickIsIdempotentWithinOneTick_SoDoubleRegistrationCannotDoubleCharge()
        {
            SteamBoiler boiler = _network.RegisterBoiler("B", C(0, 0), 1000);
            _network.RegisterConsumer("G", C(0, 1));

            for (long t = 1; t <= 100; t++)
            {
                _network.Tick(t);
                _network.Tick(t);
            }

            Assert.AreEqual(999, boiler.CokeStock);
        }

        [Test]
        public void MissingIdsAreAnswered_NotThrown()
        {
            // The registries-guard-against-null-ids convention: a TryGet on an unset id must be
            // a false, never an exception inside Tick.
            SteamBoiler boiler;
            Assert.IsFalse(_network.TryGetBoiler(null, out boiler));
            Assert.IsFalse(_network.TryGetBoiler("nope", out boiler));
            Assert.IsFalse(_network.IsPowered(null, 1));
            Assert.IsFalse(_network.UnregisterConsumer(null));
            Assert.IsFalse(_network.RemoveBoiler(null));
            Assert.IsFalse(_network.RemovePipe(C(4, 4)));
        }
    }
}
