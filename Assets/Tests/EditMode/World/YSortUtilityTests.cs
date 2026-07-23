using NUnit.Framework;
using GolemFactory.World;

namespace GolemFactory.Tests.EditMode
{
    public class YSortUtilityTests
    {
        [Test]
        public void ComputeSortingOrder_HigherWorldY_SortsBehindLowerWorldY()
        {
            int back = YSortUtility.ComputeSortingOrder(worldY: 5f);
            int front = YSortUtility.ComputeSortingOrder(worldY: 1f);

            Assert.Less(back, front);
        }

        [Test]
        public void ComputeSortingOrder_ZeroWorldY_IsZero()
        {
            Assert.AreEqual(0, YSortUtility.ComputeSortingOrder(worldY: 0f));
        }

        [TestCase(2f)]
        [TestCase(-2f)]
        public void ComputeSortingOrder_OppositeWorldY_ProducesOppositeSign(float worldY)
        {
            int order = YSortUtility.ComputeSortingOrder(worldY);
            int opposite = YSortUtility.ComputeSortingOrder(-worldY);

            Assert.AreEqual(order, -opposite);
        }
    }
}
