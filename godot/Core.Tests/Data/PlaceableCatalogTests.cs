using System.Collections.Generic;
using System.IO;
using System.Linq;
using GolemFactory.Buildings;
using GolemFactory.Data;
using GolemFactory.Economy;
using NUnit.Framework;

namespace GolemFactory.Tests.Data
{
    /// <summary>
    /// <c>data/placeables.json</c>: the ten prefabs Unity's Sandbox build menu offered, and the
    /// loader that turns them back into PlaceableBuilding prefabs.
    /// </summary>
    public class PlaceableCatalogTests
    {
        internal static List<PlaceableEntry> LoadReal(DefinitionSet definitions = null) =>
            PlaceableCatalog.Load(
                File.ReadAllText(Path.Combine(AuthoredData.DataDirectory, PlaceableCatalog.FileName)),
                definitions ?? AuthoredData.Load());

        [Test]
        public void TheBuildMenuOffersUnitysTenPlaceables_InItsOrder_PlusTheSplitter()
        {
            // Unity's ten, in Sandbox.unity's order, with the belt splitter (G10, no Unity
            // prefab) right after the belt.
            CollectionAssert.AreEqual(
                new[] { "Depot", "GolemConstructionStation", "Belt", "BeltSplitter", "Boiler", "SteamPipe", "ClockTower",
                        "HandCrankBench", "FreightMast", "SlagHeap", "ScrapRecycler" },
                LoadReal().Select(e => e.DisplayName));
        }

        // Read off the Unity prefabs at the G5 port.
        [TestCase("DepotPrefab", "Scrap:15")]
        [TestCase("GolemConstructionStationPrefab", "Scrap:25 Brass:5")]
        [TestCase("BeltPrefab", "Scrap:1")]
        [TestCase("BeltSplitterPrefab", "Scrap:4")]
        [TestCase("BoilerPrefab", "Scrap:30 IronPlate:10")]
        [TestCase("SteamPipePrefab", "IronPlate:1")]
        [TestCase("ClockTowerPrefab", "")]
        [TestCase("HandCrankBenchPrefab", "")]
        [TestCase("FreightMastPrefab", "Brass:20 Casing:10")]
        [TestCase("SlagHeapPrefab", "Scrap:20 IronPlate:10")]
        [TestCase("ScrapRecyclerPrefab", "Scrap:20 IronPlate:10")]
        public void CostsMatchTheUnityPrefabs(string key, string cost)
        {
            PlaceableEntry entry = LoadReal().Single(e => e.Key == key);
            Assert.AreEqual(cost, string.Join(" ", entry.Prefab.Cost.Select(c => c.itemType + ":" + c.quantity)));
        }

        [Test]
        public void OnlyBeltsAndPipesDrag()
        {
            CollectionAssert.AreEquivalent(
                new[] { "BeltPrefab", "SteamPipePrefab" },
                LoadReal().Where(e => e.Prefab.IsDragPlaceable).Select(e => e.Key));
        }

        [Test]
        public void EachPrefabCarriesItsBuildingPart()
        {
            Dictionary<string, PlaceableEntry> byKey = LoadReal().ToDictionary(e => e.Key);
            Assert.IsNotNull(byKey["DepotPrefab"].Prefab.GetPart<PlaceableDepot>());
            Assert.IsNotNull(byKey["GolemConstructionStationPrefab"].Prefab.GetPart<GolemConstructionStation>());
            Assert.IsNotNull(byKey["BeltPrefab"].Prefab.GetPart<PlaceableBelt>());
            Assert.IsNotNull(byKey["BoilerPrefab"].Prefab.GetPart<PlaceableBoiler>());
            Assert.IsNotNull(byKey["SteamPipePrefab"].Prefab.GetPart<PlaceableSteamPipe>());
            Assert.IsNotNull(byKey["ClockTowerPrefab"].Prefab.GetPart<PlaceableClockTower>());
            Assert.IsNotNull(byKey["FreightMastPrefab"].Prefab.GetPart<PlaceableFreightMast>());
            Assert.IsNotNull(byKey["SlagHeapPrefab"].Prefab.GetPart<PlaceableSlagHeap>());
            Assert.IsNotNull(byKey["ScrapRecyclerPrefab"].Prefab.GetPart<PlaceableScrapRecycler>());

            HandCrankBench bench = byKey["HandCrankBenchPrefab"].Prefab.GetPart<HandCrankBench>();
            Assert.IsNotNull(bench);
            Assert.That(bench.CrankableRecipes.Count, Is.GreaterThan(0), "the bench's authored recipes resolved");
            Assert.AreEqual("Clock Tower", byKey["ClockTowerPrefab"].Prefab.GetPart<PlaceableClockTower>().DisplayName);
        }

        [Test]
        public void EverySpriteItNamesExists()
        {
            string art = Path.Combine(Path.GetDirectoryName(AuthoredData.DataDirectory), "art");
            foreach (PlaceableEntry entry in LoadReal())
            {
                foreach (string sprite in new[] { entry.Sprite }.Concat(entry.ShapeSprites.Values).Where(s => s != null))
                {
                    FileAssert.Exists(Path.Combine(art, sprite + ".png"), $"{entry.Key}: {sprite}");
                }
            }
        }

        [Test]
        public void ConnectedPiecesKnowTheirShapes()
        {
            Dictionary<string, PlaceableEntry> byKey = LoadReal().ToDictionary(e => e.Key);
            CollectionAssert.IsSupersetOf(byKey["BeltPrefab"].ShapeSprites.Keys, new[] { "straight", "cornerLeft", "cornerRight" });
            CollectionAssert.IsSupersetOf(byKey["SteamPipePrefab"].ShapeSprites.Keys, new[] { "end", "straight", "corner", "tee", "cross" });
            Assert.AreEqual("item_scrap", byKey["BeltPrefab"].ShapeSprites["item:" + ItemType.Scrap]);
        }

        [Test]
        public void AnUnknownPartIsRefused_NamingThePrefab()
        {
            const string json = "[{\"name\":\"OddPrefab\",\"sprite\":null,\"parts\":{\"Teleporter\":{}}}]";
            var e = Assert.Throws<DefinitionLoadException>(() => PlaceableCatalog.Load(json, AuthoredData.Load()));
            StringAssert.Contains("OddPrefab", e.Message);
            StringAssert.Contains("Teleporter", e.Message);
        }

        [Test]
        public void AnUnknownFieldIsRefused_NamingIt()
        {
            const string json = "[{\"name\":\"DepotPrefab\",\"sprite\":\"depot.png\",\"parts\":{" +
                                "\"PlaceableDepot\":{\"bufferId\":\"X\",\"filterItemType\":\"\",\"capacity\":3}}}]";
            var e = Assert.Throws<DefinitionLoadException>(() => PlaceableCatalog.Load(json, AuthoredData.Load()));
            StringAssert.Contains("capacity", e.Message);
        }

        [Test]
        public void AMissingRecipeIsRefused()
        {
            const string json = "[{\"name\":\"BenchPrefab\",\"sprite\":null,\"parts\":{" +
                                "\"HandCrankBench\":{\"stockpileBufferId\":\"X\",\"candidateRecipes\":[\"R99_Nothing\"]}}}]";
            var e = Assert.Throws<DefinitionLoadException>(() => PlaceableCatalog.Load(json, AuthoredData.Load()));
            StringAssert.Contains("R99_Nothing", e.Message);
        }
    }
}
