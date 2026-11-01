using System.Linq;
using GolemFactory.Buildings;
using GolemFactory.Data;
using GolemFactory.Economy;
using GolemFactory.Tests.Data;
using GolemFactory.Tests.World;
using GolemFactory.World;
using Vector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>The crate suites' shared fixture: the real Sandbox, the pack, and placing through build mode.</summary>
    internal static class CrateWorld
    {
        public static SandboxWorld Compose()
        {
            DefinitionSet definitions = AuthoredData.Load();
            return SandboxWorld.Compose(definitions, SandboxSetupTests.LoadReal(), PlaceableCatalogTests.LoadReal(definitions));
        }

        public static int Pack(SandboxWorld world, string item) => world.Buffers.GetQuantity(world.StockpileBufferId, item);

        public static void Give(SandboxWorld world, string item, int quantity) =>
            world.Buffers.Deposit(world.StockpileBufferId, item, quantity);

        public static PlaceableBuilding Place(SandboxWorld world, string key, Vector2Int cell)
        {
            world.Build.SetActivePrefab(world.Placeables.Single(p => p.Key == key).Prefab);
            world.Build.PlaceOrRemove(cell);
            world.Build.CancelPlacement();
            return world.Build.Buildings.Single(b => !b.IsRemoved && b.Cell == cell);
        }

        /// <summary>A depot, paid for from the pack (15 Scrap), as the player would.</summary>
        public static ICrate PlaceDepot(SandboxWorld world, Vector2Int cell)
        {
            Give(world, ItemType.Scrap, 15);
            return Place(world, "DepotPrefab", cell).GetPart<ICrate>();
        }
    }
}
