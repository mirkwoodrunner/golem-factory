# Golem Factory — Gameplay Progression Design

**Status:** design proposal, revision 3 — cleared for implementation. No code written. Intended as
the spec another agent implements against.

**Revision history.** Revision 1 failed a rubric review on five structural points. Revision 2
passed. Revision 3 is a tightening pass fixing two must-fix defects and a set of corrections.

<details>
<summary><b>Revision 2 — the five structural fixes (kept, unchanged in principle)</b></summary>

| Failure in rev. 1 | Root cause | Fix |
|---|---|---|
| Nothing was scarce, so no ratio was a real ratio | Infinite nodes, unbounded land, no upkeep. Every bottleneck's answer was "place another golem," and golems were free. | **§3 Scarcity**: steam power with per-golem Coke upkeep, a 2-extractor cap per node, a bounded purchasable floor |
| The Clock Tower demanded less factory than the player already owned, and the hoard cap punished surplus with idle time | Stage rates costed at ~9 golems against a 40-golem factory; progress was binary fed/not-fed | **§7**: demands raised 3–4×; progress scales with delivered rate |
| The load-bearing spatial mechanic was the one thing the code refuses to do | `GolemEntity.BeginRefine` is explicitly exempt from spatial routing because `IItemEndpoint.TryTake` is untyped | **§2**: golems get an internal inventory and a typed, quantified `Haul`; slot count becomes the gate |
| One activity at N=1,2,3 for four hours | The buffer ring was the only spatial verb and caps at 3 | Zeppelin gets the **Freight Link** |
| Two soft-locks; Slag/Glass backwards; manual era ~30 s | Bay cap too late and mispriced; untyped takes + capacity = deadlock | Bay cap from Phase 1; per-item-type capacity; **Slag Heap**; **Hand-Crank Bench** |

</details>

**Revision 3 changes:**

| Defect | Fix |
|---|---|
| **Steam upkeep did not bite, and its marginal cost was literally zero.** A flat 1 Coke/8 s per Boiler powering *up to* 8 golems means 7 of every 8 golems added are free and the 8th costs 7.5 Coke/min. Players optimise on the margin; the margin was zero. It also drained the starting Coke on a fixed hidden timer the player could not affect. | **§3.1**: Boiler consumption is now **proportional to powered golem count** — 1 Coke per powered golem per 10 s = **6 Coke/min each**. Every golem's cost is felt on placement, an idle boiler burns nothing, and the coal-and-steam line becomes **~41 % of the factory**. |
| **Every logistics golem was a no-op.** §2 had `Haul` fill *input* stock and `Push` empty *output* stock, with only `Assemble` moving between them — so `Haul → Push` (the Scavenger, every extractor, every belt-to-buffer golem: ~25 of the endgame factory) filled input stock to the cap and stalled forever. | **§2**: a program containing **no `Assemble` step treats input stock as output stock**. Stated explicitly, because "Push empties the entire output stock" is also what makes byproducts free. |
| §5.3(b) still framed convergence as rev. 1's ring-vs-mixed-belt binary, and never showed the 4-input case actually fits | **§5.3(b)** rewritten around **carrier golems**: a carrier holds several `Haul` steps and delivers multiple types through one face, so chassis slots buy back buffer faces. The R17 two-stage mix is worked through. |
| The Overclocker had no verb — one chassis got a spatial ability and the other got a number | **§6**: Overclocker alone may hold **`Repeat`**, re-running its `Assemble` N times from one Haul batch. Competes for a slot against a third input type on the same chassis. |
| Extractor rate omitted its `Push` step (240/min stated, **150** actual); coker stated 50/min, **35** actual | Corrected throughout §3.2, §7, §9. Node cap is 300/min, and stage 4 needs ~8 extractors across ~8 sites — which *strengthens* the go-further-out pressure. |
| The Workbench had no decisions left — recipe + chassis fully determined the program | **§2**: `Haul` quantity is **player-set** (1–12). Batch size trades Push/Haul overhead against held stock and buffer pressure, and the right answer differs per line. |
| Hoard-blitz hole: the multiplier read delivery rate, and delivering from a warehouse *is* a delivery rate | **§7**: multiplier derives from `min(deliveryRate, freshProductionRate)` over a 60 s window. |
| Phase 2's blackout was an ambush | **§8**: Boiler fuel gauge with a countdown and a 25 % alert. |

**Relationship to the other docs:**
- `docs/game-design.md` — tabletop source. Four mechanics promoted: the **Clock Tower** (endgame),
  the **Assembly Line** draft (tech unlock), the **Patent Registry** (replication), **Assembly
  Bays** (golem cap).
- `docs/digital-design.md` — the five-chassis roster and its order is the spine of §6. One
  deliberate deviation is flagged there.
- `docs/unity-implementation-plan.md` — §11 lists everything this needs that doesn't exist.

---

## 1. Where the game is today

Verified against live code, not docs.

| Thing | Current state |
|---|---|
| Item types | `Scrap`, `Brass`, `Aether` (`Economy/ItemType.cs`) |
| Recipes | Exactly one: `RefineBrass`, Scrap → Brass, 3 ticks |
| Aether | **Dead end.** Nothing consumes it. |
| Brass | Harvested from an infinite `BrassNode`. Not manufactured. |
| Chassis | *Purchased* with flat `scrapCost`/`brassCost`. 20/30/40/50/80 Scrap, 0/10/20/30/50 Brass. |
| Slots | 2, 3, 3, 4, 5 across the roster |
| `AppendageActionType` | `Haul`, `ExtractFromNode`, `Refine`, `LoadIntoBuffer` |
| Multi-input | **Impossible.** Single `inputItemType`/`outputItemType`. |
| Spatial routing | `BeginSpatialTransfer` — **Haul, ExtractFromNode and LoadIntoBuffer are literally the same code path** ("Every spatially routed action reduces to the same physical verb"). `Refine` is **explicitly exempt**: the source comment states that routing it spatially "would let it grab the wrong input off a mixed buffer and silently transmute it… until `IItemEndpoint` grows a typed take." |
| `StorageBuffer` | **No capacity model at all.** `Deposit` always succeeds; `CanGive()` is unconditionally `true`. |
| `ChassisDefinition.tier` | Field exists. Nothing reads it. |
| `AssemblyLineState` | Fully implemented cycling draft with cost decay. **Gates nothing** (deferred in M9). |
| `AssemblyBayStructure` | `MaxGolemSlots` + `TryUpgrade` implemented. **Not in the loop.** |
| `FloorLayout` | `public const int HalfExtent = 12` → a **25 × 25** floor. Read by `PlayerController.ClampToFloor` at runtime and by `SandboxFloorGenerator`, which is **Editor-only**. |
| `BeltPlacementRules.ShouldLink` | **Directional** — requires `TargetCell(from, facing) == to` and rejects head-on pairs. |
| `PatentRegistry` / `ArtificerFocusMeter` | Implemented, wired to the Workbench. |
| Tick rate | `TicksPerSecond = 10` → **1 tick = 0.1 s**. All durations below are ticks. |

---

## 2. The machine model

Revision 1 assumed multi-input assembly could read several typed ingredients off the tile behind
the golem. The code says that is precisely the thing it refuses to do, for a good reason. Rather
than fight it, invert it.

### A golem is a machine with an internal inventory

Each golem holds an **input stock** and an **output stock**, both `Dictionary<itemType, int>`,
capped at **12 units per type**. Steps move goods between the world and those stocks:

| Step | What it does | Duration |
|---|---|---|
| `Haul(itemType, qty)` | **Typed.** Takes up to `qty` of exactly `itemType` off the tile behind into input stock. `qty` is **player-set, 1–12**. Stalls if the tile behind doesn't hold that type. | `max(2, qty)` ticks |
| `ExtractFromNode(qty)` | Takes `qty` from the resource node behind. Node has one type, so no ambiguity. | `6 + qty` ticks |
| `Assemble(recipe)` | Consumes the recipe's inputs from **input stock**, atomically. Deposits output (+ byproduct) into **output stock**. Never touches a tile. | recipe `durationTicks` |
| `Repeat(n)` | **Overclocker only.** Re-runs the immediately preceding `Assemble` `n` more times from the same input stock. Stalls on the same shortfall rules. | `n ×` the repeated `Assemble` |
| `Push` | Empties the **entire output stock** onto the tile in front, mixed types and all. | `2 + unitCount` ticks |

This kills the untyped-take problem outright: `Assemble` reads from a typed dictionary the golem
owns, so it can never transmute the wrong thing. `IItemEndpoint` still needs a typed take for
`Haul` — `TryTake(string itemType, int quantity, out int taken)` — but that is a far smaller ask
than "typed multi-quantity atomic multi-ingredient takes from a tile," and it is required anyway
(see §10, the capacity deadlock).

