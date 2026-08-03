using UnityEditor;
using UnityEngine;

namespace GolemFactory.Editor
{
    // Mirrors SandboxFloorGenerator.ReimportEnvironmentArt for the belt sprites, for the same
    // reason it exists there: manage_texture's import-settings path is unreliable, and the PNGs
    // Tools/Art/generate_placeholder_art.py writes land in the project with whatever defaults
    // Unity guesses (PPU 100, bilinear, compressed), which silently makes every belt the wrong
    // size and blurry. Keep this in sync with the "32 art px per world unit, x4 upscale, PPU
    // 128" convention documented at the top of that script's environment section.
    public static class BeltArtImporter
    {
        private const string ArtRoot = "Assets/_Project/Art/";

        // Lane/arrow/roller are authored at the environment's x4 upscale -> PPU 128.
        private const float LanePixelsPerUnit = 128f;

        // The three cargo items are authored at 16 art px upscaled x2, so their PPU is half:
        // same 32-art-px-per-world density, but the files stay 32x32 (and therefore 0.5 world
        // units) so every existing scene reference keeps its size.
        private const float ItemPixelsPerUnit = 64f;

        private static readonly string[] LaneSprites = { "belt_lane", "belt_arrow", "belt_roller" };
        private static readonly string[] ItemSprites = { "item_scrap", "item_brass", "item_aether" };

        [MenuItem("Tools/Golem Factory/Reimport Belt Art")]
        public static void ReimportBeltArt()
        {
            int changed = 0;
            changed += Reimport(LaneSprites, LanePixelsPerUnit);
            changed += Reimport(ItemSprites, ItemPixelsPerUnit);
            AssetDatabase.Refresh();
            Debug.Log("BeltArtImporter: reimported " + changed + " belt textures.");
        }

        private static int Reimport(string[] spriteNames, float pixelsPerUnit)
        {
            int changed = 0;
            foreach (string spriteName in spriteNames)
            {
                string path = ArtRoot + spriteName + ".png";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    Debug.LogWarning("BeltArtImporter: missing belt texture " + path);
                    continue;
                }

                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.textureType = TextureImporterType.Sprite;
                settings.spriteMode = (int)SpriteImportMode.Single;
                settings.spriteAlignment = (int)SpriteAlignment.Center;
                settings.spritePixelsPerUnit = pixelsPerUnit;
                settings.filterMode = FilterMode.Point;
                settings.mipmapEnabled = false;
                settings.alphaIsTransparency = true;
                importer.SetTextureSettings(settings);
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                changed++;
            }

            return changed;
        }
    }
}
