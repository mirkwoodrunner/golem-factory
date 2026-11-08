using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Player;
using GolemFactory.Settings;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>docs/menus-design.md §5: Reduced motion and the colour-blind marks.</summary>
    public class MotionPreferenceTests
    {
        [Test]
        public void Off_EverythingPlays()
        {
            var m = new MotionPreference();
            Assert.IsTrue(m.Particles && m.Squash && m.Shake && m.GolemSway && m.Pulse && m.PopupRise
                          && m.CameraGlides && m.WorkbenchMotion);
            Assert.AreEqual(0.83f, m.Breathe(0.83f));
        }

        [Test]
        public void Reduced_StopsEveryDecorativeMotion()
        {
            var m = new MotionPreference { Reduced = true };
            Assert.IsFalse(m.Particles || m.Squash || m.Shake || m.GolemSway || m.Pulse || m.PopupRise
                           || m.CameraGlides || m.WorkbenchMotion);
            Assert.AreEqual(MotionPreference.StillPulse, m.Breathe(0.83f));
        }

        [Test]
        public void TheWorldAndItsCameraShareOnePreference()
        {
            SandboxWorld world = CrateWorld.Compose();
            world.Motion.Reduced = true;
            Assert.IsFalse(world.Camera.Motion.CameraGlides);
        }

        [Test]
        public void Reduced_TheZoomCutsToItsLevel()
        {
            var camera = new CameraDirector();
            camera.ZoomStep(1);
            float glided = camera.GlideZoom(CameraDirector.ZoomLevels[CameraDirector.DefaultZoomLevel], 1f / 60f);
            Assert.AreNotEqual(camera.TargetSize, glided, "a normal zoom eases");

            camera.Motion.Reduced = true;
            Assert.AreEqual(camera.TargetSize, camera.GlideZoom(CameraDirector.ZoomLevels[CameraDirector.DefaultZoomLevel], 1f / 60f));
        }

        [Test]
        public void Reduced_TheCameraCutsToItsTarget_EvenOnAZeroLengthFrame()
        {
            var camera = new CameraDirector();
            camera.Motion.Reduced = true;
            Assert.IsTrue(float.IsPositiveInfinity(camera.GlideSpeed));
            var from = new Vector2(0f, 0f);
            var to = new Vector2(300f, -120f);
            Assert.AreEqual(to, CameraRigRules.Follow(from, to, 1f / 60f, camera.GlideSpeed));
            Assert.AreEqual(to, CameraRigRules.Follow(from, to, 0f, camera.GlideSpeed), "no NaN on a 0 s frame");
        }

        [Test]
        public void Reduced_TheTowerEventKeepsItsTimeline()
        {
            var camera = new CameraDirector();
            camera.Motion.Reduced = true;
            camera.BeginTowerEvent(2, "The Movement");
            camera.UpdateTowerEvent(CameraDirector.TowerGlideInSeconds + CameraDirector.TowerRevealAt + 0.01f);
            Assert.IsTrue(camera.TowerRevealed, "the stage still shows, with its banner");
        }
    }

    public class ColourMarksTests
    {
        [TearDown]
        public void Restore() => ColourMarks.Enabled = false;

        [Test]
        public void Off_TheGuideSaysTealAndGold()
        {
            ColourMarks.Enabled = false;
            Assert.AreEqual("teal is behind it, gold in front",
                TextTokens.Fill(ColourMarks.SourceToken + " is behind it, " + ColourMarks.TargetToken + " in front"));
        }

        [Test]
        public void On_TheGuideNamesTheStripes()
        {
            ColourMarks.Enabled = true;
            Assert.AreEqual("striped teal is behind it, plain gold in front",
                TextTokens.Fill(ColourMarks.SourceToken + " is behind it, " + ColourMarks.TargetToken + " in front"));
        }

        [Test]
        public void Toggling_ChangesTheTextVersion()
        {
            int before = TextTokens.Version;
            ColourMarks.Enabled = true;
            Assert.AreNotEqual(before, TextTokens.Version);
            int on = TextTokens.Version;
            ColourMarks.Enabled = true; // no change, no bump
            Assert.AreEqual(on, TextTokens.Version);
        }

        [Test]
        public void Stripes_CoverAboutAThirdOfTheTile_InDiagonalBands()
        {
            int lit = 0;
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    lit += ColourMarks.StripeAt(x, y) ? 1 : 0;
                }
            }
            float share = lit / (64f * 64f);
            Assert.That(share, Is.InRange(0.35f, 0.48f));
            Assert.AreEqual(ColourMarks.StripeAt(3, 4), ColourMarks.StripeAt(4, 3), "a diagonal band");
            Assert.AreEqual(ColourMarks.StripeAt(-1, 0), ColourMarks.StripeAt(ColourMarks.StripePeriod - 1, 0), "negative cells wrap");
        }

        [Test]
        public void EveryGuideStep_NamesTheTilesThroughTheTokens()
        {
            SandboxWorld world = CrateWorld.Compose();
            var colour = new Regex(@"\b(teal|gold)\b");
            ColourMarks.Enabled = true;
            int named = 0;
            foreach (var step in world.Tutorial.Steps)
            {
                string text = step.Title + " " + step.Body;
                StringAssert.DoesNotContain("[[", text, step.Id);
                // With the marks on, every "teal" is "striped teal" and every "gold" "plain gold".
                foreach (Match m in colour.Matches(text))
                {
                    string before = text.Substring(0, m.Index);
                    string expected = m.Value == "teal" ? "striped " : "plain ";
                    Assert.IsTrue(before.EndsWith(expected), $"{step.Id}: '{m.Value}' unmarked in: {text}");
                    named++;
                }
            }
            Assert.Greater(named, 10, "the guide steers by the tiles' colours in several steps");
        }
    }
}
