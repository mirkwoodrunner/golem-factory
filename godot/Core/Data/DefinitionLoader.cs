using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using GolemFactory.AssemblyLine;
using GolemFactory.ClockTower;
using GolemFactory.PunchCards;
using GolemFactory.Save;

namespace GolemFactory.Data
{
    /// <summary>
    /// Every authored definition, loaded from <c>godot/data/*.json</c>, keyed by name.
    /// </summary>
    public sealed class DefinitionSet
    {
        internal readonly Dictionary<Type, IDictionary> Tables = new Dictionary<Type, IDictionary>();

        public IReadOnlyDictionary<string, ChassisDefinition> Chassis => Table<ChassisDefinition>();
        public IReadOnlyDictionary<string, LogicCoreDefinition> LogicCores => Table<LogicCoreDefinition>();
        public IReadOnlyDictionary<string, AppendageActionDefinition> Appendages => Table<AppendageActionDefinition>();
        public IReadOnlyDictionary<string, RecipeDefinition> Recipes => Table<RecipeDefinition>();
        public IReadOnlyDictionary<string, ClockTowerStageDefinition> ClockTowerStages => Table<ClockTowerStageDefinition>();
        public IReadOnlyDictionary<string, DraftableCardDefinition> Cards => Table<DraftableCardDefinition>();
        public IReadOnlyDictionary<string, DraftableCardCatalog> Decks => Table<DraftableCardCatalog>();

        /// <summary>The save system's name-to-definition resolver, over everything loaded.</summary>
        public DefinitionCatalog ToCatalog() =>
            new DefinitionCatalog(Chassis.Values, LogicCores.Values, Appendages.Values);

        /// <summary>Every definition of type <typeparamref name="T"/>, by name.</summary>
        public IReadOnlyDictionary<string, T> Of<T>() =>
            Tables.TryGetValue(typeof(T), out IDictionary table)
                ? (Dictionary<string, T>)table
                : throw new ArgumentException($"{typeof(T).Name} is not an authored definition type");

        private Dictionary<string, T> Table<T>() => (Dictionary<string, T>)Tables[typeof(T)];
    }

    /// <summary>A definition file that does not match the classes that read it.</summary>
    public sealed class DefinitionLoadException : Exception
    {
        public DefinitionLoadException(string message) : base(message) { }
        public DefinitionLoadException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Loads the authored definitions -- what Unity kept as ScriptableObject .asset files --
    /// from <c>godot/data/*.json</c>, hand-edited since the G10 cutover.
    ///
    /// <para>
    /// <b>Strict by design.</b> A JSON key with no matching field, a reference to a name that
    /// does not exist, and an enum value the enum does not define are all errors, naming the
    /// file and the field. Unity's serializer silently drops the first and nulls the second,
    /// which is how an authored value goes missing without anyone noticing; here the build
    /// refuses to start instead. The enum check matters doubly because
    /// <c>AppendageActionType</c> is append-only and serialized by index.
    /// </para>
    ///
    /// <para>
    /// Two passes, so references can point anywhere -- including card to card
    /// (<c>prerequisiteCards</c>): every object is created and named first, then every field is
    /// filled. Fields are matched by name with the same visibility rule Unity's serializer
    /// used (public or private instance fields), so the JSON keys are the C# field names and
    /// the converter needs no copy of the schema. Unity's integer bools (0/1) are accepted.
    /// </para>
    /// </summary>
    public static class DefinitionLoader
    {
        /// <summary>File name per definition class, in the order they are created.</summary>
        public static readonly IReadOnlyList<KeyValuePair<string, Type>> Files = new[]
        {
            new KeyValuePair<string, Type>("recipes.json", typeof(RecipeDefinition)),
            new KeyValuePair<string, Type>("chassis.json", typeof(ChassisDefinition)),
            new KeyValuePair<string, Type>("logic_cores.json", typeof(LogicCoreDefinition)),
            new KeyValuePair<string, Type>("appendages.json", typeof(AppendageActionDefinition)),
            new KeyValuePair<string, Type>("clock_tower_stages.json", typeof(ClockTowerStageDefinition)),
            new KeyValuePair<string, Type>("assembly_line_cards.json", typeof(DraftableCardDefinition)),
            new KeyValuePair<string, Type>("assembly_line_decks.json", typeof(DraftableCardCatalog)),
        };

        private const BindingFlags FieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static DefinitionSet LoadFromDirectory(string directory) =>
            Load(file => File.ReadAllText(Path.Combine(directory, file)));

