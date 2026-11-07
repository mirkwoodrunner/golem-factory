# City contracts

**Status: built 2026-11-07. Phase 8's second part** (`docs/roadmap.md`), after supply carts
(`docs/supply-design.md`). Written before the code, like the other Phase 8 docs, with its
invariants first; §12 records what the build decided.

## What's already decided

- **Roadmap decision 12:** city contracts come first among the depth additions, as requests that
  fill the gaps between Clock Tower stages and give the Artificer's flexible half of the tabletop
  design a job.
- **Roadmap decision 13:** contracts are **open-ended, with a bonus for finishing quickly**. A hard
  deadline fights the cozy tone.
- **The tabletop design** (`game-design.md`): golems do rigid, automated logistics; Artificers do
  the flexible work, "drafting components, filing patents, swapping golem parts, and fulfilling
  contracts".
- **`supply-design.md`:** a contract can grant a cart's next upgrade.

**The user's calls (2026-11-06):**

1. **Contracts start earlier than the tower**: from chapter 5 (§3). This widens decision 12's
   "between tower stages" to the second half of the campaign.
2. **Any source counts** (invariant 3), **for the Clock Tower too**: "The freshly made rule
   doesn't make sense." The tower's fresh-production rule goes (§11).
3. **One contract at a time.** The office offers one; the next comes when it's done (§2).
4. **Every contract names who asks** (its requester).
5. **Rewards:** goods and cart rungs in the campaign; bigger rewards wait for endless play.

## The idea in one paragraph

The Clock Tower asks for a **rate**: a sustained flow of freshly made goods, which only golems can
give. A contract asks for an **amount**: so many goods, from anywhere. That's the Artificer's job:
hand-carried, taken from a crate or the Post, made at the bench, or a line run hard for a few
minutes. Contracts reward planning and stock, where the tower punishes both. Their rewards help the
next build-out.

## 0. Invariants

A pass that touches contracts keeps all of these. Each gets a test in `Core.Tests` when it is
built.

1. **A contract never fails.** No deadline, no penalty, no expiry. A contract waits until it's
   done.
2. **The bonus only adds.** Finishing within a contract's par time adds a bonus; finishing later
   gives the full ordinary reward. Missing the par is never shown in red, and no timer counts down
   in red.
3. **Any source counts.** Goods delivered by hand, by golem, by belt, from stock, from the Post: a
   contract counts each delivered good once, however it came. The same now holds for the tower
   (§11).
4. **Goods delivered are spent.** A contract consumes what it's given, up to its amount. It takes
   nothing it didn't ask for, and nothing past its amount.
5. **Rewards are never lost.** A reward goes into the pack if it fits, or waits at the office to be
   collected. Nothing is dropped.
6. **Deterministic.** Contracts come from an authored list in data, in a fixed order, gated by the
   Ledger. No randomness, so a run is the same every time and a test can play one.
7. **Priced and paid in existing goods.** No new good, tier or currency (roadmap decision 1).
   Rewards are goods, cart rungs, or other existing upgrades.
8. **Every contract can be met.** A contract names only goods the Ledger has seen and amounts that
   fit what the factory can make by then; a test walks the list against the Ledger at each point
   it opens.
9. **Optional for the campaign.** The tower's stages never need a contract, and the guide's path
   never needs one completed, so no chapter's measured floor (chapters 1–4's, or 5–10's
   ceilings) depends on them.
10. **Rules in Core, on `SandboxWorld`** (`SandboxWorld.Contracts`), as plain classes a test drives
    without a scene. Nodes only draw and forward clicks. A `contracts` scenario covers it with real
    input.
11. **Saves:** a load restores the board, each contract's delivered count, its start tick and any
    uncollected reward exactly. Append-only; no renames. An old save loads with the board at the
    point its Ledger and tower stage say.
12. **Creative Mode has no contracts**, as it has no tower stage.
13. **Text:** never an id, only the font's glyphs, every quoted amount built from the data.

## 1. Where contracts happen: the City Office

A small **City Office** stands in the town square, beside the Clock Tower's site: a fixture like the
tower, not in the build menu and refused by the wrecking bar. It's roped off until contracts open
(§3).

- **The notice:** E at the office opens the current contract.
- **The office's crate:** a crate-like input (an `ICrate`, like the tower's input endpoint) that
  takes only goods the current contract asks for. Golems push into it, belts feed it, and the player
  deposits by hand from the Crate screen. It holds nothing past the contract's amount.
- **The reward shelf:** a reward that didn't fit the pack waits here. E collects it.

The square is 17 cells wide around the tower's 3×3 site, so the office fits at one side with room
for a delivery line.

## 2. A contract

| Part | Example |
|---|---|
| Requester | "The Lamplighters' Guild" |
| Their words | "The new gas lamps on Brass Row want glass, and plenty of it." |
| What it asks | 60 Glass and 20 Lens |
| Par time | 8:00 from accepting it |
| Reward | 30 Casing |
| Bonus within par | a free Rate rung on a cart of the player's choice |

