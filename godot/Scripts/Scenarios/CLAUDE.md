Folder rules for the scenarios. The root `CLAUDE.md` has the project overview, commands and settled rules.
Paths here are relative to `godot/` unless they say otherwise.

# Scenarios

Current scenarios:
- `world` (Sandbox): the shell matches `SandboxLayout`, its tint and seven sconces are
  `LightingRules`', every cart on the street (the starting Scrap cart) publishes its endpoint, and
  the player walks down the street and through the town square to its far kerb and back,
  with the camera following.
- `build` (Sandbox): every placeable placed through the real build menu and cursor with
  synthetic mouse and key events, then drag runs, Escape, and a demolition that restores the
  pack exactly.
- `interact` (Sandbox): the player's `[E]`, hold-`[E]`, and `[G]` at the starting Scrap cart,
  the bench and the station, using real key and mouse events. Cranking the bench shows the
  Artificer's crank frame facing it and the bench's turning wheel; letting go stands.
- `golems` (Sandbox): every chassis built, and every `AppendageActionType` run on real steam:
  extract (from the Scrap cart, and a Coal cart added on its spot), haul, push, load, assemble
  plus repeat, refine, and freight. A golem out of steam
  must stall and wear its badge. Each boiler glows by `LightingRules.BoilerGlow`, and one
  run dry goes dark a frame later; every pipe wears its `_hot` picture exactly while it carries
  steam (`LightingRules.PipeHot`), so that boiler's run goes cold and a loose pipe never warms. Each golem draws its own chassis's frame for what it is doing,
  in its facing (`GolemFrames.Resolve`; the south base while a chassis has no frames).
- `golem-motion` (Sandbox): every chassis at its signature verb on steam, side by side on the
  street and zoomed in, with stocks fed and drained so none stops. Each body must make its
  work motion (`Core/Golems/GolemWorkMotion`): the Presser stamps, the Overclocker stamps and
  glows, the Scavenger tugs at the Scrap cart, the Hauler heaves and lunges, the Zeppelin
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
  It saves into Slot 1, so every save after the first and every load takes its second press.
  Writes only `user://scenario-saves`, never the player's saves.
- `tutorial` (Sandbox): the step-by-step guide's panel.
  - It opens on step 1, with the arrow pinned to the screen edge toward the Scrap cart and
    clear of the HUD bars.
  - Gathering advances it to "Buy a Coal cart", and the Boiler step outlines its build-menu row.
  - F1 hides and shows it, it steps aside over Management, and Skip guide puts it away.
  - In the Workbench on "Program it", it sits clear of the sockets, the lever, the vault and
    the hint's frames.
- `playtest-kit` (Sandbox): F9 once per chapter from a fresh game (all ten). The kit must
  fast-forward every guide chapter, leaving the golems and buildings a player would have
  built, and the report must record it. Its Save and Load steps use
  `user://scenario-saves/kit-save.json`.
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
  golem's [E] prompt names its problem; an extractor at a new, empty Coal cart waits grey, because
  the cart refills itself.
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
  `Diagnostics/EndgameLoad` builds 96 golems, 500 belt items and 12 boilers; seven phases of
  1,200 frames (flowing at 1× and 4×, starved, hover, unlit, world drawing hidden, and flowing at
  the farthest zoom, where off-screen skipping saves least) are timed with
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
- `audio` (Sandbox, muted): the sound (`docs/audio-design.md` §7), counted through
  `SandboxWorld.Audio.Requested`, never heard. Every slot in `data/audio.json` loads and the
  buses exist; the `golem-motion` row runs as its fixture and must request each work sound, and
  none over 90 paused frames; then the Workbench's card snap, Engage and Patent, a Claim, placing
  and demolishing a pipe, and E at a crate each request theirs exactly once.
