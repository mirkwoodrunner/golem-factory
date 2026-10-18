# Crates: local storage, and the pack

**Status: design, approved, building (Phase 1 of `docs/roadmap.md`).** §5's questions were
answered on 2026-10-18; the rest of the document was then checked against the code and turned
into a buildable spec. Like `cozy-automation-design.md`, it is written before the code and should
be kept as written afterwards, with corrections recorded rather than hidden (§6 lists the ones
found so far).

The inventory screens have their own spec: `docs/inventory-screens-design.md`.

## 0. Invariants

A pass that touches crates, the pack, the Post or any cost must keep all of these. Each gets a
test in `Core.Tests` when it is built.

1. **No item is lost.** Collecting moves what fits and leaves the rest in the crate. A transfer
   withdraws only what the other side has room for, then deposits it, so a deposit never refuses.
   Demolition refunds a crate's contents or is refused. A harvest checks the pack's room before it
   takes from the stall.
2. **A load never clamps.** A save restores the pack, the Post and every crate exactly, above
   their caps if need be (as `SteamBoiler.SetCoke` keeps above-cap Coke). Restores use a load-only
   setter (`StorageBuffer.RestoreQuantity`), never `Deposit`, which clamps.
3. **Caps are per good, never per buffer** (`StorageBuffer`'s class comment). A crate holds **50**
   of each good, the pack **100**, the Post store **200** (placeholder). A good above its cap can be
   spent or taken out, but nothing more can be added until it is under the cap. `RoomFor` already
   answers 0 there.
4. **Golems never reach the pack.** A golem reaches only the crates on its source and target
   tiles. The pack is the player's alone.
5. **Costs come only from the player's wallet**: the pack, then the Post store once it exists
   (§10). Never from a crate, near or far (§5 Q6). Every charge goes through one function
   (`PlayerWallet.TryCharge`), so the order can't drift between call sites.
6. **The first golem pushes into a crate** (§5 Q1). Chapter 1 teaches "E at the crate collects".
7. **No new `StallReason`.** An empty crate is `BufferEmpty`/`MissingItem`, a full one `BeltFull`,
   a wrong label `FilterMismatch`; each already names the crate by its endpoint's `DisplayName`.
   `StallReason`, `AppendageActionType` and `InteractionKind` stay append-only.
8. **Demolition is still a full refund** (root `CLAUDE.md`): the cost and the crate's contents
   come back into the pack (and, once it exists, the Post store after it). If they won't fit, the
   wrecking bar refuses with "pack full of ___" and nothing is torn down. A load's sweep (`refund: false`) refunds nothing, or save/load duplicates.
9. **Rules live in Core, on `SandboxWorld`.** Collect, transfer, label, wallet and migration are
   plain classes a test can drive without a scene. Nodes only draw and forward clicks.
10. **The id-routed fork is untouched.** Golems with no spatial endpoint holder route by bare
    buffer id, as `IdRoutedDemoRegressionTests` pins. A depot given an explicit `bufferId` keeps
    publishing that buffer (opt-in by null, `cozy-automation-design.md` §0.6).
11. **Labels keep their meaning** (`cozy-automation-design.md` §1): a labelled crate takes and
    gives only its good to golems. The player's hands may still take anything out of it, so a
    label never strands goods.

## 1. The problem

Every depot is a window onto one store, `FactoryStockpile`, and that store has no limit
(`PlaceableDepot.bufferId`, `SandboxSetup.ApplyBufferPolicy`). Three things follow, and the
playtest hit all three:

- **A golem anywhere reaches every good everywhere.** A plate-maker behind any depot hauls from
  the whole factory's Scrap, and converts all of it.
- **Nothing downstream ever fills**, so nothing ever stalls on "full" and tells a converter to
  stop. (The boiler half of this was fixed on its own: `SteamBoiler.CokeCapacity`.)
- **Layout does not matter.** Belts, carriers and unloaders exist because the guide asks for them,
  not because a factory needs them. A logistics game where logistics are optional has lost its
  verb.

## 2. Decisions (the user's)

1. **Crates are local.** A depot holds its own goods. A golem behind it reaches that crate and
   nothing else.
2. **A store reachable from anywhere is a late-game unlock**, in the role Satisfactory's
   Dimensional Depot plays: something you earn by building a factory, not the ground the factory
   stands on.
3. **The player's inventory is part of the game.** The pack is limited, as in other automation
   games (§5 Q2).

## 3. The model: three kinds of store

| Store | Buffer id | Who reaches it | Cap per good |
|---|---|---|---|
| **Crate** | `Crate(x,y)` on a depot, `Mast(x,y)` on a Freight Mast | golems on its neighbouring tiles; the player by hand | 50 |
| **The pack** | `FactoryStockpile` (kept: saves name it) | the player only: building, claiming, ordering, hand-loading | 100 |
| **Pneumatic Post** | `PneumaticPost`, shared by every Post crate | golems like any crate; the player's spending, after the pack | 200 (placeholder) |

**The pack is what `FactoryStockpile` already is to the player**: every cost in the game is
paid from it today. What changes is that **golems lose access to it**, and that it is limited.
The id stays; its meaning narrows from "the factory's goods" to "what the player carries".

### What the player does with a crate

- **E at a crate takes what fits into the pack.** One press, every good it holds, not one item per
  press. What doesn't fit stays in the crate, as a golem's partial haul does. The player becomes
  the factory's first truck, which is exactly the job the Pneumatic Post later takes off them.
- **I at a crate opens the crate screen** (`docs/inventory-screens-design.md`): the crate and the
  pack side by side, goods moved between them by clicking, and the label picker.
- **Hand-harvesting with a full pack** greys out the prompt, which says "pack full".
- **Labels stay** (`cozy-automation-design.md` §1), set on the crate screen instead of by
  pressing E (§4.2).

## 4. What changes, system by system

| System | Today | With crates |
|---|---|---|
| `PlaceableDepot` | publishes `FactoryStockpile` | publishes its own crate, `Crate(x,y)` (§4.1) |
| `PlaceableFreightMast` | publishes `FactoryStockpile` | publishes its own crate, `Mast(x,y)`, the id it already registers with the mast network |
| `StorageBuffer` capacity | stockpile unlimited, others 100/good | crates 50, pack 100, Post 200 (§4.3). §1.2's per-good rule carries over unchanged |
| Hand harvest (`PlayerInteractor.TryHarvest`) | deposits into the stockpile, ignoring the result | checks the pack's room first; refused at the cap with "pack full", prompt `Unavailable` |
| Costs: build, station, Floor Expansion, bays, Assembly Line, truckloads, hand-crank inputs, boiler hand-load | `TryWithdrawBundle(stockpileId, …)` | `PlayerWallet.TryCharge(…)`: the pack only until the Post exists (§4.4) |
| Golem `Haul` / `Push` | into the shared stockpile through any depot | into that depot's crate; no change to `GolemEntity` |
| Freight mast / Zeppelin | lands in the stockpile | lands in the mast's own crate, which the player or a golem empties |
| `[E]` at a depot | cycles the label (`TryRelabelDepot`) | collects (`TryCollect`); the label moves to the crate screen |
| Demolishing a crate | refunds its cost | refunds its cost **and its contents** (§4.6) |
| Ledger rates (`TechTreeReadout`) | the stockpile's rate | fresh production from `ItemAssembledEvent` (§4.5) |
| `TechTreeProgressLedger.HasItem` | sweeps every buffer | unchanged: crates are registry buffers, so the sweep already sees them (§6) |
| Save | buffers by id | crate contents on their `BuildingEntry`; `SaveData.version` (§7) |
| Pack and crate views | Management's Inventory tab lists every buffer | the Pack and Crate screens; the Inventory tab lists the pack first (§4.7) |

The column that matters: **most of the economy does not change.** Everything that charges the
player already charges `FactoryStockpile`. The change is concentrated in the depot, the mast, the
interactor and the save.

### 4.1 Crate buffers and endpoints

- **Id format:** `"Crate(" + cell.x + "," + cell.y + ")"`, the shape `PlaceableFreightMast`
  already uses for `Mast(x,y)`. It is what a stall badge prints ("Crate(3,-2) full"), via the
  endpoint's `DisplayName`.
- **Opt-in by null:** `PlaceableDepot.bufferId` empty means "my own crate", and
  `placeables.json`'s `DepotPrefab` and `FreightMastPrefab` set `"bufferId": ""`. A non-empty id
  publishes that shared buffer as today; that is how a Post crate works (§10) and how a test
  fixture keeps the old behaviour.
- **One interface for both:** `ICrate` (new, `Core/Buildings`), implemented by `PlaceableDepot` and
  `PlaceableFreightMast`: `CrateId`, `Buffer`, `OwnsBuffer` (false for a Post crate),
  `FilterItemType`, `CanLabel` (false for a mast) and `SetFilter`. The interactor, the screens,
  demolition and the save ask `ICrate`, never the concrete part.
- **A label wraps the crate's own buffer:** `FilteredBufferEndpoint(crateBuffer, label)`, unchanged.
  `PublishEndpoint` already re-registers on the same cell when the label changes.

### 4.2 The interactor

- `RefreshInteractables` collects every `ICrate` part (depots and masts) into the list it now
  keeps as `_depots`. `InteractionKind.Sort = 5` keeps its value and now means "a crate"; its
  verb (`InteractionTargeting`) becomes "Collect from", and the detail reads
  "holds 12 Scrap, 3 Coke · [I] open". With nothing in it: "empty · [I] open".
- `TryCollect(ICrate)` replaces `TryRelabelDepot`. It calls `CrateTransfer.Collect` (inventory
  spec §3) and pops "+12 Scrap", or "Pack full of Scrap" (refused) when nothing fitted.
- **The prompt greys** (`Unavailable`) at a stocked stall whose good the pack has no room for, and
  at a crate none of whose goods fit. Detail: "pack full".
- `CycleFilter` and `DepotFilterOptions.Next` retire. The picker lists `DepotFilterOptions.BuildCycle`
  over **`TechTree.Ledger.Items`** (every good the factory has ever made), not the crate's own
  keys, which in a fresh crate are nothing.

### 4.3 Capacity

`SandboxSetup` gains `packCapacityPerType` (100) and `postCapacityPerType` (200), and
`productionBufferCapacityPerType` is renamed `crateCapacityPerType` (50) in the class and in
`sandbox.json` together. `ApplyBufferPolicy` sets the registry default to the crate cap and the
pack and Post as overrides. In the Sandbox every buffer without an override is a crate, so the
default is the crate policy; the registry's comment on re-capping existing buffers still holds.

### 4.4 The wallet

`PlayerWallet` (new, `Core/Economy`) on `SandboxWorld.Wallet`, built on the registry:

- `Quantity(good)`: pack plus Post. The build menu's affordability and every shortfall message read it.
- `TryCharge(bundle)`: atomic, like `TryWithdrawBundle`. Each good is taken from the pack first,
  then the rest from the Post. All or nothing.
- `RoomFor(good)` and `Refund(bundle)`: pack first, then Post. Demolition, golem dismantling and
  a cancelled charge use these. **Harvesting and collecting go into the pack only**: they are
  what the player carries.

Every call site in the table above takes the wallet instead of a `(registry, bufferId)` pair.
Built in step 1 with no Post, so step 6 only plugs the Post in.

### 4.5 Ledger rates

`ProductionTally` (new, `Core/Economy`) listens to `EventBus.ItemAssembled` and keeps a monotone
count per good. `SandboxWorld.Advance` samples it into the existing `Throughput` tracker under the
series id `"Produced"` (not a buffer; it is never in the registry), on the same real-time
schedule as the buffers. `TechTreeReadout` reads `TryGetRatePerMinute("Produced", output)`. The
Inventory tab keeps measuring buffers.

### 4.6 Demolition

- `BuildModeController.RefundFor` adds an `ICrate`'s contents when `OwnsBuffer` is true. A Post
  crate's contents are the shared store's and stay there.
- `RefundWouldFit` asks the wallet. A refusal names the first good that doesn't fit:
  "Not demolished: pack full of Scrap." and pops "pack full of Scrap".
- After the refund, the crate's buffer is emptied and removed (`StorageBufferRegistry.Remove`,
  new), after its endpoint is unregistered. A re-placed depot on the cell gets a fresh crate.
  `Remove` is for this only: the registry's "never remove" rule is about a load, where endpoints
  still hold the instance.

