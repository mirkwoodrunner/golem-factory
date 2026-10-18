# Inventory screens: the pack, and a crate

**Status: design, approved, building** (Phase 1, step 4 of `docs/crates-design.md` §9). Written
before the code, kept as written afterwards, with corrections recorded.

Two screens, kept separate (`crates-design.md` §5 Q4): the **Pack screen**, anywhere, and the
**Crate screen**, at a crate, where the crate and the pack show side by side and goods move
between them. The crate screen also holds the depot's **label picker**, which `docs/roadmap.md`
Phase 2 folded into it and which answers `docs/open-items.md` §4's "picker panel". It replaces
pressing E to cycle a label.

## 0. Invariants

1. **No item is lost and none is made.** For every good, crate + pack is the same before and after
   any click. A transfer withdraws only what the other side has room for, then deposits it.
2. **Both caps hold** (50 a crate, 100 the pack, 200 the Post). A transfer moves at most the room
   on the receiving side. A good already above its cap can leave, never arrive.
3. **A label is honoured on the way in.** A labelled crate accepts only its good from the pack,
   as `FilteredBufferEndpoint` accepts only its good from a golem. **On the way out it is not**: the
   player can take anything out of a labelled crate, so relabelling never strands goods.
4. **The rules are Core's.** What a click moves, what a row says, why a click is refused and which
   labels are offered are plain classes, tested without a scene. The nodes draw rows and forward
   clicks.
5. **Both screens are full screens.** Each joins `ModalScreens` (`_EnterTree`), implements
   `IClosableScreen`, registers with `SandboxWorld.Screens` in `_Ready`, and calls
   `Screens.Opening(this)` in `Open`. While one is open the player stays still, the HUD hides, and
   no other screen is up.
6. **One key, no new modes.** `I` opens the inventory; standing at a crate, the inventory it opens
   is the crate's. `E` keeps its single tap meaning: collect. Nothing is triggered by holding.
7. **Text is Latin-1 plus → ≥ █ ░** (`font-glyphs`). No "…" and no "▾": "Choose a label", not
   "Choose…".

## 1. Keys

| Key | Where | Does |
|---|---|---|
| **E** (tap) | at a crate | collect what fits (`crates-design.md` §4.2). Unchanged by this spec |
| **I** | at a crate (an `ICrate` within `InteractRange`) | open the Crate screen on it |
| **I** | anywhere else | open the Pack screen |
| **I**, **E** or **Escape** | either screen open | close it. Escape closes the label picker first, if it is open |
| **Tab** | either screen open | nothing, as over every other screen (`ManagementScreen._UnhandledInput`) |

**Why I, and why not hold-E.** `I` is unbound (bound today: WASD and arrows, E, G, R, X, Escape,
Tab, 1–9, 0, -, =, F1, F9), and it is the inventory key players expect. Holding E already means
"keep doing this" (repeat-harvest at a stall, crank at a bench), and E acts on the press, so a
hold-to-open would first collect the crate the player wanted to look into. A context-shared key
is the pattern `R` already follows. `E` closes the screens as it closes the construction panel.

The action is registered in code with the others (`PlayerNode.RegisterInputActions`:
`Bind(Inventory, Key.I)`, `Inventory = "inventory"`). `PlayerNode._UnhandledInput` calls
`PlayerInteractor.OpenInventory()` when no screen is open; an open screen handles its own close.

## 2. Core

| Class | Where | What |
|---|---|---|
| `CrateTransfer` | `Core/Economy` | static: `Collect`, `Take`, `Put`, `AmountFor(shift)` (below) |
| `TransferResult` | `Core/Economy` | `readonly struct`: `ItemType`, `Moved`, `Refusal` (null when something moved) |
| `CrateScreenReadout` | `Core/UI` | rows for one buffer seen from the other side; title, cap line, label text |
| `IPackScreen`, `ICrateScreen` | `Core/UI/IScreens.cs` | `Open()`, `Open(ICrate)`, beside `IConstructionScreen` |
| `PlayerInteractor.OpenInventory()` | `Core/Player` | the nearest `ICrate` in range opens the crate screen, else the pack screen |
| `SandboxWorld.ConfigureInventoryScreens(IPackScreen, ICrateScreen)` | `Core/World` | hands them to the interactor, as `ConfigureScreens` does |

### 2.1 Transfer rules

- **Amount:** a click moves **all that fits**; **Shift**-click moves **one**. `AmountFor(shift)`
  returns `1` or `int.MaxValue`. All-that-fits is the cozy default: the common act is "fill this
  crate" or "empty it", and a click never loses anything. One-at-a-time is for the careful case
  (two Coke for a carrier).
