# Golem Factory: roadmap after the port

Written 2026-10-18, after the Godot conversion finished. It plans the work that takes the
prototype to a finished game, in order, and records the decisions taken for it. The backlog of
smaller items stays in `docs/open-items.md`.

## Next session: start here

**Where things stand (2026-11-20).** Phases 1 to 8 are done in code, and so is the second region, the
Docks. Selling golems to the city is built (`golem-sales-design.md`). The Steam Deck work has begun
(`steam-deck-design.md`). Each phase's full record, test runs and the user's calls are in
`docs/history/roadmap-log.md`. What's left is in `docs/open-items.md`.

**The design pass of 2026-11-19** (PR #73, the user's calls) made six changes. Unlocks are picked
directly at a fixed price (`progression-design.md` §8). The Workbench has no trigger socket, so every
program runs Always On. The golem cap is a fixed 112. Contracts and golem orders pay city vouchers
(`contracts-design.md` §4). The Presser costs 50 Scrap + 3 Iron Plate + 2 Gear (`tuning-data-spec.md`
§12.2). A cart refunds in full when removed or changed.

**Resume here:**
- **The Steam Deck**, the owner's side. Launch it on the Deck (D1.5, D1.6, then D2's Steam Input
  template). Try the playtest script with no mouse or keyboard (D6's exit). Run
  `export-linux.ps1 -DeckHost steamdeck -RunPerf` for D8's numbers. The machine-side work through D8
  is done (`steam-deck-design.md` §7).
- **Open questions for play**, from the design pass. What vouchers buy once every upgrade is maxed in
  endless play (nothing, today). Whether chapters 1 and 3 still crank too long (the boiler's 10 Plate
  and the Coal cart's first rung are the next levers).
- **The human playtests, still to run.** `testscript/phase-2-playtest.md`, then
  `testscript/phase-1-playtest.md`, then `testscript/full-playthrough.md`, then
  `testscript/endless-soak.md`. Run the game with the **mono** Godot build (a grey screen and "No
  loader found for resource: …cs" means the standard build was used).
- **Art for a new figure** follows `art-pipeline-design.md` §3.2 (the template) and §3.3 (the rules
  the reviews taught). The user runs `golem_set.py <set> run` in their own terminal (ComfyUI is
  killed by the low-memory reaper inside Claude Code).

`FloorPlayer`'s chapter 8 floor jumps around as prices change (`open-items.md` §3), so check
any retune at neighbouring prices.

Standing rules: tests and scenarios never write the player's saves (`user://saves`); a renamed step `Id`
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
commit; `verify_art.py` and `check_art.py` green; the lighting in from `LightingRules` and
`perf` passing with it on; every Core test and scenario, the new `art` scenario
included, passing; and every replaced file recorded.

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

0. **Supply carts** (`docs/supply-design.md`, approved 2026-11-06): the market street becomes
   carts the player adds, upgrades and removes on roped-off spots, each restocking at a rate.
   Truckloads and hand orders go. First, because contracts and endless play assume a factory that
   runs without the player at the stalls. **Done: steps 1 to 3.** Seventeen spots on the
   street, one starting Scrap cart, no truckloads (`SupplyStreet`, the `supply` scenario). The
   guide buys the Coal cart in chapter 1 and its Rate rung in chapter 3; an old save's stalls
   load as carts. A cart is offered once the Ledger has seen its price's goods (user's call). The
   Coal Rate rung was lowered to 10 Iron Plate + 5 Gear so chapter 3 fits: floors 6:06 for
   chapters 1–4 (chapter 3 2:06 of 2:20), 1:19:44 for the full run (chapter 8 28:27 of 30:00,
   from 23:05). Holding E repeats a harvest again: at 60 fps it had taken two goods and stopped.
   Step 3: the overview's graph switches between goods made and spent (what the factory uses
   up), and its lower panel between the alerts and the market, every cart with its stock and
   its goods in and out a minute.
1. **City contracts** (decision 12): requests that fill the gaps between tower stages, and give
   the Artificer's flexible half of the game a job. Open-ended, with a bonus for
   speed (decision 13). **Done (2026-11-07):** first the tower's fresh-production rule went, so
   any delivered good counts there (`contracts-design.md` §11). Then the City Office went up
   beside the tower: eighteen contracts in `data/contracts.json`, one at a time, opening with
   the first smelter in chapter 5 (the guide's `contract` step). It is filled from any source
   through its crate, its bonus earned within par, its rewards goods and cart rungs, saved, and
   covered by the `contracts` scenario (§12).
2. **Endless play after the tower** (decision 1). **Done (2026-11-07)**, `docs/endless-design.md`:
   the frame budget measured (`performance-report-endless.md`): 160 golems didn't hold, so after
   optimising the mood and stall-facts reads and the nodes' per-frame work, the golem cap is 112,
   where it holds. Endless rungs on the pack (300, 350), carts (rungs 4, 5) and bays (to 112),
   opened by the tower; generated contracts after the authored eighteen, levelling off; the last
   stage's banner pointing at the City Office and the overview's running totals; and the civic
   works (the Tramway, the Observatory, the Great Bell) on the tower's machinery, drawn by one
   placeholder site until their art is commissioned. The `endless` scenario covers each.
   Phase 8 is done.

**A second region, the Docks, is done** (2026-11-08, `docs/region-design.md` §7): east of the
street, opening at the Zeppelin in chapter 9, its rows bought north, a quay of eight cart spots,
and the `docks` scenario. Off-screen nodes skip their animation; re-measured, the golem cap stays
112 (`performance-report-endless.md` §5). After the tower the Docks are room to spread out, not to
grow (`endless-design.md` §2). Selling golems to the city is built (`golem-sales-design.md`).
Waiting: the Steam Deck
(`steam-deck-design.md` §8 holds the user's answers: native gamepad support, a Deck on hand, the
viewport chosen from frames).

## Phase 9: What release asks of development

Release itself isn't planned (decision 14). These shape the code, so they are kept in mind
throughout rather than retrofitted:

- **Steam Deck, as hardware only** (not the store): planned in `docs/steam-deck-design.md`, to
  start after Phase 7. Its first two steps (a Linux export, a Steam Input template) need no game
  changes. Native gamepad support is best built once Phase 7's menus exist.

- **Text kept in data**, not strings in code, so a translation is a data change. The font's
  glyph rule (`font-glyphs` scenario) already constrains characters.
- **Save files that can move between machines**: one folder, versioned, no absolute paths. A
  store's cloud save would need exactly that.
- **Errors captured to a file**, which the playtest report already does.
