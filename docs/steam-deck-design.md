# Golem Factory: running on the Steam Deck

**Status (2026-11-10):** D1's export is built (§7's export log); the Deck's first launch is the
owner's. D4, the gamepad's model, is built, with D5.1's build cursor pulled in so a pad can place
(§7's input audit and "D4 as built"). D5.2, the hover card at the pad's cursor, is built (§7's
"D5.2 as built"). D6's screens are built: the Workbench (D6.W, decided and built), the station's panel, the Crate
and Pack screens, the playtest card, the Management screen with all its tabs, the title with its
Settings and Load, the cart panel and the City Office's notice. Still pointer-only: the Overview
map's hover card and its click on a golem. D6's exit, the playtest script played with no mouse or
keyboard, is the owner's to try. D7 is built: text names the pad's buttons by the Deck's labels
(§ D7's "As built"). D8's parts that don't need the Deck are done: the clock at any frame rate,
the unfocused frame cap, the frame watch, two hitches fixed and ReadyToRun on export, and a
one-command perf run for the Deck. The Deck's own numbers, and D9 onward, wait for the Deck.

The goal is to run the game on Steam Deck hardware, played by its owner. **It is not a Steam store release**
(roadmap decision 14: release isn't planned). No Steamworks, no store page, no depots, no Steam Deck
Verified review. Anything that only a store release needs is out of scope here and listed in §9.

Roadmap decision 10 already set the direction: *mouse and keyboard first, with input built so a controller can
be added later.* This doc is "later".

## 0. Invariants

Any Steam Deck work keeps these.

1. **Core stays engine-free and unchanged in its rules.** Gamepad support is a Node-layer concern. No game rule
   changes for a controller: the same placements, the same costs, the same tick. A rule that exists only for
   one input device is a bug.
2. **The keyboard and mouse path stays whole.** The Windows desktop game keeps working with no gamepad
   connected, and nothing is gated on detecting a Deck.
3. **Input actions are registered in code** (`PlayerNode.RegisterInputActions`), not in `project.godot`.
   Controller bindings are added there too, through the same `Bind` helper.
4. **Anything that polls input still checks `TextEntry.IsTyping`.**
5. **Player text never prints an id and never names a key or button that is not the one in use.** A hint that
   says "press [E]" must say the right thing when a pad is the last device used (§5.5).
6. **Tests and scenarios never touch the real save or playtest report** (`godot/Core/Tutorial/CLAUDE.md`).
   The Deck's save lives in its own user folder; nothing here reads the desktop's.
7. **The game stays deterministic.** Nothing on the Deck path changes the tick clock or `--fixed-fps` runs.

## 1. What the Deck is

| | |
|---|---|
| OS | SteamOS 3 (Arch Linux). Game Mode is a gamescope session; Desktop Mode is KDE Plasma. |
| CPU / GPU | AMD Zen 2 APU, 4 cores / 8 threads, RDNA 2 graphics, Vulkan via RADV. |
| Screen | 7", **1280×800** (16:10), touch. The OLED model is 1280×800 as well, at 90 Hz. |
| Input | Two sticks, two trackpads, d-pad, ABXY, four triggers/bumpers, four back grips, gyro, touch screen. |
| Power | The game should hold its frame rate on battery without the fan working hard. |

The project today: Godot 4.7.2 **mono** (C#), `net10.0`, GL Compatibility renderer (already the
cross-platform safe choice), a 1280×720 viewport, 2D pixel art, keyboard and mouse input only.

## 2. Decisions to make first

These are the user's calls. Phase D0 asks them; the recommendation is the first option.

| # | Question | Options |
|---|---|---|
| D1 | How far does Deck support go? | **(a) Steam Input template only**: no game changes beyond the export. (b) Native gamepad support, so the Deck plays without a mouse. (c) (a) first, then (b) when the Workbench and Pack screens are final. |
| D2 | Which mode is the target? | **Game Mode** (launched from the library as a non-Steam game, controller only) or Desktop Mode (trackpad as mouse, on-screen keyboard). Recommendation: Game Mode, with Desktop Mode working as a fallback. |
| D3 | Viewport | Keep 1280×720 and letterbox on 16:10, or move to a 16:10-friendly base such as 1280×800 and make the UI flex. |
| D4 | Does the gamepad work on Windows too? | Yes by default: a pad is a pad. Recommended, since it is the only way to test without a Deck. |
| D5 | Touch screen | Ignore it, or map taps to clicks (Godot can emulate the mouse from touch). Recommendation: emulate the mouse, support nothing more. |

Recommendation overall is **(c)**: do Phase D1 and D2 (an export that runs, driven by a Steam Input template)
as soon as the Deck is in hand, because it costs little and shows the real problems. Native pad support (D3
onward) waits until the screens it must drive (Workbench, Pack, Crate, label picker) have stopped changing.

## 3. Phases

Each phase ends with something runnable and a recorded result. Phases D0 to D2 need no gameplay code.

### D0. Decide and prepare (half a day)

- Answer D1 to D5 in §8 and record them.
- Get the Deck into a state to receive builds: Desktop Mode, SSH enabled (`sudo systemctl enable --now sshd`,
  after setting a password with `passwd`), the owner's machine on the same network.
- Confirm the SteamOS version and that `dotnet` is not needed on the Deck (the export self-contains the runtime).

### D1. A Linux export that launches (1 day, most of it troubleshooting)

1. Install the **Godot 4.7.2 mono export templates** (Editor → Manage Export Templates, or the `.tpz` from the
   release). They must match 4.7.2 exactly.
2. Create `godot/export_presets.cfg` with a **Linux/X11** preset, x86_64.
   - Export the **mono** build's output: the preset produces the executable, a `.pck` and a
     `data_GolemFactory.Godot_linuxbsd_x86_64/` folder holding the .NET assemblies and runtime.
   - Embed the PCK (`binary_format/embed_pck`) if a single file is wanted; otherwise ship the folder.
   - Exclude `Core.Tests/**` and anything under `Tools/` from the export filter.
   - Commit `export_presets.cfg`. It holds no secrets for a Linux preset; never commit signing keys or the
     `.godot/export_credentials.cfg`.
3. Add a script, `godot/Tools/Deck/export-linux.ps1`, that runs
   `Godot_..._console.exe --headless --path godot --export-release "Linux Deck" <out>/GolemFactory.x86_64`
   so the export is one command. It lives beside the other tooling, not in the game.
4. **The .NET risk.** The project targets `net10.0` and GodotSharp 4.7.2 is built for net8.0. A Windows build
   works because the net10 runtime is installed. A Linux export must bundle a runtime (self-contained). Check:
   - The export includes `libcoreclr.so` and the net10 shared framework, not just net8.
   - The `nuget.config` that points at the Godot install's bundled packages also supplies the Linux runtime
     pack, or the export fails to restore it. If it doesn't, the Linux runtime pack must be fetched once from
     NuGet and cached. Record which.
   - If a self-contained net10 export fails, the fallback is retargeting to `net8.0`, which GodotSharp is built
     for. A net8 project can't reference a net10 library, so **Core would have to move to net8 too** (its tests
     can stay on net10). Treat that as a last resort and a change to the toolchain section of `CLAUDE.md`.
5. Copy the output to the Deck: `scp -r out/ deck@steamdeck:~/Games/GolemFactory/`, then `chmod +x`.
6. Run it from a Desktop Mode terminal first, so errors print. Fix until the Sandbox loads and a golem walks a
   belt.

**Exit:** the game reaches the Sandbox on the Deck in Desktop Mode, played with the trackpad and the Deck's
keyboard layout. Note any missing glyph, black texture or crash in §7's log.

### D2. Game Mode through Steam Input (half a day)

1. In Desktop Mode, Steam → Add a Non-Steam Game → the exported binary. Name it "Golem Factory".
2. Set launch options if needed (`--fixed-fps` is not wanted in play; nothing is needed by default).
3. Build a **Steam Input controller template** in Steam's configurator:
   - Left stick → W/A/S/D (or arrows), the right trackpad → mouse, right trigger → left click, left trigger →
     right click, A → E (interact), X → G (carry), Y → I (inventory), B → Escape, bumpers → the zoom keys
     Phase 6 added, d-pad → the hotbar or build-menu keys, back grips → the most-used shortcuts.
   - Export it as a community template, then keep its file in the repo at `godot/Tools/Deck/steam-input/` so it
     survives a re-flash. (The file is a VDF text file.)
4. Play chapters 1 to 4 of the guide with it, using `testscript/phase-2-playtest.md`.

**Exit:** the first four guide chapters are completable in Game Mode. This is the **MVP**: if D1 is the only
thing ever done, the game is playable on the Deck.

Known weak spots of a pure template, which D3 onward fixes: no typing for depot labels without the on-screen
keyboard (Steam + X), hover tooltips need a moving pointer, and dragging cards on the Workbench by trackpad is
slow.

### D3. Display (1 day)

1. **Screen size.** The Deck is 16:10. Test each of the three stretch setups against the 1280×720 viewport:
   `canvas_items` with `keep` aspect (black bars top and bottom, 40 px each), `expand` (extra height shown), and
   a base viewport of 1280×800. Pick by looking at frames (`--write-movie` at 1280×800 with
   `--resolution 1280x800`), not by reading settings. Record the choice as D3.
2. **Fullscreen.** In Game Mode gamescope owns the window. Set `display/window/size/mode` fullscreen for the
   Deck export (or pass `--fullscreen` in the launch options) and confirm there's no title bar.
3. **Text size.** The Deck is held at about 40 cm, so the pixel font at 12 to 14 px is small. Check every
   screen in a movie frame: the Workbench, the Pack and Crate screens, the label picker, the guide, the
   build menu, hover tooltips. List what is unreadable. If it needs a UI scale, that is the Phase 7 *display and
   UI scale* setting; do not build a second one. Make the Phase 7 setting default to a larger scale on a
   device that reports a small, dense screen.
4. **Touch.** Per D5, enable `input_devices/pointing/emulate_mouse_from_touch` if wanted.

5. **The accessibility pass** (§10) runs on the same frames.

**Exit:** every screen is readable at 1280×800 on the Deck itself, judged by the owner, not by a frame alone.

### D4. Native gamepad: the model (2 days)

This phase is only to be done if D1 chose (b) or (c). It lays the foundation without touching screens.

1. **Audit every place the Node layer polls or handles input.** Search for `Input.IsActionPressed`,
   `Input.IsKeyPressed`, `_UnhandledInput`, `_Input`, `InputEventMouse*`, `GetGlobalMousePosition`,
   `GetLocalMousePosition`, `InputEventKey`, `Key.`, in `Scripts/`. Record every site in §7 with its screen
   and what it needs (a pointer, a key, text). That list is the real size of this work.
2. **Define actions for every verb**, not just movement: `ui_*` navigation (Godot's built-ins: `ui_up`,
   `ui_accept`, `ui_cancel`, `ui_focus_next`), `Interact`, `Carry`, `Inventory`, and new ones the game
   currently does with the mouse only: `BuildPlace`, `BuildCancel`, `BuildRotate`, `Demolish`, `CameraZoomIn`,
   `CameraZoomOut`, `OpenBuildMenu`, `OpenWorkbench`, `Pause`. Every verb gets a keyboard binding *and* a pad
   binding, through `RegisterInputActions`.
3. **Default pad bindings** (a proposal, to be tuned at the Deck):

   | Action | Pad |
   |---|---|
   | Move | left stick |
   | Move the build cursor | right stick |
   | Interact | A |
   | Carry / drop | X |
   | Inventory | Y |
   | Cancel / back | B |
   | Place | right trigger |
   | Demolish | left trigger |
   | Rotate | right bumper |
   | Build menu | left bumper |
   | Zoom | d-pad up/down |
   | Alert pan | d-pad right |
   | Pause / menu | Start |

4. **Stick dead zones** in the input map (0.2 to 0.3), and diagonal handling so a stick does not walk faster
   than the keys. The walker takes a vector already; check it normalises.
5. **An `InputDevice` tracker** in the Node layer: remembers whether the last input was keyboard/mouse or a
   pad, and raises a signal on a change. Core never sees it. It drives §5.5 prompts and whether the pointer
   is shown.
6. **A scenario**, `gamepad`: sends `InputEventJoypadMotion` and `InputEventJoypadButton` through
   `Input.ParseInputEvent` and checks that the player walks, interacts, opens the pack and places a belt
   without a key or mouse event. Register it in `ScenarioRunner.Scenarios` and document it in
   `Scripts/Scenarios/CLAUDE.md`.

**Exit:** the player walks and uses the world with a pad on Windows; the `gamepad` scenario passes headless.

### D5. Native gamepad: the world (2 to 3 days)

The mouse-driven parts of the world.

1. **Build cursor.** Today the cursor follows the mouse (`BuildCursorNode`). With a pad it should move on the
   grid by the right stick (snapping cell to cell with a repeat rate) or follow the player's facing, with the
   same highlight, ghost and validity colour. A drag-run of belts (`BuildDragRunTests` is the rule) becomes
   "hold place and move the cursor". The rule stays in Core; only how a cell is chosen differs.
2. **Selecting a golem or building** (for hover moods, the Workbench, the demolish tool): the cursor cell picks
   the thing in it. Hover text (`onboarding-feedback-design.md` §4) shows for the cell under the cursor when
   the last device is a pad.
3. **Camera.** Zoom levels and the alert pan are already actions (Phase 6); give them pad bindings.
4. **Interact prompts** (`Prompt`) show the right glyph (§5.5).

**Exit:** a full guide chapter 1 to 4 run with the pad alone, and the build-and-demolish checks in the
`build` scenario pass with pad events.

### D6. Native gamepad: the screens (3 to 5 days)

Screens are built in code through `Ugui` (`Scripts/UI/CLAUDE.md`, read it first). This is the largest phase.

1. **Focus.** Godot controls take focus; pads navigate with `ui_*` actions. For every screen, make focus
   deterministic: set `FocusMode` on each button, grab focus on open, return it on close, and wire
   `FocusNeighbor*` where the automatic order is wrong (grids especially). Never find a control by a name
   built from data (root `CLAUDE.md`): keep the lookups already used.
2. **Per screen**, each with a checklist in §7:
   - Build menu: grid of buildings, shoulder buttons for tabs.
   - **Workbench**: the highest risk. Cards are dragged today. A pad needs "pick up a card, move to a slot,
     drop", i.e. a selected-card state with the focus cursor as the hand. The result must still be the same
     drag the player makes (decision 4: *the player makes every drag*), only without a pointer.
   - Pack and Crate screens: focus a stack, A moves it to the other side (the screens already have a transfer
     rule), a trigger moves half.
   - Label picker: a list; the on-screen keyboard is not needed.
   - Guide and hint: dismissible with B, no pointer required.
   - Save/load, settings, title (Phase 7): pad navigable from the start.
3. **Text entry.** The only typed text is labels. Use the label picker's list where it can; for free text
   open the system on-screen keyboard (`DisplayServer.VirtualKeyboardShow`), and respect `TextEntry.IsTyping`.
4. **Hover-only information** (the `Straining` mood, tooltips): reachable by focus or a "details" button, not
   by hover alone.
5. **A scenario**, `gamepad-ui`: opens each screen and operates it by `ui_*` events, asserting that focus
   exists and moves and that the Workbench can build a program. Pair it with `--capture-dir` frames to look at
   the focus ring (the root `CLAUDE.md` rule: look at the game view, not only properties).

**Exit:** every screen reachable and usable with a pad; the playtest script runs with no mouse or keyboard.

#### D6.W: the Workbench by pad (decided 2026-11-09)

**The owner's answers:** "A sends it to the next socket" (not the carry proposed below), the
shoulders for a card's dials, and focus follows the hint. As decided, it replaces items 2 and 6:

- **A on a vault card sends it** to the first socket that takes it: a logic core to TRIGGER
  (replacing the one there, as a drop does), an appendage to the first empty step. It's one
  press, but still the mouse's three verbs (`BeginCardDrag`, `HandleDrop` at that socket,
  `EndCardDrag`), so the rules, refusals and landing animation are the mouse's. With every step
  full, nothing moves, and the status says to remove a card first.
- **X on a socketed card removes it** (`HandleDrop(card, null)`, as dragging it out does). The
  steps below it keep their places. To change the order, remove cards and send them again.
- **Focus follows the hint**: it opens on the hinted card, and after a send moves to the next one.
- B closes the screen, as today. Items 1, 3, 4, 5 and 7 stand as proposed, except that with no
  carry, nothing limits focus.

**As built, 2026-11-09** (branch `deck/screens`, `Scripts/UI/WorkbenchScreen.Pad.cs`):
- **The focus is the screen's own, not Godot's.** The cards are rebuilt whenever the session's
  `Version` moves, which would drop Godot's focus each time. A stop is kept by what it is (the
  third vault card, STEP 2, the lever), so it survives a rebuild. The ring moves to the nearest
  stop on screen in the direction pressed, preferring one in line. Within the vault, up and down
  step through its list and scroll it. From outside, the ring enters only a vault card in the
  window.
