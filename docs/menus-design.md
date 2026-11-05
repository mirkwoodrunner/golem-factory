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

1. **Settings.** A `GameSettings` Core object (audio, display mode, UI scale, text size, reduced
   motion, colour-blind mode, key bindings) read and written as one versioned file. The Settings tab
   grows sections. Rebinding: click an action, press a key; conflicts swap, Reset restores defaults.
2. **Accessibility.** Colour-blind-safe card colours (teal against copper): add a glyph per card
   family so colour is never the only cue. Reduced motion turns off camera pan, squash and puffs.
3. **Title screen** over a live (read-only, scripted) workshop; Continue, New, Load, Creative, Quit.
4. **Save slots** with thumbnails and metadata; autosave to a reserved slot on quit and on a tower
   stage change.
5. **Factory overview:** map overlay, production graphs from the Ledger's rates, alerts history.
6. **Creative Mode:** a `GameMode` on `SandboxWorld`; the wallet, unlock checks and guide read it.

## 3. Done when

Every setting survives a restart; every action can be rebound and the guide's key names follow;
Continue resumes the newest save; Creative saves cannot load as campaign; a scenario per step passes.
