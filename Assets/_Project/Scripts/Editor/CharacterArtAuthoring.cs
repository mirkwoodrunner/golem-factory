using UnityEditor;
using UnityEngine;

namespace GolemFactory.Editor
{
    // Import settings for the art that STANDS ON the floor rather than being part of it: the five
    // chassis and the three generic golem bodies.
    //
    // BOTTOMCENTER, y = 0, and the point of it is that the transform then means the same thing for
    // a golem as it does for everything else in the world. A cell has one world position
    // (GridCoordinateConverter.CellToWorldCenter); a golem placed on that cell should stand on it.
    // These eight sprites were Center-pivoted, and at 64x96 with PPU 64 that is 96/2/64 = 0.75
    // world units, so every golem in the game rendered three quarters of a tile SOUTH of the tile
    // it occupied -- pushing from a tile it was not visibly on, and drawn over the row in front.
    //
    // docs/open-items.md named BottomCenter as the convention for new world art before this
    // existed anywhere except three building .meta files. This is the pass that makes the
    // characters obey it too. Deliberately NOT extended to the items, overlays or floor tiles:
    // those are centred ON a cell rather than standing on one, and Center is right for them.
    //
    // Separate from SandboxFloorGenerator.ReimportEnvironmentArt because it is a different
    // convention, not a different list -- that one is PPU/pivot for pieces of the room, keyed off
    // a table of contact lines. Re-runnable and idempotent: it writes the same settings every
    // time, and Unity skips the reimport if nothing changed.
    public static class CharacterArtAuthoring
    {
        private const string ArtRoot = "Assets/_Project/Art/";

        // Same 64 art pixels per world unit as the environment, so a chassis is exactly one cell
        // wide and one and a half tall.
        private const float CharacterPixelsPerUnit = 64f;

        private static readonly string[] BottomCenterCharacters = BuildCharacterList();

        private static string[] BuildCharacterList()
        {
            var names = new System.Collections.Generic.List<string>
            {
                "chassis_clockwork_scavenger",
                "chassis_brass_presser",
                "chassis_aether_hauler",
                "chassis_mainspring_overclocker",
                "chassis_zeppelin_freight_loader",
                "golem_generic_brass",
                "golem_generic_copper",
                "golem_generic_steel",
            };

            // The Artificer's sixteen walk frames answer to exactly this convention, and for the
            // same reason: he stands on a cell rather than being centred on one. Listed here so the
            // menu item stays the single place these settings are stated -- otherwise the frames
            // are correct only for as long as nobody retunes the number above, and the walk cycle
            // silently drifts off the pivot every other character sprite uses.
            //
            // Their baseline is a uniform 3px off the bottom of the cell, so BottomCenter puts his
            // feet on the tile; Center would float him three quarters of a tile north of it, which
            // is the bug this pass was originally written to fix for the chassis.
            foreach (string direction in new[] { "down", "left", "right", "up" })
            {
                for (int frame = 0; frame < 4; frame++)
                {
                    names.Add("artificer_walk_" + direction + "_" + frame);
                }
            }

            return names.ToArray();
        }

        [MenuItem("Tools/Golem Factory/Reimport Character Art (BottomCenter)")]
        public static void ApplyCharacterPivots()
        {
            int changed = 0;
            foreach (string spriteName in BottomCenterCharacters)
            {
                string path = ArtRoot + spriteName + ".png";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    Debug.LogWarning("CharacterArtAuthoring: missing character texture " + path);
                    continue;
                }

                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.textureType = TextureImporterType.Sprite;
                settings.spriteMode = (int)SpriteImportMode.Single;
                // BOTH, and in this order. Unity honours spriteAlignment and treats spritePivot as
                // meaningful only under Custom -- which is exactly how `player.png` ended up
                // recorded as alignment 7 with a stored pivot of {0.5, 0.5}, behaving as
                // BottomCenter while reading as centre to anyone auditing the .meta by eye.
                // Writing the matching pivot costs nothing and stops these eight becoming the
                // same trap.
                settings.spriteAlignment = (int)SpriteAlignment.BottomCenter;
                settings.spritePivot = new Vector2(0.5f, 0f);
                settings.spritePixelsPerUnit = CharacterPixelsPerUnit;
                settings.filterMode = FilterMode.Point;
                settings.mipmapEnabled = false;
                settings.alphaIsTransparency = true;
                importer.SetTextureSettings(settings);
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                changed++;
            }

            AssetDatabase.Refresh();
            Debug.Log("CharacterArtAuthoring: reimported " + changed
                      + " character textures at BottomCenter, PPU " + CharacterPixelsPerUnit + ".");
        }
    }
}
