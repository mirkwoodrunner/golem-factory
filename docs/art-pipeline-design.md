# Art pipeline: Phase 4

Written 2026-10-22 for Phase 4 (`docs/roadmap.md`, "Phase 4: Art"), on branch `phase4/art`;
revised 2026-10-23 to the user's twelve decisions (§9, all decided) and a thirteenth at the first review sheet (golems are creatures). It says how each set of art
is replaced, in what order, which files each set holds and what reads them, the tooling that
makes cleanup, mirroring, checks and review repeatable, the lighting, the scenarios that pin it
all, and when the phase is done. **What the art looks like** is `docs/art-style-bible.md`; its
§0 invariants hold for every step here.

**Next:** S1, the golems (§2, §4): the Brass Presser's 28 frames are in (8f09b0e, approved
2026-10-23 at its third review round, `public/images/S1/brass_presser/review.json`), and the
Clockwork Scavenger's (approved 2026-10-24 at its second round, made end to end by `golem_set.py`),
the Mainspring Overclocker's (approved 2026-10-24, redrawn as the Watchmaker at the user's call),
the Aether Hauler's (approved 2026-10-25, on treads with a crate of Aether crystals) and the
Zeppelin Freight Loader's (approved 2026-10-25, an iron behemoth under a tethered blimp): all
five S1 chassis are in. Next: the Artificer (S1b), then S2 onwards. Done on 2026-10-23: the
cleanup commit (§3.1), the `verify_art.py` fix (§5.5), the tooling (§5), Core's `GolemFrames`
(§7.4) and the `art` scenario's first part (§7.1); ControlNet and IP-Adapter are installed (§1.4).

## 1. Where things stand (measured 2026-10-22, at 8b3ec0b)

### 1.1 The art

155 PNGs under `godot/art/`. By owner:

| Owner | Files | Checked by `verify_art.py` |
|---|---|---|
| Generated, `generate_topdown_environment.py` | 31 (floor, street, walls, props, belt tiles, overlays) | Yes, byte-identical |
| Generated, `generate_building_art.py` | 11 (depot, station, Post, mast, recycler, slag heap, five pipe pieces) | Yes |
| Generated, `generate_workbench_ui_art.py` | 10 (`UI/Workbench/wb_*`) | Yes |
| Generated, `generate_tech_tree_art.py` | 13 (`UI/TechTree/tt_*`) | Yes |
| Generated, `generate_placeholder_art.py` | 27 (24 goods, `belt_lane`, `belt_arrow`, `belt_roller`) | **No: see §1.3** |
| Hand-replaced, ComfyUI | 11 (six Clock Tower, five market carts) | Listed as hand-replaced |
| ComfyUI, never generated | 1 (`fx_steam_puff.png`) | Listed as "not generated" |
| AI-authored before decision 3 (RetroDiffusion) | 22 (five chassis, sixteen Artificer walk frames, `player.png`) | Listed as "not generated" |
| Authored, origin not recorded | 8 (`steam_boiler`, `hand_crank_bench`, `golem_generic_*` ×3, `building_block`, `ghost_placeholder`, `facing_arrow`) | Listed as "not generated" |
| Third-party, Steampunk & Fantasy UI Pack (Scott Jenkins, free use; `docs/source-assets/SteampunkFantasyUIPack/`, its `ReadMe.txt` the licence note) | 21 (`UI/Steampunk/*`) | Listed as "not generated" |

`verify_art.py` today: 76 generated, 65 byte-identical, 11 hand-replaced, 0 drifted, 79
"committed but not generated".

**Unused** (no script, scene, data file or Core file names them; rechecked by string search on
2026-10-23), 26 files:

- 12 of ours: `player.png`, `golem_generic_brass`, `golem_generic_steel`, `building_block` (named
  only in a comment in `BuildGhostVisuals`), `belt_lane`, `belt_arrow`, `belt_roller`,
  `floor_edge_se`, `wall_segment_ne`, `wall_segment_ne_lamp`, `tt_line_h`, `tt_line_v`.
- 14 of the 21 Steampunk pieces. **Seven are in use**: `steampunk_panel_iron_bolt`,
  `iron_panel_nobolt`, `steampunk_button_blank`, `steampunk_button_blank_iron`,
  `steampunk_button_accept`, `steampunk_button_exit` and `gear_large`. (The first survey counted
  `gauge` as used; nothing loads it. `HudOverlay._gauge` is a text label.)

They are deleted before S1 (decision 11, §3.1).

### 1.2 What the frames show

Rendered with `--demo`, and with the `golem-motion`, `hover`, `workbench` and `moods` scenarios'
captures (`--write-movie` and `--capture-dir`):

- **The carts and the tower are the quality bar**, and they are the only art that took the chosen
  route. Warm brown outlines, muted red-brown wood, soft contact shadows.
- **The golems are the weakest thing on screen, and the most watched.** They are the darkest
  objects in the room (mean OKLab lightness 0.28–0.42 against the floor's ~0.30), outlined in
  purple-black, so a dark golem on dark planks reads as a smudge at the default zoom. The mood
  tint barely moves them: the Stalled tint shifts the Presser's mean colour 0.056 in OKLab, the
  Overclocker's 0.125 (`art-style-bible.md` §5.1). The red-bodied Hauler and copper Scavenger
  under a red tint are the worst case; the badge carries the mood today.
- **Three chassis share a silhouette.** Presser, Hauler and Zeppelin are boxy humanoids; their
  alpha masks overlap 0.58–0.60. The roster (`digital-design.md`) gives each a different body.
- **One picture for four facings.** A golem facing west looks exactly like one facing north;
  only the arrow differs (decision 3 changes that).
- **The boiler and the bench** are drawn in the chassis' harsher palette (purple outlines,
  saturated copper) and stand out from the carts beside them.
- **Goods:** Brass and Copper Ingot have the same silhouette (overlap 1.00); only their colour
  differs.
- **The Workbench** is placeholder throughout (`open-items.md` §4): the lever is an iron panel
  with a track and a handle, the cards are one 32 × 32 9-slice tinted per kind.
- **The art is shrunk at the default zoom**: 0.56 screen pixels per art pixel at the 1280 × 720
  window (`art-style-bible.md` §2.5), so a one-pixel line can drop out.

### 1.3 A blind spot in `verify_art.py`

`generate_placeholder_art.py`'s `main` treats every argument that doesn't start with `--` as a
section name. `verify_art.py` calls it with `--out-root <dir>`, so `<dir>` is taken as a
section, no section matches, and **the script writes nothing**. Its 27 files are reported as
"not generated" and never compared. Run by hand with `belts --out-root <dir>` and
`goods --out-root <dir>`, all 27 reproduce byte for byte (2026-10-22). Its other sections
(`environment`, `interaction`, `routing`) raise on purpose, because the top-down generator owns
those names (`TOP_DOWN_OWNED`), so a bare run of the script fails too.

This matters to Phase 4 because the goods are a replacement set and copper ore's colour is an
invariant (`art-style-bible.md` §0.6). The fix is a tooling task (§5.5).

**Fixed (2026-10-23, §5.5).** `verify_art.py` now regenerates and compares all 24 goods: 95
generated (84 byte-identical, 11 hand-replaced), 0 drifted, 34 not generated.

### 1.4 ComfyUI on this machine

`G:/AI/ComfyUI/ComfyUI_windows_portable` (the portable build), started by
`G:/AI/start_comfyui.bat`, tuned for an **RTX 2060 with 6 GB of VRAM** with fp8 weights: the
first image after a start takes about 7 minutes while SDXL loads from the HDD, then about 1.4 s
a step.

- Models: `sd_xl_base_1.0`, `sdxl-vae-fp16-fix`, LoRAs `lcm-lora-sdxl` and `pixel-art-xl`.
- **ControlNet and IP-Adapter are installed (decision 12, 2026-10-23)**, separately from this
  repo's work, in `G:/AI/ComfyUI`, and a smoke test rendered through both. What it proved, and
  what `comfy_redraw.py` (§5.4) uses:
  - ControlNet: the core nodes `ControlNetLoader` and `ControlNetApplyAdvanced` (this build's
    `ControlNetApplyAdvanced` takes the `vae` as an input); models
    `control-lora-{canny,depth,sketch}-rank128.safetensors`. The canny preprocessor is
    `CannyEdgePreprocessor` (comfyui_controlnet_aux); `pose_guide.py`'s lineart is already
    white edges on black, so it is fed without one by default.
  - IP-Adapter (ComfyUI_IPAdapter_plus): `IPAdapterModelLoader` (`ipadapter_file`),
    `CLIPVisionLoader`, `IPAdapterAdvanced` (weight 0.7, weight type `style transfer`, embeds
    `V only`); models `ip-adapter-plus_sdxl_vit-h.safetensors` or `ip-adapter_sdxl_vit-h`, with
    `CLIP-ViT-H-14-laion2B-s32B-b79K.safetensors` as the image encoder.
  - Measured with both on: **about 11 s an image warm, about 6 minutes for the first after a
    start**, within the 6 GB.
- **Nothing in this repo writes into `G:/AI/ComfyUI`.** The tooling talks to the server over its
  HTTP API only, and takes model names as arguments (§5.4), so an install change needs no code
  change.
- `G:/AI/comfy_generate.py` drives it from a shell: text to image only (SDXL + LCM 8 steps +
  Pixel Art XL at 1.2, 1024 px, reduced 8× with nearest-neighbour to a 128 px grid). It is
  outside the repo.
- Image to image and inpainting need only core nodes (`LoadImage`, `VAEEncode`,
  `VAEEncodeForInpaint`, a KSampler below denoise 1). The tower's build stages used inpainting.

**The 6 GB limit shapes every workflow.** SDXL in fp8 already uses most of it. With ControlNet
and IP-Adapter added: one ControlNet at a time, its small or control-LoRA variant in fp16, the
IP-Adapter's image encoder loaded once per batch of frames, batch size 1, and ComfyUI's low-VRAM
offloading accepted as the price (slower steps, not failure). A workflow that runs out of memory
drops IP-Adapter weight first, never the ControlNet guide, because the guide is what keeps the
pose. The first S1 run measures the step time and records it here.

Whoever starts ComfyUI stops it when the set that needed it is done.

### 1.5 The lighting today

`ShellNode` already lights the room: a `CanvasModulate` at a neutral 0.95 grey, and seven
`PointLight2D` sconces on the north wall (one every fourth segment, pinned by the `world`
scenario), colour (1, 0.72, 0.42), energy 1.1, a 5.5-cell radius, a smooth radial gradient, no
shadows. The renderer is GL Compatibility. Nothing else emits light: the boilers, the hearth and
the steam are unlit. The playtest already asks "Look around the workshop. Is the room too dark?"
(`PlaytestSession`, question `light`).

