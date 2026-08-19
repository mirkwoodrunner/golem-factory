# Golem Factory — Playtest Script

**What this covers:** the whole game as it now stands — the opening arc, the steam economy, and
everything two build passes have added since this script was last written (the truckload market,
Creative Mode, §8's card gating, the Freight Link, the Slag Heap, Floor Expansion, belt splitters,
the bay cap, and a lighting pass). **None of it has been played.** Tests prove the mechanics match
the spec; they say nothing about whether any of it is any good, and every number marked *TUNING*
below was invented by a developer and has never been felt.

**Time:** ~20 min for the smoke test (Part A), ~60–90 min for the full run (Parts B–H).

**Build under test:** branch `claude/artificer-walk-animation`, **1245/1245 tests green**
(1096 EditMode + 149 PlayMode).

---

## The questions I actually need answered

If you only have half an hour, answer 1–3. They are the ones no test can reach.

1. **Is the boiler fuel ratio right?** §12 calls it *"the single most important number to playtest
   first — mis-tuned, this becomes a coal simulator."* 6 Coke/min per powered golem. **Part E.**
2. **Is the manual era tolerable or miserable?** The design budgets 12–15 min of hand-work before
   your first machine — and the market street has since **doubled the walk** (16 cells to a stall
   against the old 7). **Part B.**
3. **Does the Workbench still feel like a decision?** **Part C.**
4. **Is the market a good idea?** Truckloads are priced and arrive in bursts. Does buying raw goods
   feel like an economy or like a toll booth? **Part D.**
5. **Is the room too dark now?** The lighting was retuned from an even 1.8× to a pooled 3.4× and
   **nobody has looked at it**. **Part A2.**

---

## Setup

1. Open the project in Unity Hub (**Add**, not New) — Unity **6000.5.4f1**.
2. Open `Assets/_Project/Scenes/Sandbox.unity`. **It is now the only scene** — `Main.unity` has been
   retired (it was a diorama with no player; its coverage lives in the test suite now).
3. Press **Play**.
4. Keep the **Console** visible (`Window > General > Console`). It should stay clean. Any error or
   warning during play is a finding — note the timestamp and what you had just done.

---

## Controls

| Key | Does |
|---|---|
| **WASD** | Move |
| **E** | Interact — *tap* to harvest a stall / **order a truckload from an empty stall** / open a construction station / refuel a boiler / program a golem. **Hold** at the Hand-Crank Bench to crank. |
| **R** | Context-sensitive: turns the build ghost if you hold a placeable → cycles the recipe at the bench → otherwise rotates the golem you stand next to. |
| **G** | Pick up / put down a golem |
| **Tab** | Management menu (Inventory · Assembly Line · Patents · Save/Load · **Ledger**) |
| **Left click** | Place or remove a building |
| **Mouse wheel / drag** | Zoom / pan |

Speed controls are bottom-centre: `PAUSE 0.5x 1x 2x 4x`. Cranking, golems, boilers, **market
deliveries** and the Clock Tower all run on the simulation clock. Harvesting does not — it is one
unit per key press regardless of speed.

---

## Part A — Smoke test (~20 min)

### A1. The HUD

- [ ] **Top-left:** fuel gauge, `0 Coke · 0/min · idle`.
- [ ] **Top-right:** `Clock Tower dormant` — *dormant*, not a stage or a starvation warning.
- [ ] **Top-centre:** `All golems running.`
- [ ] **Bottom-left:** a Build menu with **nine** rows, all costs legible, none cut off:

| Row | Cost |
|---|---|
| Depot | 15 Scrap |
| GolemConstructionStation | 25 Scrap + 5 Brass |
| Belt | 1 Scrap |
| Boiler | 30 Scrap + 10 Iron Plate |
| SteamPipe | 1 Iron Plate |
| ClockTower | Free |
| HandCrankBench | Free |
| **FreightMast** | 20 Brass + 10 Casing |
| **SlagHeap** | 20 Scrap + 10 Iron Plate *(TUNING)* |

### A2. The light — **question 5**

The global light was cut 1.15 → 0.62 and the wall sconces raised 0.95 → 1.5, taking a lit spot from
1.8× the shadow between lamps to **3.4×**. The reasoning is sound; the result is unseen.

- [ ] Stand in the middle of the room, away from the walls. **Can you still read the floor grid, a
      belt's direction and the build ghost?** That is the floor this change must not go below.
- [ ] Walk along the north wall. Do the sconces read as *the source of the light* now, with pools
      and darkness between, or is it just dimmer everywhere?
- [ ] **Your call:** too dark, about right, or not dramatic enough?

### A3. The world

The workshop is 25×25 with an open south front. **The market street runs outside it** — nine stalls
in a row at `y = -16`, about 16 cells south of spawn.

- [ ] Walk out of the shop onto the street. The road is **wider than the building** (37 cells) and
      should be paved edge to edge, with a kerb along its far side and short wall stubs
      ("shoulders") closing the gap beside the shop front. **No cobbles running off into
      background** — that is the specific thing to look for.
