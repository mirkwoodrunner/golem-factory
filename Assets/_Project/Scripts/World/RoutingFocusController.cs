using UnityEngine;
using GolemFactory.Golems;

namespace GolemFactory.World
{
    // Scene-level arbiter that lights exactly one golem's source/target tiles: the one nearest
    // the player. Without this every golem would draw its own pair and a working factory would
    // turn into a field of glowing diamonds that says nothing about any single machine.
    //
    // Also the component that retrofits GolemFacingIndicator onto golems, so a golem built at
    // runtime (GolemConstructionStation instantiates GolemPrefab) gets its markers without the
    // prefab needing to carry sprite references it would then have to re-wire per scene -- the
    // cross-prefab-reference trap called out in the architecture notes.
    public sealed class RoutingFocusController : MonoBehaviour
    {
        [SerializeField] private Transform focusOrigin;
        [SerializeField] private Sprite arrowSprite;
        [SerializeField] private Sprite tileSprite;
        [SerializeField] private Vector2 cellSize = new Vector2(1f, 0.5f);

        // Roughly three tiles. Far enough that walking up to a golem lights it before the
        // player is standing on top of it, close enough that only one is ever lit in a
        // reasonably spaced factory.
        [SerializeField] private float focusRange = 3.5f;

        // Re-scanned rather than tracked, matching PlayerInteractor.RefreshInteractables --
        // golems are created and destroyed at runtime and there is no registry of them.
        [SerializeField] private float rescanInterval = 0.5f;

        private GolemEntity[] _golems = new GolemEntity[0];
        private GolemFacingIndicator[] _indicators = new GolemFacingIndicator[0];
        private Vector3[] _positions = new Vector3[0];
        private float _rescanTimer;

        public void Configure(Transform origin, Sprite arrow, Sprite tile, Vector2 gridCellSize, float range)
        {
            focusOrigin = origin;
            arrowSprite = arrow;
            tileSprite = tile;
            cellSize = gridCellSize;
            focusRange = range;
        }

        /// <summary>Index of the currently focused golem, or -1. Exposed for tests.</summary>
        public int FocusedIndex { get; private set; } = RoutingFocus.None;

        private void OnEnable() => Rescan();

        private void Update()
        {
            _rescanTimer -= Time.deltaTime;
            if (_rescanTimer <= 0f)
            {
                Rescan();
                _rescanTimer = rescanInterval;
            }

            RefreshFocus();
        }

        /// <summary>
        /// Re-collects golems and makes sure each has an indicator. Public so a station that
        /// just built a golem (or a test) can light it immediately rather than waiting out the
        /// rescan interval.
        /// </summary>
        public void Rescan()
        {
            _golems = FindObjectsByType<GolemEntity>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            if (_indicators.Length != _golems.Length)
            {
                _indicators = new GolemFacingIndicator[_golems.Length];
                _positions = new Vector3[_golems.Length];
            }

            for (int i = 0; i < _golems.Length; i++)
            {
                if (_golems[i] == null)
                {
                    _indicators[i] = null;
                    continue;
                }

                GolemFacingIndicator indicator = _golems[i].GetComponent<GolemFacingIndicator>();
                if (indicator == null)
                {
                    indicator = _golems[i].gameObject.AddComponent<GolemFacingIndicator>();
                    indicator.Configure(_golems[i], arrowSprite, tileSprite, cellSize);
                }

                _indicators[i] = indicator;
            }
        }

        /// <summary>
        /// Re-picks the focused golem and updates every indicator. Public so a test can assert
        /// what is lit without waiting for an Update tick.
        /// </summary>
        public void RefreshFocus()
        {
            if (focusOrigin == null)
            {
                return;
            }

            for (int i = 0; i < _golems.Length; i++)
            {
                // Destroyed golems leave null holes; park them at +infinity so they can never
                // be selected, the same trick PlayerInteractor.FillPositions uses.
                _positions[i] = _golems[i] != null
                    ? _golems[i].transform.position
                    : new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            }

            FocusedIndex = RoutingFocus.SelectNearestIndex(focusOrigin.position, _positions, focusRange);

            for (int i = 0; i < _indicators.Length; i++)
            {
                if (_indicators[i] != null)
                {
                    _indicators[i].ShowTiles = i == FocusedIndex;
                }
            }
        }
    }
}
