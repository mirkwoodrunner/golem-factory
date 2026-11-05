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
3. **Title screen** over a live (read-only, scripted) workshop; Continue, New, Load, Creative, Quit.
4. **Save slots** with thumbnails and metadata; autosave to a reserved slot on quit and on a tower
   stage change.
5. **Factory overview:** map overlay, production graphs from the Ledger's rates, alerts history.
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
