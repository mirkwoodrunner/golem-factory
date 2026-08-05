using System.Collections.Generic;
using UnityEngine;

namespace GolemFactory.Belts
{
    // Renders one BeltSegment as a *directed lane*, not just its cargo. Everything here is
    // pooled and fixed-size: one stretched lane sprite, two end rollers, N scrolling arrows
    // (N fixed by lane length at resolve time) and one SpriteRenderer per segment SLOT bounded
    // by BeltSegment.Capacity. Nothing is instantiated per item -- see the architecture note in
    // CLAUDE.md; that rule is what this whole class is shaped around.
    //
    // Four things a player must be able to read at a glance, and where each comes from:
    //   WHERE it is      -> the lane sprite, which is drawn whether or not anything is on it.
    //   WHICH WAY        -> arrows scrolling start -> end, always, even on an empty belt.
    //   HOW FAST         -> arrow scroll speed is derived from the clock, so it literally
    //                       matches the speed an unobstructed item travels (and stops when
    //                       the clock is paused).
    //   BACKED UP        -> items interpolate toward BeltFlowUtility's predicted next-tick
    //                       progress, so a blocked item visibly stops; queued items go dark and
    //                       cool while the arrows slow to a halt and flash hot. See
    //                       BeltSignalUtility for why the jam alarm is brightness+motion rather
    //                       than a red hue, and for the rule that keeps the arrows on TOP of the
    //                       cargo (a jam fills the belt, so a signal under the cargo is hidden
    //                       exactly when it matters).
    public sealed class BeltSegmentVisual : MonoBehaviour
    {
        [System.Serializable]
        public struct ItemSpriteBinding
        {
            public string itemType;
            public Sprite sprite;
        }

        [SerializeField] private ConveyorSystemHolder conveyorHolder;
        [SerializeField] private string segmentId;
        [SerializeField] private Transform startPoint;
        [SerializeField] private Transform endPoint;
        [SerializeField] private Sprite itemSprite;

        // Per-item-type sprite table so a mixed belt is readable. Deliberately a string->Sprite
        // binding rather than a reference to Economy.ItemType, keeping Belts/ free of a
        // compile-time dependency on another gameplay namespace (same reasoning as the
        // no-reverse-reference-to-Golems/ rule). Falls back to itemSprite when unmatched.
        [SerializeField] private ItemSpriteBinding[] itemSprites;

        // Left unset, pooled item slots keep Unity's default (unlit) sprite material, same
        // fallback idiom as WorkbenchController's reskin sprite fields -- wired to the
        // project's lit material where the scene already has Light2D lighting.
        [SerializeField] private Material itemMaterial;

        [Header("Lane")]
        [SerializeField] private Sprite laneSprite;
        [SerializeField] private Sprite arrowSprite;
        [SerializeField] private Sprite rollerSprite;
        [SerializeField] private float arrowSpacing = 0.5f;
        [SerializeField] private float arrowEndFade = 0.2f;

        // Multiplied by the fitted item scale, so items always sit the same fraction of their
        // own height above the lane's centre line no matter how tight the segment is.
        [SerializeField] private float itemHeightOffset = 0.14f;

        [Header("Cargo fit")]
        [SerializeField] private float itemFitRatio = 1.25f;
        [SerializeField] private float minItemScale = 0.45f;
        [SerializeField] private float maxItemScale = 1f;

        // Optional. Without it the arrows fall back to assumedTicksPerSecond and keep scrolling
        // while the clock is paused; with it they track SimulationClock exactly.
        [SerializeField] private SimulationClockRunner clockRunner;
        [SerializeField] private float assumedTicksPerSecond = 10f;

        [Header("Flow colours")]
        // Field names deliberately differ from the previous pass's (jamColor/itemJamTint): the
        // old values are baked into every scene, and renaming is what lets the corrected defaults
        // below actually reach them instead of being overridden by stale serialized data.
        [SerializeField] private Color flowColor = new Color(1f, 0.74f, 0.32f, 1f);

