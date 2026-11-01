namespace GolemFactory.Audio
{
    /// <summary>The mixer bus a slot plays on (docs/audio-design.md §5).</summary>
    public enum AudioBus
    {
        Music,
        Ambience,
        Effects,
        UI,
    }

    /// <summary>
    /// One sound the game can make, from <c>data/audio.json</c> (docs/audio-design.md §3). Code
    /// plays a slot by its name, never a file path, so a final asset replaces a placeholder by a
    /// new file at <see cref="path"/> (invariant 1).
    /// </summary>
    public sealed class AudioSlotDefinition
    {
        public string name;

        /// <summary>The file, <c>res://audio/&lt;name&gt;.wav</c>.</summary>
        public string path;

        public AudioBus bus = AudioBus.Effects;

        /// <summary>The slot's level in the mix, in dB (0 = the file as it is).</summary>
        public float volumeDb;

        /// <summary>At most this many copies at once; a request over the cap is dropped (§5).</summary>
        public int maxCopies = 1;

        /// <summary>Played where its golem or building stands, panned and quieted by the camera.</summary>
        public bool positional;

        /// <summary>A loop that plays all the time (ambience, music) rather than on request.</summary>
        public bool loop;
    }
}
