# Endless play after the Clock Tower

**Status: approved by the user on 2026-11-06 with every recommendation in §6 as written. Phase 8's third part** (`docs/roadmap.md`),
after supply carts (`docs/supply-design.md`) and city contracts (`docs/contracts-design.md`),
which it builds on. Written before the code, with its invariants first.

## What's already decided

- **Roadmap decision 1:** a polished campaign first, **then endless play after the Clock Tower**.
  **No new tiers of goods**: that would reopen tuning that was checked twice.
- **The win state** (`progression-design.md` §7): stage 4 chimes the tower, shows "The Clockwork
  Metropolis", and **leaves the save running**. The tower is a win, not a game over.
- **`pack-upgrades-design.md` §7 Q4:** a pack rung past 250 for endless play, priced in Mechanisms
  and Regulators.
- **`contracts-design.md`:** contracts continue after the tower; bigger rewards wait for endless
  play.
- **Waiting, not part of this:** a second map region, and selling golems to the city.

## The problem

After stage 4, nothing asks for anything. The tower's four Tier 5 goods (Frame Section, Great Cog,
Aether Conduit, Chronometer Core) have no other use, every rung is bought, and the floor is at its
cap. A factory of about 96 golems runs with nowhere to send its output. Endless play needs three
things: **something to aim at**, **room to grow**, and **a factory that still runs** at the size it
grows to.

## 0. Invariants

A pass that touches endless play keeps all of these. Each gets a test in `Core.Tests` when it is
built.

1. **No new good, tier or currency** (decision 1). Endless play asks for the goods the campaign
   already makes.
2. **It's what happens after the tower, not a mode.** There's no switch. A campaign save becomes
   endless when stage 4 completes, and an old save past stage 4 is already in it.
3. **Nothing fails.** As with contracts and the tower: no deadline, no penalty, only bonuses.
4. **Deterministic.** Everything endless play posts comes from data and a formula, never chance,
   so a test can play any point of it.
5. **Any source counts**, as for contracts and the tower (`contracts-design.md` §11).
6. **The frame budget holds at the factory's largest size** (§4). Growth is capped where the
   budget is measured to hold, not left open.
7. **Every price fits the pack reachable when it opens** (`PriceReachTests`), counting endless
   rungs.
8. **Rules in Core, on `SandboxWorld`.** Nodes only draw. A scenario covers each new surface with
   real input.
9. **Saves restore every endless counter exactly**, never clamping. Append-only; no renames.
10. **Creative Mode** has no contracts or civic works, as it has no tower; its rungs are free.
11. **Text:** never an id, only the font's glyphs, every quoted amount built from the data.

## 1. Something to aim at

### 1.1 Contracts keep coming

After the campaign's authored sequence (`contracts-design.md` §3), contracts are **generated** by a
formula, still one at a time:

- **Requesters** rotate through the city's guilds and offices, each with its own lines.
- **Goods** rotate through Tier 3–5 goods: Casing, Lens, Mainspring, Aether Cell, Mechanism,
  Regulator, and the four Tier 5 goods, which gives the tower's goods a use again.
- **Amounts grow** with each contract, then **level off** at a plateau, so a contract never asks
  for more than a factory at the golem cap (§4) can fill in about ten minutes. Variety comes from
  the rotation, not from ever-bigger numbers.
- **Rewards** are endless rungs (§2) while any are left, then goods.

### 1.2 Civic works (§6 Q1: yes, after generated contracts)

Contracts ask for amounts. **Civic works** ask for a **rate**, as the tower does: a new project in
the town square with stages, each demanding goods a minute, run by the tower's own stage machinery
(`ClockTowerSite`'s meter, the `min` across demands, the 3× cap). Three, in order:

| Work | Stages | Demands (examples) |
|---|---|---|
| The Tramway | 2 | Frame Section, Mechanism, Casing |
| The Observatory | 3 | Lens, Aether Conduit, Regulator |
| The Great Bell | 3 | Great Cog, Chronometer Core, Brass |

Each completed stage changes the square's picture, as the tower's stages do. It needs art: three
works and their stages, drawn through the existing pipeline (`art-pipeline-design.md`). Whether and
when that art is made is the user's call.

## 2. Room to grow

The campaign's ladders end. Endless play adds one or two rungs to each, priced in Tier 4–5 goods,
offered once stage 4 completes:

| Ladder | Campaign top | Endless rungs | Price (proposed) |
|---|---|---|---|
| Pack | 250 | 300, 350 | Mechanism + Regulator (`pack-upgrades-design.md` §7 Q4) |
| A cart's Rate and Capacity | rung 3 | rungs 4 and 5 | Regulator, then Chronometer Core |
| Assembly Bays | its top tier | one tier, up to the golem cap (§4) | Mechanism + Great Cog |
| Floor Expansion | six purchases (`MaxNorthExtent`) | §6 Q3 | Frame Section + Great Cog |
| Street spots | 17 (`supply-design.md` §1) | none: the street is full | — |

**Land is the hard limit.** The floor stops at `MaxNorthExtent` and the street at ±34. More land is
the second region, which waits (roadmap). So endless growth is mostly **density and rates**: more
from each cart, golems that do more, carts upgraded rather than added.

## 3. What the player sees

- **The end screen** gains a line: "The city has more work for you", pointing at the City Office.
- **The overview** gains running totals: goods made since the start, contracts done, civic stages
  done. One screen that says how far the factory has come.
- **The HUD's tower panel**, once the tower is complete, shows the current civic work (if any) the
  way it showed the tower's stage.

## 4. A factory that still runs

The frame budget was measured at **96 golems, 500 belt items and 12 boilers**
(`performance-report-phase3.md`). The simulation tick was far inside its budget (0.25 ms of 4 ms at
p95), so the tick isn't the limit; drawing and the per-frame readouts may be.

- **Measure first:** the `perf` scenario runs again at a larger load (proposed: 160 golems, 800
  belt items, 20 boilers), with the endless rungs bought.
- **Then cap:** the Assembly Bays' endless tier allows golems up to the largest load the budget
  holds, and no further (invariant 6). If 160 doesn't hold, the cap is what does, after the
  obvious optimisations.
- **The contract plateau** (§1.1) and the civic works' rates are sized to that cap.

## 5. Build order

1. The `perf` measurement at the larger load, and the golem cap it sets.
2. Endless rungs on the pack, carts, bays (and floor, if §6 Q3 says yes), priced in data.
3. Generated contracts after the authored sequence, with the plateau sized to the cap.
4. The end screen's line and the overview's totals.
5. Civic works, if §6 Q1 says yes: Core first, then the art when the user commissions it.
6. A scenario for each surface with real input, frames looked at, and the docs each one amends.

## 6. Questions: decided (user, 2026-11-06, every recommendation approved)

1. **Civic works: yes or no?** Recommendation: yes, after generated contracts are in. Contracts
   alone give an endless list of errands; civic works give a goal that a factory builds toward, as
   the tower did. The cost is three new sets of art.
2. **Contracts that level off, or grow forever?** Recommendation: level off (§1.1), so every
   contract stays fillable within the golem cap. Growing forever soon asks for more than the
   factory can ever make.
3. **More floor in endless play?** Recommendation: no. The floor's cap is what the factory was
   designed around, and more land belongs with the second region. The other choice is two more
   Floor Expansion purchases, priced in Tier 5 goods.
4. **The golem cap.** Recommendation: measure 160 golems first, and cap the bays at what holds.
