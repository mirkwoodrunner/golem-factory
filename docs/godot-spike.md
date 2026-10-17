# Godot spike: what porting golem-factory costs

**Date:** 2026-10-03 · **Branch:** `spike/godot` · **Engine:** Godot 4.7.2 mono, .NET 10

**Recommendation: go.** The game's rules ported almost untouched, the golem execution model
works outside `MonoBehaviour` with no logic changes, and the Godot workflow is
substantially easier to drive and verify from the command line. What remains is the
presentation layer, the half of the codebase that is Unity-specific, and that has to be
rebuilt rather than ported.

## The three questions

### 1. How much of the C# carries over unchanged?

Most of the rules. **118 runtime files (17.3k lines) now live in `godot/Core`**, a plain
.NET class library with no engine reference:

| Change needed against the Unity original | Files |
|---|---|
| None, byte for byte | 73 |
| Only `using UnityEngine;` → `using GolemFactory.Compat;` | 35 |
| Edited | 10 |
| New (math shim, `Debug` stand-in, an extracted enum) | 3 |

The ten edited files:
- The six ScriptableObject definitions: base class and `[CreateAssetMenu]` dropped,
  `[Tooltip]` text kept as comments, and a `name` field standing in for `Object.name`.
- `GolemEntity` (below).
- `HardcodedDemoProgram`: `CreateInstance` → `new`, and warnings name the golem by id.
- `SpatialEndpointRegistry`: one qualified type name.
- `WorkbenchInteractionColors`: one method returned UGUI's `ColorBlock`.

**Tests: 1,105 pass and 0 fail under `dotnet test`, in about 0.6 s, with no engine
running.** That's 1,101 of Unity's 1,264 EditMode tests plus 4 that had to be PlayMode in
Unity. Every ported test file keeps its original `Assert` and `[Test]` counts. The edits
were setup only, applied by script and checked file by file.

The math shim (`Core/Compat/UnityMath.cs`) covers just the members the code uses, and
copies Unity's semantics where they differ from .NET's: `RoundToInt` rounds half to even,
`Lerp` clamps, and `Vector2 ==` is approximate. No test failed on a semantic difference.

### 2. Does the golem execution model hold up outside `MonoBehaviour`?

Yes, with no logic changes. `GolemEntity` (1,566 lines) became a plain class through renames
and signature changes only:

- The seven Holder references became the plain objects they only ever existed to own
  (`conveyorHolder.System` → `conveyor`).
- `OnEnable`/`OnDisable` became `Attach()`/`Detach()`.

`IdRoutedDemoRegressionTests`, which pins the reference programs, passes 10/10.
`GolemSignalTriggerTests` used to need PlayMode only because of `OnEnable`, and now runs as
four ordinary unit tests.

**In the engine:** `godot/Scenes/Sandbox.tscn` is a playable slice. The player walks the
floor and presses [E] at the station, and a Scavenger extracts scrap onto a five-cell belt
that a second golem unloads into a depot. A headless end-to-end check runs it for 600 ticks:

```
[spike-check] PASS after 600 ticks: extractor cycles=60, unloader cycles=57,
              depot scrap=58, scrap on belts=2
```

Nothing is lost: 60 extracted = 58 delivered + 2 still on the belt. The Godot layer is 11
scripts (841 lines), and every rule it uses is the ported Core one: floor shape, plank
pattern, movement, floor clamping, walk cycle, belt linking and cargo interpolation.

### 3. Is the Godot workflow better on this codebase, for Claude Code too?

Yes. This is the strongest finding, and it's about verification, not syntax.