- **The input:** the d-pad (`ui_*`) or the left stick, one step on the push, then repeating after
  0.3 s every 0.12 s. A, X, the shoulders and B are read by polling. While the screen is open,
  every pad event is marked handled, so LB doesn't also step the build menu and the d-pad doesn't
  zoom the camera behind it.
- **The A that opened the screen** (at a golem or the station) is still "just pressed" in the
  frame the screen opens, so the screen's buttons wait one frame.
- **The hint:** the ring follows it on opening, after a send and after a removal, **but not
  after a dial.** The first build moved the ring to the lever after RT, because the plan doesn't
  care about batch size, so the next LT missed the dial.
- **With the pad in use, the dials name the pad's buttons:** the picker reads `LB` / `RB` in
  place of `<` / `>`, and the stepper `LT` / `RT` in place of `-` / `+`. The screen rebuilds when
  the device changes.
- **The ring** is cream, solid and 3 px, set 7 px out from the stop, beside the hint's pulsing
  brass. It shows only while the pad is in use.
- **Checked by** `gamepad-ui` (joypad events only after the playtest card's click): open by A at
  the golem with nothing sent; three sends, the ring following the hint to the lever; LB and the
  d-pad heard by nothing behind the screen; RT and LT on STEP 1's dial; X and a send back; every
  step full; RB and LB on a Haul's good; A on the lever engages; B closes; a long vault walked to
  its last card, scrolled into view. Frames `pad-workbench-open`, `-lever`, `-dial`, `-haul`,
  `-scroll`. Core: `WorkbenchPadSendTests`.
- **Left for D7:** the guide's text still says "Drag the glowing card" and "Press E at the
  golem".

**The station, Crate and Pack screens by pad, as built 2026-11-09** (same branch):
- **One focus ring for every screen:** `Scripts/UI/PadFocus.cs`, taken out of the Workbench's
  first build. A screen lists its stops each frame, each with a kind, an index, its control,
  and the scrolling list it's in, with the list's columns for a grid. `PadFocus` keeps the ring
  on a stop that exists, moves it, scrolls its list and draws it. The screen decides what its
  buttons do. Each screen marks every pad event handled while it's open and reads the pad by
  polling. A screen's buttons wait one frame after it opens, because the press that opened it
  is still "just pressed".
- **The station's panel** (`ConstructionPanelNode.Pad.cs`): the ring opens on the first row the
  pack pays for. A builds the row, and the Workbench opens on the new golem without the same A
  sending a card. **A on a greyed row says what's short**, as the build would refuse. B closes.
  E on the keyboard still closes too; the pad's A doesn't, since it presses the row. Patent
  rows scroll in their list.
- **The Crate screen** (`CrateScreen.Pad.cs`): stops on the label button, the crate's rows (or
  the picker's options while it's open), Take all and the pack's rows. On a row, **A moves one
  across and X moves what fits** (the click and the Shift-click). This replaces item 2's "a
  trigger moves half": the screen has no half rule, and inventing one would be a second
  transfer rule. A on the label button opens the picker, with the ring on the crate's label;
  the d-pad walks the 8-column grid; the status line names the option under the ring, as the
  mouse's hover does; A sets it, and the ring returns to the label button. B closes the picker
  first, then the screen. Y closes the screen, as it opened it. The hint reads "A moves one ·
  X moves what fits".
