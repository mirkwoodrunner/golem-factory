using NUnit.Framework;
using UnityEngine;
using GolemFactory.Golems;
using GolemFactory.PunchCards;

namespace GolemFactory.Tests.EditMode
{
    public class GolemExecutionTests
    {
        [Test]
        public void NewProgram_StartsIdleAtStepZero()
        {
            var program = new GolemProgram();

            Assert.AreEqual(GolemState.Idle, program.State);
            Assert.AreEqual(0, program.CurrentStepIndex);
        }

        [Test]
        public void AdvanceStep_WrapsAroundToZero()
        {
            var program = new GolemProgram();
            program.appendages.Add(ScriptableObject.CreateInstance<AppendageActionDefinition>());

            program.AdvanceStep();

            Assert.AreEqual(0, program.CurrentStepIndex);
        }
    }
}
