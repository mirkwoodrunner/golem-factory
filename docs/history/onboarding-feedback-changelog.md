# Onboarding and feedback: the change log

> **History, not instructions.** The dated corrections and critique of `docs/onboarding-feedback-design.md`,
> moved here whole on 2026-11-20 once each was written into the section it corrects. The design doc says
> what is true now. This file says when and why it changed. Section numbers are the design doc's.

## §2.1 note (2026-11-19)

The TRIGGER socket is gone (every program runs Always On), so the hint and its fallback start at
STEP 1. §2 was rewritten to match on 2026-11-20.

## 14. Corrections, as built (§3, §4 and §13; 2026-10-19)

Built: §3 and §4 in full, §11 question 5, §12.2 and §13. Not built here: §2 (the hint,
`ProgramPlan`), §9's questions and the guide texts, which come next. Where the build differs from
the text above:

1. **`GolemHoverText.Compose(golem, mood, facts, tiles, strainingHeld)`** takes the golem, its
   drawn mood, its `StallFacts` and a `TileNames`, not a `GolemHoverFacts` struct: every fact the
   struct would carry is already on the golem. `SandboxWorld.HoverCard(golem, mood)` wires it.
2. **`TileNames` is an instance** over the endpoint registry and the masts, and names a tile from
   what stands on it and its cell, never by parsing an id (a placed belt is `Belt (0,-12)` because
   a belt endpoint stands there). The Clock Tower reads "the Clock Tower".
3. **`GolemTileOverlay.Decide` takes a `carried` flag**: a carried golem's tiles hang off its
   landing, or hide while there is none.
4. **Straining's hover dwell is timed over the hold**, not the mood: an AlwaysOn golem is `Idle`
   for a tick at the end of each cycle, which restarted a mood stopwatch every cycle, so "nearly
   full" never reached the card. The hover node counts how long the fullest good has held
   `StrainingUnits` or more. The line reads `output` or `hold` for the stock that is full, since
   `FullestTypeUnits` reads both: `Working · hold 11/12 Coal, nearly full`.
5. **The hover card is on layer 42**, not 4: the guide's arrow (layer 40) covered its text in the
   frames. It is transient and never shown over a control. The pointer is read from motion
   events, as the GUI tracks it, so a scenario's pointer is the pointer hover sees.
6. **Drained** counts golems with a Haul or Extract step at the cell (§3.2 also named Load):
   `LoadIntoBuffer` on a placed golem is a Push, so it never takes.
7. **Fed**: a golem with an Assemble pushes only its recipe's output and byproduct; its Hauls
   fill its hold, not its target. The Scrap Recycler counts as a source (it gives Scrap), against
   §3.2's parenthesis.
