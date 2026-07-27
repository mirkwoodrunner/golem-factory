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

        // Left unset, pooled item slots keep Unity's default (unlit) sprite material, same
        // fallback idiom as WorkbenchController's reskin sprite fields -- wired to the
        // project's lit material where the scene already has Light2D lighting.
        [SerializeField] private Material itemMaterial;

        private BeltSegment _segment;
        private SpriteRenderer[] _pool;
        private ParticleSystem _handoffSparkle;
        private float _previousHeadProgress = -1f;
        private int _previousItemCount;

        private void Awake()
        {
            TryResolveSegment();
            BuildHandoffSparkle();
        }

        // Bootstrap scripts (e.g. BeltDemoBootstrap) register segments in their own Start(),
        // which always runs after every Awake() -- so resolving only in Awake() means this
        // component silently never finds its segment. Keep retrying in LateUpdate until it
        // resolves once, which makes wiring order-independent without needing every caller
        // to register segments earlier than Start().
        private void LateUpdate()
        {
            if (_segment == null)
            {
                TryResolveSegment();
            }

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

            UpdateHandoffSparkle(items);
        }

        // Items are ordered head-first (index 0 = closest to exit) per BeltSegment's own
        // comment. A handoff is "item count dropped and the head was already at the end" --
        // tracking the head's progress across frames survives index reshuffling from other
        // items advancing/leaving, unlike tracking progress per pool slot would.
        private void UpdateHandoffSparkle(IReadOnlyList<ItemStack> items)
        {
            int currentCount = items.Count;
            if (currentCount < _previousItemCount && _previousHeadProgress >= _segment.Length - 0.01f && _handoffSparkle != null)
            {
                _handoffSparkle.transform.position = endPoint.position;
                _handoffSparkle.Emit(8);
            }

            _previousItemCount = currentCount;
            _previousHeadProgress = currentCount > 0 ? items[0].Progress : -1f;
        }

        private void BuildHandoffSparkle()
        {
            var go = new GameObject("HandoffSparkle");
            go.transform.SetParent(transform, false);
            _handoffSparkle = go.AddComponent<ParticleSystem>();

            // AddComponent<ParticleSystem> starts it playing immediately (default
            // playOnAwake), which blocks reconfiguring `main` below -- stop it first.
            _handoffSparkle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _handoffSparkle.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.5f;
            main.startLifetime = 0.4f;
            main.startSpeed = 1.2f;
            main.startSize = 0.08f;
            main.startColor = new Color(0.37f, 0.84f, 0.78f, 1f); // TEAL_GLOW, matches palette
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = _handoffSparkle.emission;
            emission.rateOverTime = 0f;

            var shape = _handoffSparkle.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;
        }

        private void TryResolveSegment()
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
                if (itemMaterial != null)
                {
                    renderer.sharedMaterial = itemMaterial;
                }
                renderer.enabled = false;
                _pool[i] = renderer;
            }
        }
    }
}
