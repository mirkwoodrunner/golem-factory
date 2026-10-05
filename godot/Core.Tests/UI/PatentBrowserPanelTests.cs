using System.Collections.Generic;
using GolemFactory.Blueprints;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.UI;
using NUnit.Framework;

namespace GolemFactory.Tests.UI
{
    /// <summary>
    /// Unity's PlayMode PatentBrowserPanelTests, ported onto Core's <see cref="PatentBrowser"/>
    /// (G8), plus the order bug the port found in its Load button.
    /// </summary>
    public class PatentBrowserPanelTests
    {
        private sealed class StubWorkbench : IWorkbenchScreen
        {
            private readonly WorkbenchSession _session;
            public StubWorkbench(WorkbenchSession session) => _session = session;
            public bool IsOpen => _session.IsOpen;
            public GolemEntity TargetGolem => _session.TargetGolem;
            public void Open() => _session.Open();
            public void RetargetGolem(GolemEntity golem) => _session.RetargetGolem(golem);
        }

        private static Blueprint MakeBlueprint(string id, params AppendageActionDefinition[] steps) =>
            new Blueprint(id, "LocalPlayer", new ChassisDefinition { maxAppendageSlots = 3 }, new LogicCoreDefinition(),
                new List<AppendageActionDefinition>(steps));

        [Test]
        public void Refresh_ListsAllPatentedBlueprints()
        {
            var registry = new PatentRegistry();
            registry.TryPatent(MakeBlueprint("BP-001"));
            registry.TryPatent(MakeBlueprint("BP-002"));

            Assert.AreEqual(2, PatentBrowser.Rows(registry).Count);
            Assert.IsNull(PatentBrowser.EmptyMessage(registry));
        }

        [Test]
        public void Refresh_NoPatentsYet_ExplainsHowToFileOne()
        {
            var registry = new PatentRegistry();
            Assert.AreEqual(0, PatentBrowser.Rows(registry).Count);
            StringAssert.Contains("No patents", PatentBrowser.EmptyMessage(registry));
        }

        [Test]
        public void LoadButton_LoadsBlueprintIntoDraft_AndOpensWorkbench()
        {
            var session = new WorkbenchSession(3);
            var screen = new StubWorkbench(session);
            AppendageActionDefinition step = new AppendageActionDefinition();

            PatentBrowser.Load(MakeBlueprint("BP-001", step), screen, session);

            Assert.IsTrue(screen.IsOpen);
            Assert.AreSame(step, session.DraftAppendageAt(0));
        }

        [Test]
        public void LoadingABlueprint_SurvivesOpeningTheWorkbench()
        {
            // The order bug: Unity loaded the blueprint, THEN opened the Workbench, whose Open
            // re-reads the draft from the targeted golem -- discarding what the player loaded.
            var session = new WorkbenchSession(3);
            var golem = new GolemEntity();
            golem.Configure("Golem", null);
            session.ConfigureGolem(golem); // a target with an empty program
            var screen = new StubWorkbench(session);
            AppendageActionDefinition step = new AppendageActionDefinition();

            PatentBrowser.Load(MakeBlueprint("BP-001", step), screen, session);

            Assert.AreSame(step, session.DraftAppendageAt(0), "the loaded blueprint was overwritten by the target's program");
        }
    }
}
