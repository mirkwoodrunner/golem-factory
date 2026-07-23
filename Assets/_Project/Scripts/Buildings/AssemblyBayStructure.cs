using System.Collections.Generic;
using UnityEngine;
using GolemFactory.Economy;
using GolemFactory.Golems;

namespace GolemFactory.Buildings
{
    // A concrete placeable with N golem mount slots -- the spatial translation of the
    // tabletop Assembly Bay's tableau capacity (per game-design.md). Upgrading with
    // Scrap/Brass increases tier and slot count. This is capacity/upgrade bookkeeping
    // only, not the drafting loop itself (that's M9's Assembly Line stretch scope).
    public sealed class AssemblyBayStructure : MonoBehaviour
    {
        [SerializeField] private int tier = 1;
        [SerializeField] private int maxGolemSlots = 1;
        [SerializeField] private int upgradeScrapCost = 20;
        [SerializeField] private int upgradeBrassCost = 5;

        private readonly List<GolemEntity> _assignedGolems = new List<GolemEntity>();

        public int Tier => tier;
        public int MaxGolemSlots => maxGolemSlots;
        public IReadOnlyList<GolemEntity> AssignedGolems => _assignedGolems;

        public bool TryAssignGolem(GolemEntity golem)
        {
            if (golem == null || _assignedGolems.Count >= maxGolemSlots || _assignedGolems.Contains(golem))
            {
                return false;
            }

            _assignedGolems.Add(golem);
            return true;
        }

        public bool ReleaseGolem(GolemEntity golem) => _assignedGolems.Remove(golem);

        // Withdraws the upgrade cost from the given buffer; refunds the Scrap portion if
        // the Brass withdrawal fails partway through, so a failed upgrade never leaves the
        // buffer partially charged.
        public bool TryUpgrade(StorageBufferRegistry buffers, string resourceBufferId)
        {
            if (buffers == null || !buffers.TryWithdraw(resourceBufferId, ItemType.Scrap, upgradeScrapCost))
            {
                return false;
            }

            if (!buffers.TryWithdraw(resourceBufferId, ItemType.Brass, upgradeBrassCost))
            {
                buffers.Deposit(resourceBufferId, ItemType.Scrap, upgradeScrapCost);
                return false;
            }

            tier++;
            maxGolemSlots++;
            return true;
        }
    }
}
