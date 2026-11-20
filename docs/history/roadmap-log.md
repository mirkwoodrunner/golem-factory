# Roadmap: the phase log

> **History, not instructions.** The "Next session: start here" section of `docs/roadmap.md` as it stood on
> 2026-11-20, moved here whole when that section was cut to the current state. Each phase's record, test runs
> and the user's calls in order. The art review rules it held are also in `art-pipeline-design.md` §3.3.


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

**The design pass of 2026-11-19 (the user's calls), on `claude/vibrant-hypatia-ekqgk7`:** the
board-game origin is gone from every doc and comment; then six changes, each with its tests,
scenarios and docs. (1) Unlocks are picked directly at a fixed price, from the Assembly Line tab or
a Ledger plaque: no draft slots, queue or decay (`progression-design.md` §8). (2) The Workbench has
no trigger socket; every program runs Always On. (3) The golem cap is a fixed 112, with no bay
upgrades. (4) Contracts and golem orders pay city vouchers, each the next step of any upgrade or
unlock (`contracts-design.md` §4). (5) The Presser costs 50 Scrap + 3 Iron Plate + 2 Gear: chapters
1–4 measure 4:22 (3:15 at the bench), chapter 2 halved (`tuning-data-spec.md` §12.2). (6) A cart
refunds in full when removed or changed. Every Core test and scenario passes, run here on Linux.
Open questions for play: what vouchers buy once every upgrade is maxed in endless play (nothing,
today), and whether chapters 1 and 3 still crank too long (the boiler's 10 Plate and the Coal
cart's first rung are the next levers).

**Resume here:**
- **Selling golems to the city is built** (2026-11-08, `golem-sales-design.md`), on branch
  `depth/golem-sales` from `main` (Phases 7 and 8 and the Docks went in as PR #62): golem orders
  at the City Office from chapter 5, sold by hand (carry, E) or built and shipped by a Golem
  Foundry, and the `golem-sales` scenario. PR #63.
- **The Steam Deck has begun** (`steam-deck-design.md`), merged with #66: D1's Linux export,
  self-contained .NET 10, one command (`Tools/Deck/export-linux.ps1`), and scenarios passing on an
  exported build (§7's export log). D4, the gamepad's model, is built too: every verb on the pad,
  a right-stick build cursor, and the `gamepad` scenario. D5.2, the hover card at the pad's
  cursor, and D6's screens by pad (the Workbench, the station's panel, the Crate and Pack screens,
  the playtest card, Management, merged with #67; the title, the cart panel and the City Office on
  `deck/screens-rest`, with D7: text names the pad's buttons by the Deck's labels). Next: the
  owner launches it on the Deck (D1.5, D1.6, then D2's Steam Input template) and tries the
  playtest script with no mouse or keyboard (D6's exit), and runs `export-linux.ps1 -DeckHost
  steamdeck -RunPerf` for D8's numbers. D8's machine-side work is done: the frame cap, the frame
  watch, ReadyToRun, and the screens' rows updated in place rather than rebuilt on every open
  and every change. Still to run by a person: the playtests
  below and `testscript/endless-soak.md`.

0. **Phase 6 (Game feel), on branch `phase6/feel` (from `phase5/audio`), built 2026-11-01.** The
   spec is `docs/game-feel-design.md`; the user's four decisions are its §7 (ComfyUI FX art,
   click the alert strip, fixed zoom levels, the tower's event skippable with the clock running).
   Everything plays. The S7 FX art is installed. **Done** (2026-11-05, the user is happy with the feel).
   **Phase 7 (`docs/menus-design.md`), branch `phase7/menus`, done:** step 1 (settings) is in
   (`Core/Settings`, `GameSettingsStore`, the Settings tab, the `settings` scenario; the calls
   made are `menus-design.md` §4). Step 2 (accessibility) is in too: Reduced motion
   (`MotionPreference`) and the colour-blind stripes (`ColourMarks`), `menus-design.md` §5, the
   `accessibility` scenario. Step 3 (the title screen) is in: the game opens on Continue, New
   game, Settings and Quit over the guide's six-chapter workshop, kept running (`Core/Menus`,
   `TitleScreen`, `menus-design.md` §6, the `title` scenario). Step 4 (save slots) is in: five
   slots and an Autosave with pictures and summaries, autosave on quit and on a tower stage, Load
   on the title, Quit to title (`SaveSlots`, `SaveSlotBoard`, `GameSaves`, `menus-design.md` §7,
   the `slots` scenario). Step 5 (the factory overview) is in: a Management tab with the map,
   the production graph and the alerts history (`Core/Overview`, `OverviewTab`,
   `menus-design.md` §8, the `overview` scenario). Step 6 (Creative Mode) is in: a Creative
   world from the title, everything unlocked and free, no guide or tower stage, its own save
   slots, refused as a campaign load (`SandboxWorld.Creative`, `CostsWaived`, `menus-design.md`
   §9, the `creative` scenario). **All six steps of Phase 7 are in.**
   **Phase 5 (Audio), on branch `phase5/audio` (from `phase4/art`), is done** (2026-11-01; the
   user approved the listening sheet). The spec is `docs/audio-design.md`; all four decisions are
   made (§9): CC0 recordings for the effects and ambience, synthesized music. Every slot plays at
   its trigger (`Core/Audio`, `Scripts/Audio/AudioNode`), the Settings tab has the sliders, the
   `audio` scenario and `verify_audio.py` pass. To change a sound: swap it in
   `Tools/Audio/audio_manifest.json`, rebuild (`build_cc0.py` / `synth_music.py`), rerun
   `verify_audio.py` and the sheet. Phase 6 (Game feel) starts at the user's go. Phases 1–4 are
   pushed as PR #60 (the user's call, 2026-10-31); `phase5/audio` is not pushed.
1. **Phase 4 (Art), on branch `phase4/art`, is done** (2026-10-31; the record follows). **All five S1 chassis are in the game** (the Brass
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
     game's half-size view. Today's floor, walls and props stay.
   - **Lighting (L) done** (2026-10-28, `art-pipeline-design.md` §6.5), approved by the user:
     a warm ambient, banded sconce pools and boiler glows, every value in
     `Core/World/LightingRules`; it costs 0.27 ms of render CPU at the endgame load (budget 1.0).
     The hot pipes landed 2026-10-29 (approved): a pipe glows while a fuelled boiler feeds it,
     and goes cold with a dry boiler's firebox.
   - **Buildings (S3) done** (2026-10-29, §9 decision 19): the depot, station, Post and mast
     redrawn in ComfyUI toward the boiler's look; the recycler and slag heap kept (muddy at play
     size), as were the boiler, bench, belts and pipes.
   - **The Workbench (S4) done** (2026-10-30, §9 decision 20): the lever housing, display face
     and card faces script-drawn by the generator.
   - **Goods (S5) done** (2026-10-30, §9 decision 21): 11 goods redrawn by the generator so all
     24 pass checks 7 and 11 at 24 px.
   - **The HUD (S6) done** (2026-10-31, §9 decisions 22 and 23): the seven Steampunk pieces
     in use script-drawn under `UI/Hud/` by `generate_hud_art.py`, the pack and its licence
     note removed; the locked tech-tree plaque's bar dropped; the rest of the tech tree chrome
     and the overlays kept. Every set is now through (§3).
   - The windowed `hover` flake fixed (2026-10-31, `open-items.md` §4).
   - **Phase 4's test run passed** (2026-10-31): the Godot project builds from clean with no
     errors; all 1991 Core tests pass; all 17 scenarios pass headless; `verify_art.py` exits 0
     and `check_art.py` reports 0 FAIL, 0 WARN. The done-when list (`art-pipeline-design.md` §8)
     holds; items 3, 5, 6 and 7 were reworded to the user's decisions (the Artificer kept, no
     `review.json` past S1, kept sets stay `legacy`, the lighting's cost measured when it landed).
     **Phase 4 is done.** Phase 5 (Audio) starts at the user's go.
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

Standing rules: tests and scenarios never write the player's saves (`user://saves`); a renamed step `Id`
or definition name breaks saves.
