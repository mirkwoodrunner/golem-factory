# A second region: the Docks

**Status: approved 2026-11-07 with the user's answers in §6.** The roadmap's next depth addition
after Phase 8 (decision 12: "a second region and selling golems wait"), chosen by the user on
2026-11-07. Written before the code, like the Phase 8 docs, with its invariants first.

## What's already decided

- **Roadmap decision 1:** no new goods, tiers or currency. A region is land and layout, not
  content that reopens the tuning checked twice.
- **Endless play §2:** *land is the hard limit.* The floor stops at `MaxNorthExtent`, the street
  at ±34, and "more land is the second region". Endless play grew mostly through density and
  rates because of it.
- **The golem cap is 112** (`performance-report-endless.md`): the largest factory whose frame
  holds the budget, measured with the whole factory on screen.
- **The Freight Link** (`progression-design.md` §6) exists to move goods between tiles no belt
  joins: "remote outposts". A second region is the place it was meant for.

## The idea in one paragraph

East of the market street's end, through a gate, lies **the Docks**: a second floor the player
buys, with a quay of its own cart spots where barges bring raw goods by water. It holds what the
workshop has run out of room for. Goods cross between the two by Zeppelin, by belt along the
street, by hand, or through the Pneumatic Post, whose crates open onto the one shared store in
either region (§6 Q3). It opens when the factory builds its first Zeppelin, in chapter 9 (§6
Q2), as the tower's rope comes down, so the campaign's last chapters have room to grow into.

## 0. Invariants

A pass that touches the Docks keeps all of these. Each gets a test in `Core.Tests` when built.

