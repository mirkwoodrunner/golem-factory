using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using GolemFactory.AssemblyLine;
using GolemFactory.Data;
using GolemFactory.PunchCards;
using NUnit.Framework;

namespace GolemFactory.Tests.Data
{
    /// <summary>
    /// The JSON that replaced Unity's .asset files, and the loader that reads it.
    ///
    /// <para>
    /// The parity test is the one that matters: it reads every field the loader set back out
    /// and compares it with the JSON value it came from. Together with the loader refusing
    /// unknown keys, that proves the round trip YAML → JSON → objects lost and altered nothing,
    /// without a second, hand-kept copy of the schema to drift.
    /// </para>
    /// </summary>
    public class DefinitionLoaderTests
    {
        private const BindingFlags FieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        // How many .asset files of each kind Unity has (Assets/_Project/ScriptableObjects/).
        [TestCase(typeof(RecipeDefinition), 19)]
        [TestCase(typeof(ChassisDefinition), 5)]
        [TestCase(typeof(LogicCoreDefinition), 2)]
        [TestCase(typeof(AppendageActionDefinition), 26)]
        [TestCase(typeof(ClockTower.ClockTowerStageDefinition), 4)]
        [TestCase(typeof(DraftableCardDefinition), 29)]
        [TestCase(typeof(DraftableCardCatalog), 1)]
        public void EveryAuthoredAssetIsLoaded(Type type, int expected)
        {
            DefinitionSet set = AuthoredData.Load();
            object table = typeof(DefinitionSet).GetMethod(nameof(DefinitionSet.Of)).MakeGenericMethod(type).Invoke(set, null);
            Assert.AreEqual(expected, ((IEnumerable)table).Cast<object>().Count(), type.Name);
        }