> ### The pure-logistics rule — required, not optional
>
> **A program containing no `Assemble` step treats its input stock as its output stock.**
>
> Without this every logistics golem in the game is a no-op: `Haul(Scrap, 8) → Push` would fill
> input stock to the 12-per-type cap and stall forever, taking with it the Scavenger (the first and
> most common unit), every node extractor, and every belt-to-buffer golem — roughly a quarter of the
> endgame factory. The rule is stated as an explicit special case rather than by merging the two
> stocks, because keeping them separate is exactly what lets `Push` empty everything at once and
> makes byproducts free (Consequence 3).

### Consequence 1: slot count becomes the tier gate, physically

Every processing program has the same shape:

```
N × Haul   +   1 × Assemble   +   1 × Push      =   N + 2 slots
```

So **`maxAppendageSlots` alone decides how many input types a chassis can run.** There is no
`recipeTier >= chassis.tier` stat check anywhere in this design — the gate is the physical size of
the frame, which is exactly the tabletop's *"Chassis — The Constraint."* Roster slots change from
2/3/3/4/5 to **2/3/4/5/6** (a one-field asset edit each):

| Chassis | Slots | Input types it can run |
|---|---|---|
| Clockwork Scavenger | 2 | **None.** `Haul`/`Extract` + `Push` — pure logistics (see the rule above). |
| Brass Presser | 3 | 1 |
| Aether-Hauler | 4 | 2 |
| Mainspring Overclocker | 5 | 3 — *or* 2 plus `Repeat` |
| Zeppelin Freight Loader | 6 | 4 |

### Consequence 2: logistics cards regain identity

Under the current `BeginSpatialTransfer`, `Haul`, `ExtractFromNode` and `LoadIntoBuffer` are the
same code, so the Scavenger's "2 slots = Extract then Load" is genuinely "do the same transfer
twice." With an internal inventory they are three different verbs on two different sides of the
golem: `Extract`/`Haul` fill from behind, `Push` empties in front, and a transport golem
(`Haul(type, 8) → Push`) now cares *which type it names* — and, via player-set quantity, *how much
it carries per trip*.

> **Required change:** `Haul` and `Extract` must deposit into the golem, not straight through to
> the tile in front. `Push` must be a distinct verb. This replaces `LoadIntoBuffer`'s current
> meaning; keep the enum name if churn matters, but the semantics change.

### Consequence 3: the byproduct problem solves itself

`Push` empties the whole output stock, so an iron smelter producing Iron Plate **and** Slag needs
one `Push`, not two, and stays a 4-slot recipe. The destination buffer receives both types mixed —
harmless, because every downstream `Haul` is typed. **The mixed output buffer becomes the
interesting tile**, and with per-type capacity (§11) a Slag backlog stalls the smelter that feeds
it. That is the whole Slag economy in §5.3(c).

### Consequence 4: the Workbench keeps a decision

`N × Haul + Assemble + Push` is otherwise fully determined by recipe + chassis, which would leave
the game's signature drag-and-drop UI with zero choices and reduce the Patent system to skipping
boilerplate. **Batch size is the decision.** `Haul(Coke, 1)` versus `Haul(Coke, 8)` on the same
coker: the large batch amortises the fixed `Push` overhead (a coker goes from 35 to 46 cycles/min)
but holds 8 Coke hostage inside one golem and pulls 8 at a time out of a shared buffer, which
starves neighbours on a contended line. The right answer differs between a dedicated feed and a
shared throat, and it pairs directly with `Repeat` on the Overclocker.

### Cycle time — the formula every number below is derived from

A golem's cycle is the sum of its steps.

- **Node extractor** = `Extract(4)` 10t + `Push` 6t = **16 t → 150 units/min**
- **Coker** = `Haul(Coal,1)` 2t + `Assemble(R1)` 12t + `Push` 3t = **17 t → 35 Coke/min**
- **Casing assembler** = `Haul(IronPlate,4)` 4t + `Haul(Brass,1)` 2t + `Assemble(R9)` 28t + `Push`
  2t = **36 t → 16.7 Casings/min**

---

## 3. Scarcity: what makes a ratio a real ratio

With infinite nodes, unbounded land and no running cost, the answer to every bottleneck is "place
another golem," so a longer chain is just longer. Three constraints fix that. All three are local,
rigid and deterministic — none needs pathfinding or adaptive behaviour, and a golem halting on an
unmet power precondition is the existing rigidity rule applied to a new precondition, not a
departure from it.

### 3.1 Steam power — every golem has a marginal Coke price

- A **Boiler** (30 Scrap + 10 Iron Plate) burns **1 Coke per powered golem per 10 s = 6 Coke/min
  per golem**, and can power up to **8** of them.
- Consumption is **proportional, not flat**. This is the whole point: a flat per-boiler burn makes
  seven of every eight golems free and the eighth cost a fortune, so the marginal cost the player
  actually optimises against is zero. Proportional burn means placing a golem has a cost you feel
  on placement. It also means **an idle boiler burns nothing**, so the starting Coke stock is a
  budget the player spends by building rather than a hidden timer they cannot affect.
- Power reaches a golem by **adjacency**: the golem must be orthogonally adjacent to the Boiler or
  to a **Steam Pipe** (1 Iron Plate each) that chains back to one.
- An unpowered golem stalls with a new `StallReason.NoSteam`, naming the tile.

What this buys:

- **Coke becomes the contended throat of the entire game.** At the ~96-golem stage-4 factory that
  is **~576 Coke/min of upkeep** against ~206 Coke/min of smelting demand — upkeep is *the larger
  consumer*, and the coal-and-coke line ends up **~41 % of all golems** (23 cokers, 6 extractors,
  ~11 loaders out of ~96). That is Factorio-scale power infrastructure.
- **"Add another golem" has an immediate price.** Every expansion anywhere in the factory needs
  coal capacity provisioned first, so ratio problems have to be *solved* rather than out-built,
  and the coal line must grow **superlinearly** — more cokers need more steam, which needs more
  coke.
- **Convergence check (no runaway).** A coal cluster of 1 extractor + 4 cokers + 2 loaders = 7
  golems produces **140 Coke/min** and consumes **42**. Net **+98 per 7 golems, a 3.3 : 1 ratio.**
  Comfortably convergent — this is a real tax, not a death spiral, and it cannot soft-lock.
- **Land pressure.** Every golem must touch a pipe; pipes, belts and buffers all consume tiles.

### 3.2 Two extractors per node — growth means going further out

A node may be worked by at most **2 golems**. Fiction: the seam collapses if over-crewed. (The
4-adjacency of a tile is the physical backstop; the cap of 2 is the designed one.)

At 150 units/min per extractor a node caps at **300/min**. Stage 4 needs ~302 Scrap/min and ~856
Coal/min → **2 scrap heaps, 3 coal seams, 1 copper, 1 zinc, 1 aether ≈ 8 node sites**, none
adjacent. That means long belt corridors *and* long steam pipe runs out to each — real estate and
real infrastructure, which is what makes the Zeppelin's Freight Link (§6) land as a relief rather
than a curiosity.

### 3.3 Bounded, purchasable floor

Buildable area is the rendered floor (`FloorLayout`), which starts at **25 × 25 tiles**
(`HalfExtent = 12`). **Floor Expansion** paves a 6 × 6 block for `60 Scrap + 30 Iron Plate` — cheap
enough never to soft-lock, expensive enough that sprawl is a choice. The starting floor comfortably
holds the ~44-golem Phase-5 factory and is genuinely tight at 96.

> **Implementation note (see §11):** `HalfExtent` is a `const` read by the **Editor-only**
> `SandboxFloorGenerator`. Runtime expansion needs runtime tilemap repainting and wall
> re-placement — meaningfully more than "purchasable growth" implies. If that cost is unacceptable,
> the fallback is a larger fixed floor with the outer region gated by a one-time unlock rather than
> incremental paving; the design only needs land to be *finite and expensive*, not continuous.

---

## 4. The arc

