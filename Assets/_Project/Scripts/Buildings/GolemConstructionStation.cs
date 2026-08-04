using UnityEngine;
using GolemFactory.Belts;
using GolemFactory.Economy;
using GolemFactory.Golems;
using GolemFactory.PunchCards;
using GolemFactory.UI;
using GolemFactory.World;

namespace GolemFactory.Buildings
{
    // Sibling component alongside PlaceableBuilding on its prefab (PlaceableBuilding is
    // sealed, so this can't subclass it). Spends Scrap/Brass -- finally consuming
    // ChassisDefinition.scrapCost/brassCost, which every authored chassis asset has carried
    // since M3 but nothing read until now -- to spawn a bare-chassis GolemEntity and hand it
    // straight to the Workbench so the player programs it exactly like any other golem.
    [RequireComponent(typeof(PlaceableBuilding))]
    public sealed class GolemConstructionStation : MonoBehaviour
    {
        [SerializeField] private ChassisDefinition[] chassisRoster = new ChassisDefinition[0];
        [SerializeField] private GolemEntity golemPrefab;
        [SerializeField] private ConveyorSystemHolder conveyorHolder;
        [SerializeField] private ResourceNodeRegistryHolder nodeRegistryHolder;
        [SerializeField] private StorageBufferRegistryHolder bufferRegistryHolder;
        [SerializeField] private SimulationClockRunner clockRunner;
        [SerializeField] private WorkbenchController workbenchController;
        [SerializeField] private string stockpileBufferId = "FactoryStockpile";

        private int _nextGolemNumber = 1;

        public ChassisDefinition[] ChassisRoster => chassisRoster;

        // Test/bootstrap-friendly setup mirroring GolemEntity.Configure/ConfigureEconomy.
        public void Configure(
            ChassisDefinition[] roster, GolemEntity prefab, ConveyorSystemHolder conveyor,
            ResourceNodeRegistryHolder nodes, StorageBufferRegistryHolder buffers,
            SimulationClockRunner clock, WorkbenchController workbench, string bufferId)
        {
            chassisRoster = roster ?? new ChassisDefinition[0];
            golemPrefab = prefab;
            conveyorHolder = conveyor;
            nodeRegistryHolder = nodes;
            bufferRegistryHolder = buffers;
            clockRunner = clock;
            workbenchController = workbench;
            stockpileBufferId = bufferId;
        }

        /// <summary>
        /// Current Scrap/Brass in the buffer this station spends from. Returns false when no
        /// buffer registry is wired, so the panel can say "stockpile unavailable" rather than
        /// silently printing a confident zero.
        /// </summary>
        public bool TryGetStockpile(out int scrapStock, out int brassStock)
        {
            scrapStock = 0;
            brassStock = 0;
            if (bufferRegistryHolder == null ||
                !bufferRegistryHolder.Registry.TryGetBuffer(stockpileBufferId, out StorageBuffer buffer))
            {
                return false;
            }

            scrapStock = buffer.GetQuantity(ItemType.Scrap);
            brassStock = buffer.GetQuantity(ItemType.Brass);
            return true;
        }

        /// <summary>
        /// Whether <see cref="TryConstructGolem"/> would currently succeed on cost grounds.
        /// Routed through ConstructionCostPolicy -- the same arithmetic the panel prints -- so
        /// the preview and the actual withdrawal can never disagree. A missing buffer registry
        /// reads as unaffordable, matching TryConstructGolem's own early-out.
        /// </summary>
        public bool CanAfford(ChassisDefinition chassis)
        {
            if (chassis == null)
            {
                return false;
            }

            int scrapStock;
            int brassStock;
            if (!TryGetStockpile(out scrapStock, out brassStock))
            {
                // A buffer that has never been deposited into doesn't exist yet, but a
                // genuinely free chassis is still affordable against it -- the same
                // zero-cost case StorageBufferRegistry.TryWithdrawScrapAndBrass guards.
                return bufferRegistryHolder != null &&
                       ConstructionCostPolicy.CanAfford(0, 0, chassis.scrapCost, chassis.brassCost);
            }

            return ConstructionCostPolicy.CanAfford(scrapStock, brassStock, chassis.scrapCost, chassis.brassCost);
        }

        // Withdraws the chassis's cost, instantiates a bare-chassis golem (no logic core or
        // appendages yet -- the player fits those via the Workbench, matching "feed
        // resources to build golem parts"), registers it with the clock so it sits Idle
        // until programmed, and retargets the Workbench onto it immediately.
        public bool TryConstructGolem(ChassisDefinition chassis, out GolemEntity golem)
        {
            golem = null;
            if (chassis == null || golemPrefab == null || bufferRegistryHolder == null)
            {
                return false;
            }

            if (!bufferRegistryHolder.Registry.TryWithdrawScrapAndBrass(stockpileBufferId, chassis.scrapCost, chassis.brassCost))
            {
                return false;
            }

            golem = Instantiate(golemPrefab, transform.position, Quaternion.identity);
            golem.Configure($"PlayerGolem-{_nextGolemNumber:D3}", conveyorHolder);
            _nextGolemNumber++;
            golem.ConfigureEconomy(nodeRegistryHolder, bufferRegistryHolder);
            golem.Program.TryAssignChassis(chassis);

            GolemVisual visual = golem.GetComponent<GolemVisual>();
            if (visual != null)
            {
                visual.RefreshSpriteFromChassis();
            }

            if (clockRunner != null)
            {
                clockRunner.Register(golem);
            }

            if (workbenchController != null)
            {
                workbenchController.RetargetGolem(golem);
            }

            return true;
        }
    }
}