        [Test]
        public void EveryLoadedFieldEqualsItsJsonValue()
        {
            DefinitionSet set = AuthoredData.Load();
            int compared = 0;
            foreach (KeyValuePair<string, Type> file in DefinitionLoader.Files)
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AuthoredData.DataDirectory, file.Key)));
                var table = (IDictionary)typeof(DefinitionSet).GetMethod(nameof(DefinitionSet.Of))
                    .MakeGenericMethod(file.Value).Invoke(set, null);
                foreach (JsonProperty definition in doc.RootElement.EnumerateObject())
                {
                    object instance = table[definition.Name];
                    Assert.AreEqual(definition.Name, file.Value.GetField("name", FieldFlags).GetValue(instance));
                    compared += AssertMatches(instance, definition.Value, $"{file.Key}: {definition.Name}");
                }
            }
            Assert.Greater(compared, 500, "the parity walk should cover every authored field");
        }

        [Test]
        public void AppendageRecipeReferencesResolveToTheRecipeObject()
        {
            DefinitionSet set = AuthoredData.Load();
            AppendageActionDefinition card = set.Appendages["AssembleAetherConduit"];
            Assert.AreSame(set.Recipes["R18_AetherConduit"], card.recipe);
        }

        [Test]
        public void CatalogResolvesEveryChassisByName()
        {
            DefinitionSet set = AuthoredData.Load();
            Save.DefinitionCatalog catalog = set.ToCatalog();
            foreach (ChassisDefinition chassis in set.Chassis.Values)
            {
                Assert.AreSame(chassis, catalog.FindChassis(chassis.name), chassis.name);
            }
        }

        [Test]
        public void EveryFileTheLoaderReads_Exists()
        {
            // The JSON has been hand-edited since the G10 cutover; the converter that once
            // generated it from Unity's assets is in git history at the unity-final tag.
            foreach (KeyValuePair<string, Type> file in DefinitionLoader.Files)
            {
                FileAssert.Exists(Path.Combine(AuthoredData.DataDirectory, file.Key));
            }
        }

        // --- Strictness: a bad file fails loudly, naming where -------------------------------

        [Test]
        public void UnknownKeyIsRefused()
        {
            var e = Assert.Throws<DefinitionLoadException>(() => LoadWith("recipes.json",
                "{\"R1\": {\"durationTicks\": 12, \"durationTiks\": 3}}"));
            StringAssert.Contains("recipes.json: R1.durationTiks", e.Message);
        }

        [Test]
        public void DanglingReferenceIsRefused()
        {
            var e = Assert.Throws<DefinitionLoadException>(() => LoadWith("appendages.json",
                "{\"Assemble\": {\"actionType\": 5, \"recipe\": \"NoSuchRecipe\"}}"));
            StringAssert.Contains("no RecipeDefinition named 'NoSuchRecipe'", e.Message);
        }

        [Test]
        public void UndefinedEnumValueIsRefused()
        {
            // AppendageActionType is append-only and serialized by index: an index past the end
            // is a card from a newer build, or a typo, and must not load as garbage.
            var e = Assert.Throws<DefinitionLoadException>(() => LoadWith("appendages.json",
                "{\"Bad\": {\"actionType\": 999}}"));
            StringAssert.Contains("not a defined AppendageActionType", e.Message);
        }

        [Test]
        public void UnityIntegerBoolsLoad_AndOutOfRangeOnesDoNot()
        {
            DefinitionSet set = LoadWith("chassis.json", "{\"C\": {\"allowsRepeat\": 1, \"allowsFreightLaunch\": true}}");
            Assert.IsTrue(set.Chassis["C"].allowsRepeat);
            Assert.IsTrue(set.Chassis["C"].allowsFreightLaunch);

            Assert.Throws<DefinitionLoadException>(() => LoadWith("chassis.json", "{\"C\": {\"allowsRepeat\": 2}}"));
        }

        [Test]
        public void CardsMayReferenceEachOther()
        {
            DefinitionSet set = LoadWith("assembly_line_cards.json",
                "{\"A\": {\"prerequisiteCards\": [\"B\"]}, \"B\": {\"prerequisiteCards\": [\"A\"]}}");
            Assert.AreSame(set.Cards["B"], set.Cards["A"].prerequisiteCards[0]);
            Assert.AreSame(set.Cards["A"], set.Cards["B"].prerequisiteCards[0]);
        }

        // Every file empty except the one under test.
        private static DefinitionSet LoadWith(string file, string json) =>
            DefinitionLoader.Load(name => name == file ? json : "{}");

        // Walks a JSON object against the object the loader filled, field by field. Returns the
        // number of leaf values compared.
        private static int AssertMatches(object actual, JsonElement expected, string where)
        {
            int compared = 0;
            foreach (JsonProperty property in expected.EnumerateObject())
            {
                FieldInfo field = actual.GetType().GetField(property.Name, FieldFlags);
                Assert.IsNotNull(field, $"{where}.{property.Name}: no field");
                compared += AssertValue(field.GetValue(actual), property.Value, $"{where}.{property.Name}");
            }
            return compared;
        }

        private static int AssertValue(object actual, JsonElement expected, string where)
        {
            switch (expected.ValueKind)
            {
                case JsonValueKind.Null:
                    Assert.IsNull(actual, where);
                    return 1;
                case JsonValueKind.String:
                    // A string is either a plain value or a reference by name.
                    string text = actual as string ?? (string)actual?.GetType().GetField("name", FieldFlags)?.GetValue(actual);
                    Assert.AreEqual(expected.GetString(), text, where);
                    return 1;
                case JsonValueKind.Number:
                    if (actual is bool flag)
                    {
                        Assert.AreEqual(expected.GetInt32() == 1, flag, where);
                    }
                    else if (actual is Enum)
                    {
                        Assert.AreEqual(expected.GetInt32(), Convert.ToInt32(actual), where);
                    }
                    else
                    {
                        Assert.AreEqual(expected.GetDouble(), Convert.ToDouble(actual), 1e-6, where);
                    }
                    return 1;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    Assert.AreEqual(expected.GetBoolean(), actual, where);
                    return 1;
                case JsonValueKind.Array:
                    var list = ((IEnumerable)actual).Cast<object>().ToList();
                    Assert.AreEqual(expected.GetArrayLength(), list.Count, $"{where}: length");
                    int sum = 0, i = 0;
                    foreach (JsonElement item in expected.EnumerateArray())
                    {
                        sum += AssertValue(list[i], item, $"{where}[{i}]");
                        i++;
                    }
                    return sum;
                case JsonValueKind.Object:
                    return AssertMatches(actual, expected, where);
                default:
                    Assert.Fail($"{where}: unexpected JSON {expected.ValueKind}");
                    return 0;
            }
        }
    }
}