### 4.7 The Inventory tab

`InventoryReadout.Compose` lists the pack first, then the Post, then crates in cell order
(`CellOrder`), with display headers "Pack", "Pneumatic Post", "Crate (3,-2)". The rate column is
unchanged.

## 5. Questions, answered (2026-10-18)

Each question keeps its original recommendation, then the user's decision.

1. **Does the first golem push into a crate or into the pack?** Recommendation: a crate, from the
   start. Chapter 1 then teaches "E at the crate collects", which is the verb the whole game now
   runs on, at the moment it is simplest.
   **Decided: a crate.**
2. **Is the pack limited?** Recommendation: no. A limited pack makes the player's own inventory
   a puzzle on top of the factory's, and that is not this game's puzzle. The pressure to automate
   comes from walking, not from slots.
   **Decided: limited, 100 of each good.** The player's inventory is part of automation games;
   the number can be tuned later. What follows from the limit, all agreed:
   - Collecting takes what fits; the rest stays in the crate.
   - Demolishing a crate whose contents won't fit is refused, with "pack full of ___".
   - Hand-harvesting with a full pack greys out the prompt, which says "pack full".
   - A migrated save keeps any good above 100 (§7): it can be spent, not added to, until it is
     under the cap.
   - No price in `data/` asks for 100 or more of one good today (checked 2026-10-18), so nothing
     becomes unaffordable. A tuning pass that raises a price past the cap must not.
     **Correction: one computed price does** (§6.1).
