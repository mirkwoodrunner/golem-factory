using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GolemFactory.Data;
using NUnit.Framework;

namespace GolemFactory.Tests
{
    /// <summary>
    /// The authored definitions in <c>godot/data/</c>, for tests that pin the real content --
    /// the recipe table, the chassis roster, the tower's stages -- rather than fixtures.
    ///
    /// <para>
    /// Replaces the Unity suite's <c>AssetDatabase.FindAssets</c> + <c>LoadAssetAtPath</c>
    /// helpers. Loads FRESH on every call, as AssetDatabase effectively did per test run: a
    /// test that mutates a definition must not leak into the next one.
    /// </para>
    /// </summary>
    internal static class AuthoredData
    {
        private static readonly Lazy<string> Directory = new Lazy<string>(FindDataDirectory);

        public static string DataDirectory => Directory.Value;

        public static DefinitionSet Load() => DefinitionLoader.LoadFromDirectory(DataDirectory);

        /// <summary>
        /// Every definition of type <typeparamref name="T"/>, in name order -- the order
        /// <c>FindAssets</c> returned within one folder, since Unity's asset paths were the names.
        /// </summary>
        public static List<T> All<T>() =>
            Load().Of<T>().OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Value).ToList();

        // The test assembly runs from godot/Core.Tests/bin/<config>/<tfm>/; walk up to godot/.
        private static string FindDataDirectory()
        {
            for (DirectoryInfo dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
                 dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "data");
                if (File.Exists(Path.Combine(candidate, "chassis.json")))
                {
                    return candidate;
                }
            }
            throw new DirectoryNotFoundException("godot/data not found above the test directory");
        }
    }
}
