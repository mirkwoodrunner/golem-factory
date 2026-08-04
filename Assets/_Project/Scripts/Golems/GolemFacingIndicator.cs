using UnityEngine;
using GolemFactory.World;

namespace GolemFactory.Golems
{
    // Makes facing-based routing visible. Before this, nothing on screen said which way a golem
    // pointed or which tiles it was reading -- the mechanic was real in simulation and entirely
    // invisible in play, which is the complaint that started this pass.
    //
    // Three pieces of presentation, built in code rather than authored as prefab children so a
    // golem gets them with no scene wiring (the same self-assembling idiom FloatingPopup and
    // InteractionPromptView use):
    //   * a chevron beside the golem, always on, pointing the way it faces;
    //   * a cool teal diamond on the SOURCE tile (what it pulls from);
    //   * a warm amber diamond on the TARGET tile (what it pushes to).
    //
    // The two tile diamonds are shown only while this golem is the focused one (see
    // RoutingFocusController) -- lighting every golem's pair at once turns a working factory
    // into a field of glowing tiles and communicates nothing.
    //
    // Reads GolemEntity only; never writes to it.
    public sealed class GolemFacingIndicator : MonoBehaviour
    {
        [SerializeField] private GolemEntity golem;
        [SerializeField] private Sprite arrowSprite;
        [SerializeField] private Sprite tileSprite;
        [SerializeField] private Vector2 cellSize = new Vector2(1f, 0.5f);

        // Warm amber for the push side, cool teal for the pull side. Deliberately NOT a
        // red/green pair: against this warm wood-and-brass floor dark red measured 5.4x
        // quieter than amber, so a red marker reads as "slightly darker floor". Teal is the
        // one hue in the palette (it is already the Aether accent) that is unambiguously
        // *cool*, so the two ends of the chain separate by temperature, not just brightness --
        // which also keeps them distinguishable without relying on hue discrimination.
        // The target marker is pushed near-white rather than mid-amber because the thing it
        // most often lands on is a BELT, whose own art is brass -- a mid-amber diamond on a
        // brass plate was measured in-scene as almost invisible, the same "warm on warm"
        // failure that made the old build ghost unreadable on the plank floor. Raising value
        // (not hue) is what separates it, since the palette has no cool warm.
        private static readonly Color SourceTint = new Color(0.37f, 0.90f, 0.84f, 0.72f);
        private static readonly Color TargetTint = new Color(1f, 0.90f, 0.66f, 0.80f);
        private static readonly Color ArrowTint = new Color(1f, 0.80f, 0.42f, 0.95f);

        private SpriteRenderer _arrow;
        private SpriteRenderer _sourceTile;
        private SpriteRenderer _targetTile;
        private GridCoordinateConverter _converter;
        private bool _showTiles;

        /// <summary>
        /// Whether this golem's source/target tiles are lit. Driven by RoutingFocusController
        /// so only the golem the player is nearest to draws them.
        /// </summary>
        public bool ShowTiles
        {
            get { return _showTiles; }
            set
            {
                _showTiles = value;
                ApplyTileVisibility();
            }
        }

        public void Configure(GolemEntity target, Sprite arrow, Sprite tile, Vector2 gridCellSize)
        {
            golem = target;
            arrowSprite = arrow;
            tileSprite = tile;
            cellSize = gridCellSize;
            _converter = new GridCoordinateConverter(cellSize);
        }

        private void Awake()
        {
            if (golem == null)
            {
                golem = GetComponent<GolemEntity>();
            }

            _converter = new GridCoordinateConverter(cellSize);
        }

        // Polled rather than pushed: facing changes via SetPlacement, which is a plain setter
        // with no event, and adding one just for the marker would put UI concerns on the
        // simulation type. A handful of transform writes per frame is not worth that coupling.
        private void LateUpdate() => Refresh();

        /// <summary>
        /// Re-derives every marker from the golem's current cell/facing. Public so a test can
        /// assert placement without waiting for a frame.
        /// </summary>
        public void Refresh()
        {
            if (golem == null)
            {
                return;
            }

            EnsureRenderers();

            if (_arrow != null)
            {
                Vector2 direction = FacingVisuals.ScreenDirection(golem.Facing, cellSize);
                _arrow.transform.localPosition = new Vector3(direction.x * 0.42f, direction.y * 0.42f + 0.15f, 0f);
                _arrow.transform.localRotation =
                    Quaternion.Euler(0f, 0f, FacingVisuals.ScreenAngleDegrees(golem.Facing, cellSize));
            }

            if (_showTiles)
            {
                // World-positioned, not parented offsets: the golem itself bobs and shakes
                // (GolemVisual), and a tile marker that bobbed with it would stop reading as
                // "this cell" the moment the golem stalled and jolted.
                PositionTile(_sourceTile, golem.SourceCell);
                PositionTile(_targetTile, golem.TargetCell);
            }
        }

        private void PositionTile(SpriteRenderer tile, Vector2Int cell)
        {
            if (tile != null)
            {
                tile.transform.position = _converter.CellToWorldCenter(cell);
            }
        }

        private void EnsureRenderers()
        {
            if (_arrow == null && arrowSprite != null)
            {
                _arrow = CreateRenderer("FacingArrow", arrowSprite, ArrowTint, transform, 20);
            }

            if (tileSprite == null)
            {
                return;
            }

            if (_sourceTile == null)
            {
                // Not parented to the golem -- see the note in Refresh.
                _sourceTile = CreateRenderer("SourceTile", tileSprite, SourceTint, null, -10);
                _sourceTile.gameObject.SetActive(_showTiles);
            }

            if (_targetTile == null)
            {
                _targetTile = CreateRenderer("TargetTile", tileSprite, TargetTint, null, -10);
                _targetTile.gameObject.SetActive(_showTiles);
            }
        }

        private SpriteRenderer CreateRenderer(string name, Sprite sprite, Color tint, Transform parent, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = tint;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }

        private void ApplyTileVisibility()
        {
            if (_sourceTile != null)
            {
                _sourceTile.gameObject.SetActive(_showTiles);
            }

            if (_targetTile != null)
            {
                _targetTile.gameObject.SetActive(_showTiles);
            }
        }

        // The tile markers live outside this GameObject's hierarchy, so they do not get
        // destroyed with it automatically.
        private void OnDestroy()
        {
            if (_sourceTile != null)
            {
                Destroy(_sourceTile.gameObject);
            }

            if (_targetTile != null)
            {
                Destroy(_targetTile.gameObject);
            }
        }
    }
}