- [ ] Nine stalls: Scrap ×2, Coal ×2, Copper ×2, Zinc ×2, Aether ×1. Each pair is a separate node,
      so two extractors can work each.
- [ ] All five goods are **distinguishable from each other** by silhouette (placeholder art — I am
      asking whether you can tell them apart, not whether they are pretty).
- [ ] Try to walk off the edge of the road, and off the sides past the building. You should be
      stopped everywhere, with no way into the empty space beside the shop front.
- [ ] Try to place a Depot out past the kerb. It should refuse with `off the ground`.

### A4. The Workbench

Walk to the construction station, press **E**, then close. Programming needs a golem — come back
after Part B if you have none.

- [ ] **TRIGGER** then **STEP 1**–**STEP 6**, six numbered sockets, no duplicates.
- [ ] **The vault is now GATED** — it shows only cards you have *claimed*, not the whole catalogue.
      From a fresh start you should hold exactly the movement verbs (Extract, Haul, Push). If the
      vault is empty, that is a blocker: say so immediately.
- [ ] **Hover a chassis button and a vault card.** Both should visibly brighten, and darken on
      press. This was measured at a 4% shift (invisible) and is now ~22%. Does it read?
- [ ] The fuel gauge, tower panel, bench readout **and the alerts strip** all vanish while the
      Workbench is open, and return when you close it.

---

## Part B — The manual era (~15 min at 1x) — **question 2**

**Time it.** The design budgets 12–15 minutes. It was measured at ~7.5 min of cranking *before* the
market moved the goods 16 cells away.

- [ ] Harvest **100 Scrap** from a Scrap stall (one press each). Note how long, and how it feels —
      100 discrete presses plus the walk is a candidate finding on its own.
- [ ] Crank **R2 Scrap Reclamation** at the bench for 40 Iron Plate (~9.6 s each at 1x), then **R8
      Gear Cutting** for 10 Gears.
- [ ] Let go of **E** mid-craft: progress abandons, **no goods lost**. Press **R** mid-craft: same.
- [ ] Build the **Brass Presser** (60 Scrap + 20 Iron Plate + 10 Gear).

> **Record total elapsed time.** Note separately how much of it was *walking*.

---

## Part C — Automate it (~15 min) — **question 3**

- [ ] Place a **Depot**, position a golem so the stall is the tile **behind** it and the depot the
      tile **in front** (`Extract` pulls from behind, `Push` delivers in front).
- [ ] Program `Extract → Assemble R2 → Push`, pull **ENGAGE GEARS**.
- [ ] **Focus now scales with program length** — `8 + 6 × steps`, so that 3-step program costs 26,
      not the old flat 10. Does that read as a cost worth managing?
- [ ] Patent the program, then stamp it onto a second golem: a patented commit is a **flat 10**.
      Does the saving land — does stamping feel obviously right by the third identical golem?
- [ ] Turn a golem to face nothing: it should **stall with a badge naming the problem**, then resume
      on its own when turned back.
- [ ] **Stand two stalled golems one cell apart.** Their badges should stack vertically, not
      overprint. Same for the interaction caption.

---

## Part D — The market — **question 4**

Raw goods are now **bought**, not dug. A stall holds stock; when it runs out you order a truckload,
pay for it, and it arrives as one burst after a delay.

- [ ] Harvest a stall dry, then press **E** on it. You should get an order prompt, be charged, and
      see the cart arrive later as a lump.
- [ ] **Scrap stalls are free** (§10 forbids a soft-lock; a priced market with an empty stockpile is
      one). Everything else costs Scrap — Coal 10, Copper/Zinc 20, Aether 40 per load. **All TUNING.**
- [ ] Order from a stall that already has a cart coming: it should refuse with "already on the road",
      not queue a second.
- [ ] **The judgement:** does buying in bursts create the buffer-and-accumulator problem it is meant
      to (you must store a lump and smooth it), or is it just a wait?

> **Creative Mode** turns all of this off — stalls become infinite and free, exactly as they were
> before. It is on `ManagerHolders → GameMode`. Worth one pass with it ON to compare.

---

## Part E — Steam (~15 min) — **question 1**

**`requireSteamPower` is ON now.** Golems stall `NoSteam` without a boiler in reach, so this is the
real arc, not arithmetic.

- [ ] From a cold start: hand-harvest Scrap and Coal → crank R2 for Iron Plate and R1 for Coke →
      build a **Boiler** (30 Scrap + 10 Iron Plate) → **press E on it to hand-load 20 Coke** → build
      golems within its reach.
- [ ] A golem out of reach should stall **`NoSteam` naming its own tile**. Lay a **Steam Pipe** to
      it and it should resume.
- [ ] Watch the gauge: `N Coke · N/min · M:SS left`, with an alert at 25% of the most it has held.
- [ ] **Your call, and the big one:** at 6 Coke/min per golem, is upkeep a meaningful running cost
      or a coal simulator? One coker sustains ~5.9 golems on paper.

---

## Part F — The new buildings (~15 min)

