using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Buildings;
using GolemFactory.Compat;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.Player;
using GolemFactory.Steam;
using GolemFactory.World;

namespace GolemFactory.Tests.PlayMode
{
    /// <summary>
    /// Hand-loading Coke into a boiler -- the last step of §10's total-blackout recovery, which
    /// did not exist until this.
    ///
    /// <para>
    /// THE LOOP IT BREAKS: a boiler's only Coke writer was <c>BoilerFuelEndpoint</c>, which a
    /// golem <c>Push</c>es into; a golem needs a powered boiler to move at all; and a player-built
    /// boiler starts at zero. So with steam power required, the first boiler could never be lit.
    /// </para>
    ///
    /// <para>
    /// Ported from Unity's PlayMode suite, which needed Play mode only for
    /// <c>PlayerInteractor.OnEnable</c>; that is <c>Attach()</c> now, called where Unity's first
    /// frame ran it. Namespace kept as PlayMode: an EditMode suite of the same name already
    /// covers the sizing rule as a pure policy.
    /// </para>
    /// </summary>
    public class BoilerHandRefuelTests
    {
        private readonly List<PlayerInteractor> _attached = new List<PlayerInteractor>();

        [TearDown]
        public void TearDown()
        {
            foreach (PlayerInteractor player in _attached)
            {
                player.Detach();
            }
            _attached.Clear();
        }

        private (PlayerInteractor player, PlaceableBoiler boiler,
                 StorageBufferRegistry stockpile, SteamNetwork steam) Build()
        {
            var stockpile = new StorageBufferRegistry();
            var steam = new SteamNetwork();

            var building = new PlaceableBuilding { Cell = Vector2Int.zero };
            var boiler = building.AddPart(new PlaceableBoiler());
            // COLD, and it has to be said explicitly. The C# field defaults to
            // DefaultStartingCoke (240) because that is §9's opening gift, but the boiler prefab
            // overrides it to 0 -- shipping 240 would mint Coke for 30 Scrap + 10 Iron Plate. A
            // fresh part gets the C# default, not the prefab's, so a test that skipped this would
            // be exercising a boiler no player ever builds.
            boiler.Configure("TestBoiler", 0);
            boiler.RegisterWithSteamNetwork(steam, Vector2Int.zero);

            var player = new PlayerInteractor();
            player.Configure(1.5f, stockpile, "FactoryStockpile", null, null);
            player.ConfigureWorld(
                () => new ResourceNodeMarker[0], () => new[] { building }, () => new GolemEntity[0]);
            _attached.Add(player);

            return (player, boiler, stockpile, steam);
        }

        [Test]
        public void TryRefuelBoiler_MovesCokeFromTheStockpileIntoTheFirebox()
        {
            (PlayerInteractor player, PlaceableBoiler boiler,
             StorageBufferRegistry stockpile, SteamNetwork _) = Build();
            stockpile.Deposit("FactoryStockpile", ItemType.Coke, 50);
            player.Attach();

            Assert.IsTrue(player.TryRefuelBoiler(boiler));
            Assert.AreEqual(BoilerRefuelPolicy.HandLoadBatch, boiler.Boiler.CokeStock);
            Assert.AreEqual(50 - BoilerRefuelPolicy.HandLoadBatch,
                stockpile.GetQuantity("FactoryStockpile", ItemType.Coke),
                "the Coke must come OUT of the stockpile -- a hand-load that minted fuel would void §3.1");
        }

        [Test]
        public void TryRefuelBoiler_NoCoke_RefusesAndChangesNothing()
        {
            (PlayerInteractor player, PlaceableBoiler boiler,
             StorageBufferRegistry _, SteamNetwork __) = Build();
            player.Attach();

            Assert.IsFalse(player.TryRefuelBoiler(boiler));
            Assert.AreEqual(0, boiler.Boiler.CokeStock);
            Assert.AreEqual("No Coke in the stockpile.", player.LastStatusMessage);
        }

        [Test]
        public void TryRefuelBoiler_LessThanABatch_MovesAllOfItAndEmptiesTheStockpile()
        {
            (PlayerInteractor player, PlaceableBoiler boiler,
             StorageBufferRegistry stockpile, SteamNetwork _) = Build();
            stockpile.Deposit("FactoryStockpile", ItemType.Coke, 3);
            player.Attach();

            Assert.IsTrue(player.TryRefuelBoiler(boiler));
            Assert.AreEqual(3, boiler.Boiler.CokeStock);
            Assert.AreEqual(0, stockpile.GetQuantity("FactoryStockpile", ItemType.Coke));
        }

        // THE REGRESSION THIS ALL EXISTS FOR, written as the scenario rather than the mechanism:
        // a cold boiler, a player holding hand-cranked Coke, and no working golem anywhere in the
        // world. Before this verb the factory was dead at that point, permanently.
        [Test]
        public void ColdBoilerAndNoWorkingGolem_CanBeLitByHandAndThenPowersAGolem()
        {
            (PlayerInteractor player, PlaceableBoiler boiler,
             StorageBufferRegistry stockpile, SteamNetwork steam) = Build();
            player.Attach();

            Assert.AreEqual(0, boiler.Boiler.CokeStock, "the blackout: a boiler with no fuel");
            steam.RegisterConsumer("Golem1", Vector2Int.right);
            Assert.IsFalse(steam.IsPowered("Golem1", 1),
                "precondition: nothing runs, so nothing can deliver fuel");

            // All the player has is what the Hand-Crank Bench made.
            stockpile.Deposit("FactoryStockpile", ItemType.Coke, 20);
            Assert.IsTrue(player.TryRefuelBoiler(boiler), "the player must be able to light it by hand");

            Assert.IsTrue(steam.IsPowered("Golem1", 2),
                "a golem beside a hand-lit boiler must run -- that is the whole recovery path");
        }

        // The prompt has to advertise the refusal, or the player walks to a boiler they cannot
        // use and presses a key that does nothing.
        [Test]
        public void WithNoCoke_TheBoilerAdvertisesItselfAsUnavailable()
        {
            (PlayerInteractor player, PlaceableBoiler boiler,
             StorageBufferRegistry stockpile, SteamNetwork _) = Build();
            player.Position = Vector3.zero; // the boiler's cell, (0,0)
            player.RefreshInteractables();
            player.Attach();

            player.RefreshAffordance();
            Assert.AreEqual(InteractionKind.Refuel, player.CurrentPick.Kind);
            Assert.AreEqual(InteractionAffordance.Unavailable, player.CurrentAffordance);

            stockpile.Deposit("FactoryStockpile", ItemType.Coke, 20);
            player.RefreshAffordance();
            Assert.AreEqual(InteractionAffordance.Ready, player.CurrentAffordance,
                "with Coke in hand the same boiler must read as actionable");
        }
    }
}
