# Golem Factory — Open Items

Consolidated backlog as of the progression-design pass, on branch
`polish/production-quality-pass` (17 commits, **not merged to `main`**).

Everything here is known and deliberate — none of it is a surprise waiting to be discovered.

> **Where the line is.** Everything through §1.4 (steam power) is **built, tested and
> reviewed**. Everything from §1.5 on is **spec only**. The next pass starts at §1.5, which is
> also what §1.4's Sandbox switch is waiting on.

Tests stand at **934/934** (824 EditMode + 110 PlayMode), up from 590 before the progression pass
began. Console clean.

> **The projection is top-down now**, not isometric. The switch is done, verified and recorded as
> its own milestone in `docs/unity-implementation-plan.md` — read that before touching the floor,
> the walls, or anything that turns a cell into a world position. Anything in *this* file written
> before it that says "isometric" is describing history.

> **Unity batch mode does run the tests, contrary to what the implementation plan says.** It is
> recorded there as a "nice-to-have, not implemented"; it works, and it is by far the cheapest way
> to verify a pass. The exact invocation is in `CLAUDE.md`. It needs the Editor **closed** — it
> fails on the project lock otherwise. `dotnet build GolemFactory.<Assembly>.csproj` remains useful
> as a fast compile-only gate while the Editor is open, but it type-checks and runs nothing.

---

## 1. The progression design, and how far it has been implemented

`docs/progression-design.md` passed a three-round review against a 9-point rubric, with an
independent critic between rounds, before any code was written against it. That was deliberate:
round 1 failed on five structural counts, all of which would otherwise have surfaced only after
~25 recipes had been authored and wired.

**§1.1–§1.4 are now built and green. §1.5–§1.6 are still spec.** Implementation order matters,
because each item depends on the one above it — and §1.2 in particular must never ship before
§1.1, for the reason given under it.

### 1.1 Golem internal typed stock + typed/quantified `Haul`/`Push` — **DONE**

`GolemInventory` gives each golem an input and an output `Stock`, capped at 12 **per item type**.
`Haul(itemType, qty)` and `ExtractFromNode(qty)` fill input from the tile behind; the new `Push`
verb empties stock onto the tile in front, mixed types and all; the new `Assemble` verb consumes
from input stock and deposits to output stock **without ever touching a tile**, which is what lets
`BeginRefine`'s spatial-routing exemption stay exactly as written. Durations are now derived
(`max(2,qty)` / `6+qty` / `2+unitCount`) instead of authored. `IItemEndpoint` grew
`PeekAvailableType()` and `TryTake(itemType, qty, out taken)`. Chassis slots are now **2/3/4/5/6**.

The pure-logistics rule is implemented as an explicit named special case
(`GolemProgram.HasAssembleStep` → `GolemEntity.PushStock`), **not** by merging the two stocks —
keeping them separate is what lets one `Push` empty everything at once and makes byproducts free.

