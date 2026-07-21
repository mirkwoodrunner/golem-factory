# Setting Up MCP for Unity (Windows)

This is a step-by-step guide to get **MCP for Unity** — the open-source
[CoplayDev/unity-mcp](https://github.com/CoplayDev/unity-mcp) bridge — running
locally on Windows, so Claude Code (or Claude Desktop) can drive the Unity Editor
directly: creating scenes/GameObjects, editing scripts, managing assets, running
tests, etc.

This is **not** Unity's official paid "AI Assistant" MCP (which lives behind a
Unity subscription in Project Settings > AI). MCP for Unity is free, MIT-licensed,
community-maintained, and explicitly supports Claude Code and Claude Desktop as
clients.

## 1. Install Unity Hub + Unity 6 LTS

1. Download and install **Unity Hub** from [unity.com/download](https://unity.com/download).
2. In Unity Hub, install a **Unity 6 LTS** editor version, including the **2D**
   template/module.

## 2. Create the Unity project inside this repo clone

**Status: done.** `ProjectSettings/` and `Packages/` are checked into `main`
alongside the `Assets/_Project/...` script scaffold, so a fresh clone can just be
opened directly in Unity Hub via **Add** (skip straight to step 3 below). The
steps here are kept for reference / for setting up a second machine from
scratch, since Unity Hub can't create a *new* project directly inside a
non-empty folder — the working sequence was to generate the project files in a
scratch folder and merge just the two Unity-generated folders in:

1. Clone this repo locally, e.g. to `C:\dev\golem-factory` — **not inside a
   OneDrive/Dropbox/Google-Drive-synced folder.** Cloud sync agents don't always
   fully materialize every file before a subsequent move/copy, and this caused
   `ProjectSettings/ProjectVersion.txt` (the tiny file Unity uses to identify
   which Editor version a project belongs to) to be silently dropped during
   setup. If a cloud-synced location is unavoidable, right-click each folder →
   "Always keep on this device" *before* moving anything, and don't proceed
   until the sync status icon shows fully synced (not the blue in-progress
   icon).
2. In Unity Hub: **New Project** → **2D (URP)** template → **Location**: any
   scratch folder outside the repo (e.g. `C:\dev\_scratch`), **Name**: anything.
   Let it fully generate and open once — confirm the Editor window actually
   loads (Hierarchy/Scene/Console visible) before moving on.
3. Close that project. Copy just the generated `ProjectSettings/` and
   `Packages/` folders from the scratch project into the repo clone root
   (`C:\dev\golem-factory`), next to the existing `Assets/`, `README.md`, and
   `docs/`. Do **not** copy the scratch project's `Assets/` folder — the repo's
   `Assets/_Project` scaffold is what you want to keep.
4. Delete the scratch project folder.
5. In Unity Hub, click **Add** (not New) → browse to `C:\dev\golem-factory` →
   select it. If Hub says "no project found," it means
   `ProjectSettings/ProjectVersion.txt` is missing or the wrong folder was
   selected — see Troubleshooting below.
6. Open it. Unity will import the existing `Assets/_Project/Scripts/**` files
   and auto-generate their `.meta` files on first import — check the Console
   for compile errors before continuing.

## 3. Add the remaining packages

**Status: done** (Input System, 2D Tilemap Extras, and Cinemachine v3 are
installed and committed via `Packages/manifest.json`/`packages-lock.json`).

The 2D URP template doesn't include everything the project plan
(`docs/unity-implementation-plan.md`) calls for. In **Window > Package Manager**,
add:
- **Input System** (new Input System)
- **2D Tilemap Extras**
- **Cinemachine** (v3)
- **Test Framework** (should already be present; confirm both EditMode and
  PlayMode are enabled)

The Package Manager window defaults to an **"In Project"** filter, which only
lists packages already installed — switch the dropdown at the top-left to
**"Unity Registry"** to find and install new ones.

TextMeshPro is usually prompted for automatically the first time a TMP component
is used.

## 3a. Commit the generated project files to git

Once the project opens clean, save the scene (**File > Save**, into
`Assets/_Project/Scenes/`) and the project (**File > Save Project**), then
commit `Assets/`, `Packages/`, and `ProjectSettings/` (all the new `.meta`
files, `manifest.json`, and the `ProjectSettings/*.asset` files) so the working
project is reproducible from a clean clone.

Add `*.slnx` to `.gitignore` alongside the existing `*.sln` entry — Unity/your
IDE regenerates the solution file automatically and it shouldn't be tracked.
`.vsconfig` is fine to commit either way.

**Windows-specific gotcha**: `git add`/`git commit` may intermittently fail
with `error: unable to write file .git/objects/...: Permission denied`. This is
a third-party antivirus (e.g. Norton 360) or Windows Defender real-time scanner
transiently locking newly-written objects, not a real permissions problem.
Retrying the same command usually makes forward progress (already-written
objects are skipped) and eventually succeeds, but the real fix is adding the
repo folder to your antivirus's exclusion list (in Norton: **Settings > Antivirus
> Scans and Risks > Items to Exclude from Auto-Protect... > Configure > Add**).

