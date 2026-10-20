# Tuning data spec: every number, where it lives, and what pins it

**Status: approved by the user on 2026-10-19, with §17's decisions (question 1 replaced by the
user's own call; 2–8 as recommended).** Phase 3 of `docs/roadmap.md` ("Tuning and
a full playthrough"). Like `crates-design.md` and `pack-upgrades-design.md`, it is written before
the code and kept as written afterwards, with corrections recorded rather than hidden. Its
siblings:

- `testscript/full-playthrough.md`: the human run of chapters 1–10 that judges it.
- `docs/performance-report-template.md`: the endgame profile, the `perf` scenario and its budget.
- `docs/progression-design.md` §14: the tuning decisions, recorded where the formulas live.
- `docs/onboarding-feedback-design.md` §17: the waiting golem's hover card names its feeder.

Units: **1 tick = 0.1 s** (`SimulationClock.TicksPerSecond` = 10). A price is a list of
`(good, quantity)` lines. "[M]" marks a number this spec could not measure from the code: the
Code agent measures it and records it in §18 (corrections), and does not change any other number
to make it come out.

## 0. Invariants

Every change to a number in `godot/data/*.json` or a tuning constant in Core keeps these. Each
has a test in `Core.Tests`; §15 names it.

1. **Every price fits the pack reachable when it opens** (`pack-upgrades-design.md` §0.1, §1),
   counting the pack alone, never the Post. Unchanged; this spec adds the bench's recipes and the
   boiler's hand-load to the list of prices (`open-items.md` §4).
2. **A price's ceiling is the reachable pack, and late prices use it** (user, 2026-10-19,
   replacing a flat "no line above 100"). Invariant 1 is the only ceiling: a line may go up to the
   cap reachable when its price opens (100, then 150, 200, 250). Prices that open after a rung
   should use some of that room, so each upgrade is worth buying; §8.1 raises them. Two limits:
   nothing that opens in chapters 1–4 is raised, and a price **on the guide's path** (a step's
   kit pays it) asks no more than **150** in a line, the rung chapter 7's `pack` step teaches;
   rungs 2 and 3 are bought by the player's own choice, for Floor Expansion. And **every rung
   has at least one price that needs it** (§15.3). A line is one good of one price, summed per
   good as `TryCharge` sums them.
3. **Every good a step asks for is makeable when the step opens.** For each guide step, every
   good its kit action grants (that is what the step needs the player to have) and every good of
   the card it names is makeable from the goods the Ledger has seen before the step, the stalls,
   and the recipes whose cards are claimable by then on a chassis the player can build (or at the
   bench). Defined precisely in §15.3.
4. **The Lens costs 3 Glass** (R10: 3 Glass + 1 Brass → 1 Lens), **and stage 3 still balances**:
   stage 3's Slag deficit at 1× demand stays within ±10 Slag/min of the deficit it had at 2 Glass
   (38/min), so the §5.3(c) flip from voiding to over-smelting still happens there and only there
   (§11). Roadmap decision 8.
5. **Chapters 1–4 stay within their budgets.** The game-time floor (`ChaptersOneToFourFloorTests`)
   stays under 10:00 in total and under each chapter's ceiling (§14): two thirds of the chapter's
   budget in `guide-chapters-design.md` §1.
6. **Chapters 5–10 stay within their ceilings, and no step is stuck.** A new floor test plays
   every chapter on the world's clock, earning every good, and fails above a chapter's ceiling or
   when a step does not complete (§14, §15.4).
7. **One coker feeds one full boiler.** The guide's coker program (Extract, Assemble Coking,
   Push) makes at least `MaxGolemsPerBoiler × CokePerMinutePerGolem` Coke a minute: 27.3 ≥ 8 × 3.
   A retune of the burn or of R1 that breaks it makes the coal line a death spiral at the scale
   the guide teaches.
8. **Claim prices follow their formula** (§9): a recipe card asks 4 × its recipe's inputs; a
   chassis card asks max(1, ⌊q / 4⌋) of each line of its chassis's price. A recipe or chassis
   retune moves its card with it.
9. **A Repeat can always finish** (§13): the Workbench and ENGAGE refuse a `Repeat(n)` whose
   n + 1 runs could not fit the 12-per-good hold, so a Repeat never jams its golem for good.
10. **The settled calls hold** (root `CLAUDE.md`): demolition refunds in full; patents are free and
    there is no Focus meter; golems run a fixed order and stall rather than adapt; enums are
    append-only (this spec adds none); rules live in Core and nodes only draw; a new system
    attaches to `SandboxWorld` and comes with a scenario.
11. **No renamed definition or step `Id`.** A changed price, text or recipe input is a data edit;
    names stay, so saves still resolve.
12. **Every quoted price is the data's.** A guide text or `Counts` line that names a price builds it
    from the definition (§12.3), so a retune cannot leave the guide quoting the old number.

## 1. How to read the tables

Columns: **Tunable**, **Lives in** (file and key, or Core constant), **Current** (read from the
data and code on 2026-10-19), **Formula** (from `progression-design.md` §2 or the system's own
rule), **Proposed**, **Why**. "Keep" means no change. Every proposed change is also in §16's
list, in the order to make them.

## 2. The formulas

As built, which differs from `progression-design.md` §2 only where §14 of that doc says so.

| Quantity | Formula | Lives in |
|---|---|---|
| Haul step | `max(2, q)` ticks, q = batch 1–12 | `Golems/StepDurationRules.Haul` |
| Extract step | `6 + q` ticks | `StepDurationRules.ExtractFromNode` |
| Assemble step | the recipe's `durationTicks` | `GolemEntity.BeginAssemble` |
| Repeat(n) | n × the repeated recipe's `durationTicks` | `StepDurationRules.Repeat` |
| Push step | `2 + units pushed` ticks | `GolemEntity` (Push) |
| Freight Launch | 24 ticks, flat | `GolemEntity` (FreightLaunch) |
| Golem cycle | sum of its steps; rate/min = 600 × outputs ÷ cycle ticks | |
| Steam burn | 1 Coke per working golem per 200 ticks = **3 Coke/min** | `Steam/SteamNetwork.TicksPerCokePerPoweredGolem` |
| Crank time | `durationTicks × 100 / SpeedPercent` = 2 × the recipe | `Buildings/HandCrankRules.CrankTicks`, `SpeedPercent` = 50 |
| Hand harvest | 1 at once, then one per 0.15 s after 0.35 s held: ~6.7/s | `PlayerInteractor.HarvestRepeatDelay`, `HarvestRepeatInterval` |
| Claim decay | price × max(0.25, 1 − 0.01 × seconds on the line) | `AssemblyLineState`, card `decayFractionPerSecond`, `minCostFraction` |
| Tower progress | per tick, min over lines of clamp(effective ÷ demand, 0, 3), 0 if a meter is empty | `ClockTower/ClockTowerSite.Evaluate` |
| Supply meter | +1 per unit delivered, −demand/60 per second, clamped to [0, 60] | `ClockTower/SupplyPressureMeter` |
| Orders per minute | Σ over raw goods of raw demand ÷ truckload size (§4.2) | new, §15.5 |

**The guide's programs, as cycles** (the plans in `TutorialGuide.cs`):

| Golem | Program | Cycle | Rate |
|---|---|---|---|
| Scavenger | Extract(1), Push | 7 + 3 = 10 t | 60 Scrap/min |
| Iron Presser | Haul(1), R2, Push | 2 + 24 + 3 = 29 t | 20.7 Iron Plate/min |
| Coker | Extract(1), R1, Push | 7 + 12 + 3 = 22 t | 27.3 Coke/min |
| Smelter (Hauler) | Haul(Scrap 2), Haul(Coke 1), R4, Push | 2 + 2 + 24 + 5 = 33 t | 36.4 Iron Plate + 18.2 Slag/min |
| Casing (Hauler) | Haul(Plate 4), Haul(Brass 1), R9, Push | 4 + 2 + 28 + 3 = 37 t | 16.2 Casing/min |
| Frame Section (Overclocker) | Haul(Casing 10), Haul(Plate 6), Haul(Brass 4), R15, Push | 10 + 6 + 4 + 90 + 3 = 113 t | 5.3 Frame Section/min |

## 3. Steam: boiler ratios

`progression-design.md` §12 says tune this first; the playtest already did once (G10: 6 → 3
Coke/min, idle golems free).

| Tunable | Lives in | Current | Formula | Proposed | Why |
|---|---|---|---|---|---|
| Burn per working golem | `SteamNetwork.TicksPerCokePerPoweredGolem` | 200 ticks (3 Coke/min) | §3.1, halved in G10 | **keep** | Invariant 7 holds exactly at 3 (27.3 ≥ 27). At 4/min a guide coker no longer feeds a full boiler (27.3 < 36). The full playthrough asks (`full-playthrough.md`, `coal-share`) |
| Golems per boiler | `SteamNetwork.MaxGolemsPerBoiler` | 8 | §3.1 | **keep** | Invariant 7 |
| Boiler Coke capacity | `SteamBoiler.CokeCapacity` | 240 | §8's gauge reference | **keep** | 80 golem-minutes of runway; the gauge's 25 % alert reads off it |
| Boiler hand-load | `BoilerRefuelPolicy.HandLoadBatch` | 20 Coke | | **keep**; add to the price list (invariant 1) | It charges the wallet like a price |
| Boiler price | `placeables.json` `BoilerPrefab` cost | 30 Scrap + 10 Iron Plate | §3.1 | **keep** | Chapter 1's floor counts it; changing it moves `ChaptersOneToFourFloorTests` |
| Steam Pipe price | `placeables.json` `SteamPipePrefab` | 1 Iron Plate | §3.1 | **keep** | |
| Boiler starting Coke | `placeables.json` `PlaceableBoiler.startingCoke` | 0 | | **keep** | Chapter 1's `fuel` step teaches loading it |
| `SteamNetwork.BoilerScrapCost` etc. | `Steam/SteamNetwork.cs` constants | 30, 10, 1 | | **delete** if unread, else pin equal to the data [M: unread, deleted, §18 M1] | A second copy of a price that can drift. `grep` their readers first |

