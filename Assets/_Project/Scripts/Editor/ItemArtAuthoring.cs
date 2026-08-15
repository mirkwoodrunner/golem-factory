using UnityEditor;
using UnityEngine;

namespace GolemFactory.Editor
{
    // Import settings for the goods -- the 24 item icons from progression-design section 5.1.
    //
    // CENTRE PIVOT AT PPU 64, which is the third of this project's three sprite conventions and
    // the right one here: an item is centred ON a cell rather than standing on one, so it takes
    // Center like the floor tiles and cursor overlays, not the BottomCenter that characters and
    // buildings take (see CharacterArtAuthoring) or the contact-line Custom pivots the room's own
    // pieces take (see SandboxFloorGenerator).
    //
    // PPU 64 against a 32x32 file puts an item at half a cell, and the art is drawn native at 32
    // so one item pixel is exactly one floor pixel. The eighteen new goods would otherwise import
    // at Unity's defaults -- PPU 100, bilinear filtering, compressed -- which at this size is a
    // blurred smear, and the six older ones were only correct because someone set them by hand
    // years ago and nothing recorded why.
    public static class ItemArtAuthoring
    {
        private const string ArtRoot = "Assets/_Project/Art/";
        private const float ItemPixelsPerUnit = 64f;

        // Every ItemType in section 5.1, in ladder order. The list is here rather than derived
        // from the folder so a good with no icon is a NOTICEABLE failure -- a missing file logs a
        // warning naming it, instead of the sweep silently importing whatever happens to exist.
        private static readonly string[] ItemSprites =
        {
            "item_scrap", "item_coal", "item_copper_ore", "item_zinc_ore", "item_aether",
            "item_coke", "item_iron_plate", "item_slag", "item_glass",
            "item_copper_ingot", "item_zinc_ingot", "item_brass",
            "item_copper_wire", "item_gear", "item_casing", "item_lens", "item_mainspring",
            "item_aether_cell", "item_mechanism", "item_regulator",
            "item_frame_section", "item_great_cog", "item_aether_conduit", "item_chronometer_core",
        };

        [MenuItem("Tools/Golem Factory/Reimport Item Art")]
        public static void ApplyItemImportSettings()
        {
            int changed = 0;
            foreach (string spriteName in ItemSprites)
            {
                string path = ArtRoot + spriteName + ".png";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    Debug.LogWarning("ItemArtAuthoring: missing item texture " + path);
                    continue;
                }

                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.textureType = TextureImporterType.Sprite;
                settings.spriteMode = (int)SpriteImportMode.Single;
                settings.spriteAlignment = (int)SpriteAlignment.Center;
                settings.spritePivot = new Vector2(0.5f, 0.5f);
                settings.spritePixelsPerUnit = ItemPixelsPerUnit;
                settings.filterMode = FilterMode.Point;
                settings.mipmapEnabled = false;
                settings.alphaIsTransparency = true;
                importer.SetTextureSettings(settings);
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                changed++;
            }

            AssetDatabase.Refresh();
            Debug.Log("ItemArtAuthoring: reimported " + changed + " of " + ItemSprites.Length
                      + " item textures at Center, PPU " + ItemPixelsPerUnit + ".");
        }
    }
}
