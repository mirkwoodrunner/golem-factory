# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

`golem-factory` is a solo-play **Godot 4** prototype of "Golem Factory: The Clockwork Metropolis" -- a
Factorio/Satisfactory-style automation game (cozy top-down pixel art) where the player places "golems" programmed with
punch-card-style Logic Core / Appendage / Chassis combinations that run rigidly on a world tick clock. It began as a
tabletop board-game design, became a Unity prototype, and was ported to Godot (milestones G0-G10,
`docs/godot-conversion-plan.md`). **The Unity project was removed at the G10 cutover; the tag `unity-final` holds its
last state.**

The game lives in `godot/`. Paths in the engine sections below are relative to `godot/` unless they say otherwise.

Read the docs before making design calls, not just the code:

- `docs/game-design.md` -- the original tabletop concept. Source of truth for *mechanics*.
- `docs/digital-design.md` -- visual style, the golem roster, and the Workbench spec.
- `docs/progression-design.md` -- the tech tree, steam scarcity, chassis unlocks, Clock Tower endgame.
- `docs/cozy-automation-design.md` -- depots, moods, loop labels, Recycler; §0: eight invariants.
- `docs/godot-conversion-plan.md` -- the port's milestone log (G0-G10). Finished.
- `docs/roadmap.md` -- **"Next session: start here"**. Read it before planning any work.
- `docs/crates-design.md` -- crates, capped pack, wallet, Pneumatic Post; Phase 1. §0: invariants.
- `docs/inventory-screens-design.md` -- the Pack and Crate screens and the label picker; Phase 1.
- `docs/pack-upgrades-design.md` -- pack upgrades and Floor Expansion pricing. §0: invariants.
- `docs/onboarding-feedback-design.md` -- hint, grey/red moods, hover, collecting Haul; Phase 2. §0.
- `docs/guide-chapters-design.md` -- the guide's texts, checks, marks, plans and kit actions.
- `docs/art-style-bible.md` -- Phase 4's look. §0: the invariants any art pass must keep.
- `docs/art-pipeline-design.md` -- Phase 4's how: ComfyUI workflow, sprites, user decisions (§9).
- `docs/audio-design.md` -- Phase 5: sound slots, mix rules, placeholder sources, decisions (§9).
- `docs/game-feel-design.md` -- Phase 6: effects, camera levels and alert pan, the tower's event (§7).
- `docs/menus-design.md` -- Phase 7: settings, accessibility, title and saves, overview, Creative. §0, §4.
- `docs/supply-design.md` -- Phase 8: supply carts on the market street. §0: invariants.
- `docs/contracts-design.md` -- Phase 8: city contracts (built, §12), and the tower's any-source rule (§11).
- `docs/endless-design.md` -- Phase 8: endless play after the Clock Tower.
- `docs/region-design.md` -- the second region, the Docks: east of the street, from chapter 9.
- `docs/golem-sales-design.md` -- golem orders at the City Office from chapter 5; the Golem Foundry.
- `docs/steam-deck-design.md` -- running on the Steam Deck (not a store release); planned, not started.
- `docs/performance-report-template.md` -- the endgame profile's spec; `-phase3.md` is a run.
- `docs/open-items.md` -- the backlog: open decisions, known gaps, deliberate scope cuts.
- `docs/godot-spike.md` -- what the port measured before it began.
- `docs/history/` -- the Unity era (M0-M9, test ledger, MCP setup). History, not instructions.
- `testscript/phase-1-playtest.md` -- how to run a playtest; the guide asks the questions.
- `testscript/phase-2-playtest.md` -- chapters 1–4 without F9 in 15 minutes; run it first.
- `testscript/endless-soak.md` -- two hours of endless play, sampled by `Tools/Soak/soak-sample.ps1`.

## Toolchain