- **The Pack screen:** nothing moves from it, so the ring only walks the rows, scrolling a long
  pack. B, Y or A closes it. With the pad in use, the hint says "press Y".
- **Checked by** `gamepad-ui`'s station, crate and pack steps: 22 of 23 break checks across the
  four screens and `PadFocus` were caught. The miss is the station panel's one-frame wait.
  `PlayerNode` handles the A that opens the panel before the panel's own `_Process` runs in
  that frame, so today the panel never sees that press as "just pressed". The wait is kept as
  a guard against a change of node order, which is what bit the Workbench (its break was
  caught). Frames `pad-station`, `pad-crate`, `pad-crate-picker`,
  `pad-pack`.

**The playtest card and the Management screen by pad, as built 2026-11-09** (same branch):
- **The playtest card isn't a full screen.** It sits over the world while the player plays, so
  the pad isn't the card's until the player asks. **The d-pad's left** (`card_answer`, the one
  pad button the world left free) takes the pad into the card, and with the pad in use the card
  says so ("d-pad left to answer"). The ring walks the answers, the note and Skip. A picks an
  answer, and the pad goes back to the world. A on the note gives it focus for typing; the
  Deck's keyboard is Steam's (Steam + X), since `VirtualKeyboardShow` does nothing on desktop
  Linux. B leaves the note, then the card. While the card holds the pad,
  `InputDevices.OverlayHoldsPad` stops the world's polling (walking, the right stick's cursor,
  and the A that harvests, for the frame after too), and the card swallows the pad's events.
