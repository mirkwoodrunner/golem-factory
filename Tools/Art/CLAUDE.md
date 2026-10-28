Folder rules for the art, its generators and AI image generation. The root `CLAUDE.md` has the project overview, commands and settled rules.
Paths in the root's engine sections are relative to `godot/`; paths here are from the repo
root unless they say otherwise. The project font rule stays in the root.

# Art

- **The art lives in `godot/art/`**, including `UI/…`. The generators in `Tools/Art/` write
  here. Their root comes from `Tools/Art/art_paths.py`, and `--out-root <dir>` redirects any
  of them.
- **`python Tools/Art/verify_art.py`** regenerates everything into a scratch folder and
  compares it with `godot/art/`. Exit 1 means a generated sprite drifted. It covers every
  generator, the goods included (`generate_placeholder_art.py` honours `--out-root`; a bare
  run writes only its `belts` and `goods` sections). It also lists the sprites that are
  authored rather than generated (walk frames, chassis art, the Steampunk pack), so "not
  generated" is never mistaken for "verified".
- **A generator that owns a replaced file skips it under `godot/art/`** through its
  `HAND_REPLACED` set and `art_paths.skip_hand_replaced`, so `--out-root` still renders the
  original for `verify_art.py` to report as hand-replaced. A name in `HAND_REPLACED` is also in
  `verify_art.py`'s `KNOWN_HAND_REPLACED` (art-pipeline-design.md §2, step 13).
- Texture filtering is nearest-neighbour project-wide. Standing sprites are placed with
  `GridConversions.StandOnCell`, Unity's BottomCenter pivot. The five pipe tiles are
  centre-pivoted (they rotate in quarter turns). 9-slice UI uses `StyleBoxTexture` margins.
  Those are set where each sprite is drawn, in G5 and G7, not by an import pass.

# The Phase 4 tooling (`docs/art-pipeline-design.md` §5)

```sh
python Tools/Art/verify_art.py                 # generated art reproduces
python Tools/Art/check_art.py                  # the manifest: sizes, feet, palette, frames, light, readability
python Tools/Art/check_art.py --write          # refresh the manifest's size and footRow from the files
python -m unittest discover Tools/Art/tests    # the tools' own tests (no ComfyUI, no godot/art/ writes)
python Tools/Art/golem_set.py <set> run        # a golem chassis' 28 frames: draft → … → review grid
python Tools/Art/golem_set.py <set> install    # only after the user approves ("approved" in chassis.json)
```

- **A golem chassis is made with `golem_set.py`** (art-pipeline-design.md §5.10): its own
  settings in `public/images/S1/<chassis>/chassis.json`, its draft script beside it. It starts
  and stops ComfyUI and Godot itself. **The user runs `run` (the render) in their own
  terminal**: run from Claude Code, Claude Code's low-memory reaper kills it. A helper agent on a smaller model runs the rest from
  **`Tools/Art/golem-helper.md`**: point the helper at that file and the set, and keep the
  draft, `picks`, `approved` and the user's review for the lead.
- **Among candidates that all pass, the pick is taste:** look at `candidates_<facing>.png`
  and record a choice by eye in `chassis.json`'s `picks`.

- **Every PNG under `godot/art/` has an entry in `art_manifest.json`**, and every entry a file
  (check 1). A new or renamed sprite adds its entry in the same change; only `size` and
  `footRow` are mechanical (`--write`), the rest is kept by hand.
- **`palette` is `legacy` until a set is replaced**, then `bible` (or a list of ramps). Replacing
  a set flips its entries, which turns its WARNs into FAILs: a replaced set must pass checks
  4–11, 13 and 14 outright.
- **The palette is `palette.json`** (art-style-bible.md §1). Change a colour there and in the
  bible together.
- **The shared measures are `art_metrics.py`** (OKLab, masks at the feet, IoU, the edge-light
  score, the 32 → 24 phases, the palette). A tool or check that measures imports it rather than
  measuring its own way.
- **Only the commit step writes into `godot/art/`**: `palette_snap.py` and `reshade_mirror.py`
  with `--into-art`. `pose_guide.py`, `review_sheet.py` and `comfy_redraw.py` refuse it;
  sources, guides, raw candidates and review sheets go in `public/images/<set>/`.
- **`comfy_redraw.py` only talks to ComfyUI's HTTP API** (`127.0.0.1:8188`) and takes every
  ControlNet and IP-Adapter model by name; nothing here writes into `G:/AI/ComfyUI`. Whoever
  starts ComfyUI stops it when the set is done.
- **`check_art.py` reads the mood tints from `GolemMoodPalette.cs`** by regex (check 10), so a
  tint change in Core re-checks the golems; keep the `*Tint = new Color(...)` field form.

# AI image generation (optional, personal MCP)

If an image-generation MCP server is configured in the user's global Claude config, use it for
**throwaway placeholders only** (UI mockups, icon ideas, concept sketches), never for shipped art.

- Save results to `./public/images/` with a descriptive kebab-case name (`brass-gear-icon.png`).
  Create the folder if it is missing. Do not write into `godot/art/`:
  `Tools/Art/` generators own those, and `verify_art.py` expects them to reproduce exactly.
- Prefer a tool that returns a URL, then download it with `curl -o`. Base64 tool results cannot be
  saved to disk without extra steps.
- Match the game's look in the prompt: cozy top-down pixel art, brass and mahogany palette. Ask for
  a square size (64, 128 or 256 px) so a mockup lines up with the PPU 64 grid.
- Say which file you wrote and the prompt used. Do not wire a generated image into a scene unless
  asked.
