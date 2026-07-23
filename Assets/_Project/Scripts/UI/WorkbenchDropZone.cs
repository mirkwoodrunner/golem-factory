using UnityEngine;

namespace GolemFactory.UI
{
    public enum DropZoneKind
    {
        LogicCore,
        Appendage
    }

    // Marks a slot GameObject in the Workbench blueprint viewport as a valid drop
    // target for WorkbenchCard drags. Needs a Graphic (e.g. Image) with
    // raycastTarget = true on the same GameObject (or a child) to actually be
    // hit-testable by the UGUI raycaster.
    public sealed class WorkbenchDropZone : MonoBehaviour
    {
        [SerializeField] private DropZoneKind kind;
        [SerializeField] private int appendageIndex = -1;

        public DropZoneKind Kind => kind;
        public int AppendageIndex => appendageIndex;

        public void Configure(DropZoneKind zoneKind, int index)
        {
            kind = zoneKind;
            appendageIndex = index;
        }
    }
}
