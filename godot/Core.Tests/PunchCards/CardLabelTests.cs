using System.IO;
using System.Linq;
using GolemFactory.AssemblyLine;
using GolemFactory.Data;
using GolemFactory.PunchCards;
using GolemFactory.Tests.Data;
using GolemFactory.UI;
using NUnit.Framework;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// A card's player-facing label versus its id. "ExtractScrap" extracts whatever the stall
    /// behind holds -- Coal, in chapter 3 -- so the player reads "Extract", while the id every
    /// save, deck, patent and guide step keys on stays exactly as it was.
    /// </summary>
    public class CardLabelTests
    {
        [Test]
        public void ExtractScrap_ReadsAsExtract_OnTheWorkbenchAndTheAssemblyLine()
        {
            DefinitionSet defs = AuthoredData.Load();
            AppendageActionDefinition extract = defs.Appendages["ExtractScrap"];

            Assert.AreEqual("Extract", WorkbenchSession.CardDisplayName(null, extract));
            DraftableCardDefinition card = defs.Decks.Values.SelectMany(d => d.Cards).First(c => c.appendage == extract);
            Assert.AreEqual("Extract", WorkbenchDiagnostics.Humanize(card.DisplayName));
        }

        [Test]
        public void ARelabelledCard_KeepsItsId_SoSavesStillFindIt()
        {
            DefinitionSet defs = AuthoredData.Load();
            Assert.AreEqual("ExtractScrap", defs.Appendages["ExtractScrap"].name);
            Assert.AreSame(defs.Appendages["ExtractScrap"], defs.ToCatalog().FindAppendage("ExtractScrap"),
                "a save names the card by its id");
        }

        [Test]
        public void ACardWithNoLabel_ReadsAsItsName()
        {
            DefinitionSet defs = AuthoredData.Load();
            Assert.AreEqual("Push Output", WorkbenchSession.CardDisplayName(null, defs.Appendages["PushOutput"]));
        }

        [Test]
        public void EveryPlaceablesSprite_IsInTheArtFolder()
        {
            // The old single-cell clock_tower.png was deleted once the staged art replaced it;
            // nothing may still name a sprite that is not on disk.
            string art = Path.Combine(AuthoredData.DataDirectory, "..", "art");
            foreach (PlaceableEntry entry in PlaceableCatalogTests.LoadReal())
            {
                if (!string.IsNullOrEmpty(entry.Sprite))
                {
                    FileAssert.Exists(Path.Combine(art, entry.Sprite.EndsWith(".png") ? entry.Sprite : entry.Sprite + ".png"), entry.Key);
                }
                foreach (string shape in entry.ShapeSprites.Values.Where(v => !string.IsNullOrEmpty(v)))
                {
                    FileAssert.Exists(Path.Combine(art, shape.EndsWith(".png") ? shape : shape + ".png"), entry.Key + " shape");
                }
            }
        }
    }
}
