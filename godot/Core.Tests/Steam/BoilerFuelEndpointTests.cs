using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Belts;
using GolemFactory.Economy;
using GolemFactory.Steam;

namespace GolemFactory.Tests.EditMode
{
    // The Boiler's fuel hatch. SteamBoiler.AddCoke was written as "the only way Coke ever goes
    // up" and then nothing in the game ever called it, so a boiler was a sealed tank holding
    // whatever it was constructed with -- which caps the whole steam economy at §9 Phase 1's
    // opening 240 Coke no matter how much coking capacity the player builds.
    //
    // These pin the endpoint's contract against the ClockTowerInputEndpoint it is modelled on:
    // pure sink, typed refusal, untyped acceptance.
    public class BoilerFuelEndpointTests
    {
        private static SteamBoiler NewBoiler(int startingCoke = 0) =>
            new SteamBoiler("Boiler(0,0)", new Vector2Int(0, 0), startingCoke);

        private static ItemStack Stack(string itemType) => new ItemStack { ItemType = itemType };

        [Test]
        public void TryGive_Coke_RaisesTheBoilersStock()
        {
            SteamBoiler boiler = NewBoiler();
            var endpoint = new BoilerFuelEndpoint(boiler);

            Assert.IsTrue(endpoint.TryGive(Stack(ItemType.Coke)));
            Assert.AreEqual(1, boiler.CokeStock);

            endpoint.TryGive(Stack(ItemType.Coke));
            endpoint.TryGive(Stack(ItemType.Coke));
            Assert.AreEqual(3, boiler.CokeStock, "one unit per give, like every other endpoint");
        }

        [Test]
        public void TryGive_AnythingElse_IsRefusedAndChangesNothing()
        {
            SteamBoiler boiler = NewBoiler(10);
            var endpoint = new BoilerFuelEndpoint(boiler);

            Assert.IsFalse(endpoint.TryGive(Stack(ItemType.IronPlate)));
            Assert.IsFalse(endpoint.TryGive(Stack(ItemType.Scrap)));
            // Refusing rather than silently swallowing is the no-item-loss invariant: a golem
            // pushing a mixed hold keeps its Iron Plate for the next cycle.
            Assert.AreEqual(10, boiler.CokeStock);
        }

        [Test]
        public void TryGive_NullOrEmptyType_IsRefused()
        {
            SteamBoiler boiler = NewBoiler(5);
            var endpoint = new BoilerFuelEndpoint(boiler);

            Assert.IsFalse(endpoint.TryGive(Stack(null)));
            Assert.IsFalse(endpoint.TryGive(default));
            Assert.AreEqual(5, boiler.CokeStock);
        }

        [Test]
        public void CanGive_UntypedIsAlwaysTrue_TypedIsCokeOnly()
        {
            var endpoint = new BoilerFuelEndpoint(NewBoiler());

            // §3.1 gives a Boiler no capacity, so it can never be full. The untyped question is
            // BeginPush's early-out ("could continuing to the next type possibly help?") and
            // answering it per type would abandon a mixed push at the first non-Coke good.
            Assert.IsTrue(endpoint.CanGive());

            Assert.IsTrue(endpoint.CanGive(ItemType.Coke));
            Assert.IsFalse(endpoint.CanGive(ItemType.Coal), "coal is not coke -- R1 is the whole point");
            Assert.IsFalse(endpoint.CanGive(ItemType.IronPlate));
            Assert.IsFalse(endpoint.CanGive(null));
        }

        [Test]
        public void IsAPureSink_NeverHandsFuelBack()
        {
            SteamBoiler boiler = NewBoiler(240);
            var endpoint = new BoilerFuelEndpoint(boiler);

            Assert.IsFalse(endpoint.TryTake(out ItemStack _));
            Assert.IsNull(endpoint.PeekAvailableType());
            Assert.IsFalse(endpoint.TryTake(ItemType.Coke, 5, out int taken));
            Assert.AreEqual(0, taken);

            // Not merely tidy: a boiler that gave fuel back would be an uncapped Coke warehouse
            // that §1.2's per-item-type buffer cap does not apply to.
            Assert.AreEqual(240, boiler.CokeStock);
        }

        [Test]
        public void RefuellingRaisesThePeakSoTheGaugesQuarterAlertTracksTheFullestItHasBeen()
        {
            SteamBoiler boiler = NewBoiler(0);
            var endpoint = new BoilerFuelEndpoint(boiler);

            for (int i = 0; i < 80; i++)
            {
                endpoint.TryGive(Stack(ItemType.Coke));
            }

            Assert.AreEqual(80, boiler.CokeStock);
            Assert.AreEqual(80, boiler.PeakCokeStock,
                "the §8 alert measures against the high-water mark, which delivery must move");
        }

        [Test]
        public void NullBoiler_RefusesEverythingRatherThanThrowing()
        {
            // Same guard idiom the registries use: return false on an unset reference rather
            // than letting a null deref escape into Tick.
            var endpoint = new BoilerFuelEndpoint(null);

            Assert.IsFalse(endpoint.CanGive());
            Assert.IsFalse(endpoint.CanGive(ItemType.Coke));
            Assert.IsFalse(endpoint.TryGive(Stack(ItemType.Coke)));
            Assert.AreEqual("Boiler", endpoint.DisplayName);
        }

        [Test]
        public void DisplayName_DefaultsToTheBoilerId_SoAStallNamesTheRightHatch()
        {
            var endpoint = new BoilerFuelEndpoint(NewBoiler());
            Assert.AreEqual("Boiler(0,0)", endpoint.DisplayName);

            var named = new BoilerFuelEndpoint(NewBoiler(), "North Firebox");
            Assert.AreEqual("North Firebox", named.DisplayName);
        }
    }
}
