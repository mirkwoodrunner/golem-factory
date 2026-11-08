# Selling golems to the city

**Status: built 2026-11-08, with the user's answers in §7; §8 records where the build differs.** The last depth
addition the roadmap held back (decision 12: "a second region and selling golems wait"), chosen by
the user on 2026-11-08 after the Docks. Written before the code, like the other depth docs, with
its invariants first. The points marked *(default)* are mine, not the user's; each can change.

## What's already decided

- **The tabletop design** (`game-design.md`, "Liquidating Assets"): "Because golems are the
  ultimate commodity, an Artificer can take an action to permanently remove a fully functioning
  golem from their tableau and sell it to the city. This grants a massive burst of wealth
  proportional to the golem's complexity, but intentionally breaks the player's automated
  logistics chain, forcing them to rebuild."
- **Roadmap decision 1:** no new good, tier or currency. The game has no money: every price and
  reward is goods. "Wealth" can only be goods.
- **Demolition refunds the full cost** (settled). The wrecking bar already takes a golem and hands
  back its chassis and cargo, so a sale has to give something the bar doesn't: an order filled.
- **Contracts' §4:** a reward is never worth more than the contract cost to fill, in raw goods. The
  user kept this for golems (§7 Q3).
- **After the tower the golem cap binds first** (`endless-design.md` §2): 112 golems.

## The idea in one paragraph

From chapter 5, when the City Office opens, the office posts **golem orders** beside its contracts:
"The Tramway Company wants three Brass Pressers." The player fills one in either of two ways. By
hand: build a golem at the bench, carry it to the office, press E, and it's sold. Or automated: a
**Golem Foundry**, a new building, takes a chassis's goods from belts, golems or hands, builds the
ordered chassis by itself and ships it to the city, so a line can fill golem orders with no hands
at all. The city pays in goods, never more than the golems were worth. One order at a time, from a
formula, never failing.

## 0. Invariants

A pass that touches golem sales keeps all of these. Each gets a test in `Core.Tests` when built.

