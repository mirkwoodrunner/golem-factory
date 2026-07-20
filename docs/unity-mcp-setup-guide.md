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

MCP for Unity needs an actual Unity project to attach to — this repo currently
only has the C# script scaffold (`Assets/_Project/...`) checked in, not
`ProjectSettings/` or `Packages/manifest.json`, since those are Unity-version- and
template-specific and safest to let the real Editor generate.

1. Clone this repo locally (branch `claude/unity-mcp-integration-u62310`) if you
   haven't already, e.g. to `C:\dev\golem-factory`.
2. In Unity Hub: **New Project** → select the **2D (URP)** template → set
   **Location** to the *parent* folder (`C:\dev`) and **Name** to `golem-factory`
   so Unity creates/populates its files directly inside the existing clone,
   alongside `README.md` and `docs/`, without touching them.
   - If Unity Hub refuses a non-empty target folder, create the project
     elsewhere instead, then move the generated `ProjectSettings/` and
     `Packages/` folders into the repo root and reopen it there via Unity Hub.
3. Open the project. Unity will import the existing `Assets/_Project/Scripts/**`
   files and auto-generate their `.meta` files on first import — check the
   Console for compile errors before continuing.

## 3. Add the remaining packages

The 2D URP template doesn't include everything the project plan
(`docs/unity-implementation-plan.md`) calls for. In **Window > Package Manager**,
add:
- **Input System** (new Input System)
- **2D Tilemap Extras**
- **Cinemachine** (v3)
- **Test Framework** (should already be present; confirm both EditMode and
  PlayMode are enabled)

TextMeshPro is usually prompted for automatically the first time a TMP component
is used.

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
