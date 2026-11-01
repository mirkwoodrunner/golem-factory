# Audio: Phase 5

Written 2026-11-01 for Phase 5 (`docs/roadmap.md`, "Phase 5: Audio"), on branch `phase5/audio`
(from `phase4/art`). The game has no sound today: no audio file, no player node, no bus. This
spec says what sounds, when, how loud, where the placeholder files come from, and how a final
asset replaces one. The user's open decisions are in §9; nothing in §2–§7 that depends on one is
built before it is made.

Roadmap decision 9: **placeholders for now**, built so a final asset drops into the same slot.
Whether anything is commissioned is the user's call and isn't planned here.

## 0. Invariants

1. **Every sound is a slot.** A slot has a name (`work.presser.stamp`), a file path, a bus and
   its mix values, all in one data file (§3). Code plays slots by name, never a path. Replacing a
   placeholder is a new file at the slot's path, or a new path in the data: no code change.
2. **Game rules decide what sounds; nodes only play it.** Which slot a moment plays, how loud and
   how many at once is Core (`Core/Audio/`), tested without the engine, like `LightingRules`.
   The Node layer feeds it events and positions and plays what it returns.
3. **Sound follows the simulation clock, not the frame.** A work sound lands on the stroke's
   impact (`GolemWorkMotion.Pose.Impact`), which is driven by step progress, so at 4× it plays
   four times as often and while paused it doesn't play at all. Ambience and music keep playing
   while paused.
4. **Red means the player must act.** The stall chime plays for a red stall only (a golem the
   player has to fix), never for grey waiting or blue no-steam. One chime per stall, not per
   retry.
5. **A big factory doesn't turn to noise.** At most N copies of one slot play at once (§5), a
   sound is panned and quieted by its distance from the screen centre, and one off screen past a
   margin doesn't play.
6. **Tests and scenarios are silent.** A run under `--scenario` or `dotnet test` plays nothing
   audible; scenarios check what was asked to play, not what was heard.

## 1. Where things stand (2026-11-01, at ed50ed0)

- No audio anywhere: `godot/` has no `.wav` or `.ogg`, no `AudioStreamPlayer`, and
  `project.godot` sets no bus layout.
- The hooks exist:
  - `GolemWorkMotion.Evaluate` returns a `Pose` with `Impact` true on the frame a stroke lands,
    per chassis and verb (the Presser's stamp, the Overclocker's quicker stamp, the Scavenger's
    tug, the Hauler's heave, the Zeppelin's hop).
  - `EventBus` publishes `GolemStalledEvent`, `GolemResumedEvent`, `GolemCompletedEvent`,
    `ItemAssembledEvent` and `ClockTowerStageCompletedEvent`.
  - The Workbench's drop, Engage lever (`WorkbenchLeverMotion`) and Patent, and the Assembly
    Line's Claim, each go through one Core call the cue can hang off.
  - `SteamNetwork.LastEvaluatedWorkingCount` is the boilers' load, for the steam hiss.

## 2. The sounds

The roadmap's list, as slots. "Trigger" is the one place in code that asks for it.

| Slot | Bus | Trigger | Notes |
|---|---|---|---|
| `amb.workshop` | Ambience | always, looped | low room hum |
| `amb.clock` | Ambience | each game second while the clock runs | the ticking clock; silent while paused |
| `amb.steam` | Ambience | looped; volume from the working golem count | hiss rising with boiler load, silent with no boiler burning |
| `work.<chassis>.<verb>` | Effects | `Pose.Impact` on the golem's node | one per chassis signature verb (5), plus `work.any.haul`, `work.any.push` and `work.any.extract` for the shared verbs |
| `ui.card-snap` | UI | a card dropped into a Workbench socket | |
| `ui.engage` | UI | the Engage lever thrown | |
| `ui.patent` | UI | Patent pressed | a stamp |
| `ui.claim` | UI | a card claimed on the Assembly Line | |
| `ui.stall-chime` | UI | a golem turns red (invariant 4) | positional (§5) |
| `music.layer.1`…`music.layer.4` | Music | looped together, layers faded in by factory size (§4) | one tempo and key, so they stack |
| `music.tower-fanfare` | Music | `ClockTowerStageCompletedEvent` | ducks the layers while it plays |

Decision 3 (§9) is whether to add a few slots beyond the roadmap's list.

## 3. Data: `godot/data/audio.json`

One entry per slot: `id`, `path` (`res://audio/...`), `bus`, `volumeDb`, `maxCopies`,
`positional` (true for anything a golem or building makes), and for loops `loop: true`. Loaded by
`DefinitionLoader` like the other data files, so it's strict: an unknown key or a missing file
fails the load (and the `art`-style `audio` scenario, §7).

## 4. Rules in Core: `Core/Audio/`

- **`AudioRules`** (static, pure, like `LightingRules`): the mix values the data doesn't own.
  - `SteamHissVolume(workingGolems)`: 0 with none, rising to full by 24.
  - `MusicLayers(golemCount)`: layer 1 always; 2 from 4 golems, 3 from 16, 4 from 48. Faded
    over 4 s, and a layer never drops out because one golem was wrecked (it needs the count to
    fall below 80% of its threshold).
  - `Pan(screenOffset)` and `Attenuation(screenOffset)`: −1…1 across the screen, quieter toward
    the edge, nothing past 25% of a screen width off it.