- **One or two lines** of goods per contract, so it reads at a glance.
- **Par time** runs on the `SimulationClock` from acceptance, so it follows Pause and the speed
  setting like everything else.
- **The notice shows** the requester and their words, what's asked, what's delivered, the reward,
  the bonus and the time left in the par, in plain ink. When the par passes, the bonus line just
  says "Bonus missed" and the contract carries on.

**One contract at a time.** The office offers one contract. The player accepts it with a click,
and it's theirs until it's done; the next is posted when it's done and its condition holds (§3).
There's no choosing between contracts and no setting one aside. One at a time keeps the office's
crate to one contract's goods, so golems feeding it never need to know what they're filling.

**Requesters** are named city guilds and offices, flavour only: the Lamplighters' Guild, the
Tramway Company, the City Watch, the Bellfounders, the Observatory. Each contract in data names one
and gives its line.

## 3. When contracts come

**The office opens in chapter 5** (the user's call), when the manual era is over and the factory
has its first smelter. Contracts come as **one sequence** in data, each with a condition for
posting it: a good the Ledger has seen, or a tower stage started. When a contract is done, the next
is posted as soon as its condition holds. Until then the office says what it's waiting for: "No
work today. The city will have more once you make Brass."

| Part of the game | Posted when | Asks for (examples) | Rewards (examples) |
|---|---|---|---|
| Chapters 5–6 | the office opens; Copper Ore seen | Iron Plate, Coke, Gear | Gear, a Coal cart rung |
| Chapters 7–8 | Copper Ingot, Brass, Glass seen | Copper Ingot, Brass, Glass | Brass, a Copper Ore cart rung |
| Chapter 9 | Casing, Lens, Mainspring seen | Casing, Lens | Casing, Mainspring |
| Tower stages 1–4 | each stage starts | Frame Section's inputs, Mechanism, Aether Cell, Regulator | components for that stage's build-out, rungs |

About sixteen contracts in the campaign: two each in chapters 5–8, a few in chapter 9, and two for
each tower stage. Each is sized for when it posts. Before the tower they're small enough to fill
in a few minutes of a line's output or a walk with the pack; from the tower on, their rewards help
that stage's build-out. After the tower is complete, contracts continue as endless play
(`endless-design.md`, to come).

## 4. Rewards

- **Goods**, chosen to help the next build-out: the components a stage's new lines need first.
- **A cart rung** (`supply-design.md` §2): the next Rate or Capacity rung, for nothing, on a cart
  the player chooses when collecting. A cart at the top can't be chosen; with no cart below the
  top, the rung waits on the shelf.
- **The bonus** is a second reward of the same kinds, not a multiplier, so the board can say
  exactly what it is.

A reward is never worth more than the contract cost to fill, counted in raw goods, so contracts
never become a cheaper way to make goods than making them. A test pins it.

## 5. What the player sees

- **The office's prompt:** "City Office · Lamplighters' Guild · 34 of 60 Glass".
- **The HUD's tower panel** gains a line for the active contract while one is active.
- **The overview's alerts** log a contract posted, completed and its bonus earned.
- **Never red** (invariant 2). Contracts are never stuck; the starved alarm doesn't apply.
- **A sound and a small effect** when a contract completes, from Phase 5 and 6's existing slots.

## 6. The guide

One step in chapter 5, when the office opens: "The city has work for you. Press E at the City
Office in the square, take the contract, and fill it however you like: by hand, from stock, or
with a line." Its check is a contract accepted, not completed (invariant 9). The kit accepts it.
Chapter 5 has no 15-minute budget, but its ceiling holds; the floor test measures the step.

## 7. Saves

`SaveData.contracts`: how far the sequence has got, the current contract's state (posted or
accepted), its delivered counts and start tick, the contracts done and whether each earned its
bonus, and the shelf. A save without the field starts the sequence at its first contract whose
condition holds, nothing delivered.

## 8. Build order

1. Core: the contract list in data (`data/contracts.json`), the board, the office's crate, delivery
   counting, rewards and the shelf, saves. Tests first.
2. The office fixture in the square, its rope, its art.
3. The board panel and the prompts; the HUD line; the alerts.
4. The guide step.
5. The `contracts` scenario with real input, then frames looked at.
6. The docs it amends and the folder rules.

## 9. Tests and the scenario

- **Core:** a contract counts any source once and takes nothing past its amount; it never fails;
  the bonus is earned within par and missed after it, the reward unchanged; a reward too big for
  the pack waits on the shelf; a rung reward skips nothing and refuses a cart at the top; one
  contract at a time, the next posted only when its condition holds; every contract names only
  goods the Ledger has seen when it posts; no reward beats its contract's raw cost; a save
  round-trips; an old save starts at the right contract; Creative has none.
- **`contracts` scenario:** walk to the office, E, accept the contract with a click, deliver part
  by hand from the Crate screen and the rest with a golem, see the bonus earned, collect the
  reward, see the next contract posted, save and load. Frames: the office, the notice, a completed
  contract.

## 10. Questions

