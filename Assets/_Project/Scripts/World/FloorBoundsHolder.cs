using UnityEngine;

namespace GolemFactory.World
{
    /// <summary>
    /// Scene presence for <see cref="FloorBounds"/>, following the Holder pattern. A scene with
    /// no holder simply never expands and reads the authored extents, which is every scene that
    /// predates Floor Expansion.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FloorBoundsHolder : MonoBehaviour
    {
        [SerializeField] private int startingNorthExtent = FloorLayout.DefaultNorthExtent;

        private FloorBounds _bounds;

        public FloorBounds Bounds
        {
            get
            {
                if (_bounds == null)
                {
                    _bounds = new FloorBounds(FloorLayout.HalfExtent, startingNorthExtent);
                }

                return _bounds;
            }
        }
    }
}
