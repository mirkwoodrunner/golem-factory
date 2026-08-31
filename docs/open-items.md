# Golem Factory — Open Items

Consolidated backlog as of the progression-design pass, on branch
`polish/production-quality-pass` (17 commits, **not merged to `main`**).

Everything here is known and deliberate — none of it is a surprise waiting to be discovered.

> **Where the line is.** Everything through §1.4 (steam power) is **built, tested and
> reviewed**. Everything from §1.5 on is **spec only**. The next pass starts at §1.5, which is
> also what §1.4's Sandbox switch is waiting on.

Tests stand at **1249/1249** (1100 EditMode + 149 PlayMode), up from 590 before the progression pass
began. Console clean.

> **THE BUILD QUEUE IS EMPTY AND THE PLAYTEST IS THE CRITICAL PATH.** Everything on this file is
> built; §3z below is the authoritative list of what is left, and `testscript/phase-1-playtest.md`
> is the script for the half of it that needs a person. `Main.unity` is retired -- `Sandbox.unity`
> is the only scene.

> **The Director's pass is validated.** Street extension, truckload market + Creative Mode, the
> bay upgrade row and §8's Assembly-Line gating are authored to disk, read back from the saved
> scene, and **1178/1178 tests pass** (1029 EditMode + 149 PlayMode). Validation earned its keep:
> it caught three real defects that compiling could not — the deck generator asking for
> `AssembleAssembleCoking` and silently skipping all nineteen recipe cards, the four movement
> verbs quietly priced at 20 Scrap by a legacy default, and a zero-cost claim being refused by
> the one payment path that had never guarded it.

> **THE COZY AUTOMATION PASS HAS LANDED.** Six tasks, designed up front in
> `docs/cozy-automation-design.md`: smart depot filtering, golem moods, the Workbench's loop
> labels, the Scrap Recycler, the Ledger's recipe readout, and furniture in the workshop. It also
> took §3z B's dead M2 demo and §3z C's items 1 and 3. **1220 EditMode + 160 PlayMode green**, up
> from 1100 + 149. See the cozy-automation-pass milestone in `docs/unity-implementation-plan.md`.
>
> It found **three real bugs nothing was checking for**: three Ledger nodes that could never light
> (the catalog named nine building signals and the sweep recorded six), fifteen glyphs in the
> catalog the TMP atlas cannot draw, and a recipe-node lookup that silently fell back to a
> hand-written line. It also **disproved its own design doc's safety argument** for the recycler,
> and the doc records the correction rather than hiding it. §3z below is updated.

> **A backlog pass has just been through this file**, taking the eight actionable items off it: the
> decorative construction station, the unbounded build area, the alerts strip, the two lying economy
> readouts, the belt arrow speed, the untrimmed character art and the last three crates, `Repeat(n)`
> and the Assembly Bay cap. See the backlog-pass milestone in `docs/unity-implementation-plan.md`.
> **None of it has been played** — it is tests and a scene read-back, and §12's "the boiler fuel
> ratio is the first number to time" is still the first thing owed.

> **There is a tech tree chart in the game now.** *The Artificer's Ledger* is a fifth Management
> tab showing §9's six phases as columns and 44 nodes as cards — researched / available / locked /
> designed-but-unbuilt — driven by what the player has actually produced, built and completed. It
> **does not** implement §8's gating: it is a readout of the track, not a gate on it, and §1.7
> below records what changes when the gating does land. See the Ledger milestone in
> `docs/unity-implementation-plan.md` before touching `Progression/` or the Management screen.

> **The map has an outside now.** The workshop is no longer the whole world: a cobbled market
> street runs eight rows south of the open shop front, and the five raw goods are bought from
> traders standing on it rather than dug from boulders on the factory floor. This supersedes the
> "Quadrants" node layout below. Read the market-street milestone in
> `docs/unity-implementation-plan.md` before touching `FloorLayout`, the tile painter or the wall
> runs — in particular, `GetFloorCells` still means **the workshop only**, and the thing that
> bounds the *player* is `GetWorldCells`.

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

### 1.7 The tech tree chart — **DONE (as a readout, not a gate)**

`Progression/TechTreeCatalog.cs` transcribes §9's six phases, §6's five chassis and §5.2's nineteen
recipes into a 44-node track, drawn by `UI/TechTreePanel.cs` on a fifth Management tab. Pixel-art
chrome is generated by `Tools/Art/generate_tech_tree_art.py`; the chart itself is built from data
at runtime, so it cannot drift from the recipe and chassis assets — and `TechTreeCatalogTests`
holds it to them.

**A node lights up from the world, not from a claim.** §8's Assembly-Line gating is still spec
(§11 item 12, and the standing "every card available from the start" deferral below), so the chart
reads what the player has demonstrably done: an item produced (`ItemAssembledEvent` plus a buffer
sweep, because a load restores buffers and not events), a chassis carried by a golem, a building
placed, a tower stage completed. That is the ordering §8 will eventually enforce, observed from
the other end.

**Six nodes are flagged `IsPlanned`** — `Repeat`, the Freight Link, the Freight Mast, the Slag
Heap, Floor Expansion, the Assembly Bay cap — the things this file records as designed and unbuilt.
They can reach `Available` and never `Researched`, and a test pins the exact six by name, so
**building one of them fails the suite**. That is deliberate: it is the reminder to clear the flag,
rather than leaving the chart quietly calling a shipped feature "planned".

**What lands when §8's gating is built:** `TechTreeUnlockSignal` grows a `Card` case,
`TechTreeProgressLedger` grows a claimed-card set, and the chart reads the claim ledger *as well
as* the world. Nothing else about it changes — which is why it was built this way round.

**One pre-existing bug fixed on the way through.** `hideWhileOpen` lives only on
`WorkbenchController`, despite a comment claiming it covers the Management modal too, so the steam
gauge and Clock Tower panel drew over the Management screen. `SetWorldHudVisible(bool)` is now
public and `ManagementPanel` calls it -- one authored list, not two. ~~**The alerts strip is still
not in that list**~~ -- **it is now**, in the prefab and in the scene's override of it, and the
read-back says so: `hideWhileOpen = 5` (BuildMenuPanel, SteamGauge, ClockTowerPanel, HandCrankPanel,
AlertsStrip).

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

- **The opening is §9 Phase 1's arc, and it is live.** This sat in "Still open" as "the opening
  changes substantially" long after the decision had actually been taken and shipped:
  `requireSteamPower` is **ON** in `Sandbox.unity`, the scene opens on hand-harvest → crank → build
  a Boiler → hand-load it → build golems, and the five market stalls replaced the three boulders it
  described. The bullet was deleted rather than restated, because a decided thing sitting in the
  open column is worse than no entry at all -- it invites the same decision to be made twice.

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

- ~~**The Sandbox map layout is "Quadrants"**~~ — **SUPERSEDED by the market street.** Quadrants
  was right while the workshop was the whole map, and wrong the moment there was an outside: it
  left five ore boulders standing on the factory floor, and `docs/game-design.md` puts the resource
  markets on the *edge of the board*. The five traders now stand on the street at `y = -16`, four
  cells apart; the Hand-Crank Bench and the construction station stay inside, because they are
  workshop equipment and §9's Phase 1 is spent in a cold workshop rather than in the road. See the
  market-street milestone in `docs/unity-implementation-plan.md`. The `StartingLayout` table at the
  top of `Scripts/Editor/ProgressionSceneAuthoring.cs` is still where it lives, still **in cells**:

  | Scrap | Coal | Aether | Copper | Zinc | Bench | Station |
  |---|---|---|---|---|---|---|
  | `(-8, -16)` | `(-4, -16)` | `(8, -16)` | `(0, -16)` | `(4, -16)` | `(0, 3)` | `(3, -1)` |

  **What survives from Quadrants is the reasoning below, not the table.** "Each resource gets a
  place" still holds — a stall row is a place, and a more legible one than four corners. What does
  **not** survive is the distance budget: every stall is now **16 cells Chebyshev** from spawn
  against Quadrants' 7, so the walk that §9's 12–15 minute manual era is measured in has more than
  doubled. That is the first number to time, ahead of the boiler fuel ratio.

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

