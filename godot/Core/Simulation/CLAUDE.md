Folder rules for the tick simulation. The root `CLAUDE.md` has the project overview, commands and settled rules.
Paths here are relative to `godot/` unless they say otherwise.

# The tick simulation

Everything golem- and belt-related runs off one fixed-tick loop (`Core/Simulation/
SimulationClock`), which calls `ITickable.Tick(tick)` on every registrant in registration
order, never per-object frame updates. Play, pause and speed are independent of tick
advancement. Simulation is plain, data-oriented C#: no ECS, no per-item nodes.
