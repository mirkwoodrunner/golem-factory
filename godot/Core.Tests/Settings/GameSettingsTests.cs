using System.Collections.Generic;
using NUnit.Framework;
using GolemFactory.Settings;

namespace GolemFactory.Tests.EditMode
{
    public class GameSettingsTests
    {
        [Test]
        public void RoundTrip_KeepsEveryValue()
        {
            var s = new GameSettings { Display = DisplayMode.Fullscreen, ReducedMotion = true, ColorBlindMarks = true };
            s.SetUiScale(1.25f);
            s.Keys.Rebind("interact", "F");

            GameSettings back = GameSettings.FromPairs(s.ToPairs());

            Assert.AreEqual(DisplayMode.Fullscreen, back.Display);
            Assert.AreEqual(1.25f, back.UiScale);
            Assert.IsTrue(back.ReducedMotion);
            Assert.IsTrue(back.ColorBlindMarks);
            Assert.AreEqual("F", back.Keys.Get("interact")[0]);
        }

        [Test]
        public void MissingOrBadValues_FallBackToDefaults()
        {
            GameSettings s = GameSettings.FromPairs(new Dictionary<string, string>
            {
                ["display"] = "Sideways", ["ui_scale"] = "huge", ["key.interact"] = "",
            });
            Assert.AreEqual(DisplayMode.Windowed, s.Display);
            Assert.AreEqual(1f, s.UiScale);
            Assert.AreEqual("E", s.Keys.Get("interact")[0]);
        }

        [Test]
        public void UiScale_IsClamped()
        {
            var s = new GameSettings();
            s.SetUiScale(9f);
            Assert.AreEqual(GameSettings.MaxUiScale, s.UiScale);
            s.SetUiScale(0.1f);
            Assert.AreEqual(GameSettings.MinUiScale, s.UiScale);
        }

        [Test]
        public void Rebind_ToAKeyInUse_Swaps()
        {
            var b = KeyBindings.Defaults();
            b.Rebind("interact", "G"); // G was golem_carry's

            Assert.AreEqual("G", b.Get("interact")[0]);
            Assert.AreEqual("E", b.Get("golem_carry")[0]);
        }

        [Test]
        public void Rebind_NoKeyIsHeldByTwoActions()
        {
            var b = KeyBindings.Defaults();
            b.Rebind("move_left", "Up");
            var seen = new HashSet<string>();
            foreach (string action in KeyBindings.Actions())
            {
                foreach (string key in b.Get(action))
                {
                    Assert.IsTrue(seen.Add(key), key + " is bound twice");
                }
            }
        }
    }
}
