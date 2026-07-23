using UnityEngine;

namespace GolemFactory.World
{
    // Visual-only, simulation-untouched -- same LateUpdate-driven idiom as
    // Belts/BeltSegmentVisual.cs. Drop onto any golem/building/item SpriteRenderer so
    // isometric depth looks right without hand-tuning a sortingOrder per object in the Editor.
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class YSortSpriteRenderer : MonoBehaviour
    {
        private SpriteRenderer _spriteRenderer;

        private void Awake()
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
        }

        private void LateUpdate()
        {
            _spriteRenderer.sortingOrder = YSortUtility.ComputeSortingOrder(transform.position.y);
        }
    }
}