- **`Take(crate, pack, good, want)`**: moves `min(want, crate holds, pack.RoomFor(good))` from the
  crate to the pack. Labels don't apply (invariant 3). Refusals: "Pack full of Scrap", or "None
  left" when the crate has none.
- **`Put(pack, crate, good, want)`**: refused with "This crate is for Copper Ore" when the crate is
  labelled for another good. Otherwise moves `min(want, pack holds, crate.Buffer.RoomFor(good))`.
  Refusal: "Crate full of Coke".
- **`Collect(crate, pack)`**: `Take` with all that fits for every good the crate holds, in
  `BufferTrendUtility.SortItemTypes` order. What E does, and what the screen's Take all does.
- **Order inside a move:** `TryWithdraw(n)` then `Deposit(n)`; `n` was bounded by the receiver's
  room, so the deposit accepts all of it. A test asserts the deposit returned `n`.
- **The Post** is a crate whose buffer is the shared store (`crates-design.md` §10); the same rules
  apply. The wallet's pack-then-Post order is for spending, not for these moves: the screen moves
  between the pack and the crate on screen, nothing else.

### 2.2 Rows

`CrateScreenReadout.Rows(StorageBuffer side, Func<string,int> movable)` returns one row per good
held, sorted as above: `ItemType`, `Name` (`ItemTiers.DisplayName`), `Quantity`, `QuantityText`
("37/50", or "112/100" above the cap), `Bar` (`StockBarPolicy.Evaluate` against capacity),
`CanMove` (`movable(good) > 0`) and `Reason` (the refusal a click would give, else null). An empty
side gives one message row: "Empty." for a crate, "Your pack is empty." for the pack.

Title and cap line: "Crate (3,-2)" / "Holds 50 of each good"; "Freight Mast (5,4)" /
"Holds 50 of each good"; "Pneumatic Post" / "Shared by every Post crate · 200 of each good";
"Your pack" / "Carries 100 of each good". The label button reads "Label: any goods" or
"Label: Copper Ore"; a mast's reads "A mast's crate takes no label" and is disabled.

### 2.3 Label options