        // Alarm red for semantics, flashed toward hot white for salience -- see the palette
        // argument in BeltSignalUtility. A steady dark red on this warm-brown floor measured at
        // 1/5 the healthy amber's contrast, i.e. the alarm was quieter than the healthy state.
        [SerializeField] private Color jamBaseColor = new Color(1f, 0.34f, 0.28f, 1f);
        [SerializeField] private Color jamPulseColor = new Color(1f, 0.95f, 0.9f, 1f);

        // Queued cargo goes cold and dim, not warm. The previous warm flush multiplied into
        // already-brown scrap for a luminance change too small to see; this drops ~38% of every
        // authored item colour's luminance AND swings it off the warm axis the art occupies, so
        // stalled cargo reads as cold dead metal without becoming a different item.
        [SerializeField] private Color itemQueuedTint = new Color(0.58f, 0.62f, 0.78f, 1f);

        private BeltSegment _segment;
        private SpriteRenderer[] _pool;
        private SpriteRenderer[] _arrows;
        private SpriteRenderer _lane;
        private SpriteRenderer _rollerStart;
        private SpriteRenderer _rollerEnd;
        private ParticleSystem _handoffSparkle;
        private Dictionary<string, Sprite> _spriteByItemType;

        private Vector3 _laneStart;
        private Vector3 _laneEnd;
        private Vector3 _laneDirection;
        private float _laneLength;
        private int _laneSortingOrder;
        private int _flowSignalSortingOrder;
        private float _scrollPhase;
        private float _pulsePhase;
        private float _itemScale = 1f;

        private float _previousHeadProgress = -1f;
        private int _previousItemCount;

        private void Awake()
        {
            TryResolveSegment();
            BuildHandoffSparkle();
        }

        /// <summary>
        /// Wires this visual to render a segment's CARGO only, leaving the lane strip, scrolling
        /// arrows and end rollers switched off (each of those is independently null-guarded).
        /// </summary>
        /// <remarks>
        /// Added for player-placed belts, which are one cell long and already draw their own
        /// plate and their own static direction arrow -- stretching a lane strip and scrolling a
        /// second set of arrows across a single tile would stack three direction cues on one
        /// cell. What they cannot draw themselves is the cargo: items on a belt have no
        /// GameObject of their own by design, so without this a working belt looks empty.
        ///
        /// Follows the Configure(...) idiom used across the project rather than requiring the
        /// Inspector, which is what lets a belt created at runtime wire itself.
        /// </remarks>
        public void ConfigureCargoOnly(
            ConveyorSystemHolder conveyor, string id, Transform start, Transform end,
            ItemSpriteBinding[] sprites, Material material)
        {
            conveyorHolder = conveyor;
            segmentId = id;
            startPoint = start;
            endPoint = end;
            itemSprites = sprites;
            itemMaterial = material;
            laneSprite = null;
            arrowSprite = null;
            rollerSprite = null;
            TryResolveSegment();
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
            float congestion = BeltFlowUtility.ComputeCongestion(items, _segment.Capacity, _segment.Length, 1f);

            UpdateLane(congestion);
            UpdateItems(items);
            UpdateHandoffSparkle(items);
        }

