Folder rules for the authored data. The root `CLAUDE.md` has the project overview, commands and settled rules.
Paths here are relative to `godot/` unless they say otherwise.

# Authored data (`godot/data/*.json`)

The chassis, logic cores, punch cards, recipes, Clock Tower stages, the City Office's
contracts (`contracts.json`, run by `order`; a contract's name is what a save keeps) and the
Assembly Line deck, plus `placeables.json` (the build menu) and `sandbox.json` (the world's setup). **They
are hand-edited.** They were generated from Unity's assets until the G10 cutover; the
converter is in git history at `unity-final`.

- **Keys are C# field names**, and references are by definition **name** (e.g.
  `"recipe": "R18_AetherConduit"`). Bools are `0`/`1` and enums are integers, as Unity
  serialized them.
- **`Core/Data/DefinitionLoader` is strict.** An unknown key, a reference to a missing name,
  an undefined enum value, or a bool other than 0/1 throws, naming the file and field.
  `WorldNode` loads the data at startup, so a bad edit fails on launch, not mid-game.
  - Don't "fix" a strictness failure by loosening the loader. It means the data and the
    classes disagree.
  - A key the JSON omits keeps the C# field's initializer.
- **Renaming a definition breaks saves that name it.** A save stores cards, chassis and
  buildings by name; a card the catalog can't resolve is dropped on load, with its slot's
  settings. **To change what the player reads, set a card's `displayName`** (the Workbench and
  the Assembly Line show `AppendageActionDefinition.Label`) and leave its name alone, as
  `ExtractScrap` does: it reads "Extract", since it takes whatever the stall behind holds.
- Tests read the real data through `Core.Tests/AuthoredData`, which loads fresh per call
  so no test can leak a mutation.

A price quoted in the guide comes from this data (`godot/Core/Tutorial/CLAUDE.md`); every
price must fit the reachable pack (`godot/Core/Economy/CLAUDE.md`).