- **One exception, at the user's call: the Clock Tower.** Its six sprites in `godot/art/`
  (`clock_tower_site`, `clock_tower_stage0`, `clock_tower_stage1`, `clock_tower_stage2`,
  `clock_tower_stage3`, `clock_tower_stage4`) are a 192×640 ComfyUI redraw, composited by
  hand, and they replaced `generate_clock_tower_art.py`'s 192×448 pictures. That generator now
  refuses to write into `godot/art/`, `verify_art.py` lists the six as hand-replaced, and
  `ClockTowerArt.CanvasHeight` is 640. Any change to the tower's art has to keep all three
  in step.
- **And a second: the five market carts** (`stall_scrap`, `stall_coal`, `stall_copper_ore`,
  `stall_zinc_ore`, `stall_aether`). They were redrawn in ComfyUI from hand-drawn drafts, then softened
  to match the props: the near-black ink became warm brown outlines, and the wood was shifted
  toward the props' red-brown. They keep the generator's 96×120 canvas and its pivot. Their
  wheels stand on row 117, so `SpritePivots` is unchanged. `generate_topdown_environment.py`
  skips those five names when writing to `godot/art/` (`HAND_REPLACED`), and `verify_art.py`
  lists them as hand-replaced. **Copper ore is green**, as malachite, on the cart and on the
  `item_copper_ore` good alike. The good is still generated (`generate_placeholder_art.py`),
  so a change to one has to be matched in the other.
- **And the golems, set by set (S1).** The Brass Presser's 28 frames (`chassis_brass_presser.png`
  and its 27 `chassis_brass_presser_<state>_<facing>.png`) are a creature redraw (style bible
  invariant 17), approved at its third review round on 2026-10-23. No generator ever owned them,
  so they are owner `comfyui` in the manifest, not hand-replaced, and `verify_art.py` lists them as
  not generated. They are made by `golem_set.py` from `public/images/S1/brass_presser/`
  (`chassis.json`, `scripts/presser_draft.py`): the draft script draws each frame as a
  part-mapped draft, ComfyUI redraws only each facing's base, and the compose step derives every
  frame from its facing's cleaned base (texture as one-tone steps, parts layered per facing by
  `golem_set.LAYERS`); west is the east profile mirrored and re-lit. A change to one frame is
  made there and the 28 composed again, never by hand in `godot/art/`; `golem_set.py
  public/images/S1/brass_presser verify` proves the installed 28 still reproduce.
  The Clockwork Scavenger's 28 (`public/images/S1/clockwork_scavenger/`,
  `scripts/scavenger_draft.py`, approved 2026-10-24) are made the same way. Its `chassis.json`
  sets its own `layers` (the back-worn bucket over the head from the north) and `rigid` parts
  (a tripod: `legs` and `far_leg`); its north view is the south mirrored, so the claw arm stays
  on its own side.
  The Mainspring Overclocker's 28 (`public/images/S1/mainspring_overclocker/`,
  `scripts/overclocker_draft.py`, approved 2026-10-24) likewise: the Watchmaker, three tool
  arms drawn fresh per frame with the flyball `governor`, and the caged mainspring (`cage`) over
  everything from the north. Its first draft overlapped today's Hauler at 0.55 and the draft
  step refused it; a slimmer body and a hunched head got it to 0.44.
  The Aether Hauler's 28 (`public/images/S1/aether_hauler/`, `scripts/hauler_draft.py`,
  approved 2026-10-25) likewise: `treads` for legs, the `crate` rigid (it heaves up and back),
  the `crystals` drawn fresh per frame so they jump. From the north the crate is over the arms
  and hands, which hold its near corners.
  The Zeppelin Freight Loader's 28 (`public/images/S1/zeppelin_freight_loader/`,
  `scripts/zeppelin_draft.py`, approved 2026-10-25) likewise: the tethered `balloon` rigid (it
  sags in the stall), the `ropes` and the `cargo` bundle drawn fresh per frame. From the north
  the arms are under the body (the shoulders cover their roots), and fists that hold the bundle
  in front are clipped by the body and legs in the draft; raised or hanging ones show.
- **The Artificer's 16 walk frames in `godot/art/` are today's originals**, not a redraw. A
  `golem_set.py` redraw (`public/images/S1b/artificer/`, `"kind": "artificer"`, today's head
  traced in) was installed and then rejected by the user on 2026-10-26 as worse overall
  (`art-pipeline-design.md` §9, decision 15). Don't install that set. Changes to the Artificer
  edit today's own pixels (`source/today/`, gitignored: restore with
  `git show 906d389:godot/art/<name>`). The shipped frames have had their stray shirt-green
  specks cleaned (`scripts/clean_today.py`, approved 2026-10-27; 221 pixels); rerun it from
  `source/today/` into `godot/art/` to reproduce them. The side views then share one head
  (`scripts/side_heads.py <in> <out> right=2 left=mirror:2`, approved 2026-10-27): walking
  right's frame 2 head in all four right frames, and mirrored into all four left frames, so
  the face no longer changes frame to frame. Before that, `scripts/side_eye.py` redraws that
  head's eye to match the front view's (a 3-pixel iris with the white behind it; approved
  2026-10-27). Last, `scripts/outline.py` paints the silhouette's outer two pixels a near-black
  plum (1c1422) so the figure stands off the wooden floor at in-game size (approved
  2026-10-27). The order is clean_today, side_eye on right frame 2, side_heads, outline. With a
  one-colour outline the edge-light score is 0, so all 16 frames warn on check 14; expected.
