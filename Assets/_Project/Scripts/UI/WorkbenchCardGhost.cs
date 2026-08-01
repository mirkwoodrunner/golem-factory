using UnityEngine;

namespace GolemFactory.UI
{
    // Marker for the translucent layout placeholder WorkbenchCard leaves behind in the
    // vault (or a slot) while it is being dragged, so the VerticalLayoutGroup keeps the
    // hole open instead of reflowing the whole list upward mid-drag.
    //
    // Exists purely so WorkbenchController.RebuildUI can sweep up a ghost whose drag was
    // interrupted, the same way ClearCards sweeps up cards -- a ghost has no WorkbenchCard
    // component, so without this marker an interrupted drag would leave a permanent
    // translucent stripe in the slot it came from.
    public sealed class WorkbenchCardGhost : MonoBehaviour
    {
    }
}
