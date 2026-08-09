# Golem Factory — Phase 1 Playtest Script

**What this covers:** the whole opening loop, from an empty stockpile to a golem automating a
production line, plus the steam economy. Nothing in this arc has ever been played — it has only been
tested. Tests prove the mechanics match the spec; they say nothing about whether it is any good.

**Time:** ~15 min for the smoke test (Part A), ~30–45 min for the full run (Parts B–D).

**Build under test:** branch `editor-passes/reach-the-progression-content`, 911/911 tests green.

---

## The three questions I actually need answered

Everything else is a bonus. If you only have twenty minutes, answer these.

1. **Is the boiler fuel ratio right?** §12 calls it *"the single most important number to playtest
   first — mis-tuned, this becomes a coal simulator."* 1 Coke per powered golem per 10 s = 6
   Coke/min each. Part D.
2. **Is the manual era tolerable or miserable?** The design budgets 12–15 minutes of hand-work
   before your first machine. Measured at ~7.5 min of cranking plus harvesting. Part B.
3. **Does the Workbench still feel like a decision?** Every program is now `Haul → Assemble → Push`.
   Backlog §2 flags that the signature screen may have nothing left to decide. Part C.

---

## Setup

1. Open the project in Unity Hub (**Add**, not New) — Unity **6000.5.4f1**.
2. Open `Assets/_Project/Scenes/Sandbox.unity`. **Not `Main.unity`** — that's a diorama of
   hand-wired demos with no player.
3. Press **Play**.
4. Keep the **Console** visible (`Window > General > Console`). It should stay clean. Any error or
   warning during play is a finding — note the timestamp and what you'd just done.

---

## Controls

| Key | Does |
|---|---|
| **WASD** | Move |
| **E** | Interact — *tap* to harvest a node / open a construction station / program a golem. **Hold** at the Hand-Crank Bench to crank. |
| **R** | Context-sensitive: turns the build ghost if you're holding a placeable → cycles the recipe if you're at the bench → otherwise rotates the golem you're standing next to. |
| **G** | Pick up / put down a golem |
| **Tab** | Management menu |
| **Left click** | Place or remove a building (when a placeable is selected in the Build menu) |
| **Mouse wheel / drag** | Zoom / pan |

**Speed controls are bottom-centre: `PAUSE 0.5x 1x 2x 4x`.** Cranking, golems and boilers all run on
the simulation clock, so **4x makes the grind 4x faster**. Use it to compress Part B — but do Part B
once at **1x** first, because pacing is one of the things being judged.

Harvesting is *not* on the clock — it's one unit per key press regardless of speed.

---

## Part A — Smoke test (~15 min)

Just looking. Tick each box or write what you saw instead.

### A1. The HUD

- [ ] **Top-left:** a fuel gauge reading `0 Coke · 0/min · idle`.
- [ ] **Top-right:** a panel reading `Clock Tower dormant`. It should say *dormant* — if it names a
      stage or shows a "starved" warning before you've built a tower, that's a regression.
- [ ] **Top-centre:** `All golems running.`
- [ ] **Bottom-centre:** *nothing* — the bench readout only appears when you're at a bench.
- [ ] **Bottom-left:** a Build menu with **seven** rows, all costs legible and none cut off:

| Row | Cost |
|---|---|
| Depot | 15 Scrap |
| GolemConstructionStation | 25 Scrap + 5 Brass |
| Belt | 1 Scrap |
| Boiler | 30 Scrap + 10 Iron Plate |
| SteamPipe | 1 Iron Plate |
| ClockTower | Free |
| HandCrankBench | Free |

### A2. The world

Walk a lap. There are five resource nodes on a ring around spawn, plus a construction station and a
hand-crank bench.

- [ ] All five nodes are **visually distinguishable from each other** — rust offcut (Scrap), black
      lumps (Coal), veined boulder (Copper Ore), pale crystals (Zinc Ore), teal shard (Aether).
      *These are placeholder sprites; I'm asking whether you can tell them apart, not whether they're pretty.*
- [ ] Walking near a node shows a prompt like `Move closer to harvest Scrap · unlimited`.
- [ ] The Hand-Crank Bench is a brown box near spawn.

### A3. The Workbench

Walk to the construction station and press **E**, then close the panel. Now walk to any golem and
press **E** — or if you have no golem yet, skip to Part B and come back.

