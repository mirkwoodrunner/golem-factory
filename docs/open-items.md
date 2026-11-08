# Golem Factory: open items

The backlog for the Godot game. The current state and what's next are in
`docs/roadmap.md`, under "Next session: start here". The Unity-era backlog, with
the reasoning behind every item it closed, is `docs/history/open-items-unity.md`.

Everything the progression design specifies is built (`docs/progression-design.md`), and no node
on the Ledger is flagged as planned. What is left needs a person, a decision, or is polish.

## 1. Needs a person in front of the running game

`testscript/phase-1-playtest.md` runs it; the guide asks the questions and writes the report.

| What | Why only play can answer it |
|---|---|
| **The boiler fuel ratio** | 3 Coke/min per working golem. progression-design §12: "mis-tuned, this becomes a coal simulator." |
| **The manual era** | Budgeted at 12-15 minutes. The guide's step timings now measure it. |
| **Whether the Workbench still decides anything** | Recipes and chassis fix most of a program; the Haul good and batch dials are what's left to choose. |
| **Whether the market is an economy or a toll booth** | Truckloads are priced and arrive in bursts. |
| **Marked tiles** | The guide marks where to build. Do they help, or get in the way? Decides round 3 (below). |
| **The carry outline** | Is it clear where a carried golem will land? |
| **Grey for waiting, red for stuck** | Built in Phase 2 (`docs/onboarding-feedback-design.md` §3). `testscript/phase-2-playtest.md` asks whether players can tell them apart. |
| **Chapters 1–4 in under 15 minutes** | Phase 2's "Done when". Played instantly, they took 7:58 of game time, 7:02 of it at the bench, and chapter 2 4:15 of its 4:30 budget; after Phase 3's Presser price, 5:16 (chapter 2: 2:20) (`ChaptersOneToFourFloorTests`; table in `docs/roadmap.md`, Phase 2). Only a new player can say whether the rest fits. |
| **The human playtests of Phase 1 and Phase 2** | Neither has been run. `testscript/phase-2-playtest.md` first, with a player who has never played; then `testscript/phase-1-playtest.md`. |
| **Every invented number** | Listed at the end of the playtest script. None has been felt. |

## 2. Decisions waiting on the playtest

