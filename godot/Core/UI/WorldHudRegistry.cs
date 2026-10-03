using System.Collections.Generic;
using GolemFactory.Compat;

namespace GolemFactory.UI
{
    /// <summary>
    /// Collects every world-space label's anchor and hands each one back a position that is not
    /// on top of its neighbours, resolved once per frame by <see cref="WorldHudLayout"/>.
    ///
    /// <para>
    /// A registry rather than each badge looking around itself, for the same reason the layout
    /// is single-pass: a badge that checked its neighbours would be reading positions those
    /// neighbours had not adjusted yet, so the answer would depend on script update order and
    /// shift as objects were created and destroyed. One solver, one pass, one answer.
    /// </para>
    ///
    /// <para>
    /// <b>Answers from last frame's crowd, deliberately.</b> The alternative is a contract that
    /// every label registers before any label draws -- an ordering dependency between components
    /// that do not know about each other, and exactly the class of bug this codebase keeps
    /// finding in <c>Awake</c>/<c>Start</c> order. A label that has not moved reads identically
    /// either way, and one that has moved a whole cell in a frame is not one anybody was reading.
    /// </para>
    ///
    /// <para>
    /// A plain static because there is one screen, exactly as <c>EventBus</c> is -- which is also
    /// why <see cref="Clear"/> exists: a static outlives a scene load and a test, and stale
    /// anchors from a world that no longer exists would push live labels around. It also
    /// self-heals within one frame, because submissions are per-frame rather than durable.
    /// </para>
    /// </summary>
    public static class WorldHudRegistry
    {
        // What has been submitted since the last solve, and where the last solve put everyone.
        private static readonly List<WorldHudRequest> Pending = new List<WorldHudRequest>();
        private static readonly Dictionary<string, Vector3> Resolved = new Dictionary<string, Vector3>();

        private static int _lastSolvedFrame = -1;

        public static int PendingCount => Pending.Count;

        public static int ResolvedCount => Resolved.Count;

        /// <summary>
        /// Submits <paramref name="anchor"/> for <paramref name="ownerId"/> and returns where
        /// that label should sit. An owner with no neighbours gets its anchor back unchanged, so
        /// the common case -- one golem stalled by itself -- is untouched.
        /// </summary>
        public static Vector3 Resolve(Vector3 anchor, string ownerId)
        {
            // DELIBERATELY DOES NOT SOLVE. A first-caller-solves rule would make the answer
            // depend on which label happened to register first -- the very script-order
            // dependence this class exists to remove. WorldHudSolver drives it from Update,
            // which runs before every view's LateUpdate by Unity's own phase order rather than
            // by anyone's execution-order setting.
            string key = ownerId ?? "";
            Pending.Add(new WorldHudRequest(anchor, key));

            return Resolved.TryGetValue(key, out Vector3 position) ? position : anchor;
        }

        /// <summary>
        /// Runs the layout over everything submitted since the previous solve. Public and
        /// frame-stamped so a test can drive it without a running player loop.
        /// </summary>
        public static void Solve(int frame)
        {
            if (frame == _lastSolvedFrame)
            {
                return;
            }

            _lastSolvedFrame = frame;
            Resolved.Clear();

            Vector3[] positions = WorldHudLayout.Resolve(Pending);
            for (int i = 0; i < Pending.Count; i++)
            {
                // Last submission wins for a duplicate id, which cannot happen through the
                // views (one badge per golem) and would otherwise be a silent tie.
                Resolved[Pending[i].OwnerId] = positions[i];
            }

            Pending.Clear();
        }

        public static void Clear()
        {
            Pending.Clear();
            Resolved.Clear();
            _lastSolvedFrame = -1;
        }
    }
}
