# godot/ — the Godot spike

A **spike**, not the game: it measures what porting golem-factory from Unity to Godot costs.
The Unity project at the repo root is still the reference and is untouched by anything here.
Read the root `CLAUDE.md` for the game's design rules. They apply unchanged, because the rules
themselves were ported, not rewritten. The spike's write-up is `docs/godot-spike.md`.

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
is the spike's hand-built slice, kept as the `loop` scenario's fixture; run it by passing
`res://Scenes/LoopSlice.tscn` before `--`.

**Scenarios** (`Scripts/Scenarios/`) are the scene-level checks. `ScenarioRunner` is a node in
each scene; `-- --scenario <name>` runs one to a verdict, prints
`[scenario <name>] PASS|FAIL …` and exits 0/1. An unknown name fails and lists the known
ones. Current scenarios:
- `world` (Sandbox): the shell matches `SandboxLayout`, all nine stalls publish endpoints, and
  the player walks to the street's far edge and back with the camera following.
- `loop` (LoopSlice): the station builds a Scavenger, which mines onto the belt, which the unloader hauls
  into the stockpile, over 600 ticks. `--spike-check` is an alias.
- `font-glyphs`: the project font covers printable Latin-1 plus → ≥ █ ░.

To add a scenario, implement `IScenario` and register it in `ScenarioRunner.Scenarios`.
`--demo` (alias `--spike-demo`) only runs the scripted setup, for `--write-movie` captures.

## Art and fonts

- **The art lives in `godot/art/`**, mirroring Unity's `Assets/_Project/Art/` including
  `UI/…`. Since G3 the generators in `Tools/Art/` write here. Their root comes from
  `Tools/Art/art_paths.py`, and `--out-root <dir>` redirects any of them. Unity's copy is
  frozen until cutover.
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

## Authored data (`godot/data/*.json`)

The chassis, logic cores, punch cards, recipes, Clock Tower stages and the Assembly Line
deck: what Unity kept as ScriptableObject `.asset` files.

- **Until cutover (G10) the Unity assets are the source of truth.** Regenerate the JSON with
  `python Tools/Data/convert_unity_assets.py`. `--check` exits 1 if the JSON is stale. After
  cutover the JSON is edited by hand.
- **Keys are C# field names**, and references are by definition **name** (e.g.
  `"recipe": "R18_AetherConduit"`). Unity's `0`/`1` bools and integer enums are kept as they
  are.
- **`Core/Data/DefinitionLoader` is strict.** An unknown key, a reference to a missing name,
  an undefined enum value, or a bool other than 0/1 throws, naming the file and field.
  `WorldNode` loads the data at startup, so a bad edit fails on launch, not mid-game.
  - Don't "fix" a strictness failure by loosening the loader. It means the data and the
    classes disagree.
  - A key the JSON omits keeps the C# field's initializer. That is how Unity treated
    fields added after an asset was saved.
- Tests read the real data through `Core.Tests/AuthoredData`, which loads fresh per call
  so no test can leak a mutation. `DefinitionLoaderTests.EveryLoadedFieldEqualsItsJsonValue`
  is the parity check on the conversion.

## The test ledger

`docs/godot-test-ledger.md` is **generated**. Regenerate it with
`python Tools/Godot/test_ledger.py`. `--check` exits 1 if it is stale, or if a Unity test file
is neither ported nor assigned a milestone in the script's `HELD` table. A ported file keeps
its Unity relative path under `Core.Tests/`. A PlayMode suite that lands elsewhere goes in
`MOVED`. Every milestone PR regenerates the ledger.

## Rules that are specific to this side

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
