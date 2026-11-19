using NUnit.Framework;
using GolemFactory.Buildings;
using GolemFactory.UI;

namespace GolemFactory.Tests.EditMode
{
    /// <summary>
    /// The Assembly Line tab's golem-cap row: how many golems stand, of how many. The cap is fixed
    /// (2026-11-19), so the row has nothing to buy.
    /// </summary>
    public class AssemblyBayRowPolicyTests
    {
        [Test]
        public void Occupancy_ReadsStandingOverTheCap()
        {
            Assert.AreEqual("Golems 7 / 112", AssemblyBayRowPolicy.FormatOccupancy(7, 112));
        }

        [Test]
        public void TheRowQuotesTheCap_NotACopyOfIt()
        {
            var bay = new AssemblyBayStructure();
            Assert.AreEqual(
                "Golems 0 / " + AssemblyBayStructure.DefaultCap,
                AssemblyBayRowPolicy.FormatOccupancy(bay.OccupiedSlots, bay.MaxGolemSlots));
        }
    }
}
