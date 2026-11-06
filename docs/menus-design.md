# Phase 7: The game around the game

Branch `phase7/menus` (from `phase6/feel`). Design written 2026-11-05. Roadmap: `roadmap.md` Phase 7.

## 0. Invariants

1. Settings live in `user://settings.cfg`, never in a save (the audio settings already do).
2. Game rules stay in Core; the menus are nodes that draw and feed input. Settings values, slot
   metadata, autosave triggers and Creative Mode's rules are Core objects on `SandboxWorld` with tests.
3. Input stays controller-ready (decision 10): every key is a named action registered in code
   (`PlayerNode.RegisterInputActions`); rebinding changes an action's events, never a hard-coded key.
4. A load never clamps (`Economy/CLAUDE.md`). Save slots are versioned and hold no absolute paths.
5. Tests and scenarios never touch the player's saves or settings (scratch `user://scenario-*`).
6. Red means act, grey waiting, blue steam. Colour-blind modes may add a shape or pattern; they never
   reassign these meanings.
7. All text is printable Latin-1 plus the `font-glyphs` set.

## 1. User decisions (2026-11-05)

| # | Question | Decision |
|---|---|---|
| 1 | Build order | **Settings first**, then title and saves, overview, Creative Mode. |
| 2 | Creative Mode | **Sandbox with free building:** every definition unlocked, costs waived, no guide or tower stages. Its own save kind, never mixed with campaign saves. |
| 3 | Autosave | **On quit and on stage change only.** |

## 2. Steps

1. **Settings. Done** (§4). A `GameSettings` Core object (display mode, UI scale, reduced motion,
   colour-blind marks, key bindings) kept beside the audio settings. The Settings tab has Sound,
   Display and Keys. Rebinding: click an action, press a key; conflicts swap, Reset restores defaults.
2. **Accessibility. Done** (§5). Reduced motion stills every decorative motion; Colour-blind
   marks stripe a golem's source tile, the one place colour was the only cue.
3. **Title screen. Done** (§6). Over a live workshop the player watches but can't touch:
   Continue, New game, Settings, Quit. Load joins with step 4 and Creative with step 6.
4. **Save slots. Done** (§7). Five slots and an Autosave, each with a picture and a summary;
   autosave on quit and when a tower stage completes; Quit to title.
5. **Factory overview. Done** (§8). A Management tab: the map, an hour of production graphed
   from the Ledger's counts, and every time a golem stopped.
6. **Creative Mode:** a `GameMode` on `SandboxWorld`; the wallet, unlock checks and guide read it.

## 3. Done when

Every setting survives a restart; every action can be rebound and the guide's key names follow;
Continue resumes the newest save; Creative saves cannot load as campaign; a scenario per step passes.

A step is done when its new behaviour is driven by a scenario with real input, every scenario
passes, the diff has been reviewed and its findings fixed, and its screens have been captured and
looked at.

## 4. Step 1: settings (done 2026-11-05)

**Where it lives.** Rules in `Core/Settings` (`GameSettings`, `KeyBindings`, `KeyCapture`,
`KeyNames`); the file, the window and the InputMap in `Scripts/UI/GameSettingsStore`; the tab in
`Scripts/UI/SettingsTab`. Section `game` of `user://settings.cfg`, beside AudioNode's `audio`.
Scenario runs start from the defaults and write only `user://scenario-settings.cfg`.

**Calls made while building it** (none changes a game rule):

| # | Call | Why |
|---|---|---|
| 1 | Eight rebindable actions: the four moves, Use, Carry, Pack, Turn. | The actions registered in code. Turn joined because the guide and prompts name R. |
| 2 | Reserved, never bindable: Tab, Escape, F1, F9, the digits, Minus, Equal, X, Shift, Ctrl, Alt, Meta. | Each already does something (menu, cancel, guide, build-menu rows, Shift-click). |
| 3 | A rebind replaces the action's first key; the arrows stay as the moves' second keys. Taking a key another action has swaps the two. | The old key must stop working, and no action may be left without one. |
| 4 | Escape cancels a capture; leaving the tab or closing the screen cancels too. | The press goes to the capture before the screen's own keys, so E and Escape never close it mid-capture. |
| 5 | Every key name in player text reads the bindings (`KeyNames`): prompts, hover cards, the guide, the build menu, the Pack hint, the playtest questions. | A rebind that left "[E]" in the guide would teach the wrong key. |
| 6 | UI scale 1 to 2, capped by the window: the logical canvas never drops below 1280 x 720. A window smaller than that is fitted down. | Every screen is laid out in fixed pixels for 1280 x 720 (Ugui); a smaller canvas cut the tab strip off (seen in a frame). |
| 7 | No scale below 1. | At 0.75 the 12 to 14 px text was unreadable (seen in a frame). |
| 8 | The scale is the window's content scale, so the world grows with the UI; the camera still frames the same cells. | At 1920 x 1080 and 1.5 the game looks like the 1280 x 720 layout, only larger and sharper. |
| 9 | Fullscreen is borderless, not exclusive. | Alt-tab and overlays behave. |
| 10 | A settings file with an unknown, reserved or doubled key is sanitized on load; one that would leave an action keyless resets the keys. | A hand-edited file must not leave an action dead. |

