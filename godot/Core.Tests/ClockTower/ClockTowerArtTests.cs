using System.IO;
using System.Linq;
using GolemFactory.ClockTower;
using GolemFactory.Tests.Data;
using NUnit.Framework;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// G10: the Clock Tower wears one picture per stage completed, the roped-off site before
    /// that, and every picture exists in godot/art at the shared canvas size.
    /// </summary>
    public class ClockTowerArtTests
    {
        [Test]
        public void ARopedOffSite_WearsTheRope()
        {
            var site = new ClockTowerSite();
            site.OpenWhen = () => false;
            Assert.AreEqual(ClockTowerArt.Site, ClockTowerArt.SpriteFor(site));
        }

        [Test]
        public void AnOpenSite_WearsThePictureForTheStagesCompleted()
        {
            var site = new ClockTowerSite();
            Assert.AreEqual("clock_tower_stage0", ClockTowerArt.SpriteFor(site), "open, nothing built yet");
        }

        [TestCase(-1, "clock_tower_stage0")]
        [TestCase(2, "clock_tower_stage2")]
        [TestCase(4, "clock_tower_stage4")]
        [TestCase(9, "clock_tower_stage4")]
        public void StagePictures_AreCapped(int completed, string expected) =>
            Assert.AreEqual(expected, ClockTowerArt.Stage(completed));

        [Test]
        public void EveryPicture_IsGenerated_AtTheSharedCanvasSize()
        {
            string art = Path.Combine(Path.GetDirectoryName(AuthoredData.DataDirectory), "art");
            var names = new[] { ClockTowerArt.Site }
                .Concat(Enumerable.Range(0, ClockTowerArt.FinalPicture + 1).Select(ClockTowerArt.Stage));
            foreach (string name in names)
            {
                string path = Path.Combine(art, name + ".png");
                FileAssert.Exists(path);
                using (var stream = File.OpenRead(path))
                {
                    // PNG IHDR: width and height, big-endian, at bytes 16 and 20.
                    var header = new byte[24];
                    stream.Read(header, 0, 24);
                    int width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
                    int height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
                    Assert.AreEqual(ClockTowerArt.CanvasWidth, width, name);
                    Assert.AreEqual(ClockTowerArt.CanvasHeight, height, name);
                }
            }
        }
    }
}
