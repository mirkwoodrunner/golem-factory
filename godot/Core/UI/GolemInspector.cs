using System;
using System.Collections.Generic;
using GolemFactory.Compat;
using GolemFactory.Golems;

namespace GolemFactory.UI
{
    /// <summary>
    /// Which golem the mouse is over (docs/onboarding-feedback-design.md §4.1). A node feeds the
    /// pointer's cell each frame, or null when there is no pointer in the world (over UI, a full
    /// screen open, the wrecking bar up, the pointer off the window); <see cref="Hovered"/>
    /// re-picks from the live roster on every read, so a golem dismantled or picked up stops
    /// being hovered at once.
    /// </summary>
    public sealed class GolemInspector
    {
        private readonly Func<IEnumerable<GolemEntity>> _golems;
        private Vector2Int? _cell;

        public GolemInspector(Func<IEnumerable<GolemEntity>> golems)
        {
            _golems = golems;
        }

        /// <summary>The pointer's cell this frame; null = no pointer in the world.</summary>
        public void Point(Vector2Int? cell) => _cell = cell;

        public Vector2Int? PointerCell => _cell;

        /// <summary>The golem under the pointer, or null.</summary>
        public GolemEntity Hovered =>
            _cell is Vector2Int cell ? Pick(cell, _golems?.Invoke() ?? Array.Empty<GolemEntity>()) : null;

        /// <summary>
        /// The golem standing on <paramref name="cell"/>; else the one standing on the cell south
        /// of it (Core +y is north), since a standing sprite is about two cells tall and its body
        /// covers the cell above its feet. Ties go to the lowest id. A held golem is never picked.
        /// </summary>
        public static GolemEntity Pick(Vector2Int cell, IEnumerable<GolemEntity> golems)
        {
            GolemEntity onCell = null, below = null;
            var south = new Vector2Int(cell.x, cell.y - 1);
            foreach (GolemEntity golem in golems)
            {
                if (golem == null || golem.IsRemoved || golem.IsHeld)
                {
                    continue;
                }
                if (golem.Cell == cell)
                {
                    onCell = Lower(onCell, golem);
                }
                else if (golem.Cell == south)
                {
                    below = Lower(below, golem);
                }
            }
            return onCell ?? below;
        }

        private static GolemEntity Lower(GolemEntity a, GolemEntity b) =>
            a == null || string.CompareOrdinal(b.GolemId, a.GolemId) < 0 ? b : a;
    }
}
