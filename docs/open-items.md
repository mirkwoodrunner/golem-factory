# Golem Factory — Open Items

Consolidated backlog as of the progression-design pass, on branch
`polish/production-quality-pass` (17 commits, **not merged to `main`**).

Everything here is known and deliberate — none of it is a surprise waiting to be discovered.

> **Where the line is.** Everything through §1.3 (multi-input `Assemble`) is **built, tested
> and reviewed**. Everything from §1.4 on is **spec only**. Steam power has been approved for
> inclusion but not written. The next pass starts at §1.4.

Tests stand at **736/736** (635 EditMode + 101 PlayMode), up from 590 before the progression pass
began. Console clean.

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

**§1.1, §1.2 and §1.3 are now built and green. §1.4–§1.6 are still spec.** Implementation order matters,
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

### 1.4 Steam power
Boiler burns Coke **proportional to powered golem count** (1 Coke per powered golem per 10s),
powering golems by orthogonal pipe adjacency, with `NoSteam` as a new stall precondition.

A flat per-boiler burn was rejected in review: it makes 7 of every 8 golems free, so the marginal
cost a player actually optimises against is zero. Note steam pipes are **undirected** and need a
simple flood fill — `BeltPlacementRules.ShouldLink` is directional and rejects head-on pairs, so it
cannot be reused wholesale.

### 1.5 Asset authoring
24 items, 25 recipes, revised chassis costs paid in manufactured components rather than raw currency.

### 1.6 Clock Tower
Four rate-scaled stages. Progress scales with delivered rate so surplus capacity finishes faster;
the 60-unit clamp is retained as the anti-hoard cap.

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

- **Steam power is in.** Confirmed by the project owner. It appears in neither `game-design.md` nor
  `digital-design.md` — it was invented during the progression design because "nothing in the
  economy is contended" has no fix that doesn't add a running cost. The critic ruled it justified
  and correctly shaped: local (orthogonal pipe adjacency), rigid (no falloff, no pathfinding, no
  adaptation), deterministic, and `NoSteam` is the existing stall rule with a new precondition
  rather than a departure from it. It ends up ~41% of the factory, so **the boiler fuel ratio is the
  single most important number to playtest first** — mis-tuned, this becomes a coal simulator.
  Note the design rejected a flat per-boiler burn: it makes 7 of every 8 golems free, so the
  marginal cost a player actually optimises against is zero. Consumption must scale per powered
  golem.

### Still open

- **The opening changes substantially.** Sandbox currently starts with three infinite nodes and a
  construction station. The design starts the player with a hand-crank bench and ~10–15 minutes of
  manual labour before their first automated line. That is the requested arc, but it is a real shift,
  and it pushes `Main.unity` further from being representative.
- **The Workbench loses its decisions.** With every program reduced to `N × Haul + Assemble + Push`,
  a recipe plus a chassis fully determines the program — the signature drag-and-drop UI has nothing
  left to decide, and the patent system's main use becomes skipping boilerplate the game forces on
  you. The design's mitigation is player-set `Haul` batch quantities (throughput traded against
  buffer pressure). Worth confirming that is enough to justify keeping the Workbench as the
  signature screen.
- **The Overclocker's identity.** Its flat-speed adjacency aura was cut in review — a non-local,
  adaptive effect contradicts the game's rigid local determinism. The design replaces it with a
  `Repeat(n)` appendage competing for the same slot as a third ingredient. Unbuilt, and it is the one
  chassis whose role is still only "more slots".

---

## 3. Known gaps carried forward

### Opened by the §1.1 machine-model pass

- **The Workbench has 5 appendage sockets; the Zeppelin now has 6.** `WorkbenchCanvas.prefab`
  contains `AppendageSlot0`–`AppendageSlot4` and `WorkbenchController._draftAppendages` is sized
  from `appendageSlotZones.Length`, so a 6-slot chassis behaves like a 5-slot one in the UI — and
  worse, `WorkbenchController.cs:319` silently **truncates** a longer program on load, so opening a
  6-appendage golem and hitting Engage Gears would commit a 5-appendage one. Not reachable today
  (no 4-input recipe exists until §1.5), but slot count is *the* tier gate in this design, so this
  is a **prerequisite for §1.5**, and it is prefab work that cannot be done from a text diff.
- **No UI for `Haul` batch quantity.** It is stored per slot and saved, but nothing exposes it, so
  §2's "Consequence 4" — the Workbench's one remaining real decision — is not yet playable. This is
  what the §2 open decision below is actually waiting on.
- **A one-card `ExtractFromNode` program now jams.** Extract fills internal stock instead of
  reaching the tile in front, so a spatially placed golem with no `Push` fills to 12 and stalls
  `InputFull`. Correct by design (a Scavenger is 2 slots: Extract + Push) and the stall names the
  blocked good, but it is a live behaviour change for anyone with a saved Sandbox factory.

### Carried forward from before

- **Save/load cannot respawn player-built golems.** `SaveLoadService.RestoreState` persists each
  golem's program, cell and facing, but can only restore onto a `GolemEntity` already present in the
  scene. A factory the player constructed does not survive a session. **This is the most significant
  functional gap in the game today.**
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
- **Sprite pivots are inconsistent project-wide.** Chassis sprites pivot centre, so golems render
  sunk about half a sprite-height into the floor; `GroundShadow` compensates for the shadow only.
  Fixing it shifts every hand-placed golem in `Main.unity`.
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