8. **Stuck sentences at a take:** `MissingItem` where the source holds other goods reads
   `{source} has no {good}`; a collecting Haul reads `{source} is empty, needs {good} (5/10)`.
   No steam's fix lines are written per cause (`Lay a steam pipe to it from a lit boiler.`, `Fuel
   its boiler with Coke (E at the boiler).`, `Build another boiler: this one already powers 8
   golems.`).
9. **The entry shake** plays when a golem goes from not stopped to stopped, not on every stall
   event: a collecting Haul republishes its stall each time a good arrives.
10. **The alerts strip** counts a stall event from a golem the roster doesn't know, as every stall
    counted before. Its red line keeps `StallDiagnostics.Describe`'s ids (`Crate(2,-1)`).
11. **`StallTriage.Urgency` throws** for a reason it doesn't name; `StallTriageTests` walks the
    enum, so a new reason fails a test, not a frame.
12. **The [E] prompt** puts a stopped golem's problem first in its detail:
    `[E]  Program PlayerGolem-003  -  Stuck: nothing to take from at (-4,-1), behind it · faces E · [R] turn · [G] carry`.
13. **Tile arrows** are cream on the tinted tiles (the tile's tint is its self-modulate, so the
    arrow keeps its colour). A tile under a crate is covered by the crate, arrow and all, as any
    floor-level thing is.
14. **Tests** are `StallTriageTests` (triage, probe, mood, strip), `GolemHoverTextTests` (card,
    tile names, the pick, the overlay, the font) and `HaulCollectsTests`; the scenarios `moods`
    and `hover` share `Scripts/Scenarios/StreetFixture`. Not written yet: `TutorialGuideTests`'
    chapter 4 facings and the guide-text budgets (with the guide texts).

## 15. Corrections, as built (§2 and §9; 2026-10-19)

Built: §2 (`ProgramPlan`, `PlanFor`, `PerformPlan`, `WorkbenchHint`, the outlines), §9's
questions, and `guide-chapters-design.md`'s texts. Where the build differs from the text above:

1. **No `hint` scenario.** §8's checks live in the `workbench` scenario instead: its golem is
   built while the guide is on chapter 1, so it is the guide's first Scavenger and the "Program
   it" plan applies. It checks the frames on the vault card and the socket, real drags following
   them, a wrong card keeping the hint, the lever framed last, ENGAGE clearing it, the vault
   frame hiding while the card is in hand, and no frame without a drop changing the draft. §8's
   free-play case (the fallback, and nothing on a programmed golem) is pinned in
   `WorkbenchHintTests`, not on screen. `tutorial` checks the plate is clear of both frames.
2. **A drop on a filled socket now replaces its card.** §2.2 assumed it did; the session always
   did, but on screen the socketed card swallowed the drop, so a wrong card had to be dragged out
   first. A socketed `WorkbenchCardControl` now passes a drop on to its socket row.
3. **`WorkbenchHintKind.DragOut`** is its own kind (§2.2 step 3 used `VaultCard` with no card).
   `VaultCard` with no card is the fallback's socket-only frame.
4. **`Next` asks `plan.Matches` before walking**, so an order the step accepts goes straight to
   the lever, and returns `None` as soon as the committed program matches, even while the player
   edits the draft. The good picker's frame is always on `>` (`Delta` +1).
5. **`IWorkbenchDraftView.OffersCard`** answers §2.6's "the card isn't owned"; `Next` also
   returns `None` with no chassis fitted.
6. **The frames** are two `Panel`s (a `StyleBoxFlat` border) on the screen's root, laid each frame
   over the target's rect grown 4 px; the vault card's frame is clipped to the vault's view. They
   also **glow**: an 8 px shadow in the frame's colour at 0.55 of its alpha. In the frames a bare
   3 px border was lost against the brass vault at the pulse's low point, and the guide calls
   them "the glowing card and socket".
7. **Targets:** `program-carrier`'s is the newest Presser that is neither the iron Presser nor a
   coker (§2.1 said the newest Presser): looking ahead from `r4-card`, the newest Presser is the
   coker. The unloader's and the Zeppelin's `Matches` now need a logic core, so the lever is
   never framed for a program with no trigger; the programs the old checks were tested on all
   had one.
8. **`PerformPlan` writes through `GolemProgramWriter`**, as ENGAGE does (the program rewinds to
   step 0); the kit's own `Program`/`ProgramSlots` are gone.
9. **`StallFactsProbe`: a stall is never drained.** §3.2's rule counted a golem extracting from
   the stall as draining it, so chapter 4's Scavenger turned to face the Scrap stall read grey.
   A stall never accepts a push. Found by `TutorialGuideTests`' wrong-facings test.
10. **The `hint` question** is queued after `workbench`, both at `depot`. The report's first
    lines add "Chapters 1–4: mm:ss (target 15:00) · kit used: no" once `r4-card` is reached
    (`PlaytestSession.ChaptersOneToFour`).

## 16. Corrections from the Phase 2 critique (2026-10-19)

A review of `phase1/crates..7c89d53` against §0, §13, the guide spec and root `CLAUDE.md`.
Fixed, each with a regression test that failed first:

1. **An empty loop read grey forever.** Golem A hauls crate X into crate Y, golem B hauls Y into
   X, both empty: each "fed" the other, so both were `Waiting` and nothing would ever start the
   loop (invariant 4). `StallFactsProbe` now asks whether a feeder will **deliver**: a feeder
   stalled on an empty take (`BufferEmpty`, `BeltEmpty`, `MissingItem` at a Haul or Extract)
   delivers only if its own feeders do, walked depth-first with a visited set; a walk that comes
   back round to a golem already asking does not deliver. §3.2's "one red at the root, grey
   behind it" is kept: a feeder that nothing feeds still counts, so the golems behind it stay
   grey. Cost: the walk is bounded by the golems once each, per stalled golem per frame (§3.7).
   `StallTriageTests.AnEmptyLoop_OfGolemsEachWaitingOnTheOther_IsRed`.
2. **Drained ignored the good.** A golem hauling Coal from a crate full of Scrap counted as
   draining it, so the golem pushing Scrap there read grey forever. A taker now drains only if a
   take step could take a good in the pusher's push stock (an untyped Haul or an Extract may take
   anything). `AGolemHaulingAnotherGood_DoesNotDrainAFullCrate`.
3. **Player text printed ids.** The alerts strip (`[!] PlayerGolem-004 stalled: Crate(2,-1) is
   full`), the red badges (`no input in Crate(2,-1)`, `ScrapNode depleted`, `need 2 more
   IronPlate`), the hover title (`Clockwork Scavenger · PlayerGolem-001`), the Workbench header
   (`TARGET · PlayerGolem-001`), its card subtitles (`ScrapNode → ScrapBeltA`: the id-routed
   demo route, meaningless on a placed golem), the [E] prompt and the carry/turn messages.
   - **A golem's player name is `UI/GolemNames`**: `Short` = `Scavenger #4`, `Full` =
     `Clockwork Scavenger #4` (the hover title), from the chassis's short name (Scavenger,
     Presser, Hauler, Overclocker, Zeppelin) and the number in its `GolemId`. The number is the
     id's, so it is unique across chassis, survives a load and never renumbers when a golem is
     dismantled; `GolemId` stays the identity everywhere else. An id the station didn't hand
     out prints as itself.
   - **A stall's resource is named through `TileNames`** by the side of the golem it is on (the
     tile behind for an empty source, the tile in front or the bound mast for a full target),
     goods through `ItemTiers.DisplayName`, cells as `(x,y)`:
     `StallDiagnostics.PlayerResource`, `DescribeShort(golem, tiles)` for the badge and
     `Describe(golem, stall, tiles)` for the strip (`AlertsStrip.Names`, wired by
     `SandboxWorld`). Without names wired the ported id sentences are unchanged. This replaces
     §14.10's "the strip keeps `Describe`'s ids".
   - **Card subtitles say where a card acts**: `from the tile behind`, `onto the tile in
     front`, `to its Freight Mast`, `in its hold`.
   - `UI/PlayerText.RawIdIn` finds an id in drawn text (`PlayerGolem-`, `Crate(`, `…Node`,
     `…Buffer`, `…BeltA`, camel-cased names). `moods` runs it over every badge, the strip and
     the prompt; `hover` over the card; `workbench` over every visible label.