- **The Management screen** (`ManagementScreen.Pad.cs`): its tabs are built by several nodes,
  and most hold ordinary controls, so the ring is generic. Its stops are the tab buttons, Close,
  and every visible button and slider in the shown tab, found by walking the tab's tree and
  moved between by position on screen. **LB and RB change tab.** A presses a button, and a
  checkbox ticks: setting `ButtonPressed`, because emitting `Pressed` leaves a toggle unticked.
  **LT and RT step a slider**, as they turn the Workbench's dials. **The right stick scrolls the
  tab**, both ways: a long list, or the Ledger's wide chart, which the mouse drags. B, or Start
  as it opened it, closes.
- **Settings' key buttons are left out.** Capturing a key is the keyboard's job, and a pad press
  there would wait for a key the pad can't give.
- **`PadFocus` change:** a list laid out freely (columns 0) moves by position, and within one
  list the ring may move to an item scrolled out of view, scrolling it in. Scrolling to the ring's
  stop now works sideways too, for the Ledger.
- **Still pointer-only:** the Overview map's hover card and its click on a golem. Its alert
  rows are buttons, so the pad reaches the same camera glide. The Ledger's plaques are
  buttons, so A opens their readout.
- **Checked by** `gamepad-ui`'s card steps (taken, the stick held without walking, B, the note,
  answered with no harvest) and its menu steps (RB and LB with nothing behind hearing LB, a
  slider by LT and RT, a checkbox by A, no key button a stop, the Ledger panned, B). Frames
  `pad-card`, `pad-management-settings`.

**The title, the cart panel and the City Office by pad, as built 2026-11-10** (branch
`deck/screens-rest`), which finishes D6's screens:
- **The title** keeps its own selection (Core's `TitleMenu`, the slot board), which the keyboard
  already walked by its actions. The pad's events reached that path too, one menu step per stick
  motion event, and the d-pad also zoomed the showcase's camera. Now the title marks every pad
  event handled and walks the same selection by polling, with the ring's repeat
  (`PadFocus.DirectionRepeat`). Up and down choose, A picks, B goes back; on Load, left and
  right switch Campaign and Creative. Settings is Management's tab on the generic ring (key
  buttons left out), with Back. With the pad in use, the hints say "A to pick" and "A to load ·
  B to go back".
- **The cart panel and the City Office's notice** (`SupplyScreen.Pad.cs`,
  `ContractScreen.Pad.cs`) are lists of row buttons: the ring walks them, scrolling, and A
  presses the row, a greyed one saying why. B or Y closes. The keyboard's E still closes too;
  the pad's A presses the row.
- **`PadFocus` now holds what the screens share:** the direction repeat, finding a tree's
  buttons and sliders (`ControlsIn`), A's press-or-tick (`Press`), LT and RT on a slider
  (`StepSlider`), and the right stick's scroll (`ScrollWithStick`). Management uses them too.
- **Checked by** `gamepad-ui`'s cart, office and title steps. The cart steps: A at an empty spot
  opens the panel, the ring walks to the Coal cart, A adds it, B closes. The office steps: A at
  its door opens the notice with nothing taken by that A, A takes the contract, Y closes. The
  title steps: a reload to the title with a save in a slot; five stick motion events in one
  frame step the menu once, with the camera unmoved; Settings by the ring and its slider; Load,
  where the d-pad switches kinds and A loads the slot. Frames `pad-supply`, `pad-contract`,
  `pad-title-load`. 16 of 17 break checks were caught. The miss is the notice's one-frame wait,
  which is the station panel's case: the player handles the opening A before the notice runs,
  so the wait guards only against a change of node order.
- **Left for D7:** text that names a key: the guide, the prompts, the contract's "press E to
  sell it".

The proposal as written, kept for the record:

The Workbench has four regions: the blueprint viewport's sockets (TRIGGER, then STEP 1 to n) on the
left, the chassis rack in the middle, the card vault on the right (a scrolling list), and the bottom
bar's ENGAGE GEARS lever and PATENT, with CLOSE at the top right. A mouse player drags a card from
the vault onto a socket, between sockets, or off a socket to remove it. Each drag is three
`WorkbenchSession` verbs: `BeginCardDrag` (the sockets tint valid and invalid), `HandleDrop(card,
zone)` (or `null`: released over nothing), and `EndCardDrag`.

**The rule this keeps.** The pad calls the same three verbs in the same order, so every drop rule,
tint, refusal, batch-size carry and landing animation is the mouse's, unchanged. The player picks
every card and every socket. The hint still never fills a socket.

1. **Focus.** One focus stop per card, socket, chassis button, lever, PATENT and CLOSE, as a grid:
   - left and right (d-pad or left stick) move between the columns, keeping the row as near as
     the layout allows;
   - up and down move within a column, and the vault scrolls to keep the focused card in view
     (the scroll fix from the Hauler playtest);
   - down from a column's last stop reaches the bottom bar; up from the top row reaches CLOSE.

   A brass focus ring marks the stop, distinct from the hint's glow. The buttons draw no focus
   ring today: their "focus" style is their normal face.
2. **Carry.** A on a card picks it up (`BeginCardDrag`). The card stays ghosted where it was,
   as in a mouse drag, and a lifted copy rides beside the focused stop. Focus jumps to the first
   socket that accepts it and is empty, or the first that accepts it if none is empty. The player
   moves it anywhere with the d-pad. While a card is carried, only the sockets and the vault take
   focus.
   - **A on a socket drops it there** (`HandleDrop(card, zone)`). A refused drop says why, as the
     mouse's does.
   - **A on the vault** drops a socketed card "over nothing", which removes it from its socket,
     like dragging it out.
   - **B cancels the carry** (`EndCardDrag` alone). Nothing changes, and focus returns to the
     card. With no card carried, B closes the screen, as today.