**Answered 2026-11-06:** the office opens in chapter 5; one contract at a time; every contract
names its requester; goods and cart rungs as rewards; any source counts, for contracts and the
tower.

**Written with the data (2026-11-07):** the sequence's numbers, eighteen contracts in
`data/contracts.json` (§12). The playthrough judges them.

## 11. The Clock Tower: any source counts (amends `progression-design.md` §7)

**Today** a stage's rate for each good is the smaller of what's delivered and what golems
freshly assembled in the last 60 seconds (`effectiveRate = min(deliveryRate,
freshProductionRate)`, `ClockTowerSite.EffectiveRatePerMinute`). It was added so a player couldn't
stockpile a later stage's goods and unload them at 3× to finish the climax in minutes.

**The user's call:** the rule goes. A stage's rate is its **delivery rate** alone, however the goods
were made or where they were kept.

**What still holds:**

- Progress is gated by the weakest good (the `min` across demands) and capped at 3× demand, so a
  stockpile finishes a stage in a third of its nominal time at best, never instantly.
- The supply meter still clamps at 60 a good, so a dump can't bank credit beyond a minute.
- A stage still can't fail, only take longer, and the meter still drains at the delivered rate
  (`tuning-data-spec.md` §18).

**What changes:**

- A player who stockpiles can run a stage at 3× from stock while the lines are built out, the way
  contracts reward stock. That's now intended.
- `ClockTowerSite`'s fresh-production window and its `RecordFreshProduction` feed go, with what
  reads them: the readout's "fresh" figures and the HUD line built on them.
- Tests that pin the rule change: `ClockTowerFreshProductionSignalTests` (retired),
  `ClockTowerPartialSupplyTests`, `ClockTowerProgressTests` and `ClockTowerReadoutTests`.
- `Core/ClockTower/CLAUDE.md`'s "only freshly assembled goods count" is rewritten.
- The tower's measured stage times (`tuning-data-spec.md` §18) are checked again; with no stock
  they're the same, since fresh output and delivery were equal there.

It's built with contracts, as its own commit, before the office. **Built 2026-11-07**: the
fresh window, `RecordFreshProduction`, `ClockTowerProgress.EffectiveRate`, the readout's Fresh
column and the holder's subscription are gone; `ClockTowerFreshProductionSignalTests` became
`ClockTowerAnySourceTests` (the Assemble signal still pins the Ledger's "made"). The measured
stage times are unchanged: their tests fed delivery and fresh production at the same rate.

## 12. As built (2026-11-07)

- **Core:** `Core/Contracts/` holds `ContractDefinition` (the data), `ContractBoard`
  (`SandboxWorld.Contracts`, ticked on the clock), `CityOfficeCrate`, `ContractPanel` (the
  notice's lines, rows and presses) and `ContractText` (every player line). Tests:
  `Core.Tests/Contracts/ContractTests.cs`.
- **The office** stands three cells wide and two deep, east of the tower on its rows, three
  cells clear of it (`TownSquare.OfficeOrigin`), a fixture like the tower. Its art is
  generated (`Tools/Art/generate_office_art.py`: `city_office.png`, and
  `city_office_closed.png` with the cart spot's mahogany rope). It isn't an interactable
  building: the interactor has its own kind (`InteractionKind.Office`), so E opens the notice
  and I opens its crate, and E never collects from it as from a depot.
- **The office's crate** is a `StorageBuffer` whose room is the contract's
  (`StorageBuffer.RoomLimit`), published on all six footprint cells. Golems, belts and the
  Crate screen all stop at what is still needed, with no special case; the board takes and
  spends what lands every tick and every frame (a hand delivery counts while paused), and before
  a save. Before the contract is taken it takes nothing.
- **When contracts come:** each contract posts once the Ledger has seen every good in its
  `postWhenSeen`, which always names every good it asks (a test pins it), and, when set, once
  its tower stage has started (stage 1 is the rope coming down). The office opens with the
  first: Slag seen, the first smelter's byproduct.
- **Rewards:** a rung reward names its cart's good ("a Rate rung on a Coal cart"), and the
  player picks which cart of that good. `SupplyStreet.GiveRung` records nothing as paid, so a
  removal refunds nothing of it. §4's raw-cost test counts goods only: a given rung can never be
  turned into goods.
- **Saves:** the par is saved as ticks elapsed, not a start tick, since a load starts the
  clock's count again.
- **What the player sees:** the prompt follows the game's form, `City Office  -  City Watch ·
  34 of 40 Iron Plate`; the HUD line sits under the tower panel; the alerts log posted and done
  as notes in plain ink (`AlertLog.Note`), which no click follows. A completion rings the
  claim chime; a good counted pops over the door and a contract done glints.
- **The guide:** step `contract`, the last of chapter 5. The kit walks to the office, takes
  the contract and walks back.
- **The `contracts` scenario** covers §9 with real input; frames `contracts-roped`,
  `contracts-office`, `contracts-notice`, `contracts-taken`, `contracts-reward`, `contracts-next`.
