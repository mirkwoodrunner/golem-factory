Folder rules for full screens and UI layout. The root `CLAUDE.md` has the project overview, commands and settled rules.
Paths here are relative to `godot/` unless they say otherwise. The screens' Core side
(`ScreenCoordinator`, `CrateScreenReadout`) is in `Core/UI/`.

# Screens and the player's hands

**The player's hands are Core's `PlayerInteractor`** (`SandboxWorld.Interactor`). `PlayerNode`
only feeds it the position and the keys. A full screen joins the `ModalScreens` group, implements
`IClosableScreen` and registers with `SandboxWorld.Screens`; while one is open the player stays
still, the HUD hides, and the world prompt hides (the interactor asks `Screens.AnyOpen`). A new
screen gets that for free. A screen closes on E in its own `_UnhandledInput`, so `PlayerNode`
ignores E for the frame after a screen was open; otherwise the closing press reached the world.

**Full screens report to `SandboxWorld.Screens`** (`ScreenCoordinator`). Implement
`IClosableScreen`, `Register` in `_Ready`, and call `Screens.Opening(this)` in `Open`; every
other screen closes. Don't hand-close siblings. That's the bug class the coordinator replaced.

**Screens are laid out with `Scripts/UI/Ugui.cs`**, in the numbers of the Unity prefabs they
were transcribed from (those prefabs are in git history at `unity-final`). `Ugui.Place` takes
a RectTransform's anchorMin/anchorMax/anchoredPosition/sizeDelta/pivot, with Unity's anchors
(y up). `Ugui.Image` gives a 9-sliced, tinted plate whose tint stays off its children.
`Ugui.Text` is a single-line TMP-style label. To change a screen, work from its existing
numbers; don't eyeball a layout. **A control has no size until the frame
after it's built**, so a scenario must not click something in the same step that opened it.

**A screen by pad** (`docs/steam-deck-design.md` D6) uses `PadFocus`: list the stops each frame
(kind, index, control, and the scrolling list a stop is in) and act on `Current`. Not Godot's
focus, which a rebuild from data drops. While the screen is open, mark every joypad event handled
in its `_Input` and read the pad by polling, or LB steps the build menu and the d-pad zooms the
camera behind it. The A, E or Y that opened it is still "just pressed" in the frame it opens, so
its buttons wait a frame. The pad's A presses the stop, so it must not also close the screen; B
closes. See `WorkbenchScreen.Pad.cs`, `ConstructionPanelNode.Pad.cs`, `CrateScreen.Pad.cs`.

The Pack and Crate screens' rules (`SandboxWorld.Inventory`) are in
`godot/Core/Economy/CLAUDE.md`; hover's card is in `godot/Core/Golems/CLAUDE.md`.
