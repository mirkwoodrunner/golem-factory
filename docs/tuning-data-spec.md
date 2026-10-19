# Tuning data spec: every number, where it lives, and what pins it

**Status: draft, 2026-10-19, for the user's approval.** Phase 3 of `docs/roadmap.md` ("Tuning and
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
2. **No line of any price is above 100, except Floor Expansion's.** 100 is
   `sandbox.json` `packCapacityPerType`, the pack before any upgrade. So the pack upgrade is
   needed for Floor Expansion and nothing else, and a player who never extends never meets the
   cap. Floor Expansion purchases 2–6 (120 to 250 in one line) are exempt, because the user
   approved them to ask more than a base pack (`pack-upgrades-design.md` §3, §4); they stay held
   by invariant 1. A line is one good of one price, summed per good as `TryCharge` sums them.
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
| `SteamNetwork.BoilerScrapCost` etc. | `Steam/SteamNetwork.cs` constants | 30, 10, 1 | | **delete** if unread, else pin equal to the data [M] | A second copy of a price that can drift. `grep` their readers first |

Coal-line share at stage 4 with 3/min: upkeep ~96 × 3 = 288 Coke/min (the design's 576 was at
6/min). The coal line falls from ~41 % to about a third of the factory [M]. That is the
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
| Aether truckload | `market[AetherNode]` | 40 Scrap for 20, 120 ticks | **80 Scrap for 40, 150 ticks** | Aether is small at every stage; doubled to keep its stall from being the one reordered every minute at stage 3–4 [M]; 2 Scrap per crystal, unchanged |
| Free seed | `sandbox.json` `freeStallSeedNode`, `freeStallSeedQuantity` | ScrapNode, 60 | **keep** | Chapter 1's first 20 Scrap |

**Result:** OrdersPerMinute(stage 1) = 2.8 + 3.4 + 0.9 + 0.5 = **7.6** (from 23.7). Every new
price is ≤ 100 (invariant 2) and opens at `scrap`, where the pack holds 100 (invariant 1).

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
| Pack, base | `sandbox.json` `packCapacityPerType` | 100 | **keep** | Invariant 2's line |
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
| Floor 3 | 150 Iron Plate + 40 Gear + 50 Copper Ingot | `copper-ingot` | **keep** | |
| Floor 4 | 200 Iron Plate + 20 Brass + 10 Casing | `casing` | **keep** | |
| Floor 5 | 200 Glass + 20 Lens + 15 Casing | `lens` | **180 Glass** + 20 Lens + 15 Casing | At 3 Glass a Lens, 20 Lens are 60 Glass, not 40: 180 keeps the purchase's Glass at 240, as it was |
| Floor 6 | 250 Iron Plate + 6 Mainspring + 4 Aether Cell + 15 Casing | `aether-cell` | **keep** | |

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

Everything that quotes R10's inputs changes with it [M: `grep -rn "2 Glass"` over `godot/`]:
the `lens` goal step's recipe line (`"2 Glass + 1 Brass → 1 Lens"`), `TechTreeCatalog`'s node
detail if it quotes it, and `RecipeCatalogTests` only if it pins quantities (it pins shapes:
R10 stays 2 inputs, 4 slots).

## 8. Chassis prices

`godot/data/chassis.json` `cost`. Slots and flags are `progression-design.md` §2 and §6.

| Chassis | Slots | Current | Proposed | Why |
|---|---|---|---|---|
| Clockwork Scavenger | 2 | 12 Scrap | keep | |
| **Brass Presser** | 3 | 60 Scrap + **20 Iron Plate + 10 Gear** | 60 Scrap + **10 Iron Plate + 5 Gear** | §12: chapter 2's floor is 4:15 of 4:30 |
| Aether-Hauler | 4 | 80 Iron Plate + 40 Gear + 30 Coke | keep | |
| Mainspring Overclocker | 5 | 2 Mainspring + 20 Brass + 24 Casing + 12 Gear | keep | §13 |
| Zeppelin Freight Loader | 6 | 6 Mainspring + 8 Lens + 3 Aether Cell + 30 Casing + 40 Brass | keep | At 3 Glass its 8 Lens are 24 Glass, not 16; §11 shows the Glass is there by chapter 9 |

`ChassisCostAcyclicityTests` stays true: the Presser still costs only hand-made goods.

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
| `Card_RepeatAssembly` | `prerequisiteItemProduced` "" | **"Mainspring"** | A free, cycling card no chassis before the Overclocker can hold; today it is the deck's fourth card, so it is the first refill into a slot from chapter 2 on, offering nothing [M: confirm it shows in chapter 2] |
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

### 10.1 Finding: under-supply slows a stage quadratically [M]

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
confirms it, the fix is the user's (§17 Q4); this spec doesn't change the rule.

### 10.2 The stage 4 Conduit rule

