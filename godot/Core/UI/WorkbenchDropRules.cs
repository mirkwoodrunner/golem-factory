namespace GolemFactory.UI
{
    // Pure, engine-free rules for "may this card go in this socket, and should this socket
    // be on screen at all" -- same idiom as GridCoordinateConverter / YSortUtility /
    // WorkbenchLeverMotion, so the decisions are unit-testable without a scene.
    //
    // Extracted because three call sites need the *same* answer and used to compute it
    // three different ways: WorkbenchController.HandleDrop (may I commit this drop),
    // WorkbenchController.RebuildUI (should this slot render), and the mid-drag socket
    // highlighting added for the drag-feedback pass (which sockets glow while a card is
    // held). Highlighting that disagreed with HandleDrop would be worse than none at all.
    public static class WorkbenchDropRules
    {
        // A slot the currently-fitted chassis actually provides.
        public static bool SlotWithinChassis(int appendageIndex, int chassisMaxAppendageSlots) =>
            appendageIndex >= 0 && appendageIndex < chassisMaxAppendageSlots;

        // Whether a drop of this card onto this zone should be committed to the draft.
        // Logic-core cards only fit the logic-core socket; appendage cards only fit
        // appendage sockets, and only ones the chassis provides.
        public static bool AcceptsCard(
            DropZoneKind zoneKind, int appendageIndex, bool cardIsLogicCore, int chassisMaxAppendageSlots)
        {
            if (zoneKind == DropZoneKind.LogicCore)
            {
                return cardIsLogicCore;
            }

            return !cardIsLogicCore && SlotWithinChassis(appendageIndex, chassisMaxAppendageSlots);
        }

        // Deliberately *not* the same rule as AcceptsCard: a slot that holds a card must
        // stay on screen even when it sits beyond the current chassis's capacity, or the
        // draft would contain appendages the viewport silently refuses to draw (the
        // "SLOTS 1/0 with an empty viewport" incoherence). Rendering it lets the player
        // see -- and drag out -- what is actually in the draft, while AcceptsCard still
        // refuses to put anything *new* there.
        public static bool SlotVisible(int appendageIndex, int chassisMaxAppendageSlots, bool slotOccupied) =>
            slotOccupied || SlotWithinChassis(appendageIndex, chassisMaxAppendageSlots);
    }
}
