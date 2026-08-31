using UnityEngine;

namespace GolemFactory.UI
{
    /// <summary>
    /// Ticks <see cref="WorldHudRegistry"/> once per frame.
    ///
    /// <para>
    /// In <c>Update</c>, not <c>LateUpdate</c>, and that is the whole point of it existing: the
    /// world-space labels position themselves in LateUpdate, so solving in Update means every
    /// label reads a layout computed after the last of them registered and before any of them
    /// draws -- an ordering guaranteed by Unity's phases rather than by a script execution order
    /// somebody has to remember to set.
    /// </para>
    ///
    /// <para>
    /// Absent from a scene, nothing breaks: labels simply sit on their raw anchors, which is
    /// where they sat before this existed.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldHudSolver : MonoBehaviour
    {
        private void Update() => WorldHudRegistry.Solve(Time.frameCount);

        // A registry that outlives the scene would push live labels around with anchors from a
        // world that no longer exists.
        private void OnDestroy() => WorldHudRegistry.Clear();
    }
}
