using System;
using System.Collections.Generic;
using System.Linq;
using GolemFactory.ClockTower;
using GolemFactory.Events;
using GolemFactory.Simulation;

namespace GolemFactory.Civic
{
    /// <summary>One stage of a civic work: goods a minute, sustained, as a tower stage asks.</summary>
    public sealed class CivicStage
    {
        public string stageName;
        public int nominalSeconds = 480;
        public List<StageDemand> demands = new List<StageDemand>();
    }

    /// <summary>
    /// A civic work (docs/endless-design.md §1.2), authored in <c>data/civic_works.json</c>: a
    /// project in the town square, built in stages, each demanding a rate as the Clock Tower's do.
    /// Its name is what a save keeps.
    /// </summary>
    public sealed class CivicWorkDefinition
    {
        public string name;

        /// <summary>The works come in this order, 1 first.</summary>
        public int order;

        /// <summary>As the player reads it: "The Tramway".</summary>
        public string workName;

        public List<CivicStage> stages = new List<CivicStage>();
    }

    /// <summary>
    /// The civic works after the Clock Tower (docs/endless-design.md §1.2): three, in order, on the
    /// tower's own stage machinery (a second <see cref="ClockTowerSite"/>: the meter, the min across
    /// demands, the 3x cap, any source counts). Opens when the tower stands; never in Creative
    /// (invariant 10). A stage finishing is the work's, not the tower's: it publishes
    /// <see cref="CivicStageCompletedEvent"/>, never the tower's event, so no fanfare, camera
    /// glide, autosave or Ledger stage follows it.
    /// </summary>
    public sealed class CivicWorks : ITickable
    {
        /// <summary>What the site's tiles are called (a golem's card, a prompt).</summary>
        public const string SiteName = "the civic works site";

        private readonly List<CivicWorkDefinition> _works;
        private readonly Func<bool> _open;

        public CivicWorks(IEnumerable<CivicWorkDefinition> works, Func<bool> open, bool enabled = true)
        {
            _works = (works ?? Enumerable.Empty<CivicWorkDefinition>())
                .Where(w => w != null && w.stages.Count > 0).OrderBy(w => w.order).ThenBy(w => w.name, StringComparer.Ordinal).ToList();
            _open = open ?? (() => false);
            Enabled = enabled;
            Site = new ClockTowerSite { PublishesTowerEvents = false, ClosedReason = "roped off until the Clock Tower stands" };
            Site.OpenWhen = () => Enabled && _open() && Current != null;
            Site.StageCompleted += OnStageCompleted;
            LoadWork();
        }

        public bool Enabled { get; }

        public IReadOnlyList<CivicWorkDefinition> Works => _works;

        /// <summary>The stage machinery for the work under way.</summary>
        public ClockTowerSite Site { get; }

        /// <summary>Works finished, so the index of the one under way.</summary>
        public int WorkIndex { get; private set; }

        /// <summary>The work under way, or null when all three stand.</summary>
        public CivicWorkDefinition Current => WorkIndex < _works.Count ? _works[WorkIndex] : null;

        public bool AllBuilt => Current == null;

        public bool IsOpen => Site.IsOpen;

        /// <summary>Stages built across every work: the overview's running total.</summary>
        public int StagesBuilt => _works.Take(WorkIndex).Sum(w => w.stages.Count) + (Current == null ? 0 : Site.StageIndex);

        public void Tick(long tick)
        {
            if (Enabled)
            {
                Site.Tick(tick);
            }
        }

        // The site's stage list is the work's, as tower stages. A stage completing moves the site's
        // own index; the last one finishes the work, and the next work's stages load.
        private void LoadWork()
        {
            CivicWorkDefinition work = Current;
            var stages = new List<ClockTowerStageDefinition>();
            if (work != null)
            {
                for (int i = 0; i < work.stages.Count; i++)
                {
                    var stage = new ClockTowerStageDefinition { name = work.name + "_" + (i + 1) };
                    stage.Configure(i + 1, work.stages[i].stageName, work.stages[i].nominalSeconds, work.stages[i].demands.ToArray());
                    stages.Add(stage);
                }
            }
            Site.SetStages(stages);
        }

        private void OnStageCompleted(int stageNumber, string stageName, bool isFinal, long tick)
        {
            CivicWorkDefinition work = Current;
            if (work == null)
            {
                return;
            }
            EventBus.Publish(new CivicStageCompletedEvent(work.workName, stageNumber, stageName, isFinal));
            if (isFinal)
            {
                WorkIndex++;
                LoadWork();
            }
        }

        // --- Saves -----------------------------------------------------------------------------

        public Save.CivicEntry Capture() => new Save.CivicEntry
        {
            work = Current?.name,
            worksBuilt = WorkIndex,
            stageIndex = Site.StageIndex,
            progressUnits = Site.ProgressUnits,
        };

        /// <summary>The works as a save left them; none saved (an older save) starts at the first. Never clamps a count.</summary>
        public void Restore(Save.CivicEntry entry)
        {
            WorkIndex = 0;
            if (entry != null)
            {
                int byName = _works.FindIndex(w => w.name == entry.work);
                WorkIndex = byName >= 0 ? byName : Math.Max(0, Math.Min(entry.worksBuilt, _works.Count));
            }
            LoadWork();
            if (entry != null && Current != null)
            {
                Site.RestoreProgress(entry.stageIndex, entry.progressUnits, false);
            }
        }
    }
}
