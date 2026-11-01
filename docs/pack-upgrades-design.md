# Pack upgrades, and Floor Expansion priced up the tiers

**Status: approved by the user on 2026-10-18 with every recommendation as written, and built
the same day. A Phase 1 follow-up in `docs/roadmap.md`.** Written before the code, like
`crates-design.md`, and kept as written afterwards, with corrections recorded rather than hidden
(§8).

The user's calls (2026-10-18):

1. **Floor Expansion keeps ramping its cost.** The clamp at the pack's cap goes
   (`FloorExpansionService.AtMostAPack`, the stopgap of `crates-design.md` §6.1 item 1, kept by
   §10.1.8 and §11). This document supersedes that clamp. **Pack upgrades** raise the pack's
   100-per-good cap during play, so the later purchases can be paid.
2. **Later expansions cost more advanced goods**, to give those goods a use, rather than more
   Scrap and Iron Plate.

The two ship together. Without the clamp and without upgrades, the second purchase (160 Scrap
today) can't be paid from a 100 pack, and neither can any purchase after it.

## 0. Invariants

A pass that touches the pack's cap, Floor Expansion or any price must keep all of these. Each
gets a test in `Core.Tests` when it is built.

1. **Every price fits the pack cap that's reachable when the price opens.** Defined precisely in
   §1. It replaces `CrateTests.EveryPriceInTheGame_IsPayableFromAFullPack` ("every price fits a
   full 100 pack"). The reachable cap counts **the pack alone**: the Post is optional, so no
   price assumes it, as §10.1.8 kept for the clamp.
2. **Caps only grow in play.** An upgrade sets the pack's cap to its rung's number, and never
   below the cap the buffer already has (an uncapped test fixture stays uncapped). The top rung
   offers nothing. A load restores the saved rung exactly. A save from before an upgrade puts
   back the smaller cap, as a load puts back fewer floor rows.
3. **Above the cap, the existing rule holds** (`crates-design.md` §0.3): a good above its cap can
   be spent or taken out, and nothing more can be added until it's under the cap. `RoomFor`
   answers 0 there. A load never clamps (§0.2): contents restore through `RestoreQuantity`.
4. **No goods are lost.** An upgrade charges its price and changes one number. A refused charge
   takes nothing.
5. **Costs come from the wallet**: `PlayerWallet.TryCharge`, the pack first, then the Post. The
   upgrade and each expansion charge through it. An expansion that adds no rows still hands back
   through `Uncharge`.
6. **Demolition is still a full refund**, into the pack only (`crates-design.md` §0.8). A bigger
   pack only means fewer refusals. `RoomFor` reads the live cap.
7. **Crates stay at 50 and the Post at 200.** The upgrade grows the pack alone. A crate's size is
   what makes layout matter (Phase 1's "Done when", `LayoutMattersTests`), and the Post is a
   factory store that golems fill, not the player's hands. Growing either one is its own call
   (§7 Q2).
8. **Priced in existing goods; no currency.** No new good, no new tier (roadmap decision 1), no
   meter (patents stay free, with no Focus). The factory pays in what it makes.
9. **Rules in Core, on `SandboxWorld`** (`SandboxWorld.PackUpgrade`), as plain classes that a
   test drives without a scene. Nodes only draw and forward clicks. A scenario covers the real
   tab (§5).
10. **Append-only, and no renames.** No new `StallReason`, `AppendageActionType` or
    `InteractionKind`. No step `Id` and no definition name is renamed. A new step is inserted
    with a new `Id`.
11. **A price shows before it can be paid.** A next price naming a good the Ledger has never seen
    reads `Requires: Brass`, greyed, as the Assembly Line greys a locked card (progression §8).
    Nothing is hidden, so the player always sees what the next room costs. Text stays in the
    font's glyphs (`font-glyphs`).

## 1. "Reachable", precisely

The reference path is the guide's steps in order, completed by the playtest kit (`Performs`),
from a fresh game. `PlaytestKitTests` already drives that path.

- **Seen(s)**: the goods `TechTreeProgressLedger.HasItem` answers true for after step *s*. This is
  the same signal that lets the Assembly Line offer a card priced in a good.
- **Opens(P)**, for a price *P*: the first step *s* where Seen(*s*) holds every good *P* names.
  If no step does (Mechanism, Regulator, Copper Wire and the Tier 5 goods are never made on the
  guide's path), *P* opens after the last step.
- **The ladder**: rungs 1…*n*, rung *k* with price *U<sub>k</sub>* and cap *C<sub>k</sub>*.
  *C<sub>0</sub>* is the base, `packCapacityPerType` (100). Rungs open in order: Opens(*U<sub>k</sub>*)
  never comes before Opens(*U<sub>k−1</sub>*).
- **Reachable(P)** = *C<sub>j</sub>*, for the largest *j* where every rung *i* ≤ *j* has
  Opens(*U<sub>i</sub>*) ≤ Opens(*P*). A rung's own reachable cap counts only the rungs below it.
  Ties count: a rung that opens at the same step as *P* can be bought first.
- **The invariant**: for every price *P* and every good *g*, the sum of *g*'s lines in *P* is no
  more than Reachable(*P*). Summed per good, as `TryCharge` sums them.

The prices are the old test's list (placeables, chassis, every deck card's opening price,
truckloads, the bay upgrade, every Floor Expansion purchase) plus every rung. Clock Tower stages
aren't on the list: golems deliver them, not the wallet.

**Off the path.** A player who makes goods in another order can meet a price before its rung.
They're never stuck: every rung is priced in goods their recipes still make, so the price waits
on more making, never on something impossible. The test pins the path the guide teaches.

## 2. How the player gets one

**Recommended: a Pack row on the Assembly Line tab**, beside the bays' Upgrade and Floor
Expansion's Extend. Those are the game's two "buy more capacity with goods" verbs, and the guide
already teaches both there in chapter 7. The row reads
`Pack · carries 100 of each good · +50` and has an **Upgrade** button. One click buys one rung
through the wallet, and the cap line on the Pack and Crate screens follows
(`CrateScreenReadout.PackCapLine` already reads the live cap). At the top rung the row reads
`Pack · carries 250 of each good`, with no button.

Alternatives, briefly:

- **A Ledger milestone that grants the cap for free** when the factory first makes a good (Gear,
  Brass, Mainspring). It's the coziest option, but the pack then never costs anything and gives
  no good a use. Upgrades would be "necessary" only in name.
- **A unique Assembly Line card** ("Pack Straps"), claimed with goods. That reuses claim pricing
  and gating, but the deck holds program parts that land in the Workbench vault. A card that
  isn't a program part muddles the vault and the "a slot never offers a card you own" rule.

## 3. The cap ladder

| Rung | Cap | Price | Opens at | Fits (reachable) |
|---|---|---|---|---|
| 0 | 100 | (start) | | |
| 1 | 150 | 60 Iron Plate + 30 Gear | `gears` (ch. 2) | 60 ≤ 100 |
| 2 | 200 | 40 Iron Plate + 20 Brass | `brass` (ch. 8) | 40 ≤ 150 |
| 3 | 250 | 60 Iron Plate + 10 Casing + 4 Mainspring | `mainspring` (ch. 8) | 60 ≤ 200 |

Every rung is +50. Each rung is priced in goods from no later than the purchase that needs it
(§4). Each rung keeps Iron Plate as its bulk line, since the factory never stops making it. There
is no rung after 250: no campaign price needs one (§7 Q4).

## 4. Floor Expansion's new curve

The price becomes an authored list of six bundles, one per purchase (`FloorBounds` allows +12
rows, at 2 per purchase), replacing `scrapPerRow × rowsPerPurchase × step`. The raw value still
climbs about 120 units a purchase, as today's curve does. It's counted in raw goods: Plate 1,
Gear 2, an ingot 3, Brass 9, Casing 13, Lens 11, Mainspring 31, Aether Cell 23; Glass counts 1,
since it's made from Slag. The bulk line stays a cheap good, and the advanced goods come in
small counts, so the cap bites on the bulk and the advanced goods get a use.

| # | Price | Raw | Largest line | Opens at | Rungs assumed |
|---|---|---|---|---|---|
| 1 | 80 Scrap + 40 Iron Plate | 120 | 80 | `iron` (ch. 1) | none (100) |
| 2 | 120 Scrap + 60 Iron Plate + 30 Gear | 240 | 120 | `gears` (ch. 2) | 1 (150) |
| 3 | 150 Iron Plate + 40 Gear + 50 Copper Ingot | 380 | 150 | `copper-ingot` (ch. 8) | 1 (150) |
| 4 | 200 Iron Plate + 20 Brass + 10 Casing | 510 | 200 | `casing` (ch. 8) | 1–2 (200) |
| 5 | 200 Glass + 20 Lens + 15 Casing | 615 | 200 | `lens` (ch. 8) | 1–2 (200) |
| 6 | 250 Iron Plate + 6 Mainspring + 4 Aether Cell + 15 Casing | 723 | 250 | `aether-cell` (ch. 8) | 1–3 (250) |

Purchase 1 is unchanged, so chapter 7's `expand` step, its text and its kit action stand.
Purchase 5 is the Slag line's payoff: 200 Glass is 200 smelts' worth of Slag that didn't go to
the heap. Purchase 4 needs rung 2 although it opens at `casing`, after rung 2's `brass`.
Purchase 5 opens at `lens`, before rung 3's `mainspring`, so it may assume only 200, and it asks
only 200.

## 5. What changes, system by system

| System | Today | With pack upgrades |
|---|---|---|
| `FloorExpansionService.NextCost` | `scrapPerRow`, `ironPlatePerRow` × `rowsPerPurchase` × step, clamped by `AtMostAPack` | the bundle at index *purchases made*; clamp deleted. `ConfigureCost(purchases, rows)` |
| `sandbox.json` `floorExpansion` / `SandboxSetup.Expansion` | `scrapPerRow` 40, `ironPlatePerRow` 20, `rowsPerPurchase` 2 | `rowsPerPurchase` 2, `purchases` (six bundles, §4). The old keys go in the same change |
| `sandbox.json` / `SandboxSetup` | `packCapacityPerType` 100 | plus `packUpgrades`: `[{ "capacityPerType": 150, "cost": [...] }, …]` (§3) |
| `PackUpgradeService` (new, `Core/Economy`) | | on `SandboxWorld.PackUpgrade`: `Tier`, `CapacityPerType`, `NextCost`, `NextCapacity`, `IsMaxed`, `TryUpgrade()` (wallet charge, then `SetCapacity` on the pack, never lower), `Restore(tier)`, `LastStatusMessage` |
| `SandboxSetup.ApplyBufferPolicy` | sets the pack to 100 | unchanged: the base. `PackUpgrade` applies its rung on top |
| `AssemblyLineBoard` / `AssemblyLineTab` | bay row, Extend row; the tab dispatches on `ButtonLabel` | plus the Pack row (§2). Rows dispatch by row kind, since "Upgrade" now names two buttons. A locked next price (Extend or Pack) reads `Requires: <good>` |
| `TechTreeCatalog` | `bldg.floorexpansion` detail "60 Scrap + 30 Iron Plate per 6×6 block" (stale) | detail "+2 rows · six purchases, priced up the tiers". New node `pack.upgrades`, "A Bigger Pack", "Carries 100 · up to 250 in three upgrades", `Technique`, column 1, row 8, signal `None`, prerequisite `r8.gearcutting` |
| Save (`ProgressEntry`) | `floorNorthExtent`, `assemblyBayTier` | plus `packUpgradeTier` (absent = 0 = 100). No `SaveData.version` bump. Restored with the other progress, and the order against the buffers doesn't matter, since nothing clamps |
| Guide | chapter 7: `expand`, `bays` | plus step `pack` after `bays` (§6) |
| Playtest script | "Your pack: 100 of each good"; "Floor expansion: +2 rows, rising cost, capped at +12" | "100, then 150, 200, 250 by upgrade"; "+2 rows, six purchases, from Scrap and Plate up to Casing and Mainspring" |

**Tests** (`Core.Tests`):

- `PackUpgradeTests`: a rung raises the cap and charges the pack, then the Post. A short wallet
  is refused and nothing moves. The top rung refuses. An upgrade never lowers an uncapped
  buffer. A load of a lower tier keeps above-cap goods, spendable but not addable. The tier
  round-trips through `SaveLoadService`.
- `PriceReachTests.EveryPriceInTheGame_FitsThePackReachableWhenItOpens`: §1, computed by running
  the kit step by step and reading the Ledger after each step. It replaces the old full-pack
  test. The same file checks that the rungs open in order and that `purchases` has exactly as
  many bundles as `FloorBounds` allows.
- `FloorExpansionTests`: purchase *n* charges bundle *n*, unclamped. A purchase that adds no
  rows hands everything back.
- `PlaytestKitTests` (existing) fails until `pack` has a kit action.

**Scenarios**: `management` clicks the Pack row's Upgrade with real mouse events and checks that
the Pack screen's cap line reads 150. It then extends twice, which needs the upgrade, with the
pack holding 120 Scrap. `save` saves at tier 1, upgrades, loads, and checks for tier 1 again with
nothing clamped. `playtest-kit` still passes all ten chapters.

## 6. The guide and the playtest

**Step `pack`** ("A bigger pack"), chapter 7, after `bays`: "Your pack carries 100 of each good.
On the same tab, click Upgrade on the pack (60 Iron Plate + 30 Gear): 150 of each. The next
extension needs it." Done when `_world.PackUpgrade.Tier >= 1`. Progress
`Counts((IronPlate, 60), (Gear, 30))`. Kit action: `GrantCost` of rung 1, then the board's
upgrade, the same verb as `bays`. A save keeps the step by its `Id`, so inserting it moves no
other step.

**Questions** (`PlaytestQuestion`, asked as the next step opens):

| Asked at step | Question |
|---|---|
| `ledger` (after `pack`) | The pack upgrade: did you need the room, and was it easy to find? (Needed it · Nice to have · Didn't notice it) |
| `frame-section` | Later extensions cost Brass, Casing, Glass and Mainsprings. Was spending them on room worth it? (Worth it · Too dear · Never got that far) |

## 7. Open questions: decided (user, 2026-10-18)

Each was decided as recommended.

1. **Should a price assume the Post?** Recommendation: no. The Post is optional, and a price
   that needs pack plus Post is unpayable for a player who skipped it.
2. **Should the Post's 200 grow too?** Recommendation: no, not with the pack. If the Post feels
   small in play, it gets its own upgrade in the tuning pass.
3. **A guide step for the first upgrade?** Recommendation: yes, `pack` in chapter 7 (§6). Without
   it, the second extension's price is the first time the player meets the cap as a wall.
4. **A rung past 250 for endless play?** Recommendation: not now. No campaign price needs it.
   Phase 8's endless play can add a rung priced in Mechanisms and Regulators, which would give
   Tier 4 a sink.
5. **Show a locked next price, or hide it?** Recommendation: show it greyed with
   `Requires: Brass` (§0.11). A hidden price is a surprise, and a shown one is a goal.
6. **The numbers.** §3 and §4 are a first cut for Phase 3's spreadsheet. The invariant (§1) is
   what's settled. Recommendation: tune the quantities freely, and keep the shape: bulk in cheap
   goods, a few advanced goods, and caps in steps of 50.

## 8. Corrections from building it (2026-10-18)

Where the code differs from the spec above, and why.

1. **The row's separators are the tab's**: `Pack  ·  carries 100 of each good  ·  +50`, two
   spaces either side of the dot, as the bay and Extend rows write theirs.
2. **A locked price names every good not yet made**, joined by commas. In a fresh game the first
   rung reads `Requires: Iron Plate, Gear`, since neither has been made.
3. **The cap line was drawn once.** `CrateScreenReadout.PackCapLine` reads the live cap, but the
   Pack and Crate screens set it only when they were built, so an upgrade didn't show until a
   restart. Both screens now set it on every refresh.
4. **The `management` scenario upgrades first, then extends twice** (purchases 1 and 2), in place
   of its single Extend. It opens the Pack screen with `PackScreen.Open()`, as it opens the
   Workbench, rather than by `[I]`, which depends on where the player stands. `save` also checks
   the rung, as §5 says.
5. **The questions' ids are `pack-upgrade` and `dear-room`.** The doc named only their steps.
6. **Seen(s) is read with a poll.** The Ledger polls buffers once a second, so the test calls
   `TechTreeProgressTracker.Poll` every frame. The kit performs the next step in the same
   `Update` that finishes one, so a step the world finished mid-frame is credited with the
   Ledger from before that frame. The measured "Opens at" column matches §3 and §4 exactly.
7. **`FloorExpansionService` has no built-in prices.** Unconfigured, an extension is free.
   `FloorExpansionTests` configure it from `sandbox.json`. `PurchasesMade` is new: rows grown
   past the authored room, a purchase's worth at a time.
8. **An expansion that adds no rows can't happen.** `TryPurchaseExpansion` refuses at the limit
   before it charges, and `FloorBounds.Expand` adds at least one row whenever it can expand. The
   `Uncharge` branch stays, unreachable, and the test pins the refusal instead: a purchase at the
   limit takes nothing.
9. **`Restore` leaves an uncapped pack uncapped**, as `TryUpgrade` does (§0.2), so a test
   fixture's unlimited pack survives a load.
10. **Tech tree**: the new node `pack.upgrades` makes the Ledger 47 nodes.

### From the critique (2026-10-18)

11. **The Assembly Line header showed an internal id and ignored the Post.** It read
    `Wallet: 100 Scrap (FactoryStockpile)`, while a claim spends the pack, then the Post. It now
    reads `Pack: 100 Scrap`, and `Pack: 100 Scrap  ·  Post: 40 Scrap` once a Post has been built
    (the Ledger) or the Post's store holds Scrap (`AssemblyLineBoard.WalletLine`). The `management`
    scenario puts 40 Scrap in the Post and checks the drawn header.
12. **The Inventory tab showed ids too**: headers `FactoryStockpile`, `PneumaticPost`,
    `Crate(3,-2)`, and goods as `IronPlate`. Headers now read `Your pack`, `Pneumatic Post`,
    `Crate (3,-2)`, `Freight Mast (5,4)` (`InventoryReadout.Title`), and goods their display
    names. The row keeps the id in `BufferId`. `management` checks that no drawn label holds an id.
13. **`sandbox.json` isn't read by the strict `DefinitionLoader`**, and the wallet skips a line
    with an empty good. A blank or misspelt good in a rung or a floor bundle would have opened
    "after the guide", fit the top cap, and passed `PriceReachTests`. The test now also requires
    every line of every price to name a known good (`ItemTiers.IsKnown`) with a positive
    quantity. A free card (`baseCost` 0) counts as an empty price, not a line of 0 Scrap.

### Lead's call (2026-10-18)

14. **A price the pack can't hold says so.** Floor Expansion #2 asks 120 Scrap; at cap 100 with
    no Post it read `Need 20 more Scrap`, which no amount of gathering fixes. Where a line exceeds
    the pack's cap and the wallet (pack + Post) can't cover it, the shortfall reads
    `Upgrade your pack: this needs 120 Scrap, your pack holds 100`, followed by
    `  ·  Need …` for any other short good. It lives in `ConstructionCostPolicy.FormatShortfall`,
    which every priced thing calls; a caller with a wallet passes the wallet, so it reads the cap.
15. **The Management tabs read `Assembly Line` and `Save / Load`**, not Unity's `AssemblyLine`
    and `SaveLoad`, matching the docs. Nothing finds a tab by its label; the buttons are kept by
    `ManagementTab`.