`DepotFilterOptions.BuildCycle(TechTree.Ledger.Items, crate.FilterItemType)`: `null` ("any
goods") first, then every good the factory has ever made in tier order, plus the current label if
it is somehow missing. The ledger only grows, so an option never disappears. Choosing calls
`ICrate.SetFilter`, which re-publishes the endpoint (`PlaceableDepot.PublishEndpoint`). Goods
already in the crate stay (`cozy-automation-design.md` §1's failure table).

## 3. Nodes

`Scripts/UI/PackScreen.cs` and `Scripts/UI/CrateScreen.cs`: each a `CanvasLayer`, `Layer = 30`
(Management's), built with `Ugui` in `_Ready`, registered as invariant 5 says, then handed to
`SandboxWorld.ConfigureInventoryScreens`. `_Process` re-renders a panel only when its rows'
signature changes, as `ManagementScreen.RefreshInventory` does, because golems keep filling and
draining the crate while the screen is open. Rows are found through a lookup kept by item type,
never by node name (root `CLAUDE.md`).

## 4. Layout

Numbers are `Ugui.Place(control, anchorMinX, anchorMinY, anchorMaxX, anchorMaxY, posX, posY,
sizeX, sizeY, pivotX, pivotY)`, Unity's frame (y up), at the 1280×720 reference. Colours and
sprites are `ManagementScreen`'s, so the screens read as the same family: `HeaderInk`,
`ItemNameInk`, `QuantityInk`, `DimInk`, `RowTint`, `BarTrack`, `CapacityFill`, `NearFullFill`,
`FullFill`, `SelectedTab`, `SelectedTabLabel`, and `art/UI/Steampunk/`.

### 4.1 Screen roots

| Control | Place | Notes |
|---|---|---|
| `CrateScreen` / `PackScreen` root | `Fill` | `MouseFilter.Stop` |
| `Backdrop` | `Fill` | `Ugui.Rect`, `(0.055, 0.045, 0.038, 0.80)`: the world stays faintly visible |
| Crate screen: `CratePanel` | `0, 0.5, 0, 0.5,  140, 0,  380, 520,  0, 0.5` | x 140–520, y 100–620 from the top |
| Crate screen: `PackPanel` | `0, 0.5, 0, 0.5,  536, 0,  380, 520,  0, 0.5` | x 536–916; 16 px gap |
| Crate screen: `Hint` | `0, 0.5, 0, 0.5,  528, -272,  776, 18,  0.5, 1` | `Ugui.Text` 12, `DimInk`, centred: "Click a good to move what fits · Shift-click moves one" |
| Pack screen: `PackPanel` | `0.5, 0.5, 0.5, 0.5,  0, 0,  380, 520,  0.5, 0.5` | x 450–830 |
| Pack screen: `Hint` | `0.5, 0.5, 0.5, 0.5,  0, -272,  380, 18,  0.5, 1` | "Stand at a crate and press I to fill it from here." |

The crate screen sits left of centre on purpose: the guide's plate docks at the top right
(`TutorialPanel.Dock(false)`: x 928–1268, y 168–346), and the panels end at x 916, so a guide step
done inside this screen (`TutorialStep.Crate`) is readable without a second dock.

### 4.2 Inside a panel (380 × 520)

| Control | Place | Notes |
|---|---|---|
| `Plate` | `Fill` | `Ugui.Image`, `NineSlice("…/iron_panel_nobolt.png", 8)`, tint `(0.13, 0.11, 0.09)` |
| `Title` | `0, 1, 1, 1,  -32, -10,  -88, 26,  0.5, 1` | `Ugui.Text` 17 bold `HeaderInk`; insets 12 left, 76 right; y 10–36 |
| `CloseButton` | `1, 1, 1, 1,  -10, -8,  60, 24,  1, 1` | Management's plate button, `steampunk_button_exit.png`, tint `(0.35, 0.10, 0.10)`, "Close" 12 white |
| `Cap` | `0, 1, 1, 1,  0, -38,  -24, 18,  0.5, 1` | `Ugui.Text` 12 `DimInk`; y 38–56 |
| `LabelButton` (crate panel only) | `0, 1, 1, 1,  0, -60,  -24, 28,  0.5, 1` | plate button, `steampunk_button_blank.png`, tint `SelectedTab`, label 13 `SelectedTabLabel`; y 60–88 |
| `Scroll`, crate panel | `0, 0, 1, 1,  0, -22,  -16, -148,  0.5, 0.5` | insets 8 left/right, 96 top, 52 bottom |
| `Scroll`, pack panel | `0, 0, 1, 1,  0, -6,  -16, -116,  0.5, 0.5` | insets 8, 64 top, 52 bottom |
| `LabelPicker` (crate panel) | as the crate `Scroll` | replaces the list while open (§4.4) |
| `TakeAllButton` (crate panel) | `0, 0, 0, 0,  12, 12,  110, 30,  0, 0` | plate button, `steampunk_button_accept.png`, tint `(0.80, 0.62, 0.32)`, "Take all" 13 bold |
| `Status` (crate panel) | `0, 0, 1, 0,  61, 12,  -146, 30,  0.5, 0` | `Ugui.Text` 12 `HeaderInk`; x from 134 to 12 short of the edge: "Moved 12 Scrap to your pack", or the refusal |
| `Summary` (pack panel) | `0, 0, 1, 0,  0, 12,  -24, 30,  0.5, 0` | `Ugui.Text` 12 `DimInk`: "7 goods · 100 of each" |

Inside `Scroll`: a `MarginContainer` (4 on each side), then a `VBoxContainer` "Content",
separation 2, as `ManagementScreen.ScrollList` builds them.

### 4.3 A row

A `Button` named "Row", `CustomMinimumSize (0, 30)`, `FocusMode.None`, with flat style boxes:
normal `RowTint` `(1, 1, 1, 0.035)`, hover `(1, 1, 1, 0.09)`, pressed `(0.85, 0.66, 0.32, 0.25)`.
Inside it, an `HBoxContainer` at `Fill` with offsets left 6, right -6, separation 6 (Management's
`RowLine`), holding in order:

| Child | Size | Content |
|---|---|---|
| `Icon` | `CustomMinimumSize (24, 24)`, `ShrinkCenter`, `KeepAspectCentered` | `ItemIcons.PathFor(good)`; the slot is drawn even without a sprite |
| `Name` | flex (`ExpandFill`) | `Ugui.Text` 13 `ItemNameInk` |
| `Bar` | `(56, 8)`, `ShrinkCenter` | Management's `Bar(StockBar)`, capacity mode |
| `Quantity` | fixed 60 | `Ugui.Text` 13 bold, right-aligned; `FullFill` ink at or above the cap, else `QuantityInk` |

A row whose good can't move (`CanMove` false) draws its `Name` and `Icon` at 55 % alpha and keeps
its tooltip set to `Reason`; it still takes a click, and the click writes the refusal to `Status`,
so a refusal is always said where the player is looking. Message rows are a 30 px `Panel` with
`StyleBoxEmpty` and a 12 `DimInk` label.

### 4.4 The label picker

Opened by `LabelButton`, whose text becomes "Choose a label" while it is open. Inside the
`LabelPicker` rect: a `ScrollContainer`, then a `GridContainer` with 8 columns, h and v separation
4. Each option is a 40 × 40 `Button` (flat, `RowTint`; the current label tinted `SelectedTab`)
with a 28 × 28 centred icon; the first is "any goods", drawn as the text "Any" (11, `ItemNameInk`).
Width: 8 × 40 + 7 × 4 = 348, inside the 364 px rect. Hovering an option writes its name to
`Status`; clicking applies it, closes the picker and writes "Label: Copper Ore" to `Status`.

### 4.5 The Pack screen with a Post

Once the Post store holds anything, the pack panel's list gains a header row "Pneumatic Post"
(24 px, Management's `HeaderPlateTint`, 14 bold `HeaderInk`) after the pack's rows, and the
store's goods below it, read-only (no hover tint, no click). The store is reached at a Post crate.

## 5. The guide over these screens

`TutorialStep.Crate` (new, like `Workbench`) keeps the guide on show while the crate screen is
open, in its normal dock, which §4.1 leaves clear. Steps that set it: `smelt-feed`, `carrier-feed`
and `copper-depot` (`crates-design.md` §8). `TutorialPanel` reads `CrateScreen.IsOpen` beside its
Workbench check.

## 6. The `inventory` scenario

`Scripts/Scenarios/InventoryScenario.cs` (Sandbox), registered in `ScenarioRunner.Scenarios`.
Real key and mouse events (`Input.ParseInputEvent` without a flush; a tap held across a frame; a
control clicked a frame after the screen opened). It checks:

1. **Pack screen.** `I` away from any crate opens it; the HUD hides; `Screens.OpenCount` is 1; one
   row per good with an icon slot and "x/100"; `I` closes it, and so do `E` and Escape.
2. **Collect.** A depot placed through the build menu, its crate given 20 Scrap, the pack at 95:
   `E` at the depot leaves the pack at 100 and the crate at 15. The prompt then reads "pack full"
   and is `Unavailable`; a harvest at the Scrap stall is refused and the stall keeps its unit.
3. **Crate screen.** `I` at the depot opens it with both panels; the Workbench and Management are
   closed (`OpenCount` 1); the world prompt is hidden.
4. **Put and take.** With 30 Coke in the pack and none in the crate, clicking Coke moves all 30;
   with 30 more in the pack and the crate at 48, a click moves 2 and the status says "Crate full
   of Coke"; Shift-click moves exactly one; clicking
   Scrap in the crate takes it back. After every click, crate + pack per good is unchanged.
5. **Label.** The picker lists "Any" first and only goods the ledger has seen; picking Copper Ore
   relabels the depot (`FilterItemType`), and the tile's endpoint is now a `FilteredBufferEndpoint`.
   Then a Scrap click in the pack is refused "This crate is for Copper Ore" with nothing moved,
   while the Scrap already inside can still be taken out.
6. **Take all** empties what fits, as `E` does.
7. **Above the cap.** A pack restored at 140 Scrap shows "140/100", gives Scrap to the crate, and
   refuses to take any back until it is under 100.
8. **Mast and Post.** At a Freight Mast the label button is disabled; at a Post crate the left
   panel is titled "Pneumatic Post" and moves into the shared store (once §10 is built).
9. **Demolition.** With the pack full of Scrap, X on a crate holding Scrap is refused with "pack
   full of Scrap" and the crate stands; after emptying the pack, the same click refunds cost and
   contents exactly.
10. **The guide's dock.** On `smelt-feed`, with the crate screen open, the guide's plate rect does
    not overlap either panel's rect.

It writes nothing to disk. Frames for a look (`--write-movie`) are taken at steps 1, 4 and 5, since
a UI change is verified by looking at it (root `CLAUDE.md`).

## 7. What is deliberately not here

- **Drag and drop.** Clicks are enough for two lists; the Workbench's drag code stays the
  Workbench's.
- **Moving between two crates.** Goods go through the pack: collect from one, put into the other.
  A crate-to-crate move would be a remote store, which is the Post's job.
- **Sorting or searching.** Every good in the game fits one scrolling list; tier order is the sort.
