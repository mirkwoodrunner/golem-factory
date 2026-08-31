# Golem Factory — Playtest Script

**What this covers:** the whole game as it now stands — the opening arc, the steam economy, and
everything two build passes have added since this script was last written (the truckload market,
Creative Mode, §8's card gating, the Freight Link, the Slag Heap, Floor Expansion, belt splitters,
the bay cap, and a lighting pass). **None of it has been played.** Tests prove the mechanics match
the spec; they say nothing about whether any of it is any good, and every number marked *TUNING*
below was invented by a developer and has never been felt.

**Time:** ~20 min for the smoke test (Part A), ~60–90 min for the full run (Parts B–I).

**Read "The critical path" before Part B.** Parts A–I are grouped by feature, not by what unlocks
what, and following them in printed order used to walk you into a golem that could not move
(`requireSteamPower` is on, and the boiler was printed in Part E) and a three-card program that
does not fit the two-slot chassis you can afford. That section is the real order, start to finish,
from hand-harvesting to one golem running unattended.

**Build under test:** branch `passes/market-street-to-wrecking-bar`, **1422/1422 tests green**
(1234 EditMode + 188 PlayMode).

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
| **E** | Interact — *tap* to harvest a stall / **order a truckload from an empty stall** / open a construction station / refuel a boiler / **label a depot** / program a golem. **Hold** at the Hand-Crank Bench to crank. |
| **R** | Context-sensitive, **in this order**: turns the build ghost *if you are holding a placeable* → otherwise cycles the recipe at a bench you are standing at → otherwise rotates the golem you stand next to. **If R is not doing what you expect, you are still holding a placeable — press Escape.** |
| **Escape** *or* **right-click** | **Put the placeable down / leave build mode.** New. |
| **G** | Pick up the golem you are standing next to; press again to set it down on your own tile. It refuses to drop onto an occupied tile. |
| **Tab** | Management menu (Inventory · Assembly Line · Patents · Save/Load · **Ledger**) |
| **Left click** | Place a building, or — with the **Demolish** row picked up — remove one and get its **whole cost back**. Removing needs a tool in hand: with nothing selected, clicking the world does nothing. |
| **Mouse wheel / drag** | Zoom / pan |

Speed controls are bottom-centre: `PAUSE 0.5x 1x 2x 4x`. Cranking, golems, boilers, **market
deliveries** and the Clock Tower all run on the simulation clock. Harvesting does not — it is one
unit per key press regardless of speed.

---

## The critical path — manual collection to one automated golem

**Read this before Part B. It is the thing every previous draft of this script did not say.**
Parts A–I are organised by *feature*, not by *dependency*, so following them in printed order
walks you into a golem that cannot move and a program that does not fit. This table is the real
order. Every number in it was read out of the shipped assets, not out of the design docs.

**Three facts hold from the first frame, and each one used to be buried three parts away:**

1. **`requireSteamPower` is ON in `Sandbox.unity`** (it is `1` in the scene file). A golem with
   no Boiler orthogonally adjacent to it — or to a Steam Pipe run back to one — never takes a
   single step. It stalls `NoSteam` the instant you pull the lever. **The boiler is not Part E.
   It is step 7 of the opening**, and you cannot automate anything without it.
2. **The 12-Scrap Clockwork Scavenger has exactly TWO appendage slots.** So your first golem's
   whole program is `Extract → Push`. `Extract → Assemble → Push` is three cards and needs the
   **Brass Presser** (3 slots, 60 Scrap + 20 Iron Plate + 10 Gear) — which is the *entire* manual
   era, not a step in it. Part C used to open by telling you to build the three-card program on
   whatever golem you had. On a Scavenger there is no third socket to drop it into.
3. **Every stall except Scrap starts EMPTY.** The Scrap stall is seeded with 60 units; Coal,
   Copper, Zinc and Aether all start at zero, so your first Coal is an *order*, not a harvest.

| # | Do this | Costs | Where |
|---|---|---|---|
| 1 | Harvest the **Scrap** stall dry — it holds **60**, one unit per `E` | free | B1 |
| 2 | `E` on the now-empty stall: order a **free** Scrap truckload (30 units, ~4 s) | free | B1 |
| 3 | `E` on the **Coal** stall — it starts empty — and order a truckload (40 units, ~6 s) | **10 Scrap** | B1 |
| 4 | Harvest ~20 Coal off it when the cart lands | free | B1 |
| 5 | At the bench: `R` to **R1 Coking**, crank ×20 (1 Coal → 1 Coke, 2.4 s each) | 20 Coal | B2 |
| 6 | `R` to **R2 Scrap Reclamation**, crank ×10 (1 Scrap → 1 Iron Plate, 4.8 s each) | 10 Scrap | B2 |
| 7 | Build a **Boiler** *out on the street*, on the tile beside where the golem will stand | 30 Scrap + 10 Iron Plate | B3 |
| 8 | `E` on the boiler to **hand-load 20 Coke** | 20 Coke | B3 |
| 9 | Build a **Depot** on the tile the golem will face | 15 Scrap | B3 |
| 10 | `E` on the **construction station already standing in the shop** → build a **Clockwork Scavenger** | 12 Scrap | B4 |
| 11 | `G` to pick the golem up, walk it to the street, `G` to set it down, `R` until the **stall is behind it** | free | B4 |
| 12 | `E` on the golem → **AlwaysOn** into TRIGGER, **Extract** into STEP 1, **Push** into STEP 2, raise Extract's dial to **12** | free | B5 |
| 13 | **ENGAGE GEARS** | free | B5 |

