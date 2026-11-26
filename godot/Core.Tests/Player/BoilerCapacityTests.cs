using System.Linq;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.Steam;
using GolemFactory.Tests.World;
using GolemFactory.World;
using NUnit.Framework;
using static GolemFactory.Tests.EditMode.CrateWorld;
using Vector2Int = GolemFactory.Compat.Vector2Int;
using Vector3 = GolemFactory.Compat.Vector3;

namespace GolemFactory.Tests.Player
{
    /// <summary>
    /// A boiler is storage like a crate (2026-11-21, from playtest: "I have a boiler with too much
    /// coke and cannot demolish it because I can't fit the coke in my inventory"). Its cap fits an
    /// empty pack slot, as a crate's does, and [I] takes its Coke out, so a boiler can always be
    /// emptied and then demolished, whatever it holds.
    /// </summary>
    public class BoilerCapacityTests
    {
        private static readonly Vector2Int Cell = new Vector2Int(6, 6);

        private static PlaceableBuilding PlaceBoiler(SandboxWorld world)
        {
            Give(world, ItemType.Scrap, 30);
            Give(world, ItemType.IronPlate, 10);
            return Place(world, "BoilerPrefab", Cell);
        }

        private static void StandBy(SandboxWorld world)
        {
            world.Interactor.Position = new Vector3(Cell.x, Cell.y + 1f, 0f);
            world.Interactor.Poll();
        }

        /// <summary>The wrecking bar on the boiler; returns what it said (leaving the mode clears it).</summary>
        private static string Demolish(SandboxWorld world)
        {
            world.Build.EnterDemolishMode();
            world.Build.PlaceOrRemove(Cell);
            string said = world.Build.LastStatusMessage;
            world.Build.CancelPlacement();
            return said;
        }

        [Test]
        public void TheBoilersCap_FitsAnEmptySlotOfTheBasePack()
        {
            int pack = SandboxSetupTests.LoadReal().packCapacityPerType;
            Assert.Less(SteamBoiler.CokeCapacity, pack,
                "a full boiler's Coke must fit an empty pack, or a full boiler can't be demolished");
        }

        [Test]
        public void AFullBoiler_DemolishesIntoAnEmptyPack()
        {
            SandboxWorld world = Compose();
            PlaceableBuilding building = PlaceBoiler(world);
            building.GetPart<PlaceableBoiler>().Boiler.SetCoke(SteamBoiler.CokeCapacity);
            Assert.AreEqual(0, Pack(world, ItemType.Coke), "precondition: no Coke in the pack");

            string said = Demolish(world);

            Assert.IsTrue(building.IsRemoved, said);
            Assert.AreEqual(SteamBoiler.CokeCapacity, Pack(world, ItemType.Coke), "every lump came back");
        }

        [Test]
        public void IAtABoiler_TakesWhatFits_AndLeavesTheRestBurning()
        {
            SandboxWorld world = Compose();
            PlaceableBoiler boiler = PlaceBoiler(world).GetPart<PlaceableBoiler>();
            boiler.Boiler.SetCoke(SteamBoiler.CokeCapacity);
            int cap = world.Wallet.Pack.CapacityPerType;
            Give(world, ItemType.Coke, cap - 30);
            StandBy(world);

            StringAssert.Contains("[I] take Coke", world.Interactor.CurrentPrompt, "the prompt says the key exists");
            Assert.IsTrue(world.Interactor.OpenInventory(), world.Interactor.LastStatusMessage);

            Assert.AreEqual(cap, Pack(world, ItemType.Coke), "the pack filled");
            Assert.AreEqual(SteamBoiler.CokeCapacity - 30, boiler.Boiler.CokeStock, "the rest stays in the boiler");
            Assert.AreEqual("Took 30 Coke from the boiler.", world.Interactor.LastStatusMessage);

            Assert.IsFalse(world.Interactor.OpenInventory(), "a full pack takes nothing");
            StringAssert.Contains("full", world.Interactor.LastStatusMessage);
            Assert.AreEqual(SteamBoiler.CokeCapacity - 30, boiler.Boiler.CokeStock, "nothing moved");
        }

        [Test]
        public void IAtAnEmptyBoiler_Refuses_AndOffersNoTake()
        {
            SandboxWorld world = Compose();
            PlaceBoiler(world);
            StandBy(world);

            StringAssert.DoesNotContain("take Coke", world.Interactor.CurrentPrompt);
            Assert.IsFalse(world.Interactor.OpenInventory());
            Assert.AreEqual("The boiler is empty.", world.Interactor.LastStatusMessage);
        }

        [Test]
        public void ABoilerOverItsCap_FromAnOldSave_IsEmptiedByHand_ThenDemolished()
        {
            // A load never clamps, so a save from the 240 cap can still hold more than a pack.
            SandboxWorld world = Compose();
            PlaceableBuilding building = PlaceBoiler(world);
            PlaceableBoiler boiler = building.GetPart<PlaceableBoiler>();
            int cap = world.Wallet.Pack.CapacityPerType;
            boiler.Boiler.SetCoke(cap + 140);

            string said = Demolish(world);
            Assert.IsFalse(building.IsRemoved, "too much to hand back: it stands");
            StringAssert.StartsWith("Not demolished: pack full of Coke.", said);
            StringAssert.Contains("[I]", said, "and says how to empty it");

            StandBy(world);
            Assert.IsTrue(world.Interactor.OpenInventory(), world.Interactor.LastStatusMessage);
            Assert.AreEqual(cap, Pack(world, ItemType.Coke));
            Assert.AreEqual(140, boiler.Boiler.CokeStock);

            // Put the pack's Coke somewhere (here: spent), and the rest comes out the same way.
            Assert.IsTrue(world.Wallet.TryCharge(new[] { new GolemFactory.PunchCards.RecipeIngredient(ItemType.Coke, cap) }));
            Assert.IsTrue(world.Interactor.OpenInventory());
            Assert.AreEqual(40, boiler.Boiler.CokeStock);
            Assert.IsTrue(world.Wallet.TryCharge(new[] { new GolemFactory.PunchCards.RecipeIngredient(ItemType.Coke, cap) }));

            said = Demolish(world);
            Assert.IsTrue(building.IsRemoved, said);
            Assert.AreEqual(40, Pack(world, ItemType.Coke), "the last 40 came back with the boiler");
            Assert.IsFalse(world.Build.Buildings.Any(b => !b.IsRemoved && b.Cell == Cell));
        }
    }
}
