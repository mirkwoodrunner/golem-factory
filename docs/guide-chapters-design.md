# Guide chapters: texts, checks and kit actions

**Status: approved 2026-10-19, with every recommendation in §7. Built 2026-10-19 (§8 records the corrections).** Phase 2 of
`docs/roadmap.md`. It rewrites chapters 1–4 of the guide (`Core/Tutorial/TutorialGuide.cs`) around
what `docs/onboarding-feedback-design.md` builds (the Workbench hint, grey and red, hover), and
lists every step of chapters 1–10 against the crate-era game. Like the other specs, it is kept as
written once built, with corrections recorded.

## 0. Invariants

Every change to the guide keeps these (root `CLAUDE.md`; `crates-design.md` §8):

1. **A step's `Id` is its saved name** (`ProgressEntry.tutorialStepId`). No id is renamed. A
   retired step keeps its id mapped to the step that replaces it. This spec renames none and
   retires none.
2. **`Done` checks read world state**, never a call from the code that does the thing. A step a
   player did early, or another way, passes on its own. Advancing is one-way.
3. **Every step has a kit action** (`TutorialGuide.Kit.cs`, `Performs`) that completes it with the
   player's own verbs. `PlaytestKitTests` fails for a step without one.
4. **Marks are read live.** `Spot`, `Spots` and `SpotFacing` are functions; the layout follows
   what the player built (`TutorialGuide.Layout.cs`). Nothing here caches one.
5. **A step over a full screen docks clear of its controls**: `Workbench` steps in
   `TutorialPanel.Dock(true)`, `Crate` steps in the usual dock. The Workbench dock must also stay
   clear of the hint's outlines (`onboarding-feedback-design.md` §8, `tutorial`).
6. **A step that wants a card claimed names it** (`TutorialStep.Card`), and the Assembly Line
   keeps it on show (`AssemblyLineState.Wanted`).
7. **A program step names its program once** (`TutorialStep.Program`, new): the Workbench hint
   shows it, the kit performs it, and its `Matches` is the step's `Done`
   (`onboarding-feedback-design.md` §2.1).
8. **Golems are named by role, never build order** (`RoleOpenings`). No new role is needed.
9. **Text keeps the font rule**: printable Latin-1 plus → ≥ █ ░, `·` as the separator.

## 1. The target, and where the time goes