The player begins alone in a cold workshop with a Hand-Crank Bench and a Boiler holding 240 Coke.
Interact harvests one lump of Scrap per press; holding Interact at the bench turns Scrap into Iron
Plate at a quarter of a golem's speed. The first golem — a Clockwork Scavenger, 12 Scrap — takes
about thirty seconds to afford and immediately out-gathers the player, which is the thesis of the
game stated early. But it cannot *craft*: crafting stays entirely in the player's hands for another
ten to fifteen minutes, while they hand-crank the 20 Iron Plate and 10 Gears the first Brass
Presser costs. And every Scavenger they build burns 6 Coke a minute off a gauge on the Boiler that
is now visibly ticking down — so the phase's decision, invest or grind, is also a decision about
how much runway to spend. When the Presser finally runs, the relief is earned. Then the Coke runs
out, and the player learns the real economy: golems burn coke to *exist*, and a coker only makes 35
a minute, so eight golems already need two of them. Phase 3 is the coal line and the steam grid,
where the coal-and-coke line grows into roughly two-fifths of everything the player owns. The
Aether-Hauler opens every two-input recipe at once and the factory becomes metallurgy: Brass
demands copper and zinc converging in one golem's input stock, and iron smelting starts producing
Slag, which has per-type buffer capacity, which means a Slag backlog stalls the smelter and starves
everything downstream — so Slag must be routed *every cycle*, either into a Slag Heap (which itself
burns Coke) or into Glass and Lenses. The Overclocker opens three-input recipes and Mechanisms, and
brings `Repeat`: batch an assembly four times off one haul, at the cost of the slot that would have
carried a third ingredient. The Zeppelin opens four-input recipes and the **Freight Link** — the
only way to move goods between non-adjacent tiles, which converts eight scattered node sites from
belt-corridor problems into remote outposts. And the game ends at the **Clock Tower**: four stages
whose progress scales with *freshly produced* delivered rate, so a factory running at three times
the demand finishes three times faster, while a warehouse buys sixty seconds and no more. Stage 4
demands roughly **1,390 raw units per minute — about 23 per second — against a player's
hand-gathering ceiling of well under one.**

### Phase table

| # | Phase | Player goal | Newly available | Newly gated | The new decision | Duration |
|---|---|---|---|---|---|---|
| 1 | **The Cold Workshop** | Hand-gather and hand-crank your way to the first Brass Presser | Hand harvest; Hand-Crank Bench (Tier-1 recipes at 25 % speed); Clockwork Scavenger (12 Scrap); Assembly Bays (cap 10); Boiler fuel gauge | The Presser costs 60 Scrap + 20 Iron Plate + 10 Gear — all hand-made | **Invest or grind, against a visible fuel budget.** A Scavenger automates gathering but burns 6 Coke/min off the 240 you start with. How much runway do you spend to save your hands? | 12–15 min |
| 2 | **The First Machine** | Get one Presser running before the Boiler gauge hits zero | Brass Presser (1-input recipes: Coke, Iron Plate, Glass, Gear, Wire); Boilers; Steam Pipes; belts | Everything — Coke runs out and stalls the factory | **Layout under two constraints at once**: a belt can only hand off to a belt (buffers need a `Push` golem), *and* every golem must touch a steam pipe. | 18–22 min |
| 3 | **Steam & Scale** | Build a coal line that outgrows its own consumption; expand to a second node site | Floor Expansion; Bay upgrades; multi-site belt + pipe corridors | Aether-Hauler (80 Iron Plate + 40 Gear + 30 Coke) | **Growth has a running price you feel per golem.** 6 Coke/min each against a coker's 35, so every four golems needs another coker — the coal line becomes ~40 % of the factory, and nodes cap at 2 extractors so scale also means distance. | 35–45 min |
| 4 | **The Metal Lines** | Aether-Hauler; all eight 2-input recipes; the Aether line | Smelting, Brass, Casing, Lens, Mainspring, Aether Cell; Slag Heap; carrier golems | Overclocker (2 Mainspring + 20 Brass + 24 Casing + 12 Gear) | **Convergence and disposal.** Multiple input types must reach one tile through four faces — spend chassis slots on carriers to buy back faces. And Slag now backs up: route it every cycle or the iron line stalls. | 50–60 min |
| 5 | **The Great Works** | Overclocker, then the Zeppelin; Mechanisms and remote outposts | 3-input recipes; `Repeat`; 4-input recipes; **Freight Link + Freight Mast** | The Clock Tower | **Remote logistics, and batch vs. breadth.** The Freight Link turns distant nodes into self-contained outposts. On the Overclocker, `Repeat` and a third ingredient compete for the same slot. | 50–60 min |
| 6 | **The Clock Tower** | Four stages of rate-scaled delivery | The tower site | — (win) | **Sustained rate, and whether to over-build.** Progress scales with fresh production up to 3×, so tripling a line genuinely finishes a stage in a third of the time. | 60–80 min |

**Total ≈ 4 h 15 m – 5 h 20 m.**

---

## 5. Items, recipes and the lines that interlock

### 5.1 Item list

Tier = depth in the tree.

#### Tier 0 — Raw (node-extracted; hand-harvestable)

| Item | id | Produced by | Consumed by |
|---|---|---|---|
| Scrap | `Scrap` (existing) | `ScrapNode` | R2, R4, belts (1 ea.), Boilers, Floor Expansion, Scavenger & Presser cost |
| Coal | `Coal` | `CoalNode` | R1 Coke |
| Copper Ore | `CopperOre` | `CopperOreNode` | R5 Copper Ingot |
| Zinc Ore | `ZincOre` | `ZincOreNode` | R6 Zinc Ingot |
| Aether Crystal | `Aether` (existing) | `AetherNode` | R12 Aether Cell |

> **Changes:** `BrassNode` is **deleted** — Brass is manufactured from Phase 4 on, never dug up.
> `AetherNode` becomes infinite (it is currently finite; see §10). All nodes are infinite but capped
> at 2 extractors (§3.2), so scarcity is *access*, not depletion — which cannot soft-lock.

#### Tier 1 — Basic processing (Presser, 1 input)

| Item | id | Produced by | Consumed by |
|---|---|---|---|
| Coke | `Coke` | R1 (1 Coal → 1 Coke) | R4, R5, R6, **every powered golem, via its Boiler**, the Slag Heap, Aether-Hauler cost |
| Iron Plate | `IronPlate` | R2 (reclaim, 1:1) and R4 (smelt, 2:1 + Slag) | R8 Gear, R9 Casing, R15 Frame Section, Steam Pipes, Boilers, Floor Expansion, Presser & Hauler cost |
| Slag | `Slag` | R4 byproduct | R3 Glass, **or voided in a Slag Heap at 1 Coke per 4 Slag** |
| Glass | `Glass` | R3 (1 Slag → 1 Glass) | R10 Lens |

#### Tier 2 — Metals (Hauler, 2 inputs; Wire is 1 input)

| Item | id | Produced by | Consumed by |
|---|---|---|---|
| Copper Ingot | `CopperIngot` | R5 | R7 Brass, R19 Copper Wire |
| Zinc Ingot | `ZincIngot` | R6 | R7 Brass |
| Brass | `Brass` (existing) | R7 (2 Cu + 1 Zn → 3) | R9, R10, R11, R14, R15, R16, R18, Overclocker & Zeppelin cost — **7 consumers** |
| Copper Wire | `CopperWire` | R19 (1 Cu Ingot → 3) | R14 Regulator, R17 Chronometer Core, R18 Aether Conduit |

#### Tier 3 — Components

| Item | id | Produced by | Consumed by |
|---|---|---|---|
| Gear | `Gear` | R8 (2 Iron Plate) | R11, R13, R14, Hauler & Overclocker cost |
| Casing | `Casing` | R9 (4 Plate + 1 Brass) | R13, R15, Overclocker & Zeppelin cost |
| Lens | `Lens` | R10 (2 Glass + 1 Brass) | R12 Aether Cell, R17 Chronometer Core, Zeppelin cost |
| Mainspring | `Mainspring` | R11 (3 Brass + 2 Gear) | R13, R16, Overclocker & Zeppelin cost |
| Aether Cell | `AetherCell` | R12 (1 Aether + 2 Lens) | R14, R18, Zeppelin cost |

#### Tier 4 — Mechanisms

| Item | id | Produced by | Consumed by |
|---|---|---|---|
| Mechanism | `Mechanism` | R13 (Overclocker, 3 in) | R16 Great Cog, R17 Chronometer Core |
| Regulator | `Regulator` | R14 (**Zeppelin, 4 in**) | R17 Chronometer Core |

#### Tier 5 — Megaproject goods

| Item | id | Produced by | Consumed by |
|---|---|---|---|
| Frame Section | `FrameSection` | R15 (Overclocker) | Clock Tower stages 1, 2, 3 |
| Great Cog | `GreatCog` | R16 (Overclocker) | Clock Tower stages 2, 4 |
| Aether Conduit | `AetherConduit` | R18 (Overclocker) | Clock Tower stages 3, 4 |
| Chronometer Core | `ChronometerCore` | R17 (**Zeppelin, 4 in**) | Clock Tower stage 4 |