3. **A socketed card's controls.** A socketed Haul has the good picker (`<` `>`); a socketed
   card with a batch size has the stepper (`-` `+`). With a socketed card focused, **LB and RB
   change the good, and LT and RT change the batch size**. The card keeps a single focus stop, and
   the shoulders show as small labels on it while the pad is in use.
4. **The chassis rack**: A on a chassis button picks it, as a click does.
5. **The lever and PATENT**: A on them, as a click does. ENGAGE GEARS stays a focus stop the
   player moves to, never a shortcut button, since pulling it rewrites a running golem.
6. **The guide's hint.** Opening the Workbench by pad puts focus on the hinted card, if there is
   one, else the first vault card. After a drop, focus moves to the next hinted card. Each press is
   still the player's; the hint only saves the walk across the screen.
7. **Mouse and pad together.** The mouse's drag is untouched. A mouse click during a pad carry
   cancels the carry first.

**Where it lives.** Choosing the first open socket that accepts the carried card is a rule, so it
goes in Core (on `WorkbenchSession`, beside `WorkbenchDropRules`) with unit tests. The focus grid,
the ring and the lifted copy are drawing, so they live in `WorkbenchScreen`.

**Checked by** the `gamepad-ui` scenario (D6.5), with joypad events only. It builds chapter 1's
program by pad (Always On, Extract, Push Output, ENGAGE GEARS), moves a card between sockets,
removes one to the vault, cancels a carry with B, and changes a Haul's good and batch size with
the shoulders. Frames capture the focus ring and a carry.

**Alternatives not proposed:**
- **"A sends the card to the next socket"**: one press per card, but the player never chooses
  the socket. Step order is the program, so that's the choice the screen exists for.
- **Focus inside a card** (a stop on `<`, `>`, `-` and `+`): it makes every socket row three or
  four stops wide, and left and right would mean two things.

### D7. Prompts and glyphs (1 day)

- Player text that names a key ("press [E]") reads from the **actual binding of the last device**. Today it is
  plain text; make it look up the action. A pad shows a button name ("A", "X"), with the Deck labels, not
  Xbox or PlayStation icons unless the art exists.
- The font rule applies: only printable Latin-1 plus the arrows and blocks in the project font. Names such as
  "A" or "RT" fit it. If glyph icons are drawn, they're sprites, not characters.
