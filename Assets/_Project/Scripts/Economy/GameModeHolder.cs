using UnityEngine;

namespace GolemFactory.Economy
{
    /// <summary>
    /// Scene presence for <see cref="GameMode"/>, following the Holder pattern every other
    /// plain-C# manager here uses (<c>GridMapHolder</c>, <c>ConveyorSystemHolder</c>,
    /// <c>SteamNetworkHolder</c>).
    ///
    /// <para>
    /// The serialized field is the scene's authored answer; the property is what everything
    /// reads. A scene with no holder at all means "not creative", which is the same additive
    /// fork steam, spatial routing and the bay cap all ride: absent, the mechanic is simply not
    /// in play.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameModeHolder : MonoBehaviour
    {
        [SerializeField] private bool creativeMode;

        private GameMode _mode;

        // Built lazily rather than in Awake, for the reason BufferThroughputMonitor records:
        // Awake does not run outside Play Mode, and a caller must never see a null instance.
        public GameMode Mode
        {
            get
            {
                if (_mode == null)
                {
                    _mode = new GameMode(creativeMode);
                }

                return _mode;
            }
        }

        /// <summary>
        /// Flips the mode at runtime and keeps the serialized field in step, so the Inspector
        /// does not read one thing while the game does another.
        /// </summary>
        public void SetCreativeMode(bool value)
        {
            creativeMode = value;
            Mode.IsCreativeMode = value;
        }
    }
}
