# Art style bible

Written 2026-10-22 for Phase 4 (`docs/roadmap.md`, "Phase 4: Art"). It says what the game's
art looks like and which numbers the code depends on. `docs/art-pipeline-design.md` says how a
set is made, checked and committed, in what order, and what the user still has to decide.

The two references are the art that already took the chosen route (roadmap decision 3, ComfyUI
plus hand cleanup): **the five market carts** (`stall_*.png`) and **the Clock Tower**
(`clock_tower_*.png`). Every number below that says "measured" was read from those files, or from
the current sprites, on 2026-10-22 at 8b3ec0b. A number that says "proposed" waits for the user's
approval (`art-pipeline-design.md` §9, decision 1).

## 0. Invariants

An art pass must not break any of these. Each names what catches a violation today, or the check
`art-pipeline-design.md` §5 adds.

1. **Feet on the node's origin.** A standing sprite's contact line with the floor is its pivot,
   and the pivot lands on the node's origin, because y-sort compares origins
   (`GridConversions.StandOnCell`, `Scripts/World/SpritePivots`). A redraw keeps the contact line
   on the row the pivot names: the bottom row for a BottomCenter sprite, row 117 of 120 for a
   cart (`3f / 120f`), row 93 of 96 for a north wall (`3f / 96f`), row 55 of 56 for a prop
   (`1f / 56f`). Transparent rows under the feet make a sprite float
   (`Tools/Art/trim_character_alpha.py` exists because five golems did). Caught by: the `world`
   scenario ("standing sprites sort at their feet") and `check_art.py`'s foot-row check.
2. **PPU 64.** One cell is 64 × 64 art pixels (`GridConversions.CellPixels`). Every world sprite
   is drawn at scale 1, so one art pixel is one world pixel. There is no second resolution: no
   sprite is authored at 2× and scaled down in the engine.
3. **Nearest-neighbour everywhere.** `project.godot` sets
   `textures/canvas_textures/default_texture_filter=0`. No import sets its own filter, and no
   art relies on smoothing. A soft edge is drawn, in pixels, or not at all.
4. **Canvas sizes and pivots that code reads stay fixed** unless the same change edits the code
   and its test. The list is §6 and the manifest (`Tools/Art/art_manifest.json`,
   `art-pipeline-design.md` §5.1). The ones most likely to be broken by accident:
   - `ClockTowerArt.CanvasWidth`/`CanvasHeight`: 192 × 640, pinned by `ClockTowerArtTests`.
   - A chassis is at most 64 × 96. Its badge floats `BadgeLiftCells` = 1.75 cells (112 px) above
     the feet (`GolemNode`), and hover picks "the golem's cell, or the cell above its feet". A
     taller golem hides its own badge and has a head that can't be hovered.
   - Pipe and belt tiles are 64 × 64 and centre-pivoted, because they rotate in quarter turns.
   - The 9-slice UI plates keep their margins (§6.7): a redraw that moves a plate's border moves
     every screen laid out in `Ugui` numbers.
   - The walk frames are 16 files, `artificer_walk_{down,left,right,up}_{0..3}.png`, all one
     size (`ArtificerWalkAnimation`, 4 directions × 4 frames).
5. **File names are API.** `data/*.json` names sprites (`chassisSprite`, `sprite`,
   `itemSprites`, the shape sprites), `ItemIcons.PathFor` builds `item_<snake_case>.png` from the
   item type, `ClockTowerArt.Stage(n)` builds `clock_tower_stage<n>`, and `SandboxLayout` names
   the walls and props. A redraw replaces a file under its own name. Renaming one is a code change.
6. **Copper ore is green**, as malachite, on the cart (`stall_copper_ore.png`) and on the good
   (`item_copper_ore.png`) alike. The good is generated (`generate_placeholder_art.py`); the cart
   is hand-replaced. A change to one is matched in the other in the same commit.
