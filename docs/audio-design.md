# Audio: Phase 5

Written 2026-11-01 for Phase 5 (`docs/roadmap.md`, "Phase 5: Audio"), on branch `phase5/audio`
(from `phase4/art`). The game has no sound today: no audio file, no player node, no bus. This
spec says what sounds, when, how loud, where the placeholder files come from, and how a final
asset replaces one. The user's decisions are in §9; all four are made, and §2–§7 are built.

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
   retry, and not again until the golem has run again. It isn't positional: a red golem off
   screen needs the player most.
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
| `work.<motion>` | Effects | `GolemWorkMotion.SoundBeats` goes up | one per work motion, which follows the verb: `work.stamp` (Assemble), `work.fast-stamp` (the Overclocker's Assemble and Repeat), `work.tug` (Extract), `work.heave` (Haul), `work.lunge` (Push), `work.launch` (FreightLaunch); positional, at most 4 at once |
| `ui.card-snap` | UI | a card dropped into a Workbench socket | |
| `ui.engage` | UI | the Engage lever thrown | |
| `ui.patent` | UI | Patent pressed | a stamp |
| `ui.claim` | UI | a card claimed on the Assembly Line | |
| `ui.stall-chime` | UI | a golem turns red (invariant 4) | not positional (invariant 4) |
| `ui.place` | UI | a building placed (`BuildModeController.BuildingPlaced`) | decision 3 |
| `ui.demolish` | UI | a building removed (`BuildingRemoved`) | decision 3 |
| `ui.collect` | UI | goods taken from a crate: E, or the Crate screen's Take | decision 3 |
| `music.layer.1`…`music.layer.4` | Music | looped together, layers faded in by factory size (§4) | one tempo and key, so they stack |
| `music.tower-fanfare` | Music | `ClockTowerStageCompletedEvent` | ducks the layers while it plays |

A work sound lands once per stroke at the motion's `SoundPhase` (the stamp bottoming out, the
tug's yank, the heave's lift, the lunge's thrust, the hop), counted from step progress, so it is
heard once however many frames a stroke takes, faster at 4×, and never while paused. A load
doesn't sound: `SandboxWorld.LoadFrom` suppresses the director while it rebuilds.

## 3. Data: `godot/data/audio.json`

One entry per slot, keyed by its name (`Core/Audio/AudioSlotDefinition`): `path`
(`res://audio/<name>.wav`), `bus` (an integer, as the data's enums are: 0 Music, 1 Ambience,
2 Effects, 3 UI), `volumeDb`, `maxCopies`, `positional` (the work sounds) and `loop` (the
ambience and music loops). Loaded by `DefinitionLoader` like the other data files, so an unknown
key fails the load; a missing file fails the `audio` scenario (§7).

## 4. Rules in Core: `Core/Audio/`

- **`AudioRules`** (static, pure, like `LightingRules`): the mix values the data doesn't own,
  and the slot names code asks for.
  - `SteamHissVolume(workingGolems)`: 0 with none, rising (square root) to full by 24.
  - `MusicLayers(golemCount, current)`: layer 1 always; 2 from 4 golems, 3 from 16, 4 from 48.
    Faded over 4 s, and a layer never drops out because one golem was wrecked (it needs the
    count to fall below 80% of its threshold).
  - `Audible(position, viewCentre, viewHalfSize)`: the off-screen cut, nothing past 25% of a
    screen width off it. Panning and quieting toward the edge are Godot's
    (`AudioStreamPlayer2D`, the camera as listener).
  - `SliderToDb`: a volume slider's 0–1 as a bus level.
- **`AudioDirector`** (owned by `SandboxWorld`, so a test composes it): `Update` once a frame
  after the clock reads the clock's second, each golem's `SoundBeats` and red stall, and the
  loops' levels; `Play(slot, position?)` applies the off-screen cut and `maxCopies` (copies
  counted by each file's length, which the node reports) and queues a request the node drains
  each frame. The player's moments reach it through events wired in `SandboxWorld.WireAudio`
  (`WorkbenchSession.CardSeated`, `LeverPulled`, `Patented`; `AssemblyLineBoard.Claimed`;
  `BuildModeController.BuildingPlaced`/`Removed`; `PlayerInteractor.Collected`,
  `CrateScreenReadout.Collected`) and the tower's off the event bus. Scenarios count
  `Requested(slot)`.
- **`Scripts/Audio/AudioNode`** (added by `WorldNode`): makes the buses, loads every slot, runs
  the loops at the director's levels, tells it the camera's view, and plays its queue.

## 5. Mix

- **Buses:** Master → Music and Effects; Effects → Ambience and UI, so the Effects slider covers
  every sound but the music. Created in code at startup (like the input actions), not a
  hand-written `.tres`.
- **Copies:** `maxCopies` per slot (default 4 for work sounds, 1 for UI and the chime). A request
  over the cap is dropped, not queued, so a 100-golem factory sounds like 4 stamps, not 100.
- **Position:** positional slots play through an `AudioStreamPlayer2D` where the golem stands,
  so Godot pans and attenuates by the camera. `AudioRules.Audible` adds the off-screen cut.
- **Volume controls:** Master, Music and Effects sliders and a mute on Management's Settings tab
  (`Scripts/UI/SettingsTab`), kept in `user://settings.cfg`, not the save. Music starts at 0.7.

## 6. Placeholder files

Decision 1 (§9): CC0 recordings for every effect and the ambience, synthesized music. Every
placeholder:
- is a mono 16-bit 44.1 kHz `.wav` under `godot/audio/`, at the path its slot names (WAV for
  the loops too, so the checks need only Python's standard library; Godot compresses on import);
- is listed in `Tools/Audio/audio_manifest.json` with its owner (the generator, or the pack, its
  author, licence and page), as `art_manifest.json` does for art;
- is made by `Tools/Audio/build_cc0.py` (from the downloaded packs; needs PyAV and numpy, which
  ComfyUI's embedded Python has) or `Tools/Audio/synth_music.py` (standard library);
- is checked by `Tools/Audio/verify_audio.py`: every slot has a file, every file has a slot and a
  manifest entry, loops loop without a click, and peaks stay at or under −1 dBFS.

`Tools/Audio/review_sheet.py` builds the listening sheet (§7): every slot in turn, the loops long
enough to hear the seam, the music one layer at a time.

## 7. Tests and the `audio` scenario

- **Core tests:** `AudioRulesTests` (hiss, layers and their hysteresis, the off-screen cut,
  sliders) and `AudioDirectorTests`: every slot code asks for is in the data and nothing else; a
  red stall requests one chime however often it retries, again once cleared and hit again, and a
  blue one none; 10 Pressers stamping at once request `maxCopies` stamps; a Presser stamps once per
  stroke; a paused clock requests no work sound; the clock ticks once a game second and not while
  paused; a tower stage requests the fanfare.
- **The `audio` scenario** (Sandbox, headless, muted): every slot in `audio.json` loads and the
  buses exist; the `golem-motion` row, as its fixture, requests every work sound and none while
  paused; the Workbench's card snap, Engage and Patent, a Claim, placing, demolishing and
  collecting each request theirs once.
- **A listening check by the user:** the scenario can't hear. Each slot goes in front of the user
  as a short sample sheet (all the slots played one after another, as an `.ogg`) before its
  commit, as art went through review sheets.

## 8. Done when

1. Every slot in §2 (plus decision 3's `ui.place`, `ui.demolish` and `ui.collect`) is in `audio.json`, has a placeholder file and
   plays at its trigger.
2. Invariants 3–5 hold and are pinned by Core tests.
3. The user has listened to the sample sheet and approved it. **Done 2026-11-01.**
4. `verify_audio.py` exits 0; every Core test and every scenario passes from a clean build,
   the new `audio` scenario included.

## 9. Decisions (the user's)

**All made 2026-11-01.** 2, 3 and 4 as recommended (the synthesized music layers; `ui.place`,
`ui.demolish` and `ui.collect` added; sliders and mute). For 1 the user heard the same five
sounds synthesized (`Tools/Audio/synth_samples.py`) and from CC0 packs, and chose **CC0, except
the music**: the CC0 tavern tune didn't fit the game, so the music is synthesized, as decision 2
already said. The AI route (Stable Audio Open in ComfyUI) wasn't needed; its script is in git
history at 764e6ce. Kenney's own site refuses scripted downloads, so Kenney's packs came from
OpenGameArt.

The options as they were put:

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