- `feel` (Sandbox): game feel (`docs/game-feel-design.md` §6). The wheel steps to Near and the
  camera glides there and back; a depot beside the player lands with dust, pops a good when
  filled, and comes down in dust and is gone; a card dropped in the Workbench lands and the tape
  prints its line; a click on the alert strip glides the camera to a stopped golem and walking
  brings it back; a stage completing glides to the tower, shows the banner after the reveal with
  the clock running, and glides back. Captures `feel-place`, `feel-pop`, `feel-alert`,
  `feel-tower`. (`golem-motion` checks the sparks and the boiler's puffs.)
- `settings` (Sandbox): the Settings tab with real keys, clicks and the wheel
  (`docs/menus-design.md` §4). A key button listens; Tab, a digit and Shift are refused and it
  keeps listening, E and Escape never close the screen mid-capture, F binds Use, F for Carry
  swaps Use to G, and Escape or a tab change cancels. Then the rebind is live: the InputMap, the
  guide's step 1, the Pack hint, and at the Scrap cart "[G] Harvest" with G harvesting and E
  not. Reset keys restores E. UI scale 1.5 by a click on the track is kept but held at 1 in a
  1280x720 window, let through at 1920x1080 (the camera frames the same cells), and a 1024x576
  window is fitted to 0.8; every tab button stays on screen at each. The toggles take, the saved
  file reads back the same, and a hand-edited file with a bad key reads back safe. Writes only
  `user://scenario-settings.cfg`. Captures `screen-settings`, `settings-listening`,
  `settings-scale`, `settings-scale-world`, `settings-small-window`.
- `accessibility` (Sandbox, the `moods` street): Reduced motion and Colour-blind marks
  (`docs/menus-design.md` §5), switched on and off by clicks in the Settings tab. First the
  golems must bob, so "still" proves something. On: for 2 s no golem's body moves, scales or
  skews while their frames still change; the wheel cuts to a zoom level; a depot placed, filled
  and demolished has no landing, bump, dust or pop (withheld, counted) and is gone at once; the
  alert strip cuts the camera to the golem and back; the hovered golem's source tile is striped;
  a dropped card is settled at once and the tape's line whole; a tower stage cuts to the tower
  with no swell or flash, the banner shown. Off: the golems bob, a depot lands in dust, no
  stripes. Each `MotionPreference` gate, broken on purpose, fails it. Captures
  `access-settings`, `access-stripes`, `access-tower`.
- `title` (Sandbox, reloaded onto the title): the title screen (`docs/menus-design.md` §6) with
  real keys and clicks, across the scene reloads New game and Continue make
  (`IReloadingScenario`). With no save, Continue is greyed; the staged workshop has at least six
  golems drawn and working, none stopped, the camera at Near drifting with the workshop right of
  the menu, and the HUD, build menu and guide hidden. D, Tab, 1, I, F1, Escape and the wheel
  reach nothing. The arrows and S walk the entries past the greyed Continue, the pointer lights
  one, and Quit is chosen. Settings: a rebind (Escape cancels the listening, not the tab), Reset
  keys, Reduced motion stilling the drift, Back, Enter, Escape. New game reloads onto a fresh
  game with as many bus listeners as a fresh game has; the player walks. A save made there is
  offered on the title ("Saved today at ..."), and Enter on Continue brings back its depot and
  pack. A save spoiled after the title read it brings Continue back to the title with a notice,
  Continue greyed and the file untouched. Writes only `user://scenario-saves` (emptied at the
  end) and `user://scenario-settings.cfg`. Captures `title`, `title-settings`, `title-continue`,
  `title-notice`.
- `slots` (Sandbox, across reloads): the save slots and the autosave (`docs/menus-design.md` §7)
  with real keys and clicks. A save from before slots moves into Slot 1 and the title offers it
  (Continue, Load "1 save"); the title's Load lists the slots, the keys walk the ones that load,
  and Enter loads one. In the game, Tab and the Save / Load tab: the Autosave has no Save, an
  empty slot no Load or Delete; Save into an empty slot is at once, with its summary and, when
  windowed, its picture. Saving over, deleting and loading each wait for a second press, which
  another press or closing the screen lets go. A tower stage autosaves without counting as the
  player's Save; Quit to title autosaves and the title's Continue resumes it; Quit to desktop and
  the window's close each autosave first; the showcase never does. Writes only
  `user://scenario-saves`. Captures `title-load`, `slots-world`, `slots`, `slot-picture`,
  `title-autosaved`.
- `overview` (Sandbox): the factory overview (`docs/menus-design.md` §8) with real keys and
  clicks, on the guide's first six chapters (kept stocked) after a minute of work. Tab and the
  Overview tab: the map marks every golem, building and the player, no label holds an id, and
  the summary matches. The pointer names a golem; a click closes the screen with the camera on
  it, and walking brings it back. The graph draws at most four lines and the legend lists every
  good; the pointer reads the newest point; a legend click hides a line and a second draws it
  back in its colour; 1 hour and 10 min switch the range. Spent draws the same lines in the same
  colours, Coal in the legend spent but never made; Market lists every cart west to east, the
  Scrap cart taken from, with its tooltip; Alerts brings them back. The boilers run dry: the map rings
  the stopped golems and the alerts list them, still stopped; stoked, each says how long; a
  click on one (scrolled to) looks at it. Captures `overview`, `overview-look`, `overview-hour`,
  `overview-spent`, `overview-market`, `overview-alerts`.
- `creative` (Sandbox, across reloads): Creative Mode (`docs/menus-design.md` §9) with real keys
  and clicks. The title offers Creative ("Build freely"); Down and Enter start a Creative world
  with no guide, the HUD's panel reading "Creative world", the pack full of every good, every
  chassis on the Workbench and its saves in Creative's folder. 1 and a click place a depot for
  nothing. Save into Slot 1 lands in Creative's slots only, and so does Quit to title's
  autosave. The title's Continue reads "Creative, today at ..."; Load opens on Creative's slots,
  Left shows the campaign's (none), Right comes back, Enter loads the world with its depot. A
  campaign game greys a Creative save copied among its slots and refuses to load it. Captures
  `creative`, `creative-load`.
- `supply` (Sandbox): supply carts (`docs/supply-design.md`) with real keys and clicks.
  S and A walk to the spot at x = −20, drawn as its rope; the prompt offers a cart and E opens the
  panel, listing the carts the Ledger allows. A click adds a Coal cart for its price and the rope
  becomes the Coal cart; it fills on the clock; E takes from it; I opens its panel and a click
  buys a Rate rung. Saved to Slot 1, removed for half its price (the rope again), and loaded with
  two clicks: the cart is back at its rung, with one marker. Captures `supply-spot`,
  `supply-street`, `supply-cart`.
- `contracts` (Sandbox): city contracts (`docs/contracts-design.md` §9) with real keys and
  clicks. W walks up to the City Office: roped off, its prompt greyed, E refused with when it
  opens. Slag seen, the first contract posts and the rope comes down; E opens the notice (no
  id drawn) and a click takes it, the bonus counting down in plain words, the HUD and the
  prompt showing it. I opens the office's crate: a click puts one Iron Plate in, a Shift-click
  all the pack holds; a golem hauling from a depot fills the rest within par. The reward is in
  the pack, the bonus rung on the shelf; a Coal cart added, a click on the notice gives it the
  rung for nothing. The next contract posts and is taken, five delivered, saved to Slot 1,
  five more, loaded with two clicks: five again. Captures `contracts-roped`,
  `contracts-office`, `contracts-notice`, `contracts-taken`, `contracts-reward`,
  `contracts-next`.
- `endless` (Sandbox): endless play (`docs/endless-design.md`) with real keys and clicks. Before
  the tower, the bays at 100 say "more once the Clock Tower stands" with no button and the pack
  offers nothing past 250. The tower complete, two Upgrade clicks on the bay row reach the golem
  cap of 112, which the row then says; the Pack row's Upgrade makes the pack 300; I at a Coal
  cart at rung 3 and a click on Rate buy rung 4 in Regulators. With every authored contract
  done, the first generated one posts; E at the City Office shows it (no id) and a click takes
  it. The last stage's banner points at the office, and the Overview tab's running totals
  match the world's. At the civic works site (open, drawn open), the HUD follows the Tramway; its
  first stage's goods pushed into the site build it, and the HUD moves on to its second.
  Captures `endless-rungs`, `endless-cart`, `endless-contract`, `endless-won`,
  `endless-overview`, `endless-civic`.
- `docks` (Sandbox): the Docks (`docs/region-design.md`) with real keys and clicks. D east down
  the street stops at its end, the gate roped and no region named. A Zeppelin in the Ledger opens
  the gate and paves the Docks; D walks down the lane onto them and the HUD's plate names them.
  At a quay spot the prompt offers a cart, E opens the panel and a click adds a Coal barge. The
  Assembly Line tab's Docks row's Extend buys two rows, paved at once. Saved to Slot 1, the barge
  removed and the rows taken back, loaded with two clicks: both as saved. Captures `docks-gate`,
  `docks-open`, `docks-quay`.
- `golem-sales` (Sandbox): selling golems (`docs/golem-sales-design.md`) with real keys. The office
  open and a Scavenger built, the first golem order posts (Scavengers) and the HUD shows it. G
  picks up a fresh Scavenger and W walks it to the office: the prompt offers the sale and E sells
  it, gone for good with the hands empty and the HUD counting one. E empty-handed opens the notice
  with the order (no id drawn). A Golem Foundry fed by a golem hauling Scrap from a kept-stocked
  depot builds and ships Scavengers, its prompt and I's Crate screen saying so, its golems never
  in the world; the order completes, its reward in the pack, the next posted, and the running
  totals count the golems sold. Saved to Slot 1 mid-build, the Foundry demolished, loaded with
  two clicks: the Foundry, its build and the order as saved. Captures `sales-office`,
  `sales-notice`, `sales-foundry`, `sales-foundry-intake`.
- `gamepad` (Sandbox): the pad (`docs/steam-deck-design.md` D4) with joypad events only, after the
  playtest card's one click. Every pad action has its joypad event and the keys stay. The left
  stick walks, no faster on a diagonal; A harvests at the Scrap cart; X picks a golem up and sets
  it down. The hover card at the pad's cursor (D5.2): facing the golem shows its card, hung from
  the cell ahead; turning away hides it; the right stick looks back at it, drawing the brass tile;
  the pack opened over it hides both; a walk brings the cursor back ahead. Y opens the pack and Start the menu, B closes each. LB steps the build menu to the belt
  (its card showing, the pointer hidden), the cursor starts a cell ahead of the Artificer, the right
  stick steps it once and repeats while held; RT places a belt and its release ends the drag, RB
  turns it, RT held over the stick lays a run; B puts it down. LT takes the wrecking bar and RT
  takes a belt back. The d-pad zooms; Back pauses and plays. A mouse nudge hands the cursor back.
  **Parsed input lands a frame later**: check what a release did in the step after sending it.
  Headless ignores `Input.MouseMode`; read `InputDeviceNode.AppliedMode`. Captures `gamepad-cursor`, `gamepad-hover-ahead`,
  `gamepad-hover-look`, `gamepad-hover-floor`.
- `guide-ahead` (Sandbox): the guide's program step shown over the Workbench ahead of the guide
  (`docs/guide-chapters-design.md` §9). F9 four times reaches chapter 5; Iron Smelting is claimed
  by the kit's own claim; E at the station and a click on the Aether-Hauler's row build it, and the
  Workbench opens with the guide on "Depots for the smelter". The plate shows "Two Hauls, two
  goods" in the chassis rack, naming Coke, with no goods line or notice, clear of the lever, the
  vault and every socket. Following the glow (real drags, the good arrow, the dial, the lever;
  each glowing card inside the vault's window, STEP 4 never under the plate) engages the smelter
  in 9 moves, and the plate steps aside. Captures `guide-ahead`, `guide-ahead-step4`.
- `gamepad-ui` (Sandbox): the screens by pad (`docs/steam-deck-design.md` D6), joypad events only
  throughout; so far the playtest card, the station's panel, the Workbench (D6.W), the Crate, the
  Pack and the Management screen. The card first: it says the d-pad's left answers it, which
  takes the pad (the stick held then doesn't walk); B lets go with the question waiting; A on the
  note gives it focus and B takes it back; A on the second answer answers, harvesting nothing at
  the Scrap cart beside. A at the station opens the panel with the ring on the row the pack pays for; A on
  a greyed row says what's short; A builds, and the Workbench opens with nothing sent by that A and
  the ring on the hinted Always On. A sends Always On, Extract and Push Output, the ring following the
  hint to the lever; LB and the d-pad change nothing behind the screen. The d-pad walks the ring
  to STEP 1; RT and LT step its dial (reading `LT`/`RT`); X takes it out and A sends it back. With
  every step full, A moves nothing and says why. A Haul in STEP 2: RB and LB step its good
  (reading `LB`/`RB`; Coke deposited and the ledger polled so there is a second good). A on the
  lever engages, B closes. X carries the golem off the station (where A builds) and A at it
  reopens the Workbench; with every card offered, the d-pad walks the vault to its last card,
  scrolled into view. Y at a depot opens its Crate: A takes one, X what fits, A on the pack's row
  puts one back; A on the label button opens the picker on the crate's label, the status naming
  the option under the ring, A labels it, B closes the picker and then the screen; Take all; Y
  closes. With one of every good, Y opens the pack (hint "press Y") and the ring walks to its
  last row, scrolled into view; B closes. Start opens the menu: RB and LB change tab (LB heard by
  nothing behind it); on Settings the ring starts on Master, never on a key button; LT and RT
  step it; A ticks and unticks Mute; the right stick pans the Ledger; B closes. A at an empty cart
  spot opens the cart panel; the ring walks to the Coal cart and A adds it; B closes. With the
  first contract posted, A at the City Office's door opens the notice (nothing taken by that A),
  A takes it, Y closes. Last, with a save in a slot, a reload to the title: five stick motion
  events in a frame step the menu once (the camera unmoved); Settings by the ring (LT on Master);
  Load by A, the d-pad switching Campaign and Creative; A loads the slot. **Drive the d-pad a press and a release at a time**
  (the ring steps on the push, and repeats only after 0.3 s held). Captures `pad-card`, `pad-station`,
  `pad-workbench-open`, `-lever`, `-dial`, `-haul`, `-scroll`, `pad-crate`, `pad-crate-picker`,
  `pad-pack`, `pad-management-settings`, `pad-supply`, `pad-contract`, `pad-title-load`. Clears
  the scenario saves at the end.
- `perf` also takes `--perf-golems=N` (a load of N golems in the endgame's proportions) and
  `--perf-load=endless` (160), for measuring the golem cap (`docs/performance-report-endless.md`).

**A scenario that reloads the scene** implements `IReloadingScenario` (or derives from
`StepScenario`, which does): the runner of the new scene hands it back (`Resumed`), and it finds
its nodes again. Count a reload from before the
click or key that causes it; the reload can land between two steps.

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
