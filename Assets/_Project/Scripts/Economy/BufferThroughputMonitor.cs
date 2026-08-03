using UnityEngine;

namespace GolemFactory.Economy
{
    // Thin MonoBehaviour that owns a BufferRateTracker and feeds it Time.time -- the same
    // Holder split as GridMapHolder/ConveyorSystemHolder/StorageBufferRegistryHolder, kept
    // deliberately dumb so all the actual math stays in the engine-free tracker.
    //
    // Lives on the SAME GameObject as the StorageBufferRegistryHolder it samples (it
    // resolves the holder off its own GameObject when the field is unset), for two
    // reasons: that object is always active, so sampling continues while the Management
    // HUD is closed or on another tab (a rate that only starts accumulating when you open
    // the panel is useless), and it needs no cross-object scene wiring in either scene.
    //
    // Presentation-side by construction: it only reads Quantities. Nothing here is part of
    // the tick loop and nothing here writes back into the simulation.
    [DisallowMultipleComponent]
    public sealed class BufferThroughputMonitor : MonoBehaviour
    {
        [SerializeField] private StorageBufferRegistryHolder bufferRegistryHolder;
        [SerializeField] private float windowSeconds = BufferRateTracker.DefaultWindowSeconds;
        [SerializeField] private float sampleIntervalSeconds = BufferRateTracker.DefaultSampleIntervalSeconds;

        private BufferRateTracker _tracker;
        private float _nextSampleTime;

        // Built lazily rather than in Awake() for the same reason AssemblyLineStateHolder
        // builds its State in a field initializer: Awake doesn't run outside Play Mode, and
        // a caller (test or another component's Awake) must never see a null tracker.
        public BufferRateTracker Tracker
        {
            get
            {
                if (_tracker == null)
                {
                    _tracker = new BufferRateTracker(windowSeconds);
                }

                return _tracker;
            }
        }

        public void Configure(StorageBufferRegistryHolder holder) => bufferRegistryHolder = holder;

        private void Awake()
        {
            if (bufferRegistryHolder == null)
            {
                bufferRegistryHolder = GetComponent<StorageBufferRegistryHolder>();
            }
        }

        private void Update()
        {
            if (bufferRegistryHolder == null || Time.time < _nextSampleTime)
            {
                return;
            }

            _nextSampleTime = Time.time + Mathf.Max(0.01f, sampleIntervalSeconds);
            Tracker.Sample(Time.time, bufferRegistryHolder.Registry);
        }

        /// <summary>
        /// Convenience passthrough so UI code doesn't have to reach through
        /// <see cref="Tracker"/> for the one thing it wants.
        /// </summary>
        public bool TryGetRatePerMinute(string bufferId, string itemType, out float ratePerMinute) =>
            Tracker.TryGetRatePerMinute(bufferId, itemType, out ratePerMinute);
    }
}
