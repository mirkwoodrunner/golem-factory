Folder rules for golems. The root `CLAUDE.md` has the project overview, commands and settled rules.
Paths here are relative to `godot/` unless they say otherwise.

# Golem execution

- `ChassisDefinition`, `LogicCoreDefinition` and `AppendageActionDefinition` are shared,
  authored definitions (`data/*.json`). All five chassis share one `GolemEntity`/`GolemProgram`
  path; there is no per-golem subclassing.
- **Per-golem settings live on the program, never on a shared definition.** A Haul slot's
  batch size (`GolemProgram.appendageQuantities`) and good (`appendageItemTypes`) are the
  player's; writing them onto the card would retune every golem holding it.
- **The machine model** (`Golems/GolemInventory`): an input and an output stock per golem,
  12 per item type. `Haul`/`ExtractFromNode` fill input from the tile behind, `Push` empties
  stock onto the tile in front, `Assemble` converts input to output without touching a tile.
  **The pure-logistics rule:** a program with no Assemble step treats its input as its output
  (`GolemProgram.HasAssembleStep`); without it every logistics golem stalls full forever.
- **A Haul collects until it holds its batch** (`docs/onboarding-feedback-design.md` §13). It
  takes only batch − held of its good, stays on its step (`MissingItem`, "5/10 Scrap") while
  short, and advances once it holds the batch; extra stays in the source. Already holding a
  batch, it takes nothing and advances. `ExtractFromNode` still takes partial batches.
- **Execution is strictly linear and non-adaptive.** A failed precondition doesn't skip or
  reorder; the golem stalls and retries the same step every tick, publishing
  `GolemStalledEvent`/`GolemResumedEvent`. Rigidity is structural.
- `Repeat(n)` re-runs the preceding Assemble n more times as n sequential assemblies,
  Overclocker-only (`ChassisDefinition.allowsRepeat`), refused at assembly time. **n is capped so
  n + 1 runs fit the 12 hold** (`Golems/RepeatRules.MaxIterations`): the Workbench dial stops
  there, `GolemProgramWriter.TryWrite` refuses a Repeat behind no Assemble or one that can't run
  twice, and a load clamps n.
- Triggers: `AlwaysOn`, `Interval`, `Threshold` (edge-triggered), `Signal` (latches a fire that
  arrives mid-cycle).
- **`AppendageActionType` and `StallReason` are append-only**: serialized by integer index.
- **A golem is named by its `GolemId`**, never by a node or object name. **The player reads
  `UI/GolemNames`**: "Scavenger #4" (`Short`), "Clockwork Scavenger #4" (`Full`), the chassis's
  short name and the id's number. Places and goods go through `TileNames` and
  `ItemTiers.DisplayName`, and a stall through `StallDiagnostics.DescribeShort(golem, tiles)` /
  `Describe(golem, stall, tiles)`. Never print an id in player text; scenarios check drawn text
  with `UI/PlayerText.RawIdIn`.
- **A golem's `Mood` is a read-only classification** (`GolemMoodRules.Classify`), derived from
  state, stall reason and inventory. Presentation polls it. Add feedback by widening the mood,
  not by adding a second floating badge.
- **Red means the player must act** (`docs/onboarding-feedback-design.md` §3). A stall the
  factory will clear on its own is `GolemMood.Waiting`, grey, decided by `StallTriage` from the
  facts `World/StallFactsProbe` reads (`SandboxWorld.StallFacts`, wired in `OnGolemSpawned`),
  never by colouring a `StallReason` on its own. A golem with no facts source reads every stall
  but steam red, as before (steam is always blue, `Starved`). The view draws `GolemMoodRules.Presented` (Waiting only after its 2 s dwell);
  the alerts strip counts stopped golems only.