3. **Crate size.** Recommendation: 50 of each good. Big enough that a crate collected once a
   minute or so does not stall a Scavenger (about 1 Scrap per second), small enough that a
   converter's input crate empties and makes the player feed it. Tuning, not derived.
   **Decided: 50, as a starting point.**
4. **What does "put into a crate" put?** Recommendation: a labelled crate takes its good from the
   pack, up to the crate's room. An unlabelled crate opens a small picker of the goods in the pack.
   Without the picker a player cannot hand-feed an unlabelled crate at all.
   **Decided: inventory screens, kept separate.** The pack and a crate each get their own
   inventory interface. At a crate both show, side by side, and goods move between them. This
   replaces the picker.
5. **When does the Pneumatic Post unlock, and what does it cost?** Recommendation: the tier where
   Brass and Casing exist (chapter 8, near the freight mast), so the early game is fully manual
   and the post arrives once walking between lines has become the bottleneck. A Ledger node
   marked `IsPlanned` until it ships, per the root `CLAUDE.md`.
   **Decided: the Satisfactory model, unlocked in chapter 8.** The original model ("its contents
   count as in the pack") breaks with a 100-per-good pack: a Post line would fill the pack and
   stall. Instead:
   - Every Post crate opens onto **one shared Post store**, separate from the pack, with its own
     cap per good (placeholder: 200).
   - **Spending takes from the pack first, then the Post store.** Both are the player's wallet,
     so this does not reopen Q6.
   - Unlocks with Brass and Casing (chapter 8, near the freight mast); a Ledger node marked
     `IsPlanned` until it ships.
   - Cost: some Brass and Casing, set in the tuning pass. Name: Pneumatic Post, unless a better
     one turns up.
6. **Do building costs also draw from nearby crates?** Recommendation: no. One wallet, the pack,
   keeps "can I afford this?" a single number. Proximity spending is a second rule the HUD would
   have to explain.
   **Decided: no; costs come from the pack** (and, once it exists, the Post store, per Q5).

## 6. Corrections found against the code (2026-10-18)

The invariants that were here are now §0. Checking the design against the code turned up these.

1. **A price passes the pack's cap.** `FloorExpansionService.NextCost` escalates per purchase:
   80 Scrap + 40 Iron Plate, then 160 + 80, up to 480 + 240 for the sixth. Every purchase after the
   first is unaffordable from a 100 pack, and from 100 + 200 the fourth onwards still is. §5 Q2's
   check read `data/` only. **Open, for the lead:** until the tuning pass decides (a second good per
   step, or a flatter curve), clamp each good of `NextCost` at the pack cap, and add a test that
   every price in the game (data and computed: Floor Expansion's six steps, bays, truckloads, the
   Assembly Line's opening prices, chassis, placeables) is payable from a full pack.
2. **`HasItem` needs no change.** §4's old row said the ledger "must hear about goods arriving in
   crates". `TechTreeProgressTracker.SweepBuffers` walks every buffer in the registry, and crates
   are registry buffers, so it already does.
3. **The Ledger's rates come from `ItemAssembledEvent`, not `GolemCompletedEvent`.** The old row
   named `GolemCompletedEvent`, which carries only a golem id. `ItemAssembledEvent` carries the
   good, the quantity and the tick, and is the same fresh-production signal the Clock Tower uses.
4. **There is no HUD inventory in the Sandbox.** The old row "HUD inventory: the stockpile → the
   pack" has nothing to change: the only stock views are Management's Inventory tab, the
   construction panel's stock line and the build menu's tooltips. The pack gets its own screen
   (the inventory spec) and the Inventory tab already shows a capped buffer as "84/100".
5. **A harvest into a full pack would lose the item.** `TryHarvest` takes from the stall, then
   calls `Deposit` and ignores the result. With an unlimited stockpile that never mattered; with a
   cap it is item loss. The room check moves before the take (§4).
6. **Crate contents can't ride in `SaveData.buffers`.** Buffers restore before buildings, and the
   load's sweep of the old buildings would remove the restored crates (§4.6). They save on the
   building instead (§7).
7. **A new full screen does not hide the world prompt for free.** `PlayerInteractor.RefreshAffordance`
   asks `HudScreenPolicy` about three named screens (Workbench, Management, construction), not the
   `ScreenCoordinator`. It should ask `SandboxWorld.Screens.AnyOpen`; the root `CLAUDE.md` says a new
   screen gets this for free, and today it doesn't. **Still open after steps 1-3**: no new screen
   exists yet, so it is step 4's to fix.

### 6.1 Corrections from building steps 1-3 (2026-10-18)

Where the code differs from the spec above, and why.

1. **§6.1 is done as written**: `FloorExpansionService.NextCost` clamps each good at the pack's
   cap, and `CrateTests.EveryPriceInTheGame_IsPayableFromAFullPack` covers placeables, chassis,
   every deck card's opening price, truckloads, the bay upgrade and every Floor Expansion step.
2. **The wallet is not passed to every call site.** The parts §4.4's table names keep their
   `(registry, bufferId)` wiring and charge through a `PlayerWallet` built over it (the wallet is
   stateless over the registry, so it is the same wallet as `SandboxWorld.Wallet`). Every charge
   still goes through `PlayerWallet.TryCharge`, and every refund through `Refund`. Step 6 passes
   the Post id where those wallets are built: `BuildModeController.ConfigureEconomy`,
   `GolemConstructionStation.Wallet`, `HandCrankBench`, `FloorExpansionService.Wallet`,
   `AssemblyBayStructure.TryUpgrade`, `AssemblyLineState.TryClaimSlot`, `TruckloadMarket.TryOrder`
   and `PlayerInteractor.Wallet`. Two readers still read the pack's buffer directly, which is the
   same number until the Post exists: the Assembly Line tab's affordability and the bench's.
3. **`postCapacityPerType` is not in `SandboxSetup` yet.** With no Post buffer there is nothing
   for it to cap; step 6 adds it with the store.
4. **The crate prompt has no "[I] open" yet.** `I` is unbound until step 4, so the detail reads
   "holds 12 Scrap, 3 Coke" or "empty". An empty crate's prompt is `Unavailable` too, since E
   there does nothing; the target name is "Crate", or "Freight Mast" at a mast.
5. **`DepotFilterOptions.Next` is gone with `CycleFilter`;** `BuildCycle` stays for the picker.
   Until step 4 the only way to label a crate is `ICrate.SetFilter` (what the kit calls).
6. **`TechTreeReadout`'s constructor takes a rate series id**, which the Sandbox passes as
   `ProductionTally.SeriesId`. `ProductionTally` hears every `ItemAssembledEvent` on the static
   bus, as the tower and the tech tree already do.
7. **The kit feeds crates** (§8's guide rewrite is step 5): `program-smelter` and `work-carrier`
   grant their goods into the pack and `CrateTransfer.Put` them into the `SmeltInSpot` and
   `SmeltOutSpot` crates, so the `playtest-kit` scenario still fast-forwards every chapter. The
   guide's text still says "every depot opens onto the same stockpile" in places; step 5 rewrites it.
8. **Fixtures that build a whole chapter at once uncap the pack** (`TutorialGuideTests.Plenty`,
   the `build` scenario), as a test keeps any old behaviour by opting in. Every other test and
   scenario pays from a capped pack.
9. **`StallTracker` ignores a golem with no id.** Not a crates rule: a live world's tracker hears
   every golem on the static bus, and an unconfigured test golem's stall threw a null-key
   exception once a crate test left a world composed before it.

## 7. Saves, and migrating one

**Schema.** `SaveData` gains `int version` (current: **1**; absent reads 0, a save from before
crates). `BuildingEntry` gains parallel lists `crateItemTypes` / `crateQuantities`, written for any
building whose `ICrate` owns its buffer, keyed by the entry's cell like every other per-building
field. `CaptureState` leaves owned crate buffers out of `SaveData.buffers`; the pack and the Post
stay there by id. A label already saves as `depotFilterItemType`.

**Load order** (`SaveLoadService.RestoreState`), unchanged in shape:

1. `buffers.Clear()`, then every `SaveData.buffers` entry through `RestoreQuantity` (no clamp).
2. The building sweep (`refund: false`) removes the old crates' buffers.
3. Each building is rebuilt, which creates its empty crate; `RestoreBuildingState` then fills it
   through `RestoreQuantity`, after `SetFilter`.

**Version 0.** One stockpile and depots that hold nothing of their own. The stockpile becomes the
pack, contents intact even above the 100 cap; every crate starts empty; labels are kept. Nothing
is lost and nothing is duplicated; the player's golems simply find their crates empty until the
player or a line fills them. `LoadFrom` says so once, in the SaveLoad status line and as a popup at
the player: "Crates are local now. Your depots start empty; everything you had is in your pack."

## 8. The guide

Steps detect state rather than order (`TutorialGuide`), so most checks survive; the text and the
kit's fast-forwards are what change. **No existing step `Id` is renamed** (saves key on it). New
steps get new ids. A step done inside the crate screen sets a new `TutorialStep.Crate` flag, so the
panel stays on show over it, in its normal dock, which the crate screen's layout leaves clear
(inventory spec §5). Kit actions use the player's verbs: `Interactor.TryCollect` for E, and
`CrateTransfer.Put` for a click on the crate screen.

| Ch. | Step `Id` | Change | `Done` (world state) | Marks / target | Kit (`Performs`) |
|---|---|---|---|---|---|
| 1 | `depot` | text: a depot is a crate, 50 of each good, reached by golems beside it | unchanged | unchanged | unchanged |
| 1 | **`collect`** (new, after `work`) | "Collect from the crate": E at the depot takes what fits | the crate at `DepotSpot` (else any depot's) has `TotalWithdrawn(Scrap) > 0` | target `DepotSpot`; no marks | `TryCollect` on that depot |
| 2 | `presser` | text: short of Scrap, collect from the first depot | unchanged | unchanged | unchanged |
| 2 | `depot2` | text: drop "every depot opens onto the same stockpile"; the Presser reaches only the crates beside it | unchanged | unchanged | unchanged |
| 3 | `presser2` | text: the Presser's crate holds 50 Plate and it stops, saying so, when full; collect with E | unchanged | unchanged | unchanged |
| 5 | `smelt-depots` | text: the depot behind takes Scrap and Coke, which you fill by hand | unchanged | unchanged | unchanged |
| 5 | `program-smelter` | kit only | unchanged | unchanged | stops granting Scrap and Coke (moved to `smelt-feed`) |
| 5 | **`smelt-feed`** (new, before `work-smelter`) | "Feed the smelter": I at the input depot, click Scrap then Coke in the pack. `Crate` step | the crate at `SmeltInSpot` holds ≥ 2 Scrap and ≥ 1 Coke, or a smelter is in `_completedSinceEntry` | target `SmeltInSpot` | grant 40 Scrap and 20 Coke, then `Put` both into that crate |
| 5 | `work-smelter` | text: its crate holds 50 of each; a full Slag slot stops it | unchanged | unchanged | unchanged |
| 5 | **`carrier-feed`** (new, after `program-carrier`) | "Coke for the carrier": move Coke into the smelter's output crate. `Crate` step | the crate at `SmeltOutSpot` holds ≥ 1 Coke, or the carrier is in `_completedSinceEntry` | target `SmeltOutSpot` | grant 10 Coke, `Put` it into that crate |
| 5 | `work-carrier` | kit only | unchanged | unchanged | grants Slag 8 and Coke 4 into the pack, then `Put`s them into the `SmeltOutSpot` crate (was: into the stockpile) |
| 6 | `copper` | text: a label names a good the factory has handled | unchanged | unchanged | unchanged |
| 6 | `copper-depot` | text: build it, press I, pick Copper Ore as its label. `Crate` step | unchanged (`FilterItemType == CopperOre`) | unchanged | unchanged (`SetFilter`, what the picker calls) |
| 7 | `save` | text: your pack and every crate go into the save | unchanged | unchanged | unchanged |
| 8 | `copper-ingot` | text: each line's crates are its own; feed them by hand, by golem or by belt | unchanged | unchanged | unchanged |
| 8 | **`post`** (new, after `casing`) | "A Pneumatic Post" (§10). `menuKey: "PneumaticPostPrefab"` | two Post crates stand | none (chapter 8 marks nothing); target null | `PlaceGranted` twice on `Plan.Post`, two free tiles from the layout's free-line search |
| 9 | `zeppelin-card` | text: "in your pack now" | unchanged | unchanged | unchanged |
| 9 | `mast` | text: the mast lands what it brings in its own crate | unchanged | unchanged | unchanged |
| 9 | **`mast-collect`** (new, after `launch`) | "Unload the mast": E at the mast, or stand a golem beside it | the mast's crate has `TotalWithdrawn(CopperOre) > 0` | target the mast's cell | `TryCollect` on the mast |
| 10 | `tower-feed` | text: a depot in the square starts empty; feed it by belt, by Post crate or by hand | unchanged | unchanged | unchanged |

`ChapterStarts` is unchanged: no new step opens a chapter. Playtest questions to add or reword are
in `testscript/phase-1-playtest.md`.

Between §9's step 1 and step 4 the guide is out of date: chapter 5 asks for a hand-feed the screens
don't exist for yet. The kit still gets past it, since it calls `CrateTransfer` directly.

## 9. Order of work

1. Crate buffers on depots and masts (`ICrate`), the capacity policy, the collect verb, the
   harvest room check, `PlayerWallet` over the pack, demolition with contents. Tests: a converter
   behind a crate stops when the crate is empty; collect moves what fits and leaves the rest;
   demolition refunds contents, and is refused when they don't fit; harvest stops at the cap; every
   price is payable from a full pack (§6.1).
2. Mast deliveries land in the mast's crate; Ledger rates from `ProductionTally`.
3. Save version 1 and migration, `RestoreQuantity`, including a pack above its cap.
4. The two inventory screens (`docs/inventory-screens-design.md`) and the `inventory` scenario.
5. Guide rewrite (§8) and a playtest (`testscript/phase-1-playtest.md`).
6. Pneumatic Post (§10), as its own pass.

Steps 1–3 are one PR's worth: the game is playable, if harder, after them.

## 10. The Pneumatic Post

The late-game store from §5 Q5: Satisfactory's Dimensional Depot, in brass tubes.

- **A Post crate is a depot whose `bufferId` is `PneumaticPost`.** A new placeable,
  `PneumaticPostPrefab`, in `placeables.json`: `PlaceableBuilding` (cost placeholder 10 Brass +
  5 Casing, set in tuning; not drag-placeable), `YSortSpriteRenderer`, and
  `PlaceableDepot { "bufferId": "PneumaticPost", "filterItemType": "" }`. Every Post crate
  publishes the one shared buffer, so a golem pushing into one fills what every other gives. Labels
  work on it as on any depot (the filter wraps the shared buffer).
- **The store lives on `SandboxWorld`** as an ordinary registry buffer, `PostBufferId =
  "PneumaticPost"`, capped by `postCapacityPerType` (200) in `ApplyBufferPolicy`. It exists from
  the start, empty, so the wallet never has to ask whether it does.
- **Spending is pack first, then Post** (`PlayerWallet`, §4.4). Refunds land the same way.
  Collecting at a Post crate (E) takes into the pack, like any crate.
- **Demolishing a Post crate refunds its cost only** (`OwnsBuffer` is false): the goods are the
  store's, not the crate's, and stay.
- **Unlock: its price.** It is in the build menu from the start, priced in Brass and Casing, so the
  player can first afford it in chapter 8. That is how the Freight Mast is gated today; there is no
  placeable-unlock system, and this doesn't add one.
- **Ledger node:** `bldg.pneumaticpost`, "Pneumatic Post", detail "Brass + Casing · every Post crate
  shares one store", kind `Building`, phase IV column (3), row 14, signal
  `TechTreeUnlockSignal.Building` with `TechTreeCatalog.BuildingPneumaticPost = "PneumaticPost"`,
  prerequisites `r7.brassalloying` and `r9.casingpress`. **`isPlanned: true` until the pass ships**,
  then the flag comes off with a "NO LONGER PLANNED" comment as the others did.
  `TechTreeProgressTracker.SweepBuildings` records it when a depot with that buffer id stands.
- **Save:** no new field. The store is `SaveData.buffers` under its id, restored with
  `RestoreQuantity`; the Post crates are ordinary `BuildingEntry`s.
- **Guide:** step `post` (§8). **Playtest:** the questions in the playtest script.
- **The screens:** at a Post crate the crate screen's left panel is the Post store, titled
  "Pneumatic Post". The Pack screen shows the store under the pack, read-only (inventory spec §4).