7. **Moods read over the art.** A golem's mood is a multiplicative body tint
   (`GolemMoodPalette.Tint`) plus a badge, and its source and target tiles are drawn on the floor
   at z −1 (`GolemTileOverlay`). New art must leave all three readable (§5). Red, steel blue and
   grey belong to the moods (invariant 8). Caught by: the `moods` and `hover` scenarios, and
   `check_art.py`'s tint-shift check (§5.2).
8. **Reserved state colours.** Saturated red means "the player must act", steel blue means "no
   steam", grey means "waiting, fine" (`docs/onboarding-feedback-design.md` §3). No sprite uses
   a large area of saturated red or of steel blue as its own colour (§5.3), so a tint or badge is
   never mistaken for paint, or paint for a fault.
9. **Every replaced sprite is accounted for.** A hand-replaced file is in its generator's
   `HAND_REPLACED` (or the generator refuses to write it, as the tower's does), in
   `verify_art.py`'s `KNOWN_HAND_REPLACED`, in the manifest with owner `hand-replaced`, and in
   `CLAUDE.md`'s art section. Caught by: `verify_art.py` (a drifted file not in the list fails)
   and `check_art.py` (a file not in the manifest fails).
10. **Generators still reproduce what they own.** A generator writes only the files it owns.
    `verify_art.py` regenerates them into a scratch folder and fails on any drift. A file the
    user wants replaced moves to `hand-replaced`; it never stays "generated" with different
    pixels.
11. **The route the user chose, and only that route, ships.** Shipped art is made through
    ComfyUI plus hand cleanup (roadmap decision 3), or by a generator in `Tools/Art/`, or it is
    the third-party Steampunk UI pack already in the tree. Throwaway mockups go to
    `public/images/`, never `godot/art/` (`CLAUDE.md`, "AI image generation"). The chassis and
    the Artificer came from RetroDiffusion before that decision; they are on the replacement list
    (`art-pipeline-design.md` §3), not a precedent.
12. **The font is not art.** Text stays in the project font, printable Latin-1 plus → ≥ █ ░
    (`font-glyphs` scenario). Nothing is lettered into a sprite that the player must read: a
    label in a texture can't be translated or checked by `PlayerText.RawIdIn`.
13. **Depth bands stay where they are.** The floor is z −10, floor-level things (belts, pipes,
    ghost, shadows, the tile overlay) are −1, a belt's own tile −2. New art adds no layer below
    −10. A sprite's painted contact shadow sits inside its pivot allowance (the carts' three
    rows, a wall's three), and every other shadow is the shared `ground_shadow.png` at z −1.
14. **The perf budget holds with the new art and the lighting on**
    (`docs/performance-report-template.md` §1). Art that needs more nodes, more lights or a
    shader per golem is measured by the `perf` scenario before it ships.

## 1. Palette

**Proposed.** Built from the carts and the tower (measured), with the brass highlight and the
glow colours taken from the generators where the references have none. `check_art.py` reads it
from `Tools/Art/palette.json` (`art-pipeline-design.md` §5.2). Every opaque pixel of a sprite in
a replaced set is one of these colours, or the set's manifest entry lists the extra colours it
needs (a good's own hue, for instance) and why.

Each ramp runs dark to light. Four tones is the usual material; the outline sits below the ramp.

| Ramp | Hex, dark → light | Source |
|---|---|---|
| **Outline** (warm brown, never black) | `#2f211c` `#3c2620` `#432a24` | The carts' outline: `#432a24` and its neighbours are 5% each of all cart edge pixels (measured) |
| **Mahogany** (red-brown wood) | `#34261f` `#432e25` `#553c30` `#775039` `#874f33` `#815e47` | The carts' wood after the softening pass (measured) |
| **Floor plank** (the darkest wood; background) | `#231710` `#36251c` `#413027` `#574430` | `floor_tile*.png` (measured); the floor stays below the props in value |
| **Bone / canvas** (highlights, awnings, paper) | `#bda694` `#c1ab98` `#d0bfaa` | Cart awnings and the tower's stone highlight (measured) |
| **Brass** | `#7d7153` `#967850` `#c49544` `#e2c27a` | `#7d7153` carts, `#967850` tower (measured); `#c49544` is `generate_placeholder_art.BRASS`; `#e2c27a` proposed |
| **Copper** | `#874f33` `#b87d5f` `#c08564` | Boiler and tower trim (measured) |
| **Iron / stone** | `#474242` `#51534f` `#6a6261` `#7d736f` `#9a8d85` | Tower masonry, cart ironwork (measured) |
| **Malachite** (copper ore only) | `#3e704a` `#567a56` `#609e68` `#96c896` | `item_copper_ore.png` and its cart (measured); invariant 6 |
| **Aether** (teal, and the golems' eyes) | `#296b6a` `#326f69` `#5ed6c8` | Aether cart (measured); `#5ed6c8` is `TEAL_GLOW` |
| **Steam** (puffs, vapour) | `#6e584e` `#b0a096` `#ded6cc` `#faf6ee` | `fx_steam_puff.png` (measured) |
| **Fire** (fireboxes, the hearth, the Overclocker's glow) | `#7a2e14` `#c4561f` `#f0a040` `#ffe08a` | Proposed; it matches `GolemNode.GlowTint`'s forge orange |

What stays out:

- **Pure black and the purple-blacks.** The current chassis are outlined in `#15051f`,
  `#0c0620` and `#3b0d04`, and the boiler and bench in `#322239` (measured). Those are the
  RetroDiffusion sprites' look, and they are why the golems and the boiler read colder and
  harsher than the carts beside them.
- **Saturated red and steel blue as body colours** (invariant 8, §5.3).
- **Semi-transparent pixels**, except in a contact shadow inside the pivot allowance, in FX, and
  in UI glow (§2.4).

## 2. Drawing rules

### 2.1 Outlines

- One pixel wide, on the silhouette only, in the outline ramp. No black.
- **Selective outline:** where a lit edge meets the background, the outline may step up to the
  material's darkest tone instead (the carts do this on their awnings). It never steps up on the
  shadow side.
- Interior lines (a panel seam, a rivet row) use the material's darkest tone, not the outline
  colour, so the silhouette stays the strongest line in the sprite.
- **Floor tiles, belts and pipes have no outline.** They are background: a seam or a rim in the
  material's own dark tone is enough, and an outlined floor fights every golem standing on it.
- UI plates carry their border in their own ramp (brass plate: brass shadow; iron panel: iron
  shadow), because a 9-slice border is the outline.

### 2.2 Light direction

- **The key light comes from the upper left** (north-west, about ten o'clock). Highlights go on
  top and left edges, shade on the right and bottom.
- The room's real light, the sconces on the north wall, comes from the top. The painted light
  agrees with it, so a lit pool on the floor and a highlight on a crate don't argue.
- **Sprites that rotate are lit from straight above**: belt and pipe tiles, the facing arrow,
  the tile overlay. A quarter turn of a directionally lit tile puts its highlight on the wrong
  side. They get top-down shading only (a lighter centre, darker rims).
- No cast shadow is painted beyond the pivot allowance (invariant 13). The shared
  `ground_shadow.png` is the contact shadow for golems and props.

### 2.3 Shading

- **Four tones per material at most** (shadow, base, light, highlight), plus the outline.
  Measured: the carts use about 47 colours each, a chassis 29–38, a prop up to 72.
  **A standing sprite uses at most 48 opaque colours; a good, at most 12; a floor tile, at most
  16.** The floor tiles today carry 81–88 (wood grain noise); their redraw comes down to 16.
- **No dithering on anything that moves.** Golems squash, stretch and lean
  (`GolemWorkMotion`), and a dither pattern under a skew crawls. Floor and walls may use a
  sparse 2-tone dither for grain.
- **Hue-shift the ramps**: shadows lean toward red-brown, highlights toward warm yellow, as the
  carts' wood does. No ramp darkens toward blue or purple.
- **Contrast budget:** the floor is darkest and least saturated, then walls, then props and
  buildings, then golems and goods, which carry the most contrast and the only pure highlights.
  The player's eye should land on what moves. Today it is the other way round: the golems are
  the darkest things in the room (§5.1).

### 2.4 Alpha

- Alpha is 0 or 255. The one-pixel soft edge an AI render leaves is removed in cleanup
  (`palette_snap.py`, `art-pipeline-design.md` §5.3), as `slice_artificer_walk.py` already does
  with its hard alpha threshold.
- Allowed partial alpha: a contact shadow inside the pivot allowance (the carts' rows 114–119,
  measured), `ground_shadow.png`, FX (`fx_steam_puff.png`, future glows), and UI glow frames.

### 2.5 Pixel density

- One art pixel per world pixel (invariant 2). **The camera's zoom is continuous**:
  `CameraRigNode` sets an orthographic size in cells (`CameraRigRules.Zoom`, clamped to
  `MinOrthographicSize`..`MaxOrthographicSize`), so at most zooms one art pixel covers a
  non-whole number of screen pixels and nearest-neighbour makes some columns one pixel wider than
  others. Art is judged at the default zoom, the closest zoom and the widest. Whether zoom should
  snap to whole pixel scales is decision 7 in `art-pipeline-design.md` §9.
- **Detail size:** the smallest feature that must read is 2 × 2 pixels at zoom 1 (an eye, a
  rivet you want seen). A 1-pixel detail is texture, not information.
- **ComfyUI renders at 8× the target grid** and is reduced to it with nearest-neighbour or
  k-centroid, never bilinear (`G:/AI/comfy_generate.py` does 1024 → 128 for the Pixel Art XL
  LoRA). A 64 × 96 chassis renders at 512 × 768.

## 3. Canvas sizes by category

The sizes the code reads are fixed (invariant 4). The others are the house size for new art in
that category. `art-pipeline-design.md` §4 lists every file.

| Category | Canvas | Pivot | Notes |
|---|---|---|---|
| Golem chassis | **64 × ≤ 96** (today 64 × 86 to 64 × 96) | BottomCenter, feet on the bottom row | Badge at 112 px; hover covers the cell and the one above |
| Artificer walk frames | 64 × 93, all 16 the same | BottomCenter | One shared foot row (`trim_character_alpha.py`'s shared-minimum rule) |
| Floor, street tiles | 64 × 64, seamless on all four sides | centred on the cell | Variants (`_wood_b/c/d`, `_cobble_b`) share edges |
| Floor edge, kerb | 64 × 24 | top centre (`1f`) | Hang below their edge |
| North wall | 64 × 96 | (0.5, 3/96) | Lamp variants identical except the sconce |
| Side walls | 40 × 64 | x 3/40 (E) or 37/40 (W), y centre | Mirror pair |
| Corner post | 50 × 96 | (0.5, 1/96) | |
| Props | ≤ 56 × 56 | (0.5, 1/56) | Stand on `ground_shadow` |
| One-cell buildings | ≤ 80 wide × ≤ 120 tall | BottomCenter | Depot 62 × 69, Post 62 × 92, boiler 71 × 114, mast 52 × 118 today |
| Market carts | 96 × 120 | (0.5, 3/120), wheels on row 117 | Two-cell pitch |
| Clock Tower | 192 × 640 | bottom centre of the 3 × 3 footprint | `ClockTowerArt` |
| Belt, pipe tiles | 64 × 64 | centre | Rotate; top-lit (§2.2) |
| Goods | 32 × 32 | centre | Drawn at 32 on belts, at 24 in inventory rows (§5.4) |
| FX | as small as the effect (puff 24 × 20) | centre | Scaled by code |
| 9-slice UI | per plate, §6.7 | — | Margins fixed |
| Tech tree badges | 16 × 16 | — | |

## 4. Animation

The simulation runs at 10 ticks a second at 1× (`SimulationClock.TicksPerSecond`). Golem motion
today is one body sprite moved by transforms: scale, skew, offset and tint
(`GolemWorkMotion`, `GolemAnimationUtility`), with the steam puff on impacts. **That stays.**
Frames are added on top of it, never instead of it, because the transform motion is what the
`golem-motion` scenario checks and what ties the stroke to the tick the output appears.

**Proposed** frame sets (decision 2 in the pipeline doc says how many to draw):

| State | Frames per chassis | Timing | What changes |
|---|---|---|---|
| **Idle** (Working with no motion, Sleeping, Waiting, Unprogrammed) | 2 | Eye-glow pulse: frame 0 for 1.6 s, frame 1 for 0.2 s, wall-clock | The eye or core lamp brightens. The body doesn't move; the bob does that |
| **Work**, the signature verb only | 3 key poses: wind-up, impact, recover | **Progress-driven**, by stroke phase: wind-up 0 – 0.62, impact 0.62 – 0.80, recover 0.80 – 1 (`StampImpact` = 0.62) | Presser: ram up, ram down. Overclocker: same, mainspring wound. Scavenger: arm reaching, gripping. Hauler: cargo bed tipped back. Zeppelin: crouched, launch |
| Work, other verbs | 0 (base frame) | — | Transform motion only, as today |
| **Stall** (Stalled, Starved) | 1 | Held while stopped | Slumped: head down, eye dim. The shake and the tint still play |
| **Carry** | 1 | Held while carried | Limbs or treads tucked, as if lifted |
| Walk | — | Golems never walk | — |
| **Artificer walk** | 4 directions × 4 | Stride-driven (`ArtificerWalkAnimation.DefaultStrideLength` = 0.35 cells a frame) | As today |

Stroke lengths the work frames follow (`GolemWorkMotion`): a stamp is 8 ticks (0.8 s at 1×), the
Overclocker's 5 ticks (0.5 s), a tug 7 ticks (0.7 s). At 4× a stamp is 0.2 s, so the impact pose
is on screen for about two frames at 60 fps; it must read in one.

**One facing.** A golem has one picture for all four facings today; the facing arrow says which
way it points. Facing frames would multiply every count above by up to 3 (decision 3).

**Choosing a frame is a rule, so it lives in Core**: a pure `GolemFrames.FrameFor(mood, motion,
strokePhase, carried, time)` beside `GolemWorkMotion`, with tests, and `GolemNode` only sets the
texture. A chassis with no frames for a state falls back to its base frame, so the art can land
one chassis at a time.

## 5. Readability

### 5.1 Golems

- **Each chassis is told apart by its silhouette alone.** Check: the alpha masks of any two
  chassis, aligned at the feet, overlap at most **0.50** (intersection over union). Measured
  today: Presser and Zeppelin 0.60, Hauler and Zeppelin 0.59, Hauler and Presser 0.58; the
  Scavenger and Overclocker pairs 0.28–0.40. The three boxy humanoids are the problem.
  `docs/digital-design.md`'s roster gives each its shape: the Scavenger a rickety tripod, the
  Presser bolted to the floor, the Hauler on treads, the Overclocker a thin butler, the Zeppelin
  a massive behemoth. The redraw follows the roster, not the current sprites.
- **A golem is light enough for its tint to read.** The mood tint multiplies the body, so a dark
  body barely changes. Check: the body's mean OKLab lightness is at least **0.40**, and the
  Stalled tint moves its mean colour at least **0.10** in OKLab. Measured today: lightness
  0.28–0.42, Stalled shift 0.056 (Presser) to 0.125 (Overclocker). Only the Overclocker passes.
- **The Stalled, Starved and Waiting tints stay distinct** on every chassis: pairwise OKLab
  distance of the tinted means at least 0.06 (today 0.054–0.079).
- Golems are the most saturated, highest-contrast things on the floor (§2.3), and every chassis
  has a lit teal eye or lamp, the one teal the room allows apart from Aether.

### 5.2 Tiles and overlays

- The source and target tiles (`build_ghost_tile.png`, tinted teal and gold) and the facing
  arrow must read on every floor tile, street tile and belt. The `hover` scenario's capture is
  the check, with the lighting on.
- The floor's value range stays narrow (`#231710` to `#574430`) so that every tint, ghost and
  overlay sits on a quiet ground.

### 5.3 Reserved colours

- **Red**: no sprite has more than 5% of its opaque pixels with hue 345°–15°, saturation above
  0.55 and lightness above 0.35. Fire is orange (hue ≥ 20°), not red. The Hauler's red body and
  the Scavenger's copper are measured against this in the redraw.
- **Steel blue**: no sprite has more than 5% of its pixels at hue 200°–230°, saturation above
  0.30. Aether teal sits at 175°–180° and is allowed.
- **Grey**: a golem body is never mostly neutral grey; that is the Waiting tint's job.

### 5.4 Goods icons

- **Each good is told apart by shape, not colour alone.** Check: at 24 × 24 (nearest-neighbour
  from 32, which is how inventory rows draw them), any two goods' alpha masks overlap at most
  **0.80**, or their mean colours differ by at least 0.15 in OKLab. Measured today: Brass and
  Copper Ingot share an identical silhouette (overlap 1.00), Copper Wire and Lens 0.83, Copper
  Ore and Lens 0.81.
- Raw goods read as heaps or rocks, refined goods as bars and plates, parts as cogs and springs,
  so the tier shows at a glance (`Economy/ItemTiers`).
- A good on a belt sits on the belt tile at z −1, so it carries its own one-pixel outline: it is
  the one floor-level sprite that is outlined.
- **24 is not a whole fraction of 32.** The inventory rows scale icons by 0.75, which drops
  pixels unevenly. The redraw is checked at 24, and the pipeline doc asks whether the rows should
  draw at 32 instead (decision 6).

## 6. What the code reads, by category

The full table, file by file, is `art-pipeline-design.md` §4; this is what each set must keep.

1. **Golems:** `data/chassis.json` `chassisSprite`; `GolemNode.StandOnCell`; the fallback
   `golem_generic_copper.png`; `fx_steam_puff.png` loaded by path.
2. **Artificer:** `PlayerNode` builds `res://art/artificer_walk_{row}_{frame}.png`.
3. **Floor and walls:** `FloorLayer` (floor and street tiles, by name), `SandboxLayout` and
   `SpritePivots` (walls, edges, props).
4. **Buildings:** `data/placeables.json` `sprite` and shape sprites; `BuildingsLayer` centres
   belt and pipe pieces and stands everything else on its cell; the depot by
   `DepotNode`, the station by `ConstructionStationNode`.
5. **Goods:** `placeables.json` `itemSprites` (belts), `ItemIcons.PathFor` (UI), `BeltRun` loads
   `item_scrap.png` as its default.
6. **Overlays:** `build_ghost_tile.png`, `facing_arrow.png` (golems, cursor, guide panel),
   `interaction_ring.png`, `ground_shadow.png`.
7. **UI (9-slice margins, in pixels):** `wb_panel_mahogany`, `wb_panel_iron`,
   `wb_panel_blueprint` 12; `wb_plate_brass`, `wb_slot_socket`, `wb_card_face`, `tt_phase_plate`
   and the `tt_node_*` plaques 10; `wb_tape` 0; Steampunk `iron_panel_nobolt` and the buttons 8;
   `steampunk_panel_iron_bolt` 12/10/12/10. The lever is `wb_lever_track` 40 × 120 with
   `wb_lever_handle` 34 × 30; the dial pip is `wb_gauge_needle` 16 × 16.
