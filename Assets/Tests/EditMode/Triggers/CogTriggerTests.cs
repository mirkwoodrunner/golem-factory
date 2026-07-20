using NUnit.Framework;
using UnityEngine;
using GolemFactory.PunchCards;

namespace GolemFactory.Tests.EditMode
{
    public class CogTriggerTests
    {
        [Test]
        public void LogicCore_DefaultsToAlwaysOn()
        {
            var logicCore = ScriptableObject.CreateInstance<LogicCoreDefinition>();

            Assert.AreEqual(TriggerType.AlwaysOn, logicCore.triggerType);
        }

        // Threshold/Signal trigger evaluation moves into a standalone GolemTriggerSystem
        // at M7 (see docs/unity-implementation-plan.md); add real coverage there instead
        // of testing GolemEntity's inline stub.
    }
}
