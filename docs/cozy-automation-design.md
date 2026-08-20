# Cozy Automation Pass — Technical Design

**Status:** Phase 0 design, approved for implementation.
**Baseline at start of pass:** 1249/1249 (1100 EditMode + 149 PlayMode), console clean, branch
`claude/artificer-walk-animation`.

This is the architecture spec for the four Phase-1 features of the cozy-automation pass. It is a
*design* document in the same sense as `docs/progression-design.md`: what gets built, why it is
shaped that way, and what each system does when things go wrong. Where it invents a number, that
number is marked **TUNING** and belongs in the playtest script's tuning table, not in the code's
self-image as a settled fact.

Read `docs/open-items.md` §3z first. Two of the four features below answer findings that are
already written down there:

| Feature | What it answers |
|---|---|
| Workbench loop legibility | §3y's *"What are steps 1–6?"* — logged, deliberately not fixed mid-playtest |
| Golem moods | §3z C's *"the biggest remaining gap"* is the room, but a golem that reads as *asleep* rather than *broken* is the same class of problem |
| Smart depot filtering | New. The depot is the only building the player places that has no settings at all |
| Scrap Recycler | §3z's Slag economy has exactly two outlets and no way to dispose of anything else |

---

## 0. Invariants this pass must not break

Every one of these is load-bearing somewhere and has a test that will catch a violation.

1. **Deterministic integer arithmetic.** No floats in simulation state, no probability. §1.4's
   boiler discipline and `SlagHeap`'s carried remainder are the model: accumulate integers, charge
   on the crossing. Floats are allowed only in *presentation* (rates per minute, bob offsets).
2. **The `IsSpatiallyPlaced` fork stays.** A golem with no `SpatialEndpointRegistryHolder` routes
   by bare-string id and keeps pre-machine-model semantics byte for byte. Nothing in this pass may
   change what an id-routed golem does.
3. **Append-only enums.** `AppendageActionType` and `StallReason` are serialized by integer index
   into authored `.asset` files and into `GolemStalledEvent`. New members go at the end.
4. **No item loss, ever.** `CanGive` before `TryTake`; consume-after-give in `PushStockInto`. A new
   endpoint must never accept a unit it then fails to store.
5. **A refused type is skipped, not the whole push abandoned.** §10's deadlock fix. Any new
   endpoint that can refuse one type while accepting another must answer the *untyped* `CanGive()`
   truthfully, or `PushStockInto`'s early-out will abandon goods it could have delivered.
6. **Opt-in by null.** Every cross-cutting system in this project (economy registries, spatial
   routing, steam, the extractor cap) is opt-in: a component never handed the holder is outside
   the mechanic entirely. That is what has kept every addition from being a flag day. New features
   follow it.
7. **Pure function + thin applier.** Anything with arithmetic or a decision in it goes in an
   engine-free static that a test can call without a scene; a `MonoBehaviour` applies the result.
8. **ASCII and Latin-1 in TMP text only.** The default `LiberationSans SDF` atlas has no arrows and
   no `U+26A0`. `·` (U+00B7) is safe and already used. Arrows are not.

---

## 1. Smart Depot Filtering

### The problem

A `PlaceableDepot` publishes a `StorageBufferEndpoint` for `FactoryStockpile` on its tile. Every
depot in the game publishes *the same buffer*, so a depot has no identity and no settings. Two
consequences:

- **Pushing** into a depot is all-or-nothing per type only by accident of capacity. There is no way
  to say "this crate is where Iron Plate goes."
- **Pulling** from a depot is worse. `StorageBufferEndpoint.ResolveTakeableType()` returns
  `PreferredItemType` if set and otherwise *the first type the buffer happens to enumerate*. That
  field exists and **nothing sets it**. So a golem with `Haul` behind a depot pulls an essentially
  arbitrary good, and a player cannot build two lines off one stockpile.

### The design: a crate has a label

One concept, doing double duty. A depot carries an optional **filter item type**:

- **Empty (the default) means "anything"** — byte-for-byte today's behaviour, so every depot
  already authored into `Sandbox.unity` and every existing test is untouched. This is invariant 6.
- **Set means "this crate is for X"**: it accepts only X on a push, and it dispenses only X on a
  haul.

