using Godot;

namespace GolemFactory.Nodes
{
    /// <summary>
    /// Whether the player is typing into a text field (G10: the playtest questions take a note).
    /// Key EVENTS are safe on their own -- a focused LineEdit consumes them before any
    /// _UnhandledInput sees them -- but anything that POLLS the keyboard (the player's WASD and
    /// held E) would walk and crank while the player typed. Those polls ask this first.
    /// </summary>
    public static class TextEntry
    {
        public static bool IsTyping(Viewport viewport) =>
            viewport?.GuiGetFocusOwner() is LineEdit or TextEdit;
    }
}
