using System.Collections.Generic;
using GolemFactory.Golems;

namespace GolemFactory.Buildings
{
    // The golem cap: the construction station refuses to build past it. One fixed number, where
    // the frame budget was measured to hold (docs/performance-report-endless.md), with nothing to
    // buy. The bays were once upgraded six slots at a time for a flat price that never bound, a
    // toll rather than a decision; steam upkeep is what prices each golem (2026-11-19).
    public sealed class AssemblyBayStructure
    {
        /// <summary>The golem cap when the setup names none: where the endless frame budget holds.</summary>
        public const int DefaultCap = 112;

        private int maxGolemSlots = DefaultCap;

        private readonly List<GolemEntity> _assignedGolems = new List<GolemEntity>();

        /// <summary>The most golems that can stand at once.</summary>
        public int MaxGolemSlots => maxGolemSlots;

        /// <summary>The golem cap; the same number as <see cref="MaxGolemSlots"/>.</summary>
        public int GolemCap => maxGolemSlots;

        /// <summary>Sets the cap; 0 or less leaves it uncapped (a diagnostic load past the cap).</summary>
        public void ConfigureCap(int cap) => maxGolemSlots = cap > 0 ? cap : int.MaxValue;

        public IReadOnlyList<GolemEntity> AssignedGolems
        {
            get
            {
                PruneDestroyed();
                return _assignedGolems;
            }
        }

        /// <summary>Golems currently occupying a slot, destroyed ones already forgotten.</summary>
        public int OccupiedSlots
        {
            get
            {
                PruneDestroyed();
                return _assignedGolems.Count;
            }
        }

        /// <summary>Whether another golem could be built at all.</summary>
        public bool HasFreeSlot => OccupiedSlots < maxGolemSlots;

        /// <summary>
        /// Forgets golems that no longer exist. A destroyed golem holding a bay slot forever
        /// would turn the cap into a slow leak -- §10's own recovery route from an over-built
        /// factory is "delete golems, freeing both bay slots and upkeep instantly", so a slot
        /// that does not come back breaks the escape hatch rather than the accounting.
        ///
        /// <para>
        /// Derived from the golem rather than relying on every deletion path remembering to
        /// call <see cref="ReleaseGolem"/>: a removed golem reports
        /// <see cref="GolemEntity.IsRemoved"/>, so this cannot drift out of step with what is
        /// actually standing in the world. (In Unity the same job was done by a destroyed
        /// MonoBehaviour comparing equal to null.)
        /// </para>
        /// </summary>
        private void PruneDestroyed()
        {
            for (int i = _assignedGolems.Count - 1; i >= 0; i--)
            {
                if (_assignedGolems[i] == null || _assignedGolems[i].IsRemoved)
                {
                    _assignedGolems.RemoveAt(i);
                }
            }
        }

        public bool TryAssignGolem(GolemEntity golem)
        {
            PruneDestroyed();
            if (golem == null || _assignedGolems.Count >= maxGolemSlots || _assignedGolems.Contains(golem))
            {
                return false;
            }

            _assignedGolems.Add(golem);
            return true;
        }

        /// <summary>
        /// Puts a golem in a slot even if that takes the bay over its cap. For the LOAD path
        /// only: a save describes a factory that was legal when it was built, and refusing part
        /// of it on load would delete golems the player owns because a cap moved. The same
        /// reasoning as not re-charging a rebuilt building's cost.
        /// </summary>
        public void ForceAssignGolem(GolemEntity golem)
        {
            PruneDestroyed();
            if (golem != null && !_assignedGolems.Contains(golem))
            {
                _assignedGolems.Add(golem);
            }
        }

        public bool ReleaseGolem(GolemEntity golem) => _assignedGolems.Remove(golem);
    }
}