**Not yet:** a UI scale above 1 at 1280 x 800 (the Steam Deck) needs screens that reflow, not a
setting: see `open-items.md` §3.

## 5. Step 2: accessibility (done 2026-11-05)

**Reduced motion** (`Core/World/MotionPreference`, on `SandboxWorld.Motion`, which the camera
director shares). One answer per kind of motion, so every node asks the same question:

| Stops | Stays |
|---|---|
| Dust, sparks, steam puffs, a good's pop and glint (`FxLayer` withholds and counts them) | A golem's animation frames: they say what it is doing |
| A building's landing and demolition squash, a crate's bump | Mood tints and badges |
| A golem's idle bob, work sway and stall shake, and the Overclocker's pulsing glow | The walk |
| Pulsing rings and the ghost's blocked pulse (held at their middle) | Popups: they fade where they appear, without rising |
| Camera glides and the follow ease: it cuts | The tower's stage and banner, on the same timeline |
| The tower's swell and flash | The Workbench's red refusal tint (colour, not motion) |
| A card's landing, the tape's typing, the lever's travel | |

None of it changes what the simulation does (`game-feel-design.md` §0.1).

**Colour-blind marks.** Measured first (`Machado 2009` severity 1 and CIEDE2000; above about 20
reads as different):

| Pair | Normal | Protan | Deutan | Tritan |
|---|---|---|---|---|
| Card teal / copper | 41.8 | 22.7 | 28.9 | 56.4 |
| Mood red / blue | 41.0 | 36.0 | 39.5 | 56.7 |
| Mood red / grey | 37.0 | 35.6 | 31.3 | 37.8 |
| Mood blue / grey | 20.2 | 18.4 | 20.6 | 22.0 |
| Source tile teal / target gold | 31.6 | **17.5** | 28.0 | 39.5 |

| # | Call | Why |
|---|---|---|
| 1 | No marks on cards. | A card's family is already in words wherever it is: the vault's "LOGIC CORES · triggers" and "APPENDAGES · actions", each subtitle's "trigger ·" or "action ·", an empty socket's "drop a logic core". Colour is not the only cue there, so a mark would only crowd the card. |
| 2 | The marks go on a golem's source tile: diagonal stripes (`ColourMarks.StripeAt`), so source and target differ in shape. | It is the weakest pair, and the guide steers by it ("press R until gold is on the depot"): the one place colour was the only cue. |
| 3 | With the marks on, the guide says "striped teal" and "plain gold" (tokens, like the key names). | The words must match what a player who can't see the colour sees. |
| 4 | The moods need no mark. | Their badges say what is wrong in words, and red/blue/grey stay at least 18 apart in every simulation. |
| 5 | The setting is "Colour-blind marks", not "... on cards". | It marks tiles. |
| 6 | No separate text size. | Text is the UI scale (§4, call 6); a size of its own would need the same reflowing screens as the Deck. |

## 6. Step 3: the title screen (done 2026-11-05)

**Where it lives.** The menu's rules in `Core/Menus` (`TitleMenu`, `SaveSummary`, `GameLaunch`,
`TitleShowcase`); the camera's drift in `CameraDirector` (`CameraFocus.Showcase`); the screen in
`Scripts/UI/TitleScreen`, the last node of `Sandbox.tscn`; the save's path in
`Scripts/UI/SavePaths`. The `title` scenario drives it.

**How a game starts.** The game opens on the title, which runs on the scene's own world. A
choice names the next launch (`GameLaunch.Request`) and reloads the scene; `WorldNode` takes the
launch first thing (`GameLaunch.Take`). New game is a fresh world; Continue is a fresh world
with the save loaded into it. A world comes off the static event bus when its scene goes
(`SandboxWorld.Dispose`), which the scenario checks by counting listeners after each reload. A
scenario or `--demo` run opens on a new game, as before, unless it asks for the title.