- **Round 3 of the marked-tile plan** is merged (#54): the later chapters' tiles follow where
  the player actually built, instead of fixed offsets from the stalls. Following the marks plays
  the same either way. Rounds 1 and 2 are merged too: pipe steps accept any working route, and
  the guide says what is in the way of a marked tile.
- **Crates** (`docs/crates-design.md`) are built (Phase 1): each depot is a crate of its own,
  the player carries a capped pack, and the Pneumatic Post is the shared store. Its "Done when"
  is pinned in Core (`LayoutMattersTests`; numbers in `docs/roadmap.md`, Phase 1). So are pack
  upgrades (`docs/pack-upgrades-design.md`, 100 to 250) and Floor Expansion's six authored
  prices (`PriceReachTests`). **The human playtest of `testscript/phase-1-playtest.md` has not
  been run.** It judges whether a bad layout reads as visibly worse in play, not only in the
  numbers, and whether the pack upgrade and the dearer extensions read as worth it. Phase 3's
  tuning sets the final prices.
- **Whether the Overclocker has an identity** beyond more slots: one exclusive card (`Repeat`)
  and the same dials. Decided after a full playthrough (`docs/roadmap.md`, decision 7).
- **Built in Phase 3 (`onboarding-feedback-design.md` §17): a feeder asleep on a trigger that
  never fires reads as feeding, and the waiting golem's hover card names it.**
  An Interval core set very long, or a Signal nobody sends, still counts as feeding the crate in
  front of it, so the golem behind reads grey (`onboarding-feedback-design.md` §16). The probe
  can't know a trigger will never fire without simulating it. **Recommended:** leave the rule,
  and have a waiting golem's hover card name its feeder ("Waiting for Scrap · Scavenger #1
  brings it"), so the player can follow the chain to the sleeper.

## 3. Known gaps

- **Old saves resume the guide a few steps off.** Saves from before the guide stored its step by
  id (#49), or its golems' roles (#52), fall back to the step's index and to build order. A fresh
  game is unaffected.
- **A Clock Tower placed by hand in a save from before #47 is not rebuilt.** The tower is a
  fixture in the town square now; such a save restarts it at stage 1.
- **After a load mid-stage, the tower's starved alarm can show briefly** until the first
  delivery lands, because supply meters aren't saved. Without that, a line that broke since the
  save would never raise the alarm.
- **The Ledger's golem estimates were hand-written: closed 2026-10-22.** The user decided to
  drop them. The stage lines no longer say "~63 golems", "~72" or "~96", and phase VI reads
  "Four rate-scaled stages"; `NoLedgerText_EstimatesAGolemCount` keeps them out
  (`tuning-data-spec.md` §19.3).
- **The guide's smelter and carrier plans were static: closed 2026-10-22.** The user decided to
  build them from the data. `TutorialGuide` builds the iron Presser's, the coker's, the
  smelter's and the carrier's plans per world from the loaded recipes (R2, R1, R4) and the Slag
  Heap's ratio, now `slagPerCoke` in `placeables.json`. The hint, the kit and the step texts
  follow a retune (`TheGuidesPlans_FollowARetunedRecipeAndHeap_InTheHintTheKitAndTheCheck`).
  `Matches` still reads no batch, so older saves still count (`tuning-data-spec.md` §19.3).
- **A partial Haul could jam an Assemble for good: closed 2026-10-19.** The user decided a Haul
  collects into the golem until it holds its batch, showing "5/10 Scrap", and never takes more
  than a batch (`docs/onboarding-feedback-design.md` §13). An Assemble now only runs on full
  batches.
- **The Workbench hint's free-play fallback is tested only in Core.** Without a guide plan, a
  bare golem gets TRIGGER, then STEP 1, then the lever framed, and a programmed golem nothing
  (`WorkbenchHintTests`). No scenario draws it on screen: the `workbench` scenario's golem is the
  guide's, so it sees the plan's hint (`onboarding-feedback-design.md` §15.1).
- **The full-run floor jumps by minutes when a price moves a little.** `FullRunFloorTests`
  measures chapter 8 at 23:05 with the Aether-Hauler at 20 Iron Plate + 10 Gear, 22:57 at
  36 + 18, 31:11 at 34 + 17 (over its 30:00 ceiling) and 30:01 at 32 + 16. `FloorPlayer` adds
  lines greedily: another line for a good once the request in hand is 40 a line or more
  (one-input goods), or once 45 s have passed in the call, and that 45 s includes time spent
  earning inputs in nested calls. So a small price change reorders which lines are built when.
  Counting only the call's own waiting was tried in the Phase 3 review and didn't smooth it:
  the jumps moved (chapter 10 then failed at 30 + 15 to 34 + 17, and steps timed out at
  10 + 5 and 24 + 12). A steady floor needs a player who plans its lines for a rate, which is
  a redesign. Until then, check a retune against neighbouring prices too, as tuning-data-spec
  §18 correction 10 did.
- **The guide plate flashes over the vault for one frame when the Workbench opens.** The plate
  docks clear of the Workbench (`TutorialPanel.Dock(true)`) a frame after the screen appears, so
  for that frame it sits where it was, over the vault. Cosmetic.
- **An empty labelled crate reported `BeltEmpty`: closed in Phase 2.** `GolemEntity.EmptyReasonFor`
  now reads a `FilteredBufferEndpoint` as `BufferEmpty`, so both crates say "no input in
  Crate(3,4)".

- **UI scale can't go above 1 on a 1280 x 720 or 1280 x 800 screen.** Every screen is laid out in
  fixed pixels for a 1280 x 720 canvas, so the scale is capped where it would cut a screen off
  (`menus-design.md` §4, call 6). Bigger text on the Steam Deck (`steam-deck-design.md` D3) needs
  the screens to reflow first.
- **Godot reports "7 resources still in use at exit"** at the end of every scenario run, on
  `phase6/feel` as well. Harmless so far; not yet traced.
- **No scenario flies a Zeppelin between the workshop and the Docks** (`region-design.md` §7).
  The mast and the Freight Link are unchanged and covered by `golems`; the distance is not.
- **The golem cap stays 112** with the Docks (`performance-report-endless.md` §5): the p95s hold to
  144, but single frames past 33.3 ms remain. A render pass (batched belt cargo and buildings) is
  the next gain, not more rules.
- **The `perf` scenario's lighting check swung once**: flowing-1x less unlit render CPU p95 read
  +2.86 ms on one windowed run and +0.4 ms on the next. Not reproduced, not explained.

## 4. Polish, none of it blocking

- **The art is placeholder** throughout, and the Workbench's lever housing, display face and
  card faces most of all. The City Office, the civic works' site and the Docks' gate and water are
  generated placeholders, the user's to commission.
- **The Inventory tab's order** (`crates-design.md` §4.7, §10.1.10): its headers read as words
  now ("Your pack", "Pneumatic Post", "Crate (3,-2)"), but `InventoryReadout` still sorts the
  buffers by id: crates, the pack, masts, then the Post. The spec asks for the pack
  first, then the Post, then crates in cell order.
- **The side-by-side screens** (the Crate screen beside the Pack): the transfer hint is drawn
  below the panels instead of inside them, and each panel carries its own Close button, so the
  pair shows two.
- ~~**`PriceReachTests` doesn't check the Hand-Crank Bench's recipe inputs** (or the boiler's
  hand-load).~~ Closed in Phase 3: both are on the price list (`PriceList`,
  `docs/tuning-data-spec.md` §15.2).
- **`Straining`**, the only predictive mood (9 of 12 units held): on hover only since Phase 2
  (`docs/roadmap.md`, decision 6; `onboarding-feedback-design.md` §4.4). The playtest's
  `straining` question asks whether anyone missed a warning.
- **The `hover` scenario's windowed flake: closed** (2026-10-31). Not timing: `StreetFixture`
  warped the OS cursor as well as sending its own motion event, and windowed the OS's event
  came a frame later somewhere else, putting the hover back on the last golem (it failed 5 of
  5 windowed runs, at different steps). The fixture now sends the motion event alone; `hover`
  passed 4 of 4 windowed, and `moods` windowed and both headless.
- **A first-launch hitch:** the first windowed `perf` run after a clean rebuild had one 142 ms
  frame in its first phase; the immediate rerun's worst frame was 22.8 ms. Most likely a
  first-run cost (shader or pipeline compilation, a cold .NET JIT). A player's first launch
  may stutter once. Worth a look in Phase 6 (game feel) or Phase 9; not reproduced warm.
- **The depot label picker: closed.** Built in Phase 1 on the crate screen
  (`docs/inventory-screens-design.md` §4.4), taught by step `copper-depot`.

## 5. Deliberate cuts

- **No gamepad and no Linux export yet** (roadmap decision 10). The plan to run on a Steam Deck is
  `docs/steam-deck-design.md`; it has five decisions waiting (its §2).
- No player collision; the player walks through buildings and golems.
- A belt hands off only to another belt: getting goods into a depot needs a golem.
- `Refine` stays id-routed rather than spatial, so an untyped take can't grab the wrong input.
- **Demolition refunds in full, and patents are free.** Settled design calls, not cuts; see
  `CLAUDE.md`.
