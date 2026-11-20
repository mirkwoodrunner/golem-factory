# Tuning data spec: the build record

> **History, not instructions.** `docs/tuning-data-spec.md` §16 (the build order) and §19.1–§19.2
> (the Phase 3 critique's verdicts), moved here whole on 2026-11-20. Both are finished. Section
> numbers are the spec's.

## 16. The changes, in order

Each is a data edit or a small Core change; build and run `dotnet test` after each group.

1. **Guards first** (they fail, then pass): `PriceCeilingTests` (`TheLensCostsThreeGlass` and
   `StageThreeStillBalances` fail until step 4, `ClaimPricesFollowTheirFormula` until step 5b,
   `NothingInChaptersOneToFour_WasRaised` until step 5), `PriceReachTests` widened,
   `StepGoodsReachTests`, `MarketThroughputTests` (fails until step 3),
   `ClockTowerPartialSupplyTests` (record only).
2. **Quoted prices from data** (§12.3), with no number changed: `TutorialGuideTests` and the
   `tutorial` scenario still pass.
3. **Market** (§4.3): `sandbox.json` `market` sizes, prices, delivery ticks. Update
   `SandboxSetupTests`, `TruckloadMarketTests`. Re-measure `LayoutMattersTests` and
   `ChaptersOneToFourFloorTests`; record both in §18.
4. **Lens** (§11): `recipes.json` R10 Glass 2 → 3; `clock_tower_stages.json` stage 3 Lens 24 → 12;
   `assembly_line_cards.json` `Card_R10_LensGrinding` Glass 8 → 12; `sandbox.json` Floor 5 Glass
   200 → 180; the `lens` step's recipe line; `ClockTowerStageCatalogTests`.
5. **Presser** (§12): `chassis.json` `BrassPresser` cost 60 Scrap + 10 Iron Plate + 5 Gear;
   `Card_BrassPresser` 15 Scrap + 2 Iron Plate + 1 Gear; `ChaptersOneToFourFloorTests` per-chapter
   ceilings; record the measured floor in §18 and in `docs/roadmap.md`'s Phase 2 table note.
5b. **Late prices** (§8.1): `chassis.json` Overclocker + 120 Iron Plate, Zeppelin + 150;
   `assembly_line_cards.json` their cards + 30 and + 37; `placeables.json` Freight Mast + 120,
   Pneumatic Post + 40; `sandbox.json` Floor 3 Gear 80, Floor 4 Casing 20, Floor 6 Casing 30; the
   `post`, `zeppelin` and `mast` texts and `Counts` (§12.3). `PriceReachTests` and
   `PriceCeilingTests` pass.
6. **Overclocker** (§13): `Golems/RepeatRules.cs`, the dial clamp, the writer's refusal, the load
   clamp, `RepeatRulesTests`; `Card_RepeatAssembly` prerequisite Mainspring.
7. **Floor test** (§15.4): `FloorPlayer`, `FullRunFloorTests`. Record every chapter's floor in §14
   (replacing the [M]s) and in §18.
8. **Steam constants** (§3): delete or pin `SteamNetwork.BoilerScrapCost`, `BoilerIronPlateCost`,
   `SteamPipeIronPlateCost`.
9. **The playtest report** (`docs/performance-report-template.md` §5): "Time per chapter",
   "Chapters 1–10" and "Frame rate"; the six new questions of `testscript/full-playthrough.md`
   (`PlaytestQuestion`, ids as written there); `PlaytestSessionTests`.
10. **The profile** (`docs/performance-report-template.md` §2–§5.1): `PerfSampler`,
    `EndgameLoad`, the `perf` scenario, `EndgameLoadTests`; run it and fill §6 of the template.
    The Conduit rule (§10.2) acts on its result.
11. **The feeder on the hover card** (`docs/onboarding-feedback-design.md` §17): its own tests and
    the `hover` scenario's change.
12. **Docs:** `docs/progression-design.md` §14 gets the measured numbers; `guide-chapters-design.md`
   §1's crank column and `testscript/phase-2-playtest.md`'s "about 3¾ of it is the bench" follow
   §12; `docs/open-items.md` closes the bench-price gap and the feeder item.

### 19.1 Verdicts

1. **Saves.**
   - *A Repeat above its limit* loads clamped (`RepeatRules.ClampProgram` in `SaveLoadService`,
     pinned by `ASaveHoldingRepeatTwelve_LoadsClampedToEleven`). Holds.
   - *A save from before the meter change* loads sanely: neither meter nor window was ever
     saved, only stage and progress, whose units did not change, so the old semantics leave
     nothing behind. Holds.
   - *Truckloads in flight*: **defect, fixed (59d52eb).** Neither a stall's stock nor a cart on
     the road was saved. A cart paid for before a save vanished on a fresh launch, a stocked
     stall came back empty, and in one session a cart ordered after a save still arrived on a
     load that refunded its price, a duplicator. `SaveData.market` (`MarketEntry`) now keeps each
     finite stall's stock and each cart's stall, load and ticks left, and a load replaces the
     street with it. A save without it leaves the street alone. `MarketSaveTests`.
2. **The tower meter's edge cases.** Zero supply drains at the demand; supply at or above the
   demand drains at the demand and banks to the cap (`AtThreeTimesSupply…`); a stopped line's
   window empties within a minute and the demand drains the rest, so the starved alarm still
   rises (`ALineThatStops…`); a line two stages share keeps its meter across the turn
   (`MeterFor`), and a new line raises no alarm before its first delivery. A line delivering
   in bursts, or slower than one a minute, still builds at nominal × d/s on average: the
   window's count is what the meter drains at, so a burst drains what it credited. Holds; no
   change.
3. **A patent and the Repeat limit.** **Defect, fixed (afbb4d3).** The station wrote a patent
   through `GolemProgramWriter.TryWrite` but ignored its refusal, and the Workbench patented
   any draft. A patent holding a Repeat behind no Assemble therefore charged the chassis and
   built a bare golem. `GolemConstructionStation.BlueprintRefusal` now asks
   `RepeatRules.Refusal` before anything is charged (the panel greys the row by it), and
   `WorkbenchSession.Patent` refuses a draft ENGAGE would. A patent with n above its limit
   still builds, at the limit. Every program write in Core goes through `TryWrite` (ENGAGE, the
   station, the kit, `EndgameLoad`, the hint's dry run); the save restores through its own
   path and clamps. `RepeatRulesTests`, three new cases.
4. **The feeder hover** matches `onboarding-feedback-design.md` §17.4's table: the singular and
   plural forms, "and others" at three, the trigger clause read from the core (none for a null
   core), a collecting Haul's "5/10", the bare "Waiting" head, and `BeltFull` still naming the
   target. Names come from `GolemNames.Short`; the existing `TheFeederLine_KeepsTheFontRule_AndShowsNoId`
   and the `hover` scenario check `PlayerText.RawIdIn`. Holds.
5. **Hard-coded numbers.** Prices were already the data's. Not the rest:
   - **The Ledger (bc62805):** stage 3 read "Lens @ 24" (12 since §11), the bench "25% speed"
     (50 % since the first playtest), and the Post's goods left out its Iron Plate. Fixed, and
     item 9's tests now hold every number the Ledger quotes.
   - **The guide (7327acc):** R1, R2 and R8 ("1 Coal makes 1 Coke"), the Gears step's Plate
     count, R4's "2 Scrap and 1 Coke" in two texts and the smelt-feed check and counts, a
     crate's 50, the Post's 200, stage 1's "6 a minute" twice, the heap's Slag per Coke and a
     flight's 24 ticks were literals. All correct today; each now reads its definition or
     constant, and `TheGuidesRecipesRatesAndRooms_ReadTheData_NotACopyOfIt` retunes them all.
     The smelter and carrier plans stay static, held to R4 and the heap by a test.
   - **The hover card and `StallDiagnostics` (4e44ccf):** "8 golems" and "2 golems/extractors"
     read `SteamNetwork.MaxGolemsPerBoiler` and `NodeExtractorRegistry.MaxExtractorsPerNode`.
6. **The Overclocker in the guide.** No step builds it: chapter 10's `frame-section` text names
   it, and the kit grants that step's 12 Frame Sections. `PriceCeilingTests` counts it on the
   guide's path at `frame-section`: 120 Iron Plate, within the taught rung's 150. Holds.
7. **The probe's indices.** They rebuild when the frame moves (every frame, every building
   placed or removed, every golem spawned or dismantled) and, within a frame, when any golem's
   cell, facing, mast, launch card or runnability differs. A program write, a hold or a stall
   is read live. A depot's label changes its endpoint but not the buffer the index keys on.
   A belt re-faced by a drag doesn't move the frame, but a drag re-faces only as it places the
   next cell, which does. Holds; the within-a-frame test now also covers a feeder turned and
   moved (5f80eb4).
8. **`testscript/full-playthrough.md`.** Step ids, budgets, truckloads and the Presser were
   right. Wrong: chapter 5 quoted the Hauler at 80 Plate + 40 Gear (20 + 10 since §18
   correction 10), and **none of its six questions existed** (§16 step 9). Both fixed
   (4e44ccf): the questions are in `PlaytestSession` at the script's steps. The script's
   `market` collided with chapter 3's `market`, a saved name, so the new one is `market-late`.
   Chapter 10's two and the run's wait for `done`, with the tower's and the guide's.
9. **The Ledger pinned to the data (bc62805).** `TechTreeCatalogTests` reads each chassis
   node's slots and price, the boiler, the pipe, belt, depot, mast and Post prices, each stage's
   demands in order, the bench's speed, the bays, the pack rungs and Floor Expansion.

### 19.2 Found outside the checklist

- **The Godot project did not compile (8669354).** The review (87f1356) folded
  `PerfScenario.Line` into one `FormattableString.Invariant` over concatenated interpolated
  strings, which C# types as `string`: CS1503, and `--build-solutions` failed. Scenario runs
  since then had used the last good assembly. `string.Create(InvariantCulture, …)` fixes it;
  every scenario passes on the rebuilt project.

