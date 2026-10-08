using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using GolemFactory.Buildings;
using GolemFactory.PunchCards;

namespace GolemFactory.Data
{
    /// <summary>One entry of the build menu: the prefab, and what the scene needs to draw it.</summary>
    public sealed class PlaceableEntry
    {
        /// <summary>The Unity prefab's name ("DepotPrefab"): the save file's prefab key.</summary>
        public string Key;

        /// <summary>The build menu's label: the key without "Prefab", as BuildMenuPanel showed it.</summary>
        public string DisplayName;

        /// <summary>The root sprite's file name (no extension), or null (a belt draws only its lane).</summary>
        public string Sprite;

        /// <summary>Extra sprites a connected shape picks between (belt lanes, pipe pieces), by role.</summary>
        public Dictionary<string, string> ShapeSprites = new Dictionary<string, string>();

        public PlaceableBuilding Prefab;
    }

    /// <summary>
    /// Loads <c>data/placeables.json</c> -- the ten prefabs Unity's Sandbox build menu offered,
    /// converted by <c>Tools/Data/convert_unity_assets.py</c> -- into <see cref="PlaceableBuilding"/>
    /// prefabs with their parts (milestone G5).
    ///
    /// <para>
    /// As strict as <see cref="DefinitionLoader"/>: an unknown part, an unknown field on a known
    /// part, or a reference to a missing recipe or stage throws, naming the prefab. A part the
    /// Godot scene replaces wholesale (Unity's YSortSpriteRenderer, now y_sort_enabled) is listed
    /// in <see cref="SceneOnlyParts"/> and skipped by name, never silently.
    /// </para>
    /// </summary>
    public static class PlaceableCatalog
    {
        public const string FileName = "placeables.json";

        /// <summary>Unity components whose whole job the Godot scene does itself.</summary>
        public static readonly IReadOnlyCollection<string> SceneOnlyParts = new[] { "YSortSpriteRenderer" };

        public static List<PlaceableEntry> Load(string json, DefinitionSet definitions)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            var entries = new List<PlaceableEntry>();
            foreach (JsonElement element in doc.RootElement.EnumerateArray())
            {
                string key = element.GetProperty("name").GetString();
                try
                {
                    entries.Add(LoadEntry(key, element, definitions));
                }
                catch (Exception e) when (!(e is DefinitionLoadException))
                {
                    throw new DefinitionLoadException($"{FileName}: {key}: {e.Message}", e);
                }
            }
            return entries;
        }

