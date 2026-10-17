# golem-factory

Prototyping an automation game that could be implemented as either a video game or board game.

You play an Artificer who builds and programs clockwork **golems**: rigid, automated workers
that harvest resources, refine them, and haul them around a workshop floor. You don't control
golems directly; you assemble a *program* for each one out of punch-card-style parts, then set
it loose to run on its own. The long game is the **Clock Tower** in the town square, built in
stages from a whole factory's steady output.

The game is a **Godot 4** project in `godot/`. It started as a Unity prototype; the Unity project
was removed when the port was finished, and the tag `unity-final` holds its last state.

## Running it

You need **Godot 4.7.2 (mono / .NET)** and the **.NET 10 SDK**.

1. Open `godot/project.godot` in Godot and press **F5**. The main scene is
   `godot/Scenes/Sandbox.tscn`.
2. A step-by-step guide walks you through the first session, from gathering Scrap by hand to the
   Clock Tower. **F1** hides it.

To run the rules' unit tests without the engine: `dotnet test godot/GolemFactory.sln`.
`CLAUDE.md` lists the end-to-end scenarios and everything else a contributor needs.

## Controls

| Input | Action |
|---|---|
| `W` `A` `S` `D` | Move |
| `E` | Interact: **tap** to harvest a stall, order a truckload from an empty one, fuel a boiler, label a depot, or program a golem. **Hold** at the Hand-Crank Bench to crank. |
| `R` | Turn the build ghost, the golem you're carrying, the golem you're standing by, or the bench's recipe |
| `G` | Pick up the golem you're standing by; press again to set it down on the outlined tile |
| `1`-`9`, `0`, `-`, `=` | Pick a build-menu tile; `X` is Demolish |
| Left click / drag | Place a building; belts and pipes lay a run as you drag |
| `Escape` / right click | Put the placeable down |
| `Tab` | Management: Inventory, Assembly Line, Patents, Save/Load, the Ledger |

## Programming a golem (the Workbench)

Press `E` at a golem to open the **Workbench**:

- Drag a **Logic Core** (the trigger: *when* the golem acts) and up to a chassis-limited number
  of **Appendages** (the steps: *what* it does) into the sockets. A Haul step lets you pick which
  good it takes and how many.
- Nothing takes effect until you pull **ENGAGE**.
- **Patent** saves the program as a named blueprint you can stamp onto any golem later, from
  the Patents tab. It's free.

Golems run their program every tick, strictly in order, forever. If a step's input is empty or
its output is full, the golem **stalls** and retries; it never skips or improvises. A stalled
golem wears a badge saying what's wrong, and the alerts strip at the top names it too.

## The rest of the factory

- **Steam:** every golem needs a boiler's steam, carried along pipes, and boilers burn Coke.
- **The Assembly Line** offers the cards your factory can afford; claim one to use it.
- **Depots** open onto one shared stockpile; label one to make it take a single good.
- **Belts** carry goods a golem pushes onto them; a golem at the far end takes them off.
- **Demolish** refunds everything a building cost, so moving things around is free.
