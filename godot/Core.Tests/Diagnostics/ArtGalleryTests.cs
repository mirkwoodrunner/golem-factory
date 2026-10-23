using System.Linq;
using GolemFactory.Diagnostics;
using GolemFactory.Golems;
using GolemFactory.World;
using NUnit.Framework;
using static GolemFactory.Tests.EditMode.CrateWorld;
using Vector2Int = GolemFactory.Compat.Vector2Int;

namespace GolemFactory.Tests.EditMode
{
    // The `art` scenario's gallery (docs/art-pipeline-design.md §7.1): every frame of a chassis
    // in every facing, one golem each, on its own cell.
    public class ArtGalleryTests
    {
        [Test]
        public void AFacingHasSevenFrames_TheBaseFirst()
        {
            var frames = ArtGallery.FramesOf(Facing.East);
            Assert.AreEqual(7, frames.Count);
            Assert.AreEqual(GolemFrameKey.Base(Facing.East), frames[0]);
            Assert.IsTrue(frames.All(f => f.Facing == Facing.East));
            Assert.AreEqual(7, frames.Distinct().Count());
        }

        [Test]
        public void EveryChassis_Builds28Golems_EachOnItsOwnCellInItsFrameFacing()
        {
            SandboxWorld world = Compose();
            var topLeft = new Vector2Int(-6, -10);
            foreach (string chassis in world.Definitions.Chassis.Keys)
            {
                var gallery = ArtGallery.Build(world, chassis, topLeft);
                Assert.AreEqual(28, gallery.Count, chassis);
                Assert.AreEqual(28, gallery.Select(g => g.Frame).Distinct().Count(), chassis);
                Assert.AreEqual(28, gallery.Select(g => g.Golem.Cell).Distinct().Count(), chassis);
                foreach (GalleryGolem g in gallery)
                {
                    Assert.AreEqual(g.Frame.Facing, g.Golem.Facing);
                    Assert.AreEqual(ArtGallery.CellFor(topLeft, g.Frame), g.Golem.Cell);
                    Assert.AreEqual(chassis, g.Golem.Program.chassis.name);
                }
                foreach (GalleryGolem g in gallery)
                {
                    g.Golem.Remove();
                }
            }
        }

        [Test]
        public void ACrop_IsTheBoxAroundTheFeet_AtTheZoom()
        {
            var crop = ArtGallery.CropRect(new GolemFactory.Compat.Vector2(640f, 400f), 2f, 1280, 720);
            Assert.IsTrue(crop.HasValue);
            Assert.AreEqual((640 - 96, 400 - 224, 192, 256), crop.Value);

            // At 0.56 a pixel (the default zoom at 720 high) the box shrinks with the art.
            var small = ArtGallery.CropRect(new GolemFactory.Compat.Vector2(640f, 400f), 0.5625f, 1280, 720).Value;
            Assert.AreEqual(54, small.width);
            Assert.AreEqual(72, small.height);
            Assert.AreEqual(400 - 63, small.y);
        }

        [Test]
        public void ACropOffScreen_IsNull_SoTheCameraIsAimedFirst()
        {
            Assert.IsNull(ArtGallery.CropRect(new GolemFactory.Compat.Vector2(20f, 400f), 2f, 1280, 720));
            Assert.IsNull(ArtGallery.CropRect(new GolemFactory.Compat.Vector2(640f, 100f), 2f, 1280, 720));
            Assert.IsNull(ArtGallery.CropRect(new GolemFactory.Compat.Vector2(640f, 700f), 2f, 1280, 720));
            Assert.IsNull(ArtGallery.CropRect(new GolemFactory.Compat.Vector2(640f, 400f), 0f, 1280, 720));
        }

        [Test]
        public void TheGrid_KeepsBodiesApart()
        {
            var topLeft = new Vector2Int(0, 0);
            var cells = ArtGallery.Facings.SelectMany(ArtGallery.FramesOf).Select(f => ArtGallery.CellFor(topLeft, f)).ToList();
            foreach (Vector2Int a in cells)
            {
                foreach (Vector2Int b in cells.Where(c => !c.Equals(a)))
                {
                    // A body is one cell wide and 1.5 tall: two cells apart in either axis never touch.
                    Assert.IsTrue(System.Math.Abs(a.x - b.x) >= 2 || System.Math.Abs(a.y - b.y) >= 2, $"{a} {b}");
                }
            }
        }
    }
}
