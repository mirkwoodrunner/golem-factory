Folder rules for the scenarios. The root `CLAUDE.md` has the project overview, commands and settled rules.
Paths here are relative to `godot/` unless they say otherwise.

# Scenarios

Current scenarios:
- `world` (Sandbox): the shell matches `SandboxLayout`, its tint and seven sconces are
  `LightingRules`', all nine stalls publish endpoints, and
  the player walks down the street and through the town square to its far kerb and back,
  with the camera following.
- `build` (Sandbox): every placeable placed through the real build menu and cursor with
  synthetic mouse and key events, then drag runs, Escape, and a demolition that restores the
  pack exactly.
- `interact` (Sandbox): the player's `[E]`, hold-`[E]`, and `[G]` at a stall, an empty stall,
  the bench and the station, using real key and mouse events. Cranking the bench shows the
  Artificer's crank frame facing it and the bench's turning wheel; letting go stands.
- `golems` (Sandbox): every chassis built, and every `AppendageActionType` run on real steam:
  extract, haul, push, load, assemble plus repeat, refine, and freight. A golem out of steam
  must stall and wear its badge. Each boiler glows by `LightingRules.BoilerGlow`, and one
  run dry goes dark a frame later. Each golem draws its own chassis's frame for what it is doing,
  in its facing (`GolemFrames.Resolve`; the south base while a chassis has no frames).
- `golem-motion` (Sandbox): every chassis at its signature verb on steam, side by side on the
  street and zoomed in, with stocks fed and drained so none stops. Each body must make its
  work motion (`Core/Golems/GolemWorkMotion`): the Presser stamps, the Overclocker stamps and
  glows, the Scavenger tugs at the Scrap stall, the Hauler heaves and lunges, the Zeppelin
  crouches and hops. Each also picks its stroke's three work frames (`Core/Golems/GolemFrames`),
  and a chassis with no frame files draws its south base throughout. It is also the fixture for
  capturing the motions with `--write-movie`.
- `workbench` (Sandbox): program a golem end to end through the Workbench with real mouse
  drags. Also covers socket highlights, the dial, failed drags leaving no orphans, Engage,
  Patent, and close/reopen. Its golem is the guide's first, so it checks the next-drag hint
  first: the frames on the vault card and the socket, real drags following them (a wrong card
  keeps the hint, and a drop on a filled socket replaces), the lever framed last, ENGAGE
  clearing it, and no frame without a drop changing the draft.
- `management` (Sandbox): the HUD (alerts strip, pause and play), and Tab opening Management
  with the HUD and build menu hidden. Covers the Inventory icons, the tab exclusivity, an
  Assembly Line claim reaching the Workbench, the tab's `Pack:  ·  Post:` header, the Pack row's Upgrade (the Pack screen's cap line
  reads 150), two Extends planking and walling new rows (the second at 120 Scrap), Patents
  Load, the Ledger's plaques and recipe pane, no drawn label holding an internal id, and one
  screen at a time.
- `save` (Sandbox): through the Save / Load tab, build, save, wreck everything, and load. The
  buildings, golems (with program and place) and pack must come back exactly, and two
  more rounds must change nothing. Then it extends the room after a save and loads, and the
  rows and back wall must go back, and a pack upgraded after a save loads back at the saved rung.
  Writes `user://scenario-save.json`, never the player's save.
- `tutorial` (Sandbox): the step-by-step guide's panel.
  - It opens on step 1, with the arrow pinned to the screen edge toward the Scrap stall and
    clear of the HUD bars.
  - Gathering advances it, and the Boiler step outlines its build-menu row.
  - F1 hides and shows it, it steps aside over Management, and Skip guide puts it away.
  - In the Workbench on "Program it", it sits clear of the sockets, the lever, the vault and
    the hint's frames.
- `playtest-kit` (Sandbox): F9 once per chapter from a fresh game (all ten). The kit must
  fast-forward every guide chapter, leaving the golems and buildings a player would have
  built, and the report must record it. Its Save and Load steps use
  `user://scenario-save.json`.
- `loop` (LoopSlice): the station builds a Scavenger, which mines onto the belt, which the unloader hauls
  into the stockpile, over 600 ticks. `--spike-check` is an alias.
- `inventory` (Sandbox): the Pack and Crate screens with real keys and clicks. I opens the pack
  away from a crate and the crate's screen at one; I, E and Escape close them. A click moves
  one, Shift-click all that fits, and crate + pack per good never changes. The label
  picker relabels the depot, a label refuses goods on the way in only, and a pack above its
  cap gives but takes nothing back. A mast's label is disabled; at a Post crate the left panel
  is the Pneumatic Post, a Shift-click fills the store every Post crate shares, and the Pack screen
  lists it under the pack. The guide's plate clears both
  panels, and the wrecking bar refuses a crate whose goods won't fit in the pack (the Post's
  room doesn't count), then refunds it exactly.
  Logs the frames worth a look for `--write-movie`.