Trim stage 4's Aether Conduit demand **only if** `docs/performance-report-template.md`'s `perf`
run fails the frame budget at 96 golems **after** the stall-facts probe is no longer the top Core
hot spot (the template's step 4). Then delete the `AetherConduit` line from
`Stage4_TheChronometer.demands` (rates are integers; 1 is the floor short of removing it). It
saves about 5 golems (an Overclocker part-time on R18, a Hauler on R12, part of a glass, lens and
wire line, an Aether unloader) [M].

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

**Expected floors** [M: the Code agent runs `ChaptersOneToFourFloorTests` and records the
measured line in §18]:

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
chapter 1–4 body [M].

## 13. The Mainspring Overclocker, final

Roadmap decision 7 decides its *identity* after the full playthrough: if players never choose it
over a Hauler, it gets a second exclusive card, and the speed aura stays cut. This section fixes
everything else now, so the playthrough judges a finished chassis.

| Aspect | Final | Lives in | Note |
|---|---|---|---|
| Slots | 5 | `chassis.json` `maxAppendageSlots` | 3 inputs, or 2 inputs plus Repeat |
| Cost | 2 Mainspring + 20 Brass + 24 Casing + 12 Gear (unchanged) | `chassis.json` `cost` | Raw ~578; every line ≤ 100; opens at `mainspring` (ch. 8) |
| Card | 1 Mainspring + 5 Brass + 6 Casing + 3 Gear, prerequisite Mainspring (unchanged) | `Card_MainspringOverclocker` | The formula (§9). The station builds any chassis unclaimed; the card is what the Workbench's rack offers [M: confirm an unclaimed Overclocker built at the station can be programmed, as the Hauler is in chapter 5] |
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
keeps its program and clamps n on load [M: where `GolemProgram` restores quantities]. No new
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
playthrough's `overclocker` question weighs: is an Overclocker worth 578 raw goods and a bay slot
for that? Decision 7's remedy (a second exclusive card) stays the user's call after the
playthrough (§17 Q6).

## 14. Per-chapter budgets and floors

**Budget** is the human target, what `testscript/full-playthrough.md` times. **Ceiling** is what
the floor tests assert: two thirds of the budget, as chapters 1–4 assert 10:00 of 15:00. A floor
plays every action instantly but earns every good at the player's real rates (§15.4).

| Ch. | Steps | Budget | Ceiling | Floor today | Notes |
|---|---|---|---|---|---|
| 1 | 11 | 5:00 | 3:20 | 1:20 | |
| 2 | 7 | 4:30 | 3:00 | 4:15 | fails the new ceiling until §12's change |
| 3 | 7 | 3:30 | 2:20 | 2:16 | |
| 4 | 7 | 2:00 | 1:20 | 0:06 | |
| 5 | 12 | 20:00 | 13:20 | [M] | the Hauler's 80 Plate + 40 Gear + 30 Coke |
| 6 | 8 | 15:00 | 10:00 | [M] | copper belts |
| 7 | 6 | 10:00 | 6:40 | [M] | Floor 1, bays, pack rung 1, Ledger, save, load |
| 8 | 9 | 45:00 | 30:00 | [M] | eight goods; the floor builds one reference line each |
| 9 | 8 | 30:00 | 20:00 | [M] | the Zeppelin's 1,093 raw |
| 10 | 5 | 45:00 | 30:00 | [M] | `stage1` counts as its 6:00 nominal (§15.4) |
| 1–10 | 80 | **3:00:00** | 2:00:00 | | |

A full run of three hours to the Foundation leaves stages 2–4 an hour or more, inside roadmap
decision 1's 4–5 hour campaign. If a measured floor passes its ceiling, the numbers in this spec
are revisited; the ceiling is not raised to fit (as `ChaptersOneToFourFloorTests` treats 10:00).

## 15. Tests

### 15.1 Existing tests whose pinned numbers change

| Test | Pins | Change |
|---|---|---|
| `Core.Tests/World/SandboxSetupTests` | Scrap truckload 30, Aether price 40 (lines 83, 85) | 100; 80 |
| `Core.Tests/Economy/TruckloadMarketTests` | [M: which sizes and prices it reads from data] | follow §4.3 |
| `Core.Tests/ClockTower/ClockTowerStageCatalogTests` | stage 3's (Aether Conduit 3, Lens 24, Frame Section 2) | Lens 12 |
| `Core.Tests/Tutorial/ChaptersOneToFourFloorTests` | < 10:00 | adds per-chapter ceilings (§14); its `Needs` reads the Presser's cost from data already |
| `Core.Tests/Tutorial/TutorialGuideTests` | texts ≤ 240 chars | unchanged; must still pass with data-built prices |
| `Core.Tests/Economy/LayoutMattersTests` | 103, 0, 82, 101, 50 | [M] truckload sizes may move them; the table's shape must hold: straight ≫ apart = 0, ferry recovers most, belt ≈ straight, unemptied crate stops at 50 |
| `Core.Tests/Economy/PriceReachTests` | invariant 1 | adds bench recipes and the boiler hand-load (§15.2) |
| `Core.Tests/AssemblyLine/*`, `PlaytestKitTests`, scenarios `golems`, `workbench` | [M] any that claim `RepeatAssembly` before a Mainspring | grant the Ledger a Mainspring first |

### 15.2 `PriceReachTests`, widened

Add to the price list: each of the starter bench's crankable recipes' inputs ("bench R2: 1
Scrap"), and the boiler's hand-load (`HandLoadBatch` Coke). Closes `open-items.md` §4.

### 15.3 New: `Core.Tests/Economy/MaxPriceTests`

- `NoLineOfAnyPrice_IsAboveTheBasePack_ButFloorExpansion`: over the same list as `PriceReachTests`
  (placeables, chassis, every deck card's opening price, truckloads, bays, pack rungs, bench
  recipes, boiler hand-load), every good's summed line ≤ `packCapacityPerType`. Floor Expansion's
  purchases are listed separately and skipped, with a comment pointing to invariant 2.
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
  a reserved strip [M: cells in the rows `expand` adds, clear of every guide mark], claims the
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

It runs in seconds of wall time only if the world advances fast [M: today's chapters 1–4 take
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

1. **Guards first** (they fail, then pass): `MaxPriceTests` (all but `TheLensCostsThreeGlass` and
   `StageThreeStillBalances`, which fail until step 4), `PriceReachTests` widened,
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

## 17. Open questions for the user

Each is a choice with a recommendation.

1. **"No price above 100": per line, Floor Expansion exempt?** (a) As invariant 2 says: no line
   above 100 except Floor Expansion, whose larger lines are what the pack upgrades exist for.
   (b) Every line of every price, Floor Expansion too: purchases 2–6 shrink to ≤ 100 a line, and
   the pack ladder loses its purpose (`pack-upgrades-design.md` would be reopened).
   **Recommended: (a).**
2. **The Presser's price now, or after the Phase 2 playtest?** (a) Now, as §12: 15 seconds of slack
   is no slack for a real player. (b) Wait for `testscript/phase-2-playtest.md` and change it only
   if chapter 2 overruns. **Recommended: (a)**; the playtest then measures the retuned game.
3. **The market: bigger truckloads, or standing orders?** (a) §4.3's sizes (×3–4, same unit
   price): a data change, 7.6 orders a minute at stage 1. (b) A standing order per stall that
   reorders when the stall empties, charged to the wallet: a new system, its own spec, invariants
   and scenario. (c) Both. **Recommended: (a) now, and decide (b) from the playthrough's `market`
   answer.**
4. **Under-supply slows the tower quadratically** (§10.1), if the measurement confirms it. (a) Make
   the meter decay at the line's delivered rate instead of its demand, so a steady line under
   demand never starves and stage time is nominal × d/s, as §7 and the guide's text say. (b) Keep
   the rule and lower stage 1 to 3 Frame Sections a minute. (c) Keep both and reword `stage1`'s
   text. **Recommended: (a)**, specified in a short addendum once the numbers are in.
5. **Stage 4's Conduit trim** (§10.2): (a) only after the probe is optimised and the budget still
   fails, as written. (b) Trim it now, before profiling. **Recommended: (a)**; stage 3's Conduit
   line stands anyway, so the trim saves less than it seems.
6. **The Overclocker's identity**, after the playthrough (decision 7). If players never choose it
   for Repeat: (a) a second exclusive card, as decision 7 says (to be designed then); (b) Repeat's
   iterations run at 75 % of the recipe's ticks (`StepDurationRules.Repeat`): R1 52.6 Coke/min
   (+49 %), R8 37.5 (+31 %), R4 47.4 Plate (+30 %), a number, local to the golem, no aura; (c)
   leave it as the 3-input chassis only. **Recommended: play first with Repeat at 100 %; if the
   answer is "never", try (b) before designing (a)**, since it is one constant.
7. **Lens at 3 Glass needs stage 3's Lens halved** (§11). (a) Both, as proposed. (b) Neither:
   keep 2 Glass and stage 3 as built. **Recommended: (a).**

8. **Name the drainer too?** (`onboarding-feedback-design.md` §17.8) A golem waiting on a full
   target behind a sleeping drainer has the same blind spot as the feeder case. (a) Leave it out of
   Phase 3, as approved. (b) Add "Hauler #6 takes from it" by the same mechanism. **Recommended:
   (a)**, and ask the playthrough's `whole-run` notes whether anyone hit it.

## 18. Corrections and measurements, as built

Empty until the code phase. Every [M] above is answered here with the measured number and the
test that measured it.
