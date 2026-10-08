using GolemFactory.Data;
using GolemFactory.PunchCards;
using GolemFactory.Save;
using GolemFactory.Tests.Data;
using GolemFactory.Tests.World;
using GolemFactory.World;
using NUnit.Framework;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// Unity's EditMode SaveCatalogCoverageTests (G9). A save names every chassis, core and card
    /// by NAME, and a name the catalog cannot resolve is silently skipped on load -- so the
    /// catalog a load uses must cover everything the player can put on a golem. Unity checked the
    /// SaveLoadPanel's hand-kept rosters against the asset folders; the Godot load uses the whole
    /// authored definition set, so these pin that it does.
    /// </summary>
    public class SaveCatalogCoverageTests
    {
        [Test]
        public void SandboxSaveCatalog_ResolvesEveryAuthoredDefinition()
        {
            DefinitionSet definitions = AuthoredData.Load();
            DefinitionCatalog catalog = definitions.ToCatalog();

            foreach (ChassisDefinition chassis in definitions.Chassis.Values)
            {
                Assert.AreSame(chassis, catalog.FindChassis(chassis.name), chassis.name);
            }
            foreach (LogicCoreDefinition core in definitions.LogicCores.Values)
            {
                Assert.AreSame(core, catalog.FindLogicCore(core.name), core.name);
            }
            foreach (AppendageActionDefinition card in definitions.Appendages.Values)
            {
                Assert.AreSame(card, catalog.FindAppendage(card.name), card.name);
            }
        }

        [Test]
        public void SandboxSaveCatalog_ResolvesEveryCardTheWorkbenchOffers()
        {
            // Asserted separately: a card can be on the Workbench's roster without being among
            // the authored definitions only if the two are built from different sources.
            DefinitionSet definitions = AuthoredData.Load();
            SandboxWorld world = SandboxWorld.Compose(definitions, SandboxSetupTests.LoadReal(), PlaceableCatalogTests.LoadReal(definitions));
            DefinitionCatalog catalog = definitions.ToCatalog();

            foreach (string name in world.Setup.workbench.appendages)
            {
                Assert.IsNotNull(catalog.FindAppendage(name), name);
            }
            foreach (string name in world.Setup.workbench.chassis)
            {
                Assert.IsNotNull(catalog.FindChassis(name), name);
            }
        }
    }
}