4. **Neighbouring badges overlapped.** `WorldHudLayout` stacked only labels in one column (0.9
   cells), but a red badge is several cells wide, and zoomed out taller than a row: golems two
   cells apart in a row, as every guide line stands, wore overlapping badges. A label now gives
   its drawn half-width and height (`WorldHudRequest`), and a second pass places such labels
   south first, then by owner id, each climbing by its own height while it overlaps one already
   placed: a row of red badges staggers, each still over its golem. Labels with no size are
   laid out exactly as before. `moods` checks no two drawn badges intersect.
5. **Tile arrows vanished under crates** (§14.13). A tile whose cell holds a building other than
   a belt (`GolemTileOverlay.Covered`) draws its arrow at global z 1, above the standing sprites;
   on bare floor it stays with its tile at −1.

Checked and not defects: the hint never writes the draft (`Next` reads the session and builds
`AsProgram` on a scratch `GolemProgram`; `GolemProgramWriter.TryWrite` publishes nothing). A drop
on a filled socket replaces the card in the draft only; the vault is a catalogue, not a stock,
so nothing is lost, and `workbench`'s no-orphans check passes. `PlanFor` only reads. Collect
progress is derived from the saved hold and step index, so a load mid-collect resumes it
(`HaulCollectsTests.CollectProgress_IsDerivedFromTheHold…`); a lowered batch runs on what is
held; a labelled crate gives only its good and a Haul of another stays on its step. The 2 s
dwell can't hide red: `Presented` returns `Stalled` at once and holds the previous mood only
for `Waiting`. Guide step ids are unchanged since `phase1/crates`; every step has a kit action.

Open, for the user: a feeder that sleeps on a trigger that never comes (an Interval core set
very long, a Signal nobody sends) still counts as feeding, so the golem behind it reads grey.
The probe can't know a trigger will never fire without simulating; recommended: leave it, and
have the hover card of a waiting golem name its feeder ("Waiting for Scrap · Scavenger #1 brings
it"), so the player can follow the chain.

The workbench scenario's Coke step now polls the tech-tree ledger after its deposit: the ledger
sweeps once a second, and the picker's options depended on where in that second the steps fell.

## 17.9 As built (Code agent B, 2026-10-20)

Built as §17.3–§17.7 say, with one difference and two additions:

1. **`FeedersOf` doesn't check that the stalled step is a take.** §17.3 lists it among the cases
   that answer empty, but `Read`'s `SourceFed` doesn't check it either, and §17.2's agreement
   ("non-empty exactly when the facts read `SourceGives && SourceFed`") is the invariant. So
   `FeedersOf` mirrors `IsFed` exactly, and the take check is `SandboxWorld.HoverCard`'s, which
   passes feeders only for `Waiting` with `IsTake && SourceGives && SourceFed`, as §17.4 says.
   The card reads the same.
2. `FeedersOf_TwoFeedersOneInAnEmptyLoop_NamesOnlyTheDeliveringOne` pins §17.5's row. The
   agreement test's fixtures also cover the Post and a carried feeder.
3. The probe now indexes who pushes into and takes from each store (Phase 3's profile,
   `docs/performance-report-phase3.md` §8), and `FeedersOf` reads the same indices, so the card
   and the colour still come from one search.

The `critique-hover-feeder` capture shows the card readable over the street: "Clockwork Scavenger
#2 / Waiting for Scrap · Scavenger #1 brings it when its trigger fires / From Crate (-7,1) → to
Crate (-5,1)".
