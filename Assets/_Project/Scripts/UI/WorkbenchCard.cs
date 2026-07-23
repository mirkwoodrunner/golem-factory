using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using GolemFactory.PunchCards;

namespace GolemFactory.UI
{
    // A draggable card: teal for a LogicCoreDefinition, copper for an
    // AppendageActionDefinition (per the design doc's color coding). Exactly one of
    // LogicCore/Appendage is set. Drag mechanics only move this GameObject to follow the
    // pointer and report the drop target to WorkbenchController -- all actual state
    // changes happen in WorkbenchController.HandleDrop against the draft data, and
    // WorkbenchController.RebuildUI() destroys/recreates every card GameObject from that
    // draft afterward. That data-driven approach (rather than choreographing GameObject
    // reparenting per-drag) means this component never needs to know whether it came
    // from the vault or a slot beyond reporting SourceAppendageIndex/IsVaultOrigin.
    public sealed class WorkbenchCard : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public LogicCoreDefinition LogicCore;
        public AppendageActionDefinition Appendage;
        public bool IsVaultOrigin;
        public int SourceAppendageIndex = -1;

        private WorkbenchController _controller;
        private RectTransform _rect;
        private Image _image;
        private Transform _dragLayer;

        public void Init(WorkbenchController controller, Transform dragLayer)
        {
            _controller = controller;
            _dragLayer = dragLayer;
            _rect = GetComponent<RectTransform>();
            _image = GetComponent<Image>();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            transform.SetParent(_dragLayer, worldPositionStays: true);
            transform.SetAsLastSibling();
            if (_image != null)
            {
                // Let raycasts pass through to whatever's underneath (the drop zones),
                // not this card itself.
                _image.raycastTarget = false;
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            _rect.position = eventData.position;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            GameObject hit = eventData.pointerCurrentRaycast.gameObject;
            WorkbenchDropZone zone = hit != null ? hit.GetComponentInParent<WorkbenchDropZone>() : null;

            _controller.HandleDrop(this, zone);
        }
    }
}
