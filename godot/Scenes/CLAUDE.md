Folder rules for the scene files. The root `CLAUDE.md` has the project overview, commands and settled rules.
Paths here are relative to `godot/` unless they say otherwise.

# Scenes (`godot/Scenes/*.tscn`)

`Sandbox.tscn` and `LoopSlice.tscn` are **written by hand, as text**. The editor has never saved them: there is no
`uid=` on any `[ext_resource]` and no `unique_id=` on any node. Keep them that way.

- **Don't save a scene from the editor** unless that is the change you mean. A save adds `uid=` and `unique_id=`
  throughout and rewrites the ids, which turns a one-line change into a whole-file diff.
- **Keep the header as it is:** `format=3`. `load_steps` is the count of `[ext_resource]` plus `[sub_resource]`
  entries plus one (`Sandbox.tscn`: 27 + 0 + 1 = 28). Raise it when you add a resource.
- **A new resource takes the next number:** `id="<n>_<short name>"` (`28_docks`), unique in its file. Never reuse an
  id, and don't renumber the existing ones.
- **Never invent a `uid://` value.** Leave `uid=` off a new reference, as every existing one is.
- **References are by path only**, so moving or renaming a script breaks every scene that names it. Update the
  `path=` in both scenes, and move the script's `.cs.uid` file with it (root `CLAUDE.md`, "Engine rules").
- Node names follow the root rule: no `.` or `:`, no duplicate siblings. `World` (the `WorldNode`) stays the root's
  first child.

## A broken scene doesn't fail loudly

Measured on 2026-11-07, by pointing a copy of `LoopSlice.tscn` at a script path that didn't exist:

- A plain run (`--quit-after`) logs `Parse Error: [ext_resource] referenced non-existent resource at: …` and still
  **exits 0**.
- A scenario run (`-- --scenario loop`) **hangs**. `ScenarioRunner` never reaches a verdict, so there is no FAIL.

So after a hand edit, build, then run the scenario that loads the scene (`world` for `Sandbox.tscn`, `loop` for
`LoopSlice.tscn`) with a timeout. Treat a `Parse Error` line, or no `[scenario …]` line, as a failure.
