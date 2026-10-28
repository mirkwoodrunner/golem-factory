using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using GolemFactory.Compat;
using GolemFactory.Golems;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// docs/art-pipeline-design.md §6.4: whatever the playtest's <c>light</c> answer moves in
    /// <see cref="LightingRules"/>, the room stays warm, the floor stays about as light as it was,
    /// and the tiles and badges drawn on it still read. Colours mix as the 2D renderer mixes
    /// them, in sRGB; distances are OKLab, as <c>Tools/Art/art_metrics.py</c> measures.
    /// </summary>
    public class LightingRulesTests
    {
        private static Color[] FloorRamp()
        {
            for (DirectoryInfo dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
            {
                string path = Path.Combine(dir.FullName, "Tools", "Art", "palette.json");
                if (File.Exists(path))
                {
                    using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                    return doc.RootElement.GetProperty("ramps").GetProperty("floor").GetProperty("colours")
                        .EnumerateArray().Select(c => Hex(c.GetString())).ToArray();
                }
            }
            throw new FileNotFoundException("Tools/Art/palette.json not found above the test directory");
        }

        private static Color Hex(string hex) => new Color(
            Convert.ToInt32(hex.Substring(1, 2), 16) / 255f,
            Convert.ToInt32(hex.Substring(3, 2), 16) / 255f,
            Convert.ToInt32(hex.Substring(5, 2), 16) / 255f);

        // The darkest and the lightest plank.
        private static Color[] FloorExtremes()
        {
            Color[] ramp = FloorRamp();
            return new[] { ramp.First(), ramp.Last() };
        }

        private static Color Lit(Color c, Color light) => new Color(c.r * light.r, c.g * light.g, c.b * light.b);

        private static Color Over(Color top, Color under) => new Color(
            top.r * top.a + under.r * (1f - top.a),
            top.g * top.a + under.g * (1f - top.a),
            top.b * top.a + under.b * (1f - top.a));

        private static double[] OkLab(Color c)
        {
            static double Linear(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            double r = Linear(c.r), g = Linear(c.g), b = Linear(c.b);
            double l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
            double m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
            double s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
            return new[]
            {
                0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
                1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
                0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s,
            };
        }

        private static double Apart(Color a, Color b)
        {
            double[] x = OkLab(a), y = OkLab(b);
            return Math.Sqrt(x.Zip(y, (p, q) => (p - q) * (p - q)).Sum());
        }

        [Test]
        public void TheAmbientIsWarmAndNeverDark()
        {
            Color a = LightingRules.Ambient;
            Assert.That(a.r >= a.g && a.g >= a.b, $"warm: red ≥ green ≥ blue, got {a}");
            Assert.That(Math.Min(a.r, Math.Min(a.g, a.b)), Is.GreaterThanOrEqualTo(LightingRules.AmbientMinChannel));
        }

        [Test]
        public void TheFloorIsNoDarkerThanUnderTheOldGreyLessTheSlack()
        {
            foreach (Color plank in FloorExtremes())
            {
                double now = OkLab(Lit(plank, LightingRules.Ambient))[0];
                double before = OkLab(Lit(plank, LightingRules.PreviousAmbient))[0];
                Assert.That(now, Is.GreaterThanOrEqualTo(before - LightingRules.FloorLightnessSlack), $"plank {plank}");
            }
        }

        [Test]
        public void TheSourceAndTargetTilesReadOnTheLitFloor()
        {
            foreach (Color plank in FloorExtremes())
            {
                Color floor = Lit(plank, LightingRules.Ambient);
                foreach (Color tint in new[] { GolemTileOverlay.SourceTint, GolemTileOverlay.TargetTint })
                {
                    // The tile is in the world, so the ambient tints it with the floor.
                    double apart = Apart(Lit(Over(tint, plank), LightingRules.Ambient), floor);
                    Assert.That(apart, Is.GreaterThanOrEqualTo(LightingRules.OverlayMinApart), $"tint {tint} on plank {plank}");
                }
            }
        }

        [Test]
        public void TheStoppedBadgesReadOnTheLitFloor()
        {
            // The badges are on their own canvas layer: the ambient tints the floor under them, not them.
            foreach (Color plank in FloorExtremes())
            {
                Color floor = Lit(plank, LightingRules.Ambient);
                foreach (GolemMood mood in new[] { GolemMood.Stalled, GolemMood.Starved })
                {
                    double apart = Apart(Over(GolemMoodPalette.Badge(mood), floor), floor);
                    Assert.That(apart, Is.GreaterThanOrEqualTo(LightingRules.BadgeMinApart), $"{mood} on plank {plank}");
                }
            }
        }

        // The Waiting badge is the quiet grey on purpose (onboarding-feedback-design.md §3.4: a
        // room of sleeping golems does not shout), and on the lightest plank it was never 0.15
        // apart, even under the old grey. Its caption carries it; the light must not make it worse.
        [Test]
        public void TheWaitingBadgeReadsNoWorseThanUnderTheOldGrey()
        {
            Color badge = GolemMoodPalette.Badge(GolemMood.Waiting);
            foreach (Color plank in FloorExtremes())
            {
                Color now = Lit(plank, LightingRules.Ambient), before = Lit(plank, LightingRules.PreviousAmbient);
                Assert.That(Apart(Over(badge, now), now), Is.GreaterThanOrEqualTo(Apart(Over(badge, before), before) - 0.005),
                    $"plank {plank}");
            }
        }

        [Test]
        public void ABoilerWithNoCokeHasNoGlow()
        {
            Assert.AreEqual(0f, LightingRules.BoilerGlow(0, 0));
            Assert.AreEqual(0f, LightingRules.BoilerGlow(0, 8), "a dry boiler goes dark at once, whatever it was powering");
        }

        [Test]
        public void TheGlowRisesWithTheGolemsKeptWorkingUpToABoilersShare()
        {
            float previous = LightingRules.BoilerGlow(10, 0);
            Assert.That(previous, Is.GreaterThan(0f), "a fuelled boiler glows even with nothing working");
            for (int working = 1; working <= LightingRules.BoilerGlowMaxGolems; working++)
            {
                float glow = LightingRules.BoilerGlow(10, working);
                Assert.That(glow, Is.GreaterThan(previous), $"{working} working");
                previous = glow;
            }
            Assert.AreEqual(previous, LightingRules.BoilerGlow(10, LightingRules.BoilerGlowMaxGolems + 5));
            Assert.AreEqual(GolemFactory.Steam.SteamNetwork.MaxGolemsPerBoiler, LightingRules.BoilerGlowMaxGolems);
        }

        [Test]
        public void APipeIsHotExactlyWhereSteamReaches()
        {
            Assert.IsTrue(LightingRules.PipeHot(true));
            Assert.IsFalse(LightingRules.PipeHot(false));
        }

        [Test]
        public void ThePoolsBandsStepDownFromFullToNothingAtTheEdge()
        {
            var stops = LightingRules.SconceBandStops();
            Assert.AreEqual(LightingRules.SconceBands + 1, stops.Length);
            Assert.AreEqual((0f, 1f), stops[0]);
            Assert.AreEqual((1f, 0f), stops[stops.Length - 1]);
            for (int k = 1; k < stops.Length; k++)
            {
                Assert.That(stops[k].Offset, Is.GreaterThan(stops[k - 1].Offset));
                Assert.That(stops[k].Strength, Is.LessThan(stops[k - 1].Strength));
            }
        }
    }
}