| | Unity (this repo) | Godot (spike) |
|---|---|---|
| Run the rules' tests | Editor plus MCP bridge, or batch mode with the Editor **closed** | `dotnet test`: anytime, about 0.6 s |
| Compile the game | Editor or a hand-maintained `.csproj` | `--headless --build-solutions` |
| End-to-end check | PlayMode tests in the Editor | `--headless -- --spike-check`, exit code 0/1 |
| See the game | Screenshot through the bridge | `--write-movie` renders frames to PNG |
| Author a scene | `-executeMethod` scripts through `SerializedObject` (~5k lines of Editor tooling) | Write the `.tscn` text directly |
| Prefab overrides silently winning | Real hazard (`SceneProbe.Verify` exists for it) | No equivalent |

During the spike the Unity MCP bridge was down (connection refused), so none of the Unity
verification paths were available without you opening the Editor. Every Godot path ran
unattended. The hand-written `Sandbox.tscn` loaded on the first try.

## What resisted

| Item | Why | What it needs |
|---|---|---|
| 4 catalog test files (30 tests) | They read the authored `.asset` files through `AssetDatabase` | **Your decision:** authored data as Godot `.tres` resources or as JSON. See below. |
| `SaveLoadService` + save tests | Calls `building.GetComponent<…>()` 8 times | A small building interface in Core. Mechanical, but a real design step. |
| `SaveFileIO` | `Application.persistentDataPath`, `JsonUtility` | Godot layer: `user://` + `System.Text.Json`. |
| Station, assembly bay, build mode, repositioning tests | They test MonoBehaviours | Retarget at the new Nodes once those exist. |

Smaller Godot-side friction, all recorded in `godot/CLAUDE.md`:
- Naming a namespace `...Godot` shadows the engine's namespace.
- `FindChildren`'s type filter doesn't match C# script classes.
- Input actions in `project.godot` aren't hand-writable, so they're registered in code.
- GodotSharp 4.7.2 targets net8 while this machine has only the .NET 10 runtime. Targeting
  net10 works.

## What's left, by size

Unity runtime code not yet ported: **85 files, about 16.4k lines**, plus 5k lines of Editor
tooling that Godot mostly makes unnecessary (scenes are text).

| Area | Lines | Nature of the work |
|---|---|---|
| UI (Workbench, Ledger, HUD panels) | 6.6k | Rebuild in Control nodes. The rules underneath (`*Policy`, `*Rules`, `*Diagnostics`, `WorkbenchLoopLabels`) are already in Core and tested. The biggest piece of work. |
| Player (build mode, interactor, camera) | 3.1k | Rebuild input and ghosts. `BuildDragPath`, `BuildClickPolicy` and `BuildGhostVisuals` are already in Core. |
| Buildings (placeables, station, bench) | 2.4k | Thin Nodes over Core. |
| World (bootstrap, markers, expansion) | 2.0k | Mostly scene authoring, which is now writing `.tscn` files. |
| Save, belts' visuals, the rest | 2.3k | Small pieces. |

The port so far took one working session. The UI rebuild is the real cost, and it's
design work (layout, feel), not translation.

## Decisions for you

1. **Authored data format.** The punch cards, recipes, chassis and Clock Tower stages are
   Unity `.asset` files. The options:
   - **Godot `.tres` resources:** editable in the Godot inspector, but the definition classes
     would derive from `Resource`. That pulls Godot into Core, or needs a thin wrapper per
     definition.
   - **JSON loaded into the plain Core classes:** keeps Core engine-free, and the catalog
     tests run under `dotnet test`.

   I'd lean JSON, because it keeps the line that made this port cheap. A one-time converter
   from the existing `.asset` YAML would carry the current data over.
2. **Where the Unity project goes once Godot passes it.** Archive it on a branch, and turn
   `docs/history/unity-implementation-plan.md` into history.
3. **The Workbench's look.** It's the largest rebuild and the most player-facing. The
   mahogany-and-brass art in `Art/UI/` is reusable as 9-slice textures in a Godot theme.

## Not done (out of the spike's scope)

UI, save/load wiring, build mode, porting the art generators, and the PlayMode suite.
Keyboard input was compiled but not exercised by automation, so walk and [E] in a real
window once to confirm it. Nothing under `Assets/` was changed.
