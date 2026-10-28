# Golem Factory: roadmap after the port

Written 2026-10-18, after the Godot conversion finished. It plans the work that takes the
prototype to a finished game, in order, and records the decisions taken for it. The backlog of
smaller items stays in `docs/open-items.md`.

## Next session: start here

**Phase 1 (crates) and its follow-up, pack upgrades, are done in code, on branch
`phase1/crates`**: built, reviewed, critiqued (`crates-design.md` §11,
`pack-upgrades-design.md` §8) and tested. All six steps of `docs/crates-design.md` §9 are in,
and so is `docs/pack-upgrades-design.md` (the user approved its recommendations on 2026-10-18).
Every Core test (1796) and every scenario passes, each scenario run twice from a clean build with
no flakes. Phase 1's "Done when" is pinned by `Core.Tests/Economy/LayoutMattersTests`, and the
follow-up's invariant by `PriceReachTests` (Phase 1 below). **Still to run: the human playtest of
`testscript/phase-1-playtest.md`**, which covers the crates, the pack upgrade and Floor
Expansion's new prices. What's left is in `docs/open-items.md`.

**Phase 2, onboarding and readability, is done in code** (branch `phase2/onboarding`): built,
reviewed, critiqued (`onboarding-feedback-design.md` §14–§16, `guide-chapters-design.md` §8) and
tested. Every Core test (1860) and every scenario (15) passes, each scenario run twice from a
clean build with no flakes. **Still to run: the human playtests**, `testscript/phase-2-playtest.md`
first (a new player through chapters 1–4 without F9, timed by the report's "Chapters 1–4" line),
then `testscript/phase-1-playtest.md`. The game-time floor for chapters 1–4 is measured: **7:58**
of the 15 minutes (Phase 2 below), **5:16** after Phase 3's Presser price. What's left of Phase 2
is in `docs/open-items.md`.

**Phase 3, tuning and a full playthrough, is done in code, and its "Done when" holds** (branch `phase3/tuning`, from
`phase2/onboarding`; no phase branch is pushed until all nine phases are done, the user's
call): built, reviewed, critiqued and tested. Done:
- **Docs:** `tuning-data-spec.md` (user-approved: §8.1 late prices, §17 decisions, §18
  measurements and corrections, §19 the critique), `testscript/full-playthrough.md`,
  `performance-report-template.md` and the filled `performance-report-phase3.md`,
  `progression-design.md` §14, and `onboarding-feedback-design.md` §17 (the feeder hover).
- **Code A:** the tuning data, guide prices read from data, Repeat limits, and the tower meter
  draining at the delivered rate.
- **Code B:** the feeder hover, the `perf` scenario, and the probe and badge optimisations. The
  budget holds at 96 golems.
- **The Aether-Hauler** was lowered to 20 Iron Plate + 10 Gear + 30 Coke, so chapter 8 fits:
  23:05 of 30:00. The full run's floor is 1:15:23.
- **The review** (87f1356).
- **The critique** (`tuning-data-spec.md` §19, c73068c to 8669354): saves keep the market
  street (stall stock and truckloads on the road; a cart ordered after a save no longer arrives
  on a load that refunds it); a patent whose Repeat can't finish is refused before the station
  charges; the Ledger's texts are pinned to the data (three more were stale); the guide's
  recipes, rates and rooms read the data; the full playthrough's six questions exist
  (`market-late`, not `market`); and the Godot project compiles again (the review broke
  `PerfScenario`).

- **The test run** (2026-10-22, at 499a62e): the Godot project builds from clean with no
  errors; all 1921 Core tests pass; every scenario (16) passes, each run twice headless with no
  flakes; `verify_art.py` exits 0 (65 byte-identical, 11 hand-replaced, 0 drifted); the Release
  `Category=Perf` benchmark passes. **Done when, part 1:** `FullRunFloorTests` passes, with no
  stuck step through chapter 10 and every chapter under its ceiling (75:23 of game time; chapters
  1-4 take 5:16). **Done when, part 2:** the windowed `perf` run failed (worst p95 23.4 ms,
  hover), but Satisfactory was running alongside it at about 60% CPU, and every section,
  headless and windowed, read about twice the recorded run at 18044ef. **Rerun on an idle
  machine (2026-10-22, at 7a79046): PASS**, every phase within budget: flowing p95 10.0 ms,
  4× 10.3, starved 13.9, hover 14.6, no-draw 7.5, worst frame 22.8 ms, matching
  `performance-report-phase3.md`. The first windowed launch after a clean rebuild had one
  142 ms frame (the rest of that run passed); see `open-items.md` §4.
- **After the test run** (the user's calls, 2026-10-22): the Ledger names no golem count
  (15541fe); the guide's program plans are built from the data, and the Slag Heap's ratio is
  `slagPerCoke` in `placeables.json` (9dcca18); the guide's Scavenger and Depot counts read
  their prices (7a79046). 1925 Core tests pass.

**Resume here:**
1. **Phase 4 (Art), on branch `phase4/art`: all five S1 chassis are in the game** (the Brass
   Presser, the Clockwork Scavenger, the Mainspring Overclocker, the Aether Hauler and the
   Zeppelin Freight Loader; the Hauler and the Zeppelin approved 2026-10-25). The Artificer's
   redraw (S1b) was installed and then restored to today's frames (see below). Next, at the
   user's go:
   - **The user's test of the Presser first.** Ask what they saw. Nothing has looked at a tinted
     Presser on screen yet (stuck red, waiting grey, out of steam blue; `check_art.py` measures
     the tints as readable, but `moods`' street uses Scavengers). Add a tinted Presser to a
     scenario's captures, and fold any of the user's notes into the Presser before moving on.
   - **How a chassis was made** (for a redo): `art-pipeline-design.md` §3.2, "The Presser, the
     template". Each chassis goes to its review sheet and a compact numbered grid, the user
     marks frames, and nothing enters `godot/art/` until they approve.
     **How (the user's call, 2026-10-24, to save usage):** the lead writes the chassis' draft
     script and `chassis.json` (copy the Presser's), then a helper agent on Sonnet runs
     `Tools/Art/golem_set.py <set> run` per `Tools/Art/golem-helper.md` and reports. The lead
     looks at `candidates_*.png` and the grid, sends the grid to the user, makes the fixes
     they mark, and sets `approved` once they approve; the helper installs.
     **The Scavenger trial's result (2026-10-24):** the helper's render was killed by Claude
     Code's low-memory reaper (ComfyUI holds most of the 16 GB), so **the user runs
     `golem_set.py <set> run` in their own terminal**, which also costs no usage, and tells the
     lead when it's done. A helper is still fine for install and verify (no ComfyUI); the lead
     can run those directly too, they're short. Don't restart a reaper-killed render unasked.
   - **Rules from the Presser's reviews:** golems are creatures with a head, arms and legs,
     wearing or wielding their chassis motif (`art-style-bible.md` invariant 17, the user's call);
     what a golem holds is in front of it, so from the north its body hides it (§5.1).
   - **Watch the silhouettes:** the Presser overlaps today's Hauler at 0.49 and the Scavenger
     at 0.48 against the 0.50 limit, so the Hauler's redraw must differ clearly. The draft step
     now stops a run whose south draft is over the limit, before any render.
   - **Rules from the Scavenger's review:** from the north, what's worn on the back (its scrap
     bucket) is drawn over the head and upper arms; the north view is the south mirrored and
     re-lit, so a one-handed tool stays on the golem's own side.
   - **Rules from the Overclocker's review:** a chassis' look follows its function, not the
     others' style (the user's call): the Overclocker is the Watchmaker, the precision assembler
     with three tool arms for the 3-input recipes, a loupe, a flyball governor that shows its
     state and the mainspring caged on its back; the roster's speed aura stays cut. Draw detail
     in: the first, plainer draft was "lacking". Known soft spots the user accepted: from the
     side the near arm crosses the body, and the hip arm hangs.
   - **Rules from the Hauler's review:** what a hand holds on the golem's own side is hidden
     with it from the north (its hands hold the crate's near corners, so the crate covers them
     from behind); only a re-render of that facing fixes it, since the old hands are in its
     render. A render can be redone for one facing: move that facing's `raw/base_<facing>/`
     aside and `run` again. The Hauler is wide and low on treads, in cream armour (red is the
     player's-action colour); its front outline overlaps the Scavenger at 0.47 and today's
     Zeppelin at 0.46, so the Zeppelin's redraw had to differ clearly from all four.
   - **Rules from the Zeppelin's review:** the north view is the toughest; compare it with the
     south pose by pose. One layer order per facing can't be right for every pose, so a fist
     that holds something in front is clipped by the body and legs in the draft, and a raised
     or hanging fist shows. Overlaps flip from behind: the arms come out over the shoulders
     from the front, so from the north the shoulders cover the arms' roots. Arms are the
     draft's in every frame (a recompose fixes them), but a change to the north base draft
     needs north rendered again. The Zeppelin is an iron behemoth hugging a roped bundle under a
     tethered blimp, which drifts off its middle (centred, it overlapped the Overclocker at
     0.505); the greyest of the five, with brass and copper trim.
   - **The Artificer's redraw (S1b) was rejected, and today's 16 frames are back** (the user,
     2026-10-26: "the redraw has made the artificer look worse overall"). It was made with
     `golem_set.py` (`"kind": "artificer"`, today's head traced in, the body redrawn in
     ComfyUI) and installed in 2511da8. The set and its draft stay in
     `public/images/S1b/artificer/` as history. Rules its reviews taught, for any later
     figure: sleeves are part of the shirt, not ovals laid on top, and as long from the
     front as from the side; a traced head needs a neck under it; limbs are the same length
     in every view; from behind the arms go under the body but stay visible; an arm swinging
     back moves from the shoulder with the elbow slightly bent. Check 14 now passes a mirror
     on edge light or edge tone (§9, decision 14), which stays.
   - **Adjusting today's sprite, from its own pixels** (planned with the user 2026-10-27):
     (1) stray pixels cleaned, done (`scripts/clean_today.py`, approved 2026-10-27);
     (2) side-view faces made the same in every frame, done (`scripts/side_heads.py`:
     walking right's frame 2 head, mirrored for walking left, its eye redrawn to match the
     front's by `scripts/side_eye.py`; approved 2026-10-27); (3) more contrast
     in-game, done as a darker two-pixel outline (`scripts/outline.py`, approved 2026-10-27; a
     value and colour boost was offered and set aside for now); (4) a smoother walk, done (front and back passing poses with a foot lifted, a separate
     standing pose a direction, stride 0.35 to 0.45; §9 decision 16). 
   - **The Artificer cranks at the hand-crank bench** (2026-10-27, §9 decision 17): his
     standing pose rocks and nods while the bench's wheel glints, one frame per tick of
     progress, facing the bench; an unaffordable recipe stands still. Drawn cranking arms were
     tried and rejected ("like a noodle floating on top of the sprite"): for any later
     figure, an arm drawn or rotated by script doesn't sit in an AI-rendered sprite; move
     the sprite's own pixels whole, or regenerate.
   - **Floor and walls (S2) closed, nothing replaced** (2026-10-28, §9 decision 18): a
     ComfyUI pilot (`Tools/Art/env_set.py`, `public/images/S2/set.json`) drew objects into the
     floor tile at every strength, and the wall and crate redraws lost their detail at the
     game's half-size view. Today's floor, walls and props stay. **Next: lighting (L)**
     (`art-pipeline-design.md` §6).
   - Then buildings, the Workbench, goods and the HUD, each
     with its own review sheet (`art-pipeline-design.md` §3).
   - Before Phase 4's test run: fix the windowed `hover` flake (`open-items.md` §4).
   Done so far: the specs and the user's thirteen calls (§9), the cleanup, `verify_art.py`'s
   gap, the `Tools/Art/` tooling, Core's `GolemFrames`, the `art` scenario's golem gallery,
   ControlNet and IP-Adapter in ComfyUI (§1.4), the Presser (8f09b0e), `golem_set.py` (77042e9),
   the Scavenger (3e7e413), the Overclocker (26241b6), the Hauler, and the Zeppelin (204a2b5).
2. **The human playtests, still to run:** `testscript/phase-2-playtest.md`, then
   `testscript/phase-1-playtest.md`, then `testscript/full-playthrough.md`. Run the game with the
   **mono** Godot build (a grey screen and "No loader found for resource: …cs" means the
   standard build was used).

`FloorPlayer`'s chapter 8 floor jumps around as prices change (`open-items.md` §3), so check
any retune at neighbouring prices.

Standing rules: tests and scenarios never write the player's real `save.json`; a renamed step `Id`
or definition name breaks saves.

## Decisions (2026-10-18)

| # | Question | Decision |
|---|---|---|
| 1 | Scope | **A polished 4–5 hour campaign first**, the size `progression-design.md` is built for, then endless play after the Clock Tower. No new tiers of goods: that re-opens tuning checked twice. |
| 2 | Art direction | **Top-down is final.** The isometric call in `digital-design.md` was an older version. |
| 3 | Who makes the art | **ComfyUI plus hand cleanup**, the route the Clock Tower and the carts took. Whether anything is commissioned is the user's call, made separately from this plan. *Narrowed 2026-10-23: cleanup is tool-only, with a review sheet before every commit and the user's hand only on a frame the user marks (`art-pipeline-design.md` §9.4).* |
| 4 | The first Workbench program | **A hint of the next drag**, never pre-filled cards. Programming is the core verb; the player makes every drag. |
| 5 | Waiting versus stuck | **A golem waiting for input gets a quiet grey idle mood.** Red is for failures the player has to fix. |
| 6 | `Straining` | **Kept, shown on hover only**, not as a badge, until a playtest says otherwise. |
| 7 | The Overclocker's identity | **Decided after a full playthrough.** If players never choose it over a Hauler, it gets a second exclusive card. The speed aura stays cut. |
| 8 | Lens at 3 Glass | **Tried once in the tuning spreadsheet**, kept only if stage 3 still balances. |
| 9 | Audio | **Placeholders for now.** Commissioning is the user's call, as with art. |
| 10 | Gamepad | **Mouse and keyboard first**, with input built so a controller can be added later. |
| 11 | Creative Mode | **Offered from the main menu.** |
| 12 | Depth additions | **City contracts first**, then endless play after the tower. A second region and selling golems wait. |
| 13 | Contract deadlines | **Open-ended, with a bonus for finishing quickly.** A hard deadline fights the cozy tone. |
| 14 | Release | **Not planned now.** Only the parts that shape development are kept (Phase 9). |

The crate questions were answered the same day; see `docs/crates-design.md` §5.

## Rules every phase keeps

The settled calls in the root `CLAUDE.md` hold throughout: full refund on demolish, free patents
and no Focus meter, rigid linear golems that stall rather than adapt, append-only enums, rules in
Core with nodes that only draw, and a new system on `SandboxWorld` with a scenario beside it.
`docs/cozy-automation-design.md` §0's invariants hold for anything touching golems, endpoints or
text.

## Phase 1: Make logistics matter (crates)

Build `docs/crates-design.md` in its §9 order. Steps 1–3 are one PR: local crates, the capped
pack, the collect verb, deliveries into their own crates, Ledger rates from golem output, and
save migration. Then the two inventory screens, the guide rewrite, and the Pneumatic Post.

The specs:

- `docs/crates-design.md`: crates, the pack, the wallet, saves, the guide's chapter changes (§8)
  and the Pneumatic Post (§10).
- `docs/inventory-screens-design.md`: the Pack and Crate screens, the label picker, and the
  `inventory` scenario.
- `testscript/phase-1-playtest.md`: the playtest that judges it.

**Done when:** a badly laid-out factory runs visibly worse than a good one, and belts are built
because a line needs them.

**Status: done in code (2026-10-18), the playtest still to run.** `LayoutMattersTests` composes
the real Sandbox with the same Scavenger feeder and Scrap Reclamation Presser, on steam, for five
minutes of game time:

| Layout | Iron Plate made |
|---|---|
| Feeder, crate, converter in a straight line | 103 |
| The same golems, the crates six tiles apart, nothing joining them | 0 (converter: "no input in Crate(x,y)"; feeder: "Crate(x,y) full") |
| The same, the player carrying the Scrap across once a minute | 82 |
| The same two stations joined by a five-belt run and an unloader | 101 |
| The straight line, its output crate never emptied | 50, then "Crate(x,y) full" |

A far producer feeds nothing unless something carries its goods, so a belt is built because the
line needs it. A player who ferries by hand once a minute recovers most of the output: at one
converter's scale the cost of a bad layout is the walking, which is the design's intent
(`crates-design.md` §5 Q2). Whether that reads as "visibly worse" in play is the playtest's to say.

### Phase 1 follow-up: pack upgrades

Spec: `docs/pack-upgrades-design.md`. **Status: done in code (2026-10-18), the playtest still to
run.** The user approved every recommendation, and the doc's §8 records the corrections. Its test
list is pinned: `PackUpgradeTests`, `PriceReachTests`, `FloorExpansionTests`, `PlaytestKitTests`
and `PlaytestSessionTests` in Core, and the `management`, `save` and `playtest-kit` scenarios.
`LayoutMattersTests` still passes with the new numbers (the table above is unchanged: crates stay
at 50).
The user's calls (2026-10-18): Floor Expansion keeps ramping its cost, with the clamp at the
pack's cap removed, and its later purchases cost more advanced goods. Pack upgrades raise the
pack's cap during play (100, 150, 200, 250) through a Pack row on the Assembly Line tab.

**The two are coupled, so they ship in one change.** Removing the clamp without upgrades makes
every Floor Expansion purchase from the second on unpayable from a 100 pack. Upgrades without the
new curve have nothing to pay for. The test that "every price fits a full 100 pack" becomes
"every price fits the pack reachable when it opens" (the spec's §1).

## Phase 2: Onboarding and readability

The playtest timings point here: the first program took 8 minutes, turning with R took 4½, and
the stall badge read "vaguely".

The specs (approved 2026-10-19):

- `docs/onboarding-feedback-design.md`: the hint, grey and red, hover.
- `docs/guide-chapters-design.md`: the guide's texts, checks, plans and kit actions.
- `testscript/phase-2-playtest.md`: the playtest that judges it.

What they cover:

- The Workbench shows a hint of the next drag (decision 4).
- Waiting golems go grey; red means stuck, and hovering a stuck golem names the tile and the good
  (decision 5). `Straining` moves to hover (decision 6).
- Source and target arrows always show on a hovered golem, so facing reads without R.
- The depot label picker: **done in Phase 1**, on the crate screen
  (`docs/inventory-screens-design.md` §4.4).

**Done when:** a new player finishes chapters 1–4 without the playtest kit in under 15 minutes,
the manual-era budget.

**Status: done in code (2026-10-19), the playtest still to run.** Only a new player can meet the
"Done when"; `testscript/phase-2-playtest.md` times one, and the report opens with
"Chapters 1–4: mm:ss (target 15:00) · kit used: no" (`PlaytestSessionTests`). What can be
measured is the floor: `Core.Tests/Tutorial/ChaptersOneToFourFloorTests` plays chapters 1–4 on
the world's own clock with every action instant (no walking or reading) but every good earned with
the player's verbs at their real rates: [E] at the stalls, truckloads that take their time,
cranking at the bench, golems' real cycles, boilers topped up with cranked Coke. Never the kit's
grants. It is deterministic, and pinned under 10:00.

| Ch. | Budget (`guide-chapters-design.md` §1) | Game-time floor | Of which at the bench |
|---|---|---|---|
| 1 | 5:00 | 1:20 | Coke and 10 Iron Plate |
| 2 | 4:30 | **4:15** | 40 Iron Plate and 10 Gears for the first Presser |
| 3 | 3:30 | 2:16 | the second Presser's Gears and Plate, the second boiler's Plate |
| 4 | 2:00 | 0:06 | none |
| 1–4 | 15:00 | **7:58** | 7:02 (Iron Plate 4:57, Gears 1:05, Coke 1:00) |

**Phase 3 (`docs/tuning-data-spec.md` §12, §18):** the Presser now costs 60 Scrap + 10 Iron
Plate + 5 Gear, and the Coal truckload 30 Scrap for 120. Measured again: **5:16** (1:26, 2:20,
1:22, 0:06), 4:14 at the bench (Iron Plate 2:54, Gears 0:32, Coke 0:48), each chapter under its
ceiling of two thirds of its budget.

The spec estimated about 5¼ minutes of cranking; the measurement is 7:02. Chapters 1 and 2 crank
about what the spec said. Chapter 3 cranks more: the spec took the second Presser's 20 Plate
(and the second boiler's 10) from the first Presser's crate, but by then that Presser has made
only part of it, so the rest is cranked. And the boilers' Coke, topped up by hand until the
coker runs, adds a minute. That leaves a new player about seven
minutes for everything else in chapters 1–4, and **chapter 2 almost none**: its floor is 15
seconds under its budget. If the playtest overruns there, the lever is the Presser's price
(Phase 3), as `guide-chapters-design.md` §7.1 decided.

`LayoutMattersTests` still gives Phase 1's table unchanged (103, 0, 82, 101, 50) after the
collecting Haul.

## Phase 3: Tuning and a full playthrough

The specs (written 2026-10-19; the tuning spec's decisions approved the same day, its §17):

- `docs/tuning-data-spec.md`: every number, where it lives, the test that pins it, the proposed
  changes (§16) and the open questions (§17).
- `testscript/full-playthrough.md`: chapters 1–10 without the kit, timed per chapter.
- `docs/performance-report-template.md`: the endgame load, the `perf` scenario and the budget.
- `docs/progression-design.md` §14: the tuning decisions.
- `docs/onboarding-feedback-design.md` §17: the waiting golem's hover card names its feeder.

- Play chapters 1–10 to the Clock Tower without the kit.
- Tune in this order: the boiler fuel ratio (`progression-design.md` §12 says first), market
  prices and truckload size, then the other invented numbers, in a spreadsheet beside the play.
  Try Lens at 3 Glass (decision 8). No price may pass the pack cap reachable when it opens
  (`pack-upgrades-design.md` §1).
- Profile the endgame at about 96 golems and 500 belt items. If it doesn't hold, cut stage 4's
  Conduit demand, as the design says.
- Decide the Overclocker (decision 7).

**Done when:** the playtest report shows a complete run with no step anyone stalled on, and the
frame budget holds at endgame.

## Phase 4: Art

The specs (written 2026-10-22, revised 2026-10-23 to the user's decisions):

- `docs/art-style-bible.md`: the invariants (§0), palette, outlines, light direction, shading,
  pixel density and zoom, canvas sizes, animation frames, facings and file names, and the
  readability rules, with today's art measured against them.
- `docs/art-pipeline-design.md`: the per-set workflow (tool-only cleanup, a review sheet before
  every commit), the replacement order and the cleanup commit, every file by set with its owner
  and what code reads it, the tooling to build (`art_manifest.json`, `check_art.py`,
  `palette_snap.py`, `comfy_redraw.py`, `pose_guide.py`, `reshade_mirror.py`,
  `review_sheet.py`, and a fix for a `verify_art.py` blind spot), the lighting, the `art`
  scenario, and **§9, the user's twelve decisions**.

**The decisions, in short** (2026-10-23, `art-pipeline-design.md` §9): the palette approved; the
full golem frame set for every chassis in all four facings (28 files a chassis, 140 in all: 105
drawn, 35 mirrored from the east side and re-shaded by tool); tool-only cleanup with a review
sheet at three zooms before every commit; the Artificer as S1b; inventory icons stay at 24;
zoom stays continuous; subtle lighting from `LightingRules`; the Steampunk UI pack dropped and
redrawn in S6; the unused sprites deleted before S1; ControlNet and IP-Adapter installed before
the golems.

**Order:** the cleanup commit; the tooling; then golems, the Artificer, floor and walls,
lighting, buildings, the Workbench, goods, the HUD.

**Done when** (`art-pipeline-design.md` §8): the cleanup commit in; every set replaced through
the pipeline (golems and Artificer not deferrable); every chassis with its full frame set in
every facing; the Steampunk pack gone; every set's review sheet shown to the user before its
commit; `verify_art.py` and `check_art.py` green; the lighting in from `LightingRules` and the
windowed `perf` run passing with it on; every Core test and scenario, the new `art` scenario
included, passing twice; and every replaced file recorded.

The roadmap's brief:

- **A style bible first**: palette (brass, mahogany, the warm brown outlines of the cart redraw),
  outline rules, light direction, pixel density at PPU 64, animation frame counts.
- **Replace in order of screen time**: golems (idle, work, stall and carry frames), floor and
  walls, buildings, the Workbench (lever housing, display face, card faces), goods icons, the HUD.
- Each set goes the Clock Tower's way: ComfyUI redraw, cleanup (tool-only since 2026-10-23),
  `HAND_REPLACED` in its generator, listed in `verify_art.py`, and `CLAUDE.md`'s art section
  updated.
- **Lighting**: sconces that light the floor around them, a warm room tint, steam that glows.

## Phase 5: Audio

The game has no sound today. Placeholders throughout (decision 9), built so a final asset drops
into the same slot.

- Ambience: workshop hum, the ticking clock, steam hiss rising with boiler load.
- Work sounds per chassis, synced to `GolemWorkMotion`.
- UI: card snap, the Engage Gears lever, the patent stamp, a claim, a stall chime.
- Music: a calm score that adds layers as the factory grows, and a Clock Tower fanfare.
- Mix rules: a cap on copies of one sound at once, and panning by screen position, so a
  100-golem factory doesn't turn to noise.

## Phase 6: Game feel

Steam puffs, sparks and build dust; a small squash on placing and demolishing; goods that pop
into a crate; camera zoom levels and a smooth pan to alerts; a Clock Tower stage that plays as an
event (camera to the tower, the bell, the reveal); Workbench cards that land with weight and a
tape ticker that prints.

## Phase 7: The game around the game

- A title screen over a live workshop, save slots with thumbnails, autosave, Continue.
- Settings: audio buses, display and UI scale, rebindable keys (actions are already registered in
  code). Mouse and keyboard only; input is kept controller-ready (decision 10).
- Accessibility: colour-blind-safe card colours (teal against copper is a risky pair), text
  size, reduced motion.
- A factory overview: map or overlay, production graphs from the Ledger's rates, an alerts
  history.
- Creative Mode on the main menu (decision 11).

## Phase 8: Depth

Each addition gets its own design doc, like `crates-design.md`, with its invariants written
first. Additions grow along the game's identity (rigid, local, deterministic) rather than copying
Factorio's breadth.

1. **City contracts** (decision 12): requests that fill the gaps between tower stages, and give
   the Artificer's flexible half of the tabletop design a job. Open-ended, with a bonus for
   speed (decision 13).
2. **Endless play after the tower** (decision 1).

Waiting: a second map region, and selling golems to the city.

## Phase 9: What release asks of development

Release itself isn't planned (decision 14). These shape the code, so they are kept in mind
throughout rather than retrofitted:

- **Text kept in data**, not strings in code, so a translation is a data change. The font's
  glyph rule (`font-glyphs` scenario) already constrains characters.
- **Save files that can move between machines**: one folder, versioned, no absolute paths. A
  store's cloud save would need exactly that.
- **Errors captured to a file**, which the playtest report already does.
