using UnityEngine;
using UnityEngine.UI;

namespace GolemFactory.UI
{
    public enum DropZoneKind
    {
        LogicCore,
        Appendage
    }

    // How a socket should read while a card is being dragged over the Workbench.
    public enum DropZoneHighlight
    {
        // Nothing is being dragged: the socket's authored color.
        Neutral,
        // The held card would be accepted here.
        Valid,
        // The held card would be rejected here (wrong card kind, or a slot beyond the
        // fitted chassis's capacity).
        Invalid
    }

    // Marks a slot GameObject in the Workbench blueprint viewport as a valid drop
    // target for WorkbenchCard drags. Needs a Graphic (e.g. Image) with
    // raycastTarget = true on the same GameObject (or a child) to actually be
    // hit-testable by the UGUI raycaster.
    //
    // Also owns its own mid-drag highlight, because "which socket will take this card"
    // was previously invisible: all five appendage sockets looked identical while an
    // appendage was held, and nothing signalled that the logic-core socket would reject
    // it. WorkbenchController decides *which* state each socket is in (via the shared
    // WorkbenchDropRules); this component only applies it.
    public sealed class WorkbenchDropZone : MonoBehaviour
    {
        [SerializeField] private DropZoneKind kind;
        [SerializeField] private int appendageIndex = -1;

        // The inner "Socket" plate, if this zone has one -- that's the part that reads as
        // the physical receptacle, so it's what gets tinted. Falls back to this
        // GameObject's own Graphic; null-tolerant either way, so the zones built bare in
        // tests keep working.
        private Graphic _socketGraphic;
        private Color _restColor;

        // The slot row *behind* the socket. Tinted too, at reduced strength, because a
        // socket that already holds a card is completely covered by it -- highlighting
        // only the socket left an occupied slot (in particular the trigger socket, which
        // is what has to visibly reject an appendage) looking totally unchanged mid-drag.
        private Graphic _rowGraphic;
        private Color _rowRestColor;
        private const float RowTintStrength = 0.55f;

        private bool _cached;

        public DropZoneKind Kind => kind;
        public int AppendageIndex => appendageIndex;
        public DropZoneHighlight Highlight { get; private set; } = DropZoneHighlight.Neutral;

        public void Configure(DropZoneKind zoneKind, int index)
        {
            kind = zoneKind;
            appendageIndex = index;
        }

        private void Awake() => CacheSocket();

        private void CacheSocket()
        {
            if (_cached)
            {
                return;
            }

            _cached = true;
            Transform socket = transform.Find("Socket");
            Graphic own = GetComponent<Graphic>();
            _socketGraphic = socket != null ? socket.GetComponent<Graphic>() : own;
            if (_socketGraphic != null)
            {
                _restColor = _socketGraphic.color;
            }

            _rowGraphic = own != _socketGraphic ? own : null;
            if (_rowGraphic != null)
            {
                _rowRestColor = _rowGraphic.color;
            }
        }

        public void SetHighlight(DropZoneHighlight highlight)
        {
            CacheSocket();
            Highlight = highlight;

            if (_socketGraphic != null)
            {
                _socketGraphic.color = TintFor(highlight, _restColor, 1f);
            }
            if (_rowGraphic != null)
            {
                _rowGraphic.color = TintFor(highlight, _rowRestColor, RowTintStrength);
            }
        }

        private static Color TintFor(DropZoneHighlight highlight, Color restColor, float strength)
        {
            switch (highlight)
            {
                case DropZoneHighlight.Valid:
                    return Color.Lerp(restColor, WorkbenchDragVisuals.ValidSocketTint, strength);
                case DropZoneHighlight.Invalid:
                    return Color.Lerp(restColor, WorkbenchDragVisuals.InvalidSocketTint, strength);
                default:
                    return restColor;
            }
        }
    }
}
