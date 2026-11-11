Folder rules for the guide, the playtest kit and playtest mode. The root `CLAUDE.md` has the project overview, commands and settled rules.
Paths here are relative to `godot/` unless they say otherwise.

# The guide

**The guide (`Core/Tutorial/TutorialGuide`) detects its steps from world state.** A new step
gets a `Done` check that reads the world, never a call from the code that does the thing. A
step that happens inside a full screen must also be placed clear of that screen's controls
(see `TutorialPanel.Dock`).

**A save records the guide's step by its `Id`** (`ProgressEntry.tutorialStepId`), not its
index: inserting steps shifts every index after them, and a finished guide used to come back
mid-chapter. So a step's `Id` is its saved name. Don't rename one without mapping the old id.

**A guide golem is named by its role, never by build order.** A step that builds a golem the
next steps talk about assigns it a role (`TutorialGuide.RoleOpenings`: a golem of the right
chassis that wasn't standing when the step began), and the save keeps the roles
(`tutorialRoles`). "The third Scavenger" broke on any spare or any golem the wrecking bar took.
The guide is restored on load **after** the golems, since a step notes who was standing.

**Marked tiles follow what the player builds** (`TutorialGuide.Layout.cs`). Every guide golem
stands on a free tile between its source (behind) and its target (in front), two apart in a
straight line, so each chapter's layout hangs off the building that commits it: the first
depot, the coal line's boiler, the smelter's pair of depots, the Slag Heap, the first belt.
Until that building exists the default layout stands, so following the marks plays exactly as
before; a blocked default moves to a free line. Each chapter plans around the tiles earlier
chapters use. Positions, facings and pipe routes (hand-drawn for the default layout, shortest
path otherwise) are computed once per change to the built world. **Marks are read live**:
`TutorialStep.Spot`/`Spots`/`SpotFacing` are functions, so never cache them. Pipe steps are
done by where the steam reaches. `TutorialGuide.Notice` says why a step waits: a building
that doesn't line up, a marked tile nothing could be planned around, a golem tile steam
doesn't reach yet, a belt pointing the wrong way. A step's `Builds` names what its marks are for.

**A new guide step needs a kit action** (`TutorialGuide.Kit.cs`, `Performs`): what the
playtest kit does to complete it, using the player's own verbs. `PlaytestKitTests` fails for
a step without one. **Chapters 1–4 have a game-time floor**: `ChaptersOneToFourFloorTests`
plays them instantly but earns every good at the player's real rates (carts and their trickle, the
bench, golem cycles), runs each step's kit action paid from those goods, and fails above 10:00
or above any chapter's ceiling (two thirds of its budget; measured 5:16 against Phase 2's
15-minute target). `FullRunFloorTests` carries the same player (`Core.Tests/Tutorial/FloorPlayer`)
through chapter 10, building reference lines from chapter 8, in about 6 s of wall time
(`docs/tuning-data-spec.md` §15.4, §18). A new step whose kit action grants goods instead of
placing or building needs a case in `FloorPlayer.Perform` (or its `Needs`).

A step that wants a card claimed names it (`TutorialStep.Card`), and the Assembly Line always
shows that card (`AssemblyLineState.Wanted`). The line otherwise offers a card only once the
factory has made every good its price asks for, so a claim is never one the player can't pay.
A step shown over the Workbench sets `TutorialStep.Workbench`. A kit action that moves the
player calls `PlayerInteractor.Teleport`; a bare `Position` write is overwritten by
`PlayerNode` on its next frame.

**A guide program step names its program once** (`TutorialStep.Program`, a `ProgramPlan`, and
  `ProgramTarget`, the golem it means). The Workbench hint walks the plan
  (`UI/WorkbenchHint`, read through `WorkbenchSession.Hint` from `TutorialGuide.PlanFor`), the
  step's Done check is `plan.Matches`, and the kit's `PerformPlan` writes it through
  `GolemProgramWriter`. Don't hand-write a program step's cards in the kit or its check. Plans
  are built per world from the loaded data (`TutorialGuide`'s `*PlanFor` builders), so a Haul
  quantity matching a recipe or a ratio (R4's inputs, the Slag Heap's `slagPerCoke`) is never a
  literal. **The plate shows a program step ahead of the guide** over the Workbench
  (`ProgramStepFor`; `docs/guide-chapters-design.md` §9): the station opens the Workbench on a new
  golem before its program step comes up. **The hint never fills a socket**: reading it changes nothing (`WorkbenchSession.Version` holds), and
  `WorkbenchScreen` only draws frames around controls that already exist. Without a plan, only a
  golem never programmed gets the fallback (TRIGGER, STEP 1, the lever).

**A quoted price is the data's** (tuning-data-spec invariant 12): a guide text or progress count
that names a price builds it from the definition (`TutorialGuide.Price`, `CostCounts`,
`OfferPrice`, `RecipeLine`), never a copy of the number.

**A guide text names a key by token, and a mouse gesture is reworded for the pad**
(`docs/steam-deck-design.md` D7). Write `[[interact]]`, `[[carry]]`, `[[inventory]]`,
`[[rotate]]`, `[[move]]`, `[[menu]]`, `[[cancel]]` or `[[guide]]`, never "E" or "Tab"; they name
the pad's buttons once it's the device in use. A step that says click, drag, hover or Shift-click
needs a phrase in `Settings/PadPhrasing`: `PadPhrasingTests` reads every step with the pad in use
and fails on the leftover word, naming the step.

# Playtest mode

**Playtest mode writes `user://playtest-report.md`**, and keeps the previous session's as
`playtest-report-<date>-<time>.md` before a new one starts (`PlaytestReportArchive`): every
launch used to overwrite it. A question is asked as the chapter after its experience opens
(`PlaytestQuestion.AfterStepId`); only the end-of-run ones (`tower`, `overclocker`,
`stage-rate`, `whole-run`, `guide`) wait for `done`. A new question needs a new id: saves store
the ids already asked, so reusing one (chapter 3's `market`) skips it. The
kit's chapter 7 Save and Load use `kit-save.json` in the save folder, which is never a slot
(`SaveSlots.KitFileName`). Scenario runs write
`user://scenario-playtest-report.md` instead, and save to `user://scenario-saves`
(`PlaytestNode.UnderScenario`, which reads `--scenario` off the command line itself: a flag
set by the ScenarioRunner arrived too late, and a kit run overwrote a player's save). Never
point a test at the real paths. **A save calls `TutorialGuide.Settle`, never `Update`**:
`Update` also runs the playtest kit, which then performed its Load step from inside the save.
