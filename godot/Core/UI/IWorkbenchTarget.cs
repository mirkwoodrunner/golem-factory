using GolemFactory.Golems;

namespace GolemFactory.UI
{
    /// <summary>
    /// The one thing a construction station needs from the Workbench: to point it at a golem.
    ///
    /// <para>
    /// In Unity the station held the concrete <c>WorkbenchController</c> MonoBehaviour. Core
    /// cannot, and needs only these two members, so the station depends on this instead. The
    /// Workbench (milestone G7) implements it; a station built before the Workbench exists, or
    /// in a test, simply has none and builds golems nobody retargets onto.
    /// </para>
    /// </summary>
    public interface IWorkbenchTarget
    {
        /// <summary>The golem the Workbench is currently programming, or null.</summary>
        GolemEntity TargetGolem { get; }

        /// <summary>Points the Workbench at <paramref name="golem"/>; null clears it.</summary>
        void RetargetGolem(GolemEntity golem);
    }
}