## 4. Install Python 3.10+ and `uv`

MCP for Unity's Python sidecar needs both. In PowerShell:

```powershell
winget install Python.Python.3.12
winget install astral-sh.uv
```

Restart your terminal afterward so `PATH` picks up both.

## 5. Install the Claude Code CLI

Use the native Windows installer (PowerShell):

```powershell
irm https://claude.ai/install.ps1 | iex
```

Then verify:

```powershell
claude doctor
claude --version
```

## 6. Install the MCP for Unity package

In the Unity Editor: **Window > Package Manager** → `+` → **Add package from git
URL...** → paste:

```
https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main
```

## 7. Run the setup wizard

After import, MCP for Unity opens a setup wizard automatically and checks that
Python and `uv` are detected (green). If either shows red, revisit step 4.

Then go to **Window > MCP for Unity > Configure All Detected Clients** — this
scans for installed MCP clients (Claude Code, Claude Desktop, Cursor, etc.) and
wires up their config automatically.

> **Claude Desktop note:** it only supports stdio transport. MCP for Unity will
> silently configure it that way even if you picked HTTP for other clients.

## 8. Approve the first connection

Go to **Edit > Project Settings > AI > Unity MCP Server** (or the MCP for Unity
settings page, depending on version) → **Pending Connections** → review the
client details and click **Accept**.

## 9. Verify end-to-end

With the Unity Editor open and the project loaded, start Claude Code in the
project folder and ask it to do something trivial, e.g.:

> "Create a cube at the origin and add a Rigidbody."

It should appear in the open scene within a few seconds. If it does, the bridge
is working — Claude Code (or whichever client you configured) can now use the
`Assets/_Project` scaffold from this repo as the surface to build on: attaching
`GolemEntity`/`SimulationClock` to GameObjects, wiring up the `_Project` prefabs,
authoring `LogicCoreDefinition`/`AppendageActionDefinition`/`ChassisDefinition`
ScriptableObject assets, etc.

## Troubleshooting

- **Switching transport mode (http ↔ stdio)**: if you change this in the MCP for
  Unity window, you must **restart Claude Code** for it to take effect.
- **`claude` not found / launched from Unity Hub**: if Unity was launched from
  the Start Menu/Hub shortcut rather than a terminal, its inherited `PATH` may
  not include `claude`. Either launch Unity Hub from a terminal where `claude
  --version` already works, or set the full path to the Claude executable
  explicitly in the MCP for Unity settings window.
- **More issues**: see the project's own
  [Common Setup Problems wiki page](https://github.com/CoplayDev/unity-mcp/wiki/3.-Common-Setup-Problems).

## Sources

- [CoplayDev/unity-mcp README](https://github.com/CoplayDev/unity-mcp)
- [MCP for Unity install docs](https://coplaydev.github.io/unity-mcp/getting-started/install)
- [Fix Unity MCP and Claude Code (wiki)](https://github.com/CoplayDev/unity-mcp/wiki/2.-Fix-Unity-MCP-and-Claude-Code)