**24 item types. Dead-end audit: zero.** Every Tier 0–4 item has at least one listed consumer;
Brass has seven and Coke has five (three recipes, every powered golem, the Slag Heap). `Aether` —
the current build's actual dead end — now runs Aether Cell → Regulator/Conduit → Chronometer Core →
Clock Tower. `Slag` has a productive consumer (Glass) *and* a costed sink (the Slag Heap), required
to avoid the deadlock in §10. The four Tier-5 goods are terminal by design; the Clock Tower is
their sink and the win condition.

### 5.2 Recipe list

`In` = number of distinct input types = the minimum chassis (`slots = In + 2`).

| # | Recipe | Inputs | Output | Byprod. | Ticks (s) | In | Min chassis |
|---|---|---|---|---|---|---|---|
| R0 | **Extract** (5 nodes) | — | `qty` raw | — | 6+qty | — | Scavenger |
| R1 | Coking | 1 Coal | 1 Coke | — | 12 (1.2) | 1 | Presser |
| R2 | Scrap Reclamation | 1 Scrap | 1 Iron Plate | — | 24 (2.4) | 1 | Presser |
| R3 | Glassmaking | 1 Slag | 1 Glass | — | 20 (2.0) | 1 | Presser |
| R8 | Gear Cutting | 2 Iron Plate | 1 Gear | — | 16 (1.6) | 1 | Presser |
| R19 | Wire Drawing | 1 Copper Ingot | 3 Copper Wire | — | 18 (1.8) | 1 | Presser |
| R4 | **Iron Smelting** | 2 Scrap + 1 Coke | 2 Iron Plate | **1 Slag** | 24 (2.4) | 2 | Hauler |
| R5 | Copper Smelting | 2 Copper Ore + 1 Coke | 1 Copper Ingot | — | 30 (3.0) | 2 | Hauler |
| R6 | Zinc Smelting | 2 Zinc Ore + 1 Coke | 1 Zinc Ingot | — | 30 (3.0) | 2 | Hauler |
| R7 | **Brass Alloying** | 2 Copper Ingot + 1 Zinc Ingot | **3 Brass** | — | 30 (3.0) | 2 | Hauler |
| R9 | Casing Press | 4 Iron Plate + 1 Brass | 1 Casing | — | 28 (2.8) | 2 | Hauler |
| R10 | Lens Grinding | 2 Glass + 1 Brass | 1 Lens | — | 24 (2.4) | 2 | Hauler |
| R11 | Mainspring Winding | 3 Brass + 2 Gear | 1 Mainspring | — | 40 (4.0) | 2 | Hauler |
| R12 | Aether Containment | 1 Aether Crystal + 2 Lens | 1 Aether Cell | — | 36 (3.6) | 2 | Hauler |
| R13 | **Mechanism Assembly** | 1 Mainspring + 3 Gear + 1 Casing | 1 Mechanism | — | 60 (6.0) | 3 | Overclocker |
| R15 | Frame Section | 10 Casing + 6 Iron Plate + 4 Brass | 1 Frame Section | — | 90 (9.0) | 3 | Overclocker |
| R16 | Great Cog | 4 Mechanism + 6 Brass + 2 Mainspring | 1 Great Cog | — | 100 (10.0) | 3 | Overclocker |
| R18 | Aether Conduit | 3 Aether Cell + 4 Brass + 6 Copper Wire | 1 Aether Conduit | — | 80 (8.0) | 3 | Overclocker |
| R14 | **Regulator** | 1 Aether Cell + 2 Brass + 1 Gear + 4 Copper Wire | 1 Regulator | — | 56 (5.6) | **4** | **Zeppelin** |
| R17 | **Chronometer Core** | 2 Regulator + 2 Mechanism + 2 Lens + 2 Copper Wire | 1 Chronometer Core | — | 120 (12.0) | **4** | **Zeppelin** |

**20 crafting + 5 extraction = 25 recipes.** Ladder: Presser 5, Hauler 8, Overclocker 4,
Zeppelin 2 — plus `Repeat` and the Freight Link.

**What multi-input assembly must support:** 1–4 distinct input types; per-input quantities 1–10;
output quantity > 1 (R4, R7, R19); exactly one optional byproduct with its own quantity (R4 only);
deterministic integers — no probability, fluids, heat or catalysts. Semantics mirror the existing
`Refine`: check all inputs against **input stock** atomically, withdraw nothing if any is short,
stall naming *the specific missing item and shortfall*, withdraw up front on success, count
`StepProgressTicks`, deposit output and byproduct into **output stock** at completion.

### 5.3 Cross-line dependencies