1. **No new good, tier, chassis or currency** (decision 1). One new building, the Foundry, and its
   art (a generated placeholder; commissioning it is the user's call).
2. **A golem sold by hand is gone, exactly once.** It leaves through the station's one removal path
   (birth and death in one file, `GolemConstructionStation`). Its cargo goes to the pack or the sale
   is refused whole (`FirstMisfit`, as a dismantle). No chassis refund on top of the reward.
3. **A Foundry's golem never enters the world.** It is built and shipped in one step: no
   `GolemEntity`, no node, no slot under the Assembly Bays or the cap, no frame cost.
4. **The city is never a cheaper way to make goods** (contracts §4): a reward is worth at most
   what the golems it pays for cost, in raw goods.
5. **Nothing fails.** An order has no deadline and no penalty. A Foundry with no order, or short
   of goods, is grey and waiting, never red.
6. **Deterministic.** Orders come from data and a formula, never chance.
7. **Optional for the campaign.** No guide step, tower stage or chapter floor needs a sale, so
   `FullRunFloorTests` doesn't move. The Foundry's price fits the pack in chapter 5
   (`PriceReachTests`).
8. **Rules in Core, on `SandboxWorld`.** Nodes only draw. A scenario covers both routes with real
   input.
9. **Saves restore exactly**, append-only: the current order and its count, each Foundry's held
   goods and build progress, the golems sold, any uncollected reward. An older save loads with the
   first order at the point its Ledger has reached.
10. **Every cost goes through `PlayerWallet`**; goods a Foundry uses up publish
    `EventBus.Consumed`, as the office's deliveries do.
11. **Creative Mode has no orders**, as it has no contracts. The Foundry can be placed there, free,
    and idles.
12. **Text:** never an id (a golem is "Presser #4"), only the font's glyphs, every quoted amount
    built from the data.

## 1. Golem orders

| Part | Example |
|---|---|
| Requester | "The Tramway Company" (the contracts' requesters, with lines of their own) |
| Their words | "Our depot wants hands that never tire." |
| What it asks | 3 Brass Pressers |
| Delivered | 1 of 3 |
| Reward | goods (§3) |

- **One order at a time, on its own line** *(default)*: the office keeps one contract and one golem
  order, so goods contracts carry on while an order is filled. The next order posts when one is
  done.
- **What counts is the chassis only** (§7 Q4). A golem sold by hand counts whatever its program;
  a Foundry's golem is a bare chassis.
- **Which chassis:** orders rotate through the chassis the Ledger has built, cheapest first:
  Scavenger, Presser, Hauler, Overclocker, Zeppelin. In chapter 5 that is the Scavenger and the
  Presser; a chassis joins the rotation once the player has built one. Before any chassis but the
  Scavenger has been built, every order is Scavengers.
- **How many:** an order's size is a raw-goods budget that grows over the first orders and then
  levels off, like generated contracts (`endless-design.md` §1.1); the count is the budget over the
  chassis's raw cost, at least 1. A Scavenger order asks for several; a Zeppelin order for one or
  two.

## 2. Two ways to fill one

### 2.1 By hand

Carry a golem (the existing carry) to the City Office and press E: "Sell Presser #4 to the
Tramway Company". The notice's prompt says it while carrying a golem of the ordered chassis. A
golem of another chassis is refused with the reason ("The order is for Brass Pressers"), and so is
one whose cargo the pack can't take. This is the tabletop's sale: the golem leaves its line.

### 2.2 The Golem Foundry

A building in the build menu, offered once golem orders open.

- **Size and place:** one cell, anywhere on buildable ground, like the construction bench
  *(default)*. It ships to the city by itself, as the Pneumatic Post moves goods, so it needs no
  route to the office.
- **Its input** is crate-like (an `ICrate`, like the office's crate): it takes only the goods of
  the current order's chassis, up to one chassis's cost of each. Golems push into it, belts feed
  it, and the player deposits by hand from the Crate screen.
- **Building:** when it holds a whole chassis's cost, it consumes it and builds over a set time
  *(default: data, rising with the chassis's tier)*, then ships the golem: the order counts one.
- **No steam** *(default)*: its cost is the goods and the time.
- **It follows the current order.** When an order changes chassis, goods held for the old one stay
  in its input until taken out by hand; it never builds a chassis no order asks for.
- **Several Foundries** may work one order. A Foundry building the last golem an order needs
  finishes it; a golem past the order's count waits in the Foundry for the next order of its
  chassis *(default)*, so nothing is lost.
- **What the player reads:** hover and the HUD prompt say what it is building and what it lacks
  ("Golem Foundry · Brass Presser · needs 40 more Scrap"), grey while waiting.
- **Price** *(default)*: in goods a chapter 5 factory makes (Scrap, Iron Plate, Gear), checked by
  `PriceReachTests`. Demolition refunds it and its held goods in full, as any building.

## 3. The reward

A golem's worth is its chassis's raw cost (`ContractGenerator.RawCosts`, as contracts count it),
plus a tenth for each filled card slot, so a programmed golem sold by hand is worth more than a
bare one (the tabletop's "proportional to complexity"):

| Chassis | Raw cost | Full (every slot) |
|---|---|---|
| Clockwork Scavenger | 12 | 14 (2 slots) |
| Aether Hauler | 70 | 98 (4) |
| Brass Presser | 80 | 104 (3) |
| Mainspring Overclocker | 398 | 597 (5) |
| Zeppelin Freight Loader | 645 | 1,032 (6) |

**The city pays at most the worth** (§7 Q3). It pays a share of the golems' worth when the order is
done *(default: half, as contracts pay, a data value)*, in goods the Ledger has seen, rotating
through the contracts' reward goods; a reward that doesn't fit the pack waits on the office's
shelf. So selling is a trade, not a profit. What it buys the player is an order filled, the
Foundry's line as something to build toward, and, after the tower, a slot under the cap.

## 4. What the player sees

- **The notice** shows the golem order under the contract: who, their words, the chassis and the
  count delivered, the reward.
- **The HUD's panel** gains the order's line while one is open.
- **The alerts** log an order posted and done; the claim chime and glint play on done, from the
  existing slots.
- **The overview's running totals** gain "golems sold".

## 5. Build order

1. Core: `GolemOrders` on `SandboxWorld` (the generator from data, the hand sale through the
   station's removal, the reward through the board's payout and shelf); saves. Tests first.
2. Core: the Foundry (its input, its build, its ship to the order; hover and prompt text); saves.
3. The office: E while carrying a golem sells it; the notice's and the HUD's order lines.
4. The Foundry's placeholder art, its view and the build menu row.
5. The overview's total and the alerts.
6. A `golem-sales` scenario with real input: carry a golem to the office and sell it; place a
   Foundry, feed it by belt, watch it ship; collect the reward; save and load. Frames looked at.
7. The docs it amends (`contracts-design.md`, `endless-design.md`, the roadmap, root `CLAUDE.md`'s
   scenario list and doc list).

## 6. Not part of this

- **Selling any golem at any time** at a fixed price (the user chose orders, §7 Q1).
- **A Foundry that builds programmed golems** for the world. It builds only for the city; golems
  for the factory are still built at the bench.

## 7. Questions: decided (user, 2026-11-08)

1. **The shape:** city orders, one at a time (recommended).
2. **When:** **with the office, in chapter 5** (not the recommended "after the tower").
3. **The reward:** at most the golems' worth (recommended).
4. **What an order checks:** **the chassis only**, "but we also need automation for sold": asked
   which, the user chose **a Golem Foundry** that builds the ordered chassis from fed goods and
   ships it to the city.

## 8. As built (2026-11-08)

- **Core:** `Contracts/GolemOrders` (`GolemOrderBoard` on `SandboxWorld.GolemOrders`, set by
  `sandbox.json`'s `golemOrders`), `Contracts/GolemOrderText`, `Buildings/PlaceableGolemFoundry`
  and `Buildings/FoundryText`. The hand sale is `SandboxWorld.SellGolem`, through
  `GolemConstructionStation.TrySellGolem`, which shares the dismantle's teardown and pays back the
  cargo only. A finished order pays through `ContractBoard.Pay`: the pack, else the office's shelf.
- **The numbers** (data): budget 640 raw goods at the plateau, growing over 8 orders, 1 to 8
  golems; the city pays half the worth; a filled slot adds a tenth; rewards rotate through Gear,
  Brass, Casing, Lens, Mainspring, Mechanism and Regulator, among the goods seen; the Foundry
  builds in 20, 45 or 90 seconds by tier. The first order is six Scavengers for 18 Gear.
- **The Foundry's price is 40 Scrap, 20 Iron Plate, 10 Gear and 10 Slag.** It is in the build
  menu from the start, like every building (the menu has no gating), so the Slag is what holds it
  back: Slag is the first contract's condition, so the price reads "Requires: Slag" until golem
  orders can post, and the Foundry gives Slag a use. Its hotkey is `=`, the menu's twelfth row.
- **E at the open office while carrying a golem** targets the office, not the golem in the
  player's hands (which rides with them and was always the nearest golem): it sells a golem the
  order wants, and opens the notice for any other.
- **The notice** shows the order under the contract (the panel grew to 640 tall); the HUD shows
  "Golem order · 1 of 6 Clockwork Scavengers" under the contract's line, or in its place.
- **A Foundry's golem** is spent goods and a build on the clock: it publishes `EventBus.Consumed`
  for the cost and `FoundryShippedEvent` on shipping, and its view glints and puffs. A golem past
  its order holds in the Foundry ("waits for an order of its kind") and ships when one comes.
  Demolition refunds its price, its intake and the chassis under way or held.
- **Art:** `golem_foundry.png` is a generated placeholder (`Tools/Art/generate_foundry_art.py`),
  the user's to commission.
- **Tests:** `Core.Tests/Contracts/GolemOrderTests` and `GolemFoundryTests`; the `golem-sales`
  scenario carries one golem to the office and sells it, then lets a Foundry fed by a golem fill
  the order, and saves and loads it mid-build.
