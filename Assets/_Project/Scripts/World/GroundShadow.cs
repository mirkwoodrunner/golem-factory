using UnityEngine;

namespace GolemFactory.World
{
    // Contact shadow so golems, the player, buildings and props sit *in* the scene instead of
    // floating on top of the floor. Visual-only, same LateUpdate-driven idiom as
    // YSortSpriteRenderer and Belts/BeltSegmentVisual; touches no simulation state.
    //
    // It creates its own child SpriteRenderer rather than requiring one to be wired per prefab,
    // because the child needs a sorting order that a YSortSpriteRenderer could not produce:
    // the shadow's own world Y is *lower* than its owner's, so a Y-sorted child would compute
    // a HIGHER sorting order and draw the shadow in front of the thing casting it. Instead the
    // order is derived from the OWNER's Y, minus one, which puts the shadow immediately behind
    // its owner and still far in front of the floor Tilemap (which renders at -30000).
    //
    // The shadow is a child with a fixed local offset, so it inherits GolemVisual's idle bob
    // and stall shake. That is deliberate: the offsets are a few screen pixels, and having the
    // whole silhouette move together reads better than a shadow that visibly detaches.
    public sealed class GroundShadow : MonoBehaviour
    {
        private const string ChildName = "GroundShadow";

        [SerializeField] private Sprite shadowSprite;
        [SerializeField] private Vector2 groundOffset = Vector2.zero;
        [SerializeField] private float scale = 1f;
        [SerializeField, Range(0f, 1f)] private float opacity = 0.85f;

        // Sprite pivots are not consistent across this project's art -- the player sprite is
        // pivoted bottom-centre (transform == feet) while every chassis sprite is pivoted
        // centre (feet half a sprite-height below the transform). Rather than hand-tuning an
        // offset per object, derive the sprite's own bottom edge and drop the shadow there.
        // Recomputed every frame on purpose: GolemVisual assigns the chassis sprite after
        // Awake has already run, so a value cached at startup would be wrong for every golem
        // built at the construction station.
        [SerializeField] private bool anchorToSpriteBottom = true;

        private SpriteRenderer _shadowRenderer;
        private SpriteRenderer _ownerRenderer;

        private void Awake()
        {
            _ownerRenderer = GetComponent<SpriteRenderer>();
            EnsureShadow();
        }

        // Same Configure(...) escape hatch the rest of the project uses for references that are
        // awkward to wire purely through the Inspector (GolemEntity.Configure,
        // WorkbenchController.Configure*), so bootstrap/Editor tooling can set this up directly.
        // `anchorToBottom` must be false for sprites that are already ground-anchored -- the
        // isometric props are pivoted on the centre of their base diamond, so deriving a
        // "sprite bottom" for them would sink the shadow below the floor.
        public void Configure(Sprite sprite, Vector2 offset, float shadowScale, float shadowOpacity,
            bool anchorToBottom)
        {
            shadowSprite = sprite;
            groundOffset = offset;
            scale = shadowScale;
            opacity = shadowOpacity;
            anchorToSpriteBottom = anchorToBottom;
            _ownerRenderer = GetComponent<SpriteRenderer>();
            EnsureShadow();
            ApplyAppearance();
        }

        private void LateUpdate()
        {
            Refresh();
        }

        // Public so Editor tooling can place a shadow on a static, never-moving object and have
        // it render correctly *without* Play Mode -- LateUpdate does not run in EditMode (the
        // same [ExecuteAlways] gotcha that has bitten GolemVisual's sprite assignment and
        // GolemEntity's Signal-trigger subscription).
        public void Refresh()
        {
            if (_shadowRenderer == null)
            {
                return;
            }
            _shadowRenderer.sortingOrder = YSortUtility.ComputeSortingOrder(transform.position.y) - 1;
            _shadowRenderer.transform.localPosition =
                new Vector3(groundOffset.x, groundOffset.y + ComputeSpriteBottom(), 0f);
        }

        private float ComputeSpriteBottom()
        {
            if (!anchorToSpriteBottom)
            {
                return 0f;
            }
            if (_ownerRenderer == null)
            {
                _ownerRenderer = GetComponent<SpriteRenderer>();
            }
            if (_ownerRenderer == null || _ownerRenderer.sprite == null)
            {
                return 0f;
            }
            Sprite sprite = _ownerRenderer.sprite;
            return -sprite.pivot.y / sprite.pixelsPerUnit;
        }

        private void EnsureShadow()
        {
            if (_shadowRenderer != null)
            {
                return;
            }

            Transform existing = transform.Find(ChildName);
            if (existing != null)
            {
                _shadowRenderer = existing.GetComponent<SpriteRenderer>();
            }
            if (_shadowRenderer == null)
            {
                var child = new GameObject(ChildName);
                child.transform.SetParent(transform, worldPositionStays: false);
                _shadowRenderer = child.AddComponent<SpriteRenderer>();
            }
            ApplyAppearance();
        }

        private void ApplyAppearance()
        {
            if (_shadowRenderer == null)
            {
                return;
            }
            _shadowRenderer.sprite = shadowSprite;
            _shadowRenderer.color = new Color(1f, 1f, 1f, opacity);
            _shadowRenderer.transform.localScale = new Vector3(scale, scale, 1f);
            Refresh();
        }
    }
}
