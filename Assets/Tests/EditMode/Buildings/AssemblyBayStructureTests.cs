using NUnit.Framework;
using UnityEngine;
using GolemFactory.Buildings;
using GolemFactory.Economy;
using GolemFactory.Golems;

namespace GolemFactory.Tests.EditMode
{
    public class AssemblyBayStructureTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        private AssemblyBayStructure Build()
        {
            _root = new GameObject("Bay");
            return _root.AddComponent<AssemblyBayStructure>();
        }

        private GolemEntity MakeGolem(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform);
            return go.AddComponent<GolemEntity>();
        }

        [Test]
        public void NewBay_StartsAtTierOneWithTenSlots()
        {
            // ONE became TEN when §8's cap went into the loop: the bay stopped being inert
            // bookkeeping and became the concurrent-golem limit, whose authored figure is ten.
            AssemblyBayStructure bay = Build();

            Assert.AreEqual(1, bay.Tier);
            Assert.AreEqual(AssemblyBayStructure.DefaultSlots, bay.MaxGolemSlots);
        }

        /// <summary>Fills every slot but one, so the capacity tests below stay about capacity
        /// rather than about counting to ten.</summary>
        private void FillToOneFreeSlot(AssemblyBayStructure bay)
        {
            for (int i = 0; i < AssemblyBayStructure.DefaultSlots - 1; i++)
            {
                bay.TryAssignGolem(MakeGolem("Filler" + i));
            }
        }

        [Test]
        public void TryAssignGolem_UnderCapacity_Succeeds()
        {
            AssemblyBayStructure bay = Build();
            GolemEntity golem = MakeGolem("Golem");

            Assert.IsTrue(bay.TryAssignGolem(golem));
            Assert.Contains(golem, (System.Collections.ICollection)bay.AssignedGolems);
        }

        [Test]
        public void TryAssignGolem_AtCapacity_Fails()
        {
            AssemblyBayStructure bay = Build();
            FillToOneFreeSlot(bay);
            bay.TryAssignGolem(MakeGolem("GolemA"));

            bool result = bay.TryAssignGolem(MakeGolem("GolemB"));

            Assert.IsFalse(result);
        }

        [Test]
        public void TryAssignGolem_SameGolemTwice_Fails()
        {
            AssemblyBayStructure bay = Build();
            GolemEntity golem = MakeGolem("Golem");
            bay.TryAssignGolem(golem);

            Assert.IsFalse(bay.TryAssignGolem(golem));
        }

        [Test]
        public void ReleaseGolem_FreesASlot()
        {
            AssemblyBayStructure bay = Build();
            FillToOneFreeSlot(bay);
            GolemEntity golem = MakeGolem("Golem");
            bay.TryAssignGolem(golem);
            Assert.IsFalse(bay.HasFreeSlot);

            Assert.IsTrue(bay.ReleaseGolem(golem));
            Assert.IsTrue(bay.TryAssignGolem(MakeGolem("GolemB")));
        }

        [Test]
        public void TryUpgrade_SufficientResources_IncrementsTierAndSlots_AndWithdraws()
        {
            // §8's price: 40 Scrap + 20 Iron Plate for +6 slots. The BRASS in the old pair is
            // gone with the pair itself -- the cost is an item bundle now (§11 item 8), which
            // is the only shape that can express a price in Iron Plate at all.
            AssemblyBayStructure bay = Build();
            var buffers = new StorageBufferRegistry();
            buffers.Deposit("Bay", ItemType.Scrap, 40);
            buffers.Deposit("Bay", ItemType.IronPlate, 20);

            bool result = bay.TryUpgrade(buffers, "Bay");

            Assert.IsTrue(result);
            Assert.AreEqual(2, bay.Tier);
            Assert.AreEqual(
                AssemblyBayStructure.DefaultSlots + AssemblyBayStructure.SlotsPerUpgrade,
                bay.MaxGolemSlots);
            Assert.AreEqual(0, buffers.GetOrCreate("Bay").GetQuantity(ItemType.Scrap));
            Assert.AreEqual(0, buffers.GetOrCreate("Bay").GetQuantity(ItemType.IronPlate));
        }

        [Test]
        public void TryUpgrade_InsufficientScrap_Fails_NoWithdrawal()
        {
            AssemblyBayStructure bay = Build();
            var buffers = new StorageBufferRegistry();
            buffers.Deposit("Bay", ItemType.IronPlate, 20);

            bool result = bay.TryUpgrade(buffers, "Bay");

            Assert.IsFalse(result);
            Assert.AreEqual(1, bay.Tier);
            Assert.AreEqual(20, buffers.GetOrCreate("Bay").GetQuantity(ItemType.IronPlate));
        }

        [Test]
        public void TryUpgrade_InsufficientPlate_Fails_RefundsScrap()
        {
            AssemblyBayStructure bay = Build();
            var buffers = new StorageBufferRegistry();
            buffers.Deposit("Bay", ItemType.Scrap, 40);

            bool result = bay.TryUpgrade(buffers, "Bay");

            Assert.IsFalse(result);
            Assert.AreEqual(1, bay.Tier);
            Assert.AreEqual(40, buffers.GetOrCreate("Bay").GetQuantity(ItemType.Scrap));
        }
    }
}
