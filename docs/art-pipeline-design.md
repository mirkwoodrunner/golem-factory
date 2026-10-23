# Art pipeline: Phase 4

Written 2026-10-22 for Phase 4 (`docs/roadmap.md`, "Phase 4: Art"), on branch `phase4/art`. It
says how each set of art is replaced, in what order, which files each set holds and what reads
them, the tooling that makes cleanup and checks repeatable, the lighting, the scenarios that pin
it all, and when the phase is done. **What the art looks like** is `docs/art-style-bible.md`;
its §0 invariants hold for every step here.

## 1. Where things stand (measured 2026-10-22, at 8b3ec0b)

### 1.1 The art

155 PNGs under `godot/art/`. By owner:

| Owner | Files | Checked by `verify_art.py` |
|---|---|---|
| Generated, `generate_topdown_environment.py` | 31 (floor, street, walls, props, belt tiles, overlays) | Yes, byte-identical |
| Generated, `generate_building_art.py` | 11 (depot, station, Post, mast, recycler, slag heap, five pipe pieces) | Yes |
| Generated, `generate_workbench_ui_art.py` | 10 (`UI/Workbench/wb_*`) | Yes |
| Generated, `generate_tech_tree_art.py` | 13 (`UI/TechTree/tt_*`) | Yes |
| Generated, `generate_placeholder_art.py` | 27 (25 goods, `belt_lane`, `belt_arrow`, `belt_roller`) | **No: see §1.3** |
| Hand-replaced, ComfyUI plus cleanup | 11 (six Clock Tower, five market carts) | Listed as hand-replaced |
| ComfyUI, never generated | 1 (`fx_steam_puff.png`) | Listed as "not generated" |
| AI-authored before decision 3 (RetroDiffusion) | 22 (five chassis, sixteen Artificer walk frames, `player.png`) | Listed as "not generated" |
| Authored, origin not recorded | 8 (`steam_boiler`, `hand_crank_bench`, `golem_generic_*` ×3, `building_block`, `ghost_placeholder`, `facing_arrow`) | Listed as "not generated" |
| Third-party, Steampunk & Fantasy UI Pack (Scott Jenkins, free use; `docs/source-assets/`) | 21 (`UI/Steampunk/*`) | Listed as "not generated" |

`verify_art.py` today: 76 generated, 65 byte-identical, 11 hand-replaced, 0 drifted, 79
"committed but not generated".

**Apparently unused** (no script, data file or scene names them, by string search): `player.png`,
`golem_generic_brass`, `golem_generic_steel`, `building_block`, `belt_lane`, `belt_arrow`,
`belt_roller`, `floor_edge_se`, `wall_segment_ne`, `wall_segment_ne_lamp`, `tt_line_h`,
`tt_line_v`, and 13 of the 21 Steampunk pieces (only eight are named:
`steampunk_panel_iron_bolt`, `steampunk_button_blank`, `steampunk_button_blank_iron`,
`iron_panel_nobolt`, `gauge`, `gear_large`, `steampunk_button_accept` and
`steampunk_button_exit`). None of these is redrawn. Whether to delete
them is decision 11.

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
- **The boiler and the bench** are drawn in the chassis' harsher palette (purple outlines,
  saturated copper) and stand out from the carts beside them.
- **Goods:** Brass and Copper Ingot have the same silhouette (overlap 1.00); only their colour
  differs.
- **The Workbench** is placeholder throughout (`open-items.md` §4): the lever is an iron panel
  with a track and a handle, the cards are one 32 × 32 9-slice tinted per kind.

### 1.3 A blind spot in `verify_art.py`

`generate_placeholder_art.py`'s `main` treats every argument that doesn't start with `--` as a
section name. `verify_art.py` calls it with `--out-root <dir>`, so `<dir>` is taken as a
section, no section matches, and **the script writes nothing**. Its 27 files are reported as
"not generated" and never compared. Run by hand with `belts --out-root <dir>` and
`goods --out-root <dir>`, all 27 reproduce byte for byte (2026-10-22). Its other sections
(`environment`, `interaction`, `routing`) raise on purpose, because the top-down generator owns
those names (`TOP_DOWN_OWNED`), so a bare run of the script fails too.

This matters to Phase 4 because the goods are a replacement set and copper ore's colour is an
invariant (`art-style-bible.md` §0.6). The fix is the first tooling task (§5.5).

### 1.4 ComfyUI on this machine

