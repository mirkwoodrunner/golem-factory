using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using GolemFactory.ClockTower;
using GolemFactory.Diagnostics;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.Player;
using GolemFactory.World;
using CoreVector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// The art itself, loaded and placed for real (docs/art-pipeline-design.md §7.1):
    /// <list type="number">
    /// <item>every entry of <c>Tools/Art/art_manifest.json</c> loads as a texture of its size,
    /// every <c>res://art/**.png</c> has an entry, and every name the data or the code builds
    /// exists (the data's <c>.png</c> names, <c>ItemIcons.PathFor</c> for every good, the tower's
    /// pictures, the 16 walk frames, and every <c>GolemFrames</c> name of every chassis, which
    /// resolves through the fallback while a chassis's set hasn't landed);</item>
    /// <item>every standing sprite the shell, the buildings and the golems place has its lowest
    /// opaque row (read from the texture's image) on its node's origin, within the manifest's
    /// allowance: the art's foot row against the code's pivot;</item>
    /// <item>the gallery (Core's <see cref="ArtGallery"/>): each chassis's 28 frames, pinned,
    /// captured with <c>--capture-dir</c> as <c>art-golems-&lt;chassis&gt;-default/close/wide.png</c>,
    /// the review sheet's in-game columns.</item>
    /// </list>
    /// The rest of §7.1's gallery (moods, carry and hover rows, the Artificer, buildings, goods,
    /// the lit room, the Workbench and the HUD) and its in-game readability check land with the
    /// sets that need them.
    /// </summary>
    public sealed class ArtScenario : IScenario
    {
        private const string Wait = "wait";
        private const int MaxWaitFrames = 240;
        private static readonly CoreVector2Int GalleryTopLeft = new CoreVector2Int(-6, -3); // clear of the bench (0, 3) and the station (3, -1)
        private static readonly (string name, float size)[] Zooms =
        {
            ("default", CameraRigRules.DefaultOrthographicSize),
            ("close", CameraRigRules.MinOrthographicSize),
            ("wide", CameraRigRules.MaxOrthographicSize),
        };

        private readonly Queue<(string name, Func<string> step)> _steps = new Queue<(string, Func<string>)>();
        private readonly List<string> _log = new List<string>();
        private SceneTree _tree;
        private SandboxWorld _world;
        private PlayerNode _player;
        private Dictionary<string, JsonElement> _manifest;
        private string _failure;
        private int _waited;

        public void Begin(ScenarioRunner runner)
        {
            _tree = runner.GetTree();
            _world = runner.World.Sandbox;
            _player = _tree.Root.FindChild("Player", true, false) as PlayerNode;
            if (_world?.Setup == null || _player == null)
            {
                _failure = "Sandbox.tscn is missing its setup or player";
                return;
            }
            string path = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "Tools", "Art", "art_manifest.json"));
            if (!File.Exists(path))
            {
                _failure = "no manifest at " + path;
                return;
            }
            _manifest = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path));

            Do("every manifest entry loads at its size", CheckManifestLoads);
            Do("every sprite under res://art has an entry", CheckEveryFileListed);
            Do("every name the data and the code build exists", CheckBuiltNames);
            Do("hide the player and the guide", () =>
            {
                _player.TeleportTo(new Compat.Vector3(-2f, -19f, 0f));
                _player.Visible = false;
                _world.Tutorial?.Playtest?.Skip(0f);
                return null;
            });
            foreach (string chassis in _world.Definitions.Chassis.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                PlanChassis(chassis);
            }
            Do("standing sprites stand on their foot rows", CheckFeet);
        }

        // --- 1. Loads and names ---------------------------------------------------------------

        private string CheckManifestLoads()
        {
            var problems = new List<string>();
            foreach ((string rel, JsonElement entry) in _manifest)
            {
                var texture = ResourceLoader.Load<Texture2D>("res://art/" + rel);
                if (texture == null)
                {
                    problems.Add(rel + " does not load");
                    continue;
                }
                JsonElement size = entry.GetProperty("size");
                if (texture.GetWidth() != size[0].GetInt32() || texture.GetHeight() != size[1].GetInt32())
                {
                    problems.Add($"{rel} is {texture.GetWidth()}x{texture.GetHeight()}, the manifest says {size[0]}x{size[1]}");
                }
            }
            _log.Add($"{_manifest.Count} manifest entries load at their sizes");
            return problems.Count == 0 ? null : string.Join("; ", problems.Take(5));
        }

        private string CheckEveryFileListed()
        {
            var files = new List<string>();
            Walk("res://art", "", files);
            List<string> unlisted = files.Where(f => !_manifest.ContainsKey(f)).ToList();
            if (unlisted.Count > 0)
            {
                return "no manifest entry: " + string.Join(", ", unlisted.Take(5));
            }
            return files.Count == _manifest.Count ? null : $"{files.Count} files, {_manifest.Count} entries";
        }

        private static void Walk(string dir, string prefix, List<string> into)
        {
            foreach (string file in DirAccess.GetFilesAt(dir).Where(f => f.EndsWith(".png", StringComparison.Ordinal)))
            {
                into.Add(prefix + file);
            }
            foreach (string sub in DirAccess.GetDirectoriesAt(dir))
            {
                Walk(dir + "/" + sub, prefix + sub + "/", into);
            }
        }

        private string CheckBuiltNames()
        {
            var names = new List<string>();
            foreach (string file in Directory.GetFiles(ProjectSettings.GlobalizePath("res://data"), "*.json"))
            {
                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(file), "\"([A-Za-z0-9_]+\\.png)\""))
                {
                    names.Add(m.Groups[1].Value);
                }
            }
            names.AddRange(ItemTiers.CanonicalOrder.Select(g => ItemIcons.PathFor(g).Substring("res://art/".Length)));
            names.Add(ClockTowerArt.Site + ".png");
            for (int stage = 0; stage <= ClockTowerArt.FinalPicture; stage++)
            {
                names.Add(ClockTowerArt.Stage(stage) + ".png");
            }
            foreach (string row in new[] { "down", "left", "right", "up" })
            {
                for (int frame = 0; frame < ArtificerWalkAnimation.FramesPerDirection; frame++)
                {
                    names.Add($"artificer_walk_{row}_{frame}.png");
                }
            }
            names.Add(GolemFrames.FallbackSprite);
            List<string> missing = names.Distinct().Where(n => !ResourceLoader.Exists("res://art/" + n)).ToList();
            if (missing.Count > 0)
            {
                return "missing: " + string.Join(", ", missing.Take(8));
            }

            int frames = 0, fallen = 0;
            foreach (var chassis in _world.Definitions.Chassis.Values)
            {
                string stem = GolemFrames.Stem(chassis.chassisSprite);
                foreach (Facing facing in ArtGallery.Facings)
                {
                    foreach (GolemFrameKey key in ArtGallery.FramesOf(facing))
                    {
                        string resolved = GolemFrames.Resolve(stem, key, n => ResourceLoader.Exists("res://art/" + n));
                        if (resolved == GolemFrames.FallbackSprite)
                        {
                            return $"{chassis.name}'s {key} falls through to the generic golem: no south base";
                        }
                        frames++;
                        if (resolved != GolemFrames.FileName(stem, key))
                        {
                            fallen++;
                        }
                    }
                }
            }
            _log.Add($"{names.Distinct().Count()} named sprites exist; {frames} golem frames resolve ({fallen} through the fallback)");
            return null;
        }

        // --- 3. The gallery -------------------------------------------------------------------

        private void PlanChassis(string chassis)
        {
            List<GalleryGolem> gallery = null;
            Do($"build {chassis}'s gallery", () =>
            {
                gallery = ArtGallery.Build(_world, chassis, GalleryTopLeft);
                return null;
            });
            Do($"pin {chassis}'s frames", () =>
            {
                var nodes = Nodes();
                if (gallery.Any(g => !nodes.ContainsKey(g.Golem)))
                {
                    return Wait;
                }
                foreach (GalleryGolem g in gallery)
                {
                    nodes[g.Golem].PinnedFrame = g.Frame;
                }
                AimCamera(Zooms[0].size);
                return null;
            });
            Do($"{chassis}'s pinned frames are drawn", () =>
            {
                var nodes = Nodes();
                foreach (GalleryGolem g in gallery)
                {
                    GolemNode node = nodes[g.Golem];
                    string stem = GolemFrames.Stem(g.Golem.Program.chassis?.chassisSprite);
                    string want = GolemFrames.Resolve(stem, g.Frame, n => ResourceLoader.Exists("res://art/" + n));
                    if (!node.DrawnFrame.Equals(g.Frame) || node.BodySprite != want)
                    {
                        return Wait;
                    }
                }
                return null;
            });
            foreach ((string zoom, float size) in Zooms)
            {
                Do($"{chassis} at the {zoom} zoom", () => { AimCamera(size); return null; });
                Do($"settle {zoom}", () => null);
                Do($"capture {chassis} {zoom}", () =>
                {
                    ScenarioCapture.Shot(_tree.Root, $"art-golems-{Snake(chassis)}-{zoom}");
                    return null;
                });
            }
            Do($"check {chassis}'s feet", CheckFeet);
            Do($"clear {chassis}'s gallery", () =>
            {
                foreach (GalleryGolem g in gallery)
                {
                    g.Golem.Remove();
                }
                _log.Add($"{chassis}: 28 frames pinned and drawn");
                return null;
            });
        }

        private Dictionary<GolemEntity, GolemNode> Nodes() =>
            _tree.GetNodesInGroup(GolemNodeGroup.Name).OfType<GolemNode>()
                .Where(n => !n.Entity.IsRemoved)
                .ToDictionary(n => n.Entity, n => n);

        private void AimCamera(float size)
        {
            var centre = new Compat.Vector2(GalleryTopLeft.x + 6f, GalleryTopLeft.y - 3.5f);
            Compat.Vector3 at = _player.CorePosition;
            foreach (CameraRigNode camera in _tree.Root.FindChildren("*", "Camera2D", true, false).OfType<CameraRigNode>())
            {
                camera.SetOrthographicSize(size);
                camera.Offset = new Vector2(centre.x - at.x, at.y - centre.y) * GridConversions.CellPixels;
            }
        }

        private static string Snake(string name) =>
            string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

        // --- 2. Feet --------------------------------------------------------------------------

        private int _feetChecked;

        /// <summary>
        /// Every standing Sprite2D under the entity layer whose file the manifest gives a foot row:
        /// how far its lowest opaque row sits above the line its offset puts on the node's origin.
        /// At most the entry's footSlack (one more for a fractional pivot, whose line is the top of
        /// its first shadow row), and never below its shadow rows.
        /// </summary>
        private string CheckFeet()
        {
            Node entities = _tree.Root.FindChild("Entities", true, false);
            if (entities == null)
            {
                return "no Entities layer";
            }
            var problems = new List<string>();
            var lowest = new Dictionary<string, int>();
            foreach (Sprite2D sprite in entities.FindChildren("*", "Sprite2D", true, false).OfType<Sprite2D>())
            {
                if (sprite.Texture == null || sprite.Centered || sprite.ZIndex < 0 || !sprite.IsVisibleInTree())
                {
                    continue;
                }
                string rel = sprite.Texture.ResourcePath.Replace("res://art/", "");
                if (!_manifest.TryGetValue(rel, out JsonElement entry) || entry.GetProperty("footRow").ValueKind == JsonValueKind.Null ||
                    entry.TryGetProperty("wallHung", out _))
                {
                    continue;
                }
                if (!lowest.TryGetValue(rel, out int low))
                {
                    // From the file, not the texture: a headless run's dummy renderer has no pixels.
                    low = lowest[rel] = LowestOpaqueRow(Image.LoadFromFile(ProjectSettings.GlobalizePath(sprite.Texture.ResourcePath)));
                }
                int line = Mathf.RoundToInt(-sprite.Offset.Y);
                int slack = entry.TryGetProperty("footSlack", out JsonElement s) ? s.GetInt32() : 0;
                bool fractional = entry.GetProperty("pivot").ValueKind == JsonValueKind.Array;
                int floats = line - (low + 1);
                int bottom = entry.TryGetProperty("shadowRows", out JsonElement rows) ? rows[1].GetInt32() : sprite.Texture.GetHeight() - 1;
                if (floats > slack + (fractional ? 1 : 0))
                {
                    problems.Add($"{sprite.GetParent()?.Name}/{rel} floats {floats}px above its origin");
                }
                else if (low > bottom)
                {
                    problems.Add($"{rel} draws below its shadow rows (row {low})");
                }
                _feetChecked++;
            }
            if (problems.Count > 0)
            {
                return string.Join("; ", problems.Distinct().Take(5));
            }
            return null;
        }

        private static int LowestOpaqueRow(Image image)
        {
            for (int y = image.GetHeight() - 1; y >= 0; y--)
            {
                for (int x = 0; x < image.GetWidth(); x++)
                {
                    if (image.GetPixel(x, y).A >= 0.5f)
                    {
                        return y;
                    }
                }
            }
            return -1;
        }

        // --- The runner -----------------------------------------------------------------------

        private void Do(string name, Func<string> step) => _steps.Enqueue((name, step));

        public ScenarioResult? Step(double delta)
        {
            if (_failure != null)
            {
                return new ScenarioResult(false, _failure);
            }
            if (_manifest == null)
            {
                return null; // Begin is deferred
            }
            if (_steps.Count == 0)
            {
                return new ScenarioResult(true, string.Join("; ", _log) + $"; {_feetChecked} standing sprites checked at their feet");
            }
            (string name, Func<string> step) = _steps.Peek();
            string result;
            try
            {
                result = step();
            }
            catch (Exception e)
            {
                return new ScenarioResult(false, $"{name}: {e.Message}");
            }
            if (result == Wait)
            {
                return ++_waited > MaxWaitFrames ? new ScenarioResult(false, $"{name}: timed out") : null;
            }
            if (result != null)
            {
                return new ScenarioResult(false, $"{name}: {result}");
            }
            _waited = 0;
            _steps.Dequeue();
            return null;
        }
    }
}
