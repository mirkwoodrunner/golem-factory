using NUnit.Framework;
using GolemFactory.Blueprints;

namespace GolemFactory.Tests.EditMode
{
    public class PatentRegistryTests
    {
        private static Blueprint MakeBlueprint(string id, string ownerId) =>
            new Blueprint(id, ownerId, null, null, new System.Collections.Generic.List<PunchCards.AppendageActionDefinition>());

        [Test]
        public void TryPatent_NewId_Succeeds()
        {
            var registry = new PatentRegistry();

            bool result = registry.TryPatent(MakeBlueprint("BP1", "LocalPlayer"));

            Assert.IsTrue(result);
            Assert.IsTrue(registry.Blueprints.ContainsKey("BP1"));
        }

        [Test]
        public void TryPatent_DuplicateId_Fails()
        {
            var registry = new PatentRegistry();
            registry.TryPatent(MakeBlueprint("BP1", "LocalPlayer"));

            bool result = registry.TryPatent(MakeBlueprint("BP1", "LocalPlayer"));

            Assert.IsFalse(result);
        }

        [Test]
        public void TryPatent_NullBlueprint_Fails()
        {
            var registry = new PatentRegistry();

            Assert.IsFalse(registry.TryPatent(null));
        }

        [Test]
        public void TryUseBlueprint_UnknownId_Fails()
        {
            var registry = new PatentRegistry();

            Assert.IsFalse(registry.TryUseBlueprint("NoSuchId", "LocalPlayer", out _));
        }

        [Test]
        public void TryUseBlueprint_KnownId_OwnerUses_Succeeds()
        {
            var registry = new PatentRegistry();
            registry.TryPatent(MakeBlueprint("BP1", "LocalPlayer"));

            bool result = registry.TryUseBlueprint("BP1", "LocalPlayer", out Blueprint blueprint);

            Assert.IsTrue(result);
            Assert.AreEqual("BP1", blueprint.BlueprintId);
        }

        [Test]
        public void TryUseBlueprint_KnownId_DifferentUser_StillSucceeds()
        {
            // Royalty charge is a no-op in solo v1 -- using someone else's patent still
            // works, it just doesn't (yet) charge anything.
            var registry = new PatentRegistry();
            registry.TryPatent(MakeBlueprint("BP1", "OtherPlayer"));

            bool result = registry.TryUseBlueprint("BP1", "LocalPlayer", out Blueprint blueprint);

            Assert.IsTrue(result);
            Assert.AreEqual("OtherPlayer", blueprint.OwnerId);
        }
    }
}