- `moods` (Sandbox, `Scripts/Scenarios/StreetFixture`'s street on steam): a golem a sleeping
  feeder will serve goes grey after its dwell, badge "waiting for Scrap", no shake, uncounted,
  and the strip reads "No golem needs you · 1 waiting for goods."; a collecting Haul shows
  "2/5 Scrap"; facing bare floor, pushing into a full crate (red) and off the steam grid (blue)
  are counted; a straining golem wears no badge; a golem set to haul from the full crate turns
  its feeder grey; carrying the feeder away (G) turns the waiting golem red at once; a stuck
  golem's [E] prompt names its problem; an empty Coal stall is red until a truckload is ordered.
  Every badge, the strip and the prompt read in player names (`UI/PlayerText.RawIdIn`), and
  no two badges overlap.
- `hover` (Sandbox, the same street): the real pointer on a golem's cell, or the cell above its
  feet, lights its source and target tiles with arrows at z −1 and shows its facing arrow, which
  no unhovered golem draws; its card is Core's text, a stuck
  golem's names the tile, the good and the fix, and its badge hides under the card; a straining
  golem's card says "nearly full"; nothing is hovered over the build menu or the Workbench; a
  carried golem's tiles follow its landing while another is hovered. A card names the golem
  and no id, and a tile's arrow rides above the crate that covers it. A waiting golem's card
  names its feeder (`StallFactsProbe.FeedersOf`, `onboarding-feedback-design.md` §17): while S
  sleeps, P's reads "Scavenger #1 brings it when its trigger fires" (captured as
  `critique-hover-feeder`); once S wakes, "brings it" without the clause.
- `font-glyphs`: the project font covers printable Latin-1 plus → ≥ █ ░.
- `perf` (Sandbox): the endgame profile (`docs/performance-report-template.md` §4). Core's
  `Diagnostics/EndgameLoad` builds 96 golems, 500 belt items and 12 boilers; six phases of
  1,200 frames (flowing at 1× and 4×, starved, hover, unlit, world drawing hidden) are timed with
  `SandboxWorld.Perf` (Core's sections) and `NodePerf` (the nodes'). Windowed it must hold frame
  p95 ≤ 16.6 ms and max ≤ 33.3 ms, and the lighting (flowing-1x less unlit render CPU p95)
  ≤ 1.0 ms; headless, Core's share p95 ≤ 6 ms. One `[perf]` line a
  phase, and the report's tables in `user://perf-report.md`. About a minute headless. The last
  filled-in report is `docs/performance-report-phase3.md`.
- `art` (Sandbox): the art against `Tools/Art/art_manifest.json` (`docs/art-pipeline-design.md`
  §7.1). Every entry loads at its size and every `res://art/**.png` has one; every name the data
  and the code build exists (the data's `.png` names, `ItemIcons.PathFor` for every good, the
  tower, the walk frames), and every chassis's 28 `GolemFrames` names resolve, through the
  fallback while its set hasn't landed. Core's `Diagnostics/ArtGallery` stands each chassis's 28
  frames in a grid, pinned (`GolemNode.PinnedFrame`), captured as
  `art-golems-<chassis>-{default,close,wide}.png`, plus each frame cut out at each zoom (the camera
  aimed at it first, `ArtGallery.CropRect`) as `<frame file stem>-<zoom>.png`, the review sheet's
  in-game cells; and every standing sprite's lowest opaque row
  sits on its node's origin within the manifest's allowance. A new sprite needs its manifest
  entry or this fails.

**Screen captures:** a windowed run with `--capture-dir=<dir>` writes `screen-*.png`, one per
screen (`world` the room, `management` the HUD and every tab, `build` the menu, `tutorial` the
playtest card and the guide panel, `interact` the construction panel, `inventory` the Pack and
Crate screens), beside the `critique-*` shots. They are deterministic under `--fixed-fps 60`,
so an art change that shouldn't move a screen is checked by comparing them pixel for pixel
(`docs/art-pipeline-design.md` §3.1).

**Scenario input:** `Input.ParseInputEvent` **without** `FlushBufferedEvents` for key presses,
and a tap must hold the key down across at least one frame. A press and release in the same
frame, or a flush from inside `_Process`, never reads as "just pressed" to a node that has
already run that frame.

A scenario that clicks a screen's control: see `godot/Scripts/UI/CLAUDE.md` (a control has no
size until the frame after it's built). Scenario runs never write the player's save or
playtest report: see `godot/Core/Tutorial/CLAUDE.md`.