        private static PlaceableEntry LoadEntry(string key, JsonElement element, DefinitionSet definitions)
        {
            var entry = new PlaceableEntry
            {
                Key = key,
                DisplayName = key.EndsWith("Prefab") && key.Length > "Prefab".Length ? key.Substring(0, key.Length - "Prefab".Length) : key,
                Sprite = StripPng(element.GetProperty("sprite")),
                Prefab = new PlaceableBuilding { name = key },
            };

            foreach (JsonProperty part in element.GetProperty("parts").EnumerateObject())
            {
                var fields = new Fields(key, part.Name, part.Value);
                switch (part.Name)
                {
                    case "PlaceableBuilding":
                        entry.Prefab.ConfigureCost(fields.Cost("cost"));
                        entry.Prefab.ConfigureDragPlaceable(fields.Bool("dragPlaceable"));
                        break;
                    case "PlaceableDepot":
                        entry.Prefab.AddPart(new PlaceableDepot()).Configure(fields.String("bufferId"), fields.String("filterItemType"));
                        break;
                    case "GolemConstructionStation":
                        fields.Ignore("chassisRoster"); // wired per scene (IPlacedStationConfigurator)
                        fields.Ignore("cellSize");      // the converter's 1x1; the grid is always unit
                        var station = entry.Prefab.AddPart(new GolemConstructionStation());
                        station.ConfigureSceneServices(null, null, null, null, null, fields.String("stockpileBufferId"));
                        break;
                    case "PlaceableBelt":
                        entry.Prefab.AddPart(new PlaceableBelt());
                        entry.ShapeSprites["straight"] = StripPng(fields.Raw("straightSprite"));
                        entry.ShapeSprites["cornerLeft"] = StripPng(fields.Raw("cornerLeftSprite"));
                        entry.ShapeSprites["cornerRight"] = StripPng(fields.Raw("cornerRightSprite"));
                        entry.ShapeSprites["fallbackItem"] = StripPng(fields.Raw("fallbackItemSprite"));
                        foreach (JsonElement item in fields.Raw("itemSprites").EnumerateArray())
                        {
                            entry.ShapeSprites["item:" + item.GetProperty("itemType").GetString()] =
                                StripPng(item.GetProperty("sprite"));
                        }
                        break;
                    case "PlaceableBeltSplitter":
                        entry.Prefab.AddPart(new PlaceableBeltSplitter());
                        entry.ShapeSprites["splitter"] = StripPng(fields.Raw("sprite"));
                        break;
                    case "PlaceableBoiler":
                        entry.Prefab.AddPart(new PlaceableBoiler()).Configure(fields.String("boilerId"), fields.Int("startingCoke"));
                        break;
                    case "PlaceableSteamPipe":
                        entry.Prefab.AddPart(new PlaceableSteamPipe());
                        foreach (string role in new[] { "end", "straight", "corner", "tee", "cross" })
                        {
                            entry.ShapeSprites[role] = StripPng(fields.Raw(role + "Sprite"));
                        }
                        break;
                    case "PlaceableClockTower":
                        var tower = entry.Prefab.AddPart(new PlaceableClockTower());
                        tower.ConfigureDisplayName(fields.String("displayName"));
                        tower.ConfigureStages(fields.Names("stages").Select(n => Lookup(definitions.ClockTowerStages, n, key)));
                        break;
                    case "HandCrankBench":
                        entry.Prefab.AddPart(new HandCrankBench()).Configure(
                            null, fields.String("stockpileBufferId"),
                            fields.Names("candidateRecipes").Select(n => Lookup(definitions.Recipes, n, key)));
                        break;
                    case "PlaceableFreightMast":
                        fields.Ignore("bufferId"); // PlaceableFreightMast reads the stockpile it is wired to
                        fields.Ignore("mastId");   // empty: the registry assigns one at placement
                        entry.Prefab.AddPart(new PlaceableFreightMast());
                        break;
                    case "PlaceableSlagHeap":
                        entry.Prefab.AddPart(new PlaceableSlagHeap()).Configure(fields.String("heapId"), fields.Int("startingCoke"));
                        break;
                    case "PlaceableScrapRecycler":
                        entry.Prefab.AddPart(new PlaceableScrapRecycler()).Configure(fields.String("recyclerId"), fields.Int("startingCoke"));
                        break;
                    default:
                        if (!SceneOnlyParts.Contains(part.Name))
                        {
                            throw new DefinitionLoadException($"{FileName}: {key}: unknown part '{part.Name}'");
                        }
                        fields.IgnoreAll();
                        break;
                }
                fields.AssertAllRead();
            }
            return entry;
        }

        private static T Lookup<T>(IReadOnlyDictionary<string, T> table, string name, string key) =>
            table.TryGetValue(name, out T value)
                ? value
                : throw new DefinitionLoadException($"{FileName}: {key}: no definition named '{name}'");

        private static string StripPng(JsonElement value) =>
            value.ValueKind == JsonValueKind.Null ? null : StripPng(value.GetString());

        private static string StripPng(string file) =>
            file != null && file.EndsWith(".png") ? file.Substring(0, file.Length - 4) : file;

        /// <summary>A part's fields, with every one required to be read or explicitly ignored.</summary>
        private sealed class Fields
        {
            private readonly string _where;
            private readonly Dictionary<string, JsonElement> _values = new Dictionary<string, JsonElement>();
            private readonly HashSet<string> _read = new HashSet<string>();

            public Fields(string key, string part, JsonElement body)
            {
                _where = $"{FileName}: {key}.{part}";
                foreach (JsonProperty p in body.EnumerateObject())
                {
                    _values[p.Name] = p.Value;
                }
            }

            public JsonElement Raw(string name)
            {
                if (!_values.TryGetValue(name, out JsonElement value))
                {
                    throw new DefinitionLoadException($"{_where}: missing field '{name}'");
                }
                _read.Add(name);
                return value;
            }

            public string String(string name) => Raw(name).GetString() ?? "";
            public int Int(string name) => Raw(name).GetInt32();

            public bool Bool(string name)
            {
                int v = Int(name);
                return v == 1 || (v == 0 ? false : throw new DefinitionLoadException($"{_where}.{name}: bool must be 0 or 1, got {v}"));
            }

            public IEnumerable<string> Names(string name) =>
                Raw(name).EnumerateArray().Select(e => e.GetString()).ToList();

            public List<RecipeIngredient> Cost(string name) =>
                Raw(name).EnumerateArray()
                    .Select(e => new RecipeIngredient(e.GetProperty("itemType").GetString(), e.GetProperty("quantity").GetInt32()))
                    .ToList();

            public void Ignore(string name) => Raw(name);

            public void IgnoreAll() => _read.UnionWith(_values.Keys);

            public void AssertAllRead()
            {
                string[] unread = _values.Keys.Where(k => !_read.Contains(k)).ToArray();
                if (unread.Length > 0)
                {
                    throw new DefinitionLoadException($"{_where}: unknown field(s) {string.Join(", ", unread)}");
                }
            }
        }
    }
}