Coal-line share at stage 4 with 3/min: upkeep ~96 × 3 = 288 Coke/min (the design's 576 was at
6/min). The coal line falls from ~41 % to about a third of the factory [M: §18.3 B1: a third of the
endgame load's golems, but 41 % counted from the recipes]. That is the
playthrough's `coal-share` question, not a reason to raise the burn now.

## 4. The market: prices and truckloads

### 4.1 The finding

A stall holds what its truckloads brought, and every truckload is one [E] at the stall, one in
transit per stall (`TruckloadMarket.TryOrder`, `AlreadyInTransit`). Nothing reorders on its own.
At chapter 10's stage 1 at full demand (6 Frame Sections a minute), the factory eats about 276
Scrap, 411 Coal (222 smelting plus ~189 steam for ~63 golems), 112 Copper Ore and 56 Zinc Ore a
minute (§4.2). At today's sizes that is **about 24 orders a minute, one [E] every 2.5 seconds**,
walking between stalls four cells apart. The full playthrough would turn into a toll booth at
exactly the point the game should feel automated (`open-items.md` §1: "an economy or a toll
booth").

### 4.2 Orders per minute

```
RawDemand_g(stage) = Σ over the stage's demands d:  rate_d × raw_g(d)        // §7's raw table
                   + [g == Coal] × golems(stage) × CokePerMinutePerGolem      // steam
OrdersPerMinute(stage) = Σ over raw goods g:  RawDemand_g(stage) ÷ truckloadSize_g
```

`raw_g` back-propagates the recipes, with Iron Plate from R4 (1 Scrap + ½ Coal per Plate), Coke
as 1 Coal, Glass as free (it is Slag the smelters make anyway), and Brass from R7 (per Brass:
4/3 Copper Ore + 2/3 Zinc Ore + 1 Coal). Per Frame Section: 46 Scrap, 37 Coal, 18.7 Copper Ore,
9.3 Zinc Ore, as §7's table. `golems(stage 1)` = 63 (§7). **Target: OrdersPerMinute(stage 1) ≤ 8**,
one order every 7.5 s across the whole street.

### 4.3 The table

| Tunable | Lives in | Current | Proposed | Why |
|---|---|---|---|---|
| Scrap truckload | `sandbox.json` `market[ScrapNode, ScrapNodeWest]` | free, 30, 40 ticks | **free, 100, 60 ticks** | 276/30 = 9.2 orders/min → 2.8 |
| Coal truckload | `market[CoalNode, CoalNodeWest]` | 10 Scrap for 40, 60 ticks | **30 Scrap for 120, 90 ticks** | 411/40 = 10.3 → 3.4; the price per Coal is unchanged (¼ Scrap) |
| Copper Ore truckload | `market[CopperOreNode, CopperOreNodeEast]` | 20 Scrap for 40, 80 ticks | **60 Scrap for 120, 120 ticks** | 112/40 = 2.8 → 0.9; ½ Scrap per ore, unchanged |
| Zinc Ore truckload | `market[ZincOreNode, ZincOreNodeEast]` | 20 Scrap for 40, 80 ticks | **60 Scrap for 120, 120 ticks** | 56/40 = 1.4 → 0.5 |
| Aether truckload | `market[AetherNode]` | 40 Scrap for 20, 120 ticks | **80 Scrap for 40, 150 ticks** | Aether is small at every stage; doubled to keep its stall from being the one reordered every minute at stage 3–4 [M: 0.23 and 0.18 orders a minute, §18 M3]; 2 Scrap per crystal, unchanged |
| Free seed | `sandbox.json` `freeStallSeedNode`, `freeStallSeedQuantity` | ScrapNode, 60 | **keep** | Chapter 1's first 20 Scrap |

**Result:** OrdersPerMinute(stage 1) = 2.8 + 3.4 + 0.9 + 0.5 = **7.6** (from 23.7). Every new
price is ≤ 100 and opens at `scrap`, where the pack holds 100 (invariant 1). Truckloads open in
chapters 1–6, so they are not raised under invariant 2.

**What it costs.** Chapter 1's `coal` step now needs 30 Scrap, not 10: about 1.5 s more of
harvesting at 6.7/s, plus the 20 Scrap the player already holds from `scrap`. Chapter 3's
`coal-order` the same. A bigger lump is the burst §13.2 wanted: a crate of 50 can't take a
120-Coal truckload at once, which is the job buffers have.

**Texts that quote a price** change with it (§12.3): `coal` ("order a truckload (10 Scrap)"),
`coal-order` ("10 Scrap for 40 Coal"). Build them from the offer (invariant 12).

**If 7.6 orders a minute still reads as a toll booth** in the playthrough, the remedy is a system,
not a number: a standing order per stall. That is a user's call (§17 Q3), not part of this spec.

## 5. Caps

| Tunable | Lives in | Current | Proposed | Why |
|---|---|---|---|---|
| Pack, base | `sandbox.json` `packCapacityPerType` | 100 | **keep** | The cap every chapter 1–4 price fits |
| Pack, by upgrade | `sandbox.json` `packUpgrades[].capacityPerType` | 150, 200, 250 | **keep** | `pack-upgrades-design.md` §3 |
| Crate | `sandbox.json` `crateCapacityPerType` | 50 | **keep** | Phase 1's "Done when" (`LayoutMattersTests`) rests on it |
| Post | `sandbox.json` `postCapacityPerType` | 200 | **keep** | `pack-upgrades-design.md` §7 Q2: its own upgrade only if play asks |
| Golem stock, per good | `GolemInventory.CapacityPerType` | 12 | **keep** | The Haul dial's top (`WorkbenchQuantityPolicy.MaxQuantity`) and Repeat's bind (§13) |
| Assembly bays, start | `AssemblyBayStructure.DefaultSlots` | 10 | **keep** | |
| Bay upgrade | `AssemblyBayStructure.SlotsPerUpgrade`, `upgradeCost` | +6 for 40 Scrap + 20 Iron Plate, unlimited | **keep** | 96 golems = 15 upgrades = 600 Scrap + 300 Plate over the campaign, never more than 40 at once |
| Floor | `FloorLayout.HalfExtent`, `MaxNorthExtent`, `rowsPerPurchase` | 25 × 25, +12 rows, 2 a purchase | **keep** | |

## 6. Pack rungs and Floor Expansion

`pack-upgrades-design.md` §3 and §4, in `sandbox.json` `packUpgrades` and
`floorExpansion.purchases`. Raw value counts Plate 1, Gear 2, an ingot 3, Brass 9, Casing 13, Lens
11 (12 at 3 Glass), Mainspring 31, Aether Cell 23 (25 at 3 Glass), Glass 1.

| # | Current | Opens at | Proposed | Why |
|---|---|---|---|---|
| Rung 1 (150) | 60 Iron Plate + 30 Gear | `gears` | **keep** | |
| Rung 2 (200) | 40 Iron Plate + 20 Brass | `brass` | **keep** | |
| Rung 3 (250) | 60 Iron Plate + 10 Casing + 4 Mainspring | `mainspring` | **keep** | |
| Floor 1 | 80 Scrap + 40 Iron Plate | `iron` | **keep** | Chapter 7's `expand` |
| Floor 2 | 120 Scrap + 60 Iron Plate + 30 Gear | `gears` | **keep** | |
| Floor 3 | 150 Iron Plate + 40 Gear + 50 Copper Ingot | `copper-ingot` | 150 Iron Plate + **80 Gear** + 50 Copper Ingot | §8.1: reachable 150; Gear is cut from plentiful Plate |
| Floor 4 | 200 Iron Plate + 20 Brass + 10 Casing | `casing` | 200 Iron Plate + 20 Brass + **20 Casing** | §8.1: reachable 200 |
| Floor 5 | 200 Glass + 20 Lens + 15 Casing | `lens` | **180 Glass** + 20 Lens + 15 Casing | At 3 Glass a Lens, 20 Lens are 60 Glass, not 40: 180 keeps the purchase's Glass at 240, as it was |
| Floor 6 | 250 Iron Plate + 6 Mainspring + 4 Aether Cell + 15 Casing | `aether-cell` | 250 Iron Plate + 6 Mainspring + 4 Aether Cell + **30 Casing** | §8.1: reachable 250 |

The roadmap calls these "authored, not felt". The human playtest of `testscript/phase-1-playtest.md`
and the full playthrough's `dear-room` and `pack-upgrade` answers are what move them; this spec
has no measurement that argues for a change beyond Floor 5.

## 7. Recipes

`godot/data/recipes.json`, keys `inputs`, `outputItemType`, `outputQuantity`, `byproductItemType`,
`byproductQuantity`, `durationTicks`. Every row is `progression-design.md` §5.2's; the formula is
§2's cycle time.

| Recipe | Inputs → output (+ byproduct) | Ticks | Proposed | Why |
|---|---|---|---|---|
| R1 Coking | 1 Coal → 1 Coke | 12 | keep | Invariant 7 |
| R2 Scrap Reclamation | 1 Scrap → 1 Iron Plate | 24 | keep | Chapter 2's crank time is 2 × 24; the Presser's price is the lever (§12) |
| R3 Glassmaking | 1 Slag → 1 Glass | 20 | keep | |
| R8 Gear Cutting | 2 Iron Plate → 1 Gear | 16 | keep | |
| R19 Wire Drawing | 1 Copper Ingot → 3 Copper Wire | 18 | keep | |
| R4 Iron Smelting | 2 Scrap + 1 Coke → 2 Iron Plate + 1 Slag | 24 | keep | |
| R5 Copper Smelting | 2 Copper Ore + 1 Coke → 1 Copper Ingot | 30 | keep | |
| R6 Zinc Smelting | 2 Zinc Ore + 1 Coke → 1 Zinc Ingot | 30 | keep | |
| R7 Brass Alloying | 2 Copper Ingot + 1 Zinc Ingot → 3 Brass | 30 | keep | |
| R9 Casing Press | 4 Iron Plate + 1 Brass → 1 Casing | 28 | keep | |
| **R10 Lens Grinding** | **2 Glass** + 1 Brass → 1 Lens | 24 | **3 Glass** + 1 Brass → 1 Lens | Decision 8; §11 |
| R11 Mainspring Winding | 3 Brass + 2 Gear → 1 Mainspring | 40 | keep | |
| R12 Aether Containment | 1 Aether + 2 Lens → 1 Aether Cell | 36 | keep | |
| R13 Mechanism Assembly | 1 Mainspring + 3 Gear + 1 Casing → 1 Mechanism | 60 | keep | |
| R15 Frame Section | 10 Casing + 6 Iron Plate + 4 Brass → 1 Frame Section | 90 | keep | |
| R16 Great Cog | 4 Mechanism + 6 Brass + 2 Mainspring → 1 Great Cog | 100 | keep | |
| R18 Aether Conduit | 3 Aether Cell + 4 Brass + 6 Copper Wire → 1 Aether Conduit | 80 | keep | |
| R14 Regulator | 1 Aether Cell + 2 Brass + 1 Gear + 4 Copper Wire → 1 Regulator | 56 | keep | |
| R17 Chronometer Core | 2 Regulator + 2 Mechanism + 2 Lens + 2 Copper Wire → 1 Chronometer Core | 120 | keep | |

The appendage cards' `durationTicks` in `appendages.json` (`AssembleLensGrinding` 24, …) are
not read for timing (`BeginAssemble` takes the recipe's), so they need no edit; keep them equal
to the recipe's anyway.

Everything that quotes R10's inputs changes with it [M: two quotes, §18 M4]:
the `lens` goal step's recipe line (`"2 Glass + 1 Brass → 1 Lens"`), `TechTreeCatalog`'s node
detail if it quotes it, and `RecipeCatalogTests` only if it pins quantities (it pins shapes:
R10 stays 2 inputs, 4 slots).

## 8. Chassis prices

`godot/data/chassis.json` `cost`. Slots and flags are `progression-design.md` §2 and §6.

| Chassis | Slots | Current | Proposed | Why |
|---|---|---|---|---|
| Clockwork Scavenger | 2 | 12 Scrap | keep | |
| **Brass Presser** | 3 | 60 Scrap + **20 Iron Plate + 10 Gear** | 60 Scrap + **10 Iron Plate + 5 Gear** | §12: chapter 2's floor is 4:15 of 4:30 |
| Aether-Hauler | 4 | 80 Iron Plate + 40 Gear + 30 Coke | keep; **20 Iron Plate + 10 Gear + 30 Coke** after the code phase | §18 correction 10: chapter 8 over its ceiling |
| Mainspring Overclocker | 5 | 2 Mainspring + 20 Brass + 24 Casing + 12 Gear | **+ 120 Iron Plate** | §8.1, §13 |
| Zeppelin Freight Loader | 6 | 6 Mainspring + 8 Lens + 3 Aether Cell + 30 Casing + 40 Brass | **+ 150 Iron Plate** | §8.1. At 3 Glass its 8 Lens are 24 Glass, not 16; §11 shows the Glass is there by chapter 9 |

`ChassisCostAcyclicityTests` stays true: the Presser still costs only hand-made goods, and Iron
Plate is Presser output, so the Overclocker and Zeppelin still cost only Hauler-tier goods.

### 8.1 Late prices that use the pack's room (invariant 2)

The user's call (2026-10-19): "some costs could be higher if they're expected to come later when
we'd expect the player to upgrade their limits." Every price in chapters 5–10 was checked against
the cap reachable when it opens (`pack-upgrades-design.md` §1: rung 1 opens at `gears`, rungs 2
and 3 at `brass` and `mainspring`).

**The raise goes into Iron Plate, Gear and Casing**, the goods the factory makes in bulk by then
(Iron Plate is §5.3(e)'s "universal filler"). The scarce goods (Mainspring, Lens, Aether Cell,
Brass) keep their counts: raising them would lengthen chapters 8–10 by whole production lines, not
by minutes. **Guide-path prices stop at 150** (invariant 2): the guide teaches rung 1 only, and a
chassis that needed rung 2 would put an untaught purchase in front of a step.

| Price | Old | New | Opens at | Reachable | On the guide's path |
|---|---|---|---|---|---|
| Mainspring Overclocker | 2 Mainspring + 20 Brass + 24 Casing + 12 Gear | the same **+ 120 Iron Plate** | `mainspring` (ch. 8) | 250 | yes (two for `stage1`) |
| `Card_MainspringOverclocker` | 1 Mainspring + 5 Brass + 6 Casing + 3 Gear | the same **+ 30 Iron Plate** | `mainspring` | 250 | no (optional claim) |
| Zeppelin Freight Loader | 6 Mainspring + 8 Lens + 3 Aether Cell + 30 Casing + 40 Brass | the same **+ 150 Iron Plate** | `aether-cell` (ch. 8) | 250 | yes (`zeppelin`) |
| `Card_ZeppelinFreightLoader` | 1 Mainspring + 2 Lens + 1 Aether Cell + 7 Casing + 10 Brass | the same **+ 37 Iron Plate** | `aether-cell` | 250 | yes (`zeppelin-card`) |
| Freight Mast | 20 Brass + 10 Casing | the same **+ 120 Iron Plate** | `casing` (ch. 8) | 200 | yes (`mast`) |
| Pneumatic Post crate | 10 Brass + 5 Casing | the same **+ 40 Iron Plate** | `casing` | 200 | yes, ×2 (`post`): 80 Plate in all |
| Floor 3 | 150 Iron Plate + 40 Gear + 50 Copper Ingot | 40 → **80 Gear** | `copper-ingot` | 150 | no |
| Floor 4 | 200 Iron Plate + 20 Brass + 10 Casing | 10 → **20 Casing** | `casing` | 200 | no |
| Floor 6 | 250 Iron Plate + 6 Mainspring + 4 Aether Cell + 15 Casing | 15 → **30 Casing** | `aether-cell` | 250 | no |

Cards follow the claim formula (invariant 8): max(1, ⌊q/4⌋) of each chassis line, so 120 Plate is
30 and 150 is 37. The Post's two crates are under 100 a crate, but charged one at a time they
never need a rung; the Post is a convenience and stays cheap.

**Left as they are, and why:**

- **Everything opening in chapters 1–4**, including the Aether-Hauler, the Slag Heap and the Scrap
  Recycler (all priced in goods seen by chapter 3), the boiler, the depot, belts and pipes.
- **Recipe cards.** They follow 4 × the recipe's inputs (invariant 8), and one formula covers
  chapters 1–10; raising late cards alone would need a second multiplier. A claim also decays to a
  quarter in 75 s, so a dearer claim mostly means a longer wait. Recommended: unchanged.
- **Bench recipes.** Their "price" is one craft's inputs (1–2 units); the bench is a fallback, not
  a sink.
- **The pack rungs.** They stay inside the cap before each rung (rung 2's 40 Plate ≤ 150, rung 3's
  60 ≤ 200): a dearer upgrade is the opposite of a reason to buy one.
- **The Construction Station placeable** (25 Scrap + 5 Brass): not on the guide's path, opens at
  `brass`; left as authored, since a second station is a layout tool, not a sink.

**Which price needs which rung**, after the change: rung 1 (150): Floor 2 (120 Scrap), Floor 3
(150 Plate), the Overclocker (120), the Zeppelin (150), the Freight Mast (120). Rung 2 (200):
Floor 4 (200 Plate), Floor 5 (180 Glass). Rung 3 (250): Floor 6 (250 Plate). Every rung has a
reason (§15.3).

**What it costs in time** [M: chapters 9 and 10 measure 9:34 and 24:49, §18 M5]: 450 more Iron Plate on the guide's path (Post 80, Mast 120,
Zeppelin 150 in chapters 8–9; two Overclockers 240 in chapter 10, though the floor test builds one
reference line). At chapter 8's ~57 Plate a minute (one smelter and the iron Presser) that is
about 4 minutes in chapter 9 and 4 in chapter 10, inside their ceilings of 20:00 and 30:00. If
`FullRunFloorTests` puts a chapter over its ceiling, the Plate lines come down first, Mast before
Zeppelin before Overclocker.

## 9. Assembly Line card prices

`godot/data/assembly_line_cards.json` `claimCost`, plus the decay keys. The authored prices all
follow one rule today, which this spec makes an invariant (invariant 8):

```
recipe card:   claimCost = 4 × each input of its recipe            (R1: 4 Coal; R15: 40 Casing + 24 Plate + 16 Brass)
chassis card:  claimCost = max(1, ⌊q / 4⌋) for each line q of its chassis's cost
free cards (Extract, Haul, Push, Repeat, Freight Launch): claimCost [], baseCost 0
```

| Card | Current | Proposed | Why |
|---|---|---|---|
| `Card_R10_LensGrinding` | 8 Glass + 4 Brass | **12 Glass** + 4 Brass | The formula, at 3 Glass |
| `Card_BrassPresser` | 15 Scrap + 5 Iron Plate + 2 Gear | 15 Scrap + **2 Iron Plate + 1 Gear** | The formula, at the new Presser price |
| `Card_RepeatAssembly` | `prerequisiteItemProduced` "" | **"Mainspring"** | A free, cycling card no chassis before the Overclocker can hold; today it is the deck's fourth card, so it is the first refill into a slot from chapter 2 on, offering nothing [M: it showed through chapter 1, §18 M6] |
| Every other card | | keep | They follow the formula |
| Decay | `decayFractionPerSecond` 0.01, `minCostFraction` 0.25 (all cards) | keep | A card reaches its floor after 75 s on the line |
| `baseCost`, `decayPerSecond`, `minCost` | 20, 1, 2 (legacy Scrap price) | keep | Unread where `claimCost` is set (`HasBundleCost`) |

## 10. The Clock Tower

`godot/data/clock_tower_stages.json`, `demands[].ratePerMinute` and `nominalSeconds`.

| Stage | Current demands | Nominal | Proposed | Why |
|---|---|---|---|---|
| 1 Foundation | Frame Section 6/min | 360 s | keep | The guide's last step; see §10.1 |
| 2 The Movement | Great Cog 3, Frame Section 3 | 480 s | keep | |
| 3 Aether Illumination | Aether Conduit 3, **Lens 24**, Frame Section 2 | 480 s | Aether Conduit 3, **Lens 12**, Frame Section 2 | §11: keeps the stage's Slag balance at 3 Glass a Lens |
| 4 The Chronometer | Chronometer Core 2, Great Cog 2, **Aether Conduit 1** | 600 s | keep; **remove the Aether Conduit line** only under the rule in §10.2 | `progression-design.md` §12 weakness 1 |

### 10.1 Finding: under-supply slows a stage quadratically [M: confirmed and fixed, §18 M7]

The guide's `stage1` text says "Short of 6 a minute it still builds, just slower." Read from the
code, it builds much slower than that. A line's meter gains 1 a delivery and loses demand/60 a
second, so below demand it drains to 0 between deliveries, and a meter at 0 freezes progress
(`ClockTowerSite.Evaluate`). At a supply of *s* against a demand of *d*, the meter is above 0 for
about *s*/*d* of the time, and progress then runs at *s*/*d*:

```
stage time ≈ nominal × (d / s)²      for s < d        (at s = d / 6: 36 × nominal = 216 min for stage 1)
```

The design's §7 says progress "scales with delivered rate", which reads linear (nominal × d/s).
The Code agent measures it first (`ClockTowerPartialSupplyTests`, §15.6). If the measurement
confirms it, the meter decays at the delivered rate instead (§17, decision 4), in an addendum.

### 10.2 The stage 4 Conduit rule

Trim stage 4's Aether Conduit demand **only if** `docs/performance-report-template.md`'s `perf`
run fails the frame budget at 96 golems **after** the stall-facts probe is no longer the top Core
hot spot (the template's step 4). Then delete the `AetherConduit` line from
`Stage4_TheChronometer.demands` (rates are integers; 1 is the floor short of removing it). It
saves about 5 golems (an Overclocker part-time on R18, a Hauler on R12, part of a glass, lens and
wire line, an Aether unloader) [M: 5.6 golem-equivalents, §18.3 B2; the trim was not needed].

Two caveats the user should weigh (§17 Q5): stage 3 already demands 3 Conduits a minute, so a
player reaching stage 4 already owns a Conduit line; trimming stage 4 only lets them retire it.
And Lens at 3 Glass makes stage 4's Lens lines slightly larger (§11), +1 golem or so.

## 11. Lens at 3 Glass, worked

Roadmap decision 8: "tried once in the tuning spreadsheet, kept only if stage 3 still balances".
Lenses go to the tower directly (stage 3) and through Aether Cells (2 Lens each) into Conduits (3
Cells) and Regulators (1 Cell, 2 per Chronometer Core). Slag comes from R4, 1 per 2 Iron Plate.

| | Lens demand /min | Slag needed at 2 Glass | at 3 Glass | Slag made /min | Balance at 2 Glass | at 3 Glass |
|---|---|---|---|---|---|---|
| Stage 3, Lens 24 | 24 tower + 18 for 9 Cells = 42 | 84 | 126 | ~46 (92 Plate for 2 Frame Sections) | −38 | **−80** |
| Stage 3, **Lens 12** | 12 + 18 = 30 | 60 | **90** | ~46 | −14 | **−44** |
| Stage 4 | 4 Cores + 14 for 7 Cells = 18 | 36 | **54** | ~96 (§5.3(c)) | +60 voided | **+42 voided** |

`progression-design.md` §5.3(c) counted only the tower's 24 Lens (48 Slag against 46); with the
Cells the stage-3 deficit at 2 Glass is 38, not 2. Either way the sign flips between stages 3 and
4, which is the design's point.

**Decision, as proposed:** Lens at 3 Glass **with stage 3's Lens demand halved to 12**. Stage 3's
deficit moves from 38 to 44 Slag/min (inside invariant 4's ±10), stage 4 voids 42 instead of 60
(the Slag Heap burns 10.5 Coke/min, not 15), and the Glass-or-void choice matters in chapter 8,
stage 3 and stage 4 instead of stage 3 alone. At 3 Glass **without** the halving, stage 3's
deficit doubles to 80 Slag/min, 160 surplus Iron Plate a minute to smelt and store: it fails
invariant 4, and decision 8 says drop the change then.

Prices that move: `Card_R10_LensGrinding` (§9), Floor 5 (§6). The Zeppelin's 8 Lens are now 24
Glass, the kit's `lens` goal 10 Lens 30 Glass; both come after `glass`, where a carrier already
diverts Slag.

## 12. The Brass Presser and chapter 2

### 12.1 The risk

`ChaptersOneToFourFloorTests` (2026-10-19): chapter 2 takes **4:15** of its 4:30 budget with every
action instant. Nearly all of it is the bench: the Presser's 20 Iron Plate and the 20 Plate inside
its 10 Gears, cranked at 4.8 s a Plate and 3.2 s a Gear:

```
crank(Presser) = (Plate + 2 × Gear) × 4.8 s + Gear × 3.2 s
               = (20 + 20) × 4.8 + 10 × 3.2 = 224 s        today
               = (10 + 10) × 4.8 +  5 × 3.2 = 112 s        proposed
```

Any real player adds walking and reading, so chapter 2 has 15 seconds of slack. The roadmap and
`guide-chapters-design.md` §7.1 name the lever: the Presser's price, not the guide's text.

### 12.2 The change

**Brass Presser: 60 Scrap + 10 Iron Plate + 5 Gear** (from 20 Plate + 10 Gear). The Scrap stays:
it is harvested at 6.7/s, not cranked, and it keeps the Presser a purchase that needs the first
Scavenger's crate. The card follows (§9). `gears` asks for 5 Gears.

**Expected floors** [M: measured 5:16 (1:26, 2:20, 1:22, 0:06), §18 M8]:

| Ch. | Budget | Floor today | Expected floor | Ceiling (§14) |
|---|---|---|---|---|
| 1 | 5:00 | 1:20 | ~1:23 (the 30-Scrap Coal order) | 3:20 |
| 2 | 4:30 | 4:15 | **~2:25** (112 s less cranking) | 3:00 |
| 3 | 3:30 | 2:16 | **~1:30–1:50** (the second Presser's 20 Plate-equivalents fewer, partly from the first Presser's crate) | 2:20 |
| 4 | 2:00 | 0:06 | 0:06 | 1:20 |
| 1–4 | 15:00 | 7:58 | **~5:25–5:45** | 10:00 |

The manual era shrinks by about two and a half minutes. `progression-design.md` §4 wanted it at
10–15 minutes of hand labour; Phase 2's 15-minute target for chapters 1–4 (the user's) supersedes
that, and the first Presser is still bought with cranked goods.

### 12.3 Quoted prices come from the data

Today four steps hard-code the Presser's price (`TutorialGuide.cs`: `gears` "costs 10 Gears",
`Counts((Gear, 10), …)`; `presser` and `presser2` "(60 Scrap + 20 Iron Plate + 10 Gear)" and their
`Counts`; the chapter 5 `carrier` step's `Counts`), and two the Coal truckload's. Each builds its
phrase from the definition: `ConstructionCostPolicy.FormatCost(chassis.cost)` for the text, the
chassis's lines for `Counts`, and the market offer for the truckload. The `gears` step's Done reads
the Presser's Gear line, not 10. `TutorialGuideTests`' 240-character budget still holds every
chapter 1–4 body [M: it does, §18 M9].

The same for chapters 8–9's quoted prices that §8.1 raises: `post` "(10 Brass + 5 Casing)",
`zeppelin` "6 Mainspring, 8 Lens, 3 Aether Cell, 30 Casing and 40 Brass" and its `Counts`, `mast`
"(20 Brass + 10 Casing)" and its `Counts`. Each now names its Iron Plate line, built from the data.

## 13. The Mainspring Overclocker, final

Roadmap decision 7 decides its *identity* after the full playthrough: if players never choose it
over a Hauler, it gets a second exclusive card, and the speed aura stays cut. This section fixes
everything else now, so the playthrough judges a finished chassis.

| Aspect | Final | Lives in | Note |
|---|---|---|---|
| Slots | 5 | `chassis.json` `maxAppendageSlots` | 3 inputs, or 2 inputs plus Repeat |
| Cost | 2 Mainspring + 20 Brass + 24 Casing + 12 Gear **+ 120 Iron Plate** (§8.1) | `chassis.json` `cost` | Raw ~698; opens at `mainspring` (ch. 8), reachable 250; the guide-path limit is 150 |
| Card | 1 Mainspring + 5 Brass + 6 Casing + 3 Gear **+ 30 Iron Plate**, prerequisite Mainspring | `Card_MainspringOverclocker` | The formula (§9). The station builds any chassis unclaimed; the card is what the Workbench's rack offers [M: it can, §18 M10] |
| Unlock in the guide | chapter 10's `frame-section` text names it ("build a Mainspring Overclocker at the station"); no step of its own | `TutorialGuide.cs` | The full playthrough watches it (`full-playthrough.md`, chapter 10) |
| Steam | 3 Coke/min while working, like every chassis; a Repeat iteration is working | `SteamNetwork` | No surcharge: a dearer golem that also burns more would never be chosen |
| Repeat card | free, cycling, `prerequisiteItemProduced` **Mainspring** (§9) | `Card_RepeatAssembly` | |
| Repeat n | 1 to `RepeatRules.MaxIterations(recipe)` (below), from the slot's dial | new `Golems/RepeatRules.cs` | Today 1–12, which can jam a golem for good |
| Repeat duration | n × the recipe's ticks (unchanged) | `StepDurationRules.Repeat` | See §17 Q6 for the alternative |

### 13.1 Repeat's limit

A `Repeat(n)` runs its Assemble n + 1 times from one Haul's hold, and the Push comes after it. Two
things can jam it for good, and both are reachable today with the 1–12 dial:

- **Output.** Each run needs room for its output in a 12-per-good stock that nothing empties until
  the Push. R1 with Repeat 12 makes a 13th Coke with no room: `OutputFull`, red, forever.
- **Input.** n + 1 runs need (n + 1) × q of each input, held at once: R9 with Repeat 3 needs 16
  Iron Plate in a 12 hold.

```
RepeatRules.MaxIterations(recipe) =
    min( min over inputs i: ⌊12 / q_i⌋,  ⌊12 / outputQuantity⌋,  ⌊12 / byproductQuantity⌋ if any ) − 1
    (never below 0; 12 = GolemInventory.CapacityPerType)
```

| Recipe | Max n | Recipe | Max n | Recipe | Max n |
|---|---|---|---|---|---|
| R1, R2, R3 | 11 | R4, R5, R6 | 5 | R9 | 2 |
| R8 | 5 | R7 | 3 | R10 (3 Glass) | 3 |
| R19 | 3 | R11 | 3 | R12 | 5 |

Three-input recipes can't take Repeat on an Overclocker at all (3 Hauls + Assemble + Repeat + Push
is 6 slots), so R13, R15, R16 and R18 never need a limit.

**The rule:** the Workbench's dial for a Repeat slot clamps to [1, MaxIterations] of the Assemble
before it (`WorkbenchQuantityPolicy`, read through the session). A Repeat behind an Assemble whose
MaxIterations is 0, or behind no Assemble, is refused by `GolemProgramWriter.TryWrite`, the one
writer ENGAGE and the station share, with `LastStatusMessage` "Repeat can't fit another run of
{recipe} in a 12 hold" (or "Repeat needs an Assemble before it"). A loaded save holding a larger n
keeps its program and clamps n on load [M: `SaveLoadService`, §18 M11]. No new
`StallReason`. The Haul batch is still the player's: a batch below (n + 1) × q stalls `MissingItem`
at the Repeat, red, with today's fix line "Raise the Haul's batch, then ENGAGE again."

### 13.2 What Repeat buys, for the playthrough to judge

With the Haul at 12 and Repeat at its limit, against the same recipe without Repeat on the
guide's programs (§2):

| Recipe | Without Repeat | With Repeat | Gain |
|---|---|---|---|
| R1 Coking, Repeat 11 | 35.3 Coke/min (Haul(1) coker) | 12 + 12 + 132 + 14 = 170 t per 12: 42.4 | +20 % |
| R8 Gear Cutting, Repeat 5 | 28.6 Gear/min | 12 + 16 + 80 + 8 = 116 t per 6: 31.0 | +8 % |
| R4 Iron Smelting, Repeat 5 | 36.4 Plate/min | 12 + 6 + 24 + 120 + 20 = 182 t per 6 runs: 39.6 | +9 % |

Repeat amortises only the fixed 2-tick overheads, so it buys 8–20 %. That is the number the
playthrough's `overclocker` question weighs: is an Overclocker worth 698 raw goods and a bay slot
for that? Decision 7's remedy (a second exclusive card) stays the user's call after the
playthrough (§17 Q6).

## 14. Per-chapter budgets and floors

**Budget** is the human target, what `testscript/full-playthrough.md` times. **Ceiling** is what
the floor tests assert: two thirds of the budget, as chapters 1–4 assert 10:00 of 15:00. A floor
plays every action instantly but earns every good at the player's real rates (§15.4).

| Ch. | Steps | Budget | Ceiling | Floor before | Floor, measured (§18) | Notes |
|---|---|---|---|---|---|---|
| 1 | 11 | 5:00 | 3:20 | 1:20 | 1:26 | the 30-Scrap Coal order |
| 2 | 7 | 4:30 | 3:00 | 4:15 | 2:20 | §12's Presser price |
| 3 | 7 | 3:30 | 2:20 | 2:16 | 1:22 | |
| 4 | 7 | 2:00 | 1:20 | 0:06 | 0:06 | |
| 5 | 12 | 20:00 | 13:20 | | 7:28 | the Hauler's 20 Plate + 10 Gear + 30 Coke (§18 correction 10; 12:51 at 80 + 40) |
| 6 | 8 | 15:00 | 10:00 | | 0:20 | copper belts |
| 7 | 6 | 10:00 | 6:40 | | 4:14 | Floor 1, bays, pack rung 1, Ledger, save, load |
| 8 | 9 | 45:00 | 30:00 | | 23:05 | 35:39 before §18 correction 10 |
| 9 | 8 | 30:00 | 20:00 | | 7:34 | the Zeppelin's 1,093 raw |
| 10 | 5 | 45:00 | 30:00 | | 27:21 | `stage1` 6:15 (§15.4); 12 Frame Sections the kit grants |
| 1–10 | 80 | **3:00:00** | 2:00:00 | | 1:15:23 | |

A full run of three hours to the Foundation leaves stages 2–4 an hour or more, inside roadmap
decision 1's 4–5 hour campaign. If a measured floor passes its ceiling, the numbers in this spec
are revisited; the ceiling is not raised to fit (as `ChaptersOneToFourFloorTests` treats 10:00).

## 15. Tests

### 15.1 Existing tests whose pinned numbers change

| Test | Pins | Change |
|---|---|---|
| `Core.Tests/World/SandboxSetupTests` | Scrap truckload 30, Aether price 40 (lines 83, 85) | 100; 80 |
| `Core.Tests/Economy/TruckloadMarketTests` | [M: none; it builds its own offers, §18 M12] | follow §4.3 |
| `Core.Tests/ClockTower/ClockTowerStageCatalogTests` | stage 3's (Aether Conduit 3, Lens 24, Frame Section 2) | Lens 12 |
| `Core.Tests/Tutorial/ChaptersOneToFourFloorTests` | < 10:00 | adds per-chapter ceilings (§14); its `Needs` reads the Presser's cost from data already |
| `Core.Tests/Tutorial/TutorialGuideTests` | texts ≤ 240 chars | unchanged; must still pass with data-built prices |
| `Core.Tests/Economy/LayoutMattersTests` | 103, 0, 82, 101, 50 | [M: unchanged, §18 M12] truckload sizes may move them; the table's shape must hold: straight ≫ apart = 0, ferry recovers most, belt ≈ straight, unemptied crate stops at 50 |
| `Core.Tests/Economy/PriceReachTests` | invariant 1 | adds bench recipes and the boiler hand-load (§15.2) |
| `Core.Tests/AssemblyLine/*`, `PlaytestKitTests`, scenarios `golems`, `workbench` | [M: none did, §18 M12] any that claim `RepeatAssembly` before a Mainspring | grant the Ledger a Mainspring first |

### 15.2 `PriceReachTests`, widened

Add to the price list: each of the starter bench's crankable recipes' inputs ("bench R2: 1
Scrap"), and the boiler's hand-load (`HandLoadBatch` Coke). Closes `open-items.md` §4.

### 15.3 New: `Core.Tests/Economy/PriceCeilingTests`

Named for what it pins: the ceiling on a price is the reachable pack (invariants 1 and 2), plus
the data checks this spec adds. It shares `PriceReachTests`' price list and its Seen(s) harness
(move both into a helper, `PriceList`, rather than copy them).

- `GuidePathPrices_AskNoMoreThanTheTaughtRung` (invariant 2): every price a guide step's kit pays
  (the chassis it builds, the buildings it places, the cards it claims, the truckloads it orders),
  opening after chapter 4, has every summed line ≤ `packUpgrades[0].capacityPerType` (150). Prices
  opening in chapters 1–4 have every line ≤ `packCapacityPerType` (100).
- `EveryRung_HasAPriceThatNeedsIt` (invariant 2): for each rung *k*, some price on the list has a
  summed line above the cap below the rung (rung 1: > 100, rung 2: > 150, rung 3: > 200) and no
  more than rung *k*'s cap, and opens no sooner than rung *k*. **Recommended**, so a later edit
  can't leave an upgrade with nothing to buy. Today: rung 1 Floor 2, rung 2 Floor 4, rung 3
  Floor 6.
- `NothingInChaptersOneToFour_WasRaised`: the prices that open in chapters 1–4 equal a table in
  the test, copied from §8 and §4.3 as proposed (the Presser at its new price, the truckloads at
  their new sizes). A later raise there must edit the test and say why.
- `TheLensCostsThreeGlass`: `R10_LensGrinding` has inputs exactly (Glass 3, Brass 1), output 1 Lens.
- `StageThreeStillBalances`: computes stage 3's Slag balance from the data with §11's arithmetic
  (Lens demand + 2 Lens per Aether Cell × 3 Cells per Conduit × Conduit demand, × Glass per Lens;
  against Frame Section demand × (10 × 4 + 6) Plate ÷ 2) and asserts it is within ±10 of −38.
- `ClaimPricesFollowTheirFormula` (invariant 8): every recipe card's `claimCost` equals 4 × its
  recipe's inputs; every chassis card's equals max(1, ⌊q/4⌋) per line of its chassis's cost; free
  cards have an empty `claimCost`.
- `OneCokerFeedsAFullBoiler` (invariant 7): 600 ÷ (Extract(1) + R1 + Push(1)) ≥
  `MaxGolemsPerBoiler × CokePerMinutePerGolem`, computed through `StepDurationRules`.
- `StepGoodsReachTests.EveryGoodAStepAsksFor_IsMakeableWhenTheStepOpens` (invariant 3), in the
  same harness as `PriceReachTests` (the kit plays the guide; the Ledger is polled each frame):
  - *Asked(s)*: the goods the kit deposits into the pack while performing step *s*
    (`TotalDeposited` before and after), plus the goods of the claim price of the card the step
    names.
  - *Makeable(s)*: the fixpoint from the stalls' goods and Seen(s − 1), adding a recipe's output
    (and byproduct) when its inputs are makeable, its card is claimable (its
    `prerequisiteItemProduced` makeable, its `claimCost` makeable), and either it is crankable at
    the bench or some chassis with `slots ≥ inputs + 2` has a makeable cost.
  - Assert Asked(s) ⊆ Makeable(s) for every step, naming the step and the good.

### 15.4 New: `Core.Tests/Tutorial/FullRunFloorTests`

The `ChaptersOneToFourFloorTests` approach, carried through chapter 10. Move its player verbs
(`Acquire`, `Harvest`, `Crank`, `TakeFromCrates`, `Step`'s chores, `WaitToLeave`) into a shared
`Core.Tests/Tutorial/FloorPlayer.cs`, so both tests play the same player; the chapters 1–4 test
keeps its name, its 10:00 assertion and gains §14's per-chapter ceilings.

`FullRunFloor_EveryChapter_FitsItsCeiling_AndNoStepSticks` plays a fresh game to `done`:

- **Raw goods** are harvested or ordered at the player's rates (§2), each paid truckload paid with
  earned Scrap, one in transit per stall, as today's test does for Coal.
- **One-input goods** (Coke, Iron Plate, Glass, Gear, Copper Wire) are taken from crates golems
  have filled, else cranked.
- **Goods of two or more inputs** that a kit action grants (`MakeGood`, `GrantCost`, `Feed`) are
  made by a **reference line**: `FloorPlayer.RunLine(recipe, quantity)` builds, once per recipe
  and paid from earned goods, the cheapest chassis with `inputs + 2` slots between two crates on
  a reserved strip [M: a block under the north wall, §18 M13], claims the
  recipe's card (paid), writes `Haul(input, q)… Assemble, Push` through `GolemProgramWriter`,
  steams it from a boiler on the strip (paid, fuelled with earned Coke), puts the inputs into its
  input crate from the pack with `CrateTransfer.Put` (earning them first, recursively), and waits
  real ticks until its output crate holds the quantity, then collects it. A line stays standing
  and is reused, as a player keeps their lines.
- **Everything else** a kit action does (place, build, claim, program, turn, save, load) is the
  kit's own instant action, paid from earned goods, never granted: the same repayment rule as
  today's test.
- **`stage1`** is the one exception: its floor is the stage's nominal 360 s. The test feeds the
  tower's endpoint 6 Frame Sections a minute, each recorded as fresh production
  (`ProductionTally`), and asserts the stage completes within 360 s + 1 s. Building a 6-a-minute
  line is a player's design, timed only by the human run.
- **Stuck:** a step that does not complete within 20 minutes of game time fails the test, naming
  the step and every golem's state, stall reason and resource (as `WaitToLeave` does now).
- **Output:** one line per chapter (`chapter 5: 9:41`), the total, the cranking, and every step's
  time, as today's test prints.
- **Asserts** each chapter under its ceiling (§14) and the run under 2:00:00.

It runs in seconds of wall time only if the world advances fast [M: about 6 s, §18 M13; chapters 1–4 take
how long? If the full run exceeds 60 s wall time, mark it `[Category("Slow")]` and say so in the
root `CLAUDE.md`'s Commands; it is still run before every Phase 3 commit].

### 15.5 New: `Core.Tests/Economy/MarketThroughputTests`

`StageOne_AtFullDemand_NeedsAtMostEightTruckloadsAMinute`: §4.2's formula from the data:
`raw_g` by back-propagating `recipes.json` (Plate via R4, Coke as Coal, Glass free, Brass via R7),
`golems(stage 1)` = 63 as a named constant citing `progression-design.md` §7, and each stall's
`truckloadSize` (the larger offer per good when two stalls sell it). Prints each good's orders a
minute; asserts the sum ≤ 8.

### 15.6 New: `Core.Tests/ClockTower/ClockTowerPartialSupplyTests`

`UnderSupply_StageTime_IsMeasured`: a `ClockTowerSite` on stage 1, fed Frame Sections as fresh
production at an even spacing for 6, 3, 2 and 1 a minute; records the ticks to complete (or the
progress after 4 hours of game time). It asserts only what the current rule guarantees (full
supply completes in 360 s ± 1 s; any positive supply never loses progress) and prints the rest, so
§10.1's (d/s)² reading is measured, not assumed. Its numbers go into §18.

### 15.7 New: `Core.Tests/Golems/RepeatRulesTests`

`MaxIterations` for every recipe matches §13.1's table (computed from the data, not a copy); the
Workbench dial clamps a Repeat behind R9 to 2; `GolemProgramWriter.TryWrite` refuses Repeat behind
no Assemble and behind an Assemble with limit 0, and accepts R1 with Repeat 11; an Overclocker
running R1 with Haul 12 and Repeat 11 completes a cycle of 12 Coke and never stalls `OutputFull`;
a save holding Repeat 12 behind R1 loads with n clamped to 11.

### 15.8 Scenarios

No new scenario for tuning numbers: they are data. `perf` is new, specified in
`docs/performance-report-template.md`. `hover` changes for `onboarding-feedback-design.md` §17.
Every existing scenario must still pass, run twice from a clean build; `playtest-kit` covers the
retuned prices along the guide.

## 16. The changes, in order

Each is a data edit or a small Core change; build and run `dotnet test` after each group.

1. **Guards first** (they fail, then pass): `PriceCeilingTests` (`TheLensCostsThreeGlass` and
   `StageThreeStillBalances` fail until step 4, `ClaimPricesFollowTheirFormula` until step 5b,
   `NothingInChaptersOneToFour_WasRaised` until step 5), `PriceReachTests` widened,
   `StepGoodsReachTests`, `MarketThroughputTests` (fails until step 3),
   `ClockTowerPartialSupplyTests` (record only).
2. **Quoted prices from data** (§12.3), with no number changed: `TutorialGuideTests` and the
   `tutorial` scenario still pass.
3. **Market** (§4.3): `sandbox.json` `market` sizes, prices, delivery ticks. Update
   `SandboxSetupTests`, `TruckloadMarketTests`. Re-measure `LayoutMattersTests` and
   `ChaptersOneToFourFloorTests`; record both in §18.
4. **Lens** (§11): `recipes.json` R10 Glass 2 → 3; `clock_tower_stages.json` stage 3 Lens 24 → 12;
   `assembly_line_cards.json` `Card_R10_LensGrinding` Glass 8 → 12; `sandbox.json` Floor 5 Glass
   200 → 180; the `lens` step's recipe line; `ClockTowerStageCatalogTests`.
5. **Presser** (§12): `chassis.json` `BrassPresser` cost 60 Scrap + 10 Iron Plate + 5 Gear;
   `Card_BrassPresser` 15 Scrap + 2 Iron Plate + 1 Gear; `ChaptersOneToFourFloorTests` per-chapter
   ceilings; record the measured floor in §18 and in `docs/roadmap.md`'s Phase 2 table note.
5b. **Late prices** (§8.1): `chassis.json` Overclocker + 120 Iron Plate, Zeppelin + 150;
   `assembly_line_cards.json` their cards + 30 and + 37; `placeables.json` Freight Mast + 120,
   Pneumatic Post + 40; `sandbox.json` Floor 3 Gear 80, Floor 4 Casing 20, Floor 6 Casing 30; the
   `post`, `zeppelin` and `mast` texts and `Counts` (§12.3). `PriceReachTests` and
   `PriceCeilingTests` pass.
6. **Overclocker** (§13): `Golems/RepeatRules.cs`, the dial clamp, the writer's refusal, the load
   clamp, `RepeatRulesTests`; `Card_RepeatAssembly` prerequisite Mainspring.
7. **Floor test** (§15.4): `FloorPlayer`, `FullRunFloorTests`. Record every chapter's floor in §14
   (replacing the [M]s) and in §18.
8. **Steam constants** (§3): delete or pin `SteamNetwork.BoilerScrapCost`, `BoilerIronPlateCost`,
   `SteamPipeIronPlateCost`.
9. **The playtest report** (`docs/performance-report-template.md` §5): "Time per chapter",
   "Chapters 1–10" and "Frame rate"; the six new questions of `testscript/full-playthrough.md`
   (`PlaytestQuestion`, ids as written there); `PlaytestSessionTests`.
10. **The profile** (`docs/performance-report-template.md` §2–§5.1): `PerfSampler`,
    `EndgameLoad`, the `perf` scenario, `EndgameLoadTests`; run it and fill §6 of the template.
    The Conduit rule (§10.2) acts on its result.
11. **The feeder on the hover card** (`docs/onboarding-feedback-design.md` §17): its own tests and
    the `hover` scenario's change.
12. **Docs:** `docs/progression-design.md` §14 gets the measured numbers; `guide-chapters-design.md`
   §1's crank column and `testscript/phase-2-playtest.md`'s "about 3¾ of it is the bench" follow
   §12; `docs/open-items.md` closes the bench-price gap and the feeder item.

## 17. Questions, decided

**Decided by the user on 2026-10-19.** Question 1 was rejected as written and replaced; 2–8 were
taken as recommended.

1. **"No price above 100" is dropped.** The user: "I think some costs could be higher if they're
   expected to come later when we'd expect the player to upgrade their limits." The ceiling on a
   line is the pack reachable when the price opens (invariant 1), Floor Expansion needs no
   exemption, and late prices use the room the rungs give (invariant 2, §8.1).
2. **The Presser's price changes now** (§12), before the Phase 2 playtest, which then measures the
   retuned game.
3. **The market gets bigger truckloads now** (§4.3). A standing order per stall is decided from
   the playthrough's `market` answer, as its own spec if it comes.
4. **Under-supply:** once `ClockTowerPartialSupplyTests` confirms §10.1, the meter decays at the
   line's delivered rate instead of its demand, so stage time is nominal × d/s, as §7 and the
   `stage1` text say. Specified in a short addendum to this spec once the numbers are in.
5. **Stage 4's Conduit is trimmed only** if the budget still fails after the probe is optimised
   (§10.2).
6. **The Overclocker** plays the full run with Repeat at 100 %. If the answer is "never", Repeat
   iterations at 75 % of the recipe's ticks are tried before a second exclusive card is designed.
7. **Lens at 3 Glass, with stage 3's Lens demand halved to 12** (§11).
8. **The drainer is not named in Phase 3** (`onboarding-feedback-design.md` §17.8); the
   playthrough's notes say whether anyone hit the case.

## 18. Corrections and measurements, as built

Code agent A, 2026-10-19, on `phase3/tuning`. Every number here comes from a test that prints
it; the test is named.

### 18.1 Measurements (the [M]s)

- **M1. The steam price constants** (§3). `SteamNetwork.BoilerScrapCost`, `BoilerIronPlateCost`
  and `SteamPipeIronPlateCost` had no reader except two aliases, `PlaceableBoiler.ScrapCost` /
  `IronPlateCost` and `PlaceableSteamPipe.IronPlateCost`, which had none either. All five are
  deleted; the prices live in `placeables.json` alone.
- **M2. Measured by Code agent B** (§18.3): the coal line's share at stage 4 (§3) and the golems
  the stage 4 Conduit cut would save (§10.2), from the endgame load (`EndgameLoad`).
- **M3. Aether truckloads** (§4.3), from §4.2's formula: stage 3 needs 9 Aether a minute (3
  Conduits × 3 Aether Cells × 1 Aether), stage 4 needs 7 (2 Chronometer Cores × 2 Regulators × 1
  Cell, plus 1 Conduit × 3 Cells). At 40 a truckload, 0.23 and 0.18 orders a minute (0.45 and 0.35
  at the old 20). `MarketThroughputTests` measures stage 1: **7.59 orders a minute** (Scrap 2.76,
  Coal 3.43, Copper Ore 0.93, Zinc Ore 0.47), against 8.
- **M4. "2 Glass" in `godot/`** (§7): the `lens` goal's recipe line (now built from the card's
  recipe, correction 2) and `TechTreeCatalog`'s R10 node, which still read "2 Glass". It reads 3,
  and `TechTreeCatalogTests.EveryRecipeNode_QuotesItsRecipe_AsTheDataHasIt` now checks every recipe
  node's quantities against `recipes.json`. `RecipeCatalogTests` pins shapes only.
- **M5. §8.1's raises in time** (`FullRunFloorTests`): chapter 9 measures 9:34 of 20:00, chapter 10
  24:49 of 30:00. Neither is over its ceiling, so **no Plate line was lowered**; the Mast,
  Zeppelin and Overclocker keep 120, 150 and 120.
- **M6. `Card_RepeatAssembly` before the gate** (§9), by a kit run of chapters 1–7 with its
  prerequisite set back to "": it sat on the line from chapter 1's first step through `work` (10
  steps), not from chapter 2. With the Mainspring prerequisite it is never on the line in chapters
  1–7.
- **M7. The tower under supply** (§10.1, `ClockTowerPartialSupplyTests`): stage 1 (6 Frame
  Sections a minute, 6:00) at 6, 3, 2 and 1 a minute took **6:00, 23:09, 53:30 and 3:36:00**,
  nominal × (d/s)² within a few percent. §17 Q4's fix is built (correction 1): **6:00, 12:00,
  18:00 and 36:00**, nominal × d/s exactly. The guide's `stage1` text ("Short of 6 a minute it
  still builds, just slower") is now true as written.
- **M8. Chapters 1–4** (§12.2, `ChaptersOneToFourFloorTests`): **5:16** (1:26, 2:20, 1:22, 0:06),
  4:14 of it at the bench (Iron Plate 2:54, Coke 0:48, Gears 0:32). Chapter 3 came in under the
  expected 1:30–1:50 and the total under the expected 5:25–5:45. Every chapter is under its
  ceiling.
- **M9. Text budget** (§12.3): every chapter 1–4 body, its prices now built from the data, still
  fits 240 characters (`TutorialGuideTests`).
- **M10. An unclaimed Overclocker** (§13) built at the station is programmed in the Workbench: the
  session takes the golem's own chassis into its draft, the gate filters only the rack. Pinned by
  `RepeatRulesTests.AnUnclaimedOverclocker_BuiltAtTheStation_IsProgrammedInTheWorkbench_WithRepeatAtItsLimit`.
- **M11. Where a load restores quantities** (§13.1): `SaveLoadService`, through
  `GolemProgram.SetQuantityAt`; `RepeatRules.ClampProgram` runs right after it.
- **M12. Tests §15.1 named:** `TruckloadMarketTests` reads no data (it builds its own offers), so
  it is unchanged. `LayoutMattersTests` is unchanged at 103, 0, 82, 101, 50. No Core test or
  scenario claimed Repeat before a Mainspring. One test leaned on Repeat being claimable at t=0:
  `SaveLoadPanelTests.Progress_RoundTrips_AndALoadReplacesWhatCameAfterTheSave` needed unowned
  cards to offer after a load, and now has its ledger see Scrap first.
- **M13. The full run** (§15.4, `FullRunFloorTests`): every step completes; the reference lines
  stand in a block under the workshop's north wall (two rows of lines with a steam run between
  them, a boiler for every four golems), clear of every guide mark and golem tile, and the run
  takes **about 6 s of wall time**, so it is not `[Category("Slow")]` and runs with every
  `dotnet test`. Floors per chapter are in §14: **1:31:47** in all.

### 18.2 Corrections

1. **The meter's drain** (§10.1, §17 Q4). `ClockTowerSite.DecayRate`: a line's meter drains at the
   rate the line delivers, up to its demand, and at the demand once nothing has arrived for a
   minute. The last clause is not in §17's wording: without it a line that stops would hold its
   meter above 0 forever and never raise the starved alarm. At or above the demand nothing changes.
   `ClockTowerPartialSupplyTests` pins the linear times and a stopped line still starving.
2. **More quotes come from the data than §12.3 lists.** The Copper truckload's text said "20
   Scrap", which §4.3 changed to 60, and §12.3 did not list it. Rather than patch one more number,
   every price the guide quotes is built from its definition: the boiler, the Scavenger, the
   depot, the pipe, the Aether-Hauler, the Slag Heap, the Coal and Copper truckloads, Floor 1, pack
   rung 1 and the Scrap Reclamation claim, as well as the Presser, Post, Zeppelin and Mast. The
   chapter 8 and 10 goal steps read their recipe line from the card's recipe (`Goal` lost its
   recipe argument). The kit grants the Presser's Gear line at `gears`, and its `coal-order` and
   `copper` deliver the offer's truckload size, not 40.