**How it coexists with what was already there.** The model rides the *existing* `IsSpatiallyPlaced`
fork rather than introducing a second axis. A spatially placed golem gets the machine model; an
id-routed one (`Main.unity`'s seven hand-wired demos, and every pre-existing test) keeps the old
semantics byte for byte, **including `step.durationTicks` as its duration**. `Sandbox.unity`'s
golems are spatially placed via `GolemConstructionStation.ConfigureSpatial`, so the new model is
live in the playable scene. `LoadIntoBuffer` on a spatially placed golem now *means* `Push` — the
enum member was kept rather than renumbered, because its index is serialized into
`LoadIntoScrapBuffer.asset`. `Refine` is unchanged on both paths and is superseded by `Assemble`
in §1.3; nothing new should be built on it.

Per-slot `Haul` batch size lives on `GolemProgram.appendageQuantities`, **not** on
`AppendageActionDefinition` — that is a shared asset, so a player's batch size written there would
retune every golem holding the card at once. Saved, along with each golem's held stock.

Three new stall reasons, all appended (the enum is serialized by index): `InputFull`, `OutputFull`
and `MissingItem`. `MissingItem` exists because a typed `Haul` against a buffer *full of the wrong
good* previously reported "no input in ScrapBuffer", pointing the player at a buffer they can see
is full; it splits on `PeekAvailableType()` so genuine emptiness still names the endpoint.

Follow-ups this opened, in dependency order — see §3:

- The Workbench has 5 appendage sockets against the Zeppelin's new 6.
- There is no UI for setting `Haul` quantity, so the decision §2 hands the player is data-only.
- There are no `Push`/`Assemble` roster cards authored yet (deliberate — §1.3 replaces `Assemble`'s
  data model with a `RecipeDefinition`, so authoring one now would be throwaway work).

### 1.2 Per-*item-type* buffer capacity — **DONE**

`StorageBuffer` now carries a per-item-type capacity with a `Unlimited = -1` sentinel following
`ResourceNode.Infinite`'s idiom, and `Deposit` returns the units actually accepted instead of
always succeeding. Capacity is **opt-in**: a buffer built without one is `Unlimited` and behaves
byte for byte as before, so `Main.unity`'s demo economy, the player's stockpile and every
pre-existing test are untouched — the same fork idiom spatial routing uses.

`IItemEndpoint` grew a **typed** `CanGive(itemType)` alongside the untyped one. Both stay: the
untyped one means "could you accept *anything at all*", which is still right for the id-routed
path and for `Push`'s early-out, and only a per-type-capped buffer makes the two disagree.

**The load-bearing change is in `GolemEntity.BeginPush`**: a type the destination refuses is now
**skipped**, and the remaining types still get pushed. It previously abandoned the whole push at
the first refusal, which under per-type capacity is exactly the §10 deadlock — a full Slag slot
would have blocked Iron Plate, and no rigid golem could ever clear it. The belt case (capacity not
per type) early-outs on the untyped `CanGive()` instead, so it is not walked pointlessly.

`SandboxBootstrap` sets `DefaultCapacityPerType = 100` for ordinary production buffers and
explicitly leaves `FactoryStockpile` `Unlimited`. **Both numbers are tuning, not derived** — the
design gives neither. `DefaultCapacityPerType` is scene *policy*, not a factory default: setting it
re-caps buffers that already exist and have no explicit override, so capacity can't depend on
whether a buffer happened to be auto-created before or after bootstrap ran. Per-buffer overrides
survive `StorageBufferRegistry.Clear()`, because that is the save/load path and a load would
otherwise re-cap the stockpile and clamp away its contents.

**A silent item-loss bug was fixed on the way through.** `StorageBufferRegistry.Clear()` used to
remove the buffer *objects*, but `PlaceableDepot` publishes a `StorageBufferEndpoint` holding a
direct reference to the instance. So after a load, every depot's endpoint wrote into a detached
buffer while the registry and the HUD read a freshly created one — goods pushed into a depot
vanished, with no stall and no error. `Clear()` now empties the buffers in place and keeps the
instances, which makes endpoint and registry the same object by construction rather than by
remembering to re-publish endpoints after every load. The bug predates this item; it is fixed here
because §1.2 reworked exactly that code, and because per-type capacity would have made it worse.

**Caveat:** in `Sandbox.unity` as it stands, `FactoryStockpile` is the *only* buffer (every
`PlaceableDepot` points at it), so the finite default is wired but has nothing to bite on yet. It
starts biting the moment §1.5 authors per-line buffers.

### 1.3 Multi-input `Assemble` — **DONE**

`RecipeDefinition` is a new ScriptableObject: 1–4 distinct typed inputs with quantities, an output
type and quantity (which may exceed 1 — R4 makes 2 Iron Plate, R7 makes 3 Brass), exactly one
optional byproduct with its own quantity, and `durationTicks`. `AppendageActionDefinition` gained a
`recipe` reference, and **`Assemble` now reads only that** — the single `inputItemType`/
`outputItemType` pair it borrowed in §1.1 was an explicit placeholder. Those fields stay on the
card because `Refine` and `Haul` still use them.

**Atomicity is the load-bearing property.** Every input is checked against input stock before any
input is withdrawn. A partial withdrawal on a recipe that then stalls would strand goods inside the
golem forever: a rigid program has no step that could put them back, and nothing outside the golem
can reach its input stock. Room for the output **and** the byproduct is confirmed before anything is
consumed — a recipe that could deposit its Iron Plate but not its Slag stalls with its inputs
intact, which is the mechanism behind §5.3(c)'s entire Slag economy rather than an edge case.

Validation lives on `RecipeDefinition.IsWellFormed()`, at the authoring edge, never as a throw
inside `Tick`. A malformed or missing recipe stalls `Unconfigured`. The 1–10 authored input range's
*upper* bound is deliberately not enforced: an over-large quantity is unsatisfiable rather than
structurally impossible, and the honest shortfall is exactly what §6 relies on to make `Repeat` on
R15 impossible later.

§8's "the specific short ingredient **and amount**" is now met: `StallResourceId` still carries the
bare item type (matching `InputFull`/`OutputFull`), and the amount rides alongside as
`GolemEntity.StallShortfall` → `GolemStalledEvent.Shortfall` → `StallSnapshot`, rendered as "needs
9 more Casing". When several inputs are short, the **first in the recipe's authored order** is
named, so two identically-programmed golems always give the player the same diagnosis.

### 1.4 Steam power — **DONE**

A Boiler burns **1 Coke per powered golem per 100 ticks** (`TicksPerSecond = 10`, so per 10 s),
powers **at most 8** golems, and reaches them by orthogonal adjacency to itself or to a **Steam
Pipe** chaining back to it. An unpowered golem stalls `NoSteam`, naming its own tile.

`Steam/SteamNetwork` is the plain-C# manager (Holder pattern, `SteamNetworkHolder : ITickable`);
`Steam/SteamPipeRules` is the engine-free flood fill; `Steam/SteamGaugeUtility` is the pure §8
gauge arithmetic; `Buildings/PlaceableBoiler` and `PlaceableSteamPipe` are the placeables, both
published/withdrawn through `BuildModeController` exactly as belts and depots are.

**Consumption is proportional, never flat.** A flat per-boiler burn was rejected in review: it
makes 7 of every 8 golems free, so the marginal cost a player actually optimises against is zero.
It also means **an idle boiler burns nothing** — the starting Coke is a budget spent by building,
not a hidden timer.

**Steam pipes are undirected.** `BeltPlacementRules.ShouldLink` is directional twice over (it
requires `TargetCell(from, facing) == to` and rejects head-on pairs to avoid two-cycles), so it
could not be reused: a pipe has no facing and cycles in a steam graph are meaningless rather than
harmful. `SteamPipeRules` is a plain undirected BFS instead — a much smaller component than
`BeltNetwork`, as §11 item 4 predicted.

**The three determinism decisions §3.1 leaves open, each pinned by a test:**

- **Which 8**, when more than 8 golems reach one boiler: golems in a **total order by cell**
  (`x`, then `y`, tie-broken by golem id). Cell order is factory layout, so it is identical
  between two identically-built factories and does not move unless the player moves something.
  Rejected: `Dictionary`/`HashSet` iteration order (not contractual, and rehashing reshuffles it,
  so adding a ninth golem could flip which of the first eight are powered), registration/scene
  order (identical factories built in a different order would diverge, and a load would repower a
  different eight), and nearest-first (ties are the common case on a grid and need this tiebreak
  underneath anyway). An unstable choice here flickers golems between powered and `NoSteam` every
  tick — unplayable, and untestable.
- **Two boilers on one pipe network**: boilers walked in the same cell order, each claiming up to
  8 **not-yet-claimed** golems from its reachable set. So a second boiler picks up the overflow
  and never re-shuffles what the first was already powering. Rejected: load balancing (moves
  golems between boilers whenever anything anywhere is built, so every boiler's countdown jumps)
  and nearest-boiler (needs a tiebreak anyway and can idle one boiler beside an oversubscribed one).
- **Fractional burn**: **integer accumulator, no floats.** Each tick a boiler adds its powered
  count to an accumulator and burns one Coke per crossing of 100, carrying the remainder. Exactly
  proportional, exact in integers, reproducible. 5 golems is 5 Coke in 100 ticks and 50 in 1000;
  3 golems (which divides into nothing evenly) is 90 Coke in 3000 ticks with no drift.

**§10's convergence check is asserted, not assumed.** A test pins that 7 powered golems consume
exactly **42 Coke/min**, so a future retune that breaks the design's 3.3 : 1 no-runaway claim
fails loudly instead of quietly.

Fuel exhaustion is real: a boiler at zero Coke powers nothing, burning can never take the stock
below zero, and the golems it was powering stall `NoSteam` rather than running on credit.

**Costs are recorded, not charged.** Boiler 30 Scrap + 10 Iron Plate, Steam Pipe 1 Iron Plate, as
constants on `SteamNetwork`/the two placeables. Iron Plate does not exist until §1.5, and §11
item 8 replaces the whole `scrapCost`/`brassCost` pair with an item bundle in the same pass, so
wiring a runtime cost now would mean inventing an item and then re-expressing the cost twice.

**Only one new `ItemType` was added: `Coke`.** Steam is meaningless without the good it burns and
every burn test names it. The other twenty land with §1.5.

#### The Sandbox switch, and what it waits on

`SandboxBootstrap.requireSteamPower` is **off**, and the reason is a hard dependency rather than
caution: **Coke has no source until §1.5** authors the coal node and the coking recipe. Turned on
today the starting boiler would burn down, every golem in the scene would stall `NoSteam`, and
there would be no way to make more — a soft-lock in the one playable scene, and exactly the
"total blackout with no golems to recover" row §10 clears only via the Hand-Crank Bench (§11 item
7, also unbuilt). This is the same "built, wired, tested and not yet biting" state §1.2's buffer
capacity is in.

The switch gates **one line**: whether `SandboxBootstrap` hands each `GolemConstructionStation` the
steam network. Everything else is live either way — placing a Boiler or Pipe registers it, the
burn ticks, and the fuel gauge reads honestly — because a boiler with nothing drawing on it burns
nothing anyway. Flipping it needs §1.5's coal node + coking recipe, and ideally §11 item 7's
Hand-Crank Bench as the blackout backstop.

#### One judgement call the design does not cover

§8 asks for "an alert at 25 %" but §3.1 gives a Boiler **no capacity**, so there is no authored
100 % to take a quarter of. The alert measures against `SteamBoiler.PeakCokeStock`, the most Coke
that boiler has ever held — derivable from its own history rather than invented, and against §9's
240-Coke Phase-1 boiler it puts the alert at 60, which is what the design's worked example
implies. A `CokeCapacity` field was rejected because it would also imply "this boiler is full,
your Coke has nowhere to go", a refusal rule §3.1 never sanctions and §10's audit has never been
run against. Worth revisiting if §1.5 gives boilers a real fill limit.

~~**Still needs an Editor pass**~~ — **done**, see §3. Both prefabs exist and are in the build
menu, `SteamNetworkHolder` is on `ManagerHolders.prefab` and registered with the clock, and
`SteamFuelGaugeView` has a HUD slot. §3 also records the refuelling gap found on the way through:
nothing had ever called `SteamBoiler.AddCoke`, so a boiler was a sealed tank.

### 1.5 Asset authoring — **DONE**

`Economy/ItemType.cs` now carries all **24** item ids from §5.1. All **19** crafting recipes from
§5.2 are authored as `RecipeDefinition` assets under `ScriptableObjects/Recipes/`, with matching
`Assemble` cards, plus the `Push` card §1.1 left unauthored. `RefineBrass.asset` is retired in favour
of R2 Scrap Reclamation as a proper `Assemble`.

> **A design-doc discrepancy, found and left alone.** §5.2's prose says "20 crafting + 5 extraction
> = 25 recipes" and §12 repeats it, but the table itself lists R1–R19 with no gaps, and the doc's
> own ladder tally — Presser 5 + Hauler 8 + Overclocker 4 + Zeppelin 2 — sums to **19**. Nineteen is
> what is authored. The prose is off by one; the table and the ladder agree with each other and with
> the assets. `RecipeCatalogTests` pins 19 against a hand-transcribed copy of §5.2 (deliberately
> *not* against the authoring script's own constant, which would be a tautology), so a future pass
> that "fixes" the count by inventing a twentieth recipe has to come and argue with a test.

Chassis and building costs are **item bundles** (§11 item 8) instead of the `scrapCost`/`brassCost`
int pair, which could not express a single one of §6's costs from the Presser on — the Overclocker
costs 2 Mainspring + 20 Brass + 24 Casing + 12 Gear. `StorageBufferRegistry.TryWithdrawBundle` is
atomic with a full refund on shortfall, like the `TryWithdrawScrapAndBrass` it sits beside (which
stays, because §8 keeps bay upgrades priced in Scrap + Iron Plate).

Two of the design's own rubric claims are now **tests** rather than prose: chassis-cost acyclicity
(§10's first soft-lock row — no chassis may require a good only it can produce) and the no-dead-end
audit (§12 point 1 — every Tier 0–4 item has a consumer). Both run against the assets on disk, so a
tuning pass that breaks either fails the suite instead of the player.

Nodes follow §5.1: `BrassNode` is **deleted** (Brass is manufactured from Phase 4 on, never dug up —
it was the one un-earned Tier-2 good, and leaving it in would have made the whole copper/zinc line
optional next to a hole in the ground); `AetherNode` is now **infinite** like every other node, so
scarcity is access rather than depletion and cannot soft-lock; and `CoalNode`, `CopperOreNode` and
`ZincOreNode` are added. The **2-extractor-per-node cap** (§3.2) is real, in `NodeExtractorRegistry`,
using the same stable-total-order determinism §1.4 established.

**Missing art.** The five node markers now each have their own icon (`item_coal`,
`item_copper_ore`, `item_zinc_ore` were added to `Tools/Art/generate_placeholder_art.py`, and
Aether was repointed at the `item_aether` that already existed), drawn silhouette-first because
colour is not an available channel there.

**Three of the six `building_block` users now have real art.** The Clock Tower, the Boiler and
the Hand-Crank Bench are `clock_tower.png` / `steam_boiler.png` / `hand_crank_bench.png`
(generated, then trimmed to their alpha bounds and imported at PPU 64, point filter,
BottomCenter pivot). Their tints are now `Color.white`: a tint exists to tell identical boxes
apart, so once a building has its own sprite the tint is the thing that would wreck it. The
"win condition rendered as a crate" problem is therefore **fixed**, not outstanding.

**Three remain on the box, and they are two different jobs, not one.** `SteamPipePrefab` still
comes through `BuildPlaceable`, so it needs only a sprite argument and its tint retired — the
same one-line change the other three got. But **`DepotPrefab` and `GolemConstructionStationPrefab`
are authored by `ApplyCost`, which restores a cost and never touches the `SpriteRenderer` at
all.** Their sprite is whatever is already serialized on the prefab. Giving those two real art
means routing them through `BuildPlaceable` (or setting the sprite where they are actually
built) — reading only the tint table will not lead you to them, because they were never in it.
Tints still live in one table at the top of `Scripts/Editor/ProgressionSceneAuthoring.cs`, but
that table is now a partial index of the problem rather than the whole of it.

**`requireSteamPower` is still off.** Authoring `CoalNode` and R1 removed one of its two blockers —
Coke now has a source. The remaining one is the **Hand-Crank Bench** (§11 item 7), which §10 requires
as the total-blackout backstop: "the bench must be explicitly unpowered", so the player can always
hand-crank coke to restart from a dead factory. Until it exists, a live steam requirement has no
recovery path.

### 1.6 Clock Tower — **DONE**

Four rate-scaled stages, authored as assets from §7's table. The site is an `IItemEndpoint` on a
cell, so golems `Push` into it exactly like any other destination rather than through a special case.
Per demanded item there is a supply-pressure meter (`+1`/unit, `−demandRate/60`/s, clamped `[0, 60]`)
and two 60-second rolling windows; stage progress per second is
`min` over demanded items of `clamp(effectiveRate / demandRate, 0, 3)`, so **surplus is rewarded up
to 3× and the weakest line gates everything**. Progress freezes at zero and never goes negative:
stages cannot fail, only take longer, which is §12's rubric-5 guarantee.

**The hoard-blitz hole is closed, and it needed a new signal.** The 60-unit meter clamp caps *stock*
credit, but delivering from a warehouse is still a delivery *rate* — so a player could have
over-produced stage-4 goods during stages 1–3 and unloaded at 3× to finish the climax in three
minutes. `effectiveRate = min(deliveryRate, freshProductionRate)`, where fresh production is
`Assemble` output over the last 60 s, published as a new `ItemAssembledEvent` **at recipe
completion** (crediting at ingredient withdrawal would let a stalled 120-tick Chronometer Core count
as production it never finished). A byproduct counts as fresh production of its own type. A
stockpile can now smooth a dip but can never raise the multiplier.

**The arithmetic is integer end to end**, following §1.4's discipline. Progress accrues in millionths
of a nominal tick; the per-tick increment is exact at 1× and at the 3× cap, so nominal durations land
on the tick, and the truncation elsewhere is always a *loss*, never a gain — a stage can run a
hundredth of a tick long over ten minutes, but can never finish early. The `min` across demanded
items walks the stage's authored list by index, so two identical factories always name the same
starved line.

§8's four columns (`Required / Delivered / Fresh / ×multiplier`) and the starved-item alert with its
items/min deficit are implemented as a pure formatter plus a thin view.

### Perf is not a blocker
Measured on the real stack, in Play mode:

| Configuration | Frame time |
|---|---|
| Base Sandbox scene | 3.69 ms |
| + 96 continuously-working golems | 5.71 ms (**+2.02 ms**, ~0.021 ms each) |
| + 400 rendered Y-sorted item sprites | +0.53 ms |
| **Projected combined** | **~6.2 ms** |

Against a 16.67 ms / 60 fps budget that is ~37% used, roughly **2.7× headroom**. Editor
measurement, so a standalone build errs faster. Even tripling per-golem cost for internal stock and
steam adjacency still fits.

---

## 2. Decisions

### Decided

- **Steam power is in, and is now built** (§1.4 above). Confirmed by the project owner. It appears in neither `game-design.md` nor
  `digital-design.md` — it was invented during the progression design because "nothing in the
  economy is contended" has no fix that doesn't add a running cost. The critic ruled it justified
  and correctly shaped: local (orthogonal pipe adjacency), rigid (no falloff, no pathfinding, no
  adaptation), deterministic, and `NoSteam` is the existing stall rule with a new precondition
  rather than a departure from it. It ends up ~41% of the factory, so **the boiler fuel ratio is the
  single most important number to playtest first** — mis-tuned, this becomes a coal simulator.
  Note the design rejected a flat per-boiler burn: it makes 7 of every 8 golems free, so the
  marginal cost a player actually optimises against is zero. Consumption must scale per powered
  golem.

- **The Sandbox map layout is "Quadrants"** — one node per corner, Aether alone due north.
  Confirmed by the project owner, chosen from three proposals. It lives in the `StartingLayout`
  table at the top of `Scripts/Editor/ProgressionSceneAuthoring.cs`, **in cells**:

  | Scrap | Coal | Aether | Copper | Zinc | Bench | Station |
  |---|---|---|---|---|---|---|
  | `(-7, -7)` | `(7, -7)` | `(0, 9)` | `(-7, 7)` | `(7, 7)` | `(0, 3)` | `(3, -1)` |

  **Why cells and not world coordinates.** These seven were isometric-space world literals —
  `(2.5, -1.25)`, `(0, 2.5)`, `(-5, 2.5)` — and top-down reads those as `(cx, cy)`, so after the
  projection switch all seven sat on **half-cells**, with `RoundToInt` quietly deciding which tile
  a golem thought each was on, and the spread collapsed into roughly a 10×4 band on a 25×25 floor.
  Inverting the old transform (`world = ((cx − cy)·0.5, (cx + cy)·0.25)`) turned every literal back
  into an exact integer cell, which is how we know they had been authored as cells and flattened on
  the way to disk. A world literal cannot survive a projection change; a cell can.

  **Why Quadrants.** Each resource gets a *place* — a factory is remembered as a map, and a map
  needs somewhere to be. The recovered isometric spread was drawn for a diamond and was lopsided on
  a square floor (everything north and east of spawn, the west half empty). The rejected
  alternatives were **A — Even ring** (five nodes on a radius-8 ring: neutral, no resource is the
  obvious first one, but the room reads as a test chamber) and **B — North bank** (four bulk nodes
  along the north wall so belts run south toward the open camera edge: legible, but four nodes in a
  line is monotonous and it makes the sconce wall the backdrop of every early screenshot).

  **What it costs, so a playtest knows what to look at.** One thing, and it is the walk: every node
  is **7 cells Chebyshev / 14 Manhattan** from spawn (Aether 9/9), against 5 before. That is the
  longest of the three proposals and it lands directly on §9's 12–15 minute manual era — which §12
  already flags as ±25 %, so it is inside the noise the design expects to retune, but it is the
  first number to time when the arc is finally played. The 14-cell corner-to-corner runs are the
  point rather than a side effect: §3.2's 2-extractor cap and §1.4's 8-golem boiler radius only
  start to bite at distances like these, and did not at 5.

  **The clutter concern was wrong, and measuring it is what showed that.** The proposal warned that
  the corners are where the wall posts and prop scatter already live. Measured against the
  generated shell: the nearest prop to any corner node is **5 cells** away (3 for Aether), there
  are no collisions with any of the 26 props, and there are only two wall posts, both on the north
  corners and both outside the room. Nodes sit at ±7 rather than ±9 anyway, because props hug the
  outermost ring at ±12 — so the margin was never in question.

### Still open

- **The opening changes substantially.** Sandbox currently starts with three infinite nodes and a
  construction station. The design starts the player with a hand-crank bench and ~10–15 minutes of
  manual labour before their first automated line. That is the requested arc, but it is a real shift,
  and it pushes `Main.unity` further from being representative.
- **The Workbench loses its decisions.** With every program reduced to `N × Haul + Assemble + Push`,
  a recipe plus a chassis fully determines the program — the signature drag-and-drop UI has nothing
  left to decide, and the patent system's main use becomes skipping boilerplate the game forces on
  you. The design's mitigation is player-set `Haul` batch quantities (throughput traded against
  buffer pressure) — **now built and playable** (§3), so this is no longer waiting on code. What
  is left is the judgement: play a factory and decide whether one dial per logistics slot is
  enough to carry the game's signature screen, or whether the Workbench needs a larger job.
- **The Overclocker's identity.** Its flat-speed adjacency aura was cut in review — a non-local,
  adaptive effect contradicts the game's rigid local determinism. The design replaces it with a
  `Repeat(n)` appendage competing for the same slot as a third ingredient. Unbuilt, and it is the one
  chassis whose role is still only "more slots".

---

## 3. Known gaps carried forward

### Editor passes — **DONE**

All six rows are wired and read back from the saved scene. **Scripting them worked**, which the
backlog had flagged as unproven: `Scripts/Editor/ProgressionSceneAuthoring.cs` drives
`PrefabUtility.LoadPrefabContents`/`SaveAsPrefabAsset`, `EditorSceneManager.OpenScene`/`SaveScene`
and `SerializedObject` from `-executeMethod`, exactly as `ProgressionAssetAuthoring` does for
`.asset` files, and it is idempotent for the same reason — re-running after a tuning edit updates
in place rather than duplicating.

| What | Where it landed |
|---|---|
| **Boiler + Steam Pipe prefabs** | `BoilerPrefab` (30 Scrap + 10 Iron Plate), `SteamPipePrefab` (1 Iron Plate). Both in Sandbox's build menu. |
| **Clock Tower prefab** | `ClockTowerPrefab`, free to place per §7. |
| **`SteamNetworkHolder` / `ClockTowerSiteHolder` on `ManagerHolders.prefab`** | Plus `NodeExtractorRegistryHolder`, which `SandboxBootstrap` was already looking for and never finding — so §3.2's 2-extractor cap was inert in the playable scene. All three registered with the clock. |
| **HUD slots for `SteamFuelGaugeView` / `ClockTowerPanelView`** | `SteamGauge` top-left, `ClockTowerPanel` top-right on `WorkbenchCanvas.prefab`; holders handed over at runtime by `SandboxBootstrap`, because **a prefab cannot hold a reference into another prefab**. |
| **Workbench's 6th appendage socket** | `AppendageSlot5` cloned from slot 4; `SlotStack` respaced to 7 even rows. The `WorkbenchController.cs:319` truncation is fixed too — see below. |
| **Node markers for Coal / Copper / Zinc** | `CopperOreNodeMarker` and `ZincOreNodeMarker` placed; `CoalNodeMarker` retinted off the brass sprite it inherited. |

**Then it was played, and that found five more.** Everything above passed a read-back of the saved
scene, which proves the wiring is *present*; none of it proves anything *draws*. Opening Sandbox in
the Editor and pressing Play found, in order:

1. **Both §8 readouts were invisible.** They were parented to `WorkbenchCanvas.prefab`'s **root** —
   a plain `Transform` that shares its name with the `Canvas` child, so a find-by-name returned the
   wrong one. Present, active, positioned, right text, and never rendered, because a
   `RectTransform` outside a Canvas does not draw. Every property-level check passed.
2. **The build menu overflowed.** Its panel was authored at 280×170 for three entries; six pushed
   Steam Pipe and Clock Tower off the bottom edge — placeable in principle, unreachable in fact.
   The height is now derived from the row count.
3. **The Clock Tower alarmed from the first frame.** Stages wired onto the always-present
   `ClockTowerSiteHolder` start stage 1 in `Awake`, so a fresh game opened reading "Stage 1
   Foundation · 0%" and "starved of FrameSection · progress frozen" for a megaproject that does not
   exist. The stage list now rides `ClockTowerPrefab`, so **building the tower is what starts it**
   and an unbuilt site reports the `Dormant()` it already knew how to format.
4. **The node tints did nothing** — see the `ResourceNodeMarker` note below.
5. **The sixth socket said "STEP 5".** It is cloned from slot 5 and arrived carrying its caption,
   so two sockets claimed to be the same step in the one screen whose job is showing program order.
   Captions are now derived from the index.

> **Node identity has to be sprite, not colour.** `ResourceNodeMarker.RefreshVisualState()` **owns**
> `SpriteRenderer.color` — it is the depletion readout — and since §5.1 made every node infinite it
> pins every marker to `FullTint` (white) on the first frame. An authored tint is correct on disk,
> correct in the Editor, and gone on Play. So `item_coal`, `item_copper_ore` and `item_zinc_ore` are
> now real (placeholder) sprites in `Tools/Art/generate_placeholder_art.py`, drawn to differ from
> each other and from Scrap/Brass/Aether **by silhouette**. This had already bitten unnoticed: the
> Aether marker wore the brass ingot under a teal tint and rendered as a second brass ingot in every
> play session, while `item_aether.png` sat unused. It is now pointed at its own sprite.

> **The prefab-override trap, worth remembering.** Writing `hideWhileOpen` on
> `WorkbenchCanvas.prefab` reported success and did nothing: `Sandbox.unity` carries an override
> pinning that array to 1 entry, so both new readouts would still have drawn over the full-screen
> Workbench — the exact HUD-overlap class of bug the UGUI conversion was done to kill. **An
> authoring pass reports what it wrote; only a read-back reports what the game will load.**
> `SceneProbe.Verify` is that read-back, and it is why this is claimed as done rather than assumed.

### Two functional gaps found while doing it, both fixed

- **Nothing could refuel a Boiler.** `SteamBoiler.AddCoke` was written as "the only way Coke ever
  goes up" and no caller was ever added, so a boiler was a sealed tank holding whatever it was
  constructed with. That caps the whole steam economy at §9 Phase 1's opening 240 Coke however
  much coking capacity is built — §9's "~12 Boilers" and ~23 cokers have nowhere to feed, and
  §3.1's "Coke becomes the contended throat of the entire game" collapses to a one-off allowance.
  The design assumes delivery throughout (§5.1 lists Coke's consumers as "every powered golem,
  **via its Boiler**") but never names a mechanism, because the mechanism is meant to be the
  ordinary one. `Steam/BoilerFuelEndpoint` is that: an `IItemEndpoint` on the boiler's cell,
  modelled on `ClockTowerInputEndpoint` down to the shape of the two `CanGive` answers, so a golem
  `Push`es Coke into it through the same `BeginPush` with the same per-type skip. Pure sink —
  fuel never comes back out, which also stops a boiler being an uncapped Coke warehouse that
  §1.2's per-item-type cap does not apply to.
- **Every pre-existing placeable had been free to build since §1.5.** That pass replaced
  `PlaceableBuilding`'s `scrapCost`/`brassCost` int pair with a `RecipeIngredient` bundle (§11
  item 8) and never migrated the three prefabs authored against the old pair, whose values are
  still sitting in the YAML as orphaned keys no field reads. Nothing ever refused a placement, so
  nothing drew attention to it; it became obvious the moment a priced Boiler appeared in the same
  menu as "Depot (Free)". Restored from those orphaned keys — Depot 15 Scrap, Station 25 Scrap +
  5 Brass, Belt 1 Scrap — which are the original numbers, not new tuning, and §5.1 independently
  corroborates the belt ("Scrap … belts (1 ea.)").
- **`BuildModeController` never registered a placed Clock Tower.** `RegisterPlacedEndpoints`
  handled belts, depots, boilers and pipes but not the tower, so a placed tower published no input
  tile and the win condition could be built and then never delivered to. Both it and the boiler's
  fuel hatch are now registered on placement and unregistered on removal.

**A player-built Boiler starts at 0 Coke**, not `PlaceableBoiler.DefaultStartingCoke`'s 240. That
constant is there because §9 Phase 1 hands the player one already holding 240; shipping it on the
prefab would mint 240 Coke for 30 Scrap + 10 Iron Plate — cheaper than R1 makes it, and a
build-a-boiler exploit that voids §3.1 entirely.

### The opening was soft-locked — **fixed**, the Hand-Crank Bench is built

Found by trying to answer "how do I build a Boiler?". Two separate problems, one fixed and one not.

**Fixed: the Workbench was still offering M8's four-card tutorial deck.** `availableAppendages` held
`ExtractScrap` / `HaulScrap` / `LoadIntoScrapBuffer` / `RefineIronPlate` while §1.5 had authored
nineteen `Assemble` cards and the `Push` card, none of which anything referenced. Since Iron Plate's
only two sources are R2 and R4 — both `Assemble` — **there was no route to Iron Plate in the game at
all**, and therefore no Boiler, no Steam Pipe, and nothing for the entire steam system to be spent
on. The roster is now all 23 usable cards (4 verbs + R1–R19), ungated, which keeps §3's standing
"every card available from the start" deferral rather than inventing a gating rule. `RefineIronPlate`
is deliberately omitted: it is keyed to `ScrapBuffer`/`IronPlateBuffer`, neither of which exists in
Sandbox, so it is a guaranteed stall wearing the name of the thing the player wants.

**Not fixed, and now the critical path: the chassis ladder is circular.**

| Chassis | Slots | Cost |
|---|---|---|
| Clockwork Scavenger | 2 | 12 Scrap |
| Brass Presser | 3 | 60 Scrap + **20 Iron Plate** + 10 Gear |

A production golem needs **three** steps — `Extract`/`Haul` → `Assemble` → `Push`. Two slots cannot
do it: with an `Assemble` in the program the output stock has no way out, so a 2-slot Scavenger
running `Extract + Assemble R2` makes twelve Iron Plate, hits the per-type cap and stalls
`OutputFull` **with the plates sealed inside it** — nothing outside a golem can reach its stocks, by
the §1.3 atomicity design. So the first usable chassis is the Presser, and the Presser costs Iron
Plate that only a Presser can make.

This is not a design error. §9 Phase 1 is explicit — *"Hand-gather and hand-crank your way to the
first Brass Presser… The Presser costs 60 Scrap + 20 Iron Plate + 10 Gear — **all hand-made**"* — so
the **Hand-Crank Bench (§11 item 7)** is what breaks the cycle.

**It is now built** (`Buildings/HandCrankBench.cs` + `HandCrankRules.cs`). Held-Interact turns it,
`R` at the bench cycles which recipe it makes, progress accrues on simulation ticks so it follows
Play/Pause and the speed multiplier, and inputs are taken **at completion, never at the start** — so
letting go of the crank costs time and never goods, which a rigid game has no step to refund. One
stands in Sandbox at spawn per §9, and it is also placeable and free (§11 prices it at nothing, and
§10's blackout backstop must never be unaffordable).

**Verified end to end in Play mode:** from an empty stockpile, hand-harvesting 100 Scrap and
cranking 40× R2 then 10× R8 yields exactly 60 Scrap + 20 Iron Plate + 10 Gear, and
`TryWithdrawBundle` accepts the Brass Presser. **7.5 minutes of cranking** against §9's 12–15 minute
estimate for the manual era — §6's costs and §11's 25 % speed were designed together and they land.

> **A third design discrepancy, found and resolved in favour of arity.** §11 item 7 says "any
> 1-input **Tier-1** recipe", but §5.1 classes Gear — R8's output — as **Tier 3**, while §6 and §9
> both require the Presser's ten Gears to be hand-cranked. Read literally as Tier-1-only the bench
> makes Coke, Iron Plate and Glass but **not Gears**, the Presser stays unbuildable, and the bench
> fails at the one job Phase 1 gives it. **Arity is the load-bearing constraint** — one distinct
> input type is what a person feeding a machine by hand can manage — and it is self-limiting:
> exactly five of the nineteen recipes take one input (R1, R2, R3, R8, R19) and every Tier 4–5 good
> needs three or four, so **no amount of cranking reaches the endgame**. "Tier-1" reads as shorthand
> written before R8's output was classified. Pinned by `HandCrankBenchTests`.

**`requireSteamPower` was *not* unblocked by the bench alone** — this was claimed here and it was
wrong. §10's backstop needs two halves and the bench is only one of them.

> **The blackout loop, found by trying to turn the switch on.** A boiler's only Coke writer was
> `BoilerFuelEndpoint`, which a **golem** `Push`es into. A golem needs a powered boiler to move.
> A player-built boiler starts at **0 Coke** (deliberately — `DefaultStartingCoke` is §9's opening
> gift and shipping it on the prefab would mint 240 Coke for 30 Scrap + 10 Iron Plate). And
> `Sandbox.unity` contains **zero** `PlaceableBoiler` instances. So with the switch on, the first
> boiler could never be lit: every golem stalls `NoSteam`, and nothing that is stalled can carry
> fuel. The bench solved the *goods* half — it can make Coke — and nothing moved that Coke the
> last three feet into the firebox. Flipping the switch would have been a hard soft-lock of the
> only playable scene.

**Fixed: the player can hand-load a boiler.** `[E]` on a boiler moves Coke from the stockpile into
it (`Player/BoilerRefuelPolicy` + `PlayerInteractor.TryRefuelBoiler`), the mirror of harvesting.
Deliberately **not** routed through the fuel endpoint — that is the golem-facing tile contract, and
reaching it would mean inventing a spatial position for a player who has none.

**The batch size is doing design work, not just UX.** 20 Coke is 2000 powered-golem-ticks: ~200 s
of one golem, or ~25 s of a fully subscribed 8-golem boiler. So one press is a real emergency
top-up, and keeping a live factory running by hand would mean standing at the firebox pressing a
key forever — which keeps hand-fuelling a way to **restart** a dead factory rather than a way to
**run** one. §3.1 wants Coke to be the contended throat of the game; a generous hand-load would
quietly void that. Capping also lets a player split what they have between two cold boilers.

**The fuel ratio itself is fine, and it is arithmetic rather than taste.** A coker at batch 1 is a
17-tick cycle (2 haul + 12 assemble + 3 push) → **35.3 Coke/min**, against **6 Coke/min** per
powered golem, so one coker sustains ~5.9 golems. That clears §10's 3.3 : 1 comfortably. The
economy converges; what was missing was never the ratio, it was the bootstrap.

**Turning the switch on is still a separate call**, and now a purely *design* one: it reshapes the
opening into "harvest by hand → crank Iron Plate and Coke → build a boiler → light it by hand →
build golems", which is §9 Phase 1's arc but is exactly the "the opening changes substantially"
decision §2 reserves. Nothing mechanical blocks it any more.

### Also not yet wired

- ~~**Save/load does not persist Clock Tower progress.**~~ **DONE**, and it turned out to be
  downstream of a much larger hole — see the save/load entry below. Stage index, progress units
  and the completed flag now ride the tower's own `BuildingEntry`.

  **The rate windows are deliberately NOT restored.** Stage progress is a durable achievement
  (tens of minutes of a whole factory's output); the 60-second delivery and fresh-production
  windows are estimates of what a factory is doing *right now*, stamped with tick numbers
  `SaveData` does not persist. Restoring them would replay samples against a tick counter that
  has restarted. They rebuild within a minute, which is the honest answer: a tower resumes at the
  rate its factory can actually supply, not the rate it managed before the player quit.
- ~~**`requireSteamPower` is still off.**~~ **ON**, in `Sandbox.unity`, at the project owner's
  call. The field default stays `false` so other scenes and test rigs are unaffected; the
  serialized **scene** value is what turns it on. The opening is now §9 Phase 1's arc:
  hand-harvest Scrap and Coal → crank R2 for Iron Plate and R1 for Coke → build a Boiler
  (30 Scrap + 10 Iron Plate) → hand-load it → build golems within its reach. **Unplayed** — the
  fuel ratio is the number §12 says to time first.

- **A player-built construction station is a decorative box**, and turning steam on is what made
  this worth writing down. `GolemConstructionStationPrefab` is in Sandbox's build menu at 25 Scrap
  + 5 Brass, but every serialized reference on it is null — no chassis roster, no golem prefab, no
  buffer registry — so `TryConstructGolem` early-outs and the station builds nothing. Stations are
  only ever wired by `SandboxBootstrap.WireSpatialGameplay`, which sweeps the scene **once at
  startup**; the comment there claiming newly built stations "configure themselves via
  PlaceableBuilding's own wiring path" describes a path that does not exist.

  It is **not** a steam loophole — a station that cannot build a golem cannot build an unpowered
  one — but it is a building the player can pay for and get nothing from. The fix is to wire a
  placed station from `BuildModeController.RegisterPlacedEndpoints`, the way belts, depots and
  boilers already are, which also needs the runtime holders a prefab cannot carry.
- **The whole arc is unplaytested.** §12 names the boiler fuel ratio as "the single most important
  number to playtest first" — mis-tuned, the game becomes a coal simulator — and flags every golem
  count in §7/§9 as ±25 %. Nothing here has been played, only tested.

### Opened by the §1.1 machine-model pass

- ~~**The Workbench has 5 appendage sockets; the Zeppelin now has 6.**~~ **DONE.** `AppendageSlot5`
  is authored and `appendageSlotZones` is 6, so the Zeppelin's sixth step is reachable. The
  silent truncation is fixed at the same time, and **not by widening the array alone**: overflow is
  now recorded on load (`WorkbenchController.DraftOverflowCount`) and both Engage Gears and Patent
  *refuse* rather than committing a program the player cannot see. Committing the visible five was
  data loss with no warning and no undo; committing four of six would be the same thing with extra
  steps. With the socket authored this should never fire in normal play — it is the guard that
  makes a future 7-slot chassis a refusal instead of a silent discard. The refusal costs no Focus,
  and its status line is deliberately not time-based (the program is still unrepresentable after
  six seconds), retiring only when retargeted onto a golem that fits.
- ~~**No UI for `Haul` batch quantity.**~~ **DONE.** Each `Haul`/`ExtractFromNode` slot card now
  carries a `[−] ×4 · 10t [+]` stepper, clamped 1…12 (the golem's own per-item-type stock cap, not
  a number picked for the UI). It edits the **draft**, so trying 8 and putting it back costs
  nothing and no Focus, exactly like picking a card up and setting it down.

  **The tick cost is shown because it is half the decision.** §2's trade is throughput against
  buffer pressure; a stepper showing only the number would hide the part that makes it a choice.
  The figure is quoted from `Golems/StepDurationRules` — the same functions `GolemEntity` charges,
  extracted from it for this — rather than a second copy of `max(2,q)` / `6+q` in the UI that
  would drift the first time either was retuned.

  **A defect this uncovered, which made the stored value pointless.** `EngageGears` rebuilds a
  program with `TryAddAppendage`, and that seeds each slot from the **card's** authored default —
  so every reprogram silently reset every batch size in the golem, including one restored from a
  save moments earlier. The value save/load had been carefully round-tripping was one the game
  overwrote the next time the player touched the lever. Quantities are now applied after the
  appendages, indexed by where each card **landed** rather than by its socket, since a program
  packs and the sockets do not.
- **A one-card `ExtractFromNode` program now jams.** Extract fills internal stock instead of
  reaching the tile in front, so a spatially placed golem with no `Push` fills to 12 and stalls
  `InputFull`. Correct by design (a Scavenger is 2 slots: Extract + Push) and the stall names the
  blocked good, but it is a live behaviour change for anyone with a saved Sandbox factory.

### Carried forward from before

- ~~**Save/load cannot respawn player-built golems.**~~ **DONE**, and the gap was twice the size it
  looked. Golems now come back (`IGolemRespawner` → `GolemConstructionStation.TryRespawnGolem`), and
  so does everything they route through: `SaveData` had **no concept of a placed building at all**,
  so belts, depots, boilers, steam pipes and the Clock Tower were simply gone on reload. Restoring
  golems alone would have brought a factory back already stalled, pushing into tiles that no longer
  held anything.

  Buildings ride `IBuildingRebuilder` → `BuildModeController.TryRebuildSavedBuilding`, which is the
  same reasoning as the golem respawner: the controller is the rebuilder because it is already the
  builder, and duplicating its grid occupancy plus three endpoint registrations would create a
  second definition of "a building that works". A load **replaces** the built world (clear, then
  rebuild) for the same reason buffers are cleared before being replayed.

  Neither path charges the cost again — the player paid in the session that built it, and charging
  on load fails outright for anyone who has since spent their stockpile.

  **Only what the player placed comes back.** Scene-authored buildings and golems are excluded by
  explicit flags (`IsRuntimePlaced` / `IsRuntimeSpawned`) that default to false, so `Main.unity`'s
  hand-wired demos and Sandbox's two starter buildings are untouched — the same opt-in fork spatial
  routing, steam and the machine model all ride.

  **Still not saved, and deliberately:** belt *contents* and the tick counter, which `SaveData` has
  always excluded as "continue where you left off, not a simulation snapshot".
- **World-space HUD collides.** Stall badges and the interaction caption both anchor to the golem and
  overlap when two stand close. Reduced, not solved; needs a world-space layout pass.
- **Belts are one cell per segment**, no merge or splitter, one endpoint per cell, so two belts
  cannot feed the same tile. A belt can only hand off to another belt — getting items into a buffer
  requires a golem doing `LoadIntoBuffer`. The progression design leans on this constraint
  deliberately, but merges will eventually be wanted.
- **`ComputeItemScale` clamps at `maxItemScale = 1.0`**, so on Sandbox's 0.32 lane spacing the
  auto-fit never engages and cargo overflows the lane silhouette.
- **Belt arrows under-report speed at partial congestion** (`speed × (1 − congestion)`); the correct
  fix derives from actual head throughput.
- **Belt jam signalling is off on placed belts** — a one-cell lane has no room for scrolling arrows,
  so a backed-up player belt looks like a flowing one apart from cargo sitting still.
- ~~**Chassis sprites pivot centre, so golems render sunk into the floor.**~~ **FIXED.** All five
  chassis and the three `golem_generic` bodies are BottomCenter now
  (`Scripts/Editor/CharacterArtAuthoring.cs`, re-runnable), so a golem stands on the cell it
  occupies instead of three quarters of a tile south of it. `Main.unity`'s seven hand-placed
  golems each dropped 0.75 to keep their apparent positions, and `GolemStallIndicator.worldOffset`
  went 1.0 → 1.75 because it measures from the transform, which now means the golem's feet.
  `GroundShadow` needed nothing: it reads `sprite.pivot.y` rather than assuming a convention.

  **Still open, and now measurable: the chassis art is not trimmed to its alpha bounds.** The
  pivot is the canvas bottom, and five of the eight sprites carry transparent rows beneath the
  feet — Aether-Hauler 10 px, Brass Presser 6, the three generics 4, Zeppelin 3, at PPU 64. So
  they float between **0.047 and 0.156 of a cell** above the floor. That is an art job (the same
  alpha-trim the three buildings got) and belongs to the generator, not the importer.

- **Sprite pivots are still inconsistent project-wide — three conventions and one trap.** The
  characters agree with the buildings now, but the environment art solved the same problem a
  *different* way, and an audit of every `.meta` in `Assets/_Project/Art/` finds:

  | Convention | Assets |
  |---|---|
  | **BottomCenter** (`alignment: 7`, `y=0`) | 5 chassis, 3 `golem_generic`, `clock_tower`, `steam_boiler`, `hand_crank_bench` |
  | **Custom** (`alignment: 9`), pivoted on the contact line | head-on walls `y=3/96`, side walls `x=3/40` and `37/40`, corner post `1/96`, props `1/56`, floor edges `y=1` |
  | **Center** (`alignment: 0`) | all 6 items, floor tiles, belts, overlays, `building_block` |
  | **Stale/contradictory** | `player`: `alignment: 7` but `spritePivot {0.5, 0.5}` |

  This is no longer four *rules* — it is two rules plus a trap. **BottomCenter is the convention
  for anything that stands on the floor**; **Custom-on-the-contact-line is the convention for
  pieces of the room**, which need a horizontal contact line (the side walls' is vertical) or an
  offset for the shadow the art draws under itself; **Center is correct for what is centred on a
  cell rather than standing on one** (items, tiles, cursor overlays). And **`player` is a trap for
  anyone auditing by eye**: Unity honours *alignment*, not the stored `spritePivot`, so it behaves
  as BottomCenter while its record says centre — read `alignment`, not `spritePivot`.
- **`Main.unity` is a diorama, not the game** — seven pre-wired golems demonstrating M2–M7, with no
  spatial routing (its golems are deliberately never `ConfigureSpatial`'d, which is exactly what
  keeps id-based routing working there). `Sandbox.unity` is the playable loop. The two will keep
  diverging; at some point `Main.unity` should be retired or explicitly reframed as a test bed.

### Presentation polish deferred from the production-quality pass
Each of these was reviewed and judged non-blocking:

- Workbench: no hover/press states on vault cards or chassis buttons (measured at a ~4% colour
  shift, effectively invisible); lever housing is basic hand-coded pixel art; LiberationSans SDF
  rather than a period display face; procedural grain visibly repeats; cards are text-only with no
  per-action icons; dead space below 5 chassis entries and ~8 vault cards.
- Environment: the floor is monotone at gameplay zoom with no feature larger than one tile; the
  interior is an empty box (no workbenches, shelving, or hearth — the biggest gap against "cozy,
  detailed"); lighting is even rather than dramatic.
- Economy: stock bars are still relative-only. §1.2 supplied the missing capacity concept
  (`StorageBuffer.CapacityPerType`/`RoomFor`) but did not touch the UI, so nothing reads it yet —
  this is now a straightforward wiring job rather than a blocked one. The rate readout is also
  still *net* stock change, not gross throughput, so 60/min in and 60/min out reads as Steady.
- Belt art is direction-neutral with a rotated chevron rather than a proper mirrored NE/NW
  isometric pair.

---

## 4. Deliberate scope cuts still standing

- `Refine` stays id-routed rather than spatial, on purpose: recipes are typed and the *untyped*
  `IItemEndpoint.TryTake` is not, so a spatial take could grab the wrong input and silently
  transmute it. Item 1.1 **resolved this from the other end** rather than by making `Refine`
  spatial: `Assemble` reads a typed dictionary the golem itself owns, so it needs no spatial take
  at all. `IItemEndpoint` did grow a typed take — for `Haul` — but the exemption stands as written.
- Pixel Perfect Camera installed but not enabled — conflicts with the free-zoom `CameraRigController`.
- No player collision; the player walks through buildings and golems.
- No refund on removing a placed building.
- `AssemblyBayStructure`'s capacity/tier upgrade is implemented and tested but still not wired into
  the Sandbox loop. The progression design gives it a job (the golem cap); until then it is inert.
- The Assembly Line still does not gate the Workbench roster — every card is available from the
  start. This is the M9-era deferral, and the progression design is what finally resolves it.
