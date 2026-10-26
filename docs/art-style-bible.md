# Art style bible

Written 2026-10-22 for Phase 4 (`docs/roadmap.md`, "Phase 4: Art"); revised 2026-10-23 to the
user's twelve decisions (`art-pipeline-design.md` §9) and the thirteenth (golems are creatures, invariant 17). It says what the game's art looks like
and which numbers the code depends on. `docs/art-pipeline-design.md` says how a set is made,
checked, shown and committed, and in what order.

The two references are the art that already took the ComfyUI route: **the five market carts**
(`stall_*.png`) and **the Clock Tower** (`clock_tower_*.png`). Every number below that says
"measured" was read from those files, or from the current sprites, on 2026-10-22 at 8b3ec0b.

**Cleanup is tool-only** (decision 4). Every set is cleaned by `Tools/Art/` scripts, shown to the
user on a before/after review sheet, and only the frames the user marks get a ComfyUI redo, a
scripted fix, or the user's hand on that one frame. Where this document says "hand-replaced",
it means the bookkeeping owner (a file a generator no longer owns, as `verify_art.py`'s
`KNOWN_HAND_REPLACED` and `CLAUDE.md` name it), not that a hand did the work.

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
   - **All 28 frame files of one chassis share one canvas size and one foot row** (§4), so a
     frame swap never makes the golem jump.
   - Pipe and belt tiles are 64 × 64 and centre-pivoted, because they rotate in quarter turns.
   - The 9-slice UI plates keep their margins (§6.7): a redraw that moves a plate's border moves
     every screen laid out in `Ugui` numbers. The HUD redraw (S6) keeps each pack piece's size
     and margins under its new name.
   - The walk frames are 16 files, `artificer_walk_{down,left,right,up}_{0..3}.png`, all one
     size (`ArtificerWalkAnimation`, 4 directions × 4 frames).
