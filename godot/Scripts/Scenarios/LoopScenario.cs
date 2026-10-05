using System.Linq;
using GolemFactory.Economy;

namespace GolemFactory.Nodes.Scenarios
{
    /// <summary>
    /// The spike's end-to-end loop (formerly SpikeCheck): the station builds a Scavenger, which
    /// extracts scrap onto the belt, which the unloader hauls into the stockpile. Runs a minute
    /// of simulated time and checks the whole chain.
    /// </summary>
    public sealed class LoopScenario : IScenario
    {
        /// <summary>A minute of simulated time at 10 ticks/s.</summary>
        public const long TicksToRun = 600;

        private ScenarioRunner _runner;

        public void Begin(ScenarioRunner runner)
        {
            _runner = runner;
            runner.Station.Interact();
        }

        public ScenarioResult? Step(double delta)
        {
            if (_runner == null || _runner.World.Clock.CurrentTick < TicksToRun)
            {
                return null;
            }

            GolemNode extractor = _runner.Station.Built;
            GolemNode unloader = _runner.GetTree().GetNodesInGroup(GolemNodeGroup.Name)
                .OfType<GolemNode>().FirstOrDefault(g => g != extractor);
            int depotScrap = _runner.Depot.Buffer.GetQuantity(ItemType.Scrap);
            int onBelts = _runner.GetTree().GetNodesInGroup(BeltRun.GroupName).OfType<BeltRun>().Sum(r => r.ItemCount);

            // The station is Core's real one: it must have named its first golem by the player
            // pattern, and charged the Scavenger's 12 Scrap out of the 12 the stockpile started
            // with -- so every Scrap now in the stockpile is one the unloader delivered, and
            // nothing was minted or lost. One delivery per cycle, but a Push deposits before its
            // cycle completes, so the stockpile may be one ahead of the count.
            bool stationDidItsJob = extractor != null && extractor.GolemId == "PlayerGolem-001"
                && extractor.Entity.IsRuntimeSpawned
                && unloader != null
                && depotScrap - unloader.CompletedCycles is 0 or 1;

            bool pass = stationDidItsJob
                && extractor.CompletedCycles >= 10
                && unloader.CompletedCycles >= 10;

            return new ScenarioResult(pass,
                $"after {_runner.World.Clock.CurrentTick} ticks: " +
                $"station built {extractor?.GolemId ?? "nothing"} and charged its cost: {stationDidItsJob}, " +
                $"extractor cycles={extractor?.CompletedCycles ?? -1}, " +
                $"unloader cycles={unloader?.CompletedCycles ?? -1}, " +
                $"depot scrap={depotScrap}, scrap on belts={onBelts}");
        }
    }
}
