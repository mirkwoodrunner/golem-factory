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
    //
    // Lifetime warning that cost a real bug: reparenting to the DragLayer takes this
    // GameObject *out* of everything RebuildUI() knows how to clear. A failed drag (drop
    // over the mahogany background, the chassis rack, the title bar) used to leave the
    // card alive under DragLayer forever -- RebuildUI cleared vaultContent and each drop
    // zone but never DragLayer, and HandleDrop's vault-origin/no-zone branch was a
    // comment-only no-op. Orphans survived Close()/Open() and RetargetGolem() and
    // accumulated for the whole session. There are now two independent guards: RebuildUI
    // clears DragLayer first, and OnEndDrag below destroys anything still sitting there.
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

        private Vector2 _grabOffset;
        private bool _dragging;
        private GameObject _ghost;
        private Vector3 _restScale = Vector3.one;
        private Quaternion _restRotation = Quaternion.identity;

        public void Init(WorkbenchController controller, Transform dragLayer)
        {
            _controller = controller;
            _dragLayer = dragLayer;
            _rect = GetComponent<RectTransform>();
            _image = GetComponent<Image>();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_rect == null)
            {
                _rect = GetComponent<RectTransform>();
            }

            _dragging = true;
            // Preserve where inside the card the player actually grabbed it. Without this
            // the 0.5/0.5 pivot snapped the card's centre to the cursor on the first drag
            // frame, so grabbing a card by its edge made it jump.
            _grabOffset = WorkbenchDragVisuals.ComputeGrabOffset(_rect.position, eventData.position);

            SpawnGhost();

            if (_dragLayer != null)
            {
                transform.SetParent(_dragLayer, worldPositionStays: true);
                transform.SetAsLastSibling();
            }

            if (_image != null)
            {
                // Let raycasts pass through to whatever's underneath (the drop zones),
                // not this card itself.
                _image.raycastTarget = false;
            }

            ApplyLift();
            if (_controller != null)
            {
                _controller.BeginCardDrag(this);
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            _rect.position = WorkbenchDragVisuals.ComputeDraggedPosition(eventData.position, _grabOffset);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            // Tear the drag presentation down *before* handing off to the controller:
            // HandleDrop runs RebuildUI, which destroys card GameObjects, and the ghost is
            // not a card (ClearCards would skip it and leave it behind in the slot).
            ClearLift();
            DestroyGhost();
            if (_controller != null)
            {
                _controller.EndCardDrag();
            }

            GameObject hit = eventData.pointerCurrentRaycast.gameObject;
            WorkbenchDropZone zone = hit != null ? hit.GetComponentInParent<WorkbenchDropZone>() : null;

            _dragging = false;
            if (_controller != null)
            {
                _controller.HandleDrop(this, zone);
            }

            // Second guard (see the class comment). RebuildUI has already cleared the
            // DragLayer by now on every normal path; this covers the case where the
            // controller went away mid-drag, so a lifted card can never outlive its drag.
            if (_dragLayer != null && transform.parent == _dragLayer)
            {
                Destroy(gameObject);
            }
        }

        // A ghost of the card is left in the source list so the VerticalLayoutGroup keeps
        // its hole open: yanking the real card out of the layout group made the whole
        // vault reflow upward the instant a drag started, which read as the list breaking.
        private void SpawnGhost()
        {
            Transform parent = transform.parent;
            if (parent == null)
            {
                return;
            }

            _ghost = new GameObject("CardGhost", typeof(RectTransform), typeof(Image), typeof(WorkbenchCardGhost));
            _ghost.transform.SetParent(parent, false);
            _ghost.transform.SetSiblingIndex(transform.GetSiblingIndex());

            var ghostRect = _ghost.GetComponent<RectTransform>();
            ghostRect.anchorMin = _rect.anchorMin;
            ghostRect.anchorMax = _rect.anchorMax;
            ghostRect.pivot = _rect.pivot;
            ghostRect.sizeDelta = _rect.sizeDelta;
            ghostRect.offsetMin = _rect.offsetMin;
            ghostRect.offsetMax = _rect.offsetMax;
            ghostRect.anchoredPosition = _rect.anchoredPosition;

            // Match the card's layout footprint exactly, or the ghost's hole would be a
            // different height than the card that left it.
            var sourceLayout = GetComponent<LayoutElement>();
            if (sourceLayout != null)
            {
                var ghostLayout = _ghost.AddComponent<LayoutElement>();
                ghostLayout.preferredHeight = sourceLayout.preferredHeight;
                ghostLayout.minHeight = sourceLayout.minHeight;
                ghostLayout.preferredWidth = sourceLayout.preferredWidth;
                ghostLayout.minWidth = sourceLayout.minWidth;
                ghostLayout.flexibleHeight = sourceLayout.flexibleHeight;
                ghostLayout.flexibleWidth = sourceLayout.flexibleWidth;
            }

            var ghostImage = _ghost.GetComponent<Image>();
            if (_image != null)
            {
                ghostImage.sprite = _image.sprite;
                ghostImage.type = _image.type;
                Color c = _image.color;
                ghostImage.color = new Color(c.r, c.g, c.b, WorkbenchDragVisuals.GhostAlpha);
            }
            else
            {
                ghostImage.color = new Color(1f, 1f, 1f, WorkbenchDragVisuals.GhostAlpha);
            }

            ghostImage.raycastTarget = false;
        }

        private void DestroyGhost()
        {
            if (_ghost != null)
            {
                Destroy(_ghost);
                _ghost = null;
            }
        }

        private void ApplyLift()
        {
            _restScale = transform.localScale;
            _restRotation = transform.localRotation;
            transform.localScale = _restScale * WorkbenchDragVisuals.LiftScale;
            transform.localRotation = Quaternion.Euler(0f, 0f, WorkbenchDragVisuals.TiltDegrees);

            var shadow = GetComponent<Shadow>();
            if (shadow == null)
            {
                shadow = gameObject.AddComponent<Shadow>();
            }
            shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
            shadow.effectDistance = new Vector2(WorkbenchDragVisuals.ShadowOffset, -WorkbenchDragVisuals.ShadowOffset);
            shadow.enabled = true;
        }

        private void ClearLift()
        {
            transform.localScale = _restScale;
            transform.localRotation = _restRotation;

            var shadow = GetComponent<Shadow>();
            if (shadow != null)
            {
                shadow.enabled = false;
            }
        }

        // A card can be destroyed *during* a drag -- RebuildUI tears down and recreates
        // every card from data, and nothing stops it running while the pointer is down.
        // OnEndDrag would then never fire, stranding this card's ghost in the vault and
        // leaving every socket stuck in its mid-drag highlight. The card owns the ghost,
        // so the card cleans it up when it dies.
        private void OnDestroy()
        {
            if (!_dragging)
            {
                return;
            }

            _dragging = false;
            DestroyGhost();
            if (_controller != null)
            {
                _controller.EndCardDrag();
            }
        }

        // True between OnBeginDrag and OnEndDrag; exposed so tests can assert the drag
        // actually ran rather than inferring it from side effects.
        public bool IsDragging => _dragging;
    }
}