- The guide texts live in `Core/Tutorial` as data (a quoted price is the data's). A binding name placed in a
  step is a placeholder substituted at display time, so the guide's text stays device-neutral in data.
- Extend the `font-glyphs` scenario if any new character is used.

**As built, 2026-11-10** (branch `deck/screens-rest`, after D6's last screens):
- **`KeyNames` knows the device.** `KeyNames.Pad` is set by `InputDevices` whenever the last
  device changes. With the pad in use, the names are the pad's buttons by the Deck's own labels:
  - Use A, carry X, pack Y, turn R1;
  - move with the left stick;
  - the menu is Menu and cancel is B;
  - the guide is L3, and the playtest kit's skip is R3;
  - the bumpers are L1 R1 and the triggers L2 R2. The Workbench's dials read L1/R1 and L2/R2,
    not the LB/RB/LT/RT D6 first drew.

  Setting it bumps `KeyNames.Version`, so text drawn once (the guide's plate, the build menu's
  lines) redraws. Every prompt and hover card already read `KeyNames`, so "[E] Harvest" became
  "[A] Harvest" with no change at the call sites.
- **New tokens:** `[[menu]]` (Tab or Menu), `[[cancel]]` (Escape or B) and `[[guide]]` (F1 or L3).
  The guide's "Press Tab", "press Escape" and "F1 brings this guide back" use them. The keyboard's
  text is unchanged.
- **`PadPhrasing`** rewords, by phrase and most particular first, what the mouse does in text
  built once and kept:
  - "Drag the glowing cards" becomes "Press A on each glowing card";
  - "Click Boiler in the build menu" becomes "Pick Boiler in the build menu with L1";
  - "click the marked tile" becomes "aim the right stick at the marked tile and press R2";
  - "Shift-click" becomes "press X on";
  - "Hover it" becomes "Face it";
  - "drop a logic core" becomes "send a logic core";
  - anything else clicked becomes "press A on".

  It runs in `KeyNames.Fill` with the pad in use, so it reaches everything `TextTokens.Fill`
  already reached: guide steps, the guide's notices (now filled too), playtest questions, the
  build menu's lines, the save slots' armed prompts, the Ledger's and Overview's lines, the
  Workbench's captions and empty sockets, and Settings' key line.
- **Read from `KeyNames` now, not hand-written:** the golem order's "press E to sell it" and
  "[E] sell", and the contract's "press I here".
- **Gaps closed along the way:**
  - The build menu's tiles drop their digits with the pad in use, Demolish shows L2, and the
    card names L1.
  - The guide plate's counter reads "L3 hides · R3 skips", and its "Shortcut: 5." goes.
  - L3 puts the guide away and brings it back, and R3 runs the playtest kit. F1 and F9 had no
    pad binding; the two stick clicks were the last free buttons.
- **No new characters:** the names are plain Latin letters and digits.
- **Checked by:**
  - **`PadPhrasingTests`:** every guide step, question, build-menu line, the order's how-to and
    the Workbench's empty program read with the pad in use, and none says click, drag, hover, a
    named key or a key in brackets. The test lists the leftovers, so a new step written for the
    mouse fails until it has a pad phrase.
  - **The `gamepad` scenario:** with the pad in use the prompt reads [A], the guide "the left
    stick" and "Press A", the plate "L3 hides · R3 skips" with no shortcut, and the build menu L2
    on Demolish; L3 toggles the guide; a mouse nudge brings back WASD and the digits.
  - **`gamepad-ui`:** the Workbench's empty sockets read "send …", and its dials L1/R1 and L2/R2.
  - **Frames:** `gamepad-names`, `pad-workbench-open`, `pad-workbench-haul`.
  - **Break checks:** 10 of 10 caught, covering `KeyNames`, the phrasing, the order's how-to,
    the device hook, the build menu, L3, the counter, and the Workbench's sockets and dials.
- **Not covered by a check:** the plate's missing "Shortcut" on a step that has one (the scenario's
  step has none), and the guide's notices (no scenario raises one with the pad in use). Both go
  through the code paths above.

### D8. Performance and battery (1 day)

1. Run `--scenario perf` and the endgame benchmark (`dotnet test -c Release --filter Category=Perf`, plus
   the windowed perf run) **on the Deck**. Compare with `docs/performance-report-template.md` and record
   the run next to `performance-report-phase3.md` as `performance-report-deck.md`.
2. Targets: 60 fps (90 on the OLED) at the endgame factory in Game Mode; no frame over 33 ms outside loading.
   The unresolved first-launch hitch (`open-items.md` §4) matters more here: a cold JIT on a 4-core APU is
   slower. Options if it shows: ReadyToRun or tiered compilation settings in the export, a warm-up frame on
   the title screen, and pre-creating pooled nodes.
3. **Battery.** Cap the frame rate (Godot's `application/run/max_fps`, or the Deck's own limiter) and enable
   `low_processor_mode` while the sim is idle or paused. Measure watts at the Sandbox in the Deck's
   performance overlay.
4. The tick clock is independent of frame rate (invariant 7), so a lower display rate must not slow the game.
   Check by comparing a 30 fps cap run with a 60 fps one on the `world` scenario.

**As built on the build machine, 2026-11-10** (branch `deck/screens-rest`). Items 1 to 3 need the
Deck; what could be done or prepared without it:
- **Item 4, done.** The `golems` scenario at a fixed 60, 30 and 20 fps reports the same cycles:
  G1=49, G2=49, G3=25, G4=6, G5=20, G6=11. The clock catches up every tick a slow frame owes, with
  no cap per frame. A Core test, `AMinuteAtAnyFrameRate_TicksTheSame_EvenAtFourTimesSpeed`, pins
  a minute at 60, 30 and 20 fps at 4×, where a 30 fps frame owes more than one tick. A
  one-tick-per-frame clock fails it at 30 and 20.
- **Item 3, battery.** `BackgroundFrameCap`, which the player adds, draws at 10 fps while the
  window has no focus: the Steam overlay or the quick-access menu over it, or another window on a
  desktop. The factory runs on at its own speed, and focus puts the cap back as it was. It stays
  off under a scenario, since a run launched from a shell often lacks focus, unless the
  scenario testing it turns it on (`gamepad`). A cap in play is the Deck's own limiter (Game
  Mode's quick-access menu), so there's no in-game setting.
- **Item 2, the cold start.** A frame watch for any scenario: `--frame-watch` prints its five
  worst frames of wall time after loading, each with its step and its process time. On the
  Windows export of the Deck's pack, `gamepad-ui` showed frames of 45 to 65 ms at first events:

  | First event | Before | Cause, and what was done |
  |---|---|---|
  | The pad's first press | 62 ms | `PadPhrasing`'s ~30 regexes built `Compiled`, which writes and JITs IL at first use. Now interpreted: gone. |
  | A on the playtest note | 58 ms | `VirtualKeyboardShow` on a desktop, which has none. Now asked only where `DisplayServer` has the feature: gone. |
  | The A that builds a golem (the Workbench's first open) | 65 to 72 ms | Part JIT: **ReadyToRun** on export (`PublishReadyToRun` when a runtime is named, so only an export) brought it to 40 ms in one run. |
  | The Crate screen's first open (Y) | 45 to 59 ms | Every open rebuilt every row: fixed (below). |
  | Management's first open (Start) | 42 to 54 ms | The same: fixed (below). |

  **The last two, found and fixed (the dig, 2026-11-10).** The frame watch was given stamps on
  the engine's loop: a node first and last in the process step, one first in the physics step,
  and the renderer's pre- and post-draw signals. With them, a frame splits into input, physics,
  process, layout and draw.

  The open's cost was in **input**, the step a screen opens in, and not in drawing: about 18 ms
  of a normal frame is the wait for the display. Timing inside the open showed what it was:
  - **Every open rebuilt every row.** The Pack and Crate screens invalidated their lists on
    open, and Management's Inventory tab cleared its signature, so all their rows were made
    again each time. Each row cost about a millisecond: a button, a line, labels, a bar, an icon.
    The lists also rebuilt every row whenever anything shown changed, every second or so while
    golems filled a crate or a rate moved.
  - **Icons were reloaded each time.** A row's icon texture was freed with its row, so each
    open checked the disk, loaded and uploaded it again.

  What was done:
  - **Rows update in place** (`InventoryPanel`, Management's Inventory tab): the same goods in
    the same order only rewrite their counts, bars, rates and dimming. When the goods change, a
    good still listed keeps its row, and only a new good costs one.
  - **The forced rebuilds on open are gone.**
  - **Icons are kept once loaded** (`ItemIcons`).
  - **The Pack's and Management's rows are built during loading,** and so is the Crate screen's
    pack column.

  On the ReadyToRun export, two runs of `gamepad-ui --frame-watch` (whose frames land on 20 ms,
  50 Hz, here):

  | First event | Before | After |
  |---|---|---|
  | The Crate screen's first open | 44 to 45 ms | under 27 ms (out of the worst five) |
  | Management's first open | 30 ms | 30 to 32 ms |
  | The Workbench's first open | 29 to 31 ms | 29 to 31 ms |
  | A repeat open of the Pack (benchmark, dev build) | 25 ms to drawn, 15 ms in `Open` | 19 ms to drawn (a normal frame), 2 ms in `Open` |

  The worst frame left, 43 to 48 ms, is the Pack opening just after the scenario deposits one of
  every good at once: 21 new rows in one frame, which play can't produce. Gathered goods arrive a
  few at a time, and a row, once made, is kept. Loading the title (30 to 34 ms) is loading.

  Two tries that changed nothing were taken out: rasterizing the font's glyphs at every size
  during loading, and drawing Management for its first frames during loading. Godot's fallback
  text server, a lighter shaper, isn't in the official build (`TextServerManager` lists only the
  ICU one). **A print costs milliseconds on Windows:** timing code that prints inside the
  measured span inflated it from 0.4 to 20 ms, so the frame watch collects and prints at the
  end. ReadyToRun makes the export 203 MB (from 190); the editor's and the tests' builds are
  unchanged.
- **Item 1, prepared.** `Tools/Deck/export-linux.ps1 -DeckHost steamdeck -RunPerf` exports,
  copies, then runs `perf` and `gamepad-ui --frame-watch` on the Deck's own screen (Desktop Mode,
  `DISPLAY=:0`) and prints their verdicts. Not run: there's no Deck here. The numbers go in
  `performance-report-deck.md`.

### D9. Save and suspend (half a day)

- **Where saves go.** Godot's `user://` on Linux is `~/.local/share/godot/app_userdata/<name>/`. Confirm saves
  write and read there, with no absolute Windows paths (Phase 9 already asks for this). Loading never clamps
  (`Core/Economy/CLAUDE.md`).
- **Suspend and resume.** Pressing the power button suspends the Deck. On resume the game must carry on
  without a time jump: check that wall-clock deltas are not fed to the sim (the clock is frame-driven, so
  this should hold) and that the audio resumes.
- **Autosave** (Phase 7) matters more on a handheld that can lose power. Prefer to land autosave first.
- **Pause when the Steam overlay or the quick-access menu is open.** Godot gets a focus-out event: pause on
  it, and resume on focus-in.
- Test a Windows save loading on the Deck and the reverse, to prove Phase 9's portability rule.

### D10. A playtest on the Deck (half a day)

- Run `testscript/phase-2-playtest.md` (chapters 1 to 4 in 15 minutes) and `testscript/phase-1-playtest.md`
  on the Deck, in Game Mode, with the pad only if D4 to D7 are done.
- The playtest report writes to the Deck's own user folder. Bring the file back by `scp`.
- Add Deck-specific questions to the guide's playtest kit only if the user wants them: comfort of the build
  cursor, readability, and battery. They go in `Core/Tutorial` as data like the rest.

## 4. Order and sizing

| Phase | Needs | Effort | Delivers |
|---|---|---|---|
| D0 | the Deck | 0.5 d | decisions, SSH |
| D1 | D0 | 1 d | a Linux export that launches |
| D2 | D1 | 0.5 d | **MVP**: playable in Game Mode via Steam Input |
| D3 | D2 | 1 d | readable at 1280×800 |
| D4 | D3 | 2 d | gamepad model and scenario |
| D5 | D4 | 2 to 3 d | gamepad in the world |
| D6 | D5, final screens | 3 to 5 d | gamepad in every screen |
| D7 | D6 | 1 d | right prompts and glyphs |
| D8 | D2 | 1 d | performance and battery numbers |
| D9 | D2 | 0.5 d | save and suspend |
| D10 | any | 0.5 d | the Deck playtest |

D8, D9 and D10 can run after D2. D4 to D7 are one block and are best done after Phase 7's menus exist, so they
are built pad-navigable once. The whole plan is roughly 12 to 16 working days; the MVP is about 2.

## 5. Risks

1. **The .NET runtime on Linux** (D1.4). Highest uncertainty, cheapest to test. Test it first, before any
   other plan.
2. **The Workbench without a pointer** (D6). It's a drag-and-drop screen and the game's core verb.
   A gamepad design needs to be prototyped and tried by the owner, not specified from the desk.
3. **Hover-only information** (the `Straining` mood, tooltips) is invisible to a pad user.
4. **Text legibility.** A 12 px pixel font on a 7" screen can fail even at 1:1.
5. **Cold-start hitch** on a slower CPU.
6. **Mono export size and startup.** The self-contained .NET runtime adds roughly 70 to 100 MB. Fine for a
   hand-copied build; relevant if distribution ever matters.
7. **SteamOS updates** can change gamescope or the Proton/Steam Input behaviour. The template and launch
   options are in the repo so a re-setup is quick.
8. **No Deck on hand to test.** Windows can only run the Linux export under WSL2/WSLg (no gamescope, no real
   pad). Anything past D1's "does it launch" needs the device. A Windows gamepad (any XInput pad) tests
   D4 to D7's logic without it.

## 6. Out of scope

- Steamworks (achievements, cloud saves, rich presence, overlay-aware pause beyond focus loss).
- A Steam store build, depots, a Deck Verified submission and its checklist.
- Proton: the game runs as a native Linux binary, so there's no reason to use it.
- Haptics and gyro aiming.
- Changing a game rule, number or definition for the Deck.
- A second UI layout for small screens. Use the Phase 7 UI scale instead.

## 7. Working record

Fill these in while doing the work, so a later session doesn't repeat a finding.

- **Input audit (D4.1), 2026-11-08** (`Scripts/`, scenarios aside):
  - **The world, polled:** `PlayerNode` (move by `GetVector`, Interact held and pressed);
    `CameraRigNode.PlayerIsWalking` (the move actions). Both read actions, so the pad works with no
    change, and both already check `TextEntry.IsTyping`.
  - **The world, by event:** `PlayerNode._UnhandledInput` (rotate, carry, inventory);
    `BuildCursorNode` (rotate, cancel, and the left and right mouse buttons at the pointer's cell);
    `BuildMenuNode` (digits, Minus, Equal and X as raw keys); `CameraRigNode` (zoom by the wheel,
    any key skips the tower's event); `ManagementScreen` (Tab, Escape); `HudOverlay` (a click on
    the alert strip; the PAUSE button).
  - **Needs a pointer:** `GolemHoverNode` (hover card, D5.2); `LedgerTab` (drag to pan),
    `OverviewTab` (hover and click on the map), `InventoryPanel` (click and Shift-click a row),
    the Workbench's card drags: all D6.
  - **Screens closing:** `PackScreen`, `CrateScreen`, `ContractScreen`, `SupplyScreen`,
    `ConstructionPanelNode` close on cancel, I or E by action, so B closes them.
  - **Text and keys:** `SettingsTab` captures a key (keyboard only, by design); `TextEntry`.
- **D4 as built, 2026-11-08** (branch `deck/export`):
  - **`Scripts/Player/PadInput.cs`:** `PadBindings` adds a pad event to each action beside its
    keys (every owner's `Bind` calls `AddTo`, and the player calls `RegisterAll` once ready;
    `ApplyKeys` rewrites key events only, so a rebind keeps the pad's). `InputDevices` tracks the
    last device (a stick past 0.3 or a button is the pad; a key, a click or the mouse moved 4 px is
    the keyboard), and `InputDeviceNode` hides the pointer while the pad is in use.
  - **The bindings:** left stick moves (dead zone 0.25); A use, X carry, Y pack, B cancel and close,
    RB rotate, LB the next build-menu row (after the last, empty hands), LT the wrecking bar, RT
    places; the right stick moves the build cursor (dead zone 0.5 for it and the triggers); d-pad
    up and down zoom, d-pad right glides to the next stopped golem (the alert strip's click); Start
    opens the menu (Tab's screen); Back pauses and plays. **Space pauses** on the keyboard, so
    Pause has a key too; it joined the reserved keys (`KeyBindings.Reserved`).
  - **The pad's build cursor** (D5.1, pulled in): an offset from the player's cell, starting one
    cell the way the Artificer faces when a tool is picked up, stepped by the right stick (once on
    the push, then after 0.3 s every 0.12 s), at most 6 cells out. RT is the left click's press
    and release at it, so holding RT while the stick or the player moves lays a run through the
    same `BuildModeController` drag. With the pad in use the ghost is never "over the UI", and the
    build menu's card shows the held tool's.
  - **Checked by** the `gamepad` scenario (13 break checks caught). Left for D5 to D7: the hover
    card at the cursor (D5.2, now built: below), every screen's focus (D6), and prompts and cards
    that still name keys ("[E]", the card's "[3]"; D7).
- **D5.2 as built, 2026-11-09** (branch `deck/hover-card`): with the pad in use, the hover card
  reads the pad's cursor instead of the pointer.
  - **With a tool in hand** it is the build cursor, so the card shows for a golem the ghost is over.
  - **With empty hands** the cursor is the cell ahead of the Artificer and turns as they turn, so
    walking up to a golem and facing it shows its card, as hovering it does. The right stick moves
    the cursor to look around, up to 6 cells out (the build cursor's reach and repeat). A brass
    tile, not the ghost's green, marks the cell while it's off the cell ahead. A walk brings it
    back to the cell ahead.
  - The card hangs from the cursor cell's top-right corner on screen, as it hangs from the pointer.
    A full screen or the wrecking bar hides it, as with the mouse, and a screen hides the tile.
  - A tool picked up still starts its cursor a cell ahead, whatever the look was.
  - **Checked by** the `gamepad` scenario's hover steps (8 break checks caught); frames
    `gamepad-hover-ahead`, `gamepad-hover-look`, `gamepad-hover-floor`. Over a building the tile
    is under its sprite. There, the golem's own hover lights show the pick.
- **Screen checklist (D6):** build menu, Workbench, Pack, Crate, label picker, guide, hint, title, save/load,
  settings. For each: focus order, open/close, every action by pad, hover content reachable.
- **Export log (D1), 2026-11-08:**
  - **Presets** (`godot/export_presets.cfg`): "Linux Deck" (Linux, x86_64, PCK beside the binary, not
    embedded) and "Windows Smoke" (the same pack for Windows). Both include `data/*.json`, which
    `WorldNode` reads through `FileAccess`, and exclude `Core/`, `Core.Tests/` and `Tools/`.
  - **One command:** `Tools/Deck/export-linux.ps1` writes `build/deck/` (ignored by git), and
    `-DeckHost steamdeck` copies it to `~/Games/GolemFactory/` over SSH. The build is 158 MB:
    a 74 MB binary, a 2 MB pack and the .NET folder.
  - **The .NET risk (D1.4) is cleared.** The net10 project exports self-contained with no retarget:
    `data_GolemFactory.Godot_linuxbsd_x86_64/` holds `libcoreclr.so`, `libhostfxr.so` and
    `Microsoft.NETCore.App` 10.0.10. The Linux runtime pack came from nuget.org (already in
    `nuget.config`) and is cached in `~/.nuget/packages` after the first export.
  - **Godot can sit idle after an export** that has finished: the pack and the .NET folder are
    written, and the process doesn't exit. It happened on most runs, the first for each platform
    included. The script watches the log for the pack's `DONE` and ends Godot 20 s later. A build
    made that way matched a clean run's pack byte for byte.
  - **The exported build runs scenarios.** On "Windows Smoke", `GolemFactory.exe --headless
    --fixed-fps 60 -- --scenario <name>` passes `world`, `build`, `interact`, `golems`,
    `golem-motion`, `workbench`, `save`, `tutorial`, `inventory`, `audio`, `settings`, `title`,
    `slots`, `contracts`, `endless`, `docks`, `golem-sales`, `supply` and `creative`. Two can't run
    there: `art` reads the repo's `Tools/Art` manifest, and `loop` needs a scene path on the
    command line, which release templates refuse. `golem-motion` first failed on the export
    because it listed `res://art` for `.png` files, and a pack lists `.png.import`. That was the
    scenario's check, not the game, which asks `ResourceLoader.Exists`. Fixed.
  - **Not yet run on the Deck:** no WSL on the build machine, so the Linux binary's first launch
    (D1.5 and D1.6) is the owner's.
- **Display readings (D3):** the stretch mode chosen, per-screen legibility notes.
- **Performance runs (D8):** where the numbers are stored.
- **Accessibility pass (§10):** each row's reading on the device, and the calls made.

## 8. Decisions

Asked 2026-11-07 (the user chose the second map region to build first; the Deck waits for it):

- **D1: (b) native gamepad support now**, so the game plays without a mouse. Built and tested on
  Windows with scenario-driven pad events; the Deck itself checks it.
- **The owner has a Deck.** The device steps (D1's launch, D2, D3's readability, D8, D9, D10) are
  prepared for the owner to run, and their findings recorded in §7.
- **D3: decide from frames**: every screen captured under `keep`, `expand` and a 1280×800 base, then
  picked by looking.
- D2, D4 and D5 take the recommendations: Game Mode with Desktop Mode as a fallback; the pad works
  on Windows too; touch emulates the mouse and nothing more.

## 9. If it ever becomes a store release

Not planned. For the record, it would add: Steamworks via GodotSteam (a GDExtension, which needs the
mono build checked against it), a Steam Deck Verified pass (default controller layout shipped in the game,
text and glyph legibility at 1280×800, no launcher or login the pad can't drive, proper suspend), cloud saves
(Phase 9's one-folder save), and Steam's Linux runtime container (Sniper) compatibility. This doc's D1 to D10
are the foundation of all of it.

## 10. Accessibility pass before the Deck

A checklist to run with D3's frames and again at D10, adapted from Claude Code Game Studios'
accessibility audit (MIT). Each row says where the game stands today (checked 2026-11-07) and which
phase picks it up. "Call" marks a row that needs the user's decision before anyone builds it.

| Item | Today | Lands in |
|---|---|---|
| **Text readable** at 1280×800, at arm's length | 12 to 14 px on the 1280×720 layout: the same ratio as the checklist's "18 px at 1080p", which is a monitor's figure, not a 7" screen's at 40 cm. The UI scale (1 to 2) can't rise at 1280×800 until screens reflow (`menus-design.md` §4, call 6; `open-items.md` §3). | D3: the owner judges on the device |
| **Contrast**: text 4.5:1 and controls 3:1 against what's behind them | Not measured. | D3: measure each ink against its plate from the `Ugui` colours, as `menus-design.md` §5 measured the colour-blind pairs |
| **High-contrast mode** | None. | **Call**, once the contrast numbers are in |
| **Colour never the only cue** | Done: source tiles striped, moods in words on their badges, card families in words (`menus-design.md` §5). | — |
| **Colour-blind marks, reduced motion** | Done (`menus-design.md` §5). | — |
| **Separate volumes** | Done: Master, Music, Effects and a mute (`audio-design.md` §5). There's no speech, so no dialogue slider or subtitles. | — |
| **Every sound that tells you something can also be seen** | Done: the stall chime with the red mood and its alert, the claim chime with the glint and the note, the fanfare with the banner (`audio-design.md`'s slot table, `contracts-design.md` §12). | Check again for any sound added later |
| **Mono audio** | None: effects pan by position. | **Call** (low): the Deck's speakers are close together |
| **Remapping** | Eight keyboard actions rebind (`menus-design.md` §4). On the Deck, Steam Input remaps every control at the system level (D2). | D4 adds the pad bindings in code |
| **No required chords** | Shift-click in the Pack and Crate screens (`InventoryPanel`). | D6 gives the pad a trigger for it; check a mouse-only player has a way too |
| **No long holds without an alternative** | E is held for a crank's whole length (twice the recipe's time, `HandCrankRules`) and to keep harvesting. There's no toggle. | **Call**: a "tap to start, tap to stop" setting; A on a pad is the same hold |
| **Pause at any time** | Done (D4): Space, the pad's Back, or the HUD's button. Not over a full screen. | — |
| **Adjustable speed, nothing timed** | Done: 0.5× to 4× (`ClockReadout.SpeedPresets`); contracts and the tower never fail (`contracts-design.md` §0). | — |
| **The current goal always to hand** | Done: the guide's panel; F1 hides it and brings it back. | D6: B dismisses it on a pad |
| **Everything reachable without a pointer** | No: the screens are mouse-driven. | D6. Its `ui_*` focus also gives keyboard-only play, so test it with the arrow keys too |
| **Hover-only information** | The `Straining` mood and the tooltips need a pointer. | D5.2 and D6.4 |
| **Touch** | Not handled. | D5's decision: emulate the mouse, nothing more |

Record the results in §7, under "Accessibility pass (§10)".