- ~~**The street's shape needs an owner call.**~~ **DECIDED by the Game Director: extend
  east/west.** The world grows horizontally to carry §3.2's full **8 node sites**, keeping the
  single stall row and therefore the front-row extractor clearance the two-extractor cap depends
  on. The reason given is layout, not tiles: one row of stalls lets a player run clean parallel
  vertical buses north into the factory, where a second row would force every line to route around
  it. The cost accepted with the decision is that the world is no longer as wide as the workshop,
  so the side walls no longer bound it in one run -- see §1.8 for how that was built.
- ~~**Nothing about the market is priced.**~~ **DECIDED by the Game Director: implement full
  truckload shipments.** A market order is **paid for and delivered as a batch burst** rather than
  trickling out of an infinite hole, which makes the market a late-game economic sink and gives
  buffer chests and accumulator lines a job. **With an `isCreativeMode` bypass**: with creative
  mode on, orders cost nothing and deliver as the old infinite steady stream, so the boulder-era
  behaviour is a state the game can still be put into rather than a version of it that was
  deleted. See §1.9.
- ~~**`BuildModeController` bounds placement by `GridMap` occupancy alone**~~ -- **DONE**, and the
  predicate this file named was the wrong one. `FloorLayout.IsInsideWorkshop` would forbid building
  on the market street, where all five traders stand, so no belt or depot could ever reach them --
  a worse bug than the one being fixed. The bound is the **world** (`FloorLayout.IsInsideWorld`,
  the predicate form of `GetWorldCells`), turned on by `SandboxBootstrap` next to the identical
  call that bounds the player, so "I can walk there" and "I can build there" are one boundary.
  Unbounded by default (`-1` sentinel), so Main.unity and every test rig are unchanged, and
  **removal is deliberately not bounded** -- a building left off the ground by an old save must
  always be removable.