The pull half is the valuable one and it is nearly free — it is the `PreferredItemType` field the
endpoint already has, finally wired to something. The push half is what makes it a *sorter*.

### Class structure

```
World/FilteredBufferEndpoint.cs   (new, GolemFactory.World)
    sealed class FilteredBufferEndpoint : IItemEndpoint, IFilteredEndpoint
        wraps a StorageBuffer + a filter type

World/IFilteredEndpoint.cs        (new)
    interface IFilteredEndpoint { string AcceptedItemType { get; } }

Economy/DepotFilterOptions.cs     (new, engine-free static)
    BuildCycle(knownTypes, currentFilter) -> IReadOnlyList<string>
    Next(cycle, currentFilter) -> string
    Describe(filter) -> "any goods" | "Iron Plate"

Buildings/PlaceableDepot.cs       (modified)
    [SerializeField] string filterItemType
    FilterItemType { get; }
    SetFilter(string) / CycleFilter()
```

**Why a wrapper endpoint rather than a flag on `StorageBufferEndpoint`.** `StorageBufferEndpoint`
is used by depots, freight masts and the bootstrap's authored buffers. Adding a filter field to it
would put a branch on every one of those hot paths for a feature only depots have, and the class's
own doc comment is careful about the difference between its typed and untyped `CanGive`. A wrapper
that delegates everything except the two typed questions keeps that reasoning in one place and
makes the filtered case its own testable object.

**Why `IFilteredEndpoint` rather than widening `IItemEndpoint`.** `IItemEndpoint`'s comment records
that it was kept deliberately narrow and widened exactly once, for a reason written down in two
places. A filter is not something every endpoint has. `GolemEntity.EmptyReasonFor` already
establishes the idiom for asking a *specific* endpoint kind a question it alone can answer
(`endpoint is ResourceNodeEndpoint`), and it is used only on the failure path, never per unit.

### Behaviour table

| Question | Filter empty | Filter = `X` |
|---|---|---|
| `CanGive()` (untyped) | buffer's answer | buffer's answer for `X` only |
| `CanGive(t)` | buffer's answer | `t == X` **and** buffer has room for `X` |
| `TryGive(item)` | buffer deposits | deposits only if `item.ItemType == X` |
| `PeekAvailableType()` | first held type | `X` if held, else `null` |
| `TryTake(t, n, out)` | buffer withdraws | only if `t == X` |
| `TryTake(out item)` (untyped) | first held type | `X` only |

