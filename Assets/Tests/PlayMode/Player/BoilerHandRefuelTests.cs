using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.Player;
using GolemFactory.Steam;

namespace GolemFactory.Tests.PlayMode
{
    /// <summary>
    /// Hand-loading Coke into a boiler -- the last step of §10's total-blackout recovery, which
    /// did not exist until this.
    ///
    /// <para>
    /// THE LOOP IT BREAKS: a boiler's only Coke writer was <c>BoilerFuelEndpoint</c>, which a
    /// golem <c>Push</c>es into; a golem needs a powered boiler to move at all; and a
    /// player-built boiler starts at zero. So with <c>SandboxBootstrap.requireSteamPower</c> on,
    /// the first boiler could never be lit -- every golem stalls <c>NoSteam</c>, and nothing that
    /// is stalled can carry it fuel. The Hand-Crank Bench cleared the *goods* half of §10 (it can
    /// make Coke) and nothing moved that Coke the last three feet into the firebox.
    /// </para>
    ///
    /// <para>
    /// PlayMode rather than EditMode for the same reason <c>PlayerInteractorTests</c> is:
    /// <c>PlayerInteractor.OnEnable</c> does not run outside Play Mode, and the sizing rule
    /// itself is covered as a pure policy in the EditMode suite.
    /// </para>
    /// </summary>
    public class BoilerHandRefuelTests
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

        private (PlayerInteractor player, PlaceableBoiler boiler,
                 StorageBufferRegistryHolder stockpile, SteamNetworkHolder steam) Build()
        {
            _root = new GameObject("Root");

            var stockpile = new GameObject("Stockpile").AddComponent<StorageBufferRegistryHolder>();
            stockpile.transform.SetParent(_root.transform);

            var steam = new GameObject("Steam").AddComponent<SteamNetworkHolder>();
            steam.transform.SetParent(_root.transform);

            var boilerGo = new GameObject("Boiler", typeof(PlaceableBuilding));
            boilerGo.transform.SetParent(_root.transform);
            var boiler = boilerGo.AddComponent<PlaceableBoiler>();
            // COLD, and it has to be said explicitly. The C# field defaults to
            // DefaultStartingCoke (240) because that is §9's opening gift, but BoilerPrefab
            // overrides it to 0 -- shipping 240 on the prefab would mint Coke for 30 Scrap + 10
            // Iron Plate. A fresh AddComponent gets the C# default, not the prefab's, so a test
            // that skipped this would be exercising a boiler no player ever builds. (The first
            // draft did exactly that, and every assertion here came back 240 too high.)
            boiler.Configure("TestBoiler", 0);
            boiler.RegisterWithSteamNetwork(steam, Vector2Int.zero);

            var player = new GameObject("Player").AddComponent<PlayerInteractor>();
            player.transform.SetParent(_root.transform);
            player.Configure(null, 1.5f, stockpile, "FactoryStockpile", null, null);

            return (player, boiler, stockpile, steam);
        }

        [UnityTest]
        public IEnumerator TryRefuelBoiler_MovesCokeFromTheStockpileIntoTheFirebox()
        {
            (PlayerInteractor player, PlaceableBoiler boiler,
             StorageBufferRegistryHolder stockpile, SteamNetworkHolder _) = Build();
            stockpile.Registry.Deposit("FactoryStockpile", ItemType.Coke, 50);
            yield return null;

            Assert.IsTrue(player.TryRefuelBoiler(boiler));
            Assert.AreEqual(BoilerRefuelPolicy.HandLoadBatch, boiler.Boiler.CokeStock);
            Assert.AreEqual(50 - BoilerRefuelPolicy.HandLoadBatch,
                stockpile.Registry.GetQuantity("FactoryStockpile", ItemType.Coke),
                "the Coke must come OUT of the stockpile -- a hand-load that minted fuel would void §3.1");
        }

        [UnityTest]
        public IEnumerator TryRefuelBoiler_NoCoke_RefusesAndChangesNothing()
        {
            (PlayerInteractor player, PlaceableBoiler boiler,
             StorageBufferRegistryHolder _, SteamNetworkHolder __) = Build();
            yield return null;

            Assert.IsFalse(player.TryRefuelBoiler(boiler));
            Assert.AreEqual(0, boiler.Boiler.CokeStock);
            Assert.AreEqual("No Coke in the stockpile.", player.LastStatusMessage);
        }

        [UnityTest]
        public IEnumerator TryRefuelBoiler_LessThanABatch_MovesAllOfItAndEmptiesTheStockpile()
        {
            (PlayerInteractor player, PlaceableBoiler boiler,
             StorageBufferRegistryHolder stockpile, SteamNetworkHolder _) = Build();
            stockpile.Registry.Deposit("FactoryStockpile", ItemType.Coke, 3);
            yield return null;

            Assert.IsTrue(player.TryRefuelBoiler(boiler));
            Assert.AreEqual(3, boiler.Boiler.CokeStock);
            Assert.AreEqual(0, stockpile.Registry.GetQuantity("FactoryStockpile", ItemType.Coke));
        }

        // THE REGRESSION THIS ALL EXISTS FOR, written as the scenario rather than the mechanism:
        // a cold boiler, a player holding hand-cranked Coke, and no working golem anywhere in the
        // world. Before this verb the factory was dead at that point, permanently.
        [UnityTest]
        public IEnumerator ColdBoilerAndNoWorkingGolem_CanBeLitByHandAndThenPowersAGolem()
        {
            (PlayerInteractor player, PlaceableBoiler boiler,
             StorageBufferRegistryHolder stockpile, SteamNetworkHolder steam) = Build();
            yield return null;

            Assert.AreEqual(0, boiler.Boiler.CokeStock, "the blackout: a boiler with no fuel");
            steam.Network.RegisterConsumer("Golem1", Vector2Int.right);
            Assert.IsFalse(steam.Network.IsPowered("Golem1", 1),
                "precondition: nothing runs, so nothing can deliver fuel");

            // All the player has is what the Hand-Crank Bench made.
            stockpile.Registry.Deposit("FactoryStockpile", ItemType.Coke, 20);
            Assert.IsTrue(player.TryRefuelBoiler(boiler), "the player must be able to light it by hand");

            Assert.IsTrue(steam.Network.IsPowered("Golem1", 2),
                "a golem beside a hand-lit boiler must run -- that is the whole recovery path");
        }

        // The prompt has to advertise the refusal, or the player walks to a boiler they cannot
        // use and presses a key that does nothing.
        [UnityTest]
        public IEnumerator WithNoCoke_TheBoilerAdvertisesItselfAsUnavailable()
        {
            (PlayerInteractor player, PlaceableBoiler boiler,
             StorageBufferRegistryHolder stockpile, SteamNetworkHolder _) = Build();
            player.transform.position = boiler.transform.position;
            player.RefreshInteractables();
            yield return null;

            player.RefreshAffordance();
            Assert.AreEqual(InteractionKind.Refuel, player.CurrentPick.Kind);
            Assert.AreEqual(InteractionAffordance.Unavailable, player.CurrentAffordance);

            stockpile.Registry.Deposit("FactoryStockpile", ItemType.Coke, 20);
            player.RefreshAffordance();
            Assert.AreEqual(InteractionAffordance.Ready, player.CurrentAffordance,
                "with Coke in hand the same boiler must read as actionable");
        }
    }
}