3. **The Repeat refusal is an out parameter, not a `LastStatusMessage`.** `GolemProgramWriter` is
   static; `TryWrite(..., out string refusal)` returns why, and ENGAGE shows it as "Cannot engage:
   {refusal}." The refusal is checked before anything is touched (the old chassis refusal clears
   the steps first, and still does). A Repeat whose n is above its limit is written at the limit,
   as the dial would have stopped it. The Workbench screen's + button reads the session's
   `DraftQuantityMax` (one line in `WorkbenchScreen`).
4. **`PriceCeilingTests`' "opens in chapters 1–4"**: for the guide-path test, the chapter of the
   step whose kit pays the price (the step's `MenuKey`, `Card`, and a table of the chassis,
   truckloads and purchases the kit makes); for `NothingInChaptersOneToFour_WasRaised`, the
   chapter in which the Seen harness first sees every good of the price. That table holds 40
   prices, the free cards among them. `EveryRung_HasAPriceThatNeedsIt` finds, for rung 1, the Mast,
   the Overclocker, the Zeppelin and Floors 2 and 3; rung 2, Floors 4 and 5; rung 3, Floor 6.
   `StageThreeStillBalances` measures −44 Slag a minute (§11's number).
5. **`StepGoodsReachTests`** follows §15.3 literally, a card's claimability required even for a
   bench recipe, and passes. The goods a step asks for are attributed per frame to the step
   current after the frame's kit action.
6. **The floor player** (`Core.Tests/Tutorial/FloorPlayer.cs`) departs from §15.4 where §15.4's
   player measured badly or could not finish:
   - **Lines from chapter 8 only.** In chapter 5 the guide adopts a line as its own: two crates
     two apart with a golem between are the shape `SmelterLine` takes as the smelter's pair (it
     counts a golem's tile as free), and a line's Brass Presser counts as the carrier. In chapters
     6–7 a line costs more than it saves. Before chapter 8, one-input goods are cranked or taken
     from the guide's lines, as chapters 1–4 always were.
   - **One-input goods are made by lines too** from chapter 8, up to 4 lines a good; two-input
     goods up to 2; a line is added when the player has waited 45 s on that good. One line per
     recipe, as §15.4 says, measured chapter 8 at 52:42 and left chapter 10's 12 Frame Sections
     unfinished after 20 minutes. The first line is Iron Plate from Scrap, its own price cranked;
     lines on a raw good are kept full; the bench cranks the same good alongside.
   - **Slag** comes from the guide's smelter with the carrier turned away (R) while it is taken
     (chapter 5's `work-carrier`), and from chapter 8 from a smelting line of the player's own.
   - **What the kit grants is earned**: what it spent is paid from the pack, earned first; what
     it left (a goal step's goods) is made. Chapters 1–4's old repay counted a granted good still
     in the pack as already earned, so chapter 8's goods would have cost nothing.
   - A crate the kit fills by hand (the smelter's inputs, the carrier's Coke) is never emptied
     to repay it; crates are taken from only for goods no golem hauls from them, so Iron Plate
     comes out of the smelter's output crate and its Slag stays.
   - The full run steps one tick (0.1 s) a frame; harvests within a frame are counted exactly.
     Chapters 1–4 keep 1/30 s and measure exactly as before the refactor.
   - Bays are upgraded when the player's lines fill them before a guide golem is built.
   - `stage1` is held to 360 s + 60 s (the tower's one-minute windows fill first) + 1; it measures
     6:15.
7. **Chapter 8 passed its ceiling: 35:39 against 30:00. Decided by correction 10.** The floor's chapter
   8 is dominated by one Aether-Hauler for each two-input good (80 Iron Plate + 40 Gear + 30 Coke
   each, a chapter 1–4 price §8.1 leaves alone) and, at its first step (`copper-ingot`, 13:41), by
   building the Plate, Gear and Coke lines that pay for them. The only §8.1 raise in chapter 8 is
   the Post's 80 Iron Plate, and §8.1's remedy (lower the Mast, then the Zeppelin, then the
   Overclocker) touches chapters 9 and 10, which fit. Per §14 the ceiling is not raised; the
   candidates are the Aether-Hauler's price, chapter 8's budget, or the floor model (a player
   reprogramming one Hauler across chapter 8's recipes). The floor moves with the floor player's
   line policy, and none tried fits both chapter 8 and chapter 10: one line per two-input good
   measured chapter 8 at 30:09 and left chapter 10's Frame Sections unfinished; a second
   two-input line only after 180 s of waiting measured 30:09 and 28:47 (of 30:00); the shipped
   policy (45 s) measures 35:39 and 24:49. Until the user decided, `FullRunFloorTests` held
   chapter 8 to its measured floor + 60 s; that allowance is gone (correction 10). Chapter 10's `frame-section` (18:33 of game time, the kit's 12 Frame Sections: 184
   Casing and the Overclocker) sits near §15.4's 20-minute stuck limit, which the test applies per
   wait, not per step.
8. **Test fixtures that buy late prices**: the Mast tests restore pack rung 1; the Post tests and
   `TutorialGuideTests` give the Iron Plate; `SandboxWorldTests` places every placeable with a fully
   upgraded pack; the `golems`, `golem-motion` and `inventory` scenarios put what the base pack
   can't hold into the Post, which the wallet spends next; the `interact` scenario reads the Coal
   truckload's price from its offer. §15.1 and §15.8 did not list these.
9. **`SandboxSetupTests`** pins more than §15.1 lists: the Scrap stall's 60 delivery ticks, the
   Coal and Copper Ore offers (30 for 120, 60 for 120) and the Aether truckload's 40.
10. **The Aether-Hauler costs 20 Iron Plate + 10 Gear + 30 Coke** (was 80 + 40 + 30; the user's
   call, 2026-10-20, on correction 7), and its card 5 Iron Plate + 2 Gear + 7 Coke by the claim
   formula. A chapter 1–4-era price **lowered**, not raised, so invariant 2 holds;
   `NothingInChaptersOneToFour_WasRaised` and `ChassisCostsMatchSection6` pin the new numbers.
   The Coke line is unchanged. Measured floors (`FullRunFloorTests`, every chapter now asserted
   against its real ceiling): chapters 5–10 at **7:28, 0:20, 4:14, 23:05, 7:34 and 27:21**,
   1:15:23 in all; chapters 1–4 unchanged at 5:16 (the Hauler opens in chapter 5).
   - **How the price was chosen.** The floor responds to the price unevenly, because the floor
     player's lines (when one is added, which good it serves first) shift with every price: half
     (40 + 20) measured chapter 8 at 29:57, 38 + 19 at 29:09, 36 + 18 at 22:57, 34 + 17 at 31:11,
     32 + 16 at 30:01, 28 + 14 at 29:06. Below that the floor settles: 26 + 13 at 26:19, 24 + 12 at
     28:21, 22 + 11 at 24:28, 21 + 10 at 24:54, **20 + 10 at 23:05**, 18 + 9 at 27:48. 20 + 10 sits in
     that band with 6:55 of chapter 8 slack and 2:39 of chapter 10's (27:21 of 30:00); 36 + 18
     measured better but its neighbours do not, so it would not hold under the next retune.
   - Every rung still has a price that needs it (rung 1: the Mast, the Overclocker, the Zeppelin,
     Floors 2 and 3; rung 2: Floors 4 and 5; rung 3: Floor 6), and `PriceReachTests`,
     `StepGoodsReachTests` and the guide's texts (which build the Hauler's price from the data)
     need no change.

### 18.3 Code agent B: the endgame load (2026-10-20)

The profile is `docs/performance-report-phase3.md`. Both numbers come from
`EndgameLoadTests.StageFour_CoalShare_AndTheConduitLine` (explicit, `Category=Perf`).

- **B1. The coal line's share at stage 4** (§3). The endgame load burns **288 Coke a minute**
  with all 96 golems working: 3 a golem, as §3 says, 10.5 guide cokers' output. The load stands
  32 coal-line golems of 96 (23 cokers, two Repeat Overclockers on R1, 7 Coal loaders): **a
  third**, as §3 expected. Counted from the recipes instead (stage 4 at full demand, every line at
  the guide's program shape and §2's step times, raw goods hauled by loaders at 24 a minute, and 3
  Coke a minute for every golem), stage 4 needs 73.1 golem-equivalents and **the coal line is
  29.8 of them, 41 %**: the smelters' Coke (R4, R5, R6) and Coal hauled by loader keep it at the
  design's old share, burn halved or not. That is the playthrough's `coal-share` question, not a
  reason to change the burn; no number was changed.
- **B2. The golems the stage 4 Conduit trim would save** (§10.2): **5.6 golem-equivalents**
  (73.1 → 67.5), close to §10.2's guess of about 5. The R18 Overclocker itself is 0.16; the rest
  is its Aether Cells, Lens, Glass, Brass, Copper and Zinc, Wire, their loaders, and 2.0 of Coke
  and Coal.
- **B3. The trim was not applied.** The `perf` run fails §1's budget only before optimising; after
  `docs/performance-report-template.md` §5.2 steps 2 and 3 it holds in every phase (worst p95
  15.1 ms, worst max 25.3 ms over two windowed runs), so §10.2's rule does not fire and
  `Stage4_TheChronometer` keeps its Aether Conduit line.