**Calls made while building it:**

| # | Call | Why |
|---|---|---|
| 1 | The entries are Continue, New game, Settings and Quit. Load arrives with step 4's slots and Creative with step 6, each tested then. | A button that does nothing yet, or says "coming soon", isn't finished work. Settings is here because a player wants sound and keys set before playing. |
| 2 | The workshop is the guide's first six chapters, built by the playtest kit's own actions (`TitleShowcase.Stage`) in about a third of a second, quietly (`SandboxWorld.Quietly`: no landing, dust, sound or popup). | It is the workshop the player is about to build, and it follows the guide whenever the guide changes. |
| 3 | A script keeps it running (`TitleShowcase.Keep`): it restocks stalls, stokes boilers, empties full crates, and keeps every Haul's source crate stocked. No golem goes red or blue in 20 minutes; a grey one waits on a full boiler. | Left alone, the same workshop needs a player within minutes (a test shows it). Red on the title would read as something to fix. |
| 4 | The camera drifts round the workshop's middle at Near, never faster than 0.4 cells a second, with the workshop framed right of the menu. On Reduced motion it holds still. A tower stage never takes it. | Slow enough to read as calm, not as a pan. |
| 5 | Nothing reaches the world behind the title: it is a full screen (the HUD, build menu, guide and prompts hide, the player stays still), and it swallows every key and click. | The workshop is watched, not played. |
| 6 | Keys: Up and Down (or the move keys) walk the entries, skipping a greyed one and wrapping; Enter or Use picks; the pointer lights what it is over. | Controller-ready (§0.3), and the keyboard and the pointer always agree on one highlight. |
| 7 | Continue offers the newest save that can be read. A save that can't be read greys Continue with "Your save can't be read". One that fails to load after the title read it brings the player back with a notice, and the file is left alone. | Continue must never lead nowhere, and a bad save must never be overwritten unasked. |
| 8 | Settings is the Management tab, on Management's plate, with Back where Management has Close. Escape returns too. | One settings screen to learn and to test. |
| 9 | The showcase asks no playtest questions, writes no report and has no save path. | Nobody is playing it. |

**Since step 4** (§7): the title has Load, Continue may resume the Autosave, and Quit to title
returns here.

## 7. Step 4: save slots and autosave (done 2026-11-05)

**Where it lives.** The slots in `Core/Menus` (`SaveSlots`: the files; `SaveSlotBoard`: what a
row says and offers, and the second presses); what a save says about itself in `SaveData.meta`
(`MetaEntry`); the autosave on `SandboxWorld` (`SaveFolder`, `AutosaveDue`, `Autosave`). In the
scene, `Scripts/UI/GameSaves` (the node `Saves`) writes a slot and its picture, takes the
autosave and leaves the game; `SaveSlotList` draws the rows for Management's Save / Load tab and
the title's Load. The `slots` scenario drives it.

**The files.** `user://saves/autosave.json` and `slot-1.json` to `slot-5.json`, each with its
picture beside it (`slot-1.png`). Scenario runs use `user://scenario-saves`. No absolute path is
written into a save (§0.4).

**Calls made while building it:**

| # | Call | Why |
|---|---|---|
| 1 | Five slots for the player and one Autosave, all on one screen. | A fixed list fits without scrolling, and a controller walks it. |
| 2 | Only the game writes the Autosave. The player can load it or delete it, not save into it. | A slot the game overwrites is no place for the player's own save. |
| 3 | Autosave on Quit to title, Quit to desktop and closing the window, and when a tower stage completes. Never on the title, never while a load replays a stage, and it isn't the guide's Save. | Decision 3. A stage is tens of minutes of a factory's output. |
| 4 | A save records its time (UTC), the guide's chapter, its golems, buildings and tower stages (`meta`). A load reads none of it back; a slot shows it. | Copying a file changes the file's time. The slot should say where the game was without loading it. |
| 5 | A slot's picture is the world alone, as the screen showed it: a second viewport draws the world's canvas with the screen's transform, so Management and the HUD aren't in it. 320 x 180, written a frame after its save. The old picture is deleted before the save is written. A headless run takes none, and the row says "No picture". | A picture of the menu tells the player nothing. A picture must never sit beside a newer save. |
| 6 | Saving over a slot that holds a save, loading in a game and deleting each ask for a second press: the button reads "Save over", "Load now" or "Delete now", and a line says why. Any other press or closing the screen lets it go. Loading on the title asks once. | One click must never lose a game. On the title there is nothing to lose. |
| 7 | In a game, Load loads in place, as before. The title's Load reloads the scene, as Continue does. | A load in a game was already pinned by the `save` scenario. The title's world is the showcase, which must not mix with play. |
| 8 | Quit to title and Quit to desktop live on the Save / Load tab, with "Both save to the Autosave first". There is no pause menu. | Tab is the game's menu, and leaving is about saving. |
| 9 | The save from before slots (`user://save.json`) moves into the first empty slot the first time the slots are read. With every slot full it stays where it is. | The player's game must still be there after the update. |
| 10 | Continue resumes the newest save that reads, the Autosave included, and says "Autosaved" when it is. Load is greyed with nothing to load and says how many saves it offers. | Quitting and coming back should be one click. |
| 11 | The playtest kit's save step writes `kit-save.json` in the same folder, which is no slot. | F9 must never write over a slot. |
| 12 | The save and load lines count properly ("1 golem", "1 building"). | They're player text. |
| 13 | Closing the window autosaves on the next frame: the close notification reaches nodes while the tree is busy, and the picture's viewport can't be added until it is done. One picture viewport serves the scene: freeing one that shares the world's canvas tripped the renderer. | Both were found by the scenario's windowed run. |