- [ ] Screen shows **TRIGGER** then **STEP 1** through **STEP 6** — six numbered appendage sockets,
      no duplicates.
- [ ] Pick **Zeppelin Freight Loader** in the chassis rack. All six sockets should light up and the
      tape at the bottom should read `SLOTS 0/6`.
- [ ] The Card Vault scrolls and contains ~25 cards: two logic cores, four verbs (Extract, Haul,
      **Push Output**, Load Into Scrap Buffer), then **Assemble Coking** through **Assemble Wire
      Drawing**.
- [ ] **The fuel gauge, tower panel and bench readout all vanish while the Workbench is open** and
      come back when you close it. If any floats over the Workbench, that's a regression.

> **Judgement call I want your eye on:** the socket rows were re-spaced from 6 rows to 7 and each is
> now ~20% shorter. Does a card still read at that height? Does the 6th socket look like a peer of
> the others or like an afterthought?

---

## Part B — The manual era (~10 min at 1x)

This is the part that was impossible until now. **Time it.**

### B1. Harvest

Stand next to the **Scrap** node and tap **E** repeatedly. One press = one Scrap.

You need **100 Scrap** total: 60 for the Presser itself, 40 to reclaim into Iron Plate.

- [ ] Note how long 100 presses takes, and how it feels. **This is a candidate finding on its own** —
      100 discrete key presses is a lot, and the design says "hand-gather" without saying how.
- [ ] Open **Tab → Inventory** and confirm 100 Scrap.

### B2. Crank Iron Plate

Walk to the Hand-Crank Bench. **A readout appears bottom-centre**, just above the speed controls —
it only shows while you're standing at a bench:

```
HAND-CRANK BENCH   R2 · Scrap Reclamation
1 Scrap → 1 Iron Plate
[███░░░░░░░] 30%   7s left
hold [E] to crank   ·   [R] changes recipe
```

Press **R** until it reads **R2 · Scrap Reclamation**. Then **hold E**.

- [ ] The readout appears when you walk up and disappears when you walk away.
- [ ] The bar fills while you hold E and the countdown ticks down.
- [ ] If you can't afford the recipe, the last line turns orange and reads
      `need N more <item>` instead of the hint — **before** you spend time cranking, not after.

- One Iron Plate takes **96 ticks ≈ 9.6 s** at 1x. That's 25% of machine speed, by design.
- You need **40** of them ≈ **6.4 minutes** at 1x.

- [ ] Crank a few at 1x and note how it feels. Then switch to **4x** for the rest — cranking should
      speed up 4x.
- [ ] Confirm you end with 40 Iron Plate and 60 Scrap.
- [ ] **Let go of E mid-craft.** Progress should abandon, and you should lose **no** goods.
- [ ] Press **R** mid-craft. Same: progress resets, nothing consumed.

### B3. Crank Gears

Press **R** until the readout says **R8 · Gear Cutting**. Hold **E**.

- One Gear = 2 Iron Plate, **64 ticks ≈ 6.4 s**. You need **10** ≈ 1 minute.

- [ ] Confirm you end with **60 Scrap, 20 Iron Plate, 10 Gear** — exactly the Brass Presser's cost.

### B4. Build the Presser

Walk to the **GolemConstructionStation**, press **E**, choose **Brass Presser**.

- [ ] It's affordable, and building it zeroes your Scrap/Plate/Gear.
- [ ] A golem appears and the Workbench opens on it.

> **Record your total elapsed time for Part B.** The design budgets 12–15 min. Anything wildly off
> that is a finding.

---

## Part C — Automate it (~10 min)

Now make the Presser do what you just did by hand.

### C1. Place a Depot

You'll need Scrap again — harvest ~15 more.

Select **Depot (15 Scrap)** in the Build menu, then click a tile. A golem `Push`es onto the tile
**in front** of it, so the depot has to be where the golem is facing.

- [ ] The cost is deducted and a popup shows it.
- [ ] Try placing with insufficient Scrap — you should get a refusal popup naming the shortfall, not
      a silent no-op.

### C2. Position the golem

Use **G** to pick the golem up and put it down. Use **R** to turn it.

**The geometry matters:** `Extract` pulls from the tile **behind** the golem; `Push` delivers to the
tile **in front**. So you want:

```
   [Scrap node]  →  [golem facing away from node]  →  [Depot]
```

- [ ] Standing next to the golem, **R** rotates it and a popup names the new facing.

### C3. Program it

