using UnityEngine;
using GolemFactory.Belts;
using GolemFactory.UI;

namespace GolemFactory.World
{
    // Thin spatial proxy for exactly one logical ResourceNode -- ResourceNode/
    // ResourceNodeRegistry stay pure logic with no Transform/world position at all
    // (unchanged since M5); this is what lets a player walk up to one. TryHarvest is a
    // one-line forward to ResourceNodeRegistry.TryExtract, the exact same call
    // Golems/GolemEntity.cs's TryExtractFromNode already makes via step.sourceId -- a
    // player harvesting and a golem extracting from the same nodeId genuinely compete
    // for the same RemainingQuantity, which is a fair emergent property, not something
    // this class needs to reconcile. Depositing the harvested item into a buffer is the
    // caller's job (Player/PlayerInteractor.cs), not this class's -- keeps this a pure
    // spatial+extraction proxy, matching GolemEntity's own extract/deposit split.
    //
    // It now also *renders* that quantity: the node drains in brightness and size as it is
    // worked (ResourceNodeVisualState), closing the "no node-depletion visual feedback"
    // scope cut. Deliberately polled in Update rather than pushed on harvest, because a
    // golem's ExtractFromNode step drains the same node without ever calling through here --
    // pushing would leave the marker stale exactly when the player is watching a golem work.
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class ResourceNodeMarker : MonoBehaviour
    {
        [SerializeField] private ResourceNodeRegistryHolder nodeRegistryHolder;
        [SerializeField] private string nodeId;

        private SpriteRenderer _renderer;
        private Vector3 _baseScale = Vector3.one;
        // Captured exactly once. RefreshVisualState writes localScale, so re-reading it later
        // (e.g. if Awake runs after a Configure-driven refresh) would compound the shrink.
        private bool _hasBaseScale;
        private int _peakQuantity;
        private float _pulseElapsed = -1f;
        private bool _wasDepleted;

        private const float HarvestPulseDuration = 0.22f;
        private const float HarvestPulseAmount = 0.16f;

        public string NodeId => nodeId;

        public void Configure(ResourceNodeRegistryHolder registryHolder, string id)
        {
            nodeRegistryHolder = registryHolder;
            nodeId = id;
            _peakQuantity = 0;
            RefreshVisualState();
        }

        private void Awake()
        {
            if (GetComponent<YSortSpriteRenderer>() == null)
            {
                gameObject.AddComponent<YSortSpriteRenderer>();
            }

            _renderer = GetComponent<SpriteRenderer>();
            CaptureBaseScale();
        }

        private void Start() => RefreshVisualState();

        private void CaptureBaseScale()
        {
            if (_hasBaseScale)
            {
                return;
            }

            _baseScale = transform.localScale;
            _hasBaseScale = true;
        }

        private void Update()
        {
            RefreshVisualState();

            if (_pulseElapsed >= 0f)
            {
                _pulseElapsed += Time.deltaTime;
                if (_pulseElapsed >= HarvestPulseDuration)
                {
                    _pulseElapsed = -1f;
                }
            }
        }

        /// <summary>
        /// Remaining quantity in the backing node, or <see cref="ResourceNode.Infinite"/>.
        /// Returns 0 (i.e. "nothing here") when the node isn't registered, so a mis-wired
        /// marker advertises itself as spent instead of promising resources it can't deliver.
        /// </summary>
        public int RemainingQuantity
        {
            get
            {
                if (nodeRegistryHolder == null || !nodeRegistryHolder.Registry.TryGetNode(nodeId, out ResourceNode node))
                {
                    return 0;
                }

                return node.RemainingQuantity;
            }
        }

        /// <summary>Item type this node yields, or an empty string if unresolved.</summary>
        public string ItemType
        {
            get
            {
                if (nodeRegistryHolder == null || !nodeRegistryHolder.Registry.TryGetNode(nodeId, out ResourceNode node))
                {
                    return "";
                }

                return node.ItemType;
            }
        }

        /// <summary>Highest quantity observed, the denominator for the drain visual.</summary>
        public int PeakQuantity => _peakQuantity;

        public bool IsDepleted => RemainingQuantity == 0;

        public bool TryHarvest(out ItemStack item)
        {
            item = default;
            bool harvested = nodeRegistryHolder != null && nodeRegistryHolder.Registry.TryExtract(nodeId, out item);
            if (harvested)
            {
                PlayHarvestPulse();
            }

            RefreshVisualState();
            return harvested;
        }

        /// <summary>
        /// Kicks the one-shot scale punch. Public so a golem-driven extraction could drive the
        /// same feedback later without going through the player's harvest path.
        /// </summary>
        public void PlayHarvestPulse() => _pulseElapsed = 0f;

        /// <summary>
        /// Re-derives tint and scale from the live remaining quantity. Public so a test can
        /// assert the visual without waiting for an Update tick.
        /// </summary>
        public void RefreshVisualState()
        {
            if (_renderer == null)
            {
                _renderer = GetComponent<SpriteRenderer>();
                if (_renderer == null)
                {
                    return;
                }
            }

            CaptureBaseScale();

            int remaining = RemainingQuantity;
            if (remaining > _peakQuantity)
            {
                _peakQuantity = remaining;
            }

            ResourceNodeVisual visual = ResourceNodeVisualState.Evaluate(remaining, _peakQuantity);
            _renderer.color = visual.Tint;
            _wasDepleted = visual.IsDepleted;

            float pulse = _pulseElapsed >= 0f
                ? FeedbackMotion.PulseScale(_pulseElapsed, HarvestPulseDuration, HarvestPulseAmount)
                : 1f;
            transform.localScale = _baseScale * visual.Scale * pulse;
        }

        /// <summary>Last computed depleted state, for tests and prompt text.</summary>
        public bool LastRenderedAsDepleted => _wasDepleted;
    }
}
