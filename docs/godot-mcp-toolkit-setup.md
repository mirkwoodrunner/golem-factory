# Godot MCP Toolkit: local setup

How to connect Claude Code to the running Godot editor on the Windows PC through the
[Godot MCP Toolkit](https://store.godotengine.org/asset/npgamedev/godot-mcp-toolkit/)
(MIT, [GitHub](https://github.com/NPGameDev/godot-mcp-toolkit)). Written from the toolkit's README at version 1.1.2.
The docs site's troubleshooting and compatibility pages were not read.

**What it is for here.** Live playtest inspection: start the game, read live node state, take screenshots,
simulate input, read logs. It is not for authoring. The world is built from Core rules at load and scenes are
hand-written (`CLAUDE.md`), so scene-editing tools work against the project. Its language tools and debugger target
GDScript, and this project is C#.

**Where it runs.** On the PC only. The editor's server binds `127.0.0.1`, so Claude Code must run on the same machine
as the open editor. A cloud session can't use it.

## Before you start

- The Godot project is `godot/`, not the repo root. The plugin writes `.mcp.json` into the Godot project folder,
  i.e. `godot/.mcp.json`. Claude Code reads `.mcp.json` from the folder it is launched in, so launch it from
  `godot/`. The root `CLAUDE.md` still loads, since Claude Code reads parent folders too.
- Enabling the plugin edits `godot/project.godot`. Watch for that line in `git status`.

## 1. Check Node

`node --version` in PowerShell. The bridge needs Node.js 22 or newer.

## 2. Install the addon

- Open the project in the **mono** editor (`Godot_v4.7.2-stable_mono_win64.exe`). The standard build can't load C#.
- Asset Store tab, search "Godot MCP Toolkit", Download, Install. It lands in `godot/addons/godot_mcp_toolkit/`.
- When it asks whether it may install its own updates, say no for now. An update replaces that whole folder.

## 3. Enable it

- Project Settings → Plugins → Godot MCP Toolkit → tick **Active**.
- The Output panel should print `[MCPServer] listening on 127.0.0.1:6550` (any port from 6550 to 6560).
- An MCP dock appears in the bottom panel.

## 4. Write the config, read-only

Use the dock's read-only toggle, or write `godot/.mcp.json` by hand. Windows needs the `cmd /c` wrapper:

```json
{
  "mcpServers": {
    "godot-mcp-toolkit": {
      "command": "cmd",
      "args": ["/c", "npx", "-y", "@npgamedev/godot-mcp-server"],
      "env": { "GODOT_MCP_READ_ONLY": "1" }
    }
  }
}
```

Read-only mode hides every mutating tool, `execute_code` included. The tool list is fixed at connect, so after
turning it off, reconnect the client.

## 5. Keep it out of git for now

Add to `.git/info/exclude` (a local ignore list that is never committed):

```
godot/addons/godot_mcp_toolkit/
godot/.mcp.json
```

Revert the plugin line in `godot/project.godot` before any commit, or decide to commit all three on purpose. A committed
`.mcp.json` written on Windows differs from one written on macOS/Linux (`cmd /c` vs bare `npx`).

## 6. Connect

- Keep the editor open.
- `cd godot`, then `claude`.
- Approve the `godot-mcp-toolkit` server when asked. The dock's peer count goes up by one.
- First probe, read-only: "list the scene tree of Sandbox.tscn".

## 7. Check nothing else broke

- `dotnet test godot/GolemFactory.sln`
- `./Tools/Scenarios/run-all.ps1`
- `./Tools/Deck/export-linux.ps1` once, and confirm `addons/godot_mcp_toolkit` isn't in the pack. The toolkit says
  exports strip it, but the presets here are custom.

## Unknowns

- Whether a session launched from the repo root finds the editor through a root `.mcp.json`. The bridge matches
  projects through a machine-wide registry. Launching from `godot/` avoids the question.
- The scenario scripts run `--build-solutions` through a headless editor, which may load the plugin and start its
  server each time. Probably harmless, worth watching the first run.
- Whether breakpoints and stepping work on C#. Expect GDScript only.