5. **File names are API.** `data/*.json` names sprites (`chassisSprite`, `sprite`,
   `itemSprites`, the shape sprites), `ItemIcons.PathFor` builds `item_<snake_case>.png` from the
   item type, `ClockTowerArt.Stage(n)` builds `clock_tower_stage<n>`, `GolemFrames` builds the
   golem frame names from `chassisSprite` (§4), and `SandboxLayout` names the walls and props. A
   redraw replaces a file under its own name. Renaming one is a code change, made in the same
   commit as the art (S6's HUD pieces are the one planned case).
6. **Copper ore is green**, as malachite, on the cart (`stall_copper_ore.png`) and on the good
   (`item_copper_ore.png`) alike. The good is generated (`generate_placeholder_art.py`); the cart
   is hand-replaced. A change to one is matched in the other in the same commit.
7. **Moods read over the art.** A golem's mood is a multiplicative body tint
   (`GolemMoodPalette.Tint`) plus a badge, and its source and target tiles are drawn on the floor
   at z −1 (`GolemTileOverlay`). New art must leave all three readable (§5), in every facing.
   Red, steel blue and grey belong to the moods (invariant 8). Caught by: the `moods` and `hover`
   scenarios, and `check_art.py`'s tint-shift check (§5.1).
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
    ComfyUI with tool-only cleanup (roadmap decision 3, narrowed by decision 4 here), or by a
    generator in `Tools/Art/`. The user's hand touches only a frame the user marked on a review
    sheet. **The Steampunk UI pack is being replaced** (decision 9): nothing new is taken from
    it, and its files leave the tree when S6 lands. Throwaway mockups go to `public/images/`,
    never `godot/art/` (`CLAUDE.md`, "AI image generation"). The chassis and the Artificer came
    from RetroDiffusion before decision 3; they are replaced first (S1, S1b), not a precedent.
12. **The font is not art.** Text stays in the project font, printable Latin-1 plus → ≥ █ ░
    (`font-glyphs` scenario). Nothing is lettered into a sprite that the player must read: a
    label in a texture can't be translated or checked by `PlayerText.RawIdIn`. This also keeps
    mirroring safe: a mirrored frame has no text to reverse.
13. **Depth bands stay where they are.** The floor is z −10, floor-level things (belts, pipes,
    ghost, shadows, the tile overlay) are −1, a belt's own tile −2. New art adds no layer below
    −10. A sprite's painted contact shadow sits inside its pivot allowance (the carts' three
    rows, a wall's three), and every other shadow is the shared `ground_shadow.png` at z −1.
14. **The perf budget holds with the new art and the lighting on**
    (`docs/performance-report-template.md` §1). Art that needs more nodes, more lights or a
    shader per golem is measured by the `perf` scenario before it ships. A frame swap is a
    texture change on the existing body sprite, never a node.
15. **The light comes from the upper left in every frame, mirrored ones included** (§2.2). A
    mirrored frame is re-shaded before it ships. Caught by: `check_art.py`'s light-direction
    check (`art-pipeline-design.md` §5.2, check 14).
16. **No set is committed before the user has seen its review sheet**: before and after, at the
    default, closest and widest zooms (`art-pipeline-design.md` §2, step 10).
17. **Every golem is a creature** (`art-pipeline-design.md` §9, decision 13): a head with a face
    or eye, two arms and two legs (or the chassis's own limbs: the Hauler's treads stand in for
    legs, the Scavenger's tripod for three). Its chassis motif, the press, the cargo bed, the
    balloon, is worn or wielded, never the whole body. A golem that reads as a machine with no
    one inside it breaks the cozy factory. Caught by: the review sheet.

## 1. Palette

**Approved 2026-10-23** (decision 1), as proposed. The two ramps no reference measured, the
brass highlight `#e2c27a` and the fire ramp, are confirmed on the first chassis's review sheet
before any other set uses them; if the user marks them there, they change here and in
`palette.json` before the second chassis.

Built from the carts and the tower (measured), with the brass highlight and the glow colours
taken from the generators where the references have none. `check_art.py` reads it from
`Tools/Art/palette.json` (`art-pipeline-design.md` §5.2). Every opaque pixel of a sprite in a
replaced set is one of these colours, or the set's manifest entry lists the extra colours it
needs (a good's own hue, for instance) and why.

Each ramp runs dark to light. Four tones is the usual material; the outline sits below the ramp.

| Ramp | Hex, dark → light | Source |
|---|---|---|
| **Outline** (warm brown, never black) | `#2f211c` `#3c2620` `#432a24` | The carts' outline: `#432a24` and its neighbours are 5% each of all cart edge pixels (measured) |
| **Mahogany** (red-brown wood) | `#34261f` `#432e25` `#553c30` `#775039` `#874f33` `#815e47` | The carts' wood after the softening pass (measured) |
| **Floor plank** (the darkest wood; background) | `#231710` `#36251c` `#413027` `#574430` | `floor_tile*.png` (measured); the floor stays below the props in value |
| **Bone / canvas** (highlights, awnings, paper) | `#bda694` `#c1ab98` `#d0bfaa` | Cart awnings and the tower's stone highlight (measured) |
| **Brass** | `#7d7153` `#967850` `#c49544` `#e2c27a` | `#7d7153` carts, `#967850` tower (measured); `#c49544` is `generate_placeholder_art.BRASS`; `#e2c27a` approved, confirmed on the first chassis |
| **Copper** | `#874f33` `#b87d5f` `#c08564` | Boiler and tower trim (measured) |
| **Iron / stone** | `#474242` `#51534f` `#6a6261` `#7d736f` `#9a8d85` | Tower masonry, cart ironwork (measured) |
| **Malachite** (copper ore only) | `#3e704a` `#567a56` `#609e68` `#96c896` | `item_copper_ore.png` and its cart (measured); invariant 6 |
| **Aether** (teal, and the golems' eyes) | `#296b6a` `#326f69` `#5ed6c8` | Aether cart (measured); `#5ed6c8` is `TEAL_GLOW` |
| **Steam** (puffs, vapour) | `#6e584e` `#b0a096` `#ded6cc` `#faf6ee` | `fx_steam_puff.png` (measured) |
| **Fire** (fireboxes, the hearth, the Overclocker's glow) | `#7a2e14` `#c4561f` `#f0a040` `#ffe08a` | Approved; it matches `GolemNode.GlowTint`'s forge orange; confirmed on the first chassis |
| **Skin** (the Artificer's face and hands) | `#8e5641` `#b46055` `#cc846d` `#f6ae8e` | Today's Artificer frames (measured); S1b keeps the character (the user's call, 2026-10-25) |
| **Hair** (the Artificer's plum-brown hair) | `#2d1323` `#441e33` `#532937` `#623842` `#7a4a55` | Today's Artificer frames (measured) |
| **Leather** (the Artificer's oxblood apron, strap and boots) | `#331a1c` `#4d1a17` `#612d24` `#7e3a31` `#9a5040` | Today's Artificer frames (measured); every tone under the reserved red's saturation |
| **Cloth** (the Artificer's sage shirt, eyes, goggle lenses) | `#3e4236` `#505548` `#7a8264` `#a3ab88` | Today's Artificer frames (measured); a grey sage, kept apart from malachite (invariant 6) |
| **Slate** (the Artificer's trousers) | `#1f1a2a` `#241e30` `#312b3a` `#45405a` | Today's Artificer frames (measured); off the reserved steel blue's hue |

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
  shadow side. On a mirrored frame, `reshade_mirror.py` moves the stepped-up outline back to
  the lit (left) side.
- Interior lines (a panel seam, a rivet row) use the material's darkest tone, not the outline
  colour, so the silhouette stays the strongest line in the sprite.
- **Floor tiles, belts and pipes have no outline.** They are background: a seam or a rim in the
  material's own dark tone is enough, and an outlined floor fights every golem standing on it.
- UI plates carry their border in their own ramp (brass plate: brass shadow; iron panel: iron
  shadow), because a 9-slice border is the outline.

### 2.2 Light direction

- **The key light comes from the upper left** (north-west, about ten o'clock). Highlights go on
  top and left edges, shade on the right and bottom. This holds for every facing of a golem and
  every direction of the Artificer: the light is the room's, not the body's.
- **A mirrored frame is re-shaded, never shipped as a plain flip.** Mirroring an east frame to
  make the west one moves its highlights to the right edge. `reshade_mirror.py`
  (`art-pipeline-design.md` §5.7) puts them back on the left, and `check_art.py` measures it
  (§5.1's light-direction rule).
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

- Alpha is 0 or 255. The one-pixel soft edge an AI render leaves is removed by tool
  (`palette_snap.py`, `art-pipeline-design.md` §5.3), as `slice_artificer_walk.py` already does
  with its hard alpha threshold.
- Allowed partial alpha: a contact shadow inside the pivot allowance (the carts' rows 114–119,
  measured), `ground_shadow.png`, FX (`fx_steam_puff.png`, future glows), and UI glow frames.

### 2.5 Pixel density and zoom

- One art pixel per world pixel (invariant 2). **The camera's zoom stays continuous for Phase 4**
  (decision 7): `CameraRigNode` sets an orthographic size in cells (half the view's height,
  `CameraRigRules.Zoom`, clamped to `MinOrthographicSize` 3 .. `MaxOrthographicSize` 15, default
  10). Snapping zoom to whole pixel scales is a Phase 6 (game feel) call.
- **What the three zooms do to a pixel** (screen px per art px = half the window height ÷
  (size × 64)). At the project's 1280 × 720 window: **default 0.56, closest 1.88, widest 0.38**;
  at a 1080-high window, 0.84, 2.81 and 0.56. So at the default and widest zooms the art is
  shrunk, and nearest-neighbour drops whole rows and columns. A run of *k* art pixels always
  survives a scale *s* when *k* ≥ ⌈1/*s*⌉: 2 pixels at the default zoom, 3 at the widest.
- **Detail size:** the smallest feature that must read is **2 × 2** pixels (an eye, a rivet you
  want seen), so it survives the default zoom. The features that tell one chassis or one good
  from another (an eye, a notch in the silhouette, the gap between treads) are **3 pixels**
  across, so they survive the widest. A 1-pixel detail is texture, not information.
- **Art is judged at the default, closest and widest zooms**, and every review sheet shows all
  three (invariant 16).
- **ComfyUI renders at 8× the target grid** and is reduced to it with nearest-neighbour or
  k-centroid, never bilinear (`G:/AI/comfy_generate.py` does 1024 → 128 for the Pixel Art XL
  LoRA). A 64 × 96 chassis renders at 512 × 768.

## 3. Canvas sizes by category

The sizes the code reads are fixed (invariant 4). The others are the house size for new art in
that category. `art-pipeline-design.md` §4 lists every file.

| Category | Canvas | Pivot | Notes |
|---|---|---|---|
| Golem chassis | **64 × ≤ 96** (today 64 × 86 to 64 × 96), one size for all 28 frames of a chassis | BottomCenter, feet on the bottom row | Badge at 112 px; hover covers the cell and the one above |
| Artificer walk frames | 64 × 93, all 16 the same | BottomCenter | One shared foot row (`trim_character_alpha.py`'s shared-minimum rule) |
| Floor, street tiles | 64 × 64, seamless on all four sides | centred on the cell | Variants (`_wood_b/c/d`, `_cobble_b`) share edges |
| Floor edge, kerb | 64 × 24 | top centre (`1f`) | Hang below their edge |
| North wall | 64 × 96 | (0.5, 3/96) | Lamp variant identical except the sconce |
| Side walls | 40 × 64 | x 3/40 (E) or 37/40 (W), y centre | Mirror pair (lit from straight above, so no re-shade) |
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
`golem-motion` scenario checks and what ties the stroke to the tick the output appears. The
motion already knows the facing (a lunge shifts along it), so it needs no change for facing
frames.

### 4.1 The frame set (decided: the full set, decision 2; every facing, decision 3)

**Every chassis gets every frame, in every facing.** Nothing is staged or cut.

| State | Frame slots per facing | Distinct files per facing | Timing | What changes |
|---|---|---|---|---|
| **Base** | 1 | 1 | — | The resting body. Also the work frame for every verb but the signature one, the Workbench's chassis pane (south), and the fallback for any missing frame |
| **Idle** (Working with no motion, Sleeping, Waiting, Unprogrammed) | 2 | 1 (`idle_1`; idle 0 *is* the base) | Eye-glow pulse: frame 0 for 1.6 s, frame 1 for 0.2 s, wall-clock | The eye or core lamp brightens. The body doesn't move; the bob does that |
| **Work**, the signature verb only | 3 key poses: wind-up, impact, recover | 3 | **Progress-driven**, by stroke phase: wind-up 0 – 0.62, impact 0.62 – 0.80, recover 0.80 – 1 (`StampImpact` = 0.62) | Presser: ram up, ram down. Overclocker: same, mainspring wound. Scavenger: arm reaching, gripping. Hauler: cargo bed tipped back. Zeppelin: crouched, launch |
| Work, other verbs | 0 (base frame) | 0 | — | Transform motion only, as today |
| **Stall** (Stalled, Starved) | 1 | 1 | Held while stopped | Slumped: head down, eye dim. The shake and the tint still play |
| **Carry** | 1 | 1 | Held while carried | Limbs or treads tucked, as if lifted |
| **Total** | **8** | **7** | | |
| Walk | — | — | Golems never walk | — |
| **Artificer walk** | 4 directions × 4 | 16 | Stride-driven (`ArtificerWalkAnimation.DefaultStrideLength` = 0.35 cells a frame) | As today |

**The count, exactly.** 8 frame slots per facing, 7 distinct pictures (the idle pulse's first
frame is the base). Per chassis, 7 × 4 facings = **28 files**: 21 drawn through ComfyUI (south,
north, east) and 7 made by `reshade_mirror.py` (west, from east). For the five chassis,
**140 files: 105 drawn, 35 mirrored and re-shaded.** The "35 × 4 = 140" quoted with decision 3
counts the same files: 35 is 7 pictures × 5 chassis. The fallback
`golem_generic_copper.png` stays one file, south only, since play never shows it. The Artificer
is 16 files: 12 drawn (down, up, right) and 4 mirrored and re-shaded (left, from right).

Stroke lengths the work frames follow (`GolemWorkMotion`): a stamp is 8 ticks (0.8 s at 1×), the
Overclocker's 5 ticks (0.5 s), a tug 7 ticks (0.7 s). At 4× a stamp is 0.2 s, so the impact pose
is on screen for about two frames at 60 fps; it must read in one.

### 4.2 Facings (decided: full facing, decision 3)

Four facings, `Facing.North`, `East`, `South`, `West` (`Core/World/Facing`). Each frame is made
**three ways drawn, one way mirrored**:

- **South** (front), **north** (back) and **east** (side, facing right) are drawn through
  ComfyUI (`art-pipeline-design.md` §2).
- **West** is east mirrored, then re-shaded by `reshade_mirror.py` so the light still comes from
  the upper left (§2.2), then checked (`check_art.py` check 14) and shown on the review sheet
  like every other frame.

The facing arrow stays, but only on the golem under the pointer (the user, 2026-10-24: drawn
on every golem, it covered the art). It is a state tool, and it says which tile is in front even
where a body's front and back look alike; the nearest golem's floor tiles carry the same arrow.

### 4.3 File names

The south base keeps the name the data gives it, `chassisSprite` in `data/chassis.json`
(`chassis_brass_presser.png`): it is what the Workbench pane and the fallback show, and renaming
it is a data change for nothing. **Every other frame is named from that stem:**

```
<stem>_<state>[_<n>]_<facing>.png
stem   = chassisSprite without ".png"          (chassis_brass_presser)
state  = base | idle_1 | work_0 | work_1 | work_2 | stall | carry
facing = north | east | south | west
```

So `chassis_brass_presser_work_1_east.png` is the Presser's impact pose facing east, and
`chassis_brass_presser_base_south.png` does not exist: the south base is `chassis_brass_presser.png`.
`<n>` is the frame's index within its state (idle 0 is the base, so only `idle_1` has a file).
The names are built in Core, so no chassis name is written in code.

The Artificer keeps its 16 names, `artificer_walk_{down,left,right,up}_{0..3}.png` (invariant 4).

### 4.4 Choosing a frame is a rule, so it lives in Core

- **`GolemFrames.FrameFor(mood, motion, strokePhase, carried, time, facing)`**, pure, beside
  `GolemWorkMotion`, with tests, returns a `GolemFrameKey` (state, index, facing). Facing is an
  input: each facing has its own frame set.
- **`GolemFrames.FileName(stem, key)`** builds the name of §4.3.
- **`GolemFrames.Resolve(stem, key, exists)`** walks the fallback, with `exists` a predicate the
  node passes (`ResourceLoader.Exists`; a test passes a set):
  1. the frame in its facing;
  2. **a missing facing falls back to south**, the same state and index;
  3. a missing state falls back to the base in the asked facing;
  4. then the south base (`<stem>.png`);
  5. then `golem_generic_copper.png`, `GolemNode.FallbackSprite`, as today.
- `GolemNode` resolves each key once per chassis and caches the textures, and only sets
  `_body.Texture`. The fallback lets art land one chassis at a time, though Phase 4 is not done
  until every chassis has all 28 files (`art-pipeline-design.md` §8).

## 5. Readability

### 5.1 Golems

- **A golem is a creature first** (invariant 17). Head, face or eye, arms and legs, stubby and
  sturdy rather than a knight; the chassis's motif is what it wears or works with: the Presser
  drives a ram held in a brass press frame on its shoulders, with its furnace glowing in its
  chest; the Hauler carries its cargo bed; the Zeppelin wears its balloon. Poses are the
  creature's: the Presser's arms raise the ram (wind-up), slam it down (impact) and recover; a
  stalled golem slumps with its head down; a carried one tucks its legs and arms. Its side view
  is a true profile (one arm and leg in front of the other), its back view its back (head, back
  plate, a boiler or vent), never a narrowed or masked front view.
- **What a golem holds is in front of it, in every facing.** From the north the golem faces
  away, so its body and head hide what it holds and the hands and forearms holding it: only what
  sticks out beside or above it shows (the Presser's ram overhead peeks round its head; on the
  floor at impact, a sliver beside its legs). Something in front of it on the floor sits a
  little higher on screen than its feet, behind its body, never over its legs. In a profile it
  is over the body on the facing side. The upper arms stay on top from behind. A redraw draws
  its parts in that order per facing (the Presser's `compose.py` `LAYERS`); the review sheet
  catches a tool drawn over a back view. Its surface texture is stable
  across its frames: every frame is derived from the cleaned base of its facing, so a speckle
  never moves between frames and shimmers.
- **Each chassis is told apart by its silhouette alone, in every facing.** Check: for each
  facing, the alpha masks of any two chassis' base frames, aligned at the feet, overlap at most
  **0.50** (intersection over union). Measured today (one facing): Presser and Zeppelin 0.60,
  Hauler and Zeppelin 0.59, Hauler and Presser 0.58; the Scavenger and Overclocker pairs
  0.28–0.40. The three boxy humanoids are the problem. `docs/digital-design.md`'s roster gives
  each its shape: the Scavenger a rickety tripod, the Presser bolted to the floor, the Hauler on
  treads, the Overclocker a thin butler, the Zeppelin a massive behemoth. The redraw follows the
  roster, not the current sprites. Side views are where bodies differ most; back views, where
  they differ least, so north is the facing to watch.
- **A pose keeps its chassis.** Every frame of a chassis overlaps its own base in that facing
  more than it overlaps any other chassis' base in that facing, so a stall or carry pose never
  reads as a different golem.
- **The light comes from the upper left on every frame** (invariant 15). Measured as the
  *edge-light score*: over the rows of a sprite, the mean OKLab lightness of the first
  non-outline pixel inside each row's left silhouette edge, minus that of the last one inside
  the right edge. Every frame of a standing sprite scores at least **+0.02**; a mirrored frame
  keeps at least **0.8 ×** its source's score on the edge-light score *or* on the *edge-tone
  score*, the same measure with each edge pixel counted as its tone's place in its own ramp (0
  darkest, 1 lightest) rather than its lightness. The tone score is for a mirror that puts a
  darker material on the lit edge: the Artificer walking left shows the apron there, not the
  shirt, so its lightness drops while every material is still lit from the left (user decision,
  2026-10-26). A plain flip of a correctly lit frame scores about minus its source on both, so
  it fails.
- **A golem is light enough for its tint to read.** The mood tint multiplies the body, so a dark
  body barely changes. Check, on every frame: the body's mean OKLab lightness is at least
  **0.40**, and the Stalled tint moves its mean colour at least **0.10** in OKLab. Measured
  today: lightness 0.28–0.42, Stalled shift 0.056 (Presser) to 0.125 (Overclocker). Only the
  Overclocker passes.
- **The Stalled, Starved and Waiting tints stay distinct** on every chassis and facing: pairwise
  OKLab distance of the tinted means at least 0.06 (today 0.054–0.079).
- Golems are the most saturated, highest-contrast things on the floor (§2.3), and every chassis
  has a lit teal eye or lamp in its south and side views (on the north view, a lamp on the back
  or none), the one teal the room allows apart from Aether.

### 5.2 Tiles and overlays

- The source and target tiles (`build_ghost_tile.png`, tinted teal and gold) and the facing
  arrow must read on every floor tile, street tile and belt. The `hover` scenario's capture is
  the check, with the lighting on, and `LightingRulesTests` checks it in numbers
  (`art-pipeline-design.md` §6.4).
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

**The inventory rows stay at 24 pixels** (decision 6: the user declined drawing them at 32).
Goods are drawn once, at 32, and the rows scale them by 0.75.

- **24 is not a whole fraction of 32, a known unevenness.** At 0.75 nearest-neighbour drops one
  row and one column in every four, and which ones depends on where the icon lands on screen. A
  one-pixel line can vanish in a row and not on the belt. It is accepted, not fixed: the rule
  below makes every feature that matters survive it.
- **Draw for the drop.** At 0.75 no two adjacent pixels are both dropped, so a 2-pixel feature
  always survives. A good's identifying features (the hole in a gear, the coil of a wire, a
  lens's rim) are at least 2 pixels wide, as §2.5 asks of everything.
- **Each good still reads at 24.** Check, at each of the four sampling phases (the 32 → 24
  nearest reduction offset by 0–3 pixels): the good's mask keeps the same number of connected
  pieces as at 32, and at least three of its tones survive.
- **Each good is told apart by shape, not colour alone.** Check, at 24 × 24 and at each of the
  four phases: any two goods' alpha masks overlap at most **0.80**, or their mean colours differ
  by at least 0.15 in OKLab. Measured today (phase 0): Brass and Copper Ingot share an identical
  silhouette (overlap 1.00), Copper Wire and Lens 0.83, Copper Ore and Lens 0.81.
- Raw goods read as heaps or rocks, refined goods as bars and plates, parts as cogs and springs,
  so the tier shows at a glance (`Economy/ItemTiers`).
- A good on a belt sits on the belt tile at z −1, so it carries its own one-pixel outline: it is
  the one floor-level sprite that is outlined.

## 6. What the code reads, by category

The full table, file by file, is `art-pipeline-design.md` §4; this is what each set must keep.

1. **Golems:** `data/chassis.json` `chassisSprite` (the south base); `GolemFrames` builds the
   other 27 names from it (§4.3); `GolemNode.StandOnCell`; the fallback
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
   and the `tt_node_*` plaques 10; `wb_tape` 0. The lever is `wb_lever_track` 40 × 120 with
   `wb_lever_handle` 34 × 30; the dial pip is `wb_gauge_needle` 16 × 16. The HUD pieces, today
   the Steampunk pack's and after S6 their redraws under `UI/Hud/` (`art-pipeline-design.md`
   §4, S6), keep the pack's sizes and margins: the panels 64 × 64 (bolted 12/10/12/10, plain
   8), the buttons 34 × 34 (8), the gear 58 × 59.