**Not yet:** a slot shows no play time. A save from a newer build isn't told apart from a
damaged one. Creative Mode's saves need a kind of their own so they can't load as a campaign
(step 6).

## 8. Step 5: the factory overview (done 2026-11-05)

**Where it lives.** The rules in `Core/Overview`: `FactoryMap` (what the map shows),
`ProductionHistory` (an hour of what was made, on `SandboxWorld.History`), `ProductionChart`
(the graph's range, lines and colours) and `AlertLog` (every stop, on `AlertsStrip.Log`). The
camera's `LookAt` and `SandboxWorld.FocusGolem` take the camera to a picked golem. The tab is
`Scripts/UI/OverviewTab`. The `overview` scenario drives it.

**Calls made while building it:**

| # | Call | Why |
|---|---|---|
| 1 | The overview is a Management tab, first on the bar, not a screen with a key of its own. The seven tabs are 138 px wide (six were 150). | Tab is the game's menu, and a new key would need a rebindable action and a row in Settings for one screen. |
| 2 | The map draws the workshop, the street and the town square to scale: what the player built by kind, the stalls, the tower, every golem by its mood, and the player. A stopped golem (red or blue) is ringed and drawn larger, so it isn't colour alone, and a key along the bottom names every colour in words. | The map answers "is anything wrong, and where", at a glance. |
| 3 | The pointer names what it is over in player names (the hover card's state for a golem). A click on a golem closes the screen with the camera on it, as the alerts strip's click does; walking brings the camera back. | The map leads to the problem, not just to its picture. |
| 4 | The graph reads the Ledger's own counts (fresh goods only, `ProductionTally`), in ten-second buckets on the player's time: a paused factory makes nothing. Ten minutes (a point every ten seconds) or an hour (a point a minute). It isn't saved: a session starts with an empty graph. | "Production graphs from the Ledger's rates" (§2): the graph and the Ledger never disagree. |
| 5 | At most four lines, each named at its end, in four colours validated as a set against the panel (dark mode, worst adjacent CVD ΔE 8.4, every pair at least 3:1). The legend lists every good made in the range with its average and is the table view; a click on a row draws or hides its line. A good keeps its colour while drawn, and a fifth is refused until one is hidden. | Colour follows the good, never its rank: a click must never repaint the others. |
| 6 | The pointer on the graph draws a crosshair and gives each line's rate at that point. | The values are readable without a number on every point. |
| 7 | The alerts history is fed by the alerts strip's own reconcile, so the two always agree on what counts: a golem waiting on the factory (grey) is never an alert. Each stop records the strip's sentence, when, and how long it lasted ("still stopped · 1:05"); a new cause is a new entry. The newest are first. At most 40; an open stop is never dropped. Not saved. A click looks at the golem. | Every stop the strip showed for half a second can be found again afterwards. |
| 8 | The rows are rebuilt only when an entry or a good comes, goes or ends; the times and rates are written in place. | Rebuilt every second, a row could be swapped out between the press and the release of a click. |
| 9 | The hover card names a working golem's step as the Workbench does ("Push Output"), not by its id. | The map's labels found it: the card printed "PushOutput". |

**Not yet:** the graph shows goods made, not goods spent; the map doesn't pan or zoom (the
whole world fits); a stop's history isn't kept across a load.