The untyped `CanGive()` row is the one that needs care. A filtered depot whose `X` slot is full can
accept *nothing at all*, which is genuinely different from an unfiltered buffer (which can always
accept *something*, per `StorageBufferEndpoint.CanGive`'s comment). Answering `false` there is
correct and is what lets `PushStockInto`'s early-out skip a full filtered crate cheaply, exactly as
it does for a full belt. It does **not** reinstate the §10 deadlock, because the deadlock is about
one type blocking a *different* type — and a filtered crate has only one type by construction.

### Failure states

| Situation | What happens |
|---|---|
| Golem pushes a mixed hold at a filtered depot | The matching type goes in; the rest stays in the hold. This is `PushStockInto`'s existing per-type skip and needs no change. |
| Golem pushes a hold with **nothing** the filter accepts | New `StallReason.FilterMismatch`, `ResourceId` = the first type in the golem's hold. Badge: *"nothing here takes Slag"*. |
| Filtered depot's slot is full | `StallReason.BeltFull` naming the depot, as today. "Full" and "wrong crate" have different fixes, so they get different reasons. |
| Golem hauls from a filtered depot holding none of `X` | `StallReason.MissingItem` naming `X` — already the right answer via `PeekAvailableType() == null`. |
| Player sets a filter on a depot that already holds other goods | Nothing is destroyed. The other goods stay in `FactoryStockpile` (which is shared) and simply cannot be taken *through this tile*. No migration, no loss. |
| Save/load | `filterItemType` is a `[SerializeField]` on the prefab instance, and a rebuilt building must carry it. See §1.4. |

**`StallReason.FilterMismatch` is appended** (invariant 3) after `NodeCrowded`.

### How the player sets it

**`[E]` on a depot cycles its filter.** `E` is already the context verb — *harvest a stall / order a
truckload / open a station / refuel a boiler / program a golem* — and "set what this crate is for"
belongs on that list. It is deliberately **not** `R`: `R` is arbitrated placeable → bench → golem,
and a golem almost always stands beside the depot it pushes into, so a depot claiming `R` would
make that golem unrotatable. `[E]` goes through `InteractionTargeting.SelectNearest`, which picks
the genuinely nearest of all kinds, so standing at the crate picks the crate.

This adds `InteractionKind.Sort = 5`, **appended** so the existing tie-break order is unchanged, and
a `depots` list to `SelectNearest`'s optional trailing parameters — the same shape `boilers` was
added in.

**The cycle list is short and dynamic.** Cycling 24 item types one `E` press at a time would be
miserable. `DepotFilterOptions.BuildCycle` returns:

1. `null` — *any goods* (always first, so one more press always gets you back to the default),
2. every item type the stockpile buffer currently knows about, in canonical tier order,
3. the depot's current filter, if it is not already in the list.

Rule 3 is what stops a filter silently vanishing when the last unit of its type is consumed. In
practice this is 4–8 entries a few minutes into a game and it grows with the player's factory,
which is the correct scale: you can only label a crate for something you have actually handled.

### Save/load

`SaveData` records a placed building by prefab key, cell and facing. A depot's filter is a fourth
fact about it. Following the established shape, `SaveData.BuildingRecord` gains an optional
`filterItemType` string, written on save and applied by `BuildModeBuildingRebuilder` **before**
`RegisterPlacedEndpoints` runs, so the rebuilt endpoint is published already filtered rather than
being republished a frame later.

---

## 2. Golem Moods

### The problem

A golem has exactly two visible states: white and bobbing, or red and shaking. Everything that is
not a stall looks identical — a golem asleep waiting on an Interval trigger, a golem three units
from backing up, and a golem running perfectly all read the same. The playtest's Part E is entirely
about steam brownouts, and a brownout currently reads as *"lots of red badges"* rather than as one
thing with one fix.

### The design: six moods, three volumes

A **mood** is a pure function of state the golem already exposes. It is not new simulation state,
it is a *classification* of existing state — which is why it can be computed on the presentation
side, at frame rate, without touching the tick loop or determinism.

```
Golems/GolemMood.cs           (new enum, GolemFactory.Golems)
Golems/GolemMoodRules.cs      (new, engine-free static)
    Classify(state, stallReason, hasRunnableProgram, fullestPushStockUnits) -> GolemMood
    ShouldShowBadge(mood, heldSeconds) -> bool
    DwellSeconds(mood) -> float
Golems/GolemAnimationUtility.cs (extended)
    BobFor(mood, baseAmplitude, baseFrequency) -> (amplitude, frequency)
Golems/GolemMoodPalette.cs    (new; Color constants + badge captions)
```

| Mood | When | Volume | Badge | Tint | Motion |
|---|---|---|---|---|---|
| `Working` | `Running` | silent | none | white | brisk bob (today's) |
| `Sleeping` | `Idle` for ≥ 1.5 s **TUNING** | quiet | dim slate, `zzz` | 0.88 grey | slow breathing bob |
| `Straining` | `Running`, push stock ≥ 9 of 12 for some type **TUNING** | advisory | amber, `hold nearly full` | white | brisk bob |
| `Starved` | `Stalled` with `NoSteam` | loud | steel blue, `no steam` | cold blue | held still |
| `Stalled` | `Stalled`, any other reason | loud | red, full caption (today's) | red | held still + entry shake |
| `Unprogrammed` | no chassis, or no steps | quiet | violet, `no program` | white | slow breathing bob |

**Why `Starved` is split out of `Stalled`.** It is the one stall whose fix is a *building* rather
than a rotation or a wait, and it is the one that hits the whole factory at once. A field of blue
badges reads as "the boiler died"; a field of red ones reads as "everything is broken." §3.1's own
framing is that the tile is the actionable fact, and this is the visual version of that sentence.

**Why `Straining` exists at all.** It is the early warning before a stall, and it is the one mood
that is genuinely predictive: a golem at 9/12 output with nobody collecting will be red in under a
minute. The task asks for "full-stock icons"; this is what makes them worth drawing.

**Silence is a state.** `Working` deliberately draws no badge. A factory of 40 golems each wearing
a "working" icon is a factory you cannot read. The hierarchy is: nothing = fine, amber = soon,
red/blue = stopped.

### Deterministic evaluation, and why polling is right here

Moods are **polled**, not event-driven. `GolemStalled`/`GolemResumed` cover exactly one of the six
transitions; `Idle → Running`, output filling, and a program being erased all happen with no event
at all. The existing code already carries the scar of this — `GolemStallIndicator.OnEnable`
re-derives from live state because *"enabling after a golem has already stalled is exactly the case
the event stream cannot cover"*.

So: classify every `LateUpdate` from `golem.Program.State`, `golem.StallReason` and
`golem.Inventory`. The classification is pure and allocation-free. **Only re-render when the mood
changes** — the same "re-render from data, but only when the data moved" discipline
`WorkbenchController.RebuildUI` and `BeltSegmentVisual` follow.

The stall *event* is still used for one thing: the entry shake. A shake is a transition, and a
poller cannot see a transition it did not sample.

### Dwell, and why it is in the rules not the view

`Sleeping`, `Straining` and `Unprogrammed` require the mood to have been held for
`DwellSeconds(mood)` before the badge appears. `Stalled` and `Starved` appear immediately.

Without dwell, an `AlwaysOn` golem — which is `Idle` for a single tick at the end of every cycle —
would strobe a `zzz` badge at cycle rate. With it, only a golem genuinely waiting on an Interval,
Threshold or Signal trigger reads as asleep, which is exactly when "asleep" is the useful word.

`ShouldShowBadge(mood, heldSeconds)` is a pure function so the dwell table is a test, not a
comment. The view owns only the stopwatch.

### Class changes

`GolemStallIndicator` is **widened, not renamed.** It is referenced by class name from
`GolemPrefab.prefab`'s serialized data; renaming it orphans that reference in a way that compiles
fine and fails silently in the scene. It keeps its name and grows a comment saying why.

`GolemVisual` swaps its fixed bob for `BobFor(mood, ...)` and its binary tint for
`GolemMoodPalette.Tint(mood)`.

### Failure states

| Situation | What happens |
|---|---|
| Golem has no `GolemEntity` reference | Classifier is never called; badge stays hidden. Unchanged from today. |
| Golem destroyed while badge visible | The badge is a child of the golem; it goes with it. `WorldHudRegistry` is keyed by golem id and re-resolved each frame, so a freed slot is reused. |
| Two golems one cell apart | Already solved — `WorldHudRegistry.Resolve` de-overlaps. Moods do not change the anchor, so the fix keeps holding. |
| A mood with no dwell entry | `DwellSeconds` returns 0 (show immediately). Fail-loud-but-harmless: a new mood shows up rather than silently never appearing. |

---

## 3. Workbench Loop & UI Polish

### The problem, verbatim from the playtest

> **"What are steps 1-6?" — a legibility finding, logged not fixed.** They are the golem's program,
> run top to bottom once per cycle. The screen numbers them and never says that.

The screen shows `TRIGGER`, `STEP 1` … `STEP 6` and no other words. Six numbered sockets do not by
themselves say *sequence*, and nothing at all says *loop*.

### The design: say it on the labels

No redesign, no new panels, no scene authoring. The captions the sockets already have get a second
clause, driven from the live draft.

```
TRIGGER · when to start
STEP 1  · then
STEP 2  · then
STEP 3  · loops back to 1
STEP 4  · unused
STEP 5  · unused
STEP 6  · (greyed by chassis capacity, as today)
```

That is the whole feature. `STEP 3 · loops back to 1` is the sentence the finding asked for, it
appears exactly where the player is looking, and it moves as they build the program — a card
dropped into step 4 moves the loop marker down, which *demonstrates* the cycle rather than
describing it.

```
UI/WorkbenchLoopLabels.cs   (new, engine-free static)
    Caption(rowIndex) -> "TRIGGER" | "STEP n"
    Hint(rowIndex, assignedSteps, capacity) -> "when to start" | "then" | "loops back to 1" | "unused" | ""
    Compose(rowIndex, assignedSteps, capacity) -> the full caption line
    Headline(assignedSteps) -> the summary sentence
UI/WorkbenchController.cs   (modified — writes captions in RebuildUI)
```

**Runtime, not an Editor authoring pass.** `ProgressionSceneAuthoring.RespaceSlotStack` currently
authors these captions into `WorkbenchCanvas.prefab`, and its own comment records why it derives
them from the index rather than trusting the row (`AppendageSlot5` was a clone of slot 4 and showed
`STEP 5` twice). But a *hint* depends on the draft program, which only exists at runtime. Writing
the whole caption in `RebuildUI` means one owner, no prefab round-trip, no risk of a scene override
silently winning, and an EditMode test can assert the exact string.

**Single line, `·` as the separator.** Latin-1, already used in the tech tree's phase titles, safe
in the TMP atlas. No arrow glyphs (invariant 8). A second line would risk overflowing a caption
`RespaceSlotStack` deliberately sized for one.

### Failure states

| Situation | What happens |
|---|---|
| Empty program (0 steps) | Every step reads `unused`; the headline reads *"drop cards to build a cycle"*. No loop marker — there is nothing to loop. |
| One step | `STEP 1 · loops back to 1`. Correct and slightly funny, which is the right amount of funny. |
| Program longer than the chassis capacity | Capacity greying is unchanged; a step past capacity gets no hint at all, because it is not part of the cycle. |
| A gap (step 2 empty, step 3 filled) | The loop marker follows the **last filled** step, and the gap reads `unused`. The Workbench already compacts on commit; the label describes the sockets as drawn. |

### Also in scope

- **Build menu unhighlight on Escape / right-click** — already fixed in commit `4be784b`
  ("unlight the build row"). Verified, nothing to do.
- Slot styling, action icons and lever art are §3z C item 3 and are **art**, deferred with the rest
  of the presentation polish. The loop labels are the part that answers a finding.

---

## 4. Crafting Ledger & Resource Recycler

### 4a. The Ledger

The Artificer's Ledger is a *chart*: it lights nodes up as you produce, build and claim. What it
does not do is tell you anything about the recipe a node names. A node's `Detail` is a hand-written
transcription (`"1-input recipes by hand, at 25% speed"`), which `TechTreeCatalogTests` pins against
the assets for *existence* but not for *content* — so a recipe's real ratio lives only in the
`.asset` file and in §5.2.

**The design: click a node, read the recipe.** A detail pane pinned to the chart's viewport shows,
for the selected node:

- name, kind, and state (Locked / Available / Researched / Planned),
- for a **recipe** node, resolved from the actual `RecipeDefinition` asset:
  - the full ratio — `2 Scrap + 1 Coke -> 2 Iron Plate + 1 Slag`,
  - the byproduct ratio in lowest terms — `1 Slag per 2 Iron Plate`,
  - the cycle time and theoretical rate — `24 ticks · 5.0 Iron Plate/min at 1x`,
  - the **live** rate from `BufferThroughputMonitor`, when the stockpile has a series for the
    output type — `now: 3.2/min`.

```
Progression/RecipeLedger.cs   (new, engine-free static)
    FormatRatio(inputs, outputType, outputQty, byproductType, byproductQty) -> string
    FormatByproductRatio(...) -> string          (gcd-reduced)
    RatePerMinute(outputQty, durationTicks, ticksPerSecond) -> float
    FormatRate(...) -> string
Economy/ItemTiers.cs          (new, engine-free static)
    TierOf(itemType) -> int      (0-5, -1 unknown)
    DisplayName(itemType) -> string   ("IronPlate" -> "Iron Plate")
UI/TechTreePanel.cs           (modified — node click + detail pane)
```

`ItemTiers` is worth its own file because two features need it: the Ledger wants a good's depth,
and the Recycler (§4b) prices a good *by* its depth. It transcribes the grouping already written in
`ItemType.cs`'s comments, and a test pins that every `ItemType` constant has a tier — so a
25th good cannot be added without deciding where it sits.

**"Active production speeds" comes from `BufferThroughputMonitor`, not a new tracker.** It already
samples the stockpile registry and answers `TryGetRatePerMinute(bufferId, itemType, out rate)`. The
Ledger asks it; nothing new is measured.

**Live rate is optional and clearly separated from the theoretical one.** A node with no monitor
wired, or an item with too few samples, simply omits the `now:` line rather than printing a
confident zero. A theoretical rate is a property of the recipe; a live rate is a property of the
factory, and conflating them would make an unbuilt line look broken.

#### Failure states

| Situation | What happens |
|---|---|
| Node names a recipe asset that does not exist | Detail pane shows name/kind/state only. `TechTreeCatalogTests` already fails the build in this case; the pane must not also throw. |
| Recipe is malformed (`IsWellFormed` false) | Pane prints the authoring problem string. That is what `IsWellFormed(out problem)` is for and nothing currently surfaces it. |
| `durationTicks` is 0 or negative | Rate is reported as `—`, never `Infinity`. |
| No monitor wired (tests, a bare scene) | The `now:` line is omitted. |
| Chart rebuilt while a node is selected | Selection is kept by node **id**, not by view index, and re-applied after `RebuildChart` — view arrays are recreated wholesale. |

### 4b. The Scrap Recycler

#### The problem

The Slag economy has two outlets — the Glass line (R3: 1 Slag → 1 Glass) and the Slag Heap (void, 4
Slag per Coke). Everything else in the game has **none**. A mis-ordered truckload, a decommissioned
line's leftover Casings, a depot of Glass nobody wants: there is no way to get rid of any of it.
§10's escape hatch from an over-built factory is *deleting golems*; there is no equivalent for
goods.

#### The design

A **Scrap Recycler**: throw anything in, get Scrap out, burn Coke doing it. It is
`SlagHeap`'s pattern with the sign flipped — the heap destroys and charges, the recycler charges
*and returns something*.

```
Buildings/ScrapRecycler.cs          (new, plain C#, no engine references)
Buildings/ScrapRecyclerEndpoint.cs  (new, IItemEndpoint)
Buildings/PlaceableScrapRecycler.cs (new, thin wrapper, mirrors PlaceableSlagHeap)
Economy/ItemTiers.cs                (shared with §4a)
```

**Value by tier.** One unit of an item is worth `RecycleValue(tier)` *scrap-points*:

| Tier | 0 raw | 1 basic | 2 metals | 3 components | 4 mechanisms | 5 megaproject |
|---|---|---|---|---|---|---|
| Points | 1 | 2 | 3 | 5 | 8 | 13 |

Every `PointsPerScrap = 4` points yields **1 Scrap** and burns **1 Coke**. All **TUNING**.

Integer accumulator, remainder carried, charged on the crossing — `SlagHeap`'s discipline exactly
(invariant 1), so two identical factories recover identical amounts for identical fuel.

**Coke is fuel, not feedstock.** Pushing Coke into a recycler refuels it, as with a Slag Heap and a
Boiler. It is an explicit named special case in `ScrapRecyclerEndpoint.TryGive`, not a tier-2
lookup, for the same reason `SlagHeapEndpoint` spells it out: one cell, two meanings, and the
per-type `CanGive` is what makes a mixed hold deliver its Slag and keep the rest.

**Output is hauled out, not teleported.** The recycler holds its Scrap (capped at
`OutputCapacity = 24` **TUNING**, twice a golem's per-type cap so one `Haul(12)` does not empty it)
and a golem must `Haul` it away. That makes the recycler a node in the logistics graph rather than
a magic stockpile pipe, and it gives it real backpressure: nobody collecting means it stops
accepting, which is a *visible* consequence.

#### Why this does not delete the Slag Heap

Slag is tier 1, so 2 Slag = 4 points = 1 Scrap for 1 Coke. Against the heap's 4 Slag per Coke:

| | Disposal per Coke | Returns |
|---|---|---|
| Slag Heap | 4 Slag | nothing |
| Scrap Recycler | 2 Slag | 1 Scrap |

Exactly half the throughput, plus a good. That is a real choice rather than a dominant strategy,
and §5.3(c)'s decision — *disposal competes for the scarcest intermediate* — is untouched: the
recycler competes for the same Coke, harder.

#### Why this is not a perpetual-motion machine

Every loop through it is strictly lossy in both Scrap and Coke. Checked against the two recipes
that could plausibly close a loop:

- **R2** (1 Scrap → 1 Iron Plate): Plate is tier 1 = 2 points, so 2 Plate recycle to 1 Scrap. Spend
  2 Scrap, recover 1, burn 1 Coke.
- **R4** (2 Scrap + 1 Coke → 2 Iron Plate + 1 Slag): recycling the whole output recovers ~1.5 Scrap
  from 2 Scrap spent, while burning ~1.5 Coke on top of the 1 the recipe already ate.

And the free good cannot be farmed: Scrap is tier 0 = 1 point, so 4 Scrap in yields 1 Scrap out and
costs a Coke. Feeding the recycler its own output is the worst move in the game, which is the
correct shape for a disposal machine.

#### Failure states

| Situation | What happens |
|---|---|
| No Coke, accumulator below the crossing | Accepts (the unit rides the remainder, exactly as the heap's first three Slag do). |
| No Coke, accumulator at the crossing | **Refuses** that type. The line backs up and the player is told by the thing stopping. Coke is still accepted, which is the recovery. |
| Output at capacity | Refuses everything except Coke, same as above. Untyped `CanGive()` stays **true** while Coke is acceptable — refusing outright would abandon the push that would fix it, which is the bug `SlagHeapEndpoint.CanGive()`'s comment already records. |
| Golem pushes a hold the recycler cannot pay for | `PushStockInto` pushes what it can and leaves the rest; if nothing moves, `StallReason.BeltFull` naming the recycler. "Out of Coke" reads as "full", which is honest — it has no room *for that good right now*. |
| Unknown item type (a good with no tier) | `TierOf` returns -1 and the recycler **refuses** it. Silently valuing an unknown good at some default is how a future item becomes an exploit. |
| Recycler demolished with Scrap inside | Lost, like a demolished depot's contents. §4 already stands on "no refund on removing a placed building". |

#### Cost and unlock

Placement cost **20 Scrap + 10 Iron Plate** (**TUNING**, deliberately the same Presser-tier bundle
as the Slag Heap, and for the same structural reason §8 prices the bay upgrade that way: the
recycler unblocks jams, so it must never be gated behind a jam).

It appears on the tech tree as a Building node in Phase IV beside the Slag Heap, signalled by
`BuildingScrapRecycler` — a readout, not a gate, consistent with §1.7.

---

## 5. Implementation order and test plan

Each row is one commit: design → code + assets → both suites → commit.

| # | Task | New tests |
|---|---|---|
| 1 | Smart depot filtering | `DepotFilterOptionsTests`, `FilteredBufferEndpointTests`, `DepotFilterPushRoutingTests` (golem-level), save round-trip |
| 2 | Golem moods | `GolemMoodRulesTests` (classification × dwell), `GolemAnimationUtilityTests` additions |
| 3 | Workbench loop labels | `WorkbenchLoopLabelsTests`, a PlayMode assertion that the captions reach the sockets |
| 4 | Scrap Recycler | `ItemTiersTests`, `ScrapRecyclerTests` (ratio, remainder, refusal), `ScrapRecyclerEndpointTests`, a golem push/haul round-trip |
| 5 | Ledger detail pane | `RecipeLedgerTests` (formatting, gcd reduction, rate), panel binding test |

**Both suites run in batch mode after every task** — `-runTests -batchmode -testPlatform
EditMode|PlayMode`, Editor closed, no `-quit`. Exit 0 or the task is not done.

## 6. Numbers this pass invents

Every one of these belongs in `testscript/phase-1-playtest.md`'s tuning table. None has been felt.

| Number | Value | Where |
|---|---|---|
| Recycler points per Scrap | 4 | §4b |
| Recycler tier values | 1 / 2 / 3 / 5 / 8 / 13 | §4b |
| Recycler Coke per Scrap | 1 | §4b |
| Recycler output capacity | 24 | §4b |
| Recycler cost | 20 Scrap + 10 Iron Plate | §4b |
| `Straining` threshold | 9 of 12 units | §2 |
| `Sleeping` dwell | 1.5 s | §2 |
| `Straining` / `Unprogrammed` dwell | 2.0 s | §2 |
