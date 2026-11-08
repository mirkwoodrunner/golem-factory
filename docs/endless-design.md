# Endless play after the Clock Tower

**Status: built 2026-11-07** (each section says what was built; the civic works' art is the user's
to commission). **Approved by the user on 2026-11-06 with every recommendation in §6 as written. Phase 8's third part** (`docs/roadmap.md`),
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

**Built (2026-11-07):** `Contracts/ContractGenerator`, set by `sandbox.json`'s
`endlessContracts`. Contract k (named `Endless_k`, what a save keeps) asks `goods[k % 10]` and
`goods[(k + 3) % 10]`, 60:40, for a raw-goods budget that grows over the first eight and holds at
3,300 (a quarter of what stage 4's demand already asks in ten minutes). Its reward is half that
in Casing, Mainspring, Mechanism, Regulator or Lens; the first twenty bonuses are the carts'
endless rungs (each good's Rate, then its Capacity), then a quarter in goods. Each posts once
the tower stands; before that the office says "once the Clock Tower stands". At the plateau a
single good asks up to 330, which is what the pack's 300 and 350 rungs carry in one trip.
Tests: `Core.Tests/Contracts/GeneratedContractTests`; the `endless` scenario takes the first.

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

**Built (2026-11-07), Core and a placeholder:** `Civic/CivicWorks` on `SandboxWorld.Civic`, the
works in `data/civic_works.json` (2, 3 and 3 stages, sized like the tower's: 480 to 720 s, the
tower's goods, Lens, Regulator, Casing, Brass). A second `ClockTowerSite` with
`PublishesTowerEvents` off runs each work's stages, so the meter, the min, the 3x cap and any
source all hold, and a stage built publishes `CivicStageCompletedEvent` (the alerts' note, the
claim chime, a glint), never the tower's event. The site is a 3x3 fixture west of the tower
(`TownSquare.CivicOrigin`), roped off until the tower stands. Its picture is one generated
placeholder for every work and stage (`Tools/Art/generate_civic_art.py`: `civic_site.png`,
`civic_site_closed.png`) until the works' art is commissioned. Saved in `SaveData.civic`; none in
Creative. Tests: `Core.Tests/Civic/CivicWorksTests`; the `endless` scenario builds the Tramway's
first stage.

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

**Built (2026-11-07):** `SandboxWorld.EndlessOpen` (the tower complete, or Creative) opens
every endless rung; there is no switch. In `data/sandbox.json`: the pack's `endless` rungs, 300
(10 Mechanism + 4 Regulator) and 350 (20 + 8); each cart's `campaignRungs: 3` and two more Rate
and Capacity rungs (rung 4 in Regulators and Casing, rung 5 in Chronometer Cores and
Regulators); `assemblyBays`, the campaign's top at 100 slots and two endless tiers (Mechanism +
Great Cog) to the golem cap of 112. Before the tower the bay row says "more once the Clock Tower
stands"; at the cap, "the most the city allows". Tests: `Core.Tests/Economy/EndlessRungTests`;
the `endless` scenario.

**Land is the hard limit.** The floor stops at `MaxNorthExtent` and the street at ±34. More land is
the second region, the Docks (`region-design.md`, built since: a 25-wide floor bought north in
rows, and a quay of eight more cart spots). So endless growth is mostly **density and rates**: more
from each cart, golems that do more, carts upgraded rather than added.

## 3. What the player sees

- **The end screen** gains a line: "The city has more work for you", pointing at the City Office.
- **The overview** gains running totals: goods made since the start, contracts done, civic stages
  done. One screen that says how far the factory has come.
- **The HUD's tower panel**, once the tower is complete, shows the current civic work (if any) the
  way it showed the tower's stage.

**Built (2026-11-07):** there is no separate end screen; the win is the last stage's banner,
which now says under "Stage 4 complete · The Chronometer": "The city has more work for you: the
City Office, beside the tower." (`ContractText.EndlessInvitation`). The tower's panel then
follows the civic work under way ("The Tramway 1/2 - 42% - x1.00"), or says the same line once
every work stands. The overview's running totals sit under the map
(`Overview/RunTotals`, `SandboxWorld.RunTotalsLine`): goods made since the game began (saved as
`progress.goodsMadeEver`; an older save counts from its load) and contracts done; civic stages
join it with the civic works.

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
- **Measured (2026-11-07, `performance-report-endless.md`):** at 160 golems the budget didn't hold.
  After optimising (the user's call), it holds at 112 and not at 118: **the golem cap is 112**.
  The bays' campaign top is 100 slots; endless play adds two tiers, to 112.

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
