using GolemFactory.Audio;
using GolemFactory.Compat;
using NUnit.Framework;

namespace GolemFactory.Tests.Audio
{
    // docs/audio-design.md §4: the mix's numbers.
    public class AudioRulesTests
    {
        [Test]
        public void SteamHiss_IsSilentWithNoBoilerLoad_AndFullBy24()
        {
            Assert.AreEqual(0f, AudioRules.SteamHissVolume(0));
            Assert.Greater(AudioRules.SteamHissVolume(1), 0f);
            Assert.Less(AudioRules.SteamHissVolume(12), 1f);
            Assert.AreEqual(1f, AudioRules.SteamHissVolume(AudioRules.FullHissAt), 1e-6f);
            Assert.AreEqual(1f, AudioRules.SteamHissVolume(200), 1e-6f);
        }

        [Test]
        public void SteamHiss_RisesWithEveryGolem()
        {
            for (int n = 1; n < AudioRules.FullHissAt; n++)
            {
                Assert.Greater(AudioRules.SteamHissVolume(n + 1), AudioRules.SteamHissVolume(n), n.ToString());
            }
        }

        [TestCase(0, 1)]
        [TestCase(3, 1)]
        [TestCase(4, 2)]
        [TestCase(15, 2)]
        [TestCase(16, 3)]
        [TestCase(48, 4)]
        [TestCase(500, 4)]
        public void MusicLayers_FollowTheThresholds(int golems, int layers) =>
            Assert.AreEqual(layers, AudioRules.MusicLayers(golems, 1));

        [Test]
        public void MusicLayers_DoNotDrop_UntilBelow80PercentOfTheThreshold()
        {
            Assert.AreEqual(3, AudioRules.MusicLayers(15, 3), "one golem wrecked at 16 keeps layer 3");
            Assert.AreEqual(3, AudioRules.MusicLayers(13, 3), "12.8 is 80% of 16");
            Assert.AreEqual(2, AudioRules.MusicLayers(12, 3));
            Assert.AreEqual(4, AudioRules.MusicLayers(39, 4));
            Assert.AreEqual(3, AudioRules.MusicLayers(38, 4));
        }

        [Test]
        public void MusicLayers_DropSeveralAtOnce_WhenTheFactoryEmpties() =>
            Assert.AreEqual(1, AudioRules.MusicLayers(0, 4));

        [Test]
        public void Audible_OnScreen_AndAQuarterScreenPastAnEdge_ButNotFurther()
        {
            var centre = new Vector2(0f, 0f);
            var half = new Vector2(10f, 6f); // a screen 20 cells wide: the margin is 5
            Assert.IsTrue(AudioRules.Audible(new Vector2(0f, 0f), centre, half));
            Assert.IsTrue(AudioRules.Audible(new Vector2(14.9f, 0f), centre, half));
            Assert.IsFalse(AudioRules.Audible(new Vector2(15.1f, 0f), centre, half));
            Assert.IsTrue(AudioRules.Audible(new Vector2(0f, -10.9f), centre, half));
            Assert.IsFalse(AudioRules.Audible(new Vector2(0f, -11.1f), centre, half));
        }

        [Test]
        public void SliderToDb_FullIsUnity_AndZeroIsSilent()
        {
            Assert.AreEqual(0f, AudioRules.SliderToDb(1f), 1e-5f);
            Assert.AreEqual(-6.02f, AudioRules.SliderToDb(0.5f), 0.01f);
            Assert.LessOrEqual(AudioRules.SliderToDb(0f), -80f);
        }
    }
}
