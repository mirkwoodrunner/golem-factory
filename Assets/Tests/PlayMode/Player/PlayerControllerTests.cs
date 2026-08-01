using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using GolemFactory.Player;
using GolemFactory.World;

namespace GolemFactory.Tests.PlayMode
{
    // Needs PlayMode since PlayerController.Awake (which adds YSortSpriteRenderer and
    // resolves the Move action) doesn't run outside Play Mode for a plain MonoBehaviour --
    // same gotcha GolemEntity.OnEnable/GolemVisual.Awake hit in M7/the graphics pass.
    public class PlayerControllerTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        private PlayerController Build()
        {
            _root = new GameObject("Player", typeof(SpriteRenderer));
            var controller = _root.AddComponent<PlayerController>();
            controller.Configure(null, moveSpeed: 4f);
            return controller;
        }

        [UnityTest]
        public IEnumerator MoveBy_AppliesDisplacementToTransformPosition()
        {
            PlayerController controller = Build();
            yield return null;
            Vector3 before = controller.transform.position;

            controller.MoveBy(Vector2.right, 0.5f);

            Vector3 after = controller.transform.position;
            Assert.AreEqual(before + new Vector3(2f, 0f, 0f), after);
        }

        [UnityTest]
        public IEnumerator MoveBy_ZeroInput_DoesNotMove()
        {
            PlayerController controller = Build();
            yield return null;
            Vector3 before = controller.transform.position;

            controller.MoveBy(Vector2.zero, 1f);

            Assert.AreEqual(before, controller.transform.position);
        }

        [UnityTest]
        public IEnumerator Awake_AddsYSortSpriteRenderer()
        {
            PlayerController controller = Build();
            yield return null;

            Assert.IsNotNull(controller.GetComponent<YSortSpriteRenderer>());
        }

        [UnityTest]
        public IEnumerator MoveBy_WithoutFloorBounds_LargeDisplacementIsUnclamped()
        {
            PlayerController controller = Build();
            yield return null;

            controller.MoveBy(Vector2.right, 100f);

            // Main.unity's player never calls SetFloorBounds -- confirms it stays unaffected.
            Assert.AreEqual(400f, controller.transform.position.x, 0.01f);
        }

        [UnityTest]
        public IEnumerator MoveBy_WithFloorBounds_ClampsToFloorEdge()
        {
            PlayerController controller = Build();
            var converter = new GridCoordinateConverter(new Vector2(1f, 0.5f));
            controller.SetFloorBounds(converter, 12);
            yield return null;

            // Set the position directly (rather than driving MoveBy with a huge world-space
            // displacement) since a pure world-X move actually traces a diagonal in cell space
            // -- isometric transforms mix axes -- and would hit a corner clamp instead of the
            // single-axis edge this test targets. Same setup FloorLayoutTests uses in EditMode.
            controller.transform.position = converter.CellToWorldCenter(new Vector2Int(20, 0));
            controller.MoveBy(Vector2.zero, 0f);

            Vector3 expectedEdge = converter.CellToWorldCenter(new Vector2Int(12, 0));
            Assert.AreEqual(expectedEdge.x, controller.transform.position.x, 0.01f);
            Assert.AreEqual(expectedEdge.y, controller.transform.position.y, 0.01f);
        }
    }
}
