using System.Collections.Generic;
using UnityEngine;

namespace GolemFactory.Belts
{
    // Cheapest "visualize flow" option that isn't a GameObject-per-item: one pooled
    // SpriteRenderer per segment SLOT, bounded by the fixed BeltSegment.Capacity, positioned
    // each frame from BeltSegment.Items. Pool size never grows/shrinks with item throughput.
    public sealed class BeltSegmentVisual : MonoBehaviour
    {
        [SerializeField] private ConveyorSystemHolder conveyorHolder;
        [SerializeField] private string segmentId;
        [SerializeField] private Transform startPoint;
        [SerializeField] private Transform endPoint;
        [SerializeField] private Sprite itemSprite;

        private BeltSegment _segment;
        private SpriteRenderer[] _pool;

        private void Awake()
        {
            if (conveyorHolder == null || !conveyorHolder.System.TryGetSegment(segmentId, out _segment))
            {
                return;
            }

            _pool = new SpriteRenderer[_segment.Capacity];
            for (int i = 0; i < _pool.Length; i++)
            {
                var slot = new GameObject($"ItemSlot{i}");
                slot.transform.SetParent(transform);
                var renderer = slot.AddComponent<SpriteRenderer>();
                renderer.sprite = itemSprite;
                renderer.enabled = false;
                _pool[i] = renderer;
            }
        }

        private void LateUpdate()
        {
            if (_segment == null)
            {
                return;
            }

            IReadOnlyList<ItemStack> items = _segment.Items;
            for (int i = 0; i < _pool.Length; i++)
            {
                if (i < items.Count)
                {
                    float t = Mathf.Clamp01(items[i].Progress / _segment.Length);
                    _pool[i].transform.position = Vector3.Lerp(startPoint.position, endPoint.position, t);
                    _pool[i].enabled = true;
                }
                else
                {
                    _pool[i].enabled = false;
                }
            }
        }
    }
}