- **`AudioDirector`** (owned by `SandboxWorld`, so a test composes it): subscribes to the
  events, takes the impact and UI calls, applies `maxCopies`, and keeps a queue of requests
  (`slot`, `position?`) the node drains each frame. A test reads the same queue.

## 5. Mix

- **Buses:** Master → Music, Ambience, Effects, UI. Created in code at startup (like the input
  actions), not a hand-written `.tres`.
- **Copies:** `maxCopies` per slot (default 4 for work sounds, 1 for UI and the chime). A request
  over the cap is dropped, not queued, so a 100-golem factory sounds like 4 stamps, not 100.
- **Position:** positional slots play through `AudioStreamPlayer2D` at the golem's or building's
  node, so Godot pans and attenuates by the camera. `AudioRules` adds the off-screen cut.
- **Volume controls:** decision 4 (§9).

## 6. Placeholder files

Decision 1 (§9) is where they come from. Whatever the source, every placeholder:
- is a short `.wav` (effects, UI) or a looping `.ogg` (ambience, music) under `godot/audio/`, at
  the path its slot names;
- is listed in `Tools/Audio/audio_manifest.json` with its owner (the generator, or the pack and
  its licence), as `art_manifest.json` does for art;
- is checked by `Tools/Audio/verify_audio.py`: every slot has a file, every file has a slot,
  loops loop without a click, and peaks stay under −1 dBFS.

## 7. Tests and the `audio` scenario

- **Core tests:** `AudioRulesTests` (hiss, layers and their hysteresis, pan, the off-screen cut),
  and `AudioDirectorTests`: a red stall requests one chime and a grey one none; a stall retried
  doesn't chime again; 10 Pressers stamping on one tick request `maxCopies` stamps; a paused clock
  requests no work sound; a tower stage requests the fanfare.
- **The `audio` scenario** (Sandbox, headless with the dummy audio driver): every slot in
  `audio.json` loads; the buses exist; a Presser on steam requests `work.presser.stamp` on each
  impact and none while paused; the Workbench's drop, Engage and Patent, and a Claim, each
  request theirs; a red stall chimes once.
- **A listening check by the user:** the scenario can't hear. Each slot goes in front of the user
  as a short sample sheet (all the slots played one after another, as an `.ogg`) before its
  commit, as art went through review sheets.

## 8. Done when

1. Every slot in §2 (plus decision 3's `ui.place`, `ui.demolish` and `ui.collect`) is in `audio.json`, has a placeholder file and
   plays at its trigger.
2. Invariants 3–5 hold and are pinned by Core tests.
3. The user has listened to the sample sheet and approved it.
4. `verify_audio.py` exits 0; every Core test and every scenario passes from a clean build,
   the new `audio` scenario included.

## 9. Decisions (the user's)

**Made 2026-11-01:** 2, 3 and 4 as recommended (the synthesized music layers; `ui.place`,
`ui.demolish` and `ui.collect` added; sliders and mute). For 1 the user asked to hear all three
sources first: the same five slots (`work.presser.stamp`, `ui.card-snap`, `ui.stall-chime`,
`amb.steam`, `music.layer.1`) from each, in `public/audio/samples/{synth,cc0,ai}/` (local, not
committed), each with a `_sheet.wav` that plays them in turn.
- **synth:** `Tools/Audio/synth_samples.py` (standard library only).
- **cc0:** OpenGameArt files whose pages say CC0, listed in the folder's `SOURCES.txt`. Kenney's own
  site refuses scripted downloads, so Kenney's packs there weren't used (one of Kenney's sets is
  also on OpenGameArt and was).
- **ai:** Stable Audio Open 1.0 in the local ComfyUI, `Tools/Audio/comfy_audio_samples.py`, run by
  the user once the model is downloaded (gated on Hugging Face; its licence is Stability AI's
  Community Licence, free under US$1M annual revenue).


1. **Where the placeholders come from.**
   - **Recommended: synthesized by a script** (`Tools/Audio/generate_audio.py`, numpy only):
     stamps, clicks, hiss, hum, a chime and simple layered music loops, built from noise,
     envelopes and tones. No licence to track, regenerable, and the same pattern as the
     script-drawn UI art. The music will sound plain.
   - CC0 sound packs (for example Kenney's): more natural effects, but third-party files with a
     licence to record, and little that fits clockwork. Phase 4 removed the last third-party art.
   - An AI audio tool: possible, but nothing is installed for it, and each sound needs a
     listening pass.
2. **Music now, or slots only.** Recommended: the synthesized layered loops, quiet by default,
   so the layering rule (§4) is heard and tested. The alternative is the slots with silence
   until final music exists.
3. **Extra slots beyond the roadmap's list.** Recommended: three of the player's most frequent
   actions, `ui.place` (a building placed), `ui.demolish` and `ui.collect` (taking from a crate);
   the rest stay silent. Or the roadmap's list only.
4. **Volume controls.** Recommended: Master, Music and Effects sliders and a mute, kept in
   `user://settings.cfg` (not the save), on a small Settings tab in Management. Or none in this phase (mute only).
