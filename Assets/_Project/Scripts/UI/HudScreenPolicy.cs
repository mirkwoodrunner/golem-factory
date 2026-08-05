namespace GolemFactory.UI
{
    /// <summary>
    /// The one place that decides which of the mutually-exclusive full screens may be open and
    /// whether the always-on world HUD (build menu, interaction prompt) is allowed to draw
    /// underneath them.
    /// <para>
    /// This exists because the overlap bug was structural, not cosmetic: three screens
    /// (Workbench, Management, Golem Construction) each force-closed *some* of the others,
    /// depending on which pass wired them, and the build menu lived on its own Canvas that
    /// knew about none of them. Encoding the rule once, as a pure function, is what makes it
    /// testable and what stops the next panel from re-opening the hole.
    /// </para>
    /// </summary>
    public static class HudScreenPolicy
    {
        /// <summary>
        /// The always-on world HUD hides whenever any full screen is up. A full screen dims
        /// the world behind it, so anything still drawing on top of that dim is by definition
        /// misplaced -- and on a separate Canvas it cannot be sorted correctly anyway.
        /// </summary>
        public static bool ShouldShowWorldHud(bool workbenchOpen, bool managementOpen, bool constructionOpen) =>
            !workbenchOpen && !managementOpen && !constructionOpen;

        /// <summary>
        /// True when more than one full screen claims to be open -- the exact state the wiring
        /// bug produced. Kept as a named predicate so a test can assert the invariant directly
        /// rather than restating the boolean algebra.
        /// </summary>
        public static bool HasOverlap(bool workbenchOpen, bool managementOpen, bool constructionOpen)
        {
            int open = 0;
            if (workbenchOpen) open++;
            if (managementOpen) open++;
            if (constructionOpen) open++;
            return open > 1;
        }
    }
}