- **A new way to move, turn, carry, remove or reprogram a golem calls `GolemLayout.Changed()`**
  (`GolemEntity.SetPlacement`, `SetFacing`, `SetHeld`, `Remove`, a mast bound,
  `GolemProgramWriter`, `GolemProgram`'s card add and remove do). Within a frame the probe
  re-checks its index only when that count moves; a change it misses waits a frame at most.
  Views re-classify a golem at once on its own change and otherwise every
  `GolemMoodRules.ReclassifyEveryFrames` frames (`docs/performance-report-endless.md`).
- **The probe indexes, it never caches an answer.** `StallFactsProbe` keeps, per presentation
  frame (`SandboxWorld.PresentationFrame`), who pushes into and takes from each store, rebuilt the
  moment a golem moves, turns, is carried or changes whether it runs; everything else is read
  live. A per-frame cache of facts was tried and removed: a program written mid-frame read the
  old verdict. Profiling is opt-in by null (`SandboxWorld.Perf`, `NodePerf.Sampler`).
- **Golem badges share one `CanvasLayer` (`GolemBadges`) and one font.** A layer and a
  `FontVariation` per golem cost ~4 ms of render CPU at 96 golems, badges showing or not.
- **Hover is Core's** (§4): `SandboxWorld.Inspector` picks the golem, `GolemHoverText` and
  `TileNames` write every line, `Scripts/Golems/GolemHoverNode` only feeds the pointer and
  draws the card. A golem's source and target tiles are drawn by one path
  (`World/GolemTileOverlay`, `GolemNode`'s tiles) for carry, focus and hover alike.

# A golem's picture

- **Which frame a golem shows is Core's `GolemFrames`** (`docs/art-style-bible.md` §4):
  `FrameFor(mood, motion, strokePhase, carried, time, facing)` picks it (carry, then stall for
  Stalled/Starved, then the chassis's signature stroke by phase, else the idle pulse), and
  `Resolve` walks the fallback: the frame, the frame facing south, the base in its facing, the
  south base (`chassisSprite`), then `golem_generic_copper.png`. `GolemNode` only resolves the
  key (cached per chassis and key) and sets `_body.Texture`; the transform motion
  (`GolemWorkMotion`) still runs on top. Work frames play for the signature verb only
  (`GolemFrames.SignatureMotion`) and follow step progress, never time.
- **File names are built from `chassisSprite`** (`GolemFrames.FileName`): the south base is
  `<stem>.png`, every other frame `<stem>_<state>[_<n>]_<facing>.png`. Don't write a chassis's
  file name in code; a new chassis needs a signature verb (`GolemFramesTests` checks the data).
- A frame swap is a texture change on the one body sprite, never a node (invariant 14).

# The golem object

- **`GolemEntity` is a plain class.** `Attach()`/`Detach()` replace Unity's
  `OnEnable`/`OnDisable` (the Signal-trigger subscription). A Node that owns a golem calls
  them; a unit test calls them only if it wants Signal behaviour.

# Carrying a golem

**Where a carried golem lands is `PlayerInteractor.CarryDropCell`**, the tile under the
player's feet. `GolemNode` outlines it (red where `CarryDropBlocked`) with the golem's source
and target tiles around it, and `[G]` drops on it, so the preview and the drop read one
answer. The carried sprite rides half a tile above the player and is not a guide to it. While
carrying, `R` turns the carried golem, never a nearer one or a bench.

# Patents at the construction station

**A patent is used at the construction station**, not loaded into a bare golem.
`GolemConstructionStation.TryConstructFromBlueprint` builds the patent's chassis at the bare
cost with its program engaged, before the Workbench or `GolemSpawned` see the golem; the
panel's "From a patent" rows call it and close without opening the Workbench. Management's
Load still exists for reprogramming, but Tab does nothing while the Workbench is open, so it
was never a route from the Workbench. ENGAGE and the station write a program through one
function, `GolemProgramWriter.TryWrite`; keep it that way so the two can't drift.

The wrecking bar's golem rules are in `godot/Core/Buildings/CLAUDE.md`.
