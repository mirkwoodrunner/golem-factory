# Supply carts: a market street you build

**Status: proposed 2026-11-06, not yet approved. Phase 8's first part** (`docs/roadmap.md`),
before city contracts and endless play, because both assume a factory that runs without the
player at the stalls. Written before the code, like `pack-upgrades-design.md`.

## The user's calls (2026-11-06)

1. **The market is carts the player buys.** A cart restocks itself at a rate, as a **trickle**, up
   to a capacity. Its rate and capacity are upgraded for a price.
2. **Stalls and carts are one thing.** The truckload market goes: no orders, no price per load, no
   carts on the road, no hand-ordering.
3. **The street is wider, with spots for carts.** A spot without a cart is roped off, the way the
   Clock Tower's site is.
4. **The player starts with one cart: Scrap.** Every other cart is added as the player needs it.
5. **At a spot, the player can add, remove or change a cart, for a price**, standing where the
   cart is.
6. **A cart's good is offered once that good is needed**, and its price follows progression: a
   Coal cart costs only Scrap, later carts cost more advanced goods, and upgrades climb from there.
7. **The guide teaches the Coal cart where Coal and Coke are first needed** (chapter 1's `coal`).
8. **Contracts can grant an upgrade**, as well as the player paying for one.
9. **Removing a cart refunds half** of what was paid for it. Buildings refund in full; a cart
   refunds half.
10. **Chapter 3 teaches an upgrade**: the Coal cart's first Rate rung (§6).

## Why

The market today is nine hand-ordered stalls (`progression-design.md` §13.2): E at a stall pays
Scrap and sends one truckload. At the tower's first stage that is **7.6 orders a minute**
(`tuning-data-spec.md` §4.3), all by hand: playable for a campaign, impossible for endless play.

Ideas dropped on the way, so they aren't proposed again:

- **A till on each stall, fed with Scrap:** it forced one layout on every player, a Scrap line
  crowded along the street that had to be fed first.
- **Standing orders paid from the Pneumatic Post:** nothing before chapter 8.
- **Crates marked to pay the market:** kept a per-order cost and bent the wallet's rules.
- **New carts beside nine unchanged stalls:** two systems where one will do.

## 0. Invariants

A pass that touches carts, spots or the street keeps all of these. Each gets a test in
`Core.Tests` when it is built.

1. **A cart is a source to everything downstream.** It is a `ResourceNode` with a marker. Golems
   extract from it under the two-extractor cap (§3.2), the player harvests it with E, and its
   depletion tint works. No golem rule, card or endpoint changes.
2. **The trickle is deterministic and whole.** A rate is goods a minute. Each tick adds thousandths
   of a good to an accumulator on the `SimulationClock`, and whole goods move into the cart's
   stock. Integer math, no floats. Stock never trickles past capacity; the accumulator holds while
   the cart is full.
3. **No price per good.** Raw goods cost the carts and their upgrades, nothing per load.
4. **Every cost goes through the wallet** (`PlayerWallet.TryCharge`, pack then Post), and a refused
   charge takes nothing. **Every price fits the pack reachable when it opens**
   (`pack-upgrades-design.md` §1, `PriceReachTests`).
5. **Goods never vanish.** Removing or changing a cart puts its stock in the pack, and is refused
   if the pack can't hold it ("pack full of Coal"), as a demolition is.
6. **Removing a cart refunds half, a building refunds all.** A cart isn't a building, so the
   settled full-refund rule (`Core/Buildings/CLAUDE.md`) isn't bent: it never applies to carts.
   The half refund is half of every good the player paid for the cart and its rungs, each line
   rounded down. A rung a contract granted refunds nothing. The refund goes into the pack only,
   through `PlayerWallet.Refund` after `FirstMisfit`. The removal is refused, and nothing changes,
   if the pack can't hold the refund and the stock together.
7. **Upgrades stay with the cart.** A load restores every spot exactly: its cart, good, rungs,
   stock and accumulator, through `RestoreQuantity`, never clamping.
8. **A good is offered once the Ledger has seen it.** An empty spot lists only those goods. Every
   price shows before it can be paid: a line naming a good the Ledger hasn't seen reads
   `Requires: <good>`, greyed.
9. **The game can't be soft-locked.** The starting Scrap cart can't be removed or changed while it
   is the only Scrap cart, so Scrap always comes in. Every price is ultimately paid from Scrap.
10. **Priced in existing goods.** No new good, tier or currency (roadmap decision 1).
11. **Rules in Core, on `SandboxWorld`** (`SandboxWorld.Supply`), as plain classes a test drives
    without a scene. Nodes only draw and forward clicks. A `supply` scenario covers the street with
    real input.
12. **Append-only, no renames.** A new `InteractionKind` is appended. No definition name or step
    `Id` is renamed. An old save loads (§5).
13. **Creative Mode:** every action at a spot costs nothing (the wallet's waiver), and a Creative
    cart is infinite.
14. **Text:** never an id, only the font's glyphs, and every quoted price built from the data.

## 1. The street

**Seventeen spots** on the stall row (y = −16) at the four-cell pitch, x = −32 to 32.
`FloorLayout.StreetHalfExtent` goes from 18 to 34, keeping two cells of margin at each end.

- **One row**, so §13.1's reasons hold: buses run clean north into the workshop, and every cart
  keeps its two approach tiles.
- The nine places the stalls stand today are spots like the rest. Further spots are a longer walk
  and longer belts: §3.2's "growth means going further out".
- It changes the world's bounds, so `FloorLayout`, the street's floor and kerb, the camera's
  limits and the layout tests change with it.

**At the start, one cart:** Scrap, at x = −8, where the Scrap stall the guide's first step points
at stands today. It starts full. Every other spot is roped off, with a sign.

A cart is drawn in its good's market-cart art (the five already drawn). An empty spot shows the
rope and the sign.

## 2. At a spot

E at a spot opens a small panel where the cart stands. Its rules (rows, prices, refusals) live in
Core; the panel is drawn by the screens' builder, like the Crate screen.

- **An empty spot: Add.** A row for each raw good the Ledger has seen, each with that good's cart
  price. The cart arrives empty and starts to trickle.
- **A cart: Rate and Capacity.** Each row shows the next rung's price, or that it's at the top.
- **A cart: Change.** The other goods the Ledger has seen. It costs the new good's cart price, keeps
  the rungs, and puts the old stock in the pack (invariant 5). The cart starts empty as its new good.
- **A cart: Remove.** The spot goes back to rope. The stock goes to the pack, with half of what
  the cart and its rungs cost (invariant 6). The row shows what comes back before the click.

A cart's prompt on the street reads "Coal cart · 84 of 200 · +60 a minute". It has no order key.

**A contract can grant a rung** (`contracts-design.md`, to come): the cart's next Rate or Capacity
rung for nothing. It never skips a rung, and a cart at the top gains nothing.

## 3. Rate, capacity and price

These numbers are invented, and live in `sandbox.json` (`supplyCarts`), not in code. The
playthrough judges them; the floor tests check the guide's path.

**What a cart can usefully give.** Two extractors can pull from one cart at once. A Scavenger
extracts about 60 Scrap a minute (`tuning-data-spec.md`, Extract(1) and Push), so two pull about
120 a minute, more with Repeat. A rate above what its golems pull only fills the cart.

**Rates and capacities**, goods a minute and goods held:

| Rung | Scrap cart | Other carts | Aether cart |
|---|---|---|---|
| Bought | 120 · 200 | 30 · 100 | 10 · 40 |
| 1 | 180 · 300 | 60 · 200 | 20 · 80 |
| 2 | 240 · 400 | 120 · 300 | 40 · 120 |
| 3 | 300 · 500 | 180 · 400 | 60 · 160 |

**Scrap is the exception, because the manual era runs on it.** Today the free Scrap stall gives
100 for each order, as fast as the player orders. Chapter 1 alone spends about 170 Scrap (the
Coal cart, a boiler, a golem, a depot, the Plate cranked from Scrap). The starting Scrap cart
starts full at 200 and refills at 120 a minute, so chapter 1 never waits on it. The floor test
confirms it, or the numbers move.

**Prices follow progression.** Each good has its own ladder, starting at about the tier its good is
first needed and climbing a tier per rung:

| Cart | Offered from | Add | Rate 1 | Rate 2 | Rate 3 |
|---|---|---|---|---|---|
| Scrap | the start | 60 Scrap | 40 Iron Plate + 10 Gear | 40 Copper Ingot + 20 Brass | 20 Casing + 4 Mainspring |
| Coal | chapter 1 (`coal`) | 80 Scrap | 40 Iron Plate + 10 Gear | 40 Copper Ingot + 20 Brass | 20 Casing + 4 Mainspring |
| Copper Ore | chapter 6 (`copper`) | 60 Iron Plate + 20 Gear | 40 Copper Ingot + 20 Gear | 20 Brass + 10 Casing | 20 Casing + 4 Mainspring |
| Zinc Ore | chapter 8 (`zinc-ingot`) | 40 Iron Plate + 30 Copper Ingot | 20 Brass + 10 Casing | 20 Casing + 4 Mainspring | 10 Mainspring + 4 Aether Cell |
| Aether | chapter 8 (`aether-cell`) | 20 Brass + 10 Casing | 20 Casing + 4 Mainspring | 10 Mainspring + 4 Aether Cell | 20 Mainspring + 8 Aether Cell |

- A Capacity rung costs the matching Rate rung's goods at half the quantities.
- Every cart of a good costs the same: the second Coal cart costs what the first did.
- Every line fits the pack reachable when it opens: 100 from the start, 150 from chapter 7.

**Stage 1 check.** At full demand, stage 1 needs about 276 Scrap, 411 Coal, 112 Copper Ore and 56
Zinc Ore a minute (`tuning-data-spec.md` §4.2). At the top rates that's one Scrap cart, three Coal,
one Copper Ore and one Zinc Ore: six spots of seventeen. But a cart gives only what two
extractors pull, so at the Scavenger's 120 a minute it's three Scrap, four Coal, one Copper Ore
and one Zinc Ore: nine. A test pins that **the street's spots at the top rungs, pulled at two
extractors each, cover stage 1's raw demand**, with spots to spare.

## 4. What the player sees

- **An empty cart** reads as waiting, grey. A golem starved by it goes red through the existing
  starved warning, because then the player must act (an upgrade, or another cart).
- **A roped spot's sign** says "Add a cart here", and the goods it can offer.
- **The overview** gets a market view: each cart, its stock, and its rate in and out. That needs
  Phase 7's missing "goods spent" on the graph.

## 5. Saves

`SaveData.supply`, one entry per spot with a cart: the spot, the good, the two rungs, the stock and
the accumulator.

**An old save** (no `supply` field) had nine stalls, so it loads with a cart at rung 0 on each of
those nine places, holding the stall's saved stock. A cart that was on the road is delivered into
its cart's stock, which may hold it above capacity (a load never clamps). The game says so once,
as the version-0 crate migration does. The player loses nothing they had.

## 6. The guide

Every spot is on the street from the start. What a spot offers grows with the Ledger
(invariant 8). The guide's marks are read live (`guide-chapters-design.md` §0.4), and each cart the
guide asks for has a marked spot, so its later plans keep their geometry.

| Step | Ch. | Today | With carts |
|---|---|---|---|
| `scrap` | 1 | take Scrap at the Scrap stall | the same, at the starting Scrap cart (x = −8) |
| `coal` | 1 | order a Coal truckload (30 Scrap) and take Coal | **Buy a Coal cart** on the marked spot (x = −4) and take 5 Coal |
| `coal-order` | 3 | stock the empty Coal stall for the coker | **Upgrade the Coal cart**: its first Rate rung, before the coker is programmed |
| `copper` | 6 | order a Copper Ore truckload | buy a Copper Ore cart on the marked spot (x = 0) |
| `zinc-ingot`, `aether-cell` | 8 | target the Zinc and Aether stalls | buy each cart on its marked spot (x = 4, x = 8) first |

Every step keeps its `Id` (a renamed `Id` breaks saves) and its check where it still makes sense.

**`coal`'s new text**, built from the offer and under 240 characters: "Coal comes by cart. At the
marked spot, press E and add a Coal cart (80 Scrap). It fills by itself: press E at it to take 5
Coal." The kit grants the price, adds the cart and grants 5 Coal.

**Chapter 3 teaches an upgrade, on the Coal cart.** Coal rather than Scrap, because chapter 3 is
where Coal starts to run short: its coking Presser extracts from the Coal cart and makes 27 Coke a
minute, nearly all of the cart's 30, with the player still taking Coal by hand. The Rate rung takes
it to 60. Scrap isn't short yet in chapter 3: its cart gives 120 a minute.

- **Check:** the Coal cart's Rate rung is at least 1.
- **Text** (built from the offer, under 240 characters): "Your coker will eat nearly all the Coal
  your cart brings. At the Coal cart, press E and buy Rate (40 Iron Plate + 10 Gear): twice the
  Coal."
- **Kit action:** grant the rung's price and buy it.
- **What it costs chapter 3:** 40 Iron Plate, which the first Presser's crate is making by then,
  and 10 Gears, about 32 s at the bench. Chapter 3's floor is 1:22, and its ceiling is 2:20.

**What it costs chapter 1.** 80 Scrap where the truckload cost 30, which the full Scrap cart pays
at once. The cart's 30 a minute gives the step's 5 Coal in 10 s, against the truckload's 9 s
delivery. The spot is the Coal stall's place today, so the walk is the same. Chapters 1–4's floor
(`ChaptersOneToFourFloorTests`) is measured again with carts, and each chapter must stay under two
thirds of its budget, as Phase 3 required.

## 7. What it amends

- `progression-design.md` §13.1: the street runs to ±34 with seventeen spots.
- `progression-design.md` §13.2: truckloads and their per-order price are replaced by carts that
  trickle; the market's cost is per cart and per rung.
- `progression-design.md` §10's no-soft-lock rule: kept by the starting Scrap cart (invariant 9)
  instead of the free Scrap stall.
- `tuning-data-spec.md` §4 and §17 Q3: truckload tuning and the "standing order" remedy are
  replaced by §3 here.
- `guide-chapters-design.md`: the steps in §6.
- `TruckloadMarket` and its save of carts on the road are retired; the migration (§5) reads them.

## 8. Build order

1. Core: the cart (a `ResourceNode` with a trickle), the spots, the ladders from `sandbox.json`,
   add, change, remove and the rungs through the wallet, the save and its migration. Tests first.
2. The street: the wider bounds and floor, the spots, the rope and sign, a cart's sprite by good.
3. The panel at a spot, and the prompts.
4. The guide's steps (§6), and the floor tests measured again.
5. The overview's market view, and goods spent on the graph.
6. The `supply` scenario with real keys and clicks, then frames looked at.
7. The docs it amends (§7) and the folder rules.

## 9. Tests and the scenario

- **Core:** a trickle adds its rate exactly over a minute and stops at capacity; it's the same
  across a load mid-tick; add, change, remove and rungs charge through the wallet and a refusal
  takes nothing; change and remove put the stock in the pack or refuse; a removal refunds half of what was paid,
rounded down per line, and nothing for a granted rung; the last Scrap cart can't
  be removed or changed; a good unseen isn't offered; golems extract from a cart under the cap;
  every price fits the reachable pack; the street covers stage 1; a save round-trips; an old save
  becomes nine carts with its stock and its carts on the road; Creative costs nothing; chapters
  1–4's floor holds.
- **`supply` scenario:** walk to a roped spot, E, add a Coal cart with a click, watch it fill, buy a
  Rate rung, see a Scavenger extract from it, change it to Scrap, remove it, save and load and find
  the street as it was. Frames: a roped spot, the panel, a street of carts.

## 10. Questions

**Answered 2026-11-06:** removing a cart refunds half (invariant 6); chapter 3 teaches the Coal
cart's first Rate rung (§6).

**Still open:** the numbers (§3). They're invented; the floor tests check the guide's path, and the
playthrough judges the rest.
