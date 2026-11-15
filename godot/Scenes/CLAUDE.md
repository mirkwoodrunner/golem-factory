Folder rules for the scene files. The root `CLAUDE.md` has the project overview, commands and settled rules.
Paths here are relative to `godot/` unless they say otherwise.

# Scenes (`godot/Scenes/*.tscn`)

`Sandbox.tscn`, the only scene, is **written by hand, as text**. The editor has never saved it: there is no
`uid=` on any `[ext_resource]` and no `unique_id=` on any node. Keep it that way.

- **Don't save a scene from the editor** unless that is the change you mean. A save adds `uid=` and `unique_id=`
  throughout and rewrites the ids, which turns a one-line change into a whole-file diff.
- **Keep the header as it is:** `format=3`. `load_steps` is the count of `[ext_resource]` plus `[sub_resource]`
  entries plus one (`Sandbox.tscn`: 29 + 0 + 1 = 30). Raise it when you add a resource.
- **A new resource takes the next number:** `id="<n>_<short name>"` (`28_docks`), unique in its file. Never reuse an
  id, and don't renumber the existing ones.
- **Never invent a `uid://` value.** Leave `uid=` off a new reference, as every existing one is.
- **References are by path only**, so moving or renaming a script breaks the scene that names it. Update the
  `path=` there, and move the script's `.cs.uid` file with it (root `CLAUDE.md`, "Engine rules").
- Node names follow the root rule: no `.` or `:`, no duplicate siblings. `World` (the `WorldNode`) stays the root's
  first child.

## A broken scene doesn't fail loudly

Measured on 2026-11-07, by pointing a copy of a scene at a script path that didn't exist:

- A plain run (`--quit-after`) logs `Parse Error: [ext_resource] referenced non-existent resource at: …` and still
  **exits 0**.
- A scenario run (`-- --scenario <name>`) **hangs**. `ScenarioRunner` never reaches a verdict, so there is no FAIL.

So after a hand edit, build, then run the `world` scenario with a timeout. Treat a `Parse Error` line, or no
`[scenario …]` line, as a failure. `Tools/Scenarios/run-all.ps1` ends a scenario that gives no verdict in time.