- **Godot 4.7.2 mono** at `G:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\`. The folder name is
  doubled. Use `Godot_v4.7.2-stable_mono_win64_console.exe` for anything run from a shell. Always the **mono**
  build: the standard Godot can't load C# and shows a grey screen ("No loader found for resource: …cs" in the log).
- **.NET SDK 10**. Every project targets `net10.0`. GodotSharp 4.7.2 is built for net8.0, but only the .NET 10 runtime
  is installed, and net10 can reference net8.
- `nuget.config` points at the Godot install's bundled packages, so `dotnet build` works without opening the editor
  first.

## Layout

| Project | What it is |
|---|---|
| `Core/GolemFactory.Core.csproj` | All game rules. **No engine reference**: the compiler refuses a Godot type here. |
| `Core.Tests/GolemFactory.Core.Tests.csproj` | NUnit 3, ported from Unity's EditMode suite. NUnit 3, not 4, so the classic `Assert.AreEqual` form works unchanged. |
| `GolemFactory.Godot.csproj` (+ `Scripts/`, `Scenes/`, `art/`) | Nodes and scenes only. Excludes `Core/**` and `Core.Tests/**` from its compile glob. |

`Core/` and `Core.Tests/` each hold a `.gdignore` so the editor doesn't import .NET build output as resources.

**Folder rules** load only when you work in that folder; read the right one first when working on its area from
elsewhere: `godot/Scripts/Scenarios/`, `godot/Scripts/UI/` (screens, `Ugui`), `godot/Core/` `Tutorial/` (guide, playtest
kit), `Golems/`, `Simulation/` (the tick), `Buildings/` (wrecking bar), `Economy/` (cards, crates, pack), `ClockTower/`;
`godot/data/`; `godot/Scenes/` (hand-written `.tscn`); `Tools/Art/` (art, AI images, hand-replaced sprites).

## Commands

```sh
dotnet test godot/GolemFactory.sln            # Core tests: ~10 s, no engine involved
dotnet test godot/GolemFactory.sln -c Release --filter Category=Perf   # the explicit endgame benchmark

G=".../Godot_v4.7.2-stable_mono_win64_console.exe"
"$G" --headless --path godot --build-solutions --quit             # compile the Godot project
"$G" --headless --path godot --fixed-fps 60 -- --scenario world   # an end-to-end scenario, exit 0/1
"$G" --path godot --fixed-fps 60 --quit-after 240 \
     --write-movie <dir>/frame.png -- --demo                       # render frames to look at

./Tools/Deck/export-linux.ps1 [-DeckHost steamdeck]   # the Steam Deck build, into build/deck/ (PowerShell)
./Tools/Deck/export-linux.ps1 -Preset 'Windows Smoke' -Out build/win-smoke   # the same pack for Windows
```

An exported build runs scenarios too (`GolemFactory.exe --headless --fixed-fps 60 -- --scenario world`), except
`art` (it reads `Tools/`) and `loop` (release builds refuse a scene path). A scenario must not list `res://` for
`.png` files: a pack lists `.png.import`; ask `ResourceLoader.Exists`.

`--fixed-fps 60` makes every frame 1/60 s of game time, so headless runs are deterministic and as fast as the machine
allows.

**Scenes.** `Scenes/Sandbox.tscn` is the game world (the main scene). It opens on the title
screen (`docs/menus-design.md` §6), and starting a game reloads it; a scenario or `--demo` run
opens straight on a new game. `Scenes/LoopSlice.tscn` is a small hand-built
slice, kept as the `loop` scenario's fixture; run it by passing `res://Scenes/LoopSlice.tscn` before `--`.

**Scenarios** (`Scripts/Scenarios/`) are the scene-level checks. `ScenarioRunner` is a node in each scene; `--
--scenario <name>` runs one to a verdict, prints `[scenario <name>] PASS|FAIL …` and exits 0/1. An unknown name fails
and lists the known ones. Current scenarios: `world`, `build`, `interact`, `golems`, `golem-motion`, `workbench`,
`management`, `save`, `tutorial`, `playtest-kit`, `loop`, `inventory`, `moods`, `hover`, `font-glyphs`, `perf`, `art`, `audio`, `feel`, `settings`, `accessibility`, `title`, `slots`, `overview`, `creative`, `supply`, `contracts`, `endless`, `docks`, `golem-sales`, `gamepad`, `guide-ahead`, `gamepad-ui`; what
each checks is in `godot/Scripts/Scenarios/CLAUDE.md`.

To add a scenario, implement `IScenario` and register it in `ScenarioRunner.Scenarios`. `--demo` (alias `--spike-demo`)
only runs the scripted setup, for `--write-movie` captures. A windowed scenario run given `-- --scenario <name>
--capture-dir=<dir>` saves the frames its steps name (`ScenarioCapture.Shot`), e.g. `moods`, `hover` and `workbench`'s
`critique-*.png`.

## Git in this repo

`git add` intermittently fails with `Permission denied` on `.git/objects`, a different file each time and in any shell.
Retry it. Never discard its stderr and carry on, or the commit runs on an empty index.

## The world is built from rules at load

`Sandbox.tscn` is mostly empty. `ShellNode` builds walls, props and sconces from `Core/World/SandboxLayout`, and
`SandboxNode` draws the street's cart spots and the starter bench from `data/sandbox.json` (via `WorldNode.Setup`). Change the room
in Core, where `SandboxLayoutTests` pins it to Unity's numbers. Don't hand-place walls in the scene. Sprite pivots,
Unity's import pivots, live in `Scripts/World/SpritePivots`.

## The Sandbox is composed in Core

`Core/World/SandboxWorld` is what Unity's `SandboxBootstrap` did, as a plain object: every registry, and the wiring
between them and `BuildModeController`. `WorldNode` owns one. Unit tests compose the same world with no scene
(`SandboxWorldTests`). A new system belongs there, not on a node, so a test can reach it.

**Godot node names can't hold `.` or `:`**, and a duplicate sibling name is silently renamed. Never find a control by a
name built from data (a node id, a card name); keep a lookup.

**Depth: a standing sprite's feet go on its node's origin.** Y-sort compares origins, so use
`GridConversions.StandOnCell` or `SpritePivots` for anything that stands, and never offset a sprite's feet away from its
node. The `world` scenario fails if one does.

**Z-order:** the floor is z −10 (`FloorLayer.FloorZ`), and floor-level things (belts, pipes, the ghost, shadows) are −1.
A belt's own tile is −2, so every lane sits under every item of cargo; at one z, an item crossing into the next cell
vanished under that cell's tile. Z-index is global within a canvas layer, so anything you add at a negative z must stay
above −10.

## Engine rules

- **Core keeps Unity's frame: +y is north.** `Scripts/GridConversions.cs` is the only place a cell becomes a Godot
  position (Godot's +y points down). Don't do cell arithmetic in a Node.
- **Core has its own math types** (`Core/Compat/UnityMath.cs`). Ported files keep `using GolemFactory.Compat;` where
  they had `using UnityEngine;`. Don't use Godot's `Vector2I` in Core; convert at the Node boundary. The shim follows
  Unity's semantics where they differ from `System.Math` (`RoundToInt` rounds half to even, `Lerp` clamps), because the
  ported tests assert Unity's answers.
- **Don't name a namespace `...Godot`.** It shadows the engine's `Godot` namespace. The Node layer is
  `GolemFactory.Nodes`.
- **One `WorldNode` replaces Unity's seven Holders.** It must be the scene root's first child, because siblings run
  `_Ready` in tree order and everything looks it up there.
- **Input actions are registered in code** (`PlayerNode.RegisterInputActions`), not in `project.godot`'s `[input]`
  block, whose serialized `InputEvent` objects aren't meant to be hand-written.
- Anything that POLLS the keyboard must also check `TextEntry.IsTyping`, or typing into a field walks the player.
- Godot writes `*.import` and `*.cs.uid` files beside assets and scripts. **Commit them.** `.godot/` is the cache and
  stays ignored.
- **Font:** `fonts/LiberationSans.ttf` (OFL; licence beside it) is the project font (`gui/theme/custom_font`). Game text
  is printable Latin-1 plus → ≥ █ ░, with · as the separator, and the `font-glyphs` scenario pins that. To use a new
  character, check that the font has it; if not, add a fallback font and widen the scenario in the same change.

## Game rules that hold whatever the engine

These came over from the Unity project unchanged, because the rules themselves were ported, not rewritten. Each is
pinned by a test in `Core.Tests`.

### Seams worth keeping

- **Late-wiring seams** (`IGolemRespawner`, `IBuildingRebuilder`, `IPlacedStationConfigurator`, `IGolemDismantler`):
  something entering the world after setup asks the component that already knows the wiring, rather than growing a
  second copy of it. Birth and death of a golem stay in one file (`GolemConstructionStation`).
- **Events** go through `Events/EventBus`, a static bus of `readonly struct` events. Add event types there rather than a
  second bus.
- **Multiplayer-compatible seams**: ownable things carry an explicit `OwnerId` (a single `LocalPlayer` for now), and
  `PatentRegistry.TryUseBlueprint(id, userId)` already has the royalty branch. Global systems (`SimulationClock`,
  `GridMap`) may stay simple singletons.

### Verifying

- **Verify a UI change by looking at the game view**, not only its properties (`--write-movie` frames). A control that
  is present, positioned and holding the right text can still be invisible.

## Settled rules (never break by accident)

- Demolition refunds the full cost, no salvage fraction (`godot/Core/Buildings/CLAUDE.md`).
- Patenting is free and there is no Artificer Focus meter (`godot/Core/Economy/CLAUDE.md`).
- Golems are rigid: a failed step stalls and retries, never adapts (`godot/Core/Golems/CLAUDE.md`).
- `AppendageActionType` and `StallReason` are append-only (`godot/Core/Golems/CLAUDE.md`).
- Game rules live in Core; nodes only feed input and draw (Layout, "The Sandbox is composed in Core").
- Red means the player must act; Waiting is grey, steam is blue (`godot/Core/Golems/CLAUDE.md`).
- Never print an id in player text (`godot/Core/Golems/CLAUDE.md`).
- A load never clamps (`godot/Core/Economy/CLAUDE.md`).
- Tests and scenarios never touch the real save or playtest report (`godot/Core/Tutorial/CLAUDE.md`).
- A quoted price is the data's, never a copy of the number (`godot/Core/Tutorial/CLAUDE.md`).
- `DefinitionLoader` is strict; don't loosen it (`godot/data/CLAUDE.md`).
- Renaming a definition (`godot/data/CLAUDE.md`) or a guide step's `Id` (`godot/Core/Tutorial/CLAUDE.md`) breaks saves.
- Every cost goes through `PlayerWallet` (`godot/Core/Economy/CLAUDE.md`).
- Everything ticks off one `SimulationClock`, never per-object frame updates (`godot/Core/Simulation/CLAUDE.md`).
- Depth (feet on the node's origin) and z-order (above). Hand-replaced art: `Tools/Art/CLAUDE.md`.


