namespace GolemFactory.Menus
{
    /// <summary>What a scene load is for (docs/menus-design.md §6).</summary>
    public enum LaunchKind
    {
        /// <summary>The title screen over the showcase workshop.</summary>
        Title,

        /// <summary>A fresh game: the guide's first step, the starting pack.</summary>
        NewGame,

        /// <summary>A fresh world with a save loaded into it.</summary>
        Continue,
    }

    /// <summary>
    /// Carries the player's choice across a scene reload. The title runs on a throwaway world, so
    /// starting a game reloads the scene: <see cref="Request"/> names what the next load is for,
    /// and the new scene asks <see cref="Take"/> once, before anything is built. Static because it
    /// must outlive the scene; it holds a choice, never world state.
    ///
    /// <para>
    /// The game opens on the title. A scenario or demo run opens on a new game, as it always has,
    /// unless it asks for the title itself (the <c>title</c> scenario does).
    /// </para>
    /// </summary>
    public static class GameLaunch
    {
        private static LaunchKind? _next;
        private static string _nextPath;
        private static string _nextNotice;
        private static bool _nextCreative;
        private static int _nextChapter = 1;

        /// <summary>What the scene now running was loaded for.</summary>
        public static LaunchKind Current { get; private set; } = LaunchKind.Title;

        /// <summary>The save a <see cref="LaunchKind.Continue"/> loads; null otherwise.</summary>
        public static string SavePath { get; private set; }

        /// <summary>
        /// A line for the title to show: why the last Continue came back to it ("That save
        /// couldn't be read."). Null when there is nothing to say.
        /// </summary>
        public static string Notice { get; private set; }

        /// <summary>
        /// Whether the game now running is a Creative world (docs/menus-design.md §9): asked for
        /// with a new game or a Continue. Never on the title, whose showcase is the guide's.
        /// </summary>
        public static bool Creative { get; private set; }

        /// <summary>
        /// The chapter a new game starts at (<see cref="ChapterStart"/>): 1, the guide's first
        /// step, unless playtest mode's "Start at chapter" or <c>--chapter</c> asked for a later one.
        /// </summary>
        public static int StartChapter { get; private set; } = 1;

        /// <summary>Whether a request is waiting for the next load.</summary>
        public static bool HasRequest => _next != null;

        /// <summary>
        /// Names what the next scene load is for. <paramref name="savePath"/> is for Continue;
        /// <paramref name="creative"/> for a new game or a Continue, never the title;
        /// <paramref name="startChapter"/> for a campaign's new game only.
        /// </summary>
        public static void Request(LaunchKind kind, string savePath = null, string notice = null, bool creative = false,
            int startChapter = 1)
        {
            _next = kind;
            _nextPath = kind == LaunchKind.Continue ? savePath : null;
            _nextNotice = notice;
            _nextCreative = creative && kind != LaunchKind.Title;
            _nextChapter = kind == LaunchKind.NewGame && !_nextCreative && startChapter > 1 ? ChapterStart.Clamp(startChapter) : 1;
        }

        /// <summary>
        /// Called once per scene load, first thing: the requested launch, or on the first load
        /// the title (a new game under a scenario or demo). The request is used up.
        /// </summary>
        public static LaunchKind Take(bool underScenario)
        {
            Current = _next ?? (underScenario ? LaunchKind.NewGame : LaunchKind.Title);
            SavePath = Current == LaunchKind.Continue ? _nextPath : null;
            Notice = _nextNotice;
            Creative = _nextCreative;
            StartChapter = _nextChapter;
            _next = null;
            _nextPath = null;
            _nextNotice = null;
            _nextCreative = false;
            _nextChapter = 1;
            return Current;
        }

        /// <summary>For tests: forget everything.</summary>
        public static void Reset()
        {
            _next = null;
            _nextPath = null;
            _nextNotice = null;
            _nextCreative = false;
            _nextChapter = 1;
            StartChapter = 1;
            Current = LaunchKind.Title;
            SavePath = null;
            Notice = null;
            Creative = false;
        }
    }
}