**Done when:** a new player finishes chapters 1–4 without the kit in under 15 minutes, from the
first frame to entering `r4-card` (chapter 5's first step). The playtest report's step timings
measure it (`testscript/phase-2-playtest.md`).

**Budget**, a target per chapter, not a measurement:

| Ch. | Steps | Budget | Of which cranking at the bench |
|---|---|---|---|
| 1 | 11 | 5:00 | ~1:00 (5 Coke at 2.4 s, 10 Iron Plate at 4.8 s) |
| 2 | 7 | 4:30 | ~3:45 if every Plate is cranked (40 Plate at 4.8 s, 10 Gears at 3.2 s) |
| 3 | 7 | 3:30 | ~0:30 (10 Gears; the Plate comes from the first Presser's crate) |
| 4 | 7 | 2:00 | none |

Crank times are `HandCrankRules.CrankTicks` (twice the recipe's `durationTicks`) at 10 ticks a
second. About 5¼ of the 15 minutes is cranking that no text can shorten. **If chapter 2 overruns,
the lever is the Presser's price (Phase 3's tuning), not its text.**

**Text budget:** a chapter 1–4 step's body is at most **240 characters**, so it fits the guide
plate (340 × 178) beside the counter and progress lines without the plate growing over the HUD.
`TutorialGuideTests` pins it. Chapters 5–10 keep their texts and are not held to it in this pass.

### 1.1 Which steps waste time, and what cuts them

Only two steps were timed; the rest of this list is the same patterns found in their texts.

| Step(s) | Measured | Why | Cut by |
|---|---|---|---|
| `program` | 8:00 | Three cards and three sockets in one sentence; the vault is a list; ENGAGE unmentioned on screen | The hint: one glowing card and socket at a time, then the lever. The text says "drag the glowing card" and stops naming sockets |
| `turn` | 4:30 | "Stand next to it and press R": R turns the *nearest* golem, and chapter 4's Scavenger stands diagonal to the first, so R can turn the wrong one. Facing shows as a small arrow on the body | Turn while carrying: R then turns only the carried golem (root `CLAUDE.md`), and the carry outline's teal and gold tiles, now with arrows, show the result before G sets it down |
| `work`, `work-presser`, `work-coker` | not timed | The same carry-and-R pattern, and "faces the depot" asks the player to read the body's arrow | The same: one sentence teaches "teal behind, gold in front" in `work`, and later steps say only which tile goes teal and which gold |
| `stall`, `unstall` | badge read "vaguely" | A red badge with a golem id and a short reason | Hover: the card names the tile, the good and the fix. The text says to hover |
| `program-presser`, `program-coker`, `stamp` | not timed | Each repeats a card list | The hint; the text lists the cards only as a fallback for the eye |
| `gears`, `presser`, `presser2` | not timed | Cranking (§1) | Nothing in text; Phase 3 |

## 2. Chapters 1–4, rewritten

Columns: the step's **id** (unchanged), its **title and body** as the player reads them, its
**Done** check, its **marks** (`spot`/`spots`/`SpotFacing`, target arrow), its **flags**
(`Card`, `Workbench`, `Crate`, `Program`, `menuKey`) and its **kit** action. Where a column says
"unchanged", the code in `TutorialGuide.cs` / `TutorialGuide.Kit.cs` stays as it is.

### Chapter 1: The first golem

| Id | Title · body | Done | Marks · target | Flags | Kit |
|---|---|---|---|---|---|
| `scrap` | **Gather Scrap** · "Walk with WASD to the Scrap stall the arrow points at. Press E to take Scrap; hold E to keep taking. Scrap pays for almost everything at first." | pack Scrap ≥ 20 | target the Scrap stall | | grant 20 Scrap |
| `coal` | **Buy Coal** · "The Coal stall is empty. Press E there to order a truckload (10 Scrap). When the cart arrives, press E again to take Coal." | pack Coal ≥ 5 | target Coal stall, or Scrap stall while short and nothing in transit | | grant 5 Coal |
| `coke` | **Make Coke** · "At the Hand-Crank Bench, hold E to crank R1 Coking: 1 Coal makes 1 Coke. Boilers burn Coke." | pack Coke ≥ 5 | target the bench | | grant 5 Coke |
| `iron` | **Make Iron Plate** · "Press R at the bench to switch it to R2 Scrap Reclamation (1 Scrap makes 1 Iron Plate), then hold E. Holding a building? Escape puts it down first." | pack Iron Plate ≥ 10 | bench (Scrap stall while out of Scrap) | | grant 10 Iron Plate |
| `boiler` | **Build a Boiler** · "Golems run on steam. Click Boiler in the build menu (30 Scrap + 10 Iron Plate), click the marked tile beside the Scrap stall, then press Escape." | unchanged (a boiler on `BoilerSpot`, or the first golem's tile steamable) | spot `BoilerSpot`, exact | `menuKey` Boiler | unchanged (`PlaceGranted`) |
| `fuel` | **Fuel the Boiler** · "Stand at the boiler and press E to load your Coke. No Coke, no steam." | a boiler holds Coke | target the boiler | | unchanged |
| `golem` | **Build a Golem** · "At the construction station, press E and pick the Clockwork Scavenger (12 Scrap). The Workbench opens on it." | a golem stands | target the station | | unchanged (`BuildGolem`) |
| `program` | **Program it** · "Drag the glowing card onto the glowing socket, then the next: Always On, Extract, Push Output. Then pull ENGAGE GEARS. Closed it? Press E at the golem." | `Program.Matches` on any golem (was `IsProgrammed`, the same check) | target the first golem | `Workbench`; `Program` §3 | `PerformPlan` |
| `depot` | **Build a Depot** · "A golem takes from the tile behind it and gives to the tile in front. Build a Depot (15 Scrap) on the marked tile: a crate of 50 of each good. Misplaced? X takes it back, full refund." | unchanged | spot `DepotSpot`, exact | `menuKey` Depot | unchanged |
| `work` | **Put it to work** · "Press G by the golem to lift it. The outline under you is where it lands: teal is behind it, gold in front. Stand on the marked tile, press R until gold is on the depot, then G." | unchanged (`_cycleSeen`) | spot `GolemSpot` + `GolemFacing` | | unchanged (`SetPlacement`) |
| `collect` | **Collect from the crate** · "Your golem fills the depot's crate, not your pack. Stand at the depot and press E: you take what fits, the rest stays. Hover any golem to see its teal and gold tiles." | unchanged (`TotalWithdrawn(Scrap) > 0`) | target the first depot | | unchanged (`TryCollect`) |

### Chapter 2: The first Presser

| Id | Title · body | Done | Marks · target | Flags | Kit |
|---|---|---|---|---|---|
| `gears` | **Cut Gears** · "The Brass Presser crafts, and costs 10 Gears. Press R at the bench until it shows R8 Gear Cutting (2 Iron Plate make 1 Gear), then hold E. Short of Plate? Back to R2." | pack Gear ≥ 10 | bench | | grant 10 Gear |
| `claim` | **Claim Scrap Reclamation** · "A golem can only use cards you own. Press Tab, open the Assembly Line and click Claim on Assemble Scrap Reclamation (4 Scrap): Scrap in, Iron Plate out." | claimed `AssembleScrapReclamation` | none | `Card` | unchanged (`Claim`) |
| `presser` | **Build a Brass Presser** · "At the station, build the Brass Presser (60 Scrap + 20 Iron Plate + 10 Gear). Short of Scrap? Collect from your Scavenger's crate. Program it now if you like: the cards glow." | a Presser stands | station (Scrap stall while short) | | unchanged |
| `pipe` | **Lay a steam pipe** · "The Presser will stand two tiles from the boiler, out of its reach. Lay Steam Pipe (1 Iron Plate each) on the marked tiles: steam reaches any golem beside a pipe." | unchanged (steam reaches `PresserSpot`) | spots `PipeSpots` | `menuKey` Steam Pipe | unchanged |
| `depot2` | **A depot for its output** · "Build a second Depot on the marked tile. The Presser will take Scrap from the first crate, behind it, and push Iron Plate into this one." | unchanged | spot `PresserDepotSpot`, exact | `menuKey` Depot | unchanged |
| `program-presser` | **Program the Presser** · "Drag the glowing cards: Always On, Haul, Assemble Scrap Reclamation, Push Output. Then ENGAGE. Closed it? Press E at the Presser." | `Program.Matches` (was `IsIronPresser`) | target the first Presser | `Workbench`; `Program` | `PerformPlan` |
| `work-presser` | **Make Iron Plate** · "Lift the Presser (G) onto the marked tile between the depots: teal on the first, gold on the new one (R turns it), then G. Grey means it is waiting for Scrap. That's fine." | unchanged (a Presser cycled since entry) | spot `PresserSpot` + facing | | unchanged |

### Chapter 3: The Coke line

| Id | Title · body | Done | Marks · target | Flags | Kit |
|---|---|---|---|---|---|
| `coking-card` | **Claim Coking** · "Your boiler burns Coke you crank, and it will run dry. Press Tab, open the Assembly Line and claim Assemble Coking. It costs Coal: short? Take some at the Coal stall." | claimed `AssembleCoking` | Coal stall while short | `Card` | unchanged |
| `presser2` | **A second Presser** · "Build another Brass Presser (60 Scrap + 20 Iron Plate + 10 Gear). Take Plate from the first Presser's crate (E); crank the Gears (R8). A full crate turns its golem red." | two Pressers stand | station | | unchanged |
| `boiler2` | **A boiler for the coal line** · "Build a second Boiler on the marked tile, two out from the Coal stall. The coking Presser will stand between them and push Coke into it." | unchanged | spot `Boiler2Spot`, exact | `menuKey` Boiler | unchanged |
| `pipes2` | **Join the boilers** · "Lay Steam Pipe on the marked tiles, from your first boiler to the new one. The first powers the coking Presser until the new one has Coke. Your own route counts if it does both." | unchanged (`FirstBoilerPowersCokerAndJoins`) | spots `Pipe2Spots` | `menuKey` Steam Pipe | unchanged |
| `coal-order` | **Stock the Coal stall** · "Golems take from stalls as you do. Press E at the empty Coal stall to order a truckload (10 Scrap for 40 Coal)." | the Coal stall holds Coal | Coal stall | | unchanged (`Deliver(40)`) |
| `program-coker` | **Program the coking Presser** · "Drag the glowing cards: Always On, Extract, Assemble Coking, Push Output, then ENGAGE. Extract takes whatever the stall behind holds: here, Coal." | `Program.Matches` (was `IsCoker`) | the newest Presser | `Workbench`; `Program` | `PerformPlan` |
| `work-coker` | **Feed the boiler** · "Lift the coking Presser onto the marked tile: teal on the Coal stall, gold on the new boiler. Boilers dry? Load Coke into the new one with E." | unchanged | spot `CokerSpot` + facing; the new boiler while dry | | unchanged |

### Chapter 4: Copies, turning and stalls

| Id | Title · body | Done | Marks · target | Flags | Kit |
|---|---|---|---|---|---|
| `patent` | **Patent a program** · "A patent saves a program to reuse. Press E at your first Scavenger and press PATENT in the Workbench." | a patent exists | the first golem | `Workbench` | unchanged |
| `scav2` | **Build one from the patent** · "At the station, under From a patent, click your patent: a Scavenger (12 Scrap) arrives already programmed." | the `scav2` role has a golem | station | | unchanged (`TryConstructFromBlueprint`) |
| `stamp` | **Program the new Scavenger** · "This one arrived bare. Drag the glowing cards (Always On, Extract, Push Output) and pull ENGAGE. Next time, build from the patent." | `Program.Matches` on the `scav2` golem | that golem | `Workbench`; `Program` | `PerformPlan` |
| `depot3` | **A depot beside the stall** · "Build a Depot on the marked tile, two out from another side of the Scrap stall. Leave the tile between empty: the new Scavenger stands there." | unchanged | spot `Depot3Spot`, exact | `menuKey` Depot | unchanged |
| `turn` | **Turn it with R** · "Lift the new Scavenger (G) and stand on the marked tile. While you carry it, R turns it: turn until teal is on the stall and gold on the depot, then set it down (G)." | unchanged (the `scav2` golem cycled since entry) | spot `Scav2Spot` + facing | | unchanged |
| `stall` | **Stall it on purpose** · "Golems never improvise. Stand by the new Scavenger and press R to turn it away. It stops and turns red. Hover it: it names the tile and what is wrong." | unchanged (the `scav2` golem is `Stalled`) | that golem | | unchanged |
| `unstall` | **And back to work** · "Turn it back (R) until teal is on the stall. A stuck golem retries the same step, and starts again the moment its tile is right." | unchanged | spot `Scav2Spot` + facing | | unchanged |

**What the texts teach, in order:** E takes and collects (1), the build menu and Escape (1),
behind and in front (1, `depot`), teal and gold with R while carrying (1, `work`), hover (1,
`collect`), the glowing cards (1, `program`), grey is fine (2, `work-presser`), red needs you
(3, `presser2`; 4, `stall`), hover names the fix (4, `stall`).

**`stall` reads red in every wrong facing.** Turned a quarter either way or half round, chapter
4's Scavenger stalls on `NoSourceAtTile`, on its boiler behind it (`BeltEmpty` from a source that
never gives), or on a stall in front that takes nothing (`BeltFull`, not drained): all stuck
under `onboarding-feedback-design.md` §3.3. `TutorialGuideTests` pins all three, so the lesson
can never show grey. The kit's quarter turn back is unchanged.

**`stamp`'s text is unchanged in substance** and still passes at once when the golem came from
the patent.

## 3. The program plans

Each of the nine program steps carries a `ProgramPlan` (`onboarding-feedback-design.md` §2.1).
The cards, goods and batch sizes are exactly what the kit passes today; the targets are the
golems the kit programs today.

| Step | Ch. | Core | Slots (card, good, batch) | `ProgramTarget` | `Matches` (was) |
|---|---|---|---|---|---|
| `program` | 1 | Always On | Extract · Push Output | the `first` role, else the first Scavenger | `IsProgrammed` |
| `program-presser` | 2 | Always On | Haul · Assemble Scrap Reclamation · Push Output | the first Presser | `IsIronPresser` |
| `program-coker` | 3 | Always On | Extract · Assemble Coking · Push Output | the newest Presser that isn't the iron Presser | `IsCoker` |
| `stamp` | 4 | Always On | Extract · Push Output | the `scav2` role | `IsProgrammed` |
| `program-smelter` | 5 | Always On | Haul (Scrap, 2) · Haul (Coke, 1) · Assemble Iron Smelting · Push Output | the first Aether-Hauler | `IsSmelter` (either Haul order) |
| `program-carrier` | 5 | Always On | Haul (Slag, 4) · Haul (Coke, 1) · Push Output | the newest Presser | `IsCarrier` |
| `scav3` | 6 | Always On | Extract · Push Output | the `extractor` role | `IsProgrammed` |
| `unloader` | 6 | Always On | Haul (Copper Ore, 1) · Push Output | the `unloader` role | its Haul of Copper Ore and a Push |
| `program-zeppelin` | 9 | Always On | Haul (Copper Ore, 1) · Freight Launch | the Zeppelin | its Haul of Copper Ore and Freight Launch |

"Extract" is the `ExtractScrap` card, "Haul" `HaulScrap`, as the Workbench labels them
(`AppendageActionDefinition.Label`). `program-coker`'s target narrows the kit's `NewestPresser`
so the hint can't land on the iron Presser if the player builds the coker first; the kit's
behaviour is unchanged, since in a kit run the newest Presser is the coker.

## 4. Chapters 5–10

Texts are kept unless wrong after Phase 1 or against Phase 2; none is. The only changes are the
plans on four program steps (§3), which add the hint without touching their text. Done, marks,
flags and kit are as built (`TutorialGuide.cs`, `TutorialGuide.Kit.cs`, `crates-design.md` §8).

| Id | Ch. | Text | Done | Marks · target | Flags | Kit |
|---|---|---|---|---|---|---|
| `r4-card` | 5 | kept | claimed Iron Smelting | none | `Card` | `Claim` |
| `hauler` | 5 | kept | an Aether-Hauler stands | station | | `BuildGolem` |
| `smelt-depots` | 5 | kept | depots on `SmeltIn` and `SmeltOut` | spots both, exact | `menuKey` Depot | `PlaceGranted` ×2 |
| `pipes3` | 5 | kept | steam reaches the smelter's and carrier's tiles | spots `Pipe3Spots` | `menuKey` Steam Pipe | `PlaceGranted` each |
| `program-smelter` | 5 | kept | `Program.Matches` | the Hauler | `Workbench`; `Program` | `PerformPlan` |
| `smelt-feed` | 5 | kept | `SmeltIn` crate ≥ 2 Scrap and ≥ 1 Coke, or the smelter cycled | `SmeltIn` | `Crate` | grant, `CrateTransfer.Put` 40 Scrap, 20 Coke |
| `work-smelter` | 5 | kept | the smelter cycled since entry | spot `SmelterSpot` + facing | | `SetPlacement` |
| `slag-heap` | 5 | kept | a heap on `SlagHeapSpot` | spot, exact | `menuKey` Slag Heap | `PlaceGranted` |
| `carrier` | 5 | kept | three Pressers stand | station | | `BuildGolem` |
| `program-carrier` | 5 | kept | `Program.Matches` | newest Presser | `Workbench`; `Program` | `PerformPlan` |
| `carrier-feed` | 5 | kept | `SmeltOut` crate ≥ 1 Coke, or the carrier cycled | `SmeltOut` | `Crate` | `Put` 10 Coke |
| `work-carrier` | 5 | kept | a heap has voided Slag | spot `CarrierSpot` + facing | | `SetPlacement`, `Put` 8 Slag, 4 Coke |
| `copper` | 6 | kept | pack Copper Ore ≥ 1 | Copper stall | | `Deliver(40)`, grant 1 |
| `belts` | 6 | kept | the run laid, pointing `CopperFacing` | spots `BeltSpots` | `menuKey` Belt | `PlaceGranted` each |
| `pipes4` | 6 | kept | steam reaches the extractor's and unloader's tiles | spots `Pipe4Spots` | `menuKey` Steam Pipe | `PlaceGranted` each |
| `scav3` | 6 | kept | `Program.Matches` on the `extractor` role | station | `Workbench`; `Program` | `BuildGolem`, `PerformPlan` |
| `work-extractor` | 6 | kept | goods on the belt run | spot `ExtractorSpot` + facing | | `SetPlacement` |
| `copper-depot` | 6 | kept | depot on `CopperDepotSpot` labelled Copper Ore | spot, exact | `menuKey` Depot; `Crate` | `PlaceGranted`, `SetFilter` |
| `unloader` | 6 | kept | `Program.Matches` on the `unloader` role | station | `Workbench`; `Program` | `BuildGolem`, `PerformPlan` |
| `work-unloader` | 6 | kept | the unloader cycled since entry | spot `UnloaderSpot` + facing | | `SetPlacement` |
| `expand` | 7 | kept | the floor extended | none | | grant cost, `ExtendFloor` |
| `bays` | 7 | kept | bay tier ≥ 2 | none | | grant cost, `UpgradeBays` |
| `pack` | 7 | kept | pack tier ≥ 1 | none | | grant cost, `UpgradePack` |
| `ledger` | 7 | kept | a Ledger node selected | none | | `Select("r2.reclamation")` |
| `save` | 7 | kept | a save made | none | | `SaveTo(kit-save.json)` |
| `load` | 7 | kept | a load made | none | | `LoadFrom(kit-save.json)` |
| `copper-ingot` | 8 | kept | the Ledger has Copper Ingot | none | `Card`; `Workbench` | `MakeGood` |
| `zinc-ingot` | 8 | kept | … Zinc Ingot | Zinc stall | `Card`; `Workbench` | `MakeGood` |
| `brass` | 8 | kept | … Brass | none | `Card`; `Workbench` | `MakeGood` |
| `casing` | 8 | kept | … Casing | none | `Card`; `Workbench` | `MakeGood` |
| `post` | 8 | kept | two Post crates stand | none | `menuKey` Pneumatic Post | `PlaceGranted` ×2 on `PostSpots` |
| `glass` | 8 | kept | … Glass | none | `Card`; `Workbench` | `MakeGood` |
| `lens` | 8 | kept | … Lens | none | `Card`; `Workbench` | `MakeGood` |
| `mainspring` | 8 | kept | … Mainspring | none | `Card`; `Workbench` | `MakeGood` |
| `aether-cell` | 8 | kept | … Aether Cell | Aether stall | `Card`; `Workbench` | `MakeGood` |
| `zeppelin-card` | 9 | kept | claimed the Zeppelin chassis | none | `Card` | `Claim` |
| `freight-card` | 9 | kept | claimed Freight Launch | none | `Card` | `Claim` |
| `zeppelin` | 9 | kept | a Zeppelin stands | station | | `BuildGolem` |
| `mast` | 9 | kept | a Freight Mast stands | spot `MastSpot` | `menuKey` Freight Mast | `PlaceGranted` |
| `zeppelin-pipe` | 9 | kept | steam reaches `ZeppelinSpot` | spots `ZeppelinPipeSpots` | `menuKey` Steam Pipe | `PlaceGranted` each |
| `program-zeppelin` | 9 | kept | `Program.Matches` | none | `Workbench`; `Program` | `PerformPlan` |
| `launch` | 9 | kept | the Zeppelin cycled since entry | spot `ZeppelinSpot` + facing | | `SetPlacement` |
| `mast-collect` | 9 | kept | a mast's crate gave Copper Ore | the mast | | `TryCollect`, retried |
| `tower-visit` | 10 | kept | the player is in the town square | the tower | | `Teleport` |
| `frame-section` | 10 | kept | … Frame Section | none | `Card`; `Workbench` | `MakeGood` |
| `tower-feed` | 10 | kept | the tower's Frame Section rate > 0 | the tower | | a push into the tower's endpoint |
| `stage1` | 10 | kept | stage 1 laid | the tower | | `RestoreProgress(1, …)` |
| `done` | 10 | kept | never | none | | none (the kit stops here) |

Chapter 8's goal steps carry `Workbench` but no plan: there the program is the player's to
design, and the hint's fallback doesn't apply to a programmed golem.

Two texts in chapters 5–10 mention a golem stopping, and both stay true under grey and red:
`work-smelter` ("a full Slag slot stops it": red until the carrier drains the crate, then grey)
and `smelt-feed` (a smelter behind a crate only the player fills is red when it empties, which is
the point of the step).

## 5. Steps to add or retire

**None.** Considered and left out:

- **A "hover to read why" step** after `stall`. Its `Done` would read `SandboxWorld.Inspector`,
  which is allowed (the `ledger` step reads `LedgerReadout.HasSelection`), but it adds a step to a
  chapter the target is already tight on. `stall`'s text asks for the hover instead, and the
  `hover` question measures whether players did.
- **Moving `program` after `depot`.** The station opens the Workbench on the new golem at once,
  so programming first saves a walk back. Kept.
- **Retiring `stamp`.** It already passes at once for a golem built from the patent, and shows
  only when a player built one bare. Kept.

`ChapterStarts` is unchanged.

## 6. Tests

- `TutorialGuideTests`: every chapter 1–4 body is at most 240 characters and within the font's
  character set; chapter 4's three wrong facings read `Stalled`.
- `PlaytestKitTests`: unchanged in shape (every step has a kit action; F9 per chapter from a fresh
  game), plus §3's plan checks (`onboarding-feedback-design.md` §7).
- The `tutorial` and `playtest-kit` scenarios keep passing; `tutorial` adds the hint-outline
  clearance.

## 7. Questions, decided

**Decided 2026-10-19: the user took every recommendation below.**

1. **The 15-minute target against ~5¼ minutes of cranking.** The texts can't cut it.
   **Recommendation:** run the Phase 2 playtest as specified; if chapter 2 overruns its 4:30,
   carry the Presser's price into Phase 3's tuning rather than shortening the guide.
2. **The 240-character budget** shortens most chapter 1–4 texts by a third. **Recommendation:
   keep it**; the hint and hover now carry what the long sentences tried to.
3. **`turn` teaches R while carrying, not R beside a set-down golem.** Both still work.
   **Recommendation:** teach carrying first, since it is unambiguous; `stall` and `unstall` then
   use R beside the golem, where it is the only one close.

## 8. Corrections, as built (2026-10-19)

Chapters 1–4's texts are as §2 writes them, every one within 240 characters
(`TutorialGuideTests`). No step was added, retired or renamed; chapters 5–10's texts are kept.

1. **`program-carrier`'s target** is the newest Presser that is neither the iron Presser nor a
   coker, not just the newest Presser: `PlanFor` looks ahead from `r4-card`, where the newest
   Presser is the coker.
2. **The unloader's and the Zeppelin's `Matches` need a logic core**, as every other plan's do.
3. **`stall` reads red in every wrong facing only after a fix**: facing the Scrap stall read grey,
   because the first Scavenger extracts from it and the probe counted that as draining it. A
   stall is never drained now (`onboarding-feedback-design.md` §15).
4. **The kit's program steps all call `PerformPlan`**, which writes through
   `GolemProgramWriter`; the kit's behaviour is otherwise unchanged.

**Critique (2026-10-19):** reviewed with `onboarding-feedback-design.md` §16. No guide defect:
step ids are unchanged since `phase1/crates`, every step has a kit action, the program steps'
Done checks are their plans' `Matches` over the golem's committed program, and `PlanFor` only
reads. Golems the guide's texts talk about now print as `Scavenger #4` on every readout
(`UI/GolemNames`); the texts themselves name roles, not numbers, and are unchanged.
