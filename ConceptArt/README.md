# Concept Art

Inspirational/reference art for golem-factory. Not final game assets.

## Contents

- **`artificer.png`** (172x256) - Pixel-art portrait of the Artificer, the
  player-facing character. Goggles pushed up on a headscarf, denim/canvas
  work clothes, leather tool apron with wrench and tools.

- **`golem lineup.png`** (1408x768) - "Golem Factory: The Clockwork
  Metropolis - Unit Lineup" reference sheet, showing five golem units with
  their game-stage roles:
  1. Scavenger (Early Game)
  2. Brass Presser (Early-Mid Game)
  3. Aether-Hauler (Mid-Game Logistics)
  4. Mainspring Overclocker (Utility)
  5. Zeppelin Freight Loader (Late Game Wealth)

- **`workshop.png`** (1408x768) - Isometric pixel-art scene of the golem
  workshop floor: an artificer channeling aether energy at a central
  workbench, conveyor belts feeding scrap and resources between forges and
  crafting stations, a scavenger golem sorting rubble, and a hauler golem
  crane loading crates onto airships over the clockwork city skyline outside.

- **`artificer_walk_4dir_sheet.png`** (768x1362) - Four-direction walk cycle for the
  Artificer, 4 rows x 4 frames: down, right, up, left. Note that the two profile rows
  face **opposite** ways and are drawn independently, so neither is a mirror of the
  other. Rendered *in* a pixel-art style rather than as pixel art - 101k colours, no
  recoverable native grid - so it needs conditioning before use, which is what
  `artificer_walk/` holds.

- **`artificer_walk/`** - The above, made engine-ready by
  `Tools/Art/slice_artificer_walk.py` (re-run it to regenerate; it is idempotent):
  - `frames/artificer_walk_<dir>_<0-3>.png` - 16 frames at 64x96, one shared 32-colour
    palette, hard alpha edges, feet on a common baseline 3px off the cell floor.
  - `artificer_walk_sheet_64x96.png` (256x384) - the same frames as a uniform grid,
    ready for Unity's *Grid By Cell Size* slicer at 64x96.
  - `artificer_walk_preview.gif` - all four directions animating side by side.

  64x96 at PPU 64 matches the chassis sprites and puts the character at 1.0x1.5 world
  units - near-identical on-screen size to the current `player.png` (96x143 at PPU 100).
  Still concept-derived, not wired into the game: nothing under `Assets/` references it.

## Adding new art

Drop the file in this folder and add a matching entry above.
