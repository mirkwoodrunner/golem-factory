Folder rules for the tick simulation. The root `CLAUDE.md` has the project overview, commands and settled rules.
Paths here are relative to `godot/` unless they say otherwise.

# The tick simulation

Everything golem- and belt-related runs off one fixed-tick loop (`Core/Simulation/
SimulationClock`), which calls `ITickable.Tick(tick)` on every registrant in registration
order, never per-object frame updates. Play, pause and speed are independent of tick
advancement. Simulation is plain, data-oriented C#: no ECS, no per-item nodes.

- **A tick allocates nothing**, in the bare simulation and in the whole composed world
  (`SimulationStressTests.TheTick_AllocatesNothing`, `TheComposedWorldsTick_AllocatesNothing`).
  What usually breaks it: a `foreach` over an interface (`IEnumerable`, `IReadOnlyDictionary`
  box an enumerator; walk a `List` or the dictionary itself), a lambda that captures (a LINQ
  query in a `Tick`, a `Settle` or a bus handler), a string built on every retry of a stall.
  A handler on `EventBus` runs inside the tick that published, so it is held to the same rule.
- **A tick is the same simulation however it arrives**: run twice, built in another order, or
  sliced into frames of any length (`SimulationStressTests`, comparing `StressLoad.Checksum`
  tick by tick). Never let a `Dictionary`'s or a `HashSet`'s order decide an outcome: it follows
  removals. `ConveyorSystem` walks belts in the order they were registered, oldest first, which
  decides which of two belts feeding one gets the free slot.
- **Nothing per golem listens on the bus.** A golem hears `GolemCompleted` only while its core
  is a Signal trigger (`GolemEntity.RefreshSignalSubscription`), and its view reads
  `GolemEntity.CompletedCycles` and `StallIncidents` on its own frame. A listener per golem
  makes every event cost the golem count: at a thousand golems that was over half a tick.
- **Steam's claims stand between ticks** until something is built or moved, or a boiler runs
  dry or is lit (`SteamNetwork.Evaluate`, `FuelChanged`). A new input to who is powered must
  mark the evaluation dirty, or be added to that check.
- **Cells are looked up in `World/SpatialHashGrid`** (endpoints, occupancy): flat arrays, no
  allocation once sized, queries into a caller's `Span`. A query promises no order among its
  results; pick by `CellOrder` or an id where order matters.
- **The measures**: `dotnet test godot/GolemFactory.sln -c Release --filter Category=Perf` runs
  `Diagnostics/StressLoad` (1,024 golems, 10,000 ticks, twice: time, bytes, checksums) beside
  the endgame profile. `StressLoad` is the tick alone, past the game's cap; `EndgameLoad` is the
  whole world at the cap.