- [ ] **Slag Heap.** R4 Iron Smelting makes 1 Slag per 2 Plate whether you want it or not. Build a
      heap, push Slag into it, and **push Coke in as fuel** — it voids 4 Slag per Coke. Let it run
      out of Coke: it should **refuse Slag** (and your smelter should back up and stall), while
      still accepting Coke. Is that refusal legible, or does it just look broken?
- [ ] **Freight Mast + Zeppelin.** Place a mast, build a Zeppelin, give it `Haul → FreightLaunch`.
      It binds to the nearest mast **at placement and keeps it**, and launches its whole hold onto
      the mast's tile from anywhere on the map at a flat 24 ticks. Does a remote outpost feel worth
      building?
- [ ] **Belt splitter.** Place one, run a belt into it and two belts out. It should alternate
      between the branches, and skip a branch that is backed up rather than waiting on it.
- [ ] **Bay cap.** Build golems until you hit **10**. The station should refuse with "all 10 assembly
      bays are full" and **charge nothing**. Then **Tab → Assembly Line** and buy the upgrade row
      (`Bays 10/10 · +6 slots · 40 Scrap + 20 Iron Plate`). Does hitting the cap read as a decision
      or as an arbitrary wall?
- [ ] **Floor Expansion**, same tab: `Workshop 25x25 · +2 rows north`. Buying it should paint new
      plank rows at the **back** of the room and move the back wall, with no seam against the old
      floor and the market unmoved. Prices rise per purchase and the land runs out. *TUNING.*

---

## Part G — §8's card gating (~10 min)

- [ ] **Tab → Assembly Line.** Cards cost **item bundles** now and get cheaper the longer they sit.
      Claim one and check it appears in the Workbench vault.
- [ ] A recipe card should not appear at all until its prerequisite is met (you have made its first
      input). Does "the card I need is not offered yet" read as progression or as being blocked?
- [ ] Recipe and chassis cards **leave the pool** once claimed; the movement verbs keep cycling.
- [ ] **Tab → Ledger.** The tech tree chart should light nodes up as you produce, build and claim.
      Nothing on it should read as "planned" any more — every node is a shipped feature.

---

## Part H — Save/load (~5 min)

- [ ] **Tab → Save/Load**, save, quit to the Editor, reload, load.
- [ ] Golems you built come back, standing where they stood, with their programs and batch sizes.
- [ ] Buildings come back — belts, depots, boilers, pipes, the tower, masts, heaps.
- [ ] Clock Tower stage progress comes back. Its *rate windows* deliberately do not — the readout
      should rebuild within a minute rather than resuming at the old rate.

---

## Known gaps — please *don't* report these

- **The interior is an empty box** — no workbenches, shelving or hearth. The biggest known gap
  against "cozy, detailed", and the largest thing still outstanding.
- **The floor is monotone** at gameplay zoom, with no feature larger than one tile.
- **Belts can't feed a buffer.** A belt hands off only to another belt; getting goods into a depot
  needs a golem doing `Push`. Deliberate.
- **Workbench polish**: hand-coded lever housing, LiberationSans rather than a period face,
  procedural grain that repeats, text-only cards with no icons, dead space in both lists.
- **No player collision** — you walk through everything.
- **No refund** on removing a placed building.
- **A one-card `ExtractFromNode` program jams** by design (a Scavenger needs Extract + Push).
- **Placeholder art throughout** — the buildings all have their own sprites now, but they are
  generated placeholders, not final art.

---

## Findings

| # | Part | What happened | What you expected | Severity |
|---|---|---|---|---|
| 1 | | | | |
| 2 | | | | |
| 3 | | | | |

Severity: **blocker** (can't proceed) / **bad** (playable but wrong) / **note** (feel, polish).

### The five questions

**1. Fuel ratio —** is 6 Coke/min per golem right? Too harsh / about right / too cheap?

> 

**2. Manual era —** how long did Part B take, how much was walking, and was it tolerable?

> 

**3. Workbench —** a decision, or paperwork? Did Focus scaling and patent-stamping change that?

> 

**4. The market —** an economy, or a toll booth? Did bursts create a storage problem worth solving?

> 

**5. Lighting —** too dark, about right, or not dramatic enough? Was anything unreadable?

> 

### Every number below is TUNING and has never been felt

Mark any that felt wrong, with a direction.

| Number | Value | Felt |
|---|---|---|
| Coke per powered golem | 6/min | |
| Truckload prices | Scrap free · Coal 10 · Copper/Zinc 20 · Aether 40 | |
| Truckload size / delay | 30–40 units · 40–120 ticks | |
| Slag Heap ratio | 1 Coke per 4 Slag | |
| Assembly Line card cost | 4× the recipe's own inputs | |
| Bay cap / upgrade | 10 slots · +6 for 40 Scrap + 20 Plate | |
| Floor expansion | +2 rows, rising cost, capped at +12 | |
| Freight launch | flat 24 ticks | |
| Focus | `8 + 6 × steps`, patent stamp flat 10 | |

### Anything else

> 
