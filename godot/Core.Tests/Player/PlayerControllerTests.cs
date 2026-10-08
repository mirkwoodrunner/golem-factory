using GolemFactory.Compat;
using GolemFactory.Player;
using GolemFactory.World;
using NUnit.Framework;

namespace GolemFactory.Tests.Player
{
    /// <summary>
    /// Ported from Unity's PlayMode PlayerControllerTests onto <see cref="PlayerWalker"/>, the
    /// rules PlayerController.MoveBy applied to its transform. Awake_AddsYSortSpriteRenderer is
    /// retired: y-sorting is the Godot scene's y_sort_enabled, and the `world` scenario draws it.
    /// </summary>
    public class PlayerControllerTests
    {
        private static PlayerWalker Build() => new PlayerWalker(moveSpeed: 4f);

        [Test]
        public void MoveBy_AppliesDisplacementToTransformPosition()
        {
            PlayerWalker walker = Build();
            Vector3 before = walker.Position;

            walker.MoveBy(Vector2.right, 0.5f);

            Assert.AreEqual(before + new Vector3(2f, 0f, 0f), walker.Position);
        }

        [Test]
        public void MoveBy_ZeroInput_DoesNotMove()
        {
            PlayerWalker walker = Build();
            Vector3 before = walker.Position;

            walker.MoveBy(Vector2.zero, 1f);

            Assert.AreEqual(before, walker.Position);
        }

        [Test]
        public void MoveBy_WithoutFloorBounds_LargeDisplacementIsUnclamped()
        {
            PlayerWalker walker = Build();

            walker.MoveBy(Vector2.right, 100f);

            Assert.AreEqual(400f, walker.Position.x, 0.01f);
        }

        [Test]
        public void MoveBy_WithFloorBounds_ClampsToFloorEdge()
        {
            PlayerWalker walker = Build();
            var converter = new GridCoordinateConverter(new Vector2(1f, 0.5f));
            walker.SetFloorBounds(converter, 12);

            walker.Position = converter.CellToWorldCenter(new Vector2Int(20, 0));
            walker.MoveBy(Vector2.zero, 0f);

            Vector3 expectedEdge = converter.CellToWorldCenter(new Vector2Int(12, 0));
            Assert.AreEqual(expectedEdge.x, walker.Position.x, 0.01f);
            Assert.AreEqual(expectedEdge.y, walker.Position.y, 0.01f);
        }

        [Test]
        public void MoveBy_WithFloorBounds_FollowsTheNorthWallAsTheRoomGrows()
        {
            // New with the port: Floor Expansion moves the clamp's north edge (Unity's
            // PlayerController read it from FloorBounds; here the Node pushes it).
            PlayerWalker walker = Build();
            var converter = new GridCoordinateConverter(new Vector2(1f, 1f));
            walker.SetFloorBounds(converter, 12);
            walker.SetNorthExtent(14);

            walker.Position = converter.CellToWorldCenter(new Vector2Int(0, 20));
            walker.MoveBy(Vector2.zero, 0f);

            Assert.AreEqual(converter.CellToWorldCenter(new Vector2Int(0, 14)).y, walker.Position.y, 0.01f);
        }
    }
}
