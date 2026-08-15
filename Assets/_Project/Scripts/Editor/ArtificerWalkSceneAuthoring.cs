using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using GolemFactory.Player;

namespace GolemFactory.Editor
{
    // Puts the ArtificerWalkAnimator on the Sandbox player and fills in its sixteen frames.
    //
    // Scripted rather than hand-wired, per the repo's convention for Editor work, and idempotent:
    // find-or-add the component, rewrite the same array, save. Re-running it after an art or
    // ordering change is the intended way to apply that change, not a fresh manual pass.
    //
    // Configure() deliberately is NOT used here. It writes the private [SerializeField]s on a live
    // instance and none of that reaches disk -- SerializedObject is what actually persists, which
    // is the trap ProgressionSceneAuthoring documents at length.
    public static class ArtificerWalkSceneAuthoring
    {
        private const string SandboxScene = "Assets/_Project/Scenes/Sandbox.unity";
        private const string ArtRoot = "Assets/_Project/Art/";

        // Down, Left, Right, Up -- the order ArtificerFacing declares and ComputeSpriteIndex
        // flattens to. Changing this order silently remaps every direction, so it is stated once.
        private static readonly string[] Directions = { "down", "left", "right", "up" };

        [MenuItem("Tools/Golem Factory/Wire Artificer Walk Cycle")]
        public static void WireWalkCycle()
        {
            Sprite[] frames = LoadFrames();
            if (frames == null)
            {
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(SandboxScene, OpenSceneMode.Single);

            int wired = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (PlayerController player in root.GetComponentsInChildren<PlayerController>(true))
                {
                    WireOnto(player.gameObject, frames);
                    wired++;
                }
            }

            if (wired == 0)
            {
                Debug.LogWarning("ArtificerWalkSceneAuthoring: no PlayerController in " + SandboxScene + ".");
                return;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("ArtificerWalkSceneAuthoring: wired the walk cycle onto " + wired + " player(s).");
        }

        private static void WireOnto(GameObject player, Sprite[] frames)
        {
            var animator = player.GetComponent<ArtificerWalkAnimator>();
            if (animator == null)
            {
                animator = player.AddComponent<ArtificerWalkAnimator>();
            }

            var so = new SerializedObject(animator);
            SerializedProperty framesProperty = so.FindProperty("_frames");
            framesProperty.arraySize = frames.Length;
            for (int i = 0; i < frames.Length; i++)
            {
                framesProperty.GetArrayElementAtIndex(i).objectReferenceValue = frames[i];
            }

            so.FindProperty("_strideLength").floatValue = ArtificerWalkAnimation.DefaultStrideLength;
            so.FindProperty("_playerController").objectReferenceValue = player.GetComponent<PlayerController>();
            so.ApplyModifiedPropertiesWithoutUndo();

            // The renderer keeps player.png on disk otherwise, so the Scene view shows the old
            // static Artificer until someone presses Play -- ArtificerWalkAnimator has no
            // [ExecuteAlways], so nothing reassigns it in EditMode. Parking it on the standing
            // frame makes what you see when the scene is open match what you get when you run it.
            var renderer = player.GetComponent<SpriteRenderer>();
            if (renderer != null)
            {
                var rendererSo = new SerializedObject(renderer);
                rendererSo.FindProperty("m_Sprite").objectReferenceValue =
                    frames[ArtificerWalkAnimation.ComputeSpriteIndex(
                        ArtificerFacing.Down, ArtificerWalkAnimation.StandingFrameIndex)];
                rendererSo.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static Sprite[] LoadFrames()
        {
            var frames = new List<Sprite>();
            var missing = new List<string>();

            foreach (string direction in Directions)
            {
                for (int frame = 0; frame < ArtificerWalkAnimation.FramesPerDirection; frame++)
                {
                    string path = ArtRoot + "artificer_walk_" + direction + "_" + frame + ".png";
                    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    if (sprite == null)
                    {
                        missing.Add(path);
                    }

                    frames.Add(sprite);
                }
            }

            if (missing.Count > 0)
            {
                // Refuse rather than write a half-filled array: a null in the middle of the cycle
                // would show up as the Artificer flickering out on one frame of one direction,
                // which reads as a rendering glitch rather than as missing art.
                Debug.LogError("ArtificerWalkSceneAuthoring: missing " + missing.Count
                               + " walk frame(s), wrote nothing. First: " + missing[0]);
                return null;
            }

            return frames.ToArray();
        }
    }
}
