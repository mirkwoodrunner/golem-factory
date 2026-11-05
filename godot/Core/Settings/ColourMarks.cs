namespace GolemFactory.Settings
{
    /// <summary>
    /// The Colour-blind marks setting (docs/menus-design.md §5). A golem's source tile (teal) and
    /// target tile (gold) are told apart by colour alone, and the guide steers by those colours
    /// ("press R until gold is on the depot"). They are also the closest pair the game relies on
    /// for a protanope (ΔE2000 17.5, §5's table). With the marks on, the source tile wears
    /// diagonal stripes (<see cref="StripeAt"/>), so the two differ in shape, and the guide's
    /// colour words name the stripes. The Godot layer sets <see cref="Enabled"/>.
    /// </summary>
    public static class ColourMarks
    {
        public const string SourceToken = "[[source-tile]]";
        public const string TargetToken = "[[target-tile]]";

        /// <summary>The stripes' period and width, in art pixels of the 64-pixel tile.</summary>
        public const int StripePeriod = 12;
        public const int StripeWidth = 5;

        private static bool _enabled;

        public static bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled != value)
                {
                    _enabled = value;
                    Version++;
                }
            }
        }

        /// <summary>Changes whenever <see cref="Enabled"/> does, so text drawn once can redraw.</summary>
        public static int Version { get; private set; }

        /// <summary>The source tile as the guide names it: "teal", or "striped teal".</summary>
        public static string SourceWord => Enabled ? "striped teal" : "teal";

        /// <summary>The target tile as the guide names it: "gold", or "plain gold".</summary>
        public static string TargetWord => Enabled ? "plain gold" : "gold";

        public static string Fill(string text) =>
            string.IsNullOrEmpty(text) || text.IndexOf("[[", System.StringComparison.Ordinal) < 0
                ? text
                : text.Replace(SourceToken, SourceWord).Replace(TargetToken, TargetWord);

        /// <summary>Whether art pixel (x, y) of the source tile is a stripe: bands down to the right.</summary>
        public static bool StripeAt(int x, int y) => ((x + y) % StripePeriod + StripePeriod) % StripePeriod < StripeWidth;
    }

    /// <summary>
    /// Every token player text can hold: key names (<see cref="KeyNames"/>) and the tiles' colour
    /// words (<see cref="ColourMarks"/>). Text built once and kept (the guide's steps, the build
    /// menu's lines, the playtest questions) is passed through <see cref="Fill"/> when read, and
    /// a drawn copy redraws when <see cref="Version"/> changes.
    /// </summary>
    public static class TextTokens
    {
        public static string Fill(string text) => ColourMarks.Fill(KeyNames.Fill(text));

        /// <summary>Changes whenever a token's text can have changed.</summary>
        public static int Version => KeyNames.Version + ColourMarks.Version;
    }
}
