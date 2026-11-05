# Game feel: Phase 6

Written 2026-11-01 for Phase 6 (`docs/roadmap.md`, "Phase 6: Game feel"), on branch `phase6/feel`
(from `phase5/audio`). The roadmap's list: steam puffs, sparks and build dust; a small squash on
placing and demolishing; goods that pop into a crate; camera zoom levels and a smooth pan to
alerts; a Clock Tower stage that plays as an event (camera to the tower, the bell, the reveal);
Workbench cards that land with weight and a tape ticker that prints. The user's decisions are in
§7.

## 0. Invariants

1. **Feel never changes a rule.** Every effect reads the world and draws; none delays, blocks or
   alters what the simulation does. The factory keeps running through the tower's event.
2. **Every curve is Core's** (`Core/World/FeelRules`, like `LightingRules`): pure, timed in real
   seconds since its moment, ending exactly at rest. Nodes read them; none keeps its own.
3. **The camera's decisions are Core's** (`Core/Player/CameraDirector`, on `SandboxWorld`): the
   zoom levels, what it looks at, the tower's timeline. `CameraRigNode` feeds it input and moves.
4. **Work effects follow the simulation**, as the work sounds do: a spark lands with the stamp,
   a boiler puffs on the factory's clock, so nothing sparks or puffs while paused.
5. **A load is silent and still.** `SandboxWorld.Restoring` is set while a load rebuilds the
   world: buildings appear and go without their landing or dust, and no tower event starts.
6. **The player can always take the camera back.** Walking ends an alert's look; any key skips
   the tower's event.
7. **Reduced motion stills it** (Phase 7, `menus-design.md` §5): every effect here asks
   `SandboxWorld.Motion` before it moves; the camera cuts instead of gliding. A new effect asks
   too, and the `accessibility` scenario checks it.

## 1. Effects (`Scripts/World/FxLayer`)

One layer of short-lived sprites above everything standing (z 1), each timed by a `FeelRules`
curve and freed when it ends. Kinds and where they start:

| Kind | Starts | Picture |
|---|---|---|
| Dust | a building placed (both sides of its feet; a floor tile one), demolished, and the tower's reveal | `fx_build_dust.png` |
| Spark | the Overclocker's stamp landing (`GolemNode`, with the puffs) | `fx_spark.png` |
| Puff | a burning boiler with golems on it, every 1.4 s of factory time, at its chimney | `fx_steam_puff.png` |
| GoodPop | a deposit landing in a crate: the good's icon out of the lid (at most every 0.25 s a crate) | the good's icon |
| Glint | with the pop | `fx_pop_glint.png` |

The S7 set (§5) draws Dust, Spark and Glint. A picture missing falls back to the steam puff
tinted to its colour, so the effect still plays.

## 2. The camera (`CameraDirector`, `CameraRigNode`)

- **Zoom levels** (decision 3): Close 3, Near 5.625 (one art pixel to one screen pixel at 720p),
  Normal 10 (the start), Wide 15. The wheel steps one level a notch; the camera glides there
  (8/s). A size a scenario sets directly stays until the wheel moves.
- **The pan to alerts** (decision 2): a click on the alert strip glides the camera (3.5/s,
  slower than following, so it reads as a move) to the worst stopped golem, in the strip's own
  order (`StallTracker.StallsWorstFirst`); each click goes to the next, wrapping round. It holds
  there until the player walks, or the golem is gone.

## 3. The Clock Tower's stage event (decision 4)

`ClockTowerStageCompletedEvent` starts it. About 4.4 s, the factory running throughout:
1. **Glide in** (1.2 s): the camera to the tower; the tower still wears the stage before
   (`ClockTowerArt.SpriteFor(site, holdBack)`).
2. **Reveal** (2.4 s): 0.6 s in, the new stage appears with a swell, a warm flash and dust
   (`FeelRules.TowerReveal`), and the banner shows "Stage 2 complete · The Movement". The fanfare
   (Phase 5) plays from the event.
3. **Glide back** (0.8 s): the player has control again.

Any key, or walking, skips straight to the end with the stage shown. The alert strip's click is
ignored while it plays.

## 4. The curves (`FeelRules`)

| Moment | Length | Shape |
|---|---|---|
| Place | 0.32 s | dropped in tall, squashed on landing, a damped spring back; floor tiles get dust only |
| Demolish | 0.24 s | squashed flat and faded, then freed |
| Dust | 0.6 s | swells, drifts out and up, fades |
| Spark | 0.28 s | a hot flash that rises a little and dims fast |
| Crate pop | 0.36 s | the crate bumps; the good arcs up out of the lid and fades |
| Card landing | 0.22 s | the last 6 px of the drop, a squash, settled |
| Tape | 60 chars/s | a new draft's line prints a character at a time, then follows the live numbers |
| Tower reveal | 0.5 s | a 5% swell and a warm flash |

## 5. The FX art: set S7 (decision 1, ComfyUI)

`public/images/S7/`: `scripts/fx_draft.py` draws each picture's shape in the palette's ramps;
`env_set.py` redraws them in ComfyUI. Three files, `kind: sprite`, each naming its `draft` and
rendering at `scale` 32 (a 24 px sprite at 8x is too small for the model):
`fx_spark` (24×24, fire), `fx_build_dust` (32×24, steam and bone), `fx_pop_glint` (16×16, brass).

```sh
python public/images/S7/scripts/fx_draft.py             # the drafts (done)
python Tools/Art/env_set.py public/images/S7 source     # the inits (done)
python Tools/Art/env_set.py public/images/S7 run        # THE USER RUNS THIS, in their own terminal
python Tools/Art/env_set.py public/images/S7 pick       # then the sheets, the user's picks
python Tools/Art/env_set.py public/images/S7 install    # once set.json is "approved"
```

Installing adds the three to `Tools/Art/art_manifest.json` (owner `comfyui`, kind `fx`) and to
the `art` scenario's named sprites. Nothing enters `godot/art/` before the user approves.

## 6. Tests

- **Core:** `CameraDirectorTests` (the levels and their ends, the glide, the alert order and
  wrap, walking back, the tower's phases and single reveal, the skip, the banner) and
  `FeelRulesTests` (each curve moves and ends at rest; the tape; the tower holding its stage back).
- **The `feel` scenario** (Sandbox): the wheel steps to Near and back; a depot lands with dust,
  pops a good when filled and comes down in dust; a dropped card lands and the tape prints; a
  click on the alert strip glides to a stopped golem and walking brings the camera back; a stage
  glides to the tower, shows the banner and comes back, the clock running. Windowed with
  `--capture-dir`, it saves `feel-place`, `feel-pop`, `feel-alert` and `feel-tower`.
- **`golem-motion`** also checks the Overclocker's sparks and the boiler's puffs.

## 7. Decisions (the user's, 2026-11-01)

1. **FX art:** ComfyUI renders (over script-drawn pixels, or shapes only).
2. **The pan to alerts:** a click on the alert strip, as recommended.
3. **Zoom:** fixed levels with a smooth glide, as recommended.
4. **The tower's event:** the clock keeps running and any key skips, as recommended.

## 8. Done when

1. Every item in the roadmap's list plays (§1–§4), and every Core test and scenario passes.
2. The S7 pictures are rendered, picked and approved by the user, and installed. **Done
   2026-11-01:** the user picked spark #6, dust #7, glint #6.
3. The user has played it and is happy with the feel.
