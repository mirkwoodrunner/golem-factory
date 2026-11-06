Folder rules for the economy, cards, patents, crates and the pack. The root `CLAUDE.md` has the project overview, commands and settled rules.
Paths here are relative to `godot/` unless they say otherwise. Some of this code lives beside
it: `Core/AssemblyLine/`, `Core/Blueprints/`, `Core/World/FilteredBufferEndpoint`,
`Core/Buildings/PlaceableDepot`, `Core/UI/CrateScreenReadout`.

# Economy, cards and patents

- **A labelled depot publishes a different endpoint** (`FilteredBufferEndpoint`), which takes
  and gives only its good. `IFilteredEndpoint` lets a push tell "full" from "for something
  else"; it is not part of `IItemEndpoint` and should not become part of it.
- **The Assembly Line's unlock context is wired before the pool is seeded, and it is
  monotone.** Seeding first makes every prerequisite unanswerable and gating inert. The
  context reads `TechTreeProgressLedger`, which only grows, so spending your last Scrap never
  re-locks a card. `TryClaimSlot` refuses a locked card; a slot never offers a card the player
  already owns, nor one priced in a good the factory has never made.
- **There is no Artificer Focus meter, and patenting is free. Settled.** The Patent Registry is
  a named-program library. Do not reintroduce a Workbench currency to "give patents a
  purpose"; if reprogramming should cost something, the factory pays it in goods.
- **Item tiers are data** (`Economy/ItemTiers`), pinned by reflection over `ItemType`, so a new
  good can't be added without a tier.
- **Bare-string ids** identify belts, buffers and nodes across systems; `ItemType` holds the
  item-type constants. **Registries return `false` for a `null` id** rather than throwing.

# Crates and the pack (Phase 1: built and reviewed; the human playtest is pending)

The rules `docs/crates-design.md` and `docs/inventory-screens-design.md` set.

- **A depot publishes its own crate** (`Crate(x,y)`), a Freight Mast its own (`Mast(x,y)`, where
  a Zeppelin lands), 50 of each good. Both are `ICrate`; ask that, never the concrete part.
  **Golems never reach the pack** (`FactoryStockpile`, id kept because saves name it; 100 of
  each, up to 250 by upgrade). A depot with an explicit `bufferId` publishes that buffer and doesn't own it: a Post
  crate, and how a test keeps the old behaviour.
- **Creative Mode waives every cost at the wallet** (`StorageBufferRegistry.CostsWaived`,
  docs/menus-design.md §9). Ask a wallet whether something is affordable (`PlayerWallet.CanAfford`),
  never the stock directly, or Creative's waiver is missed.
- **Every cost goes through `PlayerWallet.TryCharge`** (pack first, then the Post store);
  refunds through `Refund`, **into the pack only, never the Post**, after `FirstMisfit` says they
  fit (a demolition or a dismantle the pack can't hold is refused, Post or no Post). Only a
  charge that bought nothing goes back through `Uncharge` (pack, then Post). Never charge a buffer
  id directly, and never charge a crate. Harvesting and collecting fill the pack only, and check
  its room before taking. A part that builds its own wallet takes the Post's id too (an optional
  `postBufferId`), or it spends from the pack alone.
- **The Pneumatic Post** is one registry buffer, `SandboxWorld.PostBufferId` ("PneumaticPost"),
  200 of each good, there from the start. A Post crate (`PneumaticPostPrefab`) is a depot whose
  `bufferId` is that id, so every Post crate opens onto it and golems reach it like any crate.
  Demolishing one refunds its cost, never the store. It saves in `SaveData.buffers` by its id.
- **Hands move goods through `CrateTransfer`** in Core: withdraw what the other side has room
  for, then deposit it. A label is honoured on the way in, not on the way out. Tap-E at a crate
  is `PlayerInteractor.TryCollect` (`InteractionKind.Sort`, "Collect from").
- **Demolishing a crate refunds its contents** with its cost, or refuses with "pack full of ___";
  its buffer is then removed (`StorageBufferRegistry.Remove`), on a load's sweep too.
- **A load never clamps.** Restores use `StorageBuffer.RestoreQuantity`, never `Deposit`. Crate
  contents save on their `BuildingEntry`, not in `SaveData.buffers`. `SaveData.version` is 1;
  a version-0 save keeps its stockpile as the pack (above the cap if need be), starts every crate
  empty, and says so once.
- **The pack's cap grows only by upgrade** (`SandboxWorld.PackUpgrade`, `sandbox.json`'s
  `packUpgrades`, the Assembly Line tab's Pack row): never lowered in play, restored by rung on a
  load (`packUpgradeTier`) without clamping. Crates stay at 50 and the Post at 200. **Every price
  fits the pack reachable when it opens** on the guide's path, pack only
  (`PriceReachTests`): a new price or rung must pass it. Floor Expansion charges
  `floorExpansion.purchases`, one bundle per purchase, unclamped. A next price naming a good the
  Ledger hasn't seen reads `Requires: <good>`, greyed. A price with a line the pack's cap
  can't hold (and the wallet can't cover) says so, `Upgrade your pack: …`, not `Need N more`:
  every priced thing formats its shortfall through `ConstructionCostPolicy.FormatShortfall`,
  passing the wallet so it reads the cap.
- **The Ledger's live rate is fresh production** (`ProductionTally`, series `"Produced"`), not a
  buffer's level.
- **`I` is the inventory key** (`PlayerNode.Inventory`, `PlayerInteractor.OpenInventory`): the
  Crate screen at a crate in reach, else the Pack screen. Nothing is bound to holding E.
- **The screens' rules are `SandboxWorld.Inventory`** (`CrateScreenReadout`): rows, clicks (one;
  Shift, all that fits, as automation games do), status lines, and the label picker's options, every good the ledger has
  seen. Moves go through `CrateTransfer`; `PlanTake`/`PlanPut` are the dry runs a row's refusal
  reads. `CrateScreen` and `PackScreen` only draw, through one `InventoryPanel` builder, and keep
  their rows by good. A step done in the Crate screen sets `TutorialStep.Crate`.