**Total: ~77 Scrap, ~20 Coal, and about two minutes of cranking.** You start with 60 Scrap on the
stall, so exactly one free truckload covers the lot.

**The three cards in step 12 need no claim.** Extract, Haul and Push are granted outright at t=0
(they are the catalogue's opening hand), and both Logic Cores are ungated. **You do not need the
Assembly Line at all to build your first automated golem** — that is Part C's business, and the
last draft put it in front of this one.

**The layout, since "behind" and "in front" decide whether any of it works.** A golem pulls from
the tile *behind* it and pushes to the tile *in front*, on one straight line. So you want:

```
  (-8,-16)  [ Scrap stall ]                 <- the golem's SOURCE (behind it)
  (-8,-17)  [    golem    ] [ Boiler ]      <- boiler orthogonally adjacent, (-7,-17)
  (-8,-18)  [    Depot    ]                 <- the golem's TARGET (in front)
```

Those exact cells work: the Scrap stalls sit at `(-8,-16)` and `(-16,-16)`, and the street runs
from `y = -13` down to `y = -20`, so there is room on either side of a stall. The construction
station is at `(3,-1)` inside the shop. **You can build on the street** — the boiler and the depot
both go out there, and a placement that refuses with `off the ground` means you have walked past
the kerb.

**What you should see at the end.** The golem takes 12 Scrap out of the stall (18 ticks), turns
round, and puts 12 into the Depot (14 ticks) — about 3 seconds a round trip at 1x. **The Depot is
your stockpile**: it writes into `FactoryStockpile`, the same wallet the build menu spends from,
so your Inventory number climbs with nobody pressing anything. That is the whole point, and it is
the first moment in the game where the factory pays you while you stand still.

**It will stop twice, and both are the loop rather than a bug.** The stall runs dry (walk over,
`E`, free truckload) and the boiler burns out after **200 seconds** — 20 Coke at 1 Coke per 100
ticks per powered golem. If you want it to run longer, crank more Coke and hand-load again;
a hand-load moves 20 at a time on purpose.

---

## Retests from session 1

Your first session produced four findings. Two were real bugs, one was this script being wrong, and
one was a question. Each is answered **in place** below as an indented `RETEST` line under the item
that found it, so you can pick them up without re-reading the whole script:

| Finding | Verdict | Where |
|---|---|---|
| Clipping through the wall at the top of the street | **Real bug, fixed** | A3 |
| Bench recipe would not change with R | **Real bug, fixed — it was build mode, not the bench** | B2 |
| Crank progress "remains at whatever % it was at" | **The script was wrong, not the game** | B2 |
| "What are steps 1–6?" | **Answered; nothing changed in the game** | A4 |
| Lighting "a little dark" | **Raised 0.62 → 0.72** | A2 |

**Session 3** is this build's new work rather than a retest — see **Part I** at the end. In short:
depots can be labelled, golems have moods, the Workbench says its steps are a loop, there is a
Scrap Recycler, and the Ledger tells you what a recipe costs. Two of those answer findings you
raised: **A4's "what are steps 1-6?"** is now answered on the screen itself, and the room is no
longer an empty box.

**Session 2** added four more:

| Finding | Verdict | Where |
|---|---|---|
| Escape/right-click left the build row lit, needing two clicks to reselect | **Real bug, fixed** | A1 |
| "Still feels dark" | **Both lights raised — 0.95 ambient, 1.9 sconce** | A2 |
| "Hand Crank is too long — halve it" | **Done: 25 % → 50 % of machine speed** | B4 |
| "What is this test?" on the golem-rotation retest | **My fault; rewritten and moved to Part C** | C |

---

## Part A — Smoke test (~20 min)

### A1. The HUD

- [Done] **Top-left:** fuel gauge, `0 Coke · 0/min · idle`.
- [Done] **Top-right:** `Clock Tower dormant` — *dormant*, not a stage or a starvation warning.
- [Done] **Top-centre:** `All golems running.`
- [Done] **Bottom-left:** a Build menu with **eleven** rows, all costs legible, none cut off:

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
| **ScrapRecycler** | 20 Scrap + 10 Iron Plate *(TUNING)* |
| **Demolish** | free — **refunds the full cost** |

> **Eleven rows now, not nine.** The panel was authored with room for exactly nine (308px of
> space against 302px of rows); it grows itself to fit from code now, so check nothing is clipped
> at the bottom. Ten of those rows are placeables and the eleventh is the wrecking bar.
>
> **Note what you cannot build yet, and why it matters below:** the
> **GolemConstructionStation costs 5 Brass**, and Brass is R7 (2 Copper Ingot + 1 Zinc Ingot),
> which is two smelts away and not hand-crankable. **There is already one standing in the shop**
> — that is the one you will use for hours, and building a second is a mid-game move, not an
> opening one.

- [All those options work, but ESC and Right Click don't unhighlight the build option in the list, so to reactivate, you have to click twice] **NEW — leaving build mode.** Click a row, then get out of build mode three ways: **Escape**,
      **right-click**, and **clicking the same row again**. After each, the ghost should vanish and
      a left click on the floor should do **nothing** (no placing, no demolishing). This did not
      exist before your session — there was no way out at all.
  - [ ] **RETEST — FIXED (real bug).** The highlight was only ever refreshed when a ROW was
        clicked, so Escape and right-click cleared the held placeable and left the row still lit.
        Worse than cosmetic: the menu still believed it held that prefab, so your next click on it
        *toggled it off* rather than reselecting — hence needing two clicks. The rows now follow
        the controller's real state every frame, whichever way you let go.
        **Check:** press Escape (and right-click) and confirm the row **unlights immediately**,
        then that **one** click on it picks it up again.

### A2. The light — **question 5**

The global light was cut 1.15 → 0.62 and the wall sconces raised 0.95 → 1.5, taking a lit spot from
1.8× the shadow between lamps to **3.4×**. The reasoning is sound; the result is unseen.

- [Done] Stand in the middle of the room, away from the walls. **Can you still read the floor grid, a
      belt's direction and the build ghost?** That is the floor this change must not go below.
- [Done] Walk along the north wall. Do the sconces read as *the source of the light* now, with pools
      and darkness between, or is it just dimmer everywhere?
- [A little dark] **Your call:** too dark, about right, or not dramatic enough?
  - [Still feels dark] **RETEST — ambient raised 0.62 → 0.72** on your note. *(Superseded — see below.)*
  - [ ] **RETEST 2 — brighter again, and this time BOTH lights moved.** Ambient 0.72 → **0.95**
        (+32 % in the unlit middle of the room) and the sconces 1.5 → **1.9**, so a lit spot keeps
        its lead: 3.0× the shadow, against 3.1× last time and 1.8× before this whole pass. Raising
        the ambient alone would have flattened the room back toward the even wash the pass set out
        to fix — your complaint was never about contrast, it was about the floor being hard to
        read, so the whole room got brighter instead. Still dark, about right, or now too flat?

### A3. The world

The workshop is 25×25 with an open south front. **The market street runs outside it** — nine stalls
in a row at `y = -16`, about 16 cells south of spawn.

- [Done] Walk out of the shop onto the street. The road is **wider than the building** (37 cells) and
      should be paved edge to edge, with a kerb along its far side and short wall stubs
      ("shoulders") closing the gap beside the shop front. **No cobbles running off into
      background** — that is the specific thing to look for.
- [Done] Nine stalls: Scrap ×2, Coal ×2, Copper ×2, Zinc ×2, Aether ×1. Each pair is a separate node,
      so two extractors can work each.
- [Done] All five goods are **distinguishable from each other** by silhouette (placeholder art — I am
      asking whether you can tell them apart, not whether they are pretty).
- [You can clip through the wall on the top of the street where there isnt any workshop space] Try to walk off the edge of the road, and off the sides past the building. You should be
      stopped everywhere, with no way into the empty space beside the shop front.
  - [Done] **RETEST — FIXED (real bug, thank you).** The clamp asked "how far north may I go?" before
        "how far east am I?", so standing on the outer street at x = 16 and walking north put you
        on a workshop row — where the legal width is only the room's — and the sideways clamp then
        **pushed you through the building's flank into the shop**. It now bounds east/west first
        and north/south against it, so the shoulder behaves like the wall it is drawn as.
        **Walk the outer street on BOTH sides and push north into the building's flank.** You
        should stop dead beside the shop front, and never end up inside. Also confirm the shop
        floor itself is still fully walkable to all four corners — the fix must not have shrunk
        the room.
- [Done] Try to place a Depot out past the kerb. It should refuse with `off the ground`.

### A4. The Workbench

Walk to the construction station, press **E**, then close. Programming needs a golem — come back
after Part B if you have none.

- [What are steps 1-6?] **TRIGGER** then **STEP 1**–**STEP 6**, six numbered sockets, no duplicates.
  - **Answer:** they are the golem's **program**, run top to bottom once per cycle — `STEP 1`
        happens, then `STEP 2`, and so on, then it starts again. TRIGGER decides *when* a cycle
        begins. So `Extract → Assemble → Push` means "take from the tile behind me, make the
        thing, put it on the tile in front", forever. A chassis with fewer slots simply greys the
        later sockets out.
  - [Answered] ~~**RETEST — nothing changed in the game.**~~ *(Superseded — the screen says it now.)*
  - [ ] **RETEST 2 — THE SCREEN SAYS IT.** Each socket's caption gained a second clause driven from
        what you have actually built:

        ```
        TRIGGER  ·  when to start
        STEP 1   ·  then
        STEP 2   ·  loops back to 1
        STEP 3   ·  unused
        ```

        **The marker moves as you build.** Drop a card into step 3 and "loops back to 1" walks down
        to it, which is meant to *show* you the cycle rather than tell you about it. Before you fit
        a chassis the trigger row reads `fit a chassis first`; with a chassis and no cards it reads
        `drop cards below to build a cycle`.
        **Check:** build a two-card program and watch the marker move. Does the loop read now
        without anyone explaining it?
- [Done] **The vault is now GATED** — it shows only cards you have *claimed*, not the whole catalogue.
      From a fresh start you should hold exactly the movement verbs (Extract, Haul, Push). If the
      vault is empty, that is a blocker: say so immediately.
- [Done] **Hover a chassis button and a vault card.** Both should visibly brighten, and darken on
    ewas  press. This was measured at a 4% shift (invisible) and is now ~22%. Does it read?
- [Done] The fuel gauge, tower panel, bench readout **and the alerts strip** all vanish while the
      Workbench is open, and return when you close it.

---

## Part B — From a cold start to your first automated golem (~20 min) — **question 2**

**Time it.** The design budgets 12–15 minutes of hand-work before your first machine. It was
measured at ~7.5 min of cranking *before* the market moved the goods 16 cells away, and the bench
has since been halved.

**Follow the critical-path table above in order.** The five sub-parts below are those same
thirteen steps with the things to actually look at. **Part B now ends with a golem running on its
own** — that is the change. It used to end with a chassis you could not afford and no way to power
it.

### B1. Buy and harvest the raw goods

- [Done] Harvest a **Scrap** stall (one press each). Note how long, and how it feels — discrete
      presses plus the walk is a candidate finding on its own.
  - [ ] **CORRECTION — there are 60 units there, not 100.** This step used to say "harvest 100
        Scrap", which is not a thing you can do: `SandboxBootstrap` seeds the centre Scrap stall
        with exactly **60** and starts every other stall in the market at **zero**. The remaining
        Scrap comes from the free truckload below. If you stood there pressing `E` at an empty
        stall waiting for a hundredth unit, that was this script's fault.
- [ ] With the stall empty, press **E** on it again: that is an **order**, and the Scrap stall is
      the **free** one. 30 units, ~4 s. (Scrap is free by design — §10 forbids a soft-lock, and a
      priced market with an empty wallet is one.)
- [ ] Press **E** on the **Coal** stall. It is empty from the first frame, so this is also an
      order: **10 Scrap** for 40 Coal, ~6 s. Harvest ~20 Coal off it when the cart lands.
  - [ ] **You need the Coal now, not in Part E.** The old script did not mention Coal until the
        steam part, twenty minutes further down. With `requireSteamPower` on, no Coal means no
        Coke, means no lit boiler, means **no golem in the game moves at all**.

### B2. Crank the two things the boiler needs

- [How do I change the bench? I can only do coking, pressing R changes orientation of item it wants to place, but doesn't change recipe] Crank at the bench. Press **R** to change recipe.
  - [Done] **RETEST — FIXED, and the bench was never the problem.** `R` is arbitrated on "am I
        holding a placeable?", and **there was no way to stop holding one** — the build menu only
        ever selected. So after you opened the build menu once, `R` turned the ghost and returned
        before it could ever reach the bench (or a golem). **Press Escape or right-click to put
        the placeable down, then press R at the bench** — it should cycle through the five
        hand-crankable recipes (R1 Coking, R2 Scrap Reclamation, R3 Glassmaking, R8 Gear Cutting,
        R19 Wire Drawing) and the readout should name each one.
  - [What is this test?] ~~**RETEST — and the same fix should give you R on golems back**~~
        *(My fault — that said nothing useful, and you have no golem yet at this point in the
        script. It belongs in Part C, where it now is. Skip it here.)*
- [ ] **R1 Coking ×20** — 1 Coal → 1 Coke, 24 ticks (2.4 s) each. That is your boiler's first
      tank.
- [ ] **R2 Scrap Reclamation ×10** — 1 Scrap → 1 Iron Plate, 48 ticks (4.8 s) each. That is the
      boiler's build cost. *(Note it eats the Scrap: 10 plates cost you 10 Scrap.)*
- [Done] Let go of **E** mid-craft: progress **is kept**, and no goods are lost. Walk away and come
      back and it resumes where it was — that is deliberate (a pause, not an exploit: the inputs
      are only charged at completion). Press **R** mid-craft to change recipe: progress resets to
      zero, still no goods lost.
      *(An earlier draft of this script said progress "abandons" on release. That was the script
      being wrong, not the game.)*
  - [ ] **RETEST — HALVED, as asked.** The bench ran at §11 item 7's authored **25 %** of machine
        speed; it now runs at **50 %**. Every hand-cranked craft is twice as fast: R2 Scrap
        Reclamation 96 ticks → **48** (9.6 s → 4.8 s at 1x), R8 Gear Cutting 64 → **32**, R1 Coking
        48 → **24**. §9's ~7.5 minutes of cranking becomes **~3.75**.
        **Time Part B again from the top** and say whether the manual era now lands — this is
        question 2, and it is the number the whole opening arc is paced by.
        *One property held deliberately: the bench is still strictly slower than the machine that
        replaces it (a test pins it at every duration), so automating is still the point of
        automating. It is twice as slow now rather than four times.*

### B3. Light a boiler, out on the street

- [ ] Build a **Boiler** (30 Scrap + 10 Iron Plate) **on the market street**, on the tile that
      will sit *beside* your golem. The street is buildable — placement is bounded by the world,
      not by the workshop.
  - [ ] **The reach rule, and it is the one that catches people:** a boiler powers **only its own
        four orthogonal neighbours**. No diagonals. If you want the golem further away than one
        tile, that is a run of **Steam Pipes at 1 Iron Plate each**, and a single cell's gap ends
        the run. Building the boiler in the shop and the golem at the stall is **16 pipes**, so
        do not — put the boiler on the street.
- [ ] Press **E** on the boiler to **hand-load Coke**. It moves **20 at a time**, capped
      deliberately: hand-loading is how you restart a dead factory, not how you run a live one.
      The top-left gauge should come off `idle`.
- [ ] Build a **Depot** (15 Scrap) on the tile the golem will face. **A Depot writes into
      `FactoryStockpile` — your own wallet** — so this is what turns a golem's work into money you
      can spend.

### B4. Build the golem and stand it in the line

- [ ] Walk to the **construction station already standing in the shop** (cell `(3,-1)`) and press
      **E**. Build a **Clockwork Scavenger, 12 Scrap** — the only chassis you can afford, and the
      right one.
  - [ ] **Do not try to build a second station.** It costs 25 Scrap + **5 Brass**, and Brass is
        R7 (2 Copper Ingot + 1 Zinc Ingot), two smelts deep and not hand-crankable. The starter
        station is the one you use for hours.
  - [ ] It arrives **bare** — chassis fitted, no logic core, no cards. That is expected; the
        Workbench opens next.
- [ ] Press **G** to pick it up, walk it out to the street, and **G** again to set it down on your
      own tile. It refuses to drop onto an occupied tile.
  - [ ] **Golems do not walk.** They act on the tiles beside them and stay put, so carrying is the
        only way one gets from the shop to the market. **Is a 16-cell carry per golem acceptable,
        or does this want a way to build them where they work?** That is a real question about the
        opening and nothing in the game answers it.
- [ ] Press **R** until the **stall is behind it and the Depot in front**. A popup should name the
      new facing.

### B5. Program it, and let go

- [ ] Press **E** on the golem to open the Workbench. Drop **AlwaysOn** into TRIGGER, **Extract**
      into STEP 1, **Push** into STEP 2.
  - [ ] **These three cards are already yours** — Extract, Haul and Push are granted outright at
        t=0, and both Logic Cores are ungated. **You do not need the Assembly Line for this
        golem.** If the vault is empty of those three, that is a blocker: say so immediately.
  - [ ] **Steps 3–6 are greyed out**, because a Scavenger has **two** appendage slots. That is
        correct, not a bug — and it is why the three-card `Extract → Assemble → Push` program is
        Part C's business and not this one's.
- [ ] Raise **Extract**'s batch dial from **1 to 12** with the `+` stepper, and watch the tick
      cost it quotes climb with it (`6 + qty`). At 1 it moves one unit a trip and looks broken;
      at 12 it is a real hauler. **Is that dial discoverable, or did you have to be told?**
- [ ] Pull **ENGAGE GEARS**.
- [ ] **Watch it work without you.** Extract 12 (18 ticks) then Push 12 (14 ticks), about 3 s a
      round trip at 1x, and your Inventory number climbing on its own. **This is the moment the
      whole opening exists to reach. Did it land, and did you understand why it worked?**
- [ ] **It stops twice, and both are the loop:** the stall runs dry (walk over, `E`, free
      truckload) and the boiler burns out after **200 s** (20 Coke divided by 1 Coke per 100 ticks
      per powered golem). Check the badge tells the two apart — `NodeEmpty` and a **blue**
      `NoSteam`.

> **Record total elapsed time**, from Play to the golem running unattended. Note separately how
> much of it was *walking* and how much was *cranking*.

---

## Part C — Automate a *recipe* (~20 min) — **question 3**

**Part B automated hauling. This part automates production**, and it is a genuinely bigger step
than the last draft made it look — it needs a different chassis and a bought card.

> **READ THIS FIRST — two things the old draft got wrong, and either one stops you dead.**
>
> **1. `Assemble R2` is not in your opening hand.** §8's gating means the Workbench vault shows
> only cards you have **claimed**, and a fresh save grants exactly three: Extract, Haul, Push.
>
> **Do this first:** **Tab → Assembly Line**, and claim your way down to
> `Assemble Scrap Reclamation` (**4 Scrap** — 4× the recipe's own 1 Scrap input — and it gets
> cheaper the longer it sits). Its prerequisite is "you have produced Scrap", which Part B already
> satisfied. Then reopen the Workbench and it will be in the vault under `APPENDAGES · actions`.
> **That is the card this step calls "Assemble R2"** (1 Scrap → 1 Iron Plate).
>
> **2. It does not fit on the golem you have.** `Extract → Assemble → Push` is **three appendage
> cards**, and a Clockwork Scavenger has **two slots**. There is no third socket to drop it into,
> and no amount of claiming will make one. You need the **Brass Presser** — 3 slots, and this is
> its cost:
>
> | For | Needs | Which costs |
> |---|---|---|
> | Brass Presser chassis | 60 Scrap + 20 Iron Plate + 10 Gear | — |
> | 10 Gear | R8 Gear Cutting ×10, at 2 Iron Plate each | 20 Iron Plate |
> | 40 Iron Plate total | R2 Scrap Reclamation ×40 | 40 Scrap |
> | **Running total** | | **100 Scrap + ~5 min of cranking** |
>
> **That is the manual era's actual payoff, and it is the number question 2 is really asking
> about.** Budget for it: 100 Scrap is three and a bit free truckloads on top of everything Part B
> already spent, and you have to walk to the stall for each one.

- [ ] **Crank the Presser's parts.** `R` to **R2 Scrap Reclamation** ×40 (48 ticks each), then
      `R` to **R8 Gear Cutting** ×10 (32 ticks each, eating 2 Plate apiece). You should end with
      **20 Iron Plate and 10 Gear** left over — the Gears consume half the plates.
  - [Hand Crank is too long.  Let's halve the time, then we'll retest] *(Your session-2 finding
        lives here now — this is the block of cranking you were complaining about, and it has been
        halved. The retest note is under B2.)*
- [ ] Build the **Brass Presser** at the construction station (60 Scrap + 20 Iron Plate + 10
      Gear). **Was that worth it?** It is the single largest spend in the opening and it buys you
      exactly one extra program slot.
- [ ] Place a **Depot**, position the Presser so the stall is the tile **behind** it and the depot
      the tile **in front** (`Extract` pulls from behind, `Push` delivers in front). **And put it
      in reach of a boiler** — the same rule as B3, and it is still the easiest thing to forget.
- [ ] **Set Extract's dial to 1, not 12 — and this is the interesting bit.** R2 is a 1:1 recipe
      and `Assemble` runs **once per cycle**, so an Extract batch of 12 fills the golem's input
      stock to its 12-per-type cap and then only ever spends one of them a cycle. It does not
      deadlock (Extract takes `min(qty, room)`, so it self-limits to topping up by 1), but the
      golem then carries a permanently full hold — **and the amber `hold nearly full` badge fires
      at 9 of 12 and never goes out.** A golem that is working perfectly wears a warning forever.
  - [ ] **Try it wrong on purpose**, at 12, and watch the badge stick. **Then set it to 1.**
        Two questions, and they are the ones the Workbench lives or dies on: **did you work out
        why on your own?** And **is a permanent amber badge on a correctly-tuned production golem
        the right behaviour**, or should `Straining` only fire on a stock that is not draining?
        *(This is the same 9-of-12 number I1 asks you to judge — here is the case that tests it.)*
- [ ] Program `Extract → Assemble Scrap Reclamation → Push`, pull **ENGAGE GEARS**. It should now
      pull raw Scrap out of the stall and bank **Iron Plate** without you touching the bench
      again. **That is the bench being replaced, which is the whole arc.**
  - [ ] **Compare it to cranking.** The machine runs R2 at 24 ticks against the bench's 48. Does
        automating *feel* twice as fast, and does the Presser earn its 100 Scrap back at a rate
        you can perceive?
- [ ] Patent the program, then load it onto a second golem from the **Patents** tab. Patenting is
      free now (Focus is gone), so the only thing it buys is not rebuilding the program by hand.
      **Is that enough to make you use it?** If you rebuild the second golem card-by-card out of
      habit, the Patents tab is not carrying its screen.
- [ ] **Dismantle it.** Open the build menu, click **Demolish (full refund)**, then click the
      golem's tile. It should vanish and pay back its chassis cost **plus whatever it was
      carrying** in one popup. Press **Escape** or right-click to put the wrecking bar away.
      The question to answer: does having a free undo change how freely you build golems in the
      first place, or is it a tool you never reach for?
- [ ] **RETEST (moved here from B2) — `R` should rotate golems again.** Stand next to a golem and
      press **R**: it should turn, and a popup should name the new facing. If it does not, you are
      still holding a placeable — press **Escape** or right-click first. That arbitration was the
      whole bug: while a placeable is in hand, `R` turns the build ghost and never reaches a golem.
- [ ] Turn a golem to face nothing: it should **stall with a badge naming the problem**, then resume
      on its own when turned back.
- [ ] **Stand two stalled golems one cell apart.** Their badges should stack vertically, not
      overprint. Same for the interaction caption.
- [ ] **NEW — Demolish, with a full refund.** The build menu's **last row** is `Demolish
      (full refund)`. Pick it up, and the ghost turns a steady deep red over anything removable
      and inert steel over everything else. Click a Depot you placed: it comes down and **the
      whole 15 Scrap comes back**, with a green `+15 Scrap` popup where the grey `-15 Scrap`
      appeared when you built it. Escape / right-click / clicking the row again all put the bar
      down, same as a placeable.
  - [ ] **This is the answer to "the ability to pick up depots goes away at some point."** It was
        real, and it was caused by last session's fix: removal was only ever reachable from a
        click *while holding a placeable*, which was invisible while build mode had no exit and
        broke the moment Escape shipped. Removal now has its own tool.
  - [ ] **Check the refund is not free money.** Demolish a depot that was **authored into the
        scene** rather than placed by you — it should still come down and pay **nothing**.
  - [ ] **Check "move" works out of it:** demolish a building and re-place it somewhere else. Your
        stockpile should end exactly where it started. That is the whole reason there is no
        separate pick-up-and-carry mode.
  - [x] ~~**Your call:** is a full refund right, or does free relocation take the sting out of
        placing badly?~~ **DECIDED — full refund, and it is not a tuning knob.** The game is
        cozy; placement and reorganising must not be punitive. Recorded in `CLAUDE.md` so it is
        not re-litigated as "balance" later.
  - [ ] **Two consequences of that decision, both worth a click.** Demolish a building when your
        stockpile has **no room** for the refund: it should **refuse and stay standing** rather
        than come down and eat the goods. (Sandbox's stockpile is Unlimited, so you will only
        see this in a capped scene — it is covered by a test.) And **save, then load**: your
        stockpile must be exactly what you saved. A load sweeps the same runtime-placed
        buildings a refund pays out on, so refunding there would have handed you your whole
        factory's cost on every load — save/load/save/load as a resource duplicator. Caught and
        fixed before it shipped; worth one confirming pass in **Part H**.

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

> **You already did the cold start — it is B1–B3 now.** This part used to open by walking you
> through harvesting Coal, cranking Coke and lighting your first boiler, which is why the opening
> arc read as optional: the one thing without which *no golem in the game moves* was printed
> twenty minutes after the part that told you to build golems. That sequence has moved up into
> Part B where it belongs. **What is left here is the question**, which is about the burn rate
> rather than about the steps.

- [ ] By now you have one lit boiler from B3. Build **a second**, or run three or four golems off
      the first, and watch what fuel actually costs once the factory is not one golem.
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
  - [ ] **RETEST — FIXED, and it had never once worked.** `SandboxBootstrap` seeded the card pool
        *before* wiring the question "has the player produced X", and an unanswerable prerequisite
        deliberately passes — so the whole deck went straight into the offer queue and nothing was
        ever gated. `R1 Coking` sat in a claimable slot in a factory that had never seen coal.
        The order is swapped, the till refuses a locked card as well as the door, and the question
        is now asked of the tech tree's ledger (which only ever grows) instead of live stock, so
        spending your last Scrap cannot re-lock something you had already unlocked.
        **Check:** from a fresh save the line should offer **only** the three movement verbs, with
        **24 cards waiting**. Harvest Scrap and the Scrap-gated cards should join the queue on
        their own, without you having to claim anything to shake them loose.
- [ ] **NEW — the panel says what is coming and why it is not here yet.** Under the three slots
      there should be a `Waiting on prerequisites: N` line and a few named cards with `needs ...`.
      Without it the gate is an *absence* — you see three cards and cannot tell a fourth exists.
      **Does that turn "blocked" into "progression", or is it just a wall with a label?**
- [ ] **NEW — prices show the WHOLE bundle.** A card costing 8 Scrap + 4 Coke used to advertise
      "8 Scrap", light its Claim button off a Scrap-only check, and then refuse the sale saying
      "Not enough Scrap" while you were staring at plenty of Scrap. Claim something with a
      multi-good price and check the row, the button and the refusal all agree.
- [ ] **NEW — the line stops re-selling you what you own.** The movement verbs are free, granted at
      the start, *and* listed twice in the deck, so slots used to be spent re-offering Extract,
      Haul and Push. Claiming an owned card should now bring up something new instead.
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
- [ ] **NEW — write down your Scrap before you save, and check it after you load.** It must match
      exactly. A load replaces the built world by demolishing every runtime-placed building, and
      demolition now refunds — so the two features together nearly turned save/load into an
      infinite resource duplicator. The refund is now gated to the player's own click; this is the
      pass that proves it. **Load twice in a row** and check again.

---

## Part I — What the cozy automation pass added (~20 min) — **NEW, never played**

None of this has been in front of a person. Five things, and the first two are the ones I most
want an opinion on.

### I1. Golem moods — is silence the right default?

A golem used to have two looks: running, or red. It has six now, at three volumes.

- [ ] **Working draws NOTHING.** That is deliberate — forty golems each wearing a "working" icon is
      a factory you cannot read — but it is also the call I am least sure of. Stand in front of a
      working factory: does "no badge" read as *fine*, or as *the badges are broken*?
- [ ] **A golem waiting on a trigger shows a dim `zzz`** after about 1.5 s. An `AlwaysOn` golem
      should **never** show it (it is only idle for a single tick per cycle). If you see `zzz`
      flickering on a busy golem, that is a bug — say so.
- [ ] **A golem with no program shows `no program`.** Build a golem and *don't* program it.
- [ ] **A golem whose hold is nearly full shows an amber `hold nearly full`** at 9 of 12 units.
      **The judgement:** is 9 far enough ahead of the jam to be worth acting on, or does it fire so
      late you were going to notice anyway? *TUNING.*
- [ ] **A golem with no steam shows a BLUE badge, not a red one.** Let a boiler run dry with several
      golems on it. **The thing to look for:** does a field of blue read as *one* fault with one
      fix, rather than as everything being broken at once? That split is the whole reason it exists.

### I2. Smart depots — can you sort with them?

Press **[E]** on a depot to label it. It cycles through "any goods" and everything your stockpile
has handled, so the list stays short and grows with your factory.

- [ ] A labelled crate **takes only that good**. Push a mixed hold at one: the matching good goes
      in, the rest **stays in the golem** for the next tile. That is the sorter.
- [ ] A labelled crate **hands out only that good**. This is the half that was actually broken —
      before, a golem hauling from a depot got whichever good the shared stockpile felt like. Try
      running **two different lines off one stockpile** with two labelled crates. Does that work,
      and did you want it?
- [ ] Push a hold with **none** of the label at one: the golem stalls with `nothing here takes
      Slag`, naming the good it is stuck with. Relabel the crate and it should resume on its own.
- [ ] A crate that is simply **full** of its own good says `full` instead. Those are different
      problems with different fixes; check the badge tells them apart.
- [ ] **The judgement:** is `[E]`-cycling a decent control, or do you want a picker? With twenty
      goods in the stockpile the cycle is twenty presses long.

### I3. The Scrap Recycler — 20 Scrap + 10 Iron Plate *(TUNING)*

New building, the **tenth placeable** in the build menu (the eleventh row is Demolish). Throw **anything** in, get **Scrap** out, and it burns
**Coke** doing it. Deeper goods are worth more: a Casing comes back worth more than a lump of Coal.

- [ ] Build one, push Coke into it as fuel, then push junk in. Haul the Scrap back out with a golem
      — **it does not teleport to your stockpile**, which is deliberate.
- [ ] Let it run out of Coke: it should **refuse junk** and keep taking Coke, so refuelling is the
      way out. Let it fill up with nobody collecting: it should **stop**, visibly.
- [ ] **THE BIG ONE.** It disposes of Slag at **half** the Slag Heap's rate (2 per Coke against 4)
      and gives you a Scrap for the difference. Is that a real choice, or is one of them obviously
      right? If the heap now feels pointless, or the recycler does, that is the number to move.
      *TUNING.*
- [ ] Do you ever actually use it for anything **other** than Slag? It exists because there was no
      way to get rid of a mis-ordered truckload or a decommissioned line's leftovers. Did that
      turn out to be a problem you have?

### I4. The Ledger tells you what a recipe costs

**Tab → Ledger**, then **click a node**.

- [ ] A recipe node opens its full ratio, its byproduct **in lowest terms** (`1 Slag per 2 Iron
      Plate` — the number you size a Slag Heap by), its cycle time and rate, and its **live** rate
      once your factory is actually making it.
- [ ] **Check the arrows render.** Every recipe node's detail line used to contain a character the
      font does not have, so it drew a box. If you see any square boxes anywhere on this chart,
      that is a finding.
- [ ] **The Slag Heap, the Freight Mast and Floor Expansion should now light up when you build
      them.** They never did — three nodes on the chart were unreachable for as long as those
      features have existed. Build a Slag Heap and check its node goes lit.

### I5. The workshop is furnished

- [ ] Look at the **north wall** (the back of the room). There should be a hearth with a fire on the
      centre line, flanked by shelves, a workbench and tool racks. The other three walls keep their
      crates and barrels on purpose.
- [ ] **Nothing should be standing anywhere you wanted to build.** Everything sits on the wall ring.
      If a piece of furniture is in your way, say which one.
- [ ] **Your call:** does the room read as somebody's workshop now, or just as a room with more
      stuff in it?
- [ ] **And the floor.** It already has four plank variants plus rare brass plates and grates — that
      was built a while ago. Standing at gameplay zoom, can you see any of it? If it still reads as
      flat, the accents need to be commoner, not newer.

---

## Known gaps — please *don't* report these

- ~~**The interior is an empty box**~~ — **fixed, see I5.** Report what you think of it.
- ~~**The floor is monotone**~~ — it has variants and accents already; **see I5** for whether they
  are visible enough.
- **Belts can't feed a buffer.** A belt hands off only to another belt; getting goods into a depot
  needs a golem doing `Push`. Deliberate.
- **Workbench polish**: hand-coded lever housing, LiberationSans rather than a period face,
  procedural grain that repeats, text-only cards with no icons, dead space in both lists. *(The
  step captions are no longer part of this — see A4.)*
- **No player collision** — you walk through everything.
- ~~**No refund** on removing a placed building~~ — **stale, and the opposite is now true.**
  Demolition refunds the **full** cost and it is a settled design call, not a tuning knob. See
  Part C.
- **A one-card `ExtractFromNode` program jams** by design (a Scavenger needs Extract + Push).
- **Golems cannot walk to their post.** They are built at a construction station and carried
  there one at a time with `G`. With the market 16 cells outside the shop, that is a real walk per
  golem — deliberate today, but say if it grates.
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

**3. Workbench —** a decision, or paperwork? With Focus cut, the only lever left on that
question is the per-slot `Haul` batch dial. Does one dial per logistics slot carry the screen?

> 

**4. The market —** an economy, or a toll booth? Did bursts create a storage problem worth solving?

> 

**5. Lighting —** too dark, about right, or not dramatic enough? Was anything unreadable?

>

**6. The moods —** does a factory where a working golem shows NOTHING read as calm, or as broken?
This is the call I am least sure of in the whole pass. **Part I1.**

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
| **Recycler: points per Scrap** | 4 | |
| **Recycler: value by tier** | 1 / 2 / 3 / 5 / 8 / 13 (raw → megaproject) | |
| **Recycler: Coke per Scrap** | 1 | |
| **Recycler: output capacity** | 24 (two golem-loads) | |
| **Recycler: cost** | 20 Scrap + 10 Iron Plate | |
| **Recycler vs Slag Heap** | 2 Slag/Coke + a Scrap, against 4 Slag/Coke and nothing | |
| **Golem `Straining` threshold** | 9 of 12 units | |
| **Badge dwell: sleeping** | 1.5 s | |
| **Badge dwell: advisory** | 2.0 s | |

### Anything else

> 
