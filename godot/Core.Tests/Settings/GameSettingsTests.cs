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
        public void LoadedFile_WithAReservedOrDoubledKey_IsMadeSafe()
        {
            GameSettings s = GameSettings.FromPairs(new Dictionary<string, string>
            {
                ["key.interact"] = "Tab,F",   // Tab is the menu's: dropped, F kept
                ["key.golem_carry"] = "F,G",  // F is Use's now: dropped, G kept
            });
            CollectionAssert.AreEqual(new[] { "F" }, s.Keys.Get("interact"));
            CollectionAssert.AreEqual(new[] { "G" }, s.Keys.Get("golem_carry"));
        }

        [Test]
        public void LoadedFile_LeavingAnActionWithoutAKey_ResetsToDefaults()
        {
            GameSettings s = GameSettings.FromPairs(new Dictionary<string, string>
            {
                ["key.interact"] = "F",
                ["key.inventory"] = "Shift", // reserved: Pack would have no key
            });
            Assert.AreEqual("E", s.Keys.Get("interact")[0]);
            Assert.AreEqual("I", s.Keys.Get("inventory")[0]);
        }

        [Test]
        public void Sanitize_DropsNamesTheEngineDoesNotKnow()
        {
            var b = KeyBindings.Defaults();
            b.Set("interact", new List<string> { "Bogus", "F" });
            Assert.IsTrue(b.Sanitize(k => k != "Bogus"));
            CollectionAssert.AreEqual(new[] { "F" }, b.Get("interact"));
            Assert.IsFalse(b.Sanitize(_ => true));
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

        [TestCase(1280, 720, 1f, 1f)]     // the project's window: no room above 1
        [TestCase(1280, 720, 2f, 1f)]
        [TestCase(1920, 1080, 1.5f, 1.5f)]
        [TestCase(1920, 1080, 2f, 1.5f)]
        [TestCase(3840, 2160, 2f, 2f)]
        [TestCase(1280, 800, 1.25f, 1f)]  // the Steam Deck: width-bound
        [TestCase(1024, 576, 1f, 0.8f)]   // a small window is fitted down, below the slider's 1
        [TestCase(200, 100, 1f, 0.5f)]    // never below MinFitScale
        public void EffectiveUiScale_NeverShrinksTheCanvasBelowTheLayout(int w, int h, float setting, float expected)
        {
            var s = new GameSettings();
            s.SetUiScale(setting);
            Assert.AreEqual(expected, s.EffectiveUiScale(w, h), 1e-4f);
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

namespace GolemFactory.Tests.EditMode
{
    using System.Linq;
    using GolemFactory.Settings;
    using GolemFactory.UI;
    using NUnit.Framework;

    public class KeyCaptureTests
    {
        [Test]
        public void Press_RebindsTheListeningActionAndStops()
        {
            var b = KeyBindings.Defaults();
            var c = new KeyCapture(b);
            c.Begin("interact");

            Assert.AreEqual(RebindResult.Bound, c.Press("F"));
            Assert.IsNull(c.Listening);
            Assert.AreEqual("F", b.Get("interact")[0]);
        }

        [Test]
        public void Escape_CancelsAndChangesNothing()
        {
            var b = KeyBindings.Defaults();
            var c = new KeyCapture(b);
            c.Begin("interact");

            Assert.AreEqual(RebindResult.Cancelled, c.Press("Escape"));
            Assert.IsNull(c.Listening);
            Assert.AreEqual("E", b.Get("interact")[0]);
        }

        [TestCase("Tab")]
        [TestCase("F1")]
        [TestCase("F9")]
        [TestCase("Key3")]
        [TestCase("X")]
        [TestCase("Shift")]
        [TestCase("Ctrl")]
        public void ReservedKey_IsRefusedAndKeepsListening(string key)
        {
            var b = KeyBindings.Defaults();
            var c = new KeyCapture(b);
            c.Begin("interact");

            Assert.AreEqual(RebindResult.Reserved, c.Press(key));
            Assert.AreEqual("interact", c.Listening);
            Assert.AreEqual("E", b.Get("interact")[0]);
        }

        [Test]
        public void SameKey_IsUnchanged()
        {
            var b = KeyBindings.Defaults();
            Assert.AreEqual(RebindResult.Unchanged, b.Rebind("interact", "E"));
            CollectionAssert.AreEqual(new[] { "E" }, b.Get("interact"));
        }

        [Test]
        public void SecondaryKey_PromotedToFirst_ReplacesTheFirstWithoutADuplicate()
        {
            var b = KeyBindings.Defaults();
            Assert.AreEqual(RebindResult.Bound, b.Rebind("move_left", "Left"));
            CollectionAssert.AreEqual(new[] { "Left" }, b.Get("move_left"));
        }

        [Test]
        public void Rebind_ToAFreeKey_ReplacesTheOldKeySoItNoLongerWorks()
        {
            var b = KeyBindings.Defaults();
            Assert.AreEqual(RebindResult.Bound, b.Rebind("interact", "F"));
            CollectionAssert.AreEqual(new[] { "F" }, b.Get("interact"));
            Assert.IsNull(b.ActionWith("E"));
        }

        [Test]
        public void Rebind_KeepsTheSecondKey()
        {
            var b = KeyBindings.Defaults();
            b.Rebind("move_left", "J");
            CollectionAssert.AreEqual(new[] { "J", "Left" }, b.Get("move_left"));
        }

        [Test]
        public void Swap_ReportsTheActionThatGaveUpTheKey()
        {
            var b = KeyBindings.Defaults();
            Assert.AreEqual("golem_carry", b.ActionWith("G"));
            Assert.AreEqual(RebindResult.Swapped, b.Rebind("interact", "G"));
            Assert.AreEqual("interact", b.ActionWith("G"));
        }

        [Test]
        public void EveryActionIsRebindableIncludingTurn()
        {
            CollectionAssert.AreEquivalent(
                new[] { "move_left", "move_right", "move_up", "move_down", "interact", "golem_carry", "inventory", "build_rotate" },
                KeyBindings.Actions().ToArray());
        }

        [Test]
        public void NoDefaultKeyIsReserved()
        {
            var b = KeyBindings.Defaults();
            foreach (string action in KeyBindings.Actions())
            {
                foreach (string key in b.Get(action))
                {
                    Assert.IsFalse(KeyBindings.Reserved.Contains(key), key);
                }
            }
        }
    }

    public class KeyNamesTests
    {
        [TearDown]
        public void Restore() => KeyNames.Bindings = null;

        [Test]
        public void Defaults_ReadAsTheyAlwaysHave()
        {
            Assert.AreEqual("E", KeyNames.Interact);
            Assert.AreEqual("G", KeyNames.Carry);
            Assert.AreEqual("I", KeyNames.Inventory);
            Assert.AreEqual("R", KeyNames.Rotate);
            Assert.AreEqual("WASD", KeyNames.Move);
        }

        [Test]
        public void Move_JoinsLongNamesWithSlashes()
        {
            var b = KeyBindings.Defaults();
            b.Rebind("move_up", "Up");
            KeyNames.Bindings = b;
            Assert.AreEqual("Up/A/S/D", KeyNames.Move);
        }

        [Test]
        public void Display_DropsTheDigitPrefix()
        {
            Assert.AreEqual("5", KeyNames.Display("Key5"));
            Assert.AreEqual("Space", KeyNames.Display("Space"));
        }

        [Test]
        public void Fill_LeavesTextWithoutTokensAlone()
        {
            Assert.AreEqual("R8 Gear Cutting [x]", KeyNames.Fill("R8 Gear Cutting [x]"));
        }

        [Test]
        public void EveryGuideStep_ShowsNoTokenAndFollowsARebind()
        {
            var world = CrateWorld.Compose();
            var b = KeyBindings.Defaults();
            b.Rebind("interact", "F");
            b.Rebind("golem_carry", "C");
            b.Rebind("inventory", "P");
            b.Rebind("build_rotate", "T");
            b.Rebind("move_up", "Up");
            KeyNames.Bindings = b;

            // Any default key name left in a step is a mention the rebind didn't reach.
            var stale = new System.Text.RegularExpressions.Regex(
                @"\b(press|Press|hold|Hold|with|then) [EGIR]\b|\([EGIR]\)|\bR turns\b|\bWASD\b");
            bool sawF = false, sawC = false, sawP = false, sawT = false, sawMove = false;
            foreach (var step in world.Tutorial.Steps)
            {
                string text = step.Title + " " + step.Body;
                StringAssert.DoesNotContain("[[", text, step.Id);
                Assert.IsFalse(stale.IsMatch(text), step.Id + ": " + text);
                sawF |= text.Contains("Press F") || text.Contains("press F");
                sawC |= text.Contains("Press C") || text.Contains("(C)");
                sawP |= text.Contains("press P");
                sawT |= text.Contains("press T") || text.Contains("Press T") || text.Contains("(T)");
                sawMove |= text.Contains("Walk with Up/A/S/D");
            }
            Assert.IsTrue(sawF && sawC && sawP && sawT && sawMove, $"F {sawF} C {sawC} P {sawP} T {sawT} move {sawMove}");
        }

        [Test]
        public void BuildMenuAndPlaytestText_ShowNoToken()
        {
            KeyNames.Bindings = KeyBindings.Defaults();
            StringAssert.Contains("Hold E to crank, R to change recipe.", BuildMenuLabels.DescriptionFor("HandCrankBenchPrefab"));
            foreach (var q in GolemFactory.Tutorial.PlaytestSession.Questions)
            {
                StringAssert.DoesNotContain("[[", q.Prompt, q.Id);
            }
        }
    }
}
