using System.Collections.Generic;
using System.Linq;
using GolemFactory.Data;
using GolemFactory.Settings;
using GolemFactory.Tests.Data;
using GolemFactory.Tests.World;
using GolemFactory.Tutorial;
using GolemFactory.UI;
using GolemFactory.World;
using NUnit.Framework;

namespace GolemFactory.Tests.Settings
{
    /// <summary>
    /// Player text with the pad in use (docs/steam-deck-design.md D7): key names name the pad's
    /// buttons, by the Deck's labels, and no text built once and kept says click, drag or a
    /// keyboard key.
    /// </summary>
    public class PadPhrasingTests
    {
        [TearDown]
        public void Restore()
        {
            KeyNames.Pad = false;
            KeyNames.Bindings = null;
        }

        [Test]
        public void ThePad_NamesItsButtons_ByTheDecksLabels()
        {
            KeyNames.Pad = true;
            Assert.AreEqual(("A", "X", "Y", "R1", "Menu", "B"), (KeyNames.Interact, KeyNames.Carry, KeyNames.Inventory, KeyNames.Rotate, KeyNames.Menu, KeyNames.Cancel));
            Assert.AreEqual("the left stick", KeyNames.Move);
            Assert.AreEqual("hold A, then Menu", KeyNames.Fill("hold " + KeyNames.InteractToken + ", then " + KeyNames.MenuToken));
        }

        [Test]
        public void TheKeyboard_StillNamesItsKeys_AndARebindStillShows()
        {
            Assert.AreEqual(("E", "Tab", "Escape", "WASD"), (KeyNames.Interact, KeyNames.Menu, KeyNames.Cancel, KeyNames.Move));
            KeyNames.Pad = true;
            KeyNames.Pad = false;
            Assert.AreEqual("E", KeyNames.Interact);
            Assert.AreEqual("Drag to lay a run.", KeyNames.Fill("Drag to lay a run."), "the keyboard's text is untouched");
        }

        [Test]
        public void ChangingTheDevice_RedrawsKeptText()
        {
            int before = TextTokens.Version;
            KeyNames.Pad = true;
            Assert.AreNotEqual(before, TextTokens.Version);
            int pad = TextTokens.Version;
            KeyNames.Pad = true;
            Assert.AreEqual(pad, TextTokens.Version, "setting the same device again redraws nothing");
        }

        [Test]
        public void WithThePad_NoGuideStep_Question_OrBuildMenuLine_SaysClickDragOrAKey()
        {
            DefinitionSet definitions = AuthoredData.Load();
            SandboxWorld world = SandboxWorld.Compose(definitions, SandboxSetupTests.LoadReal(), PlaceableCatalogTests.LoadReal(definitions));
            KeyNames.Pad = true;

            var texts = new List<(string where, string text)>();
            foreach (TutorialStep step in world.Tutorial.Steps)
            {
                texts.Add(("step " + step.Id + " title", step.Title));
                texts.Add(("step " + step.Id, step.Body));
            }
            foreach (PlaytestQuestion question in PlaytestSession.Questions)
            {
                texts.Add(("question " + question.Id, question.Prompt));
            }
            foreach (string key in world.Placeables.Select(p => p.Key))
            {
                texts.Add(("build menu " + key, BuildMenuLabels.DescriptionFor(key)));
            }
            texts.Add(("the golem order's how-to", GolemFactory.Contracts.GolemOrderText.HowTo));
            texts.Add(("the Workbench's empty program", TextTokens.Fill(WorkbenchLoopLabels.EmptyProgramHint)));

            List<string> left = texts
                .SelectMany(t => PadPhrasing.Leftover(t.text).Select(word => $"{t.where}: '{word}' in \"{t.text}\""))
                .ToList();
            Assert.IsEmpty(left, string.Join("\n", left));
        }
    }
}
