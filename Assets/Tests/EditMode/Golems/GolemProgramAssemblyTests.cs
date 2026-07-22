using NUnit.Framework;
using UnityEngine;
using GolemFactory.Golems;
using GolemFactory.PunchCards;

namespace GolemFactory.Tests.EditMode
{
    public class GolemProgramAssemblyTests
    {
        private static ChassisDefinition MakeChassis(int slots)
        {
            var chassis = ScriptableObject.CreateInstance<ChassisDefinition>();
            chassis.maxAppendageSlots = slots;
            return chassis;
        }

        private static AppendageActionDefinition MakeAppendage()
        {
            return ScriptableObject.CreateInstance<AppendageActionDefinition>();
        }

        [Test]
        public void TryAssignChassis_WithNoExistingAppendages_Succeeds()
        {
            var program = new GolemProgram();

            bool result = program.TryAssignChassis(MakeChassis(2));

            Assert.IsTrue(result);
            Assert.IsNotNull(program.chassis);
        }

        [Test]
        public void TryAssignChassis_Null_Fails()
        {
            var program = new GolemProgram();

            Assert.IsFalse(program.TryAssignChassis(null));
        }

        [Test]
        public void TryAssignChassis_SmallerThanCurrentAppendageCount_FailsAndKeepsOldChassis()
        {
            var program = new GolemProgram();
            ChassisDefinition roomy = MakeChassis(2);
            program.TryAssignChassis(roomy);
            program.TryAddAppendage(MakeAppendage());
            program.TryAddAppendage(MakeAppendage());

            bool result = program.TryAssignChassis(MakeChassis(1));

            Assert.IsFalse(result);
            Assert.AreEqual(roomy, program.chassis);
        }

        [Test]
        public void TryAddAppendage_WithoutChassis_Fails()
        {
            var program = new GolemProgram();

            Assert.IsFalse(program.TryAddAppendage(MakeAppendage()));
            Assert.AreEqual(0, program.appendages.Count);
        }

        [Test]
        public void TryAddAppendage_UpToChassisCapacity_Succeeds()
        {
            var program = new GolemProgram();
            program.TryAssignChassis(MakeChassis(2));

            Assert.IsTrue(program.TryAddAppendage(MakeAppendage()));
            Assert.IsTrue(program.TryAddAppendage(MakeAppendage()));
            Assert.AreEqual(2, program.appendages.Count);
        }

        [Test]
        public void TryAddAppendage_BeyondChassisCapacity_FailsAndDoesNotAdd()
        {
            var program = new GolemProgram();
            program.TryAssignChassis(MakeChassis(1));
            program.TryAddAppendage(MakeAppendage());

            bool result = program.TryAddAppendage(MakeAppendage());

            Assert.IsFalse(result);
            Assert.AreEqual(1, program.appendages.Count);
        }

        [Test]
        public void RemoveAppendageAt_FreesSlotForAnotherAdd()
        {
            var program = new GolemProgram();
            program.TryAssignChassis(MakeChassis(1));
            program.TryAddAppendage(MakeAppendage());

            program.RemoveAppendageAt(0);

            Assert.AreEqual(0, program.appendages.Count);
            Assert.IsTrue(program.TryAddAppendage(MakeAppendage()));
        }

        [Test]
        public void RemoveAppendageAt_OutOfRange_IsNoOp()
        {
            var program = new GolemProgram();
            program.TryAssignChassis(MakeChassis(1));
            program.TryAddAppendage(MakeAppendage());

            program.RemoveAppendageAt(5);
            program.RemoveAppendageAt(-1);

            Assert.AreEqual(1, program.appendages.Count);
        }
    }
}
