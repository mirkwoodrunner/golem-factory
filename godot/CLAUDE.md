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
"$G" --headless --path godot --fixed-fps 60 -- --spike-check      # end-to-end check, exit 0/1
"$G" --path godot --fixed-fps 60 --quit-after 240 \
     --write-movie <dir>/frame.png -- --spike-demo                 # render frames to look at
```

`--fixed-fps 60` makes every frame 1/60 s of game time, so headless runs are deterministic
and as fast as the machine allows. `--spike-check` auto-builds the golem, runs 600 ticks and
asserts the whole loop (node → extractor → belt → unloader → depot).

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
