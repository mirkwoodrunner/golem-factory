# Golem Factory — Open Items

Consolidated backlog as of the progression-design pass, on branch
`polish/production-quality-pass` (17 commits, **not merged to `main`**).

Everything here is known and deliberate — none of it is a surprise waiting to be discovered.
Tests stand at **590/590** (489 EditMode + 101 PlayMode), console clean, both scenes verified in
Play mode.

---

## 1. The progression design is written but entirely unimplemented

`docs/progression-design.md` passed a three-round review against a 9-point rubric, with an
independent critic between rounds. **No game code has been written against it.** That was
deliberate: round 1 failed on five structural counts, all of which would otherwise have surfaced
only after ~25 recipes had been authored and wired.

Implementation order matters, because each item depends on the one above it.

### 1.1 Golem internal typed stock + typed/quantified `Haul`/`Push`
Golems gain internal input and output stock. `Haul` pulls one typed quantity in, `Assemble`
consumes from inside, `Push` empties output. This makes `maxAppendageSlots` the *physical* tier
gate (`N` inputs = `N + 2` slots), and lets `BeginRefine`'s spatial-routing exemption stay exactly
as written rather than needing the typed-endpoint work the code currently, deliberately refuses.

> **Critical, do not miss.** A program containing **no `Assemble` step must treat input stock as
> output stock.** Without that special case, every logistics golem in the game hauls into input,
> pushes an empty output, fills to the per-type cap and stalls forever — roughly 25 of ~96 golems
> at stage 4, including the Scavenger, the game's first and most common unit.

### 1.2 Per-*item-type* buffer capacity
`StorageBuffer.Deposit` always succeeds and `CanGive()` is unconditionally `true`, so there is no
backpressure anywhere and "destination full" stalls are currently **impossible** — half the drama
of the rigidity rule is unavailable, and every ratio problem in the progression design is toothless.

Capacity must be **per item type**, not per buffer. A whole-buffer cap combined with untyped takes
deadlocks permanently: a full plate buffer stalls smelting, which stops slag, which stops glass,
and no rigid golem can ever drain the wrong type out. Ships **with** 1.1, never before it.

### 1.3 Multi-input `Assemble`
`AppendageActionDefinition` carries a single `inputItemType`/`outputItemType`, so no recipe can
combine goods. Needs 1–4 typed inputs with quantities, output quantity > 1, and one optional
byproduct, withdrawn atomically with a shortfall-naming stall.

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

## 2. Decisions needing a human call before implementation

- **Steam power is invented.** It appears in neither `game-design.md` nor `digital-design.md`. The
  critic ruled it justified and correctly shaped — local, rigid, deterministic, and `NoSteam` is the
  existing stall rule with a new precondition rather than a departure from it — but it ends up ~41%
  of the factory. If a power layer isn't wanted, that call is far cheaper now than after it is woven
  through every phase.
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
- Economy: stock bars are relative-only because no capacity concept exists (1.2 above fixes this);
  the rate readout is *net* stock change, not gross throughput, so 60/min in and 60/min out reads
  as Steady.
- Belt art is direction-neutral with a rotated chevron rather than a proper mirrored NE/NW
  isometric pair.

---

## 4. Deliberate scope cuts still standing

- `Refine`/`Assemble` stays id-routed rather than spatial, on purpose: recipes are typed and
  `IItemEndpoint` is not, so a spatial take could grab the wrong input and silently transmute it.
  Item 1.1 removes any need to revisit this.
- Pixel Perfect Camera installed but not enabled — conflicts with the free-zoom `CameraRigController`.
- No player collision; the player walks through buildings and golems.
- No refund on removing a placed building.
- `AssemblyBayStructure`'s capacity/tier upgrade is implemented and tested but still not wired into
  the Sandbox loop. The progression design gives it a job (the golem cap); until then it is inert.
- The Assembly Line still does not gate the Workbench roster — every card is available from the
  start. This is the M9-era deferral, and the progression design is what finally resolves it.