        private void UpdateLane(float congestion)
        {
            if (_arrows == null)
            {
                return;
            }

            float ticksPerSecond = assumedTicksPerSecond;
            float clockSpeed = 1f;
            if (clockRunner != null)
            {
                ticksPerSecond = clockRunner.Clock.TicksPerSecond;
                clockSpeed = clockRunner.Clock.State == Simulation.ClockState.Running ? clockRunner.Clock.Speed : 0f;
            }

            // Arrows slow as the lane jams so "slow + flashing" and "fast + amber" are the two
            // ends of one continuous readout rather than a binary light.
            float speed = BeltFlowUtility.ComputeTreadSpeed(_laneLength, _segment.Length, ticksPerSecond, clockSpeed)
                          * (1f - congestion);
            _scrollPhase = BeltFlowUtility.AdvanceScrollPhase(_scrollPhase, Time.deltaTime, speed, arrowSpacing);
            _pulsePhase = BeltSignalUtility.AdvancePulsePhase(
                _pulsePhase, Time.deltaTime, BeltSignalUtility.JamPulseHz, clockSpeed);

            float pulse = BeltSignalUtility.ComputeJamPulse(congestion, _pulsePhase);
            Color arrowColor = BeltSignalUtility.ComputeFlowSignalColor(
                flowColor, jamBaseColor, jamPulseColor, congestion, pulse);

            // Area, not just hue: no red is as luminous as the healthy amber against this warm
            // plank floor, so the chevrons swell to carry the alarm. See BeltSignalUtility.
            float signalScale = BeltSignalUtility.ComputeSignalScale(pulse);

            // The end drums double as flow lamps: extra alarm mass at the two points of the lane
            // that cargo never fully covers, for free (they are already pooled renderers).
            if (_rollerStart != null)
            {
                _rollerStart.color = Color.Lerp(Color.white, arrowColor, congestion);
            }

            if (_rollerEnd != null)
            {
                _rollerEnd.color = Color.Lerp(Color.white, arrowColor, congestion);
            }

            for (int i = 0; i < _arrows.Length; i++)
            {
                float distance = _scrollPhase + i * arrowSpacing;
                float fade = BeltFlowUtility.ComputeArrowFade(distance, _laneLength, arrowEndFade);
                if (fade <= 0f)
                {
                    _arrows[i].enabled = false;
                    continue;
                }

                _arrows[i].enabled = true;
                _arrows[i].transform.position =
                    BeltSignalUtility.ComputeFlowSignalPosition(_laneStart, _laneDirection, distance);
                _arrows[i].transform.localScale = Vector3.one * signalScale;
                _arrows[i].color = new Color(arrowColor.r, arrowColor.g, arrowColor.b, fade);
            }
        }

        private void UpdateItems(IReadOnlyList<ItemStack> items)
        {
            float tickFraction = clockRunner != null ? clockRunner.Clock.TickFraction : 0f;

            for (int i = 0; i < _pool.Length; i++)
            {
                if (i >= items.Count)
                {
                    _pool[i].enabled = false;
                    continue;
                }

                float predicted = BeltFlowUtility.PredictProgressAfterAdvance(items, i, _segment.Length, 1f);
                float display = BeltFlowUtility.ComputeDisplayProgress(items[i].Progress, predicted, tickFraction);
                float t = Mathf.Clamp01(display / _segment.Length);

                Vector3 groundPoint = Vector3.Lerp(_laneStart, _laneEnd, t);
                _pool[i].transform.position = groundPoint + new Vector3(0f, itemHeightOffset * _itemScale, 0f);

                // Sorted from the point ON the lane, not the raised sprite position, so the
                // cosmetic "sits on top of the belt" offset can't reorder anything. Per item,
                // never per segment: a diagonal lane spans a whole range of world Y, so one
                // constant order for the whole belt is guaranteed wrong at one end or the other.
                _pool[i].sortingOrder = BeltSignalUtility.ComputeCargoSortingOrder(groundPoint.y);

                _pool[i].sprite = ResolveSprite(items[i].ItemType);
                bool queued = BeltFlowUtility.IsQueuedBehindAnother(items, i, _segment.Length, 1f);
                _pool[i].color = BeltSignalUtility.ComputeCargoRenderColor(queued, itemQueuedTint);
                _pool[i].enabled = true;
            }
        }

        private Sprite ResolveSprite(string itemType)
        {
            Sprite resolved;
            if (itemType != null && _spriteByItemType != null && _spriteByItemType.TryGetValue(itemType, out resolved) && resolved != null)
            {
                return resolved;
            }

            return itemSprite;
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
                _handoffSparkle.transform.position = _laneEnd;
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
            if (conveyorHolder == null || startPoint == null || endPoint == null
                || !conveyorHolder.System.TryGetSegment(segmentId, out _segment))
            {
                return;
            }

            _laneStart = startPoint.position;
            _laneEnd = endPoint.position;
            Vector3 delta = _laneEnd - _laneStart;
            _laneLength = delta.magnitude;
            _laneDirection = _laneLength > 0f ? delta / _laneLength : Vector3.right;

            // Layering rules live in BeltSignalUtility so the "flow signal always outranks cargo"
            // contract is one testable property rather than two constants a reader has to
            // mentally subtract -- getting that wrong is exactly how the arrows ended up buried
            // under the cargo in the first pass.
            _laneSortingOrder = BeltSignalUtility.ComputeLaneSortingOrder(_laneStart.y, _laneEnd.y);
            _flowSignalSortingOrder = BeltSignalUtility.ComputeFlowSignalSortingOrder(_laneStart.y, _laneEnd.y);

            BuildSpriteLookup();
            BuildLane();
            BuildArrows();
            BuildItemPool();
        }

