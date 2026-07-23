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
        public void NewBay_StartsAtTierOneWithOneSlot()
        {
            AssemblyBayStructure bay = Build();

            Assert.AreEqual(1, bay.Tier);
            Assert.AreEqual(1, bay.MaxGolemSlots);
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
            GolemEntity golem = MakeGolem("Golem");
            bay.TryAssignGolem(golem);

            Assert.IsTrue(bay.ReleaseGolem(golem));
            Assert.IsTrue(bay.TryAssignGolem(MakeGolem("GolemB")));
        }

        [Test]
        public void TryUpgrade_SufficientResources_IncrementsTierAndSlots_AndWithdraws()
        {
            AssemblyBayStructure bay = Build();
            var buffers = new StorageBufferRegistry();
            buffers.Deposit("Bay", ItemType.Scrap, 20);
            buffers.Deposit("Bay", ItemType.Brass, 5);

            bool result = bay.TryUpgrade(buffers, "Bay");

            Assert.IsTrue(result);
            Assert.AreEqual(2, bay.Tier);
            Assert.AreEqual(2, bay.MaxGolemSlots);
            Assert.AreEqual(0, buffers.GetOrCreate("Bay").GetQuantity(ItemType.Scrap));
            Assert.AreEqual(0, buffers.GetOrCreate("Bay").GetQuantity(ItemType.Brass));
        }

        [Test]
        public void TryUpgrade_InsufficientScrap_Fails_NoWithdrawal()
        {
            AssemblyBayStructure bay = Build();
            var buffers = new StorageBufferRegistry();
            buffers.Deposit("Bay", ItemType.Brass, 5);

            bool result = bay.TryUpgrade(buffers, "Bay");

            Assert.IsFalse(result);
            Assert.AreEqual(1, bay.Tier);
            Assert.AreEqual(5, buffers.GetOrCreate("Bay").GetQuantity(ItemType.Brass));
        }

        [Test]
        public void TryUpgrade_InsufficientBrass_Fails_RefundsScrap()
        {
            AssemblyBayStructure bay = Build();
            var buffers = new StorageBufferRegistry();
            buffers.Deposit("Bay", ItemType.Scrap, 20);

            bool result = bay.TryUpgrade(buffers, "Bay");

            Assert.IsFalse(result);
            Assert.AreEqual(1, bay.Tier);
            Assert.AreEqual(20, buffers.GetOrCreate("Bay").GetQuantity(ItemType.Scrap));
        }
    }
}