Press **E** on the golem to open the Workbench. Build this program:

| Socket | Card |
|---|---|
| TRIGGER | Always On Core |
| STEP 1 | Extract Scrap |
| STEP 2 | Assemble Scrap Reclamation |
| STEP 3 | Push Output |

Then pull **ENGAGE GEARS** (costs 10 Focus).

- [ ] Dragging cards works; sockets highlight green/red while dragging.
- [ ] After engaging, the golem starts cycling and Iron Plate accumulates (check **Tab →
      Inventory**). Its Scrap comes from the **node**, which is infinite — it doesn't draw down your
      stockpile.
- [ ] It should be **4x faster** than hand-cranking — 24 ticks vs 96.

> **Measured on this build:** this exact setup produced **+17 Iron Plate per minute** with no stall.
> If yours stalls or produces nothing, the geometry is the first thing to check — the node must be
> the tile *behind* and the depot the tile *in front*, and "in front" means the direction the golem
> faces. Note the stall badge text if it appears; it names the blocked good.

> **This is question 3.** Building that program involved no real choices — the recipe and chassis
> determined it. Does the Workbench still feel like the signature screen, or like paperwork?

### C4. Break it deliberately

- [ ] Turn the golem so it faces nothing. It should **stall** and show a badge naming the problem —
      not silently idle.
- [ ] Turn it back. It should resume on its own.

---

## Part D — Steam (~10 min) — **the most important part**

### D1. Build a Boiler

You need 30 Scrap + 10 Iron Plate. Your Presser is making plate; let it run.

- [ ] Select **Boiler**, place it. Cost deducted.
- [ ] The top-left gauge still reads `0 Coke` — a player-built boiler starts **empty** by design.

### D2. Fuel it

Coke comes from **R1 Coking** (1 Coal → 1 Coke). Two routes:

- **By hand:** harvest Coal, crank R1 at the bench (48 ticks ≈ 4.8 s each).
- **By golem:** a second golem doing `Extract Coal → Assemble Coking → Push` into a Depot, then a
  hauler pushing Coke into the boiler. The boiler's *tile* accepts Coke like any other destination.

- [ ] Push some Coke into the Boiler (a golem facing it with Coke in hand, or hand-crank and haul).
- [ ] The gauge updates: `N Coke · 0/min · idle`.

### D3. Judge the ratio — **question 1**

Right now nothing draws steam, because `requireSteamPower` is **off**, so golems don't yet need it.
What you *can* judge is the arithmetic:

- Each powered golem burns **6 Coke/min**.
- The design's endgame is ~96 golems ≈ **576 Coke/min** of upkeep.
- One coking golem produces ~20 Coke/min.

- [ ] Watch the gauge's `/min` figure and countdown as you add golems near the boiler.
- [ ] **Your call:** does 6 Coke/min per golem feel like a meaningful running cost, or like a tax
      that will turn the game into a coal simulator? This is the number the whole economy hangs on
      and it has never been played.

---

## Known gaps — please *don't* report these

These are already on the backlog. Noting them again costs us both time.

- **The three buildings look identical** (Boiler, Steam Pipe, Clock Tower are the same tinted box).
  Placeholder art, known.
- **The bench readout is text-only** — a character-cell progress bar, no icons, no animation on the
  bench itself. It tells you everything; it isn't pretty.
- **Belts can't feed a buffer** — a belt only hands off to another belt. Getting goods into a depot
  needs a golem doing `Push`. Deliberate.
- **Golems you build don't survive a reload.** Save/load can't respawn them yet.
- **Clock Tower progress isn't saved.**
- **No player collision** — you walk through everything.
- **Golems render sunk into the floor** — sprite pivot issue, project-wide.
- **`requireSteamPower` is off**, so nothing stalls for lack of steam yet.

---

## Findings

Copy this table and fill it in. Severity: **blocker** (can't proceed) / **bad** (playable but wrong)
/ **note** (feel, polish, opinion).

| # | Part | What happened | What you expected | Severity |
|---|---|---|---|---|
| 1 | | | | |
| 2 | | | | |
| 3 | | | | |

### The three questions

**1. Fuel ratio —** is 6 Coke/min per golem right? Too harsh / about right / too cheap?

> 

**2. Manual era —** how long did Part B actually take, and was it tolerable?

> 

**3. Workbench —** does it still feel like a decision, or like paperwork?

> 

### Anything else

> 