        private void BuildSpriteLookup()
        {
            _spriteByItemType = new Dictionary<string, Sprite>();
            if (itemSprites == null)
            {
                return;
            }

            for (int i = 0; i < itemSprites.Length; i++)
            {
                if (!string.IsNullOrEmpty(itemSprites[i].itemType))
                {
                    _spriteByItemType[itemSprites[i].itemType] = itemSprites[i].sprite;
                }
            }
        }

        private void BuildLane()
        {
            if (laneSprite == null)
            {
                return;
            }

            _lane = NewChild("Lane", laneSprite, itemMaterial, _laneSortingOrder);
            _lane.transform.position = (_laneStart + _laneEnd) * 0.5f;
            _lane.transform.rotation = Quaternion.Euler(0f, 0f,
                BeltFlowUtility.ComputeLaneAngleDegrees(_laneStart, _laneEnd));

            // The lane art is deliberately uniform along its length, so stretching X to fit an
            // arbitrary segment is invisible -- no repeated rivets to smear.
            float spriteLength = laneSprite.bounds.size.x;
            if (spriteLength > 0f)
            {
                _lane.transform.localScale = new Vector3(_laneLength / spriteLength, 1f, 1f);
            }

            _lane.enabled = true;

            if (rollerSprite != null)
            {
                _rollerStart = NewChild("RollerStart", rollerSprite, itemMaterial,
                    BeltSignalUtility.ComputeRollerSortingOrder(_laneStart.y));
                _rollerStart.transform.position = _laneStart;
                _rollerStart.enabled = true;

                _rollerEnd = NewChild("RollerEnd", rollerSprite, itemMaterial,
                    BeltSignalUtility.ComputeRollerSortingOrder(_laneEnd.y));
                _rollerEnd.transform.position = _laneEnd;
                _rollerEnd.enabled = true;
            }
        }

        private void BuildArrows()
        {
            if (arrowSprite == null)
            {
                _arrows = new SpriteRenderer[0];
                return;
            }

            float angle = BeltFlowUtility.ComputeLaneAngleDegrees(_laneStart, _laneEnd);
            int count = BeltFlowUtility.ComputeArrowCount(_laneLength, arrowSpacing);
            _arrows = new SpriteRenderer[count];
            for (int i = 0; i < count; i++)
            {
                // Unlit (no itemMaterial) on purpose, and sorted ABOVE every item on this lane:
                // the direction arrows are a HUD element painted on the world, and "which way /
                // is it jammed" has to survive a wall-to-wall loaded belt, which is both the
                // normal working state and the jam state.
                _arrows[i] = NewChild("Arrow" + i, arrowSprite, null, _flowSignalSortingOrder);
                _arrows[i].transform.rotation = Quaternion.Euler(0f, 0f, angle);
            }
        }

        private void BuildItemPool()
        {
            float spriteWorldSize = itemSprite != null ? itemSprite.bounds.size.x : 0.5f;
            _itemScale = BeltFlowUtility.ComputeItemScale(
                _laneLength, _segment.Length, spriteWorldSize, itemFitRatio, minItemScale, maxItemScale);

            _pool = new SpriteRenderer[_segment.Capacity];
            for (int i = 0; i < _pool.Length; i++)
            {
                _pool[i] = NewChild("ItemSlot" + i, itemSprite, itemMaterial, 0);
                _pool[i].transform.localScale = Vector3.one * _itemScale;
            }
        }

        private SpriteRenderer NewChild(string childName, Sprite sprite, Material material, int sortingOrder)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(transform, false);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            if (material != null)
            {
                renderer.sharedMaterial = material;
            }

            renderer.sortingOrder = sortingOrder;
            renderer.enabled = false;
            return renderer;
        }
    }
}