**Installed, not running.** `G:/AI/ComfyUI/ComfyUI_windows_portable` (the portable build), started
by `G:/AI/start_comfyui.bat`, tuned for an RTX 2060 (6 GB) with fp8 weights: the first image
after a start takes about 7 minutes while SDXL loads from the HDD, then about 1.4 s a step.

- Models: `sd_xl_base_1.0`, `sdxl-vae-fp16-fix`, LoRAs `lcm-lora-sdxl` and `pixel-art-xl`.
- No ControlNet, IP-Adapter or upscale models, and no custom nodes.
- `G:/AI/comfy_generate.py` drives it from a shell: text to image only (SDXL + LCM 8 steps +
  Pixel Art XL at 1.2, 1024 px, reduced 8× with nearest-neighbour to a 128 px grid). It is
  outside the repo.
- Image to image and inpainting need only core nodes (`LoadImage`, `VAEEncode`,
  `VAEEncodeForInpaint`, a KSampler below denoise 1), so a redraw from the current sprite needs
  no install. The tower's build stages used inpainting.

Whoever starts ComfyUI stops it when the set that needed it is done.

### 1.5 The lighting today

`ShellNode` already lights the room: a `CanvasModulate` at a neutral 0.95 grey, and seven
`PointLight2D` sconces on the north wall (one every fourth segment, pinned by the `world`
scenario), colour (1, 0.72, 0.42), energy 1.1, a 5.5-cell radius, a smooth radial gradient, no
shadows. The renderer is GL Compatibility. Nothing else emits light: the boilers, the hearth and
the steam are unlit. The playtest already asks "Look around the workshop. Is the room too dark?"
(`PlaytestSession`, question `light`).

## 2. The per-set workflow

Every set goes through these steps, in order. The Clock Tower and the carts went this way;
this writes it down so each set is done the same.

1. **Brief.** Fill the set's row in §4: what each file must show (from `digital-design.md`'s
   roster or the building's role), its palette ramps (`art-style-bible.md` §1), its canvas,
   pivot and frames, and what code reads it.
2. **Source.** Either the current sprite, enlarged 8× with nearest-neighbour, or a hand-drawn
   draft (the carts' route, which held their silhouettes best). Sources go in
   `public/images/<set>/source/`.
3. **ComfyUI redraw.** Image to image from the source (`Tools/Art/comfy_redraw.py`, §5.4), a
   denoise sweep (0.35, 0.5, 0.65) over three or four seeds, at 8× the target canvas. Raw
   results go in `public/images/<set>/raw/`, never `godot/art/`.
4. **Pick.** A comparison sheet (`public/images/<set>/comparison.png`: the current sprite beside
   each candidate, at 1× and 4×, standing on a floor tile under the room's light). The user picks
   (decision 4 says who). The carts and the tower kept their sheets the same way.
5. **Reduce.** 8× to 1× by nearest-neighbour or k-centroid (`palette_snap.py --reduce 8`),
   never bilinear.
6. **Hand cleanup.** In a pixel editor: the outline, eyes at 2 × 2, the foot row, stray
   pixels, symmetry where the body is symmetric, and the softening the carts got (near-black ink
   to warm brown, contrast eased). Decision 4 says who does it.
7. **Palette snap and alpha.** `palette_snap.py` (§5.3): every opaque pixel to the nearest
   colour of the set's ramps in OKLab, alpha to 0 or 255 outside the allowed shadow rows, any
   black or purple-black outline pixel to the outline ramp. It reports how many pixels moved;
   more than 10% means the cleanup was off-palette and goes back to step 6.
8. **Canvas and pivot.** Placed on the manifest's canvas with the contact line on the pivot row.
   `check_art.py <file>` (§5.2) must pass.
9. **Frames.** Extra frames are edited from the cleaned base frame, never generated
   separately, so the body doesn't drift between them (the Artificer's frames share one palette
   and one foot row for the same reason, `slice_artificer_walk.py`).
10. **Commit the art.** Into `godot/art/` under the same name (`art-style-bible.md` §0.5), with
    its `.import`. New frame files get names the manifest and the code agree on.
11. **Bookkeeping**, in the same commit:
    - the generator's `HAND_REPLACED` set (add one to a generator that has none, copying
      `generate_topdown_environment.py`'s `_save` guard: it skips the name only when writing to
      `godot/art/`, so `--out-root` still renders the original);
    - `verify_art.py`'s `KNOWN_HAND_REPLACED`;
    - the manifest entry's `owner` to `hand-replaced`, with its `source` folder;
    - one line in `CLAUDE.md`'s "AI image generation" exceptions (the tower and carts are the
      model).
