# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

`golem-factory` is a solo-play **Godot 4** prototype of "Golem Factory: The Clockwork Metropolis" -- a
Factorio/Satisfactory-style automation game (cozy top-down pixel art) where the player places
"golems" programmed with punch-card-style Logic Core / Appendage / Chassis combinations that run
rigidly on a world tick clock. It began as a tabletop board-game design, became a Unity
prototype, and was ported to Godot (milestones G0-G10, `docs/godot-conversion-plan.md`). **The
Unity project was removed at the G10 cutover; the tag `unity-final` holds its last state.**

The game lives in `godot/`. Paths in the engine sections below are relative to `godot/`
unless they say otherwise.

Read the docs before making design calls, not just the code:

- `docs/game-design.md` -- the original tabletop concept. Source of truth for *mechanics*.
- `docs/digital-design.md` -- visual style, the golem roster, and the Workbench spec.
- `docs/progression-design.md` -- the tech tree, steam scarcity, chassis unlocks and the Clock
  Tower endgame.
- `docs/cozy-automation-design.md` -- smart depots, golem moods, loop labels, the Scrap Recycler.
  Its §0 lists eight invariants a pass touching golems, endpoints or text must not break.
- `docs/godot-conversion-plan.md` -- the port's milestone log, and **"Next session: start
  here"**, the current state and what's next. Read it before planning any work.
- `docs/open-items.md` -- the backlog: open decisions, known gaps, deliberate scope cuts.
- `docs/godot-spike.md` -- what the port measured before it began.
- `docs/history/` -- the Unity era: `unity-implementation-plan.md` (milestones M0-M9 and every
  Unity pass, with the reasons behind much of what Core still does), the Unity-to-Godot test
  ledger, and the MCP-for-Unity setup guide. History, not instructions.
- `testscript/phase-1-playtest.md` -- how to run a playtest; the guide asks the questions.

## Toolchain