        /// <summary>
        /// Loads every file through <paramref name="readFile"/>, which maps a file name to its
        /// text. The Godot layer passes a reader over <c>res://data/</c>; tests pass a directory.
        /// </summary>
        public static DefinitionSet Load(Func<string, string> readFile)
        {
            var set = new DefinitionSet();
            var documents = new List<(string File, Type Type, JsonDocument Doc)>();
            try
            {
                foreach (KeyValuePair<string, Type> entry in Files)
                {
                    JsonDocument doc = JsonDocument.Parse(readFile(entry.Key));
                    documents.Add((entry.Key, entry.Value, doc));
                    IDictionary table = (IDictionary)Activator.CreateInstance(
                        typeof(Dictionary<,>).MakeGenericType(typeof(string), entry.Value));
                    foreach (JsonProperty definition in doc.RootElement.EnumerateObject())
                    {
                        object instance = Activator.CreateInstance(entry.Value);
                        NameField(entry.Value).SetValue(instance, definition.Name);
                        table.Add(definition.Name, instance);
                    }
                    set.Tables[entry.Value] = table;
                }

                foreach ((string file, Type type, JsonDocument doc) in documents)
                {
                    foreach (JsonProperty definition in doc.RootElement.EnumerateObject())
                    {
                        object instance = set.Tables[type][definition.Name];
                        Fill(instance, type, definition.Value, set, $"{file}: {definition.Name}");
                    }
                }
            }
            finally
            {
                foreach ((_, _, JsonDocument doc) in documents)
                {
                    doc.Dispose();
                }
            }
            return set;
        }

        private static FieldInfo NameField(Type type) =>
            type.GetField("name", FieldFlags)
            ?? throw new DefinitionLoadException($"{type.Name} has no 'name' field to key it by");

        private static object Fill(object target, Type type, JsonElement json, DefinitionSet set, string where)
        {
            if (json.ValueKind != JsonValueKind.Object)
            {
                throw new DefinitionLoadException($"{where}: expected an object, found {json.ValueKind}");
            }
            foreach (JsonProperty property in json.EnumerateObject())
            {
                FieldInfo field = type.GetField(property.Name, FieldFlags);
                if (field == null || field.IsInitOnly || field.IsLiteral)
                {
                    throw new DefinitionLoadException(
                        $"{where}.{property.Name}: {type.Name} has no writable field by that name");
                }
                field.SetValue(target, Convert(property.Value, field.FieldType, set, $"{where}.{property.Name}"));
            }
            return target;
        }

        private static object Convert(JsonElement json, Type type, DefinitionSet set, string where)
        {
            if (json.ValueKind == JsonValueKind.Null)
            {
                if (type.IsValueType)
                {
                    throw new DefinitionLoadException($"{where}: null for a {type.Name}");
                }
                return null;
            }

            try
            {
                if (type == typeof(string))
                {
                    return json.GetString();
                }
                if (type == typeof(int))
                {
                    return json.GetInt32();
                }
                if (type == typeof(long))
                {
                    return json.GetInt64();
                }
                if (type == typeof(float))
                {
                    return json.GetSingle();
                }
                if (type == typeof(double))
                {
                    return json.GetDouble();
                }
                if (type == typeof(bool))
                {
                    // Unity writes bools as 0/1; accept real JSON bools too, for hand edits.
                    if (json.ValueKind == JsonValueKind.True || json.ValueKind == JsonValueKind.False)
                    {
                        return json.GetBoolean();
                    }
                    int flag = json.GetInt32();
                    if (flag != 0 && flag != 1)
                    {
                        throw new DefinitionLoadException($"{where}: {flag} is not a bool");
                    }
                    return flag == 1;
                }
                if (type.IsEnum)
                {
                    object value = json.ValueKind == JsonValueKind.String
                        ? Enum.Parse(type, json.GetString())
                        : Enum.ToObject(type, json.GetInt32());
                    if (!Enum.IsDefined(type, value))
                    {
                        throw new DefinitionLoadException($"{where}: {json} is not a defined {type.Name}");
                    }
                    return value;
                }
            }
            catch (Exception e) when (e is InvalidOperationException || e is FormatException || e is ArgumentException)
            {
                throw new DefinitionLoadException($"{where}: {json} cannot be read as {type.Name} ({e.Message})");
            }

            if (set.Tables.TryGetValue(type, out IDictionary table))
            {
                // A reference to another definition, by name.
                string name = json.ValueKind == JsonValueKind.String
                    ? json.GetString()
                    : throw new DefinitionLoadException($"{where}: a {type.Name} reference must be a name");
                if (!table.Contains(name))
                {
                    throw new DefinitionLoadException($"{where}: no {type.Name} named '{name}'");
                }
                return table[name];
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                if (json.ValueKind != JsonValueKind.Array)
                {
                    throw new DefinitionLoadException($"{where}: expected a list, found {json.ValueKind}");
                }
                Type element = type.GetGenericArguments()[0];
                IList list = (IList)Activator.CreateInstance(type);
                int index = 0;
                foreach (JsonElement item in json.EnumerateArray())
                {
                    list.Add(Convert(item, element, set, $"{where}[{index++}]"));
                }
                return list;
            }

            // A nested value (RecipeIngredient, StageDemand): filled field by field, boxed so
            // a struct's fields can be set before it is copied into place.
            object nested = Activator.CreateInstance(type);
            return Fill(nested, type, json, set, where);
        }
    }
}