**(a) Coke is the throat, and upkeep is its largest consumer.** R4, R5 and R6 all consume it; the
Slag Heap charges 1 Coke per 4 Slag voided; and **every powered golem burns 6 Coke/min**. At stage
4 that is ~206 Coke/min of smelting against **~576 Coke/min of upkeep** — power is the bigger
draw. Because upkeep scales with golem *count*, the reflexive answer to any bottleneck ("build
another smelter") makes the coke problem worse by 6/min immediately, so the coal line must be
over-provisioned ahead of every expansion. This is the single mechanism that converts every
downstream ratio from advisory to binding.

**(b) Convergence: four input types, four buffer faces, and carriers as the currency.**

The assembler `Haul`s every input from the **one tile behind it**, so all of a recipe's types must
reach a single buffer. A buffer tile has four faces and the assembler occupies one, leaving three
feeders. R13/R15/R16/R18 (3 inputs) fit directly. **R14 and R17 have four inputs and do not fit
naively** — which is the interesting case.

What resolves it is that a **carrier** golem can hold several `Haul` steps and deliver multiple
types through a single face. A Scavenger (2 slots) carries one type; an Aether-Hauler (4 slots)
running `Haul(A) → Haul(B) → Haul(C) → Push` carries three. Worked example for **R17 (Regulator +
Mechanism + Lens + Copper Wire)**, a two-stage mix:

- **Buffer A** — Regulator feeder (face 1), Mechanism feeder (face 2), carrier pulling both (face 3).
- **Buffer B** — that carrier pushing Regulator + Mechanism (face 1), Lens feeder (face 2), Copper
  Wire feeder (face 3), **the Zeppelin assembling on face 4.**

It fits, with one face to spare at buffer A. The generalisable trade the player learns here is:
**chassis slots buy back buffer faces.** Every type a carrier consolidates frees a face downstream,
so a 4-slot carrier is often worth more than a 4-slot assembler. The alternative is the mixed belt
— one belt carrying several types into one `Push` golem, one face instead of three, but the buffer
now fills at whatever ratio the belt happens to carry, and per-type capacity means a copper overrun
does not block zinc but *does* consume the copper slot. Both are legitimate: consolidation costs
golems and steam, mixed belts cost determinism.

**(c) Slag is a disposal obligation, and Glass is the reward for solving it well.** R4 yields 1
Slag per 2 Iron Plate, forever, whether or not the player wants it. With per-type buffer capacity
(§11) a full Slag slot means `Assemble` cannot deposit its byproduct, so **the smelter stalls — and
with it iron, gears, casings and every branch below.** Slag must be routed every cycle. Two sinks:

- **Slag Heap** — voids it, but burns **1 Coke per 4 Slag**, i.e. disposal competes directly with
  the boilers and the smelters for the scarcest intermediate.
- **Glass line** — R3 converts it 1:1 into the sole input of Lenses, the sole input of Aether
  Cells, required for Regulators and Conduits.

The balance genuinely flips over the campaign. At **stage 4**, iron smelting runs ~192 Plate/min →
**96 Slag/min produced**, while Lens demand (~16/min) consumes only **32** — so ~64 Slag/min must
be voided at a cost of 16 Coke/min, and expanding the Lens line reclaims that coke *and* feeds the
Aether chain. At **stage 3**, Lens demand of 24/min needs **48 Slag against roughly 46 produced** —
the player must actively over-smelt to keep the Lens line fed. Same mechanic, opposite sign, at
different points in the game.

**(d) Copper Ingot is split between Brass and Wire.** R7 (Brass, seven consumers) and R19 (Wire,
three) both eat it, with no substitute for either. Expanding copper is the highest-leverage
infrastructure investment from Phase 5 on.

**(e) Iron Plate is the universal filler and the tempo-setter.** Gears, Casings, Frame Sections,
Steam Pipes, Boilers and Floor Expansion all consume it — so the smelter rate that (c) forces the
player to over-build always has somewhere to go, which prevents (c) from creating a Plate surplus
deadlock.

```
Coal ─► Coke ─┬─► R4 Iron ─┬─► IronPlate ─┬─► Gear ─┬──────────┐
              ├─► R5 Copper│              ├─► Casing│          │
              ├─► R6 Zinc  │              ├─► FrameSection     │
              ├─► EVERY POWERED GOLEM     ├─► SteamPipe/Boiler │
              └─► SlagHeap └─► Slag ─► Glass ─► Lens ◄─ Brass  │
CopperOre ─► CuIngot ─┬─► Brass ◄─ ZnIngot ◄─ ZincOre          │
                      └─► CopperWire ─────┐                    │
Aether ─► AetherCell ◄─ Lens              │      Mainspring ◄──┘
   ├─► Regulator ◄─ Brass, Gear, Wire ────┤            │
   └─► AetherConduit ◄─ Brass, Wire       │      Mechanism ◄─ Gear, Casing
                                          │            ├─► GreatCog
                       ChronometerCore ◄──┴────────────┘
```

---

## 6. Chassis sequencing and costs

Slot count is the only gate (§2). Costs are item bundles, paid in manufactured goods from the
second chassis on.

| # | Chassis | Slots | Inputs | Cost | Why *here* |
|---|---|---|---|---|---|
| 1 | **Clockwork Scavenger** | 2 | 0 | 12 Scrap | Cannot craft — `Haul`/`Extract` + `Push` under the pure-logistics rule, exactly "supplies basic input lines." Cheap enough to afford in ~30 s so Phase 1 can't soft-lock, and it is the only thing that can staff a node while the player is at the crank bench. |
| 2 | **Brass Presser** | 3 | 1 | 60 Scrap + 20 Iron Plate + 10 Gear | The Plate and Gears must be **hand-cranked** (~10–15 min), so the first Presser is the payoff for the manual era. 3 slots = the minimum machine. Its cost is deliberately free of anything a Presser makes — otherwise circular. |
| 3 | **Aether-Hauler** | 4 | 2 | 80 Iron Plate + 40 Gear + 30 Coke | Every ingredient is Presser-craftable, so Phase 3 is spent scaling three simple lines to afford it. Opens **all eight** 2-input recipes at once, including R12 Aether Containment — literally the only recipe that touches raw Aether. "Safely moves high-value, unstable Aether crystals" becomes true rather than flavour. Also the first viable **carrier** (3 `Haul`s + `Push`). |
| 4 | **Mainspring Overclocker** | 5 | 3 | 2 Mainspring + 20 Brass + 24 Casing + 12 Gear | Named for its own cost. Opens Mechanism, Frame Section, Great Cog and Aether Conduit — the whole Tier-4/5 middle — **and `Repeat`**. |
| 5 | **Zeppelin Freight Loader** | 6 | 4 | 6 Mainspring + 8 Lens + 3 Aether Cell + 30 Casing + 40 Brass | The largest purchase in the game; ~12–18 min of a mature Phase-4 factory. Every ingredient is Hauler-tier, so it is reachable without an Overclocker. Opens the only two 4-input recipes **and** the Freight Link. |

**Acyclicity, checked:** Scavenger and Presser cost only hand-obtainable goods. Hauler costs only
Presser output (Iron Plate via R2, Gear via R8, Coke via R1). Overclocker costs only Hauler output.
Zeppelin costs only Hauler output. **No chassis requires a good that only it can produce.**

### The Overclocker's verb: `Repeat`

The Overclocker alone may hold a **`Repeat(n)`** appendage, which re-runs the immediately preceding
`Assemble` `n` more times from the same input stock. It amortises the fixed `Haul`/`Push` overhead
across a batch: a Casing assembler at `Haul(Plate,8) → Haul(Brass,2) → Assemble → Repeat(1) → Push`
runs 2 Casings per 8+2+28+28+4 = 70 ticks = **17.1/min** against the single-shot 16.7/min, and the
gap widens sharply on short recipes with big batches.

It is local, rigid, non-adaptive, and applies no multiplier to any other golem. Crucially it
**costs the fifth slot** — so on the same chassis `Repeat` and a third ingredient are mutually
exclusive, which makes it a decision rather than an upgrade. And it is the mechanic that finally
makes the **12-per-type input cap** bind: `Repeat` on R15 (10 Casing) is impossible, because two
iterations would need 20 Casing in stock.

### The Zeppelin's verb: the Freight Link

The Zeppelin alone may hold **`FreightLaunch`**. A **Freight Mast** (20 Brass + 10 Casing) is placed
anywhere on the floor; a Zeppelin is bound to exactly one mast at placement. `FreightLaunch` empties
the golem's output stock onto the mast's tile — **regardless of distance**. One-way, fixed pair, 24
ticks, no pathfinding, no route-finding, no adaptivity: a rigid link between two authored points,
the same kind of object a belt is.

§3.2 pushes eight node sites apart and §3.1 makes every belt corridor also need a parallel steam
pipe. The Freight Link turns a distant site into a **self-contained outpost** — local boiler, local
extractors, local Presser, one Zeppelin, one mast back home — a genuinely different spatial problem
from the belt-corridor layouts of phases 2–4.

### Cut: the Overclocker's speed aura

Revision 1 gave the Overclocker an adjacency speed multiplier. **Cut.** A flat multiplier is a
bigger number, not a decision; its placement question has one obvious answer (the current
bottleneck); and a non-local effect that changes how a neighbouring golem behaves contradicts the
rigid-local-determinism identity the game is built on. `Repeat` replaces it with a local verb.

> **Documented deviation from `docs/digital-design.md`:** its roster line says the Overclocker
> "projects a harmonic wave that boosts the speed of surrounding machinery." This design does not
> implement that. `Repeat` preserves the *idea* — this is the chassis that makes machinery run
> faster — but confines the effect to the golem holding the card. If the wave flavour matters, it
> should be a purely visual effect with no mechanical payload.

---

## 7. The Clock Tower

Four stages, placed in the world at the start of Phase 6, with an input buffer tile golems `Push`
into like any other.

### Progress mechanics

Per demanded item the site tracks a **supply-pressure meter**: `+1` per unit delivered,
`−rate/60` per second, clamped to `[0, 60]`.

```
effectiveRate_i        = min( deliveryRate_i , freshProductionRate_i )     // 60 s rolling windows
stageProgressPerSecond = min over demanded items of  clamp(effectiveRate_i / demandRate_i, 0, 3)
```

- Progress **scales with delivered rate**, capped at 3×. A factory running 3× the demand finishes a
  stage in a third of the time — surplus is rewarded.
- Progress is **gated by the weakest line** (`min` across items), so all lines must run.
- If any meter hits **0**, progress is `0` — frozen, never negative. An alert names the starved
  item; the HUD shows the deficit in items/min.
- **`freshProductionRate` closes the hoard-blitz hole.** The 60-unit clamp caps *stock* credit, but
  delivering from a warehouse is still a delivery rate, so a player could over-produce stage-4 goods
  during stages 1–3 and then unload at 3× to finish the climax in three minutes. Deriving the
  multiplier from the smaller of delivery and **fresh `Assemble` output over the last 60 s** means a
  stockpile can smooth a dip but can never raise the multiplier. (The exploit was partly
  self-limiting — the `min()` gate means capacity diverted to stockpiling slows the current stage —
  so this is protecting the climax's drama more than its difficulty.)

Stages cannot fail — only take longer. That is the rubric-5 guarantee.

### Stages

Nominal duration is at exactly 1× supply; at 3× it is a third of that.

| Stage | Name | Demands (sustained) | Nominal | Est. golems at 1× | Introduces |
|---|---|---|---|---|---|
| 1 | **Foundation** | Frame Section @ 6 /min | 6 min | **~63** | The tower loop. One line, at six times the rate a Phase-5 factory produces — **arrival requires immediate build-out**, ~19 golems' worth. |
| 2 | **The Movement** | Great Cog @ 3 /min · Frame Section @ 3 /min | 8 min | **~72** | Two lines at once: the Mechanism chain must come up *while* the Casing chain keeps running. |
| 3 | **Aether Illumination** | Aether Conduit @ 3 /min · Lens @ 24 /min · Frame Section @ 2 /min | 8 min | **~82** | Three lines with a shared root. Lens @ 24/min needs 48 Slag against ~46 produced, **flipping the §5.3(c) decision** from voiding to over-smelting. |
| 4 | **The Chronometer** | Chronometer Core @ 2 /min · Great Cog @ 2 /min · Aether Conduit @ 1 /min | 10 min | **~96** | Peak. Everything simultaneously, including both 4-input Zeppelin recipes and their carrier chains. |

The player finishes Phase 5 at **~44 golems**. Every stage requires visible construction on arrival
(+19, +9, +10, +14).

### Why hand-gathering is arithmetically impossible

Raw cost per unit, fully back-propagated (Coke counted as Coal 1:1; Glass costed through Slag,
which is co-produced with the Iron Plate the recipe needs anyway). **Excludes steam upkeep**, which
is accounted separately below because it scales with golem count, not with output:

| Good | Scrap | Coal | Cu Ore | Zn Ore | Aether | **Total raw** |
|---|---|---|---|---|---|---|
| 1 Frame Section | 46 | 37 | 19 | 9 | 0 | **~111** |
| 1 Great Cog | 64 | 60 | 37 | 19 | 0 | **~180** |
| 1 Aether Conduit | 30 | 34 | 17 | 7 | 3 | **~91** |
| 1 Chronometer Core | 72 | 57 | 31 | 12 | 2 | **~174** |

Stage 4 goods = 2 Cores + 2 Cogs + 1 Conduit per minute ≈ **799 raw units/min**.
Steam upkeep at ~96 golems = 576 Coke/min, plus ~12 Coke/min for the Slag Heap ≈ **588 extra
Coal/min**. Total ≈ **1,390 raw units/min ≈ 23 per second.**

A player pressing Interact across eight scattered node sites sustains well under **0.5/s**. That is
a **~46× shortfall** — and it ignores that the Hand-Crank Bench runs Tier-1, 1-input recipes only,
at 25 % speed, and cannot touch any 2-, 3- or 4-input recipe at all. Stage 4 needs roughly:

- **~8 extractors** at 150/min each, across ~8 node sites (2-per-node cap)
- **~23 coking golems** (~794 Coke/min total demand at 35/min each) plus ~11 coal loaders —
  **the coal-and-coke line alone is ~41 % of the factory**
- **~12 smelters**, ~4 Brass/Wire, ~12 components, ~8 Tier-4/5 assemblers and carriers
- **~15 other logistics golems** (belt→buffer, Slag routing)
- **~12 Boilers** and the pipe network to reach all of it

**≈ 96 concurrently running golems.**

### Win state

Stage 4 completion chimes the tower and shows a "Clockwork Metropolis" end screen, then **leaves
the save running** — consistent with the implementation plan's "open-ended sandbox, no forced end
condition." The Clock Tower is a win, not a game-over.

---

## 8. Unlock and gating mechanism

**Use `AssemblyLineState`.** This is the exact follow-up M9 deferred, and the system already has
cost decay, claiming and drip-feed refill, all tested. Four changes:

1. **Gate the Card Vault.** Wire `GetClaimedCards("LocalPlayer")` into
   `WorkbenchController.ConfigureRoster` so the vault shows only claimed cards, refreshing live.
2. **Item-bundle claim costs.** Generalize `DraftableCardDefinition.baseCost` from an int of Scrap
   to a `List<(itemType, quantity)>`, with decay scaling the whole bundle. Tier-N cards cost
   Tier-(N−1) goods — the actual tech spend.
3. **Prerequisites.** Add `prerequisiteCards` and `prerequisiteItemProduced`. A card enters the
   refill queue only once both are satisfied: the Aether Containment card cannot appear until you
   have made a Lens.
4. **Unique cards leave the pool.** `RefillEmptySlots` currently re-enqueues everything forever.
   Add `isUnique`: recipe and chassis cards are removed on claim; generic Logic Cores and
   `Haul`/`Push` appendages keep cycling. Without this the tech tree never terminates.

Cost decay stays — it preserves the tabletop's "wait for it to get cheaper, or take it now" tension
and gives the player something to spend surplus on while a stage grinds.

### Two supporting gates, both promoting unused systems

- **Assembly Bays cap concurrent golems.** Introduced in **Phase 1**, starting at **10 slots**,
  above the natural Phase-2 count of ~8. Each upgrade is **+6 slots for 40 Scrap + 20 Iron Plate** —
  **Presser-tier goods only**, deliberately, so the cap can never gate on something the cap itself
  prevents you from making. This is the tabletop's Assembly Bay rule verbatim, and combined with
  steam upkeep it is what makes 5- and 6-slot chassis worth their cost: one Overclocker running a
  3-input recipe replaces a cluster and costs one bay slot and one golem's 6 Coke/min.
- **Patents become the scaling tool.** Scale `EngageGears` Focus cost with program length
  (`8 + 6 × appendageCount`); make committing an already-patented blueprint via
  `LoadBlueprintIntoDraft` a flat **10** Focus. By stage 4 the player needs **~23 identical coking
  golems** and ~12 identical smelters; patenting once and stamping is then obviously correct — the
  Factorio blueprint arc, built entirely from mechanics already in the repo.

### Legibility surfaces

Every one extends something that exists.

| Player question | Answered by |
|---|---|
| "What now?" | **Next Objective** line in the Management HUD: cheapest affordable unclaimed Assembly Line card, or the active tower stage's largest deficit. |
| "How long until the lights go out?" | **Boiler fuel gauge**: current Coke, burn rate, and a countdown (`240 Coke · 24/min · 10:00 left`), with an alert at 25 %. Phase 2's blackout must be a deadline the player watches approach, not an ambush — and with proportional burn the countdown responds live to every golem placed. |
| "Why can't I claim that?" | Assembly Line panel greys locked cards with `Requires: <prerequisite>`. |
| "Why is this golem stopped?" | Existing stall diagnostics, extended with: the specific short ingredient **and amount** for `Assemble`; `NoSteam` naming the nearest pipe gap; `OutputFull` naming the blocked item type. |
| "Why is my rate low?" | Existing per-buffer per-minute readouts, plus `Required / Delivered / Fresh / ×multiplier` during a tower stage — four columns because a player whose multiplier is capped by *fresh production* rather than delivery needs to see exactly that. |
| "Why can't I put this card on this golem?" | Workbench greys it: `Needs 5 slots (3 inputs) — this chassis has 4`. |
| "How much coke am I burning?" | HUD line: `Golems 96 · Powered 92 · Upkeep 576 Coke/min · Smelting 206 · Net −14`. **The most important new readout in the game** — it is how the player perceives that growth has a price. |
| "Where is my Slag going?" | HUD line: `Slag 96/min → Glass 32 · Voided 64 (16 Coke/min)`. |

---

## 9. Expected playthrough

At 10 ticks/sec, **1 minute = 600 ticks**. Golem counts include logistics and are ±25 % pending a
tuning pass.

### Phase 1 — The Cold Workshop (~13 min)

Spawn beside a scrap heap and a coal seam, with a Hand-Crank Bench and a Boiler holding **240 Coke**.
Interact harvests 1 unit per press; holding Interact at the bench runs any 1-input Tier-1 recipe at
25 % golem speed (Scrap → Iron Plate = 96 ticks, ~9.6 s each). The Presser costs 60 Scrap + 20 Iron
Plate + 10 Gear = **100 Scrap of gathering and ~7 minutes of cranking.** Around minute 4 the phase's
decision lands: a Scavenger automates gathering but burns 6 Coke/min off a gauge that is visibly
counting down. One Scavenger gives 40 minutes of runway; four gives 10. Ends: **3–4 golems**, one
Presser about to come online, the gauge low.

*Beat: the first Scavenger out-gathers you — but it still can't craft, and now the clock is running.*

### Phase 2 — The First Machine (~20 min)

The first Presser runs `Haul(Scrap,4) → Assemble(R2) → Push` and produces Iron Plate at ~19/min
against the crank bench's ~6. Relief. Immediately, two walls: the belt from the scrap heap **cannot
feed the Presser's source buffer** — a `Push` golem must sit between them — and the second Presser
two tiles further out **doesn't run**, because it isn't adjacent to steam. Steam Pipes at 1 Iron
Plate each. Then the gauge hits zero, and the player discovers the sting: **eight golems burn 48
Coke/min and one coker only makes 35**, so the coal line has to be two cokers deep before it breaks
even. Ends: **~8 golems**, Coke ~70/min gross, ~48 consumed.

*Wall: golems burn coke to exist, and a coker feeds fewer than six of them.*

### Phase 3 — Steam & Scale (~40 min)

Grind toward the Aether-Hauler (80 Iron Plate + 40 Gear + 30 Coke). Three lessons land, all now
sharp: every golem placed anywhere costs another 6 Coke/min and the gauge responds instantly, so the
coal-and-coke line grows to **~7 of 18 golems**; the local scrap heap caps at 2 extractors so the
third goes across the floor, needing a belt *and* a parallel pipe run; and the 25×25 floor fills, so
Floor Expansion gets bought. First bay upgrade around 10 golems. Ends: **~18 golems**, ~108 Coke/min
upkeep against ~175 produced.

*Decision: growth has a price, and the price is paid in the resource you are trying to grow.*

### Phase 4 — The Metal Lines (~55 min)

The Aether-Hauler opens all eight 2-input recipes at once — the biggest single unlock in the game.
Iron smelting doubles plate per scrap **and** starts producing Slag. Within minutes the Slag slot in
the smelter's output buffer fills, `Assemble` can't deposit, and the smelter stalls, taking gears,
casings and everything downstream with it. Slag must be routed every cycle: Slag Heap (1 Coke per 4)
or Glass line. Meanwhile Brass demands copper and zinc in one golem's input stock, so the first ring
— or the first **carrier** — gets built. Copper and zinc sites go up. Aether comes online. Ends:
**~32 golems**, ~192 Coke/min upkeep plus ~60 smelting.

*Wall: Slag. The first thing in the game whose surplus actively breaks you.*

### Phase 5 — The Great Works (~55 min)

The Overclocker opens Mechanisms, the Tier-5 goods and `Repeat` — and immediately poses its own
question, since `Repeat` and a third ingredient compete for the same fifth slot. Then the long haul
for the Zeppelin, roughly **12–18 minutes** of a mature factory's full output, during which the
player patents the Coke, Smelter and carrier programs and stamps out copies at a flat 10 Focus. The
first Zeppelin opens Regulators and Chronometer Cores, but the bigger change is the **Freight
Link**: distant copper and aether sites get local boilers and a mast, and two long belt corridors
plus their pipe runs get torn out and their tiles reclaimed. Ends: **~44 golems**.

*Decision: outposts, not corridors. The map's shape changes.*

### Phase 6 — The Clock Tower (~60–80 min)

Stage 1 demands 6 Frame Sections/min against a factory doing maybe 1.5 — the Casing line roughly
quadruples, the smelter bank doubles, and the coal line has to grow to carry ~19 new golems'
upkeep. It clears in 6 minutes of supply, realistically ~16 including build-out. Stage 2 forces
Mechanisms concurrently. Stage 3's Lens @ 24/min flips the Slag decision from voiding to
over-smelting. Stage 4 runs both Zeppelin recipes plus Cogs plus Conduits for 10 minutes at ~1,390
raw/min, ending at **~96 golems** and ~12 boilers. Players who over-build to 2× finish stages in
half the nominal time — the phase's real decision.

*Beat: a stage bar freezes because one zinc extractor lost steam when a pipe was paved over, and
the alerts strip tells you exactly which tile.*

### Cumulative shape

| Phase end | Golems | Coke upkeep | Smelting coke | Raw extraction | Coal line share | Cumulative |
|---|---|---|---|---|---|---|
| 1 | 4 | 24 /min (from stock) | — | ~40 /min | — | 0:13 |
| 2 | 8 | 48 /min | — | ~110 /min | ~38 % | 0:33 |
| 3 | 18 | 108 /min | — | ~230 /min | ~39 % | 1:13 |
| 4 | 32 | 192 /min | ~60 /min | ~500 /min | ~40 % | 2:08 |
| 5 | 44 | 264 /min | ~120 /min | ~700 /min | ~40 % | 3:03 |
| 6 | ~96 | ~576 /min | ~206 /min | **~1,390 /min** | ~41 % | 4:15 – 4:35 |

---

## 10. Soft-lock audit

| Risk | Status |
|---|---|
| **Chassis cost circularity** | **Clear.** Verified in §6: Scavenger/Presser cost hand-obtainable goods; Hauler costs only Presser output; Overclocker and Zeppelin cost only Hauler output. |
| **Bay cap unreachable** (rev. 1 soft-lock (a)) | **Fixed.** Cap 10 from **Phase 1**, above the natural Phase-2 count of ~8; upgrades cost **Scrap + Iron Plate only**, both hand-obtainable. |
| **Buffer capacity deadlock** (rev. 1 soft-lock (b)) | **Fixed via three coupled changes.** Capacity is **per item type**, so a full Slag slot never blocks Iron Plate. Typed `Haul` is **mandatory**, not recommended — §2 makes it structural. And Slag has an explicit sink, the **Slag Heap**. Without all three the rev.-1 scenario recurs. |
| **Logistics golems stall forever** (rev. 2 defect) | **Fixed** by the pure-logistics rule in §2: a program with no `Assemble` treats input stock as output stock. |
| **Node depletion** | **Clear.** All nodes infinite. Scarcity is the 2-extractor cap — a throughput ceiling, not exhaustion. |
| **Coke death spiral under proportional burn** | **Clear.** A coal cluster of 1 extractor + 4 cokers + 2 loaders = 7 golems produces 140 Coke/min and consumes 42 — **3.3 : 1**. Convergent at every scale. A player who over-builds into a deficit can delete golems (freeing both bay slots and upkeep instantly), or hand-crank coke. |
| **All Scrap spent on belts/floor** | **Clear.** Scrap is always hand-harvestable and the crank bench always works. |
| **Total blackout with no golems to recover** | **Clear.** The Hand-Crank Bench runs R1 without steam, so the player can always hand-crank coke to restart. **The bench must be explicitly unpowered.** |
| **Focus exhaustion** | **Clear.** Regenerates at 5/s. |
| **Clock Tower failure** | **Clear.** Progress freezes at 0, never negative. |
| **Wasted Assembly Line claims** | **Low.** Prerequisites prevent claiming far-future cards; claim costs are small relative to a phase's output. |

---

## 11. What this needs that doesn't exist

Ordered by blocking-ness.

1. **The machine model (§2)** — golem internal input/output stocks; `Haul`/`ExtractFromNode`
   depositing into input stock; typed, quantified, **player-set-quantity** `Haul(itemType, qty)`; a
   new `Push` verb; `IItemEndpoint.TryTake(itemType, qty, out taken)`; **the pure-logistics rule**.
   This changes what a golem *is*, but it is strictly smaller than making `BeginRefine` spatial, and
   it is the reason that exemption can stay.
2. **Multi-input assembly** — `AppendageActionType.Assemble` + a `RecipeDefinition` SO: 1–4
   `(itemType, quantity)` inputs, `outputItemType`/`outputQuantity`, one optional byproduct pair,
   `durationTicks`. Atomic all-or-nothing check against **input stock**, stall naming the specific
   shortfall. Reads and writes only the golem's own stocks — never a tile.
3. **Per-item-type buffer capacity** with real backpressure. `StorageBuffer` currently has none
   (`Deposit` always succeeds, `CanGive()` is always `true`), so no "destination full" stall in this
   design can currently occur — and §5.3(c), the entire Slag economy, depends on it.
4. **Steam power (§3.1)** — Boiler with **per-powered-golem** Coke burn and a fuel gauge; Steam Pipe
   placeable; adjacency check in `GolemEntity.Tick`; `StallReason.NoSteam`.
   > **Implementation note:** §3.1 of revision 2 claimed wholesale reuse of
   > `BeltNetwork`/`BeltPlacementRules`. That is wrong and the correction makes this *easier*:
   > `BeltPlacementRules.ShouldLink` is **directional** (it requires `TargetCell(from, facing) == to`
   > and rejects head-on pairs, to avoid two-cycles). Steam pipes are **undirected** and have no
   > flow, so they need a simple undirected flood fill from each Boiler over orthogonally adjacent
   > pipe cells — a much smaller component than `BeltNetwork`, not a reuse of it.
5. **Slot-count gate** — enforce `program.appendageCount <= chassis.maxAppendageSlots` (already
   there) and expose a reason string. Change roster slots to 2/3/4/5/6. **Add nothing** —
   `ChassisDefinition.tier` stays unused; this design deliberately does not add a stat check.
6. **New content** — 21 new `ItemType` constants; `Coal`/`CopperOre`/`ZincOre` node types with
   markers and sprites; delete `BrassNode` from `SandboxBootstrap`; make `AetherNode` infinite;
   2-extractor-per-node cap; repurpose `RefineBrass.asset` to Scrap → Iron Plate.
7. **Hand-Crank Bench** — placed building; held-Interact runs any 1-input Tier-1 recipe at 25 %
   speed into the player's inventory. **Unpowered by design** (see §10).
8. **Chassis cost as item bundle** — `scrapCost`/`brassCost` → `List<(itemType, quantity)>`;
   `TryWithdrawScrapAndBrass` → `TryWithdrawBundle`.
9. **Clock Tower** — placeable structure + `ITickable` implementing the rate-scaled pressure meter
   with **both** 60 s rolling windows (delivery and fresh production), four stage definitions, a
   panel.
10. **Slag Heap** — placeable sink; consumes Slag at 1 Coke per 4.
11. **`Repeat(n)` appendage** — Overclocker-only; re-runs the preceding `Assemble`.
12. **Assembly Line as tech tree** — the four changes in §8.
13. **Freight Mast + `FreightLaunch`** — placeable mast, Zeppelin-only appendage, bound pair.
14. **Assembly Bay in the loop** — cap active golems; `TryUpgrade` costs Scrap + Iron Plate.
15. **Floor Expansion** — purchasable `FloorLayout` growth.
    > **Implementation note:** `HalfExtent` is a `const` read by the **Editor-only**
    > `SandboxFloorGenerator`, so runtime expansion needs runtime tilemap repainting and wall
    > re-placement. This is more work than "purchasable growth" suggests. Acceptable fallback: a
    > larger fixed floor whose outer region is gated by a one-time unlock. The design needs land to
    > be finite and expensive, not continuously paveable.
16. **Focus cost scaling** on `EngageGears`; flat cost for patented commits.
17. **Legibility surfaces** — the eight rows in §8. Not polish; the fuel gauge and the coke/slag
    lines are how the player perceives the entire scarcity system.

Items 1–4 are the real work. The rest is authoring or small additive changes.

---

## 12. Self-assessment against the rubric

### 1. No dead-end resources — **PASS**
Audited item-by-item in §5.1; an independent review spot-checked all 24 and agreed. Brass has seven
consumers, Coke five (three recipes, every powered golem, the Slag Heap). `Aether` — the live
build's actual dead end — now runs to the win condition. `Slag` has both a productive consumer and
a costed sink. The four Tier-5 goods terminate in the Clock Tower, the intended sink.

### 2. Each tier gates the next — **PASS**
Three stacked gates. **Slot count** is physical, not a stat check: a 4-slot chassis cannot hold a
3-input program, full stop. **Chassis costs** are paid in manufactured goods (verified acyclic).
**Assembly Line claims** with prerequisites order the recipes. And the player cannot execute any
2-, 3- or 4-input recipe by hand — the crank bench is Tier-1, 1-input only.

### 3. The manual→automated arc is real — **PASS**
The first Brass Presser costs 60 Scrap + 20 Iron Plate + 10 Gear, all **hand-cranked at 25 %
speed** — roughly **10–15 minutes of genuine hand labour** with a live decision throughout (each
Scavenger trades fuel runway for hands). At the far end, stage 4 needs ~23 raw/s against a hand
ceiling under 0.5/s — a **~46× shortfall** — plus 576 Coke/min of upkeep that no amount of cranking
touches.

### 4. Each tier introduces a new decision — **PASS**
P1 invest-or-grind against a visible fuel budget. P2 layout under two simultaneous constraints. P3
growth priced per golem, with nodes capped so scale means distance — and this is the phase revision
2 was weakest on, which is exactly what the upkeep retune fixes: at 6 Coke/min against a coker's 35,
every four golems needs another coker, so the phase's stated content is now its actual content. P4
convergence through four faces via carriers, plus Slag as an active disposal obligation. P5 remote
outposts, plus `Repeat`-versus-third-ingredient on the same slot. P6 whether to over-build. **Six
distinct decisions.**

### 5. No stall-out gaps — **PASS**
Full audit in §10, including both rev.-1 soft-locks and the rev.-2 logistics no-op. Convergence
under the new upkeep is re-derived: a 7-golem coal cluster nets +98 Coke/min at 3.3 : 1. Recovery
from any over-built stall is explicit — delete golems (upkeep drops instantly, which is only true
because burn is proportional) or hand-crank coke at the unpowered bench.

### 6. The five chassis sequence meaningfully — **PASS**
Each unlocks a strictly wider recipe class by slot count, and **each of the top two now has a
verb**: Scavenger 0 inputs (pure logistics, and the only thing that can staff a node while you
crank), Presser 1, Hauler 2 (including R12, the only recipe touching raw Aether — its roster role
made literal — and the first viable carrier), Overclocker 3 **plus `Repeat`**, Zeppelin 4 **plus the
Freight Link**. Revision 2's "one chassis gets a spatial verb and the other gets a number" is
resolved. No stat checks, no multipliers on other golems.

### 7. The Clock Tower demands sustained throughput — **PASS**
Stage 1 alone needs ~63 golems against a Phase-5 exit of ~44, and every stage requires +9 to +19
more. Progress **scales with rate** to 3×, so over-building is rewarded. The hoard-blitz hole is
closed by taking `min(delivery, fresh production)` over 60 s windows, so a warehouse can smooth a
dip but never raise the multiplier.

### 8. Legibility — **PASS**, conditional on §8's surfaces landing
Existing infrastructure does most of it. The additions are eight readouts, of which the **fuel
gauge**, the coke-upkeep line and the slag-routing line are load-bearing: they are how the player
perceives the scarcity system at all. If they are cut the economy becomes invisible and this point
fails.

### 9. Breadth — **PASS**
Six phases, 24 items, 25 recipes, five chassis, four rate-scaled stages, ~4.5 hours, 4 → ~96 golems.
The phases are distinct in *kind*: P1 manual labour, P2 dual-constraint layout, P3 running-cost
economics, P4 convergence and waste, P5 remote logistics, P6 rate balancing. The rev.-1 criticism
("the topology is inert; nothing is contended") is addressed at the root by §3 rather than by adding
items — the same 24-item tree is deep because coke, node access and floor space are finite.

### Where I still think this is weak

1. **~96 golems at stage 4 against the plan's stated "100 golems / 500 belt items" perf budget.**
   The tightest constraint in the document, and the upkeep retune pushed it *up* (from ~88) because
   the coal line grew. The lever is stated: lower stage-4 rates or lengthen deep-recipe durations,
   both trading golem count for wall-clock time. **This needs a real profile before assets are
   authored**, and if 96 doesn't hold, stage 4's Conduit demand is the cheapest thing to cut.
2. **Golem-count and throughput figures are ±25 %.** Derived from the §2 cycle-time formula and
   internally consistent — an independent re-derivation of stage 1 got ~51 against my ~50 at the
   old upkeep — but ratios are not solved to clean integers and perfect uptime is assumed. Treat
   every number in §7 and §9 as a target, not a fact.
3. **I declined to change Lens to 3 Glass**, which was suggested to make the Glass-versus-void
   decision live across more of the game. The argument is good, but the raw-cost table in §7 has now
   been independently verified twice at 2 Glass/Lens, and the stage-3 flip (48 Slag needed against
   ~46 produced) is already well tuned at the current ratio. Changing it re-opens arithmetic two
   parties have checked for a marginal gain. **Flagging it as the first thing to try in the tuning
   pass**, where the spreadsheet can absorb the churn safely.
4. **My revision-2 justification for the Slag design was wrong**, even though the design was right.
   I claimed raising Glass-per-Lens would force smelting past what Iron Plate demand can absorb; it
   would not — at stage 4 the factory makes 96 Slag and Lens consumes 48, so doubling Lens would
   consume existing byproduct with no extra smelting. The disposal-obligation design stands on its
   own merits (a priced choice at every scale, and a genuine sign flip between stages 3 and 4); the
   bad reasoning is retracted.
5. **Steam power is invented** and is the largest addition in this document. It is not in either
   design doc. I judged it necessary because "nothing is contended" has no fix that doesn't add a
   running cost, and fuel is the only such cost that is thematically exact, local, rigid, and
   deterministic. It has now been reviewed as justified, but it is still the thing most likely to be
   wrong, and the proportional-burn retune makes it *much* more load-bearing than before — the coal
   line is now ~41 % of the factory, so if the ratio is mis-tuned the game becomes a coal simulator.
   **This is the number to playtest first.**
6. **`Push` emptying the entire output stock is a deliberate convenience** so byproducts don't cost
   a slot. A golem can never selectively withhold an output, which removes a possible decision. The
   right trade — selective output needs a second card and makes every smelter a 5-slot job — but a
   simplification, not a neutral choice.
7. **Whether this is genuinely Factorio-scale.** The *tree* is: 24 items, depth 7, two hub nodes, a
   byproduct obligation, three contended intermediates, and with §3 the topology is no longer inert.
   The *mechanic variety* is not — no fluids, no circuit network, no trains, one power type. Depth
   comes from three sources: tree topology, the tile/facing/rigidity puzzle, and coke scarcity. That
   is narrower than Factorio, and it should be, because rigid local determinism is this game's
   identity rather than a limitation. It is Factorio-*shaped* at roughly a fifth of the mechanical
   surface area, sized for a 4–5 hour prototype campaign rather than a 40-hour game.