- **Godot 4.7.2 mono** at `G:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\`.
  The folder name is doubled. Use `Godot_v4.7.2-stable_mono_win64_console.exe` for anything
  run from a shell.
- **.NET SDK 10**. Every project targets `net10.0`. GodotSharp 4.7.2 is built for net8.0, but
  only the .NET 10 runtime is installed, and net10 can reference net8.
- `nuget.config` points at the Godot install's bundled packages, so `dotnet build` works
  without opening the editor first.

## Layout

| Project | What it is |
|---|---|
| `Core/GolemFactory.Core.csproj` | All game rules. **No engine reference**: the compiler refuses a Godot type here. |
| `Core.Tests/GolemFactory.Core.Tests.csproj` | NUnit 3, ported from Unity's EditMode suite. NUnit 3, not 4, so the classic `Assert.AreEqual` form works unchanged. |
| `GolemFactory.Godot.csproj` (+ `Scripts/`, `Scenes/`, `art/`) | Nodes and scenes only. Excludes `Core/**` and `Core.Tests/**` from its compile glob. |

`Core/` and `Core.Tests/` each hold a `.gdignore` so the editor doesn't import .NET build
output as resources.

## Commands

```sh
dotnet test godot/GolemFactory.sln            # Core tests: ~0.6 s, no engine involved

G=".../Godot_v4.7.2-stable_mono_win64_console.exe"
"$G" --headless --path godot --build-solutions --quit             # compile the Godot project
"$G" --headless --path godot --fixed-fps 60 -- --scenario world   # an end-to-end scenario, exit 0/1
"$G" --path godot --fixed-fps 60 --quit-after 240 \
     --write-movie <dir>/frame.png -- --demo                       # render frames to look at
```

`--fixed-fps 60` makes every frame 1/60 s of game time, so headless runs are deterministic
and as fast as the machine allows.

**Scenes.** `Scenes/Sandbox.tscn` is the game world (the main scene). `Scenes/LoopSlice.tscn`
is a small hand-built slice, kept as the `loop` scenario's fixture; run it by passing
`res://Scenes/LoopSlice.tscn` before `--`.

**Scenarios** (`Scripts/Scenarios/`) are the scene-level checks. `ScenarioRunner` is a node in
each scene; `-- --scenario <name>` runs one to a verdict, prints
`[scenario <name>] PASS|FAIL …` and exits 0/1. An unknown name fails and lists the known
ones. Current scenarios:
- `world` (Sandbox): the shell matches `SandboxLayout`, all nine stalls publish endpoints, and
  the player walks down the street and through the town square to its far kerb and back,
  with the camera following.
- `build` (Sandbox): every placeable placed through the real build menu and cursor with
  synthetic mouse and key events, then drag runs, Escape, and a demolition that restores the
  stockpile exactly.
- `interact` (Sandbox): the player's `[E]`, hold-`[E]`, and `[G]` at a stall, an empty stall,
  the bench and the station, using real key and mouse events.
- `golems` (Sandbox): every chassis built, and every `AppendageActionType` run on real steam:
  extract, haul, push, load, assemble plus repeat, refine, and freight. A golem out of steam
  must stall and wear its badge.
- `workbench` (Sandbox): program a golem end to end through the Workbench with real mouse
  drags. Also covers socket highlights, the dial, failed drags leaving no orphans, Engage,
  Patent, and close/reopen.
- `management` (Sandbox): the HUD (alerts strip, pause and play), and Tab opening Management
  with the HUD and build menu hidden. Covers the Inventory icons, the tab exclusivity, an
  Assembly Line claim reaching the Workbench, Extend planking and walling new rows, Patents
  Load, the Ledger's plaques and recipe pane, and one screen at a time.
- `save` (Sandbox): through the SaveLoad tab, build, save, wreck everything, and load. The
  buildings, golems (with program and place) and stockpile must come back exactly, and two
  more rounds must change nothing. Then it extends the room after a save and loads, and the
  rows and back wall must go back. Writes `user://scenario-save.json`, never the player's save.
- `tutorial` (Sandbox): the step-by-step guide's panel.
  - It opens on step 1, with the arrow pinned to the screen edge toward the Scrap stall and
    clear of the HUD bars.
  - Gathering advances it, and the Boiler step outlines its build-menu row.
  - F1 hides and shows it, it steps aside over Management, and Skip guide puts it away.
  - In the Workbench on "Program it", it sits clear of the sockets, the lever and the vault.
- `playtest-kit` (Sandbox): F9 once per chapter from a fresh game (all ten). The kit must
  fast-forward every guide chapter, leaving the golems and buildings a player would have
  built, and the report must record it. Its Save and Load steps use
  `user://scenario-save.json`.
- `loop` (LoopSlice): the station builds a Scavenger, which mines onto the belt, which the unloader hauls
  into the stockpile, over 600 ticks. `--spike-check` is an alias.
- `font-glyphs`: the project font covers printable Latin-1 plus → ≥ █ ░.

To add a scenario, implement `IScenario` and register it in `ScenarioRunner.Scenarios`.
`--demo` (alias `--spike-demo`) only runs the scripted setup, for `--write-movie` captures.

## Art and fonts

- **The art lives in `godot/art/`**, including `UI/…`. The generators in `Tools/Art/` write
  here. Their root comes from `Tools/Art/art_paths.py`, and `--out-root <dir>` redirects any
  of them.
- **`python Tools/Art/verify_art.py`** regenerates everything into a scratch folder and
  compares it with `godot/art/`. Exit 1 means a generated sprite drifted. It also lists the
  sprites that are authored rather than generated (walk frames, item and chassis art, the
  Steampunk pack), so "not generated" is never mistaken for "verified".
- Texture filtering is nearest-neighbour project-wide. Standing sprites are placed with
  `GridConversions.StandOnCell`, Unity's BottomCenter pivot. The five pipe tiles are
  centre-pivoted (they rotate in quarter turns). 9-slice UI uses `StyleBoxTexture` margins.
  Those are set where each sprite is drawn, in G5 and G7, not by an import pass.
- **Font:** `fonts/LiberationSans.ttf` (OFL; licence beside it) is the project font
  (`gui/theme/custom_font`). Game text is printable Latin-1 plus → ≥ █ ░, with · as the
  separator, and the `font-glyphs` scenario pins that. To use a new character, check that
  the font has it; if not, add a fallback font and widen the scenario in the same change.

## The world is built from rules at load

`Sandbox.tscn` is mostly empty. `ShellNode` builds walls, props and sconces from
`Core/World/SandboxLayout`, and `SandboxNode` builds the stalls and the starter bench from
`data/sandbox.json` (via `WorldNode.Setup`). Change the room in Core, where
`SandboxLayoutTests` pins it to Unity's numbers. Don't hand-place walls in the scene.
Sprite pivots, Unity's import pivots, live in `Scripts/World/SpritePivots`.

## The Sandbox is composed in Core

`Core/World/SandboxWorld` is what Unity's `SandboxBootstrap` did, as a plain object: every
registry, and the wiring between them and `BuildModeController`. `WorldNode` owns one.
Unit tests compose the same world with no scene (`SandboxWorldTests`). A new system belongs
there, not on a node, so a test can reach it.

Buildings are Core `PlaceableBuilding`s. Godot draws them in `Scripts/Buildings/BuildingsLayer`,
one `BuildingView` each, by listening to `BuildingPlaced`, `BuildingRemoved` and
`ConnectedShapesChanged`. Don't give a building its own scene with logic in it.

**Where a carried golem lands is `PlayerInteractor.CarryDropCell`**, the tile under the
player's feet. `GolemNode` outlines it (red where `CarryDropBlocked`) with the golem's source
and target tiles around it, and `[G]` drops on it, so the preview and the drop read one
answer. The carried sprite rides half a tile above the player and is not a guide to it. While
carrying, `R` turns the carried golem, never a nearer one or a bench.

**The player's hands are Core's `PlayerInteractor`** (`SandboxWorld.Interactor`). `PlayerNode`
only feeds it the position and the keys. A full screen joins the `ModalScreens` group and
implements `IScreen`; while one is open the player stays still and the world prompt hides. A
new screen gets that for free.

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

**Marked tiles: what the guide checks.** Pipe steps are done by where the steam reaches, not
which tiles hold pipe (any route counts). Buildings later steps are laid out around stay on
their marked tiles (`TutorialStep.ExactTile`), and `TutorialGuide.Notice` says why a step is
waiting: the right building built elsewhere, something else on a marked tile, a building on a
golem's tile, a belt pointing the wrong way. A step's `Builds` names what its marks are for.

**A new guide step needs a kit action** (`TutorialGuide.Kit.cs`, `Performs`): what the
playtest kit does to complete it, using the player's own verbs. `PlaytestKitTests` fails for
a step without one.

A step that wants a card claimed names it (`TutorialStep.Card`), and the Assembly Line always
shows that card (`AssemblyLineState.Wanted`). The line otherwise offers a card only once the
factory has made every good its price asks for, so a claim is never one the player can't pay.
A step shown over the Workbench sets `TutorialStep.Workbench`. A kit action that moves the
player calls `PlayerInteractor.Teleport`; a bare `Position` write is overwritten by
`PlayerNode` on its next frame.

**The Clock Tower is a fixture** (`PlaceableBuilding.IsFixture`,
`BuildModeController.RegisterFixture`): it stands from the start on a 3×3 footprint in the
town square (`Core/World/TownSquare`), is not in the build menu (`SandboxWorld.Placeables`
versus `AllPlaceables`), and the wrecking bar refuses it. A fixture is never in
`Build.Buildings`, which is the player's factory (save, refund, counts); its progress saves in
`ProgressEntry` instead. It is roped off until the ledger has seen a Zeppelin
(`ClockTowerSite.OpenWhen`), and only freshly assembled goods count toward a stage.

**Playtest mode writes `user://playtest-report.md`**, and keeps the previous session's as
`playtest-report-<date>-<time>.md` before a new one starts (`PlaytestReportArchive`): every
launch used to overwrite it. A question is asked as the chapter after its experience opens
(`PlaytestQuestion.AfterStepId`); only the tower's and the guide's own wait for `done`. The
kit's chapter 7 Save and Load use `kit-save.json` beside the player's save, never the save
itself. Scenario runs write
`user://scenario-playtest-report.md` instead, and save to `user://scenario-save.json`
(`PlaytestNode.UnderScenario`, which reads `--scenario` off the command line itself: a flag
set by the ScenarioRunner arrived too late, and a kit run overwrote a player's save). Never
point a test at the real paths. **A save calls `TutorialGuide.Settle`, never `Update`**:
`Update` also runs the playtest kit, which then performed its Load step from inside the save. Anything that POLLS the keyboard
must also check `TextEntry.IsTyping`, or typing into a field walks the player.

**Full screens report to `SandboxWorld.Screens`** (`ScreenCoordinator`). Implement
`IClosableScreen`, `Register` in `_Ready`, and call `Screens.Opening(this)` in `Open`; every
other screen closes. Don't hand-close siblings. That's the bug class the coordinator replaced.

**Godot node names can't hold `.` or `:`**, and a duplicate sibling name is silently renamed.
Never find a control by a name built from data (a node id, a card name); keep a lookup.

**Screens are laid out with `Scripts/UI/Ugui.cs`**, in the numbers of the Unity prefabs they
were transcribed from (those prefabs are in git history at `unity-final`). `Ugui.Place` takes
a RectTransform's anchorMin/anchorMax/anchoredPosition/sizeDelta/pivot, with Unity's anchors
(y up). `Ugui.Image` gives a 9-sliced, tinted plate whose tint stays off its children.
`Ugui.Text` is a single-line TMP-style label. To change a screen, work from its existing
numbers; don't eyeball a layout. **A control has no size until the frame
after it's built**, so a scenario must not click something in the same step that opened it.

**Scenario input:** `Input.ParseInputEvent` **without** `FlushBufferedEvents` for key presses,
and a tap must hold the key down across at least one frame. A press and release in the same
frame, or a flush from inside `_Process`, never reads as "just pressed" to a node that has
already run that frame.

**Depth: a standing sprite's feet go on its node's origin.** Y-sort compares origins, so use
`GridConversions.StandOnCell` or `SpritePivots` for anything that stands, and never offset a
sprite's feet away from its node. The `world` scenario fails if one does.

**Z-order:** the floor is z −10 (`FloorLayer.FloorZ`), and floor-level things (belts, pipes,
the ghost, shadows) are −1. A belt's own tile is −2, so every lane sits under every item of
cargo; at one z, an item crossing into the next cell vanished under that cell's tile. Z-index is global within a canvas layer, so anything you add at a
negative z must stay above −10.

## Authored data (`godot/data/*.json`)

The chassis, logic cores, punch cards, recipes, Clock Tower stages and the Assembly Line
deck, plus `placeables.json` (the build menu) and `sandbox.json` (the world's setup). **They
are hand-edited.** They were generated from Unity's assets until the G10 cutover; the
converter is in git history at `unity-final`.

- **Keys are C# field names**, and references are by definition **name** (e.g.
  `"recipe": "R18_AetherConduit"`). Bools are `0`/`1` and enums are integers, as Unity
  serialized them.
- **`Core/Data/DefinitionLoader` is strict.** An unknown key, a reference to a missing name,
  an undefined enum value, or a bool other than 0/1 throws, naming the file and field.
  `WorldNode` loads the data at startup, so a bad edit fails on launch, not mid-game.
  - Don't "fix" a strictness failure by loosening the loader. It means the data and the
    classes disagree.
  - A key the JSON omits keeps the C# field's initializer.
- **Renaming a definition breaks saves that name it.** A save stores cards, chassis and
  buildings by name; a card the catalog can't resolve is dropped on load, with its slot's
  settings. **To change what the player reads, set a card's `displayName`** (the Workbench and
  the Assembly Line show `AppendageActionDefinition.Label`) and leave its name alone, as
  `ExtractScrap` does: it reads "Extract", since it takes whatever the stall behind holds.
- Tests read the real data through `Core.Tests/AuthoredData`, which loads fresh per call
  so no test can leak a mutation.

## Engine rules

- **Core keeps Unity's frame: +y is north.** `Scripts/GridConversions.cs` is the only place a
  cell becomes a Godot position (Godot's +y points down). Don't do cell arithmetic in a Node.
- **Core has its own math types** (`Core/Compat/UnityMath.cs`). Ported files keep
  `using GolemFactory.Compat;` where they had `using UnityEngine;`. Don't use Godot's
  `Vector2I` in Core; convert at the Node boundary. The shim follows Unity's semantics where
  they differ from `System.Math` (`RoundToInt` rounds half to even, `Lerp` clamps), because
  the ported tests assert Unity's answers.
- **Don't name a namespace `...Godot`.** It shadows the engine's `Godot` namespace. The Node
  layer is `GolemFactory.Nodes`.
- **`GolemEntity` is a plain class.** `Attach()`/`Detach()` replace Unity's
  `OnEnable`/`OnDisable` (the Signal-trigger subscription). A Node that owns a golem calls
  them; a unit test calls them only if it wants Signal behaviour.
- **One `WorldNode` replaces Unity's seven Holders.** It must be the scene root's first child,
  because siblings run `_Ready` in tree order and everything looks it up there.
- **Input actions are registered in code** (`PlayerNode.RegisterInputActions`), not in
  `project.godot`'s `[input]` block, whose serialized `InputEvent` objects aren't meant to be
  hand-written.
- Godot writes `*.import` and `*.cs.uid` files beside assets and scripts. **Commit them.**
  `.godot/` is the cache and stays ignored.

## Git in this repo

`git add` intermittently fails with `Permission denied` on `.git/objects`, a different file
each time and in any shell. Retry it. Never discard its stderr and carry on, or the commit
runs on an empty index.

## Game rules that hold whatever the engine

These came over from the Unity project unchanged, because the rules themselves were ported,
not rewritten. Each is pinned by a test in `Core.Tests`.

### The tick simulation

Everything golem- and belt-related runs off one fixed-tick loop (`Core/Simulation/
SimulationClock`), which calls `ITickable.Tick(tick)` on every registrant in registration
order, never per-object frame updates. Play, pause and speed are independent of tick
advancement. Simulation is plain, data-oriented C#: no ECS, no per-item nodes.

### Golem execution

- `ChassisDefinition`, `LogicCoreDefinition` and `AppendageActionDefinition` are shared,
  authored definitions (`data/*.json`). All five chassis share one `GolemEntity`/`GolemProgram`
  path; there is no per-golem subclassing.
- **Per-golem settings live on the program, never on a shared definition.** A Haul slot's
  batch size (`GolemProgram.appendageQuantities`) and good (`appendageItemTypes`) are the
  player's; writing them onto the card would retune every golem holding it.
- **The machine model** (`Golems/GolemInventory`): an input and an output stock per golem,
  12 per item type. `Haul`/`ExtractFromNode` fill input from the tile behind, `Push` empties
  stock onto the tile in front, `Assemble` converts input to output without touching a tile.
  **The pure-logistics rule:** a program with no Assemble step treats its input as its output
  (`GolemProgram.HasAssembleStep`); without it every logistics golem stalls full forever.
- **Execution is strictly linear and non-adaptive.** A failed precondition doesn't skip or
  reorder; the golem stalls and retries the same step every tick, publishing
  `GolemStalledEvent`/`GolemResumedEvent`. Rigidity is structural.
- `Repeat(n)` re-runs the preceding Assemble n more times as n sequential assemblies,
  Overclocker-only (`ChassisDefinition.allowsRepeat`), refused at assembly time.
- Triggers: `AlwaysOn`, `Interval`, `Threshold` (edge-triggered), `Signal` (latches a fire that
  arrives mid-cycle).
- **`AppendageActionType` and `StallReason` are append-only**: serialized by integer index.
- **A golem is named by its `GolemId`**, never by a node or object name.
- **A golem's `Mood` is a read-only classification** (`GolemMoodRules.Classify`), derived from
  state, stall reason and inventory. Presentation polls it. Add feedback by widening the mood,
  not by adding a second floating badge.

### Building, demolition and the wrecking bar

- **Demolition refunds the full cost. This is a settled design call**: the game is cozy, so
  placing and reorganising must not be punitive. Do not reintroduce a salvage fraction as
  "balance". Three rules keep it honest:
  - **Only what the player paid for**: the price is gated on `PlaceableBuilding.IsRuntimePlaced`.
    (What a building *holds* comes back whoever built it.)
  - **Only a player's own click**: `ClearRuntimePlacedBuildings` (a load) passes `refund: false`,
    or save/load/save/load is an infinite duplicator.
  - **Never destroy what it cannot hand back**: `RefundWouldFit` is asked before teardown.
- **The two tools are exclusive in both directions**: entering demolish puts the placeable
  down, and picking a placeable puts the wrecking bar down. The invariant lives on
  `BuildModeController`, not on any menu.
- **The wrecking bar takes golems too** (`IGolemDismantler`), refunding chassis and cargo
  (`GolemDismantleRules`). **A click takes a golem; a drag never does**, because a refund can't
  restore a program. A golem the player is carrying is refused.
- **Golems are NOT `GridMap` occupants**; only buildings are. Anything asking "what stands
  here" scans for golems (`TryFindGolemAt`). There is no golem-by-cell registry.
- **Click-and-drag lays a run that points along itself** (`Player/BuildDragPath`, orthogonal,
  axis-first). Which placeables drag is a per-prefab flag, `IsDragPlaceable`. **A drag is not a
  click**: it never demolishes. It stops at an obstruction rather than skipping it, and
  wiggling back over the run re-anchors without re-facing. The wrecking bar's drag needs no
  flag, never stops at an empty cell, and never takes a golem.
- **A belt's and a pipe's picture is derived from its links**, never the other way round
  (`World/BeltShapeRules`, `Steam/PipeShapeRules` beside the routing rules). A steam pipe has
  no facing; R only orients an isolated stub.

### Economy, cards and patents

- **A labelled depot publishes a different endpoint** (`FilteredBufferEndpoint`), which takes
  and gives only its good. `IFilteredEndpoint` lets a push tell "full" from "for something
  else"; it is not part of `IItemEndpoint` and should not become part of it.
- **The Assembly Line's unlock context is wired before the pool is seeded, and it is
  monotone.** Seeding first makes every prerequisite unanswerable and gating inert. The
  context reads `TechTreeProgressLedger`, which only grows, so spending your last Scrap never
  re-locks a card. `TryClaimSlot` refuses a locked card; a slot never offers a card the player
  already owns, nor one priced in a good the factory has never made.
- **There is no Artificer Focus meter, and patenting is free. Settled.** The Patent Registry is
  a named-program library. Do not reintroduce a Workbench currency to "give patents a
  purpose"; if reprogramming should cost something, the factory pays it in goods.
- **Item tiers are data** (`Economy/ItemTiers`), pinned by reflection over `ItemType`, so a new
  good can't be added without a tier.
- **Bare-string ids** identify belts, buffers and nodes across systems; `ItemType` holds the
  item-type constants. **Registries return `false` for a `null` id** rather than throwing.

### Seams worth keeping

- **Late-wiring seams** (`IGolemRespawner`, `IBuildingRebuilder`, `IPlacedStationConfigurator`,
  `IGolemDismantler`): something entering the world after setup asks the component that
  already knows the wiring, rather than growing a second copy of it. Birth and death of a golem
  stay in one file (`GolemConstructionStation`).
- **Events** go through `Events/EventBus`, a static bus of `readonly struct` events. Add event
  types there rather than a second bus.
- **Multiplayer-compatible seams**: ownable things carry an explicit `OwnerId` (a single
  `LocalPlayer` for now), and `PatentRegistry.TryUseBlueprint(id, userId)` already has the
  royalty branch. Global systems (`SimulationClock`, `GridMap`) may stay simple singletons.

### Verifying

- **Verify a UI change by looking at the game view**, not only its properties
  (`--write-movie` frames). A control that is present, positioned and holding the right text
  can still be invisible.