- **The Workbench loses its decisions.** With every program reduced to `N × Haul + Assemble + Push`,
  a recipe plus a chassis fully determines the program — the signature drag-and-drop UI has nothing
  left to decide, and the patent system's main use becomes skipping boilerplate the game forces on
  you. (Since Focus was cut, skipping boilerplate is the patent system's *only* use.) The design's mitigation is player-set `Haul` batch quantities (throughput traded against
  buffer pressure) — **now built and playable** (§3), so this is no longer waiting on code. What
  is left is the judgement: play a factory and decide whether one dial per logistics slot is
  enough to carry the game's signature screen, or whether the Workbench needs a larger job.
- ~~**The Overclocker's identity.**~~ **BUILT.** `Repeat(n)` re-runs the immediately preceding
  `Assemble` n more times from the same input stock, at n x its duration, Overclocker-only via
  `ChassisDefinition.allowsRepeat` (a flag on the data, written from the same authoring table as
  the costs -- not a name comparison a renamed asset breaks). It runs as n **sequential**
  assemblies rather than one multiplied step, which is what keeps §1.3's per-assembly atomicity:
  a repeat that runs dry keeps the batches it finished and strands nothing. §6's claim that the
  12-per-type cap makes `Repeat` on R15 impossible falls out for free -- the second iteration
  stalls `MissingItem`, because 20 Casing cannot be held.

  **What is still a judgement call** is whether it is enough: the Overclocker now has a verb, and
  whether one dial plus one exclusive card gives it an identity beyond "more slots" is a
  playtest question, not a code one.

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

### The Director's pass

- **The bay cap is no longer a wall.** §8's cap went in with `TryUpgrade` reachable from
  nowhere, so the game stopped dead at ten golems and §9's phases 4-6 need ~18. The Assembly
  Line tab now carries a `Bays 7/10 · +6 slots · 40 Scrap + 20 Iron Plate · [Upgrade]` row
  (`UI/AssemblyBayRowPolicy` for the wording and affordability, the panel for the rendering).
  The button is non-interactable when unaffordable rather than clickable-and-refused, matching
  the Claim button beside it.

- **The street runs east and west** (§13.1), so the world is a **T** rather than a rectangle:
  `StreetHalfExtent = 18` against the workshop's 12, `IsInsideWorld` and `ClampToFloor` are
  region-aware, the side walls step out at the shop front, the kerb spans the street's width,
  and two new "shoulder" runs close the gap beside the shop front. **The original five stalls do
  not move** -- the pitch and origin are unchanged and the four new sites continue outward, so
  walk distances (on the Director's do-not-tune list) are untouched. Nine sites now, one per
  bulk good doubled and Aether left single, so the exotic input stays scarce by access.

- **The market sells truckloads, and Creative Mode bypasses it** (§13.2).
  `Economy/TruckloadMarket` prices an order, charges it atomically and delivers the whole load
  after a delay via `ResourceNode.Deliver`; everything downstream -- harvesting, `ExtractFromNode`,
  the two-extractor cap, the marker's depletion tint -- is untouched and simply sees a stall that
  has stock and later does not. `[E]` on a full stall harvests and on an empty one orders, so one
  key covers both without ambiguity. `Economy/GameMode.IsCreativeMode` restores infinite, free,
  steady supply; **no holder means creative**, which is how `Main.unity` and every existing test
  keep the behaviour they were written against. **The Scrap stall is free and seeded** -- §10
  forbids a soft-lock, and a priced market with an empty stockpile is one.

- **§8's Assembly-Line gating is built.** All four changes: bundle claim costs with decay scaling
  the whole bundle (legacy Scrap cards untouched, so M9's demo deck is unrepriced), prerequisites
  that keep a locked card out of the pool entirely rather than offering something unbuyable,
  `isUnique` so recipe and chassis cards leave the pool (defaulting **false**, so cards authored
  before §8 keep cycling), and a vault filtered to claimed cards, plus the tech tree's `Card`
  signal with a claimed-card ledger. §8's **Focus scaling** was built here too and has since been
  **cut with the rest of the Focus meter** — see "Focus is gone" below.

  Two things it needed that the design does not spell out. A **deck** -- gating is half a feature
  until the thing it gates exists, so `ProgressionAssetAuthoring` generates one card per recipe
  and chassis, deriving each claim cost from the recipe's own inputs (§8.2's rule as code rather
  than nineteen transcribed bundles) and its prerequisite from the recipe's first input. And an
  **opening hand** -- the movement verbs granted free at t=0, because §9 Phase 1 programs a golem
  within minutes and waiting for `Push` to drift up a drip-fed queue is §10's soft-lock wearing a
  slot machine's face. The multipliers on both are tuning, flagged as such.

  The switch is `SandboxBootstrap.gateWorkbenchRoster`, field-default **false** and turned on by
  the scene value, exactly as `requireSteamPower` is -- one line to flip back.

- **§6's Freight Link is built.** `FreightLaunch` empties a Zeppelin's push stock onto its bound
  mast's tile regardless of distance, at a flat 24 ticks, Zeppelin-only via
  `ChassisDefinition.allowsFreightLaunch` -- the same chassis-gated-verb pattern `Repeat`
  established. `PlaceableFreightMast` (20 Brass + 10 Casing) publishes its own receiving tile
  exactly as a depot does, so the goods land somewhere a belt or another golem can work from, and
  registers in `FreightMastRegistry` so a Zeppelin can bind to it.

  Three decisions worth keeping. **The pair is fixed at placement** (nearest by Chebyshev, ties
  broken by cell order): a per-tick search would re-route a working factory the moment a mast was
  built elsewhere, and an uncontracted tie-break would let two identically-built factories
  diverge. **The launch shares `Push`'s loop** (`PushStockInto`) rather than copying it, because
  the per-type skip in there is §10's deadlock fix and a second copy is a second place for a full
  Slag slot to start blocking Iron Plate. And **the hold moves at Begin, not at completion**, like
  every other movement verb -- an "in flight" payload would be a third place goods can exist,
  neither in the golem nor on the mast, which is exactly the window the consume-after-give
  ordering was written to keep closed.

  Two of the four remaining `IsPlanned` nodes are cleared; the pinned test now names **two**:
  Floor Expansion and the Slag Heap.

- **§5.3(c)'s Slag Heap is built.** A placeable that voids Slag at **1 Coke per 4**, on an
  integer accumulator with the remainder carried (§1.4's no-floats discipline), so the ratio is
  exact at every scale. Its tile takes two goods and means different things by them: Slag is
  destroyed, Coke is fuel -- one cell, because a second would be a second building, and drawing
  Coke from the stockpile at a distance would make disposal cost no logistics at all.

  **A heap out of Coke refuses Slag, and that refusal is the mechanic**: the backlog builds, the
  smelter stalls on its byproduct, and the player is told by the thing stopping that disposal has
  a running cost they stopped paying. Voiding free when the fuel ran out would delete the §5.3(c)
  decision silently. It keeps accepting **Coke** while refusing Slag, which is how it recovers --
  answering "full" to everything would leave an empty heap unrefuellable by golem.

  The pinned `IsPlanned` test is now down to **one** name: Floor Expansion.

- **§11 item 15's Floor Expansion is built, at runtime.** The workshop's back wall moves north,
  new plank rows are painted onto the Tilemap and the wall run is re-placed -- in a build, not in
  the Editor. §11 flags this as "more work than 'purchasable growth' suggests" and names the
  reason: `SandboxFloorGenerator` is Editor-only. What is shared is the *math* -- the runtime
  service walks the same `FloorLayout` methods and the same `FloorTileVariant` chooser -- and only
  the Tile assets and wall sprites are handed over as serialized references, so a bought row is
  tiled by the identical rule as an authored one.

  **It grows NORTH only, and that is the load-bearing constraint.** Growing symmetrically would
  move the shop front, and the street is defined as the eight rows south of it -- so the road, the
  kerb and all nine market stalls would slide south with every purchase, moving the landmarks the
  player navigates by. North extends the room away from the camera into empty space instead.

  `FloorLayout.HalfExtent` stays a `const`: every method there takes its extents as parameters
  with const defaults, and a default argument must be compile-time constant, so making it a field
  would break every signature in the file. The live extent lives in `FloorBounds` and is passed
  in -- which also means the player's clamp and the build bound read the **same object**, so new
  floor is walkable and buildable the instant it is paid for.

  Rows are capped and get dearer as the room grows: §11 asks for land that is "finite and
  expensive, not continuously paveable", and the cap alone would leave the last row as cheap as
  the first.

  **The chart's `IsPlanned` list is now empty** -- every node on the Artificer's Ledger is a
  shipped feature. The flag and its test stay, so the next designed-but-unbuilt thing is marked
  the same way rather than quietly drawn as though it existed.

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

- ~~**A player-built construction station is a decorative box.**~~ **FIXED**, through a seam
  rather than a second set of holders. `Buildings/IPlacedStationConfigurator` follows
  `Save/IGolemRespawner` and `IBuildingRebuilder` exactly: the bootstrap already knows how to wire
  a station, so it implements the interface and `BuildModeController.RegisterPlacedEndpoints` asks
  it. `SandboxBootstrap.ConfigureStation` is now the **one** definition of a wired station, used by
  the startup sweep and by placement alike.

  Two things fell out of it worth keeping. The station's wiring is **split in half**
  (`ConfigureSceneServices` for scene references, `ConfigureBuildRoster` for the assets), so a
  sweep handing out holders can never blank the roster an authored station already carries. And
  the roster for a placed station is **captured from the scene's own station** rather than
  duplicated onto a serialized field of the bootstrap -- one authored roster, so there is nothing
  to drift. A save's rebuilt station comes back wired too, since it goes through the same
  registration path.
- **The whole arc is unplaytested.** §12 names the boiler fuel ratio as "the single most important
  number to playtest first" — mis-tuned, the game becomes a coal simulator — and flags every golem
  count in §7/§9 as ±25 %. Nothing here has been played, only tested.

- ~~**The Artificer walk cycle is cut but not wired.**~~ **DONE**, and it needed no animation
  infrastructure at all. The 16 frames are imported to `Assets/_Project/Art/artificer_walk_*.png`
  (PPU 64, point, uncompressed, BottomCenter) and `Player/ArtificerWalkAnimator` drives them on the
  Sandbox player. There is still not one `.controller` or `.anim` asset in the project, which was
  the point: an Animator would have been a state machine to describe something that is a pure
  function of where the player is standing.

  **Frame comes from distance travelled, not wall time**, which is the whole reason it was worth
  writing rather than dropping in an Animator. `ArtificerWalkAnimation` is engine-free math —
  `ComputeFrameIndex`/`ComputeFacing`/`ComputeSpriteIndex`/`AdvanceDistance`/`IsWalking`, unit-
  tested without a scene the way `GolemAnimationUtility` is — and the `MonoBehaviour` only measures
  the frame's actual transform delta and assigns a sprite.

  **Facing comes from intent, the frame from what actually happened**, and splitting those two is
  what makes walls behave. `PlayerController.LastMoveInput` is the player's request *before*
  `ClampToFloor` gets a say, so leaning into a bound turns the Artificer to face it and then leaves
  his legs still. Verified in Play mode against the eastern bound: input held at `(1, 0)`, position
  frozen, accumulated distance frozen, sprite parked on the standing frame.

  **Standing is its own frame** (index 0), not "hold the last one drawn", which would freeze him
  mid-stride on every stop.

  What the art turned out to impose, against what was predicted here:
  - **`left` is not a mirror of `right` — confirmed, and it is wired as two independent rows.**
    A pixel diff puts left against a mirrored right at ~38 % of the sprite. `ComputeSpriteIndex`
    can never map the two rows to the same frame and there is a test pinning that; `flipX` is
    never touched.
  - ~~**Frames are in sheet order, unjudged.**~~ **The sheet order is already a correct
    contact/pass cycle** — judged off a contact sheet of the imported frames, and it agrees with
    the pixel diffs: within every row, frames 0 and 2 are the two legs-together pass poses (they
    are the most similar pair) and 1 and 3 are the opposite strides. **No reordering was needed**,
    and it makes frame 0 the right choice for standing.
  - **The source has no vertical bob, and this is the one thing still open.** Confirmed: all 16
    frames share a 3px baseline. It is less bad than feared for three of the rows — `left`,
    `right` and `up` do carry ~2px of head movement between pass and contact — but the `down` row
    is genuinely flat, so walking toward the camera reads weakest. **No procedural bob was added**:
    a `ComputeIdleBobOffset`-style lift layered on top would have been a silent substitute for the
    hand-lifted pixel this actually wants, and the walk reads acceptably without it. It is an art
    task, deliberately left for one.

  Re-running it: **Tools > Golem Factory > Wire Artificer Walk Cycle** rebuilds the frame array on
  the Sandbox player from disk, idempotently — that is the way to apply an art or frame-order
  change, not a fresh manual pass. The frames are also listed in `CharacterArtAuthoring` so
  **Reimport Character Art (BottomCenter)** keeps their pivot with the chassis sprites' rather than
  letting them drift.

### Opened by the §1.1 machine-model pass

- ~~**The Workbench has 5 appendage sockets; the Zeppelin now has 6.**~~ **DONE.** `AppendageSlot5`
  is authored and `appendageSlotZones` is 6, so the Zeppelin's sixth step is reachable. The
  silent truncation is fixed at the same time, and **not by widening the array alone**: overflow is
  now recorded on load (`WorkbenchController.DraftOverflowCount`) and both Engage Gears and Patent
  *refuse* rather than committing a program the player cannot see. Committing the visible five was
  data loss with no warning and no undo; committing four of six would be the same thing with extra
  steps. With the socket authored this should never fire in normal play — it is the guard that
  makes a future 7-slot chassis a refusal instead of a silent discard. Its status line is
  deliberately not time-based (the program is still unrepresentable after six seconds), retiring
  only when retargeted onto a golem that fits.
- ~~**No UI for `Haul` batch quantity.**~~ **DONE.** Each `Haul`/`ExtractFromNode` slot card now
  carries a `[−] ×4 · 10t [+]` stepper, clamped 1…12 (the golem's own per-item-type stock cap, not
  a number picked for the UI). It edits the **draft**, so trying 8 and putting it back costs
  nothing at all, exactly like picking a card up and setting it down.

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
- ~~**World-space HUD collides.**~~ **DONE** -- the layout pass it was waiting for.
  `UI/WorldHudLayout` is the pure solver (bucket by column, order south-first with the owner id
  as tie-break, stack the rest upward); `WorldHudRegistry` collects one frame's anchors and
  `WorldHudSolver` ticks it from `Update`, so every label reads a layout computed after the last
  of them registered and before any of them draws -- an ordering guaranteed by Unity's phases
  rather than by a script execution order somebody has to remember.

  **Stacked, not scattered**: colliding labels climb in a column so each stays horizontally over
  the thing it describes, where a radial nudge would put a badge over a *neighbour* at exactly
  the moment that neighbour also has something to say. The interaction caption sorts below every
  golem id on purpose -- the thing the player is reaching for should not be the thing that moved.
- ~~**Belts are one cell per segment**, no merge or splitter~~ -- **merges and splitters are
  built**, and this entry was recording ONE gap where there were two, only half of them real.

  **Merging already worked.** Two belts pointing at the same cell both link to it and contend for
  its slots through the receiving lane's own spacing rule; nothing had to change, and there are
  tests pinning it now so it stays true. **Fan-out was the real gap**: a belt has one facing, so
  it points at exactly one cell and cannot split by construction. A **splitter** is therefore its
  own placeable -- the cell with no facing of its own, whose outputs are every neighbour facing
  away from it, so the neighbours opt in rather than the splitter choosing a direction it does
  not have.

  `BeltSegment.Next` became a list, with `Next` kept as "the first output" so every existing
  caller reads unchanged, and `ConveyorSystem`'s handoff pass now makes one call for both cases --
  a splitter and a plain belt take the identical path through the two-pass ordering, which is the
  one piece of belt code that must never be duplicated. The branch cursor advances **only on a
  successful handoff**, so a jammed branch is skipped rather than costing the item its turn, and
  the scan order is a fixed compass walk rather than dictionary order, for §1.4's determinism
  reason.

  **Still true, and still deliberate:** a belt hands off only to another belt, so getting goods
  into a buffer is still a golem's job. The progression design leans on that constraint and it
  was not part of this item.
- ~~**`ComputeItemScale` clamps at `maxItemScale = 1.0`**~~ -- **not true any more, and nobody
  fixed it: the projection switch did.** Measured at the real geometry (a placed belt is one cell =
  1.0 world units, `BeltNetwork`'s default 4-tick segment, item sprites 32px at PPU 64 = 0.5
  units) the fit lands at **0.625**, strictly inside both clamps, and cargo draws at exactly the
  authored 1.25x of slot spacing. A test pins that geometry so the claim cannot quietly become
  true again.
- ~~**Belt arrows under-report speed at partial congestion**~~ -- **FIXED**, and it was wrong in
  *both* directions. `speed x (1 - congestion)` measures how full a lane is, not how fast it runs:
  two of four slots queued behind a parked head halved the arrows while the front was keeping up,
  and a lane whose single item could not move at all ran them at **full** speed, because a parked
  head is deliberately not counted as a jam. `BeltFlowUtility.ComputeFlowFactor` replaces it, as
  the **maximum** over the cargo rather than the mean -- the mean freezes the arrows over a parked
  head while items behind it visibly advance, which is a worse lie than the one being fixed.
  Congestion keeps the alarm channel untouched.
- ~~**Belt jam signalling is off on placed belts**~~ -- **FIXED.** A one-cell belt lends its own
  static direction arrow to the cargo visual as a flow lamp
  (`BeltSegmentVisual.ConfigureFlowSignalTarget`), which captures the authored colour as its
  resting state -- so a free-flowing belt looks exactly as authored and only a jammed one changes.
- ~~**Chassis sprites pivot centre, so golems render sunk into the floor.**~~ **FIXED.** All five
  chassis and the three `golem_generic` bodies are BottomCenter now
  (`Scripts/Editor/CharacterArtAuthoring.cs`, re-runnable), so a golem stands on the cell it
  occupies instead of three quarters of a tile south of it. `Main.unity`'s seven hand-placed
  golems each dropped 0.75 to keep their apparent positions, and `GolemStallIndicator.worldOffset`
  went 1.0 → 1.75 because it measures from the transform, which now means the golem's feet.
  `GroundShadow` needed nothing: it reads `sprite.pivot.y` rather than assuming a convention.

  ~~**Still open, and now measurable: the chassis art is not trimmed to its alpha bounds.**~~
  **DONE** -- `Tools/Art/trim_character_alpha.py`, which measured exactly the figures recorded
  here (Aether-Hauler 10px, Presser 6, generics 4, Zeppelin 3) and trimmed all 24 standing sprites,
  the sixteen walk frames included. It is a script rather than an importer change because an
  importer that trimmed would silently disagree with the file on disk about where the sprite ends,
  and it could not go in `generate_placeholder_art.py`, whose chassis output was hand-replaced long
  ago (`--legacy` exists to warn about exactly that).

  > **The shared-minimum rule.** The walk frames are trimmed by the *same* number of rows, taken
  > from the frame with the least padding -- never per frame. Per-frame trimming would manufacture
  > a vertical bob out of art that deliberately has none, which is the procedural substitute the
  > walk-cycle pass rejected. The missing bob is still an art task for a person.

- **Sprite pivots are still inconsistent project-wide — three conventions and one trap.** The
  characters agree with the buildings now, but the environment art solved the same problem a
  *different* way, and an audit of every `.meta` in `Assets/_Project/Art/` finds:

  | Convention | Assets |
  |---|---|
  | **BottomCenter** (`alignment: 7`, `y=0`) | 5 chassis, 3 `golem_generic`, `clock_tower`, `steam_boiler`, `hand_crank_bench` |
  | **Custom** (`alignment: 9`), pivoted on the contact line | head-on walls `y=3/96`, side walls `x=3/40` and `37/40`, corner post `1/96`, props `1/56`, floor edges `y=1` |
  | **Center** (`alignment: 0`) | all 6 items, floor tiles, belts, overlays, `building_block` |
  | **Stale/contradictory** | `player`: `alignment: 7` but `spritePivot {0.5, 0.5}` |

  ~~And **`player` is a trap**~~ -- its stored pivot now reads `{0.5, 0}`, matching the alignment
  it has always honoured. The rule below still holds; the one asset that contradicted it does not.

  This is no longer four *rules* -- it is two rules plus a trap. **BottomCenter is the convention
  for anything that stands on the floor**; **Custom-on-the-contact-line is the convention for
  pieces of the room**, which need a horizontal contact line (the side walls' is vertical) or an
  offset for the shadow the art draws under itself; **Center is correct for what is centred on a
  cell rather than standing on one** (items, tiles, cursor overlays). And **`player` is a trap for
  anyone auditing by eye**: Unity honours *alignment*, not the stored `spritePivot`, so it behaves
  as BottomCenter while its record says centre — read `alignment`, not `spritePivot`.
- **`Main.unity`'s id-routed regressions are rehoused, so retiring it is now a content decision
  rather than a coverage loss.** `IdRoutedDemoRegressionTests` drives the scene's OWN program
  builders (`HardcodedDemoProgram`, the same static its bootstraps call, so the tests cannot
  drift from the demos) with no scene at all, and pins what the scene was the only witness to:
  id routing by bare-string node/belt/buffer ids, the authored `durationTicks` rather than a
  derived one, `Refine` between named buffers, the belt hand-off chain, the self-stalling
  two-step program, the Threshold trigger firing once per crossing, and the machine model NOT
  reaching an id-routed golem.

  > **It also found that one of the seven demos never worked.**
  > `HardcodedDemoProgram.ExtractAndDeposit` -- applied by `GolemDemoBootstrap` to a golem
  > standing in `Main.unity` -- describes "extract from a node, deposit into a buffer", and the
  > id-routed `ExtractFromNode` has never been able to do that: it extracts onto a BELT named by
  > the card's `destinationId`, and that card names none. The step refuses at `CanEnqueue(null)`
  > before it touches the node, every tick, forever, so the M2 demo golem has been visibly doing
  > nothing. **Pinned rather than repaired** -- giving it a belt changes what the scene
  > demonstrates, which is a content call.

  Still true, and still the reason to retire it eventually:

- ~~**`Main.unity` is a diorama, not the game**~~ -- **RETIRED.** The scene and the four
  bootstraps that only fed it (`MainSceneBootstrap`, `GolemDemoBootstrap`, `BeltDemoBootstrap`,
  `TriggerDemoBootstrap`) are deleted; `Sandbox.unity` is the only scene. `AssemblyLineDemoBootstrap`
  stays, because Sandbox uses it, and so does `HardcodedDemoProgram`, which is now the definition
  of the reference programs the regression suite drives. Both suites stayed green with the scene
  gone, which is the evidence the rehousing actually held. **Comments across the codebase still
  say "Main.unity" when explaining the id-routed fork -- read them as history**; the diorama is in
  git if it is ever wanted back. Original entry, for the record:

 — seven pre-wired golems demonstrating M2–M7, with no
  spatial routing (its golems are deliberately never `ConfigureSpatial`'d, which is exactly what
  keeps id-based routing working there). `Sandbox.unity` is the playable loop. The two will keep
  diverging; at some point `Main.unity` should be retired or explicitly reframed as a test bed.

### Presentation polish deferred from the production-quality pass
Each of these was reviewed and judged non-blocking:

- ~~Workbench: no hover/press states on vault cards or chassis buttons~~ -- **DONE, and the
  measurement was the fix.** `Image.color` MULTIPLIES its sprite, so a tint can only darken and
  "highlight" is unreachable from a resting state of white -- which is why Unity's default
  highlighted multiplier composites to the 3.9 % nobody could see. The controls now rest at 0.82
  and hover goes to full, so the lift is real; the cost is a slightly dimmer resting card, and it
  is the same headroom move `BuildGhostVisuals` made with its near-white source sprite. A vault
  card is not a `Button` (it is a drag handle) so it got the four pointer handlers directly.
  Tests assert the SEPARATION between every pair of states, including a test that pins Unity's
  default at ~4 % so nobody reinstates it. Workbench polish still outstanding: lever housing is basic hand-coded pixel art; LiberationSans SDF
  rather than a period display face; procedural grain visibly repeats; cards are text-only with no
  per-action icons; dead space below 5 chassis entries and ~8 vault cards.
- Environment: the floor is monotone at gameplay zoom with no feature larger than one tile; the
  interior is an empty box (no workbenches, shelving, or hearth — the biggest gap against "cozy,
  detailed"). ~~Lighting is even rather than dramatic.~~ **Retuned**: the global light drops
  1.15 → 0.62 and the sconces rise 0.95 → 1.5 with a wider radius, taking a lit spot from 1.8× the
  shadow between lamps to **3.4×**. Cutting the ambient is what does the work -- while a 2D global
  light sits near 1 the sconces can only add to an already-lit room. 0.62 is a floor rather than a
  mood: this is a factory game and the grid must stay readable unlit. Every `BuildGhostVisuals`
  contrast figure survives, because a uniform multiplier scales the ghost and its floor equally
  and those were all stated as ratios. **Unverified by eye** -- the numbers are reasoned, not seen.
- ~~Economy: stock bars are still relative-only ... the rate readout is also still *net* stock
  change.~~ **BOTH DONE.** A capped buffer's bar is a real fill fraction now
  (`Economy/StockBarPolicy`, with a `84/100` label and a warning ramp at five sixths); an uncapped
  one -- the player's deliberately `Unlimited` stockpile -- keeps the honest relative comparison,
  and the two modes stay visually distinct because a half-length bar means different things in
  them. The rate column asks a second question when the level is flat: is anything passing
  *through*? That needed a new signal, because sampling a level can only ever see net change --
  `StorageBuffer` now keeps monotone lifetime deposited/withdrawn counters, `BufferRateTracker`
  fits both slopes, and throughput is `min(in, out)` (the surplus of either side is already
  reported as net movement; counting it twice would read as double the traffic there is).
- ~~Belt art is direction-neutral with a rotated chevron rather than a proper mirrored NE/NW
  isometric pair.~~ **Stale, like the `ComputeItemScale` entry above: the projection switch
  resolved it.** A mirrored pair is what an ISOMETRIC grid needs, because a chevron rotated onto
  a diagonal shears against the diamond it sits on. Top-down puts all four facings on world axes
  -- measured at exactly 0°, 90°, 180°, 270° -- so the four are exact quarter turns of one sprite
  and a mirrored pair would be two copies of the same picture. The art is flat white on a dark
  outline with no baked light, so rotating it contradicts no light direction. A test pins the
  quarter-turn property, because if it ever stops holding (a projection change, eight-way facing)
  the rotated chevron stops working and the mirrored pair becomes real work again.

---

## 3y. Playtest session 1 — findings and dispositions

The first live session, and it earned four findings in Part A alone. Two were real bugs that no
test had reached, one was the script being wrong about the game, and one was a legibility question.

- **Wall clip at the street/workshop seam — FIXED.** Standing on the outer street beside the
  building and walking north pushed the player *through* the flank into the shop.
  `ClampToFloor` was clamping one axis then the other, and **a T cannot be clamped one axis at a
  time**: Y-then-X pulled the player sideways through the wall, and X-then-Y (the first fix) sent
  anyone standing off the north-east corner the length of the road. It now clamps into **each
  rectangle the T is made of and takes the nearer** — which is a wall to a walking player, because
  walking is small steps and the road stays nearer until the room genuinely is, and the honest
  nearest-legal-point answer for anything teleported in from outside. Four tests, including one
  that *walks* rather than teleports, because the teleport version asks a different question.
- **No way to leave build mode — FIXED, and it was three bugs wearing one coat.** `BuildMenuPanel`
  only ever selected a placeable; nothing ever cleared it, so `IsPlacementActive` stayed true for
  the session. Left click went on placing forever — and because **`R` is arbitrated on that flag**,
  R could never again reach the Hand-Crank Bench *or* a golem. The player reported it as "I can
  only do coking" and "how do I move a golem": one cause, three symptoms. There is now a
  `CancelBuild` action (**Escape** and **right-click**), and clicking the held row toggles it off.
- **"Crank progress remains at whatever % it was at" — the SCRIPT was wrong, not the game.**
  Progress is held on release deliberately, so walking away mid-craft and coming back works; the
  inputs are only charged at completion, so letting go costs time and never goods. The script had
  claimed progress "abandons". Corrected there.
- **"What are steps 1-6?" — a legibility finding, logged not fixed.** They are the golem's program,
  run top to bottom once per cycle. The screen numbers them and never says that. Deliberately NOT
  relabelled mid-playtest: a Workbench redesign is not something to do while somebody is using it.
- **"A little dark" — ambient raised 0.62 → 0.72**, keeping the lamps as the light source (a 3.1×
  pool against the shadow, from 1.8× before the pass) while bringing the unlit middle of the room
  up ~16 %. Flagged for retest.

Every one of these has a `RETEST` sub-task under the item that found it in
`testscript/phase-1-playtest.md`.

## 3y2. Playtest session 2 — findings and dispositions

Two findings, both feedback rather than mechanics, and both of the same shape as session 1's "how
do I move a golem": the game *could* already do the thing, and never said so.

- **A finished hand-crank said nothing — FIXED.** Harvesting a node pops "+1 Scrap" over it;
  finishing a craft at the Hand-Crank Bench banked the output in total silence, so the only way to
  learn that a minute of cranking had produced anything was to open Management and compare
  numbers. The bench now records `LastCompletedRecipe` alongside `CompletedCrafts`, and
  `PlayerInteractor` — which already tracks which bench the player is turning — watches that pair
  and spawns the same `FloatingPopup`. Kept out of `Buildings/` deliberately: the bench advances
  on simulation ticks and must stay constructible in an EditMode test with no Canvas, and the
  confirmation belongs to whoever is standing at the handle. A byproduct (only R4's Slag) gets its
  own caption a line lower rather than sharing one.
  - It is a batch **count**, not a bool. At 4x several ticks land in one frame, so a short recipe
    can finish twice between two `Update`s; those collapse into one "+2 Coke".
- **The wording of all three gain popups was unified.** They were written months apart and had
  drifted: the harvest line printed the raw buffer key, so copper ore read "+1 CopperOre" while
  every panel in the game called it Copper Ore, and the boiler's line hard-coded the word "Coke".
  All three now go through `UI/YieldPopupText.Gain`, which names the good via
  `ItemTiers.DisplayName`. A test walks the whole item roster against the TMP atlas rule.
- **"I can't figure out how to pick up and place the golem" — FIXED, and it was discoverability,
  not a missing feature.** `[G]` carry/drop and `[R]` rotate have both existed since the
  repositioning pass. The problem is *where the player is standing when they first want them*: a
  freshly built golem is emitted onto the tile its station faces, so at that spot the **station**
  wins the `[E]` pick, the caption reads "[E] Build Golem", and the golem's own caption — the only
  line in the game that has ever mentioned `[G]` — is not the line being drawn. A player who never
  wanders off the station never learns a golem can be moved at all.
    - The fix is the same one rotation already got, applied to the *prompt* instead of the action:
      the aside is keyed off the nearest **golem**, not off the winner of the combined pick, so
      "[E] Build Golem · [G] carry PlayerGolem-001" appears at the station. `GolemHandlingHint`
      is one shared function precisely because those are the only two places the key is ever
      named and they had to agree.
    - This is the **third** symptom of session 1's root cause to be closed: that one was `R` being
      swallowed by a stuck build mode, this one is `[G]` never being offered where it is needed.

### The golem could never be picked up at all — **FIXED**, and the prompt work above was inert

The player reported, after the prompt fix shipped, that they *still* could not pick up or move a
golem. They were right, and the cause was not discoverability at all.

**`PlayerInteractor.RefreshInteractables()` was called from exactly one non-test place: `OnEnable`.**
Nothing in the game ever called it again. It caches all six interactable kinds with
`FindObjectsByType` — correctly, because `RefreshAffordance` runs every frame and
`FindObjectsByType` must not — but that snapshot was never invalidated. And **`Sandbox.unity`
starts with zero golems**, because every golem in the game is built by the player. So the array
stayed empty for the whole session:

| Standing directly on the golem | Result |
|---|---|
| `[G]` carry | *"No golem in range to pick up."* |
| `[R]` rotate | *"No golem in range to rotate."* |
| `[E]` re-program | prompt reads `[E]  Build Golem` — the golem is not a candidate |

It was never golem-specific. A depot placed from the build menu was in the scene and
`cachedDepots=0`; standing on it the prompt was **empty**, so it could never be labelled. Same for
a boiler (never fuelled), a placed construction station (never built from), a placed bench.
`RefreshInteractables`' own comment already claimed a station built mid-session could "make
itself/new golems interactable" — that was an intention nobody ever wired up.

- **The fix** is `WorldInteractablesChangedEvent` on the existing `EventBus`, published from the
  three chokepoints every runtime-created interactable passes through —
  `GolemConstructionStation.SpawnGolem` (covering both a fresh build and a save restore),
  `BuildModeController.PlaceInternal`/`DemolishBuilding` (covering place, remove and
  `ClearRuntimePlacedBuildings`), and `TryRebuildSavedBuilding`, which instantiates directly.
  `PlayerInteractor` subscribes in `OnEnable` and re-scans wholesale. An event rather than a
  direct reference because the station and the build controller have no business knowing the
  player exists — and `RoutingFocusController` has the same re-scan problem and can now subscribe
  too.
- **The publish is last in `PlaceInternal`, after every endpoint registration**, or a listener
  would find a depot that is interactable and routing-invisible at the same time.
- **HOW THIS HID, and it is the lesson worth keeping.** The live verification of the prompt fix
  called `interactor.RefreshInteractables()` by hand immediately after building the golem — so it
  drove a door the game never opens, and reported success on a feature that did nothing. Verifying
  through the real entry point is not the same as verifying in Play mode. The regression tests
  therefore publish the **event** and never call `RefreshInteractables` themselves.
- **The tests are PlayMode, and that is load-bearing.** Written first as EditMode, they failed
  against a working fix: the subscription lives in `OnEnable`, which does not run outside Play
  mode (CLAUDE.md's `[ExecuteAlways]` gotcha, the same one that forced the Signal-trigger tests
  across). One of the three asserts the *unsubscribe*, since the bus is static and a leaked
  handler would re-scan for a scene that is gone.

### Verified in the live Sandbox, not just in tests

Driven through the MCP-for-Unity bridge in Play mode, because both fixes are about what a player
*sees* and neither could be settled by a passing assertion.

- Standing at the station, prompt = `[E]  Build Golem  -  [G] carry PlayerGolem-001`.
  Standing at the golem = `... faces N · [R] turn · [G] carry`. Carrying it = `... [G] set down`.
- Every crankable recipe produced exactly one popup at the bench, `y+1.50`:
  `+1 Coke`, `+1 Iron Plate`, `+1 Glass`, `+1 Gear`, `+3 Copper Wire`. Two crafts landing between
  one pair of frames produced a single `+2 Coke`, as the batching intends.
- **The byproduct branch is currently unreachable and was kept anyway.** All 19 authored recipes
  were checked: 5 are crankable, and none of those 5 has a byproduct. It is kept because
  `HandCrankBench.TryCompleteCraft` *already* deposits a byproduct, so dropping the caption would
  reintroduce the exact bug this pass fixes the moment a single-input recipe gains one.

### Found on the way: the TMP "Latin-1 only" rule is out of date

Cranking the bench made TMP write three glyphs into
`LiberationSans SDF - Fallback.asset` — `U+2192 →`, `U+2588 █`, `U+2591 ░`. They come from
`HandCrankReadout`: the conversion line (`1 Coal → 1 Coke`) and the progress bar
(`[█████░░░░░] 50%`). A game-view screenshot confirms **all three render correctly** — the atlas
is `m_AtlasPopulationMode: 1` (dynamic), so TMP resolves them from an OS fallback rather than
drawing the missing-glyph box the rule predicts.

Both consequences were then taken:

1. **`CLAUDE.md`'s rule is now an allowlist**, not a blanket ban. Its old claim that `U+2192`
   "renders as a missing-glyph box" is **not true for a dynamic atlas** — it was true of the baked
   Ledger strings, and was never a law of the project. `TechTreeCatalogTests`' `> U+00FF` guard is
   explicitly kept as-is, because catalog strings *are* baked and the allowlist does not reach them.
2. **The churn is fixed by completing the set, not by reverting it.** Reverting the asset would
   only have reset the counter: the atlas is dynamic, so the next session to render any of the four
   would dirty it again. Instead `U+2265` — the one the accidental bake had missed, since reaching
   the Workbench's threshold caption needs an authored Threshold core — was added deliberately via
   `TMP_FontAsset.TryAddCharacters` + `SaveAssets`. All four now live in the committed asset.
   - **Verified by hash**: a full play session that renders the crank readout *and* all four
     glyphs through a live TMP label leaves the file byte-identical. The set is closed, and
     provably so — a grep of `Assets/_Project/Scripts` for anything above `U+00FF` outside
     comments returns exactly four sites, in two files.
   - The aesthetic call went with the blocks: `[█████░░░░░]` and `1 Coal → 1 Coke` read better than
     `[#####-----]` and `->` on a cozy pixel UI. **The counter-argument is on the record**: for an
     eventual standalone build, OS-font fallback is less dependable than in the Editor, which is
     exactly why the glyphs are baked into the asset rather than left to resolve at runtime.
     `HandCrankReadout.ProgressBar`'s doc comment still says `[####------]` and is now wrong about
     its own code; left alone rather than fixed blind, since it is a one-line comment on a
     screen somebody should look at first.

## 3z. WHAT IS ACTUALLY LEFT

Every coded item on this file is built, and no node on the tech tree chart is flagged as planned
any more. What remains splits three ways, and the first way is now the critical path.

### A. Needs a person in front of the running game

`testscript/phase-1-playtest.md` is the script for all of it and has been rewritten against this
build. Nothing below can be produced by a test.

| What | Why it can only be played |
|---|---|
| **The boiler fuel ratio** | §12: "the single most important number to playtest first -- mis-tuned, this becomes a coal simulator." |
| **The manual era** | Budgeted at 12-15 min; measured at ~7.5 min of cranking *before* the market street doubled the walk to a stall (16 cells against 7). |
| **Golem counts per phase** | §7/§9's figures, flagged ±25 % by the design itself. |
| **Whether the Workbench still decides anything** | The design's own deferred judgement. Focus scaling and patent-stamping were the two things layered on top of it, and both are gone -- the `Haul` batch dial is now the only answer in the build. |
| **Whether the market is a good idea** | Truckloads are priced and bursty. Does that create a storage problem worth solving, or a toll booth? |
| **The lighting** | Retuned from an even 1.8x to a pooled 3.4x. The ratio argument is sound; **nobody has looked at it.** |
| **Whether a free golem undo changes how you build** | Golems are now dismantlable with a full refund (chassis + cargo), so a mis-programmed golem costs nothing but time. Does that make you build golems more freely, or is the wrecking bar a tool you never reach for? |

**And every number either pass invented.** They are listed as a table at the end of the playtest
script: truckload prices and sizes, Assembly Line card costs (derived as 4x a recipe's own inputs),
the floor-expansion curve, the Slag Heap ratio, the bay upgrade, the mast and heap prices, the flat
freight launch. All were marked TUNING where they live, and none has been felt.

### B. Content decisions

- ~~**The dead M2 demo.**~~ **TAKEN: it got its belt.** The constraint that made it a judgement
  call was the diorama, and the diorama is retired -- so only the question about the *program* was
  left, and a file whose entire job is being the definition of the reference programs cannot afford
  a reference that does not run. The belt is a **required** argument rather than a defaulted magic
  string, so the fiction cannot be rebuilt by accident. The regression test pins the two-step chain
  end to end now; the invariant the fiction was accidentally documenting (an extract card with no
  belt must refuse *before* it touches the node) is kept as its own test and now checks the seam is
  untouched.
- **Whether `Sandbox.unity` wants a second scene back** -- a small test bed for hand-wired
  experiments, now that the diorama is retired. Nothing needs it; it is a workflow preference.
  **Still open, still nobody's problem.**

### C. Real work, none of it blocking

In rough order of how much a player would notice:

1. ~~**The interior is an empty box**~~ -- **DONE.** A hearth with a live fire, a loaded shelf
   unit, a trestle workbench with a vice and a pegboard tool rack now line the **north** wall, which
   is the run the camera looks straight at (the shop front is open to the south). The other three
   walls keep their clutter, which is what stops the furniture reading as a showroom. Positions are
   authored rather than generated, because clutter should look scattered and furniture should look
   *arranged* -- and an arrangement is exactly what a hash cannot produce.
2. **The floor is monotone** at gameplay zoom -- **RE-DIAGNOSED, NOT A MISSING FEATURE.** Four
   plank variants plus two rare hash-placed accents are already built and were not touched by this
   pass. If it still reads as flat in play, that is a tuning question about `FloorTileVariant`'s
   `GrateRarity`/`PlateRarity` and the plank tones, not an art job. **Needs a person to look at it
   before anyone builds anything.**
3. **Workbench polish**: the lever housing is hand-coded pixel art, LiberationSans SDF stands in for
   a period display face, the procedural grain visibly repeats, cards are text-only with no
   per-action icons, and both lists have dead space below their entries. **The legibility half is
   done** -- §3y's "What are steps 1-6?" is answered by the loop labels (`STEP 2 · loops back to
   1`), which move as the player builds. What is left here is purely art.
4. **Sprite pivots are three conventions project-wide.** Documented and coherent -- BottomCenter for
   things that stand, Custom-on-the-contact-line for pieces of the room, Center for what is centred
   on a cell -- and the one asset that contradicted its own record is fixed. A tidy-up, not a defect.
   The four new furniture pieces were authored 56 tall precisely so they share the crate's contact
   line rather than adding a fifth convention.

### C2. Opened by the cozy automation pass

Nothing here blocks anything. All of it is felt-not-measured.

- **Eight new invented numbers**, all marked TUNING and all listed in the playtest script's table:
  the recycler's four constants and its cost, the `Straining` threshold, and the two badge dwells.
  The recycler's ratio is the one with teeth -- it is deliberately half the Slag Heap's disposal
  rate, and if that trade is wrong the heap either becomes pointless or the recycler does.
- **Does the depot label cycle stay short enough?** It is built from what the stockpile has handled,
  so it grows with the factory. Four to eight entries is the design intent; a late-game player with
  twenty goods in the stockpile presses `[E]` twenty times to get back round. If that bites, the
  answer is a picker panel rather than a longer cycle.
- **Is `Straining` useful or is it noise?** It is the only predictive mood, and the only one whose
  value depends entirely on whether 9-of-12 is far enough ahead of the jam to act on.
- **Eleven rows in the build menu** — ten placeables plus the Demolish row. The menu's height is
  derived from the row count so nothing is cut off, but eleven is worth a look on a real screen.
- **The recycler's art is a placeholder** like every other building's, and it carries more weight
  than most: its silhouette is the only thing saying "this one gives something back, go and collect
  it".

### D. Deliberate cuts, unchanged

`Refine` stays id-routed; Pixel Perfect Camera stays off (it fights the free-zoom camera); no player
collision; a one-card `ExtractFromNode` program jams by design. See §4.

**The no-refund cut is gone from this list on purpose.** It read "no refund on demolishing a
building" from the day the list was written, and that was **reversed rather than deferred**:
removal now refunds the full cost, for buildings and for golems, and CLAUDE.md carries it as a
settled design call with the three rules that keep it honest.

---

## 3a. What the backlog pass deliberately did not take

Not oversights. Each of these is either a call that is not a coder's to make, or work that needs a
person in front of the running game.

| Item | Why it was left |
|---|---|
| **The street's shape** (§2) | A map-shape decision with real costs either way -- a second stall row eats the approach clearance §3.2's two-extractor cap depends on, and extending east/west stops the world being a rectangle the side walls bound in one run. Owner's call. |
| **Pricing the market** (§2) | `docs/game-design.md`'s "full truckload shipments" implies a cost and a delivery that no design doc specifies. Inventing an economy for it would be design work wearing an implementation hat. |
| **Whether the Workbench needs a larger job** (§2) | Explicitly a judgement to be made after playing a factory, and the mitigation it was waiting on (per-slot `Haul` quantities) is already built. |
| **Playtesting the arc** (§3) | The boiler fuel ratio, §9's 12-15 minute manual era, every golem count at ±25 %. Nothing here can be measured from a test suite. |
| **§8's Assembly-Line gating** (§4) | ~~The standing "every card available from the start" deferral~~ — **it has since landed and is ON in `Sandbox.unity`** (`gateWorkbenchRoster: 1`). The vault shows claimed cards only, with the movement verbs granted at t=0 so a fresh save can still program a golem. Row kept as the record of why the pass left it. |
| **Belt merges and splitters** (§3) | A deliberate design constraint the progression leans on, not a defect. |
| **Retiring `Main.unity`** (§3) | ~~It is load-bearing as the id-routed regression bed~~ — **done: the scene is gone and those tests were rehoused** into `Assets/Tests/EditMode/Golems/IdRoutedDemoRegressionTests.cs`, which drives the same `HardcodedDemoProgram` builders the scene's bootstraps used. Row kept as the record of what had to exist first. |
| **Floor Expansion, the Freight Link and Mast, the Slag Heap** | ~~Still `IsPlanned` on the tech tree~~ — **all three are now built and light up when you build them**, and the pinned-planned test asserts an **empty** set. Left in this table as the record of why they were deferred at the time. |
| **Presentation polish** (§3) | Workbench hover states, the empty interior, dramatic lighting, the mirrored belt-art pair. Reviewed and judged non-blocking, and unchanged by this pass. |

One pre-existing oddity noticed on the way through and left alone: every Unity run logs a YAML
parse warning for `Assets/Tests/EditMode/Simulation.meta` (committed in M2, GUID intact, folder
imports fine). It is noise rather than breakage, and nothing this pass touched.

---

## 4. Deliberate scope cuts still standing

- `Refine` stays id-routed rather than spatial, on purpose: recipes are typed and the *untyped*
  `IItemEndpoint.TryTake` is not, so a spatial take could grab the wrong input and silently
  transmute it. Item 1.1 **resolved this from the other end** rather than by making `Refine`
  spatial: `Assemble` reads a typed dictionary the golem itself owns, so it needs no spatial take
  at all. `IItemEndpoint` did grow a typed take — for `Haul` — but the exemption stands as written.
- Pixel Perfect Camera installed but not enabled — conflicts with the free-zoom `CameraRigController`.
- No player collision; the player walks through buildings and golems.
- ~~No refund on removing a placed building.~~ **REVERSED, not deferred.** Removal refunds the
  **full** cost -- the game is cozy, so placement and reorganising must not be punitive, and a
  full refund makes "move a building" free with the tools that already exist rather than needing
  a pick-up-and-carry mode. It extends to golems (chassis **and** cargo) via the wrecking bar.
  Three rules hold it honest: only what the player paid for (`IsRuntimePlaced`), only a player's
  own click (a load sweeps *without* refunding, or save/load/save is an infinite duplicator), and
  never destroy what it cannot hand back (`RefundWouldFit` is asked before anything is torn
  down). **Do not reintroduce a salvage fraction as "balance".**
- ~~`AssemblyBayStructure` ... is implemented and tested but still not wired into the Sandbox
  loop.~~ **WIRED**, with §8's numbers: bays start at **10** slots ("above the natural Phase-2
  count of ~8") and upgrade **+6 for 40 Scrap + 20 Iron Plate**. That price is Presser-tier goods
  only, deliberately -- "so the cap can never gate on something the cap itself prevents you from
  making" -- and that is a test now rather than a sentence. `GolemConstructionStation` checks the
  cap **before** the cost so a refusal never touches the stockpile, reports `LastRefusalReason` so
  the panel stops printing a shortfall of nothing, and the load path force-assigns past the cap for
  the same reason a rebuilt building is not re-charged. Occupancy is derived (destroyed golems are
  pruned on read), because §10's escape hatch from an over-built factory is deleting golems and a
  slot that never came back would break it.
- ~~The Assembly Line still does not gate the Workbench roster — every card is available from the
  start.~~ **RESOLVED** — this was the M9-era deferral and §8's gating is what finally closed it:
  the vault shows claimed cards only, behind `SandboxBootstrap.gateWorkbenchRoster`, which is ON
  in `Sandbox.unity` and field-default false everywhere else. The one thing that stays is the
  **opening hand**: the movement verbs are granted at t=0, because a gated vault with nothing
  claimed cannot program a golem at all and §9 Phase 1 asks for one within minutes.