12. **Verify.** `verify_art.py`, `check_art.py`, `python -m unittest discover Tools/Art/tests`,
    `dotnet test`, the `art` scenario and the set's own scenarios (§7.4), and `perf` when the set
    adds nodes, frames or lights. Look at the captures: a sprite that passes every check can
    still look wrong.
13. **Review sheet.** `public/images/<set>/ingame.png`, the `art` scenario's capture of the set
    in the room, as `market-carts-ingame.png` was.

One commit per set, or per chassis inside the golem set.

## 3. Replacement order

The roadmap's order, by screen time, with the two additions this survey found. Decision 10 asks
whether to keep it.

| # | Set | Files | Why here |
|---|---|---|---|
| S1 | **Golems** | 5 chassis, their frames (`art-style-bible.md` §4), the fallback `golem_generic_copper` | Most watched, weakest today, and the moods depend on them |
| S1b | **The Artificer** (not in the roadmap's list) | 16 walk frames | Always on screen, and the other RetroDiffusion art. Decision 5 |
| S2 | **Floor and walls** | Floor and street tiles, edges, walls, posts, props, `ground_shadow` | The ground everything is judged against; the lighting is tuned on it |
| L | **Lighting** (§6) | No art files; `LightingRules`, `ShellNode`, boiler glow, hot pipe variants | Right after S2, so later sets are reviewed under the final light |
| S3 | **Buildings** | Depot, station, Post, mast, recycler, slag heap, boiler, bench, belt tiles, pipe pieces (+ hot variants) | Carts and tower are done |
| S4 | **The Workbench** | Lever housing, display face, card faces, panels | The core verb's screen |
| S5 | **Goods** | 25 `item_*` | Small, generator-owned, cheap; copper ore stays green |
| S6 | **The HUD** | The Steampunk pieces in use, the tech tree chrome, overlays | Last: mostly flat style boxes today |

Overlays (`build_ghost_tile`, `facing_arrow`, `interaction_ring`) are state tools, not
decoration. They stay generated and are only re-coloured to the palette if the review asks.

## 4. Inventory, by set

"Reads" is the code or data that loads the file or depends on its size or pivot. Sizes are
pixels. "Gen" names the generator (`topdown` = `generate_topdown_environment.py`, `building`,
`placeholder`, `workbench_ui`, `tech_tree`, `clock_tower`).

### S1 Golems

| File | Size | Owner | Frames | Reads |
|---|---|---|---|---|
| `chassis_clockwork_scavenger.png` | 64 × 96 | RetroDiffusion | 1 | `data/chassis.json` `chassisSprite`; `GolemNode.ApplyChassisSprite` (`StandOnCell`); `WorkbenchScreen` (chassis pane, enlarged) |
| `chassis_brass_presser.png` | 64 × 90 | RetroDiffusion | 1 | same |
| `chassis_aether_hauler.png` | 64 × 86 | RetroDiffusion | 1 | same |
| `chassis_mainspring_overclocker.png` | 64 × 96 | RetroDiffusion | 1 | same; `GolemNode.GlowTint` multiplies it |
| `chassis_zeppelin_freight_loader.png` | 64 × 93 | RetroDiffusion | 1 | same |
| `golem_generic_copper.png` | 64 × 92 | authored (legacy placeholder) | 1 | `GolemNode.FallbackSprite` |
| `fx_steam_puff.png` | 24 × 20 | ComfyUI | 1 | `GolemNode.PuffSprite`, scaled by `GolemWorkMotion.Puff` |
| `ground_shadow.png` | 48 × 36 | Gen topdown | 1 | `SpritePivots` (0.5, 0.5); under golems and props |

Code limits: ≤ 64 × 96 (`BadgeLiftCells` 1.75; hover covers two cells); feet on the bottom row.
New frame files: `chassis_<name>_<state>_<n>.png`, chosen by `GolemFrames` (§7.2).

### S1b The Artificer

| File | Size | Owner | Frames | Reads |
|---|---|---|---|---|
| `artificer_walk_{down,left,right,up}_{0..3}.png` (16) | 64 × 93 each | RetroDiffusion concept sheet, sliced by `slice_artificer_walk.py` | 4 × 4 | `PlayerNode` builds the names; `ArtificerWalkAnimation` (4 directions, 4 frames, stride 0.35 cells) |
| `player.png` | 96 × 143 | RetroDiffusion | 1 | unused |

### S2 Floor and walls

| File | Size | Owner | Reads |
|---|---|---|---|
| `floor_tile.png`, `floor_tile_wood_b/c/d.png` | 64 × 64 | Gen topdown | `FloorLayer` by name; seamless |
| `floor_tile_accent.png`, `floor_tile_grate.png` | 64 × 64 | Gen topdown | `FloorLayer` |
| `street_cobble.png`, `street_cobble_b.png` | 64 × 64 | Gen topdown | `FloorLayer` |
| `street_edge.png` | 64 × 24 | Gen topdown | `SpritePivots` (0.5, 1) |
| `floor_edge_sw.png` | 64 × 24 | Gen topdown | `SandboxLayout`, `SpritePivots` (0.5, 1) |
| `floor_edge_se.png` | 64 × 24 | Gen topdown | unused |
| `wall_segment_nw.png`, `wall_segment_nw_lamp.png` | 64 × 96 | Gen topdown | `SandboxLayout` (lamp on every fourth), `SpritePivots` (0.5, 3/96) |
| `wall_segment_ne.png`, `wall_segment_ne_lamp.png` | 64 × 96 | Gen topdown | unused |
| `wall_side_e.png`, `wall_side_w.png` | 40 × 64 | Gen topdown | `SpritePivots` (3/40 or 37/40, 0.5) |
| `wall_corner_post.png` | 50 × 96 | Gen topdown | `SpritePivots` (0.5, 1/96) |
| `prop_barrel.png` | 40 × 56 | Gen topdown | `SandboxLayout.Props`, `SpritePivots` (0.5, 1/56) |
| `prop_crate.png`, `prop_hearth.png`, `prop_workbench.png` | 56 × 56 | Gen topdown | same |
| `prop_shelf.png` | 52 × 56 | Gen topdown | same |
| `prop_tool_rack.png` | 48 × 56 | Gen topdown | same |

The lamp walls' painted sconce sits where `ShellNode` puts the light
(`LampLightInsetCells` −0.25 below the wall's anchor); a redraw keeps the flame in the same column.

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
| `belt_tile.png`, `belt_tile_corner_left.png`, `belt_tile_corner_right.png`, `belt_splitter.png` | 64 × 64 | Gen topdown | `placeables.json` shape sprites, `BeltRun`, `BuildingsLayer` (centred, rotated) |
| `steam_pipe.png`, `_corner`, `_cross`, `_end`, `_tee` | 64 × 64 | Gen building | `BuildingsLayer` (centred, rotated); hot variants added by §6 |
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

### S5 Goods

| Files | Size | Owner | Reads |
|---|---|---|---|
| `item_scrap`, `item_coal`, `item_copper_ore`, `item_zinc_ore`, `item_aether`, `item_brass` | 32 × 32 | Gen placeholder (`belts` section) | `placeables.json` `itemSprites` (belts, drawn at 32); `ItemIcons.PathFor` (UI rows, drawn at 24); `BeltRun` default `item_scrap` |
| `item_coke`, `item_iron_plate`, `item_slag`, `item_glass`, `item_copper_ingot`, `item_zinc_ingot`, `item_copper_wire`, `item_gear`, `item_casing`, `item_lens`, `item_mainspring`, `item_aether_cell`, `item_mechanism`, `item_regulator`, `item_frame_section`, `item_aether_conduit`, `item_chronometer_core`, `item_great_cog` | 32 × 32 | Gen placeholder (`goods` section) | same |

`item_copper_ore.png` stays malachite green (`art-style-bible.md` §0.6). Since the goods are
generated, replacing them means either redrawing in the generator (it stays the owner, and
`verify_art.py` keeps checking it) or hand-replacing all 25 (decision 4 applies).

### S6 The HUD

| Files | Size | Owner | Reads |
|---|---|---|---|
| `UI/Steampunk/steampunk_panel_iron_bolt.png` | 64 × 64 | third-party | `HudOverlay`, `BuildMenuNode`, `TutorialPanel`, `PlaytestNode`; 9-slice 12/10/12/10 |
| `steampunk_button_blank.png`, `_blank_iron.png` | 34 × 34 | third-party | `BuildMenuNode`, `AssemblyLineTab`, `ConstructionPanelNode`; 9-slice 8 |
| `iron_panel_nobolt.png` | 64 × 64 | third-party | `ConstructionPanelNode`, Management; 9-slice 8 |
| `gauge.png`, `gear_large.png`, the accept and exit buttons | 34 × 44, 58 × 59, 34 × 34 | third-party | Management and the panels |
| `UI/TechTree/tt_*` (13) | 16 × 16 to 32 × 32 | Gen tech_tree | `LedgerTab`; plaques and phase plate 9-slice 10 |
| `interaction_ring.png` | 64 × 64 | Gen topdown | `InteractionPromptNode` |
| `facing_arrow.png` | 28 × 28 | authored | `GolemNode` (and at 0.6 on the tiles), `BuildCursorNode`, `TutorialPanel` |
| `build_ghost_tile.png` | 64 × 64 | Gen topdown | `BuildCursorNode`, `GolemNode` (tile overlay, carry preview) |

## 5. Tooling to build

All of it in `Tools/Art/`, Python 3 with Pillow (already required), standard library otherwise.
Tests are `unittest` in `Tools/Art/tests/`, run by `python -m unittest discover Tools/Art/tests`,
on small synthetic images built in the test, so they need neither ComfyUI nor `godot/art/`.

### 5.1 The manifest: `Tools/Art/art_manifest.json`

One entry per PNG under `godot/art/`, keyed by its path relative to it:

```json
"chassis_brass_presser.png": {
  "set": "S1",
  "size": [64, 90],
  "pivot": "bottom-center",
  "footRow": 89,
  "owner": "retrodiffusion",
  "frames": 1,
  "palette": "legacy",
  "readers": ["data/chassis.json", "GolemNode.ApplyChassisSprite"]
}
```

- `owner`: `generated:<script>`, `hand-replaced`, `comfyui`, `retrodiffusion`, `authored`,
  `third-party`, or `unused`.
- `pivot`: `bottom-center`, `center`, or `[x, y]` in Unity's bottom-left fractions, exactly as
  `SpritePivots` writes them. `footRow` is derived and stored so a reader needn't compute it.
- `palette`: `bible` once the set is replaced (the palette and alpha rules apply), `legacy`
  until then (they don't), or a list of extra ramps.
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
8. **Silhouettes** (S1): pairwise alpha-mask overlap of the chassis ≤ 0.50, aligned at the
   feet.
9. **Tint shift** (S1): mean OKLab lightness ≥ 0.40; Stalled shift ≥ 0.10; the three stopped
   or waiting tints ≥ 0.06 apart. The tints are read from `GolemMoodPalette.cs` by a regex, so
   the check can't drift from the code, and multiplied by the room's ambient colour from
   `LightingRules` (§6) once that exists.
10. **Goods** (S5): at 24 × 24, pairwise mask overlap ≤ 0.80 or mean colour ≥ 0.15 apart.
11. **Copper ore** is green on both files: the most common opaque hue of `item_copper_ore.png`
    and of the ore pixels of `stall_copper_ore.png` falls in 100°–160°.
12. **Hand-replaced bookkeeping:** an entry with `owner: hand-replaced` appears in
    `verify_art.py`'s `KNOWN_HAND_REPLACED` and in `CLAUDE.md`; a `KNOWN_HAND_REPLACED` name has
    an entry that says so.

Checks 8–11 run on the current art too and report its baseline (§1.2) as warnings while the set
is `legacy`, failures once it is `bible`.

Tests (`tests/test_check_art.py`): one synthetic image per failure (a canvas a pixel too tall, a
floating foot row, a half-alpha pixel, an off-palette pixel, two identical silhouettes, a red
body, a copper ore turned orange, a missing manifest entry), each failing with its message, and a
clean image passing.

### 5.3 `Tools/Art/palette_snap.py`

`python Tools/Art/palette_snap.py <in.png> <out.png> --ramps outline,mahogany,brass
[--reduce 8] [--alpha 128] [--outline]`

1. `--reduce N`: N× to 1×, k-centroid per block (the block's most common colour after a
   two-means split), which keeps a one-pixel line that a nearest pick can drop.
2. Alpha to 0 or 255 at the threshold, except in `--shadow-rows`.
3. Each opaque pixel to the nearest colour of the named ramps, in OKLab.
4. `--outline`: an opaque pixel next to transparency whose OKLab lightness is under 0.2 becomes
   the outline ramp's nearest tone, which is the carts' black-to-brown softening as a tool.
5. Prints pixels changed per step, and fails if more than `--max-change` (default 10%) moved in
   step 3.

Tests (`tests/test_palette_snap.py`): a 2 × 2 block reduces to its majority colour; a
one-pixel line survives `--reduce 8`; a half-alpha pixel goes to 0 or 255; an off-palette pixel
lands on its nearest ramp colour and an on-palette pixel doesn't move; a black outline turns
warm brown; the shadow rows keep their alpha.

### 5.4 `Tools/Art/comfy_redraw.py`

Image to image against the local ComfyUI, standard library only, in the shape of
`G:/AI/comfy_generate.py`: `LoadImage` of the 8× source, `VAEEncode`, KSampler (LCM, 8 steps,
cfg 1.5) at `--denoise`, Pixel Art XL at 1.2, `VAEDecode`, save. `--seeds`, `--denoise 0.35,0.5,0.65`
for a sweep, `--out public/images/<set>/raw/`. It refuses an `--out` under `godot/art/`. No test
needs the server: the workflow it builds is a function, and a test asserts its nodes and that it
refuses `godot/art/`.

### 5.5 `verify_art.py` and the placeholder generator

- `generate_placeholder_art.py`: drop the value after `--out-root` from the section list, and
  make the default sections `belts` and `goods` (the only ones that can run without tripping
  `TOP_DOWN_OWNED`).
- `verify_art.py`: run it with those sections. The expected result after the fix is 103
  generated (92 identical, 11 hand-replaced), 52 not generated.
- Add a `HAND_REPLACED` guard to `generate_building_art.py`, `generate_placeholder_art.py` and
  `generate_workbench_ui_art.py`, the same shape as the top-down generator's, before their first
  file is replaced.
- Test (`tests/test_verify_art.py`): with `--out-root` at a temporary folder, the placeholder
  generator writes its 27 files there and nothing under `godot/art/`.

### 5.6 Commands this adds to `CLAUDE.md`

```sh
python Tools/Art/verify_art.py                 # generated art reproduces (exists)
python Tools/Art/check_art.py                  # the manifest: sizes, pivots, palette, readability
python -m unittest discover Tools/Art/tests    # the tools' own tests
```

## 6. Lighting

Three things the roadmap asks for: sconces that light the floor, a warm room tint, steam that
glows. All of it in the GL Compatibility renderer, with no normal maps (an AI redraw can't be
given consistent ones) and no light occluders (cost).

### 6.1 The rules in Core

`Core/World/LightingRules` (pure, engine-free, tested by `LightingRulesTests`), read by
`ShellNode` and `BuildingsLayer`:

- `Ambient`: the `CanvasModulate` colour. Proposed warm (0.94, 0.88, 0.80) for the 0.95 grey.
  The HUD and the golem badges are on their own `CanvasLayer`s and are not tinted.
- `Sconce`: colour, energy, radius (today's values the default).
- `BoilerGlow(fuelLeft, load)`: off with no fuel; on, energy rising with the boiler's load, so a
  busy boiler glows brighter. A boiler that goes out goes dark at once, which reads as the
  Starved blue's cause.
- `PipeHot(reached)`: whether a pipe piece shows its hot variant: the same answer the guide reads
  for "steam reaches here".

### 6.2 How it's drawn

- **Warm tint:** the existing `CanvasModulate`, coloured from `LightingRules.Ambient`.
- **Sconce pools:** the existing seven lights, with the radial gradient replaced by a
  **banded** one (four or five flat steps), so the pool reads as pixel art instead of a smooth
  airbrush, and its energy and radius from the rules. The lamp walls' painted flames get an
  unshaded material so they stay bright under the tint.
- **Boilers:** one small `PointLight2D` per boiler at its firebox (radius about 1.5 cells, the
  fire ramp), driven by `BoilerGlow`. The endgame has 12 boilers, so 19 lights in all.
- **Steam:** no light per pipe. Each pipe shape gets a hot variant (`steam_pipe*_hot.png`,
  five files in S3) with a few bright rivets and seams, drawn with an unshaded
  `CanvasItemMaterial` so it glows through the tint. `BuildingsLayer` swaps the texture by
  `PipeHot`, which costs no node. The steam puffs and the Overclocker's glow are unshaded too.

### 6.3 What it may cost

The budget is `performance-report-template.md` §1: windowed p95 ≤ 16.6 ms and worst ≤ 33.3 ms
in every phase. On an idle machine at 7a79046 the worst p95 was 14.6 ms (`hover`), so the
lighting has about 2 ms. **Proposed sub-budget: lighting adds at most 1.0 ms of render CPU p95
at the endgame load.** Whether lights cost per item in this renderer is measured, not assumed.

If it doesn't fit, cut in this order: boiler glows beyond the eight nearest the camera, then the
banded texture's size, then boiler glows altogether (the hot pipes stay, as they cost nothing).

### 6.4 What covers it

- `LightingRulesTests`: the ambient is warm and not darker than 0.75 in any channel (the tile
  overlay must read on it); a boiler with no fuel has no glow; glow rises with load; a pipe is hot
  exactly where steam reaches.
- The `world` scenario: the `CanvasModulate` colour is `LightingRules.Ambient`; still seven
  sconces; each boiler's light follows its state (a fuelled boiler glows, one run dry doesn't).
- The `perf` scenario (§7.3): an `unlit` phase measures the lighting's cost.
- The playtest's `light` question ("Is the room too dark?") answers whether the strength is
  right (decision 8).

## 7. Scenarios and tests

### 7.1 A new `art` scenario (Sandbox)

1. **Every manifest entry loads** through `ResourceLoader` as a `Texture2D` of the manifest size,
   and every `res://art/**.png` has an entry. Every sprite named in `data/*.json` and every name
   the code builds (`ItemIcons.PathFor` for every `ItemType`, `ClockTowerArt.Stage(0..4)`, the
   walk frames, `GolemFrames`' names) exists.
2. **Pivots, placed for real:** every standing sprite the shell, the buildings layer and the
   golems place, its lowest opaque row (read from the texture's `Image`) lands on its node's
   origin, within its shadow rows. The `world` scenario checks y-sort at the feet; this checks the
   art's foot row against the code's pivot.
3. **The gallery**, captured with `--capture-dir`: a layout built in Core
   (`Diagnostics/ArtGallery`, beside `EndgameLoad`) and drawn by the normal nodes.
   - `art-golems.png`: each chassis in every mood (tint and badge), working at its impact pose,
     carried, and hovered with its tiles.
   - `art-buildings.png`: every placeable, a belt with every good on it, pipes cold and hot.
   - `art-goods.png`: every good at 32 on a belt and at 24 in an inventory row.
   - `art-room-lit.png` and `art-room-unlit.png`: the room at the default zoom, lights on and off.
   - `art-workbench.png`: the Workbench with every card kind in the vault.

   Each frame is also written at the closest zoom (`-close`).
4. **Readability in the game**: for each chassis in the gallery, the drawn body's mean colour
   under Stalled, Starved and Waiting (read back from the viewport) differs pairwise by the
   style bible's margins, with the ambient applied.

### 7.2 Extended

- `golem-motion`: for a chassis with work frames, the impact frame is showing on the stroke's
  impact (the scenario already samples `StampImpact`), and the transform motion still passes as
  it does today.
- `moods`, `hover`: unchanged checks, rerun on the new art, and their `critique-*` captures
  viewed.
- `workbench`: when S4 changes the layout, the hint's frames still sit on the controls they name.
- `world`: the lighting checks (§6.4).

### 7.3 `perf`

A sixth phase, `unlit`: the `flowing-1x` load with `ShellNode`'s lights and the boiler glows off.
Its render CPU p95 against `flowing-1x`'s is the lighting's cost, a new row in the report's
§6.4. Every lit phase must still pass. The run is recorded in `docs/performance-report-phase4.md`.

### 7.4 Core tests

- `ArtManifestTests` (no engine): reads `Tools/Art/art_manifest.json` and each PNG's size from its
  IHDR header. Every sprite named in `data/*.json` is in the manifest; every chassis is ≤ 64 × 96;
  the tower's entries match `ClockTowerArt.CanvasWidth`/`CanvasHeight`; there are
  `ArtificerWalkAnimation.DirectionCount × FramesPerDirection` walk frames of one size; every
  `ItemType` has an icon entry.
- `GolemFramesTests`: the frame for each mood, motion, stroke phase and carried state; a chassis
  without a frame falls back to its base; the work frame follows step progress, not time (pausing
  the clock holds it).
- `LightingRulesTests` (§6.4).

### 7.5 Which checks each set runs

| Set | Python | Core | Scenarios |
|---|---|---|---|
| S1 Golems | verify, check (8, 9) | `ArtManifestTests`, `GolemFramesTests` | `art`, `golem-motion`, `moods`, `hover`, `golems`, `workbench`, `perf` |
| S1b Artificer | check (3, size) | `ArtManifestTests` | `art`, `world`, `interact` |
| S2 Floor and walls | verify, check | `SandboxLayoutTests` | `art`, `world`, `hover`, `management` (Extends) |
| Lighting | — | `LightingRulesTests` | `world`, `art`, `perf` |
| S3 Buildings | verify, check | `ArtManifestTests` | `art`, `build`, `world`, `perf` |
| S4 Workbench | verify, check (nine-slice) | — | `workbench`, `tutorial`, `art` |
| S5 Goods | verify, check (10, 11) | `ArtManifestTests` | `art`, `inventory`, `management` |
| S6 HUD | check | — | `management`, `tutorial`, `inventory`, `font-glyphs`, `art` |

## 8. Done when

**Proposed.** Phase 4 is done when all of these hold:

1. **Every set in §3 is replaced through §2, or deferred** in §4 with the user's OK and a reason.
   The golems (S1) are not deferrable.
2. **The checks are green:** `verify_art.py` exits 0 and covers every generator (the §1.3 blind
   spot fixed); `check_art.py` exits 0 over the whole manifest with every replaced set at
   `palette: bible`, including the silhouette, tint-shift, goods and copper-ore checks; the tools'
   unit tests pass.
3. **The lighting is in**, from `LightingRules`, and the **windowed `perf` run passes on an idle
   machine with it on**, its cost recorded in `docs/performance-report-phase4.md`.
4. **Every Core test and every scenario passes**, the new `art` scenario included, each scenario
   run twice headless from a clean build with no flakes.
5. **The captures are looked at:** the `art` gallery, lit and unlit, and the `moods`, `hover` and
   `workbench` critiques, with each set's in-game sheet in `public/images/<set>/`.
6. **The record is complete:** every hand-replaced file is in its generator's guard,
   `KNOWN_HAND_REPLACED`, the manifest and `CLAUDE.md`'s art section, and no RetroDiffusion art
   is left in a replaced set.

## 9. Decisions for the user

Each has a recommendation. Only the user can make these calls.

1. **The palette** (`art-style-bible.md` §1). *Recommend:* approve it as proposed. Check the two
   proposed ramps (the brass highlight `#e2c27a` and the fire ramp) on the first chassis before
   they spread.
2. **How many golem frames.** None (redraw the base only), minimal (base, stall, carry: 15
   files), or the bible's full set (base, two idle, three signature work poses, stall, carry: 35
   files, fewer if idle is cut). *Recommend:* stage it. Base, stall and carry for all five first,
   since the stall frame helps the mood read, then the signature work poses, then the idle pulse,
   which is the first to cut.
3. **Facing frames** for golems. *Recommend:* no. The facing arrow already says it, and facing
   frames would multiply every count by up to 3. Revisit in Phase 6 (game feel), where mirroring
   east and west costs no art.
4. **Who does the hand cleanup.** The user in a pixel editor, or tool-only (`palette_snap.py`,
   `check_art.py`, and scripted pixel fixes, reviewed on the comparison sheets). *Recommend:* the
   user's hand on the golems, the Artificer and the three Workbench centrepieces, where a stray
   pixel shows; tool-only for floor, walls, goods, belts and pipes, which are small and
   tile-like. If the user would rather not, tool-only everywhere, with every set's sheet shown
   before it's committed.
5. **The Artificer.** It isn't in the roadmap's list. *Recommend:* add it after the golems
   (S1b). It is always on screen, and it and the chassis are the RetroDiffusion art left.
6. **Inventory icon size.** The rows draw 32-pixel goods at 24, which drops pixels unevenly.
   *Recommend:* draw the rows at 32 (a small `InventoryPanel` change), so each good is drawn once
   and shown whole.
7. **Pixel-exact zoom.** The camera zooms continuously, so pixels are uneven at most zooms.
   *Recommend:* keep it continuous for Phase 4 and judge art at the default, closest and widest
   zooms; snapping zoom is a game-feel call for Phase 6.
8. **Lighting strength.** *Recommend:* start subtle (warm ambient around 0.88 average, today's
   sconce energy, banded pools), with every value in `LightingRules`, and let the playtest's
   `light` question move it. Never darker than the tile overlay can read on.
9. **The Steampunk UI pack.** *Recommend:* keep the eight pieces in use for Phase 4, snapped to the
   palette only if S6's review says they clash. The pack is free to use, and the HUD is last in
   screen time.
10. **The order.** *Recommend:* the roadmap's, with the Artificer after the golems and the
    lighting straight after floor and walls (§3). Goods could move ahead of the Workbench (they
    are on the belts all the time and are cheap), but the Workbench is the core verb's screen.
    The roadmap's order is fine.
11. **The unused sprites** (§1.1). *Recommend:* delete them in one cleanup commit before S1, with
    their generator lines, after a full scenario run confirms nothing loads them. Fewer files
    means a shorter manifest and nothing redrawn by mistake.
12. **ControlNet.** None is installed. *Recommend:* don't install one yet. Image to image at
    moderate denoise from the current sprite or a draft held the carts' silhouettes. Revisit only
    if a golem's redraw keeps drifting from its brief.