1. **No new good, tier, recipe, chassis or currency** (decision 1). New buildings only where
   §6 says (a gate, the quay's spots); new art for those only.
2. **One world, one clock, one save.** The Docks are cells in the same `SandboxWorld`, ticked by
   the same `SimulationClock`, saved in the same file. No scene switch, no loading screen.
3. **Rules in Core, on `SandboxWorld`.** The region's bounds, gate and quay are Core, tested
   without a scene; nodes only draw. A scenario covers each surface with real input.
4. **The frame budget holds** (endless invariant 6). The cap is re-measured with the Docks built
   out, with the camera in either region. It may only rise if the measurement says so.
5. **Land stays finite and bought.** The Docks' floor is bought in rows like the workshop's,
   through `PlayerWallet`; it has a hard edge.
6. **Every price fits the pack reachable when it opens** (`PriceReachTests`).
7. **Saves restore exactly and never clamp.** An older save has no Docks and loads as before.
8. **Creative Mode** has the Docks open from the start, free, as it has every rung.
9. **Text:** never an id, only the font's glyphs, every quoted amount built from the data.

## 1. Where it is

The world today is a cross: the workshop (x −12..12, y −12..24 at full extent), the street below
it (x −34..34, eight rows), the town square below that. The Docks sit **east of the street's
end**: a floor of their own starting at x 40, joined to the street by a short gated lane on the
street's rows (x 35..39). The gate is a fixture, roped until the Docks open; once open it is
walkable floor and buildable ground like the street.

| Part | Cells (proposed) |
|---|---|
| The lane | x 35..39, the street's rows |
| The Docks' floor | x 40..64, y −20..−4 to start; bought north in rows to y 8 |
| The quay | the Docks' south edge, y −20: eight cart spots at x 44..60, pitch 2 |

The camera follows the player there as it does down the street and into the square. The overview's
map gains the Docks beside the workshop.

## 2. What is different there

Only layout, so the region asks for different factories rather than more content:

- **The quay's barges** are supply carts (`supply-design.md`) on spots of their own, the same
  ladders and prices, so a factory can feed itself at the Docks without hauling raw goods down
  the street. Seventeen street spots plus eight quay spots.
- **No starting steam.** The Docks have no boiler or pipe until the player builds them: a line
  there pays its own steam, or a pipe is run down the street.
- **Distance, with the Post as the way round it.** The quay is about 40 cells from the workshop's
  front. A belt that far is a lot of belt; a Zeppelin and two masts do it in one launch; a Post
  crate at each end moves goods through the shared store (the user's call, §6 Q3), at the Post's
  own capacity.

## 3. When it opens

**When the factory has built a Zeppelin** (the user's call, §6 Q2): the same condition that brings
down the Clock Tower's rope (`SandboxWorld.ZeppelinChassis` in the Ledger). The gate's rope says
"opens once you build a Zeppelin". This lands mid-campaign, in chapter 9, so:

- **The guide gains a step** in chapter 9, after the Zeppelin flies: walk through the gate to the
  Docks. Its check is the player standing on the Docks' floor; the kit walks there and back. It
  asks nothing to be built, so the region stays optional for the campaign (no later step needs it).
- **The measured floors hold.** `FullRunFloorTests` carries the full run through chapter 10 within
  each chapter's ceiling; the new step must keep chapter 9 inside its ceiling (a walk, no price).
- **Prices fit the pack of chapter 9** (invariant 6): the Docks' first floor rows and the quay's
  spots are priced in goods a chapter 9 factory makes, within the pack it has then.
- Nothing before chapter 9 changes.

## 4. The golem cap

The cap was measured with the whole factory on screen. With two regions the camera shows one at a
time, so the off-screen region's **drawing** costs nothing, but its golem and building nodes
still run their per-frame work (about 2.4 ms and 1.9 ms at 160 golems). Proposed:

1. Nodes off screen skip their per-frame presentation (frame choice, badge, animation) and catch
   up when they come back into view; the simulation never skips.
2. Re-measure with the Docks built out: a 112-golem workshop plus a Docks factory, the camera in
   each. Raise the cap only to what holds (§6 Q4).

## 5. Build order

1. Core: the Docks' bounds and floor purchases, the lane and gate fixture, the quay's spots on
   the supply street, opening when the tower stands; saves. Tests first.
2. The shell: the Docks' floor, walls and quay drawn from Core's layout (as `ShellNode` does the
   workshop), the gate's art (generated), the camera's bounds.
3. Off-screen nodes skip presentation; the `perf` scenario's Docks load; the cap measured.
4. The overview's map shows the Docks; the HUD's region name when the player crosses the gate.
5. The guide's chapter 9 step and its kit action; the floors checked.
6. A `docks` scenario with real input: walk through the gate, buy a row, add a barge, build a
   line, launch a Zeppelin to the workshop, save and load. Frames looked at.
7. The docs it amends (`endless-design.md` §2, `guide-chapters-design.md`, root `CLAUDE.md`'s
   scenario list).

## 6. Questions for the user

**Answered by the user, 2026-11-07:** 1 (a) the Docks; 2 (b) at the Zeppelin, mid-campaign;
3 (b) yes, the Post reaches the Docks; 4 (a) re-measure the cap; 5 (a) generated placeholder art.

1. **Where and what?** (a) *Recommended:* the Docks, a second floor east of the street through a
   gated lane, with a quay of barge spots. (b) A second floor north of the workshop, through a back
   door: simpler, no quay, closer, so no reason for Zeppelins. (c) An outpost reachable only by
   Zeppelin (no walking there): the purest Freight Link use, but the player can't stand in it.
2. **When does it open?** (a) *Recommended:* once the Clock Tower stands. (b) At the Zeppelin
   (chapter 9), which needs a campaign retune and a guide chapter.
3. **Does the Pneumatic Post reach the Docks?** (a) *Recommended:* no; Post crates are the
   workshop's, so goods cross by Zeppelin, belt or hand, and distance means something. (b) Yes:
   any Post crate opens onto the shared store, which makes moving goods between regions free.
4. **The golem cap with two regions.** (a) *Recommended:* off-screen nodes skip presentation,
   then re-measure and raise the cap only to what holds. (b) Keep 112 across both regions, no
   measurement.
5. **Art.** The Docks' floor, quay and gate need pictures. (a) *Recommended:* generated
   placeholders from scripts, as the City Office and civic site were, until the user commissions
   art. (b) Wait for commissioned art before building.