## 2. The per-set workflow

Every set goes through these steps, in order. **Cleanup is tool-only** (decision 4): no step
assumes anyone edits pixels by hand. The user sees every set before it is committed, and only
the frames the user marks are worked on again.

**Working files stay out of git.** `public/images/<set>/` is the set's working folder; `.gitignore`
drops every PNG under `public/images/`, so captures, sources, raw results and review sheets stay
local (5.4 MB for the cleanup's captures alone, and S1 makes hundreds). The JSON inputs
(`poses.json`, `fixes.json`, `review.json`) are committed: they are what makes a set's result
reproducible from its sources. What ships is only what lands in `godot/art/`.

1. **Brief.** Fill the set's row in §4: what each file must show (from `digital-design.md`'s
   roster or the building's role), its palette ramps (`art-style-bible.md` §1), its canvas,
   pivot, frames and facings, and what code reads it.
2. **Before captures.** The `art` scenario's gallery (§7.1) on the committed art, at the default,
   closest and widest zooms, into `public/images/<set>/before/`. These are the review sheet's
   "before" column.
3. **Sources and guides**, in `public/images/<set>/source/`:
   - **A base frame** (the south base, or a building, tile or good): the current sprite enlarged
     8× with nearest-neighbour, for image to image. A draft the user offers is used the same
     way; none is asked for.
   - **The other facings' bases** (golems and the Artificer): north from a guide `pose_guide.py`
     (§5.6) makes from the cleaned south base with the front details (eyes, face plate) masked
     out; east with no ControlNet guide, from the text prompt ("side view, facing right") and
     the IP-Adapter holding the character (§5.4), swept over seeds.
   - **Every other frame** (idle, work poses, stall, carry, walk steps): a pose guide from
     `pose_guide.py`, made from the **cleaned base of the same facing** with the moving parts
     transformed as `public/images/<set>/poses.json` says (the ram moved down for impact, the
     head tipped for stall). Frames are never generated from nothing, so the body doesn't drift
     between them (the Artificer's frames share one palette and one foot row for the same
     reason, `slice_artificer_walk.py`).
4. **ComfyUI redraw** (`Tools/Art/comfy_redraw.py`, §5.4), at 8× the target canvas.
   - A base frame: image to image from its source, a denoise sweep (0.35, 0.5, 0.65) over three
     or four seeds.
   - Every other frame, and every non-south base: **ControlNet driven by the frame's guide**
     holds the pose and silhouette; **IP-Adapter with the cleaned south base as its reference**
     holds the character (materials, colours, details); image to image from the guide's colour
     composite at denoise 0.5–0.65.
   - Raw results go in `public/images/<set>/raw/`, never `godot/art/`.
5. **Pick.** The candidates are scored by `check_art.py`'s metrics (silhouette, lightness, tint
   shift, palette distance) and the best-scoring candidate that looks right is picked. A
   comparison sheet (`public/images/<set>/comparison.png`: the current sprite beside each
   candidate, at 1× and 4×, on a floor tile) records the pick and the runners-up, so the user
   can swap a pick at review.
6. **Reduce.** 8× to 1× by k-centroid (`palette_snap.py --reduce 8`), never bilinear.
7. **Scripted cleanup** (`palette_snap.py`, §5.3), which replaces the hand cleanup the carts
   once had:
   - every opaque pixel to the nearest colour of the set's ramps in OKLab;
   - alpha to 0 or 255 outside the allowed shadow rows;
   - any black or purple-black outline pixel to the outline ramp (the carts' softening, as a
     tool);
   - the set's scripted fixes, `public/images/<set>/fixes.json` (rectangles of a ramp tone,
     per file), for anything the snap can't decide, such as a 2 × 2 eye or a symmetric rivet
     row.

   It reports how many pixels moved; more than 10% in the snap means the redraw was off-palette,
   and the frame goes back to step 4.
8. **Canvas and pivot.** Placed on the manifest's canvas with the contact line on the pivot row;
   a frame set shares one foot row (`trim_character_alpha.py`'s shared-minimum rule).
   `check_art.py <file>` (§5.2) must pass.
9. **Mirror** (golems and the Artificer). The west frames from the east ones, the Artificer's
   left from its right, by `reshade_mirror.py` (§5.7): flip, re-shade so the light is back on
   the upper left, then `check_art.py` (check 14, light direction).
10. **Review sheet**, shown to the user **before anything is committed** (invariant 16).
    - The candidate art goes into `godot/art/` in the working tree, uncommitted, and the `art`
      scenario captures the "after" gallery at the default, closest and widest zooms.
    - `review_sheet.py` (§5.8) builds `public/images/<set>/review-<page>.png`: every frame
      numbered, before and after at 4× pixel view and in the game at the three zooms.
    - The user names the frames that look wrong by their numbers. **Only those** are worked on
      again, each one way: a ComfyUI redo (back to step 4), a scripted fix in `fixes.json`
      (back to step 7), or the user's hand on that one frame. The redone frames go on a new
      sheet. The rest stay as they are.
    - The set goes on when the user passes the sheet.
11. **Verify.** `verify_art.py`, `check_art.py`, `python -m unittest discover Tools/Art/tests`,
    `dotnet test`, the `art` scenario and the set's own scenarios (§7.5), and `perf` when the set
    adds nodes, frames or lights.
12. **Commit the art.** Into `godot/art/` under the names the manifest and the code agree on
    (`art-style-bible.md` §0.5, §4.3), each with its `.import`, and the review sheet and
    `review.json` in `public/images/<set>/`.
13. **Bookkeeping**, in the same commit:
    - a file a generator owned: that generator's `HAND_REPLACED` set (add one to a generator
      that has none, copying `generate_topdown_environment.py`'s `_save` guard: it skips the
      name only when writing to `godot/art/`, so `--out-root` still renders the original), and
      `verify_art.py`'s `KNOWN_HAND_REPLACED`; its manifest `owner` becomes `hand-replaced`;
    - a file no generator owned (the chassis, the Artificer, the boiler, the bench, the HUD):
      its manifest `owner` becomes `comfyui`;
    - the manifest entry's `palette` to `bible` and its `source` folder;
    - one line in `CLAUDE.md`'s "AI image generation" exceptions (the tower and carts are the
      model).

One commit per set, or per chassis inside the golem set (all 28 of its files at once).

## 3. Replacement order

**Decided** (decision 10): golems; the Artificer; floor and walls; lighting; buildings; the
Workbench; goods; the HUD.

| # | Set | Files | Why here |
|---|---|---|---|
| — | **Cleanup** (§3.1) | Delete the 26 unused files | Before S1, so nothing unused is redrawn or listed |
| S1 | **Golems** | 5 chassis × 28 frame files (140), the fallback `golem_generic_copper` | Most watched, weakest today, and the moods depend on them |
| S1b | **The Artificer** | 16 walk frames | Always on screen, and the other RetroDiffusion art (decision 5) |
| S2 | **Floor and walls** | Floor and street tiles, edges, walls, posts, props, `ground_shadow` | **Closed, nothing replaced** (decision 18): today's art stays |
| L | **Lighting** (§6) | No art files; `LightingRules`, `ShellNode`, boiler glow, hot pipe variants | Right after S2, so later sets are reviewed under the final light |
| S3 | **Buildings** | Depot, station, Post, mast, recycler, slag heap, boiler, bench, belt tiles, pipe pieces (+ hot variants) | Carts and tower are done |
| S4 | **The Workbench** | Lever housing, display face, card faces, panels | The core verb's screen |
| S5 | **Goods** | 24 `item_*` | Small, generator-owned, cheap; copper ore stays green |
| S6 | **The HUD** | The seven Steampunk pieces in use, redrawn under new names; the tech tree chrome | Last: mostly flat style boxes today. The pack leaves the tree here |

Overlays (`build_ghost_tile`, `facing_arrow`, `interaction_ring`) are state tools, not
decoration. They stay generated and are only re-coloured to the palette if a review asks.

### 3.1 The cleanup commit (decision 11)

One commit, before S1 and before the tooling's manifest is built (so the manifest never lists
them):

1. **A full scenario run** on the current tree (every scenario, headless), and **a capture of
   each screen** (windowed, `--capture-dir`): the room at the default zoom, the HUD and alerts
   strip, the build menu, the guide panel, the playtest card, the construction panel, the
   Workbench, every Management tab, the Pack screen and a Crate screen. Into
   `public/images/cleanup/before/`.
2. Delete the 26 files of §1.1 with their `.import` files, and their generator lines:
   `belt_lane`, `belt_arrow`, `belt_roller` in `generate_placeholder_art.py`; `floor_edge_se`,
   `wall_segment_ne`, `wall_segment_ne_lamp` in `generate_topdown_environment.py`; `tt_line_h`,
   `tt_line_v` in `generate_tech_tree_art.py`.
3. The same scenario run and the same captures, into `public/images/cleanup/after/`. Every
   scenario passes, and each after capture matches its before capture pixel for pixel (nothing
   drawn used a deleted file).
4. `verify_art.py` exits 0. Expected: **129 PNGs; 71 generated (60 byte-identical, 11
   hand-replaced), 0 drifted, 58 not generated.** After the §5.5 fix: **95 generated (84
   identical, 11 hand-replaced), 34 not generated.**

The Steampunk licence note (`docs/source-assets/SteampunkFantasyUIPack/`) stays until S6, since
seven pieces remain.

**Done (2026-10-23).** Every file was searched for again first (code, scenes, data, generators,
names built at run time such as `ManagementScreen`'s `"Steampunk/" + sprite`); none of the 26 was
read. The captures are the scenarios' `screen-*` shots (`--capture-dir`, one per screen listed
above, plus the four `critique-*`), taken into `public/images/cleanup/before/` and `after/`:
all 17 are identical pixel for pixel, and two runs before the deletion were too, so the
comparison is exact. All 16 scenarios passed headless before and after. `verify_art.py`: 129
PNGs, 71 generated (60 byte-identical, 11 hand-replaced), 0 drifted, 58 not generated, as
expected. Besides the lines named above, the deleted names also left `generate_placeholder_art.py`'s
`--legacy` list and `trim_character_alpha.py`'s list (which would have failed on a missing file).
One pre-existing flake, not from this change: the `hover` scenario fails about one windowed run in
two ("W's card says nearly full"); it passes headless, which is how scenarios are run.

### 3.2 The Presser, the template for the next chassis

What the Presser's three review rounds settled (2026-10-23), so the next chassis starts there:

- **A creature, drawn as a layered draft first.** Plain image-to-image from the old sprite, or
  text-to-image, can't hold a design across 28 frames. The Presser's frames come from a script
  (`public/images/S1/brass_presser/scripts/presser_draft.py`) that draws each frame as named parts
  (legs, body, head, upper arms, forearms, hands, the held tool, the pack; `far_arm`/`far_leg` in
  profile) with a map of which part owns each pixel. Copy it per chassis and change the shapes.
- **Only each facing's base goes through ComfyUI.** South, north and east bases: img2img of the
  draft at denoise 0.35–0.55 (0.55 picked for south and north, 0.45 for east), seeds swept, the
  control-LoRA canny at 0.7 ending at 0.8, IP-Adapter plus vit-h at 0.5 ("style transfer") on
  `stall_scrap` at 4×, 512×768 on a `#28be46` key, 8 LCM steps, cfg 1.5. The pick step scores
  every candidate by `check_art`'s measures; all nine of each Presser facing passed, so the pick
  among them is taste (§5.10). Higher denoise lost the upper-left light.
- **Every other frame is composed, never rendered** (`golem_set.py`'s compose): rigid parts move by the
  pose's offsets, arms come from the draft, and the render adds only a one-tone step inside a part,
  never on its border, so texture can't shimmer between frames. `golem_set.LAYERS` gives the
  draw order per facing; from the north, what the golem holds is drawn first, behind it.
- **West is the east shapes mirrored and re-lit by the draft's shading**, then composed with the
  mirrored east texture. `reshade_mirror.py` on finished east frames failed the light check for a
  profile (its front isn't the mirror of its back).
- **Cleanup:** `palette_snap.py --key #28be46 --key-holes 256 --reduce 8 --outline --canvas 64,96`
  with the chassis's ramps; fire only through fixes (with fire in the snap, bright brass landed on
  fire orange).
- **Review:** `review_sheet.py` plus a compact numbered grid (`grid.png`: 4×, rows south, north,
  east, west; columns base, idle_1, work_0–2, stall, carry; today's sprite beside). The user marks
  frames by number. Install: the frames into `godot/art/` with their `.import` files, manifest
  entries (`owner: comfyui`, `mirroredFrom` on west), `review.json`, and the `art`, `golems`,
  `golem-motion` scenarios plus a look at the game.
- **Time:** about 5 min for ComfyUI's first image, 7–11 s after; the renders are ~10 min a
  chassis. Drafting the creature and its poses is most of the work.
- **All of it is one command now** (2026-10-24): `python Tools/Art/golem_set.py <set> run`,
  with the chassis' own settings in `<set>/chassis.json` (§5.10). The Presser's `chassis.json`
  records its prompts and picks, and `golem_set.py public/images/S1/brass_presser verify`
  recomposes its 28 frames pixel for pixel from the kept renders.
- **Still plainer than the carts**, and the profile is mostly iron with a small ram; the user
  accepted it. A later pass may raise detail where the light check allows.

## 4. Inventory, by set

"Reads" is the code or data that loads the file or depends on its size or pivot. Sizes are
pixels. "Gen" names the generator (`topdown` = `generate_topdown_environment.py`, `building`,
`placeholder`, `workbench_ui`, `tech_tree`, `clock_tower`). The unused files of §1.1 are left
out: they are gone before S1.

### S1 Golems

| File | Size | Owner | Frames | Reads |
|---|---|---|---|---|
| `chassis_clockwork_scavenger.png` | 64 × 96 | RetroDiffusion | south base | `data/chassis.json` `chassisSprite`; `GolemNode.ApplyChassisSprite` (`StandOnCell`); `WorkbenchScreen` (chassis pane, enlarged) |
| `chassis_brass_presser.png` | 64 × 90 | RetroDiffusion | south base | same |
| `chassis_aether_hauler.png` | 64 × 86 | RetroDiffusion | south base | same |
| `chassis_mainspring_overclocker.png` | 64 × 96 | RetroDiffusion | south base | same; `GolemNode.GlowTint` multiplies it |
| `chassis_zeppelin_freight_loader.png` | 64 × 93 | RetroDiffusion | south base | same |
| `<stem>_<state>[_<n>]_<facing>.png`, 27 per chassis, 135 in all (new) | the chassis' one size | ComfyUI (105 with the bases) / `reshade_mirror.py` (35, west) | the rest of the set | `GolemFrames.FileName`, `Resolve` (§7.4) |
| `golem_generic_copper.png` | 64 × 92 | authored (legacy placeholder) | 1, south | `GolemNode.FallbackSprite` |
| `fx_steam_puff.png` | 24 × 20 | ComfyUI | 1 | `GolemNode.PuffSprite`, scaled by `GolemWorkMotion.Puff` |
| `ground_shadow.png` | 48 × 36 | Gen topdown | 1 | `SpritePivots` (0.5, 0.5); under golems and props |

**The frame count** (decisions 2 and 3, `art-style-bible.md` §4.1): 8 frame slots per facing,
7 distinct files (base, `idle_1`, `work_0`–`work_2`, `stall`, `carry`; the idle pulse's first
frame is the base). **28 files per chassis, 140 for the five: 105 drawn (south, north, east),
35 mirrored and re-shaded (west).** The south base keeps its data name; the other 27 are named
`<stem>_<state>[_<n>]_<facing>.png` (`art-style-bible.md` §4.3), for example
`chassis_brass_presser_work_1_east.png`.

Code limits: ≤ 64 × 96 (`BadgeLiftCells` 1.75; hover covers two cells); feet on the bottom row;
one canvas and foot row for all 28 files of a chassis. The redraw may change a chassis' height
within 96 (all five may become 64 × 96); `GolemNode` stands each on its feet whatever its height.

Order inside S1, per chassis: south base → north and east bases → the other frames, facing by
facing → west by mirror → review sheet → commit. The first chassis's sheet also confirms the
brass highlight and the fire ramp (decision 1).

### S1b The Artificer

| File | Size | Owner | Frames | Reads |
|---|---|---|---|---|
| `artificer_walk_{down,left,right,up}_{0..3}.png` (16) | 64 × 93 each | RetroDiffusion concept sheet, sliced by `slice_artificer_walk.py`; adjusted by `public/images/S1b/artificer/scripts/` (decision 16) | 4 × 4 | `PlayerNode` builds the names; `ArtificerWalkAnimation` (4 directions, 4 frames, stride 0.45 cells) |
| `artificer_stand_{down,left,right,up}.png` (4) | 64 × 93 each | each walk's frame 0 before its foot was lifted (`walk_feet.py`) | 1 a direction | `PlayerNode`; `ComputeStandingSpriteIndex` |
| `artificer_crank_{down,left,right,up}_{0..3}.png` (16) | 64 × 93 each | the standing pose, rocked (`crank_frames.py`, decision 17) | 4 a direction | `PlayerNode`; `ComputeCrankSpriteIndex`, frame = `HandCrankBench.TurnFrame` |

Frame 0 of each direction is that direction's base; frames 1–3 are pose guides from it (§2,
step 3). A mirrored walk cycle swaps the leading leg, which reads as walking the other way.

### S2 Floor and walls

| File | Size | Owner | Reads |
|---|---|---|---|
| `floor_tile.png`, `floor_tile_wood_b/c/d.png` | 64 × 64 | Gen topdown | `FloorLayer` by name; seamless |
| `floor_tile_accent.png`, `floor_tile_grate.png` | 64 × 64 | Gen topdown | `FloorLayer` |
| `street_cobble.png`, `street_cobble_b.png` | 64 × 64 | Gen topdown | `FloorLayer` |
| `street_edge.png` | 64 × 24 | Gen topdown | `SpritePivots` (0.5, 1) |
| `floor_edge_sw.png` | 64 × 24 | Gen topdown | `SandboxLayout`, `SpritePivots` (0.5, 1) |
| `wall_segment_nw.png`, `wall_segment_nw_lamp.png` | 64 × 96 | Gen topdown | `SandboxLayout` (lamp on every fourth), `SpritePivots` (0.5, 3/96) |
| `wall_side_e.png`, `wall_side_w.png` | 40 × 64 | Gen topdown | `SpritePivots` (3/40 or 37/40, 0.5) |
| `wall_corner_post.png` | 50 × 96 | Gen topdown | `SpritePivots` (0.5, 1/96) |
| `prop_barrel.png` | 40 × 56 | Gen topdown | `SandboxLayout.Props`, `SpritePivots` (0.5, 1/56) |
| `prop_crate.png`, `prop_hearth.png`, `prop_workbench.png` | 56 × 56 | Gen topdown | same |
| `prop_shelf.png` | 52 × 56 | Gen topdown | same |
| `prop_tool_rack.png` | 48 × 56 | Gen topdown | same |

The lamp wall's painted sconce sits where `ShellNode` puts the light
(`LampLightInsetCells` −0.25 below the wall's anchor); a redraw keeps the flame in the same column.

**Closed on 2026-10-28 with every file above unchanged** (decision 18). The pilot's renders and
picks stay in `public/images/S2/` (`set.json` records them), and `Tools/Art/env_set.py` stays for
any later environment redraw.

### S3 Buildings

| File | Size | Owner | Reads |
|---|---|---|---|
| `depot.png` | 62 × 69 | Gen building | `placeables.json`, `DepotNode`, `StandOnCell` |
| `golem_construction_station.png` | 78 × 96 | Gen building | `placeables.json`, `ConstructionStationNode` |
| `pneumatic_post.png` | 62 × 92 | Gen building | `placeables.json` |
| `freight_mast.png` | 52 × 118 | Gen building | `placeables.json` |
| `scrap_recycler.png` | 62 × 56 | Gen building | `placeables.json` |
| `slag_heap.png` | 68 × 44 | Gen building | `placeables.json` |
| `steam_boiler.png` | 71 × 114 | authored | `placeables.json`; gets a glow (§6) |
| `hand_crank_bench.png` | 94 × 82 | authored | `placeables.json` |
| `hand_crank_bench_turn_{0..3}.png` (4) | 94 × 82 each | `hand_crank_bench.png` with rim glints (`crank_frames.py`, decision 17) | `HandCrankBench.SpriteFor`, while it turns |
| `belt_tile.png`, `belt_tile_corner_left.png`, `belt_tile_corner_right.png`, `belt_splitter.png` | 64 × 64 | Gen topdown | `placeables.json` shape sprites, `BeltRun`, `BuildingsLayer` (centred, rotated) |
| `steam_pipe.png`, `_corner`, `_cross`, `_end`, `_tee` | 64 × 64 | Gen building | `BuildingsLayer` (centred, rotated); each has a `_hot` twin (§6.5) |
| `stall_*.png` (5), `clock_tower_*.png` (6) | 96 × 120, 192 × 640 | hand-replaced | Done; not in this set |
| `ghost_placeholder.png` | 64 × 64 | authored | `BuildingsLayer` fallback, `fallbackItemSprite` |

### S4 The Workbench

| File | Size | 9-slice margin | Owner | Reads |
|---|---|---|---|---|
| `UI/Workbench/wb_panel_mahogany.png` | 48 × 48 | 12, tiled | Gen workbench_ui | `WorkbenchScreen` backdrop, viewport, vault |
| `wb_panel_iron.png` | 48 × 48 | 12 | Gen | rack, bottom bar, **the lever housing** (a `TextureButton`), status plate |
| `wb_panel_blueprint.png` | 48 × 48 | 12, tiled | Gen | **the display face** (chassis pane) |
| `wb_plate_brass.png` | 32 × 32 | 10 | Gen | header bar, title plate |
| `wb_slot_socket.png` | 32 × 32 | 10 | Gen | sockets (tinted by `WorkbenchDragVisuals`) |
| `wb_card_face.png` | 32 × 32 | 10 | Gen | **the card faces**, tinted per card kind |
| `wb_tape.png` | 32 × 64 | 0 | Gen | diagnostic tape |
| `wb_lever_track.png` | 40 × 120 | — | Gen | lever |
| `wb_lever_handle.png` | 34 × 30 | — | Gen | lever handle (moves) |
| `wb_gauge_needle.png` | 16 × 16 | — | Gen | dial pip |

The three named pieces (lever housing, display face, card faces) are today 9-slices of generic
panels. Drawing them as pictures, a housing with the lever in it, a framed display, a card with a
punched edge, means new files (`wb_lever_housing.png`, `wb_display_face.png`, card frames per
kind) and a change to `WorkbenchScreen`'s `Ugui` numbers. That is a code change inside the set,
with the `workbench` scenario as its check (the hint's frames must still sit on the controls).
The Workbench's Steampunk pieces (its gear, close, chassis and patent buttons) are redrawn in
S6 with the rest of the pack.

### S5 Goods

| Files | Size | Owner | Reads |
|---|---|---|---|
| `item_scrap`, `item_coal`, `item_copper_ore`, `item_zinc_ore`, `item_aether`, `item_brass` | 32 × 32 | Gen placeholder (`belts` section) | `placeables.json` `itemSprites` (belts, drawn at 32); `ItemIcons.PathFor` (UI rows, drawn at 24); `BeltRun` default `item_scrap` |
| `item_coke`, `item_iron_plate`, `item_slag`, `item_glass`, `item_copper_ingot`, `item_zinc_ingot`, `item_copper_wire`, `item_gear`, `item_casing`, `item_lens`, `item_mainspring`, `item_aether_cell`, `item_mechanism`, `item_regulator`, `item_frame_section`, `item_aether_conduit`, `item_chronometer_core`, `item_great_cog` | 32 × 32 | Gen placeholder (`goods` section) | same |

`item_copper_ore.png` stays malachite green (`art-style-bible.md` §0.6). The goods are redrawn
through ComfyUI like every set and hand-replaced (all 24 into `generate_placeholder_art.py`'s
`HAND_REPLACED`); the rows stay at 24 (decision 6), so each good is checked at 24 at all four
sampling phases (`art-style-bible.md` §5.4, check 11).

### S6 The HUD

**The Steampunk pack is dropped** (decision 9). The seven pieces in use are redrawn through
ComfyUI like everything else, under new names in `UI/Hud/`, with **the same size and 9-slice
margins**, so no `Ugui` number moves:

| Today (`UI/Steampunk/`) | Redrawn as (`UI/Hud/`) | Size | 9-slice | Reads (path changes in S6) |
|---|---|---|---|---|
| `steampunk_panel_iron_bolt.png` | `hud_panel_bolted.png` | 64 × 64 | 12/10/12/10 | `HudOverlay` (alerts strip), `BuildMenuNode` (menu, card), `TutorialPanel` (plate, notice), `PlaytestNode` (card) |
| `iron_panel_nobolt.png` | `hud_panel_plain.png` | 64 × 64 | 8 | `ManagementScreen` (background), `ConstructionPanelNode`, `InventoryPanel` |
| `steampunk_button_blank.png` | `hud_button.png` | 34 × 34 | 8 | `BuildMenuNode` (rows), `AssemblyLineTab`, `ConstructionPanelNode`, `ManagementScreen` (tabs, Load), `CrateScreen` (label, Take all), `WorkbenchScreen` (chassis) |
| `steampunk_button_blank_iron.png` | `hud_button_active.png` | 34 × 34 | 8 | `BuildMenuNode` (active row), `WorkbenchScreen` (Patent) |
| `steampunk_button_accept.png` | `hud_button_accept.png` | 34 × 34 | 8 | `ManagementScreen` (Save) |
| `steampunk_button_exit.png` | `hud_button_close.png` | 34 × 34 | 8 | `ManagementScreen`, `InventoryPanel`, `WorkbenchScreen` (Close) |
| `gear_large.png` | `hud_gear.png` | 58 × 59 | — | `WorkbenchScreen` (the gear) |

Each gets a manifest entry (owner `comfyui`, `set: S6`, its `nineSlice`). Renaming is a code
change (`art-style-bible.md` §0.5), made in S6's commit: the paths in the ten files above
(`CrateScreen` and `InventoryPanel` build them from a `Steampunk` constant). The buttons carry
no lettering (invariant 12); the code draws "Close", "Save" and the rest in the font.

**When S6 lands**, in the same commit:

- `godot/art/UI/Steampunk/` is removed, all seven files and their `.import`s;
- `docs/source-assets/SteampunkFantasyUIPack/` (the PSD and `ReadMe.txt`, the licence note) is
  removed, since no piece of the pack remains;
- the wording that names the pack is updated: `CLAUDE.md`'s art section ("the Steampunk pack" in
  `verify_art.py`'s not-generated list), `verify_art.py`'s docstring, and
  `generate_workbench_ui_art.py`'s header comment. The history docs
  (`docs/history/unity-implementation-plan.md`) and the Phase 1 design docs that mention the
  pack keep their wording; they record what was true then.

The rest of the HUD set:

| Files | Size | Owner | Reads |
|---|---|---|---|
| `UI/TechTree/tt_*` (11 after the cleanup) | 16 × 16 to 32 × 32 | Gen tech_tree | `LedgerTab`; plaques and phase plate 9-slice 10 |
| `interaction_ring.png` | 64 × 64 | Gen topdown | `InteractionPromptNode` |
| `facing_arrow.png` | 28 × 28 | authored | `GolemNode` (and at 0.6 on the tiles), `BuildCursorNode`, `TutorialPanel` |
| `build_ghost_tile.png` | 64 × 64 | Gen topdown | `BuildCursorNode`, `GolemNode` (tile overlay, carry preview) |

## 5. Tooling to build

All of it in `Tools/Art/`, Python 3 with Pillow (already required), standard library otherwise.
Tests are `unittest` in `Tools/Art/tests/`, run by `python -m unittest discover Tools/Art/tests`,
on small synthetic images built in the test, so they need neither ComfyUI nor `godot/art/`.
Every tool that writes images refuses an output under `godot/art/` unless it is the commit step
(`palette_snap.py` and `reshade_mirror.py` take `--into-art` for that, and nothing else does).

The shared measures live in one module, **`Tools/Art/art_metrics.py`**: sRGB to OKLab, alpha
masks aligned at the feet, intersection over union, the edge-light score
(`art-style-bible.md` §5.1), and the 32 → 24 reduction at a sampling phase. `check_art.py`,
`reshade_mirror.py`, `review_sheet.py` and the pick (§2, step 5) all import it, so a check and
the tool it checks can't measure differently. Its tests: OKLab of known colours, IoU of known
masks, the edge-light score of a left-lit and a right-lit gradient (positive, negative).

**Built (2026-10-23)**, with 79 unit tests (`python -m unittest discover Tools/Art/tests`).
Where the build settled something this section left open:

- **The manifest** has 129 entries and three fields beyond §5.1: `kind` (`standing`, `floor`,
  `edge`, `tile`, `good`, `fx`, `shadow`, `overlay`, `ui`), which picks the colour-count limit
  and which checks apply; `footSlack` with a `footNote` (the six tower pictures stand 2 rows above
  their canvas bottom, measured, and say so); and `wallHung` (the tool rack hangs on the wall, so
  it has no foot row). Overlays carry `partialAlpha`. The carts and the tower are set `reference`.
  `--write` is `check_art.py --write`.
- **check_art.py** binds checks 1–3, 12, 15 and 16 on everything; 4–6 only on a replaced entry
  (`palette` not `legacy`); 7–11, 13 and 14 FAIL on a replaced entry and WARN on legacy art, so
  the report shows the baseline without failing the build. Check 10's ambient is white until
  `LightingRules` exists (§6). Check 12's "ore pixels" are the saturated pixels outside the wood
  hues (0°–60°), on the good and the cart alike. Check 15 reads `Tools/Art/CLAUDE.md`, where the
  hand-replaced list now lives, and wants each file's full name in backticks.
- **palette_snap.py** judges `--outline` on the colour before the snap (step 3 would otherwise
  have moved a black edge onto another ramp's dark tone first).
- **Added in the first S1 run (2026-10-23):** `palette_snap.py --key <colour> [--key-holes N]`
  clears a render's flat background (ComfyUI returns no alpha; the init is composited on that
  colour), `--canvas W,H` stands the result on its canvas with the feet on the bottom row, a
  `fixes.json` entry may give a `"ramp"` instead of a `"tone"` (the rectangle's pixels move to that
  ramp's nearest tones: one part, one material, which `reshade_mirror.py` needs), and
  `--max-change` counts only pixels the snap moved further than 0.08 in OKLab (every render is a
  shade off the palette, so "moved at all" refused everything). `comfy_redraw.py` uploads each
  image under its role and a hash of its bytes: by file name, an init and a reference both called
  `init.png` were one image on the server.
- **review_sheet.py** reads the in-game columns as `<frame stem>-{default,close,wide}.png` from
  `--game-before` and `--game-after`; the `art` scenario's per-frame crops are its input once
  that scenario exists, and a missing one is an empty, labelled cell.
- **The baseline** (`check_art.py` on today's art): 0 FAIL, 63 WARN. The golems would fail
  checks 8 (three pairs at 0.58–0.60), 10 (lightness 0.27–0.34 on four chassis; Stalled shift
  0.057–0.083; stopped tints 0.041–0.059 apart), 13 (1 of 28 frames each) and 14 (four chassis
  and all 16 walk frames under +0.02); the goods checks 7 (zinc's steel blue) and 11.

### 5.1 The manifest: `Tools/Art/art_manifest.json`

One entry per PNG under `godot/art/`, keyed by its path relative to it, built after the cleanup
commit:

```json
"chassis_brass_presser_work_1_west.png": {
  "set": "S1",
  "size": [64, 96],
  "pivot": "bottom-center",
  "footRow": 95,
  "owner": "comfyui",
  "chassis": "chassis_brass_presser",
  "state": "work_1",
  "facing": "west",
  "mirroredFrom": "chassis_brass_presser_work_1_east.png",
  "palette": "bible",
  "source": "public/images/S1/brass_presser/",
  "readers": ["GolemFrames.FileName", "GolemNode"]
}
```

- `owner`: `generated:<script>`, `hand-replaced` (a generator owned it once), `comfyui` (no
  generator ever did), `retrodiffusion`, `authored`, or `third-party` (until S6).
- `pivot`: `bottom-center`, `center`, or `[x, y]` in Unity's bottom-left fractions, exactly as
  `SpritePivots` writes them. `footRow` is derived and stored so a reader needn't compute it.
- `palette`: `bible` once the set is replaced (the palette and alpha rules apply), `legacy`
  until then (they don't), or a list of extra ramps.
- `chassis`, `state`, `facing` (golem and Artificer frames): what `GolemFrames` would ask for;
  check 13 reads them. `mirroredFrom`: the drawn frame a mirrored one came from; check 14 reads
  it.
- `shadowRows`: rows where partial alpha is allowed (the carts' 114–119).
- `nineSlice`: the margins the code passes, for UI plates.

`--write` regenerates the mechanical fields (size, foot row) from the files; the rest is
hand-kept. The first version is built from §4.

### 5.2 `Tools/Art/check_art.py` and `Tools/Art/palette.json`

`python Tools/Art/check_art.py [file …]`: exit 1 on any failure, one line per finding. With no
arguments it checks the whole manifest.

`palette.json` holds the ramps of `art-style-bible.md` §1 by name, and the reserved-colour
limits of its §5.3.

Checks:

1. **Complete:** every PNG under `godot/art/` has an entry, and every entry has a file.
2. **Size** matches the manifest. For a chassis, ≤ 64 × 96.
3. **Foot row:** for a `bottom-center` sprite, the lowest row with an opaque pixel (alpha ≥ 128)
   is the bottom row; for a fractional pivot, it is the pivot row, give or take the
   `shadowRows`. A sprite that floats fails.
4. **Alpha** (`palette: bible` only): every alpha is 0 or 255 outside `shadowRows`, unless the
   entry is FX or UI glow.
5. **Palette** (`bible` only): every opaque colour is in the set's ramps; report the strays.
6. **Colour count** against `art-style-bible.md` §2.3 (48, 12, 16).
7. **Reserved colours** (§5.3): no more than 5% saturated red or steel blue.
8. **Silhouettes, per facing** (S1): for each facing, the pairwise alpha-mask overlap of the
   five chassis' base frames, aligned at the feet, ≤ 0.50.
9. **A pose keeps its chassis** (S1): every frame overlaps its own chassis' base in its facing
   more than any other chassis' base in that facing.
10. **Tint shift** (S1, every frame): mean OKLab lightness ≥ 0.40; Stalled shift ≥ 0.10; the
    three stopped or waiting tints ≥ 0.06 apart. The tints are read from `GolemMoodPalette.cs`
    by a regex, so the check can't drift from the code, and multiplied by the room's ambient
    colour from `LightingRules` (§6) once that exists.
11. **Goods** (S5), at 24 × 24 and at each of the four sampling phases: pairwise mask overlap
    ≤ 0.80 or mean colour ≥ 0.15 apart; and each good keeps its number of connected pieces and
    at least three tones (`art-style-bible.md` §5.4).
12. **Copper ore** is green on both files: the most common opaque hue of `item_copper_ore.png`
    and of the ore pixels of `stall_copper_ore.png` falls in 100°–160°.
13. **Frame sets complete** (S1, S1b): every chassis in `data/chassis.json` has its south base
    and 27 frame files, all one size and one foot row; the Artificer has its 16. Before a
    chassis' set lands, missing frames are warnings (the fallback covers them); once any frame
    of a chassis is `bible`, all 28 must be.
14. **Light from the upper left** (every `bible` standing sprite): the edge-light score
    (`art-style-bible.md` §5.1) is ≥ +0.02; and a frame with `mirroredFrom` scores ≥ 0.8 × its
    source's on the edge-light or the edge-tone score (`art_metrics.edge_tone_score`), has the
    plain flip's alpha mask exactly, and has every pixel in the same ramp as
    the plain flip's (re-shading changes tone, never material or shape).
15. **Hand-replaced bookkeeping:** an entry with `owner: hand-replaced` appears in
    `verify_art.py`'s `KNOWN_HAND_REPLACED` and in `CLAUDE.md`; a `KNOWN_HAND_REPLACED` name has
    an entry that says so.
16. **No pack left** (once S6 lands): no entry has `owner: third-party`, and nothing under
    `UI/Steampunk/` exists.

Checks 8–12 run on the current art too and report its baseline (§1.2) as warnings while the set
is `legacy`, failures once it is `bible`.

Tests (`tests/test_check_art.py`): one synthetic image per failure (a canvas a pixel too tall, a
floating foot row, a half-alpha pixel, an off-palette pixel, two identical silhouettes in one
facing, a stall pose closer to another chassis than its own, a red body, a copper ore turned
orange, a good that splits in two at one sampling phase, a chassis missing its north stall
frame, a right-lit frame, a plain-flipped mirror, a missing manifest entry), each failing with
its message, and a clean image passing.

### 5.3 `Tools/Art/palette_snap.py`

`python Tools/Art/palette_snap.py <in.png> <out.png> --ramps outline,mahogany,brass
[--reduce 8] [--alpha 128] [--outline] [--fixes <fixes.json>] [--max-change 0.10]`

1. `--reduce N`: N× to 1×, k-centroid per block (the block's most common colour after a
   two-means split), which keeps a one-pixel line that a nearest pick can drop.
2. Alpha to 0 or 255 at the threshold, except in `--shadow-rows`.
3. Each opaque pixel to the nearest colour of the named ramps, in OKLab.
4. `--outline`: an opaque pixel next to transparency whose OKLab lightness is under 0.2 becomes
   the outline ramp's nearest tone, which is the carts' black-to-brown softening as a tool.
5. `--fixes`: the scripted fixes for this file from the set's `fixes.json`, each a rectangle
   and a ramp tone (or transparent), applied last and checked to be on the palette.
6. Prints pixels changed per step, and fails if more than `--max-change` moved in step 3.

Tests (`tests/test_palette_snap.py`): a 2 × 2 block reduces to its majority colour; a
one-pixel line survives `--reduce 8`; a half-alpha pixel goes to 0 or 255; an off-palette pixel
lands on its nearest ramp colour and an on-palette pixel doesn't move; a black outline turns
warm brown; the shadow rows keep their alpha; a fix paints its rectangle, and a fix with an
off-palette colour is refused.

### 5.4 `Tools/Art/comfy_redraw.py`

Against the local ComfyUI's HTTP API, standard library only, in the shape of
`G:/AI/comfy_generate.py`. One call renders one frame's candidates.

```
python Tools/Art/comfy_redraw.py --init <8x.png> --out public/images/<set>/raw/<frame>/
    [--prompt "..."] [--denoise 0.35,0.5,0.65] [--seeds 4]
    [--guide <guide_8x.png> --control-model <name> --control-strength 0.8 --control-end 0.8]
    [--reference <cleaned_south_base_8x.png> --ipadapter-model <name>
     --clip-vision <name> --ipadapter-weight 0.7]
    [--txt2img --width 512 --height 768]
```

- **The base workflow:** `LoadImage` of `--init`, `VAEEncode`, KSampler (LCM, 8 steps, cfg 1.5)
  at each `--denoise`, SDXL with Pixel Art XL at 1.2, `VAEDecode`, save. `--txt2img` starts
  from an empty latent instead (an east base, §2 step 3).
- **`--guide`** adds ControlNet: `LoadImage` of the guide, `ControlNetLoader` of
  `--control-model`, `ControlNetApplyAdvanced` on the positive and negative conditioning at
  `--control-strength`, ending at `--control-end` of the steps (so the last steps can settle
  the pixels). The guide is `pose_guide.py`'s lineart.
- **`--reference`** adds IP-Adapter: `CLIPVisionLoader` of `--clip-vision`, the IP-Adapter
  model loader of `--ipadapter-model`, and the apply node on the model at `--ipadapter-weight`,
  with the cleaned south base as the image. The class names are the installed pack's, recorded
  in §1.4: `IPAdapterModelLoader`, `CLIPVisionLoader` and `IPAdapterAdvanced` (weight type
  `style transfer`, as the smoke test ran it; `--ipadapter-weight-type` changes it).
- **Built (2026-10-23).** It also takes `--guide-preprocess canny` (the
  `CannyEdgePreprocessor` before ControlNet, for a guide that isn't already lineart),
  `--negative`, `--seed-base`, `--url` and `--dry-run` (prints the workflow, queues nothing).
  Candidates are named `<frame>_d<denoise>_s<seed>.png` in `--out`.
- **6 GB:** batch size 1, one ControlNet, the models named on the command line (no defaults
  that load a larger model), and the guide and reference at the render size, never larger.
- It refuses an `--out` under `godot/art/` and a `--guide` without `--control-model`.

No test needs the server: the workflow it builds is a function. Tests assert the base
workflow's nodes; that `--guide` adds the ControlNet nodes wired into both conditionings and
`--reference` adds the IP-Adapter nodes wired into the model, and neither appears without its
flag; that both together form one valid graph; and that it refuses `godot/art/`.

### 5.5 `verify_art.py` and the placeholder generator

- `generate_placeholder_art.py`: drop the value after `--out-root` from the section list, and
  make the default sections `belts` and `goods` (the only ones that can run without tripping
  `TOP_DOWN_OWNED`).
- `verify_art.py`: run it with those sections. The expected result after the cleanup and the fix
  is 95 generated (84 identical, 11 hand-replaced), 34 not generated (§3.1).
- Add a `HAND_REPLACED` guard to `generate_building_art.py`, `generate_placeholder_art.py` and
  `generate_workbench_ui_art.py`, the same shape as the top-down generator's, before their first
  file is replaced.
- Test (`tests/test_verify_art.py`): with `--out-root` at a temporary folder, the placeholder
  generator writes its files there and nothing under `godot/art/`.
- **Done (2026-10-23).** The guard is one helper, `art_paths.skip_hand_replaced` (with
  `writes_into_godot_art`), which the three generators' `save` functions call; each has an empty
  `HAND_REPLACED` until its first file is replaced. The top-down generator keeps its own copy.

### 5.6 `Tools/Art/pose_guide.py`

Makes the ControlNet guide and the image-to-image start for one frame from a cleaned base.

```
python Tools/Art/pose_guide.py <cleaned_base.png> --poses public/images/<set>/poses.json
    --frame work_1 --out public/images/<set>/source/<frame>/ [--scale 8]
```

- `poses.json` gives, per frame, a list of parts: a rectangle or a set of ramp colours that
  selects the part's pixels, and its transform (translate in pixels, rotate in quarter or free
  degrees about a named pivot pixel, scale), plus `hide` rectangles (the face masked out for a
  north base).
- It moves the parts on a copy of the base at 1×, fills the holes they leave with transparency,
  and writes two files at `--scale`: `guide.png`, the lineart (white on black: the silhouette and
  every boundary between two ramps), and `init.png`, the moved colour image, both enlarged by
  nearest-neighbour.
- It refuses an `--out` under `godot/art/`.

Tests (`tests/test_pose_guide.py`): an identity pose gives the base's own edges; a translated
part's edges move by exactly its offset; a hidden rectangle leaves no edges in it; both files
are `--scale` × the base; it refuses `godot/art/`.

### 5.7 `Tools/Art/reshade_mirror.py`

Makes a west frame from an east one (and the Artificer's left from its right) so the light still
comes from the upper left (`art-style-bible.md` §2.2, invariant 15).

```
python Tools/Art/reshade_mirror.py <east.png> <west.png> --ramps <set's ramps> [--into-art]
```

1. **Flip** horizontally about the canvas centre. The canvas width must be even (every chassis
   and the Artificer are 64), so a BottomCenter pivot and the foot row stay exactly where they
   were; an odd width is refused.
2. **Classify** each opaque pixel by its ramp and its tone index in that ramp
   (`palette.json`). A pixel in no ramp is left alone and counted.
3. **Re-shade the interior.** In each row, for each maximal horizontal run of pixels of one
   ramp (the outline ramp excluded), reverse the tone indices along the run: each pixel keeps
   its ramp and its place, and takes the tone of its mirror place in the run. The flip reversed
   every run, so this restores the source's left-to-right tone order, which puts the highlights
   back on the left. Vertical shading is untouched, because a horizontal flip never moved it.
4. **Re-shade the outline.** In each row, swap the tones of the leftmost and rightmost outline
   pixels of each silhouette span, so a selective outline's stepped-up tone is back on the lit
   side (`art-style-bible.md` §2.1).
5. **Measure.** Prints the source's and the result's edge-light and edge-tone scores
   (`art_metrics`) and the count of untouched pixels, and exits 1 if the result's edge light is
   under +0.02, or under 0.8 × the source on both scores.

What it keeps on purpose: an asymmetric detail (a lamp on one shoulder) stays on the mirrored
side, as a mirrored body would carry it. What it can't fix, a shading that isn't a left-to-right
ramp across a run, shows on the review sheet, where the user can mark the frame.

Tests (`tests/test_reshade_mirror.py`): on a synthetic left-lit sprite, a plain flip scores
negative and the re-shaded one at least 0.8 × the source; the result's alpha mask equals the
plain flip's; every pixel stays in its plain-flip ramp and on the palette; the foot row doesn't
move; the stepped-up outline moves to the left; a pixel in no ramp is untouched and counted;
re-shading a re-shaded mirror's mirror gives back the source exactly; an odd width is refused.

### 5.8 `Tools/Art/review_sheet.py`

Builds the sheet the user reviews before a set is committed (§2, step 10; invariant 16).

```
python Tools/Art/review_sheet.py --set S1 --frames <file …>
    --before public/images/<set>/before/ --after public/images/<set>/after/
    --out public/images/<set>/
```

- One row per frame, **numbered from 1**: the before frame and the after frame at 4× pixel view
  on a floor tile; then, from the `art` scenario's captures, the frame in the game at the
  default, closest and widest zooms, before and after. A new frame with no before (a facing or
  pose that didn't exist) shows the frame it was made from, labelled so.
- A mirrored frame's row also shows its plain flip, so a re-shade that went wrong is visible.
- Pages of at most 28 rows (one chassis), `review-<page>.png`, and `review.json` mapping each
  number to its file, so "12 and 31 look wrong" maps back to files without guessing.
- Labels are drawn by Pillow's default font; the sheet is for the user, not the game.
- It refuses an `--out` under `godot/art/`.

Tests (`tests/test_review_sheet.py`): numbering follows the `--frames` order and is stable
across runs; `review.json` maps every number to its file; a page holds at most 28 rows; a frame
with no before is labelled; it refuses `godot/art/`.

### 5.10 `Tools/Art/golem_set.py`: a chassis in one command

Added 2026-10-24, after the Presser (§3.2), so a chassis costs a draft and the reviews, and the
mechanical steps can go to a helper agent on a smaller model (`Tools/Art/golem-helper.md`).

```sh
python Tools/Art/golem_set.py <set> run        # draft, render, pick, compose, check, review
python Tools/Art/golem_set.py <set> <step>     # draft | render | pick | compose | check | review [--game]
python Tools/Art/golem_set.py <set> install    # refuses until chassis.json has "approved"
python Tools/Art/golem_set.py <set> verify     # recompose from the kept renders = godot/art/?
```

- **`<set>/chassis.json` holds what is the chassis' own:** `stem`, `title`, `draft` (its draft
  script, relative to the set), `ramps`, `prompts` for south, north and east; optionally
  `layers` (per-facing part order over `golem_set.LAYERS`), `rigid`, `textured`, the render
  settings (defaults are the Presser's: denoise 0.35/0.45/0.55 × 3 seeds from 5000, canny 0.7
  to 0.8, IP-Adapter 0.5 on `stall_scrap`), `picks`, `dirs` and `approved`.
- **render** starts ComfyUI if it isn't up and stops it afterwards only if it started it, and
  skips a facing whose candidates are all on disk.
- **pick** scores every candidate and picks the one with the most render texture of those that
  pass `check_art`'s bounds (feet, silhouette, lightness, the tints, the edge light). The
  Presser showed that every candidate can pass, and its picks were made by eye: the auto pick
  matched 1 of 3. So the step writes `candidates_<facing>.png`, every candidate's base frame
  best first, and a choice by eye goes in `chassis.json`'s `picks`.
- **check** is `check_art` over a scratch copy of `godot/art/` with the frames in place, as if
  replaced. **install** reruns it and refuses on a FAIL, then copies, writes the manifest
  entries, imports (a headless Godot it starts and stops itself) and runs `art`, `golems` and
  `golem-motion`. It commits nothing.
- Only install writes into `godot/art/`.

Tests (`tests/test_golem_set.py`): a config missing its own parts is refused; defaults merge;
the 28 frames in review order; candidate names as `comfy_redraw.py` writes them; no failing
candidate is picked, and the pick is the most texture then the edge light; the manifest entries
(owner, ramps, west's `mirroredFrom`); compose refuses `godot/art/`; install refuses without
`approved`; the layering rule.

### 5.9 Commands this adds to `CLAUDE.md`

```sh
python Tools/Art/verify_art.py                 # generated art reproduces (exists)
python Tools/Art/check_art.py                  # the manifest: sizes, pivots, palette, frames, light, readability
python -m unittest discover Tools/Art/tests    # the tools' own tests
```

## 6. Lighting

Three things the roadmap asks for: sconces that light the floor, a warm room tint, steam that
glows. All of it in the GL Compatibility renderer, with no normal maps (an AI redraw can't be
given consistent ones) and no light occluders (cost).

**Built and approved by the user on 2026-10-28** ("It looks good") (§6.5 says what changed from this plan):
`Core/World/LightingRules`, `LightingRulesTests`, the warm ambient, banded pools
(`Scripts/World/LightTexture`), a glow on every boiler, the `world` and `golems` checks, and
`perf`'s `unlit` phase. Measured windowed at the endgame load: the lighting costs **0.27 ms**
of render CPU p95 (budget 1.0), 16 draw calls.

**Decided** (decision 8): **start subtle** (a warm ambient averaging about 0.88, today's sconce
energy, banded pools), with **every value in Core's `LightingRules`**, and let the playtest's
`light` question tune it. The floor is never darker than the mood tints and the tile overlays
can read on, and a test says so.

### 6.1 The rules in Core

`Core/World/LightingRules` (pure, engine-free, tested by `LightingRulesTests`), read by
`ShellNode` and `BuildingsLayer`:

- `Ambient`: the `CanvasModulate` colour. Warm (0.94, 0.88, 0.80), average 0.873, in place of
  the 0.95 grey. The HUD and the golem badges are on their own `CanvasLayer`s and are not tinted.
- `Sconce`: colour, energy, radius, and the number of bands. Today's values (colour (1, 0.72,
  0.42), energy 1.1, radius 5.5 cells) are the start.
- `BoilerGlow(fuelLeft, load)`: off with no fuel; on, energy rising with the boiler's load, so a
  busy boiler glows brighter. A boiler that goes out goes dark at once, which reads as the
  Starved blue's cause.
- `PipeHot(carries)`: whether a pipe piece shows its hot variant: exactly when it carries steam
  (`SteamNetwork.CarriesSteam`), joined to a boiler that has Coke. Not the guide's "steam reaches
  here", which ignores Coke: a boiler run dry goes dark and its pipes go cold with it (the
  user's call, Â§6.5).
- `FloorReadable`: the floor and overlay numbers `LightingRulesTests` checks (§6.4), kept here so
  a retune moves them together.

No lighting number is written in a node. The playtest's answer to `light` moves `LightingRules`,
and the tests say whether the new values are allowed.

### 6.2 How it's drawn

- **Warm tint:** the existing `CanvasModulate`, coloured from `LightingRules.Ambient`.
- **Sconce pools:** the existing seven lights, with the radial gradient replaced by a
  **banded** one (four or five flat steps), so the pool reads as pixel art instead of a smooth
  airbrush, and its energy and radius from the rules. The lamp wall's painted flame gets an
  unshaded material so it stays bright under the tint.
- **Boilers:** one small `PointLight2D` per boiler at its firebox (radius about 1.5 cells, the
  fire ramp), driven by `BoilerGlow`. The endgame has 12 boilers, so 19 lights in all.
- **Steam:** no light per pipe. Each pipe shape gets a hot variant (`steam_pipe*_hot.png`,
  five files in S3) with a few bright rivets and seams, drawn with an unshaded
  `CanvasItemMaterial` so it glows through the tint. `BuildingsLayer` swaps the texture by
  `PipeHot`, which costs no node. The steam puffs and the Overclocker's glow are unshaded too.

### 6.3 What it may cost

The budget is `performance-report-template.md` §1: windowed p95 ≤ 16.6 ms and worst ≤ 33.3 ms
in every phase. On an idle machine at 7a79046 the worst p95 was 14.6 ms (`hover`), so the
lighting has about 2 ms. **Sub-budget: lighting adds at most 1.0 ms of render CPU p95 at the
endgame load.** Whether lights cost per item in this renderer is measured, not assumed.

If it doesn't fit, cut in this order: boiler glows beyond the eight nearest the camera, then the
banded texture's size, then boiler glows altogether (the hot pipes stay, as they cost nothing).

### 6.4 What covers it

- `LightingRulesTests`:
  - the ambient is warm (red ≥ green ≥ blue) and **not darker than 0.75 in any channel**;
  - **the floor stays readable**: the darkest and the lightest floor-plank tones
    (`Tools/Art/palette.json`'s floor ramp, read by the test) multiplied by the ambient, with
    no sconce light, have OKLab lightness at least that of today's floor under today's 0.95 grey
    less 0.03; the source and target tile tints (`GolemTileOverlay`'s teal and gold, at their
    alpha) composited over each of them differ from the floor by at least 0.10 in OKLab; and the
    Stalled, Starved and Waiting badge colours over each differ from it by at least 0.15;
  - a boiler with no fuel has no glow; glow rises with load; a pipe is hot exactly when it
    carries steam (`SteamNetworkTests`: a dry boiler's pipes go cold, a pipe two boilers share
    stays hot while either has Coke).
- `check_art.py` check 10 multiplies the golem tint check by `LightingRules.Ambient`, so a
  darker room fails the golems before it ships.
- The `world` scenario: the `CanvasModulate` colour is `LightingRules.Ambient`; still seven
  sconces; each boiler's light follows its state (a fuelled boiler glows, one run dry doesn't).
- The `perf` scenario (§7.3): an `unlit` phase measures the lighting's cost.
- The playtest's `light` question ("Is the room too dark?") answers whether the strength is
  right; a change it asks for goes into `LightingRules` and must pass the tests above.

### 6.5 As built, and where it differs

- **The values are the plan's:** ambient (0.94, 0.88, 0.80); sconces (1, 0.72, 0.42), energy
  1.1, radius 5.5 cells, five bands stepping 1, 0.8 … 0.2 then nothing; boiler glow the fire
  ramp's #f0a040, radius 1.5 cells, energy 0.35 lit plus 0.08 per golem it keeps working (up to
  8, a boiler's share), and off with no Coke. The firebox's pixel is `BuildingView`'s
  `BoilerFireboxPixel`.
- **The Waiting badge is held to "no worse", not 0.15.** Its plate is the quiet grey on
  purpose, and over the lightest plank it was 0.05 apart under the old grey too; its white
  caption carries it. Stalled and Starved pass 0.15 (lowest 0.16).
- **The source and target tile tints moved to Core** (`GolemTileOverlay.SourceTint` and
  `TargetTint`), so the test reads the colours the game draws.
- **No unshaded flame.** `CanvasModulate` tints unshaded items too, so the material would not
  keep it bright; the flame sits inside its own lamp's pool, which lights it above the ambient.
- **The hot pipes** (2026-10-29, approved by the user): `steam_pipe*_hot.png`, five pictures
  `generate_building_art.py` draws from the cold pieces' geometry with `hot=True`: the brass
  pushed toward the fire ramp, a two-pixel highlight (#ffe08a over #f0a040) along the bore,
  glowing collars, rivets and hub seams, and the end piece's gauge needle swung over. Fire ramp
  only, no red. `BuildingView` swaps a pipe to `HotPipe(piece)` by `PipeHot` every frame (Coke
  running out reports no shape change); the `golems` scenario drains a spare boiler and checks
  its run goes cold while every pipe matches the rule, and a loose pipe stays cold. **No
  unshaded material**: as with the flame, `CanvasModulate` would tint it anyway, so the pixels
  carry the glow.
- **A pipe is hot only while a fuelled boiler feeds it** (the user's call, at the S3 hot-pipe
  review). The plan read "where steam reaches", the guide's answer, which ignores Coke; a dark
  boiler beside glowing pipes said the opposite of its firebox. `SteamNetwork.CarriesSteam`
  answers it.
- **`check_art.py` check 10** now multiplies by the new ambient (it reads `LightingRules.cs`).

## 7. Scenarios and tests

### 7.1 A new `art` scenario (Sandbox)

1. **Every manifest entry loads** through `ResourceLoader` as a `Texture2D` of the manifest size,
   and every `res://art/**.png` has an entry. Every sprite named in `data/*.json` and every name
   the code builds (`ItemIcons.PathFor` for every `ItemType`, `ClockTowerArt.Stage(0..4)`, the
   walk frames, every `GolemFrames.FileName` for every chassis, state and facing) exists, or,
   while a chassis' set hasn't landed, resolves through `GolemFrames.Resolve` to one that does.
2. **Pivots, placed for real:** every standing sprite the shell, the buildings layer and the
   golems place, its lowest opaque row (read from the texture's `Image`) lands on its node's
   origin, within its shadow rows, in every facing. The `world` scenario checks y-sort at the
   feet; this checks the art's foot row against the code's pivot.
3. **The gallery**, captured with `--capture-dir`: a layout built in Core
   (`Diagnostics/ArtGallery`, beside `EndgameLoad`) and drawn by the normal nodes. `ArtGallery`
   can pin a golem's frame key (a diagnostic seam on the golem's view, null in play), so every
   frame is shown without waiting for the moment it plays.
   - `art-golems-<chassis>.png`: one chassis, every frame in every facing (28), then in every
     mood (tint and badge), carried, and hovered with its tiles.
   - `art-artificer.png`: the 16 walk frames.
   - `art-buildings.png`: every placeable, a belt with every good on it, pipes cold and hot.
   - `art-goods.png`: every good at 32 on a belt and at 24 in an inventory row.
   - `art-room-lit.png` and `art-room-unlit.png`: the room, lights on and off.
   - `art-workbench.png`: the Workbench with every card kind in the vault.
   - `art-hud.png`: the HUD, the build menu, Management, the Pack screen and the guide panel.

   **Every capture is written at the default, closest and widest zooms** (`-default`, `-close`,
   `-wide`; for the screens, which don't zoom, once). These are the review sheet's in-game
   columns (§5.8).
4. **Readability in the game**: for each chassis and facing in the gallery, the drawn body's
   mean colour under Stalled, Starved and Waiting (read back from the viewport) differs pairwise
   by the style bible's margins, with the ambient applied.

**Built (2026-10-23), the part S1 needs:** items 1 and 2 as written (the data's names are every
`"*.png"` string in `data/*.json`), and item 3's golem gallery: `Diagnostics/ArtGallery` stands
each chassis's 28 frames in a 7 × 4 grid in the room (columns are states, rows facings, two cells
apart, clear of the bench and the station), pins them, and captures
`art-golems-<chassis>-{default,close,wide}.png`. Still to come, each with the set that needs it:
the gallery's mood, carried and hovered rows (S1, once frames differ), `art-artificer` (S1b),
`art-buildings`, `art-goods`, `art-room-lit/unlit`, `art-workbench`, `art-hud`, and item 4's
in-game readability (Lighting, since it reads the ambient).

### 7.2 Extended

- `golem-motion`: for a chassis with work frames, the impact frame is showing on the stroke's
  impact (the scenario already samples `StampImpact`), in the golem's facing, and the transform
  motion still passes as it does today.
- `golems`: each chassis built in each facing shows that facing's base frame.
- `moods`, `hover`: unchanged checks, rerun on the new art, and their `critique-*` captures
  viewed.
- `interact`: a carried golem shows its carry frame, and turning it with R changes its facing
  frame.
- `workbench`: the chassis pane shows the south base; when S4 changes the layout, the hint's
  frames still sit on the controls they name.
- `world`: the lighting checks (§6.4).

### 7.3 `perf`

A sixth phase, `unlit`: the `flowing-1x` load with `ShellNode`'s lights and the boiler glows off.
Its render CPU p95 against `flowing-1x`'s is the lighting's cost, a new row in the report's
§6.4. Every lit phase must still pass with all 28 frames per chassis in use. The run is recorded
in `docs/performance-report-phase4.md`.

### 7.4 Core tests

- `ArtManifestTests` (no engine): reads `Tools/Art/art_manifest.json` and each PNG's size from its
  IHDR header. Every sprite named in `data/*.json` is in the manifest; every chassis frame is
  ≤ 64 × 96 and a chassis' 28 files are one size; the tower's entries match
  `ClockTowerArt.CanvasWidth`/`CanvasHeight`; there are
  `ArtificerWalkAnimation.DirectionCount × FramesPerDirection` walk frames of one size; every
  `ItemType` has an icon entry.
- `GolemFramesTests`:
  - the frame for each mood, motion, stroke phase, carried state and **facing**;
  - the idle pulse's frame 0 is the base key;
  - the work frame follows step progress, not time (pausing the clock holds it);
  - `FileName`: the south base is `<stem>.png`, every other key `<stem>_<state>[_<n>]_<facing>.png`;
  - `Resolve`, with a set of existing names: the frame → the same frame facing south → the base
    in the asked facing → the south base → `golem_generic_copper.png`, each step taken only when
    the one before is missing.
- `LightingRulesTests` (§6.4).

### 7.5 Which checks each set runs

| Set | Python | Core | Scenarios |
|---|---|---|---|
| Cleanup | verify | — | every scenario, before and after; the screen captures (§3.1) |
| S1 Golems | verify, check (8–10, 13, 14) | `ArtManifestTests`, `GolemFramesTests` | `art`, `golem-motion`, `golems`, `moods`, `hover`, `interact`, `workbench`, `perf` |
| S1b Artificer | check (3, 13, 14, size) | `ArtManifestTests` | `art`, `world`, `interact` |
| S2 Floor and walls | verify, check | `SandboxLayoutTests` | `art`, `world`, `hover`, `management` (Extends) |
| Lighting | check (10, with the ambient) | `LightingRulesTests` | `world`, `art`, `hover`, `perf` |
| S3 Buildings | verify, check | `ArtManifestTests` | `art`, `build`, `world`, `perf` |
| S4 Workbench | verify, check (nine-slice) | — | `workbench`, `tutorial`, `art` |
| S5 Goods | verify, check (11, 12) | `ArtManifestTests` | `art`, `inventory`, `management` |
| S6 HUD | check (16), verify | — | `management`, `inventory`, `build`, `interact`, `workbench`, `tutorial`, `playtest-kit`, `font-glyphs`, `art` |

## 8. Done when

Phase 4 is done when all of these hold:

1. **The cleanup commit is in** (§3.1): the 26 unused files gone, every scenario passing and
   every screen capture unchanged across it.
2. **Every set in §3 is replaced through §2.** Nothing is deferred without the user's OK and a
   reason recorded in §4; the golems (S1) and the Artificer (S1b) are not deferrable.
3. **The golems have the full frame set in every facing:** every chassis has its 28 files
   (8 frame slots, 7 distinct files, × 4 facings), **140 for the five, 105 drawn and 35
   mirrored and re-shaded**, one canvas and foot row per chassis, chosen by `GolemFrames` with
   facing as an input. The Artificer's 16 walk frames are redrawn, 4 of them mirrored and
   re-shaded.
4. **The Steampunk pack is gone:** `godot/art/UI/Steampunk/` and
   `docs/source-assets/SteampunkFantasyUIPack/` are removed, the seven HUD pieces in use are
   redrawn under `UI/Hud/` with manifest entries, and no doc that describes the current tree
   names the pack as present.
5. **Every set's review sheet was shown to the user before its commit**, at the default, closest
   and widest zooms, and every frame the user marked was redone, fixed by script or by the
   user's hand and shown again. The sheets and their `review.json` are in
   `public/images/<set>/`.
6. **The checks are green:** `verify_art.py` exits 0 and covers every generator (the §1.3 blind
   spot fixed); `check_art.py` exits 0 over the whole manifest with every replaced set at
   `palette: bible`, including the per-facing silhouette, pose, tint-shift, light-direction,
   frame-set, goods (at 24, all four phases) and copper-ore checks; the tools' unit tests pass.
7. **The lighting is in**, every value from `LightingRules`, `LightingRulesTests` passing
   (the floor stays readable), and the **windowed `perf` run passes on an idle machine with it
   on**, its cost recorded in `docs/performance-report-phase4.md`.
8. **Every Core test and every scenario passes**, the new `art` scenario included, each scenario
   run twice headless from a clean build with no flakes.
9. **The record is complete:** every hand-replaced file is in its generator's guard,
   `KNOWN_HAND_REPLACED`, the manifest and `CLAUDE.md`'s art section; every `comfyui` file is in
   the manifest; and no RetroDiffusion or third-party art is left in `godot/art/`.

## 9. Decisions (all made by the user, 2026-10-23)

Each records the decision and, where it differs, what this document first recommended.

1. **The palette** (`art-style-bible.md` §1). **Approved as proposed.** The brass highlight
   `#e2c27a` and the fire ramp are checked on the first chassis's review sheet. (As
   recommended.)
2. **How many golem frames.** **The full set for every chassis, nothing staged or cut:** base,
   two idle pulse, three signature work poses, stall, carry; 8 frame slots per facing, 7
   distinct files (the idle pulse's first frame is the base). *First recommended:* stage it
   (base, stall and carry for all five first, then the work poses, then the idle pulse, the
   first to cut).
3. **Facing frames.** **Yes, full facing**: four facings per frame, three drawn (south, north,
   east) and west made by mirroring east and re-shading it by tool so the light still comes
   from the upper left, then the review sheet. 28 files per chassis, **140 for the five (105
   drawn, 35 mirrored)**; the "35 × 4" quoted with the decision counts the same files.
   `GolemFrames.FrameFor` takes the facing, a missing facing falls back to south, then to the
   base (`art-style-bible.md` §4). *First recommended:* no facing frames; the arrow says it, and
   Phase 6 could mirror east and west for free.
4. **Who does the hand cleanup.** **Tool-only everywhere.** Every set gets a before/after review
   sheet at the default, closest and widest zooms, shown to the user before commit; the user
   marks frames that look wrong, and only those get a ComfyUI redo, a scripted fix, or the
   user's hand on that one frame (§2, steps 7 and 10). *First recommended:* the user's hand on
   the golems, the Artificer and the Workbench centrepieces, tool-only elsewhere.
5. **The Artificer.** **Added as set S1b, right after the golems** (16 walk frames). (As
   recommended.)
6. **Inventory icon size.** **The rows stay at 24 pixels.** Goods are drawn at 32; each must
   still read at 24 (checked at all four sampling phases), and the 0.75 scale's uneven pixel
   drop is a known, accepted unevenness (`art-style-bible.md` §5.4). *First recommended:* draw
   the rows at 32.
7. **Pixel-exact zoom.** **Zoom stays continuous for Phase 4**; art is judged at the default,
   closest and widest zooms; snapping is Phase 6's. (As recommended.)
8. **Lighting strength.** **Start subtle**: a warm ambient averaging about 0.88, today's sconce
   energy, banded pools; every value in Core's `LightingRules`; the playtest's `light` question
   tunes it; a test holds the floor no darker than the mood tints and tile overlays read on
   (§6.4). (As recommended, with the test made explicit.)
9. **The Steampunk UI pack.** **Dropped.** The HUD set (S6) is redrawn through ComfyUI like
   everything else; each of the seven pieces in use gets a pipeline entry and a new name under
   `UI/Hud/`; the pack's files, and its licence note, are removed when S6 lands (§4, S6).
   *First recommended:* keep the pieces in use for Phase 4.
10. **The order.** **Golems; the Artificer; floor and walls; lighting; buildings; the Workbench;
    goods; the HUD** (§3). (As recommended.)
11. **The unused sprites.** **Deleted in one cleanup commit before S1**, after a full scenario
    run and a capture of each screen (§3.1). (As recommended, with the screen captures added.)
12. **ControlNet and IP-Adapter.** **Installed before the golems** (being done separately, in
    `G:/AI/ComfyUI`). Per frame, a pose or lineart guide made from the cleaned base drives
    ControlNet, and IP-Adapter holds the character (§2, steps 3–4); `comfy_redraw.py` takes both
    (§5.4). The 6 GB of VRAM limits the models and the batch (§1.4). *First recommended:* don't
    install one yet.
13. **Golems are creatures** (the user, 2026-10-23, at the first Brass Presser review sheet).
    The first Presser was drawn as a stamping press, a machine with no one in it; the user
    rejected it: "It could look similar but with some arms and legs and a head. The cozy factory
    vibe needs it." Every golem has a head with a face or eye, arms and legs; its chassis motif is
    worn or wielded, never the whole body (`art-style-bible.md` invariant 17, §5.1). The same
    review asked for true side profiles, a clean back view, texture that holds still across
    frames, and more detail where the light check allows.
14. **The mirror's light check reads tones as well as lightness** (the user, 2026-10-26, at the
    Artificer's walk review). Walking right, the Artificer's lit edge is the pale shirt; mirrored
    to walk left, it is the darker leather apron, so `artificer_walk_left_1` scored 0.69 × its
    source's edge light, under the 0.8 bound, while every material was lit from the left. A
    mirrored frame now passes on the edge-light score or on the edge-tone score, which counts each
    edge pixel as its tone's place in its own ramp (`art-style-bible.md` §5.1; check 14,
    `reshade_mirror.py` step 5). Tone alone was tried first and failed four golem mirrors that
    pass on lightness, so either measure holding is enough; a plain flip still fails both.
15. **The Artificer keeps today's sprite** (the user, 2026-10-26). A redraw through
    `golem_set.py` (today's head traced in, the body drafted and redrawn in ComfyUI) went
    through five review rounds and was installed (2511da8), then the user judged it "worse
    overall" than the original, and the original 16 frames were restored. Adjustments to the
    Artificer, and its cranking frames, are planned with the user and made from today's own
    pixels, keeping the original's charm. *First recommended:* the redraw (S1b in §3).
16. **The Artificer's adjustments, from its own pixels** (the user, 2026-10-27, each shown and
    approved): stray shirt-green specks cleaned; the side views share one head (walking
    right's frame 2, mirrored for walking left), its eye redrawn to match the front's; a
    near-black plum outline two pixels thick, chosen over a value-and-colour boost; the
    front and back walks' frames 0 and 2 made passing poses (a foot lifted 3 px), with a
    separate standing pose per direction so standing never shows a lifted foot; and the
    stride raised from 0.35 to 0.45 cells, about nine frames a second instead of eleven. The
    side walks keep standing in-between frames: their legs are one block there, and lifting a
    foot would mean drawing new legs. The scripts are in `public/images/S1b/artificer/scripts/`;
    `Tools/Art/CLAUDE.md` gives their order. *First recommended:* as done, the outline
    over the boost.
17. **Cranking moves the Artificer's own pixels, and the bench's wheel glints** (the user,
    2026-10-27). While [E] turns a Hand-Crank Bench he faces it and his standing pose rocks
    (upper body round a 2 px loop, the head nodding a pixel more); the bench's brass rim
    carries two glints round 45 degrees a frame. Both advance one frame per simulation tick
    that made progress (`HandCrankBench.TurnFrame`), so they turn with the work at any speed
    and stop when it stops; an unaffordable recipe stands still (the user's call). Walking
    while [E] is held shows the walk. *First recommended:* drawn cranking arms; a scripted
    arm looked "like a noodle floating on top of the sprite" (the user), the sprite's own arm
    rotated fell apart, and turning the wheel's pixels dragged the tabletop into its spokes.
18. **S2 is closed with today's floor, walls and props unchanged** (the user, 2026-10-28). A
    pilot of three files (`floor_tile`, `wall_segment_nw`, `prop_crate`) was redrawn in
    ComfyUI from today's art by `env_set.py`, nine candidates each, then the floor and wall
    again lighter (denoise 0.15-0.3, objects in the negative prompt). The floor never worked:
    even at 0.15 the model drew marks, brackets or machines into the boards, and a 64 px tile
    repeats each one across the room. The wall at 0.15 (#3) was clean, and crate #3 (0.35) was
    the user's pick, but in the game, drawn at about half size, the redraw's detail vanished:
    the crate read paler and flatter than today's, with its dark corner fittings mostly lost,
    and the wall would differ little. So nothing was installed and the other 19 files were not
    started; the effort goes to lighting (L) next. A wall redraw would also have to remake
    `wall_segment_nw_lamp` (today's sconce laid on the new wall, which joins cleanly). If the
    props should stand out more later, pixel edits to today's sprites (outlines, top-edge
    highlights) show at play size where a redraw's texture doesn't. *First recommended:* the
    pilot, then the other 19 files the same way.
