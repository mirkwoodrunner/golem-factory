"""Trim the transparent rows beneath a character sprite's feet.

WHY THIS EXISTS. Every sprite that STANDS ON the floor is imported BottomCenter
(Scripts/Editor/CharacterArtAuthoring.cs), so its pivot is the bottom row of the canvas -- not
the bottom row of the drawing. Five of the eight golem sprites carry transparent rows under
their feet, so they float between 0.047 and 0.156 of a cell above the tile they occupy: the
pivot fix put them on the right tile, and the canvas padding lifts them off it again.

This is the same alpha-trim clock_tower.png, steam_boiler.png and hand_crank_bench.png were
given when they were imported, applied to the art that never got it. It is a job for the art
pipeline rather than the importer, because an importer that trimmed would be silently
disagreeing with the file on disk about where the sprite ends.

NOT generate_placeholder_art.py's job, despite that being where these sprites' PLACEHOLDERS
came from: the chassis art in godot/art/ was hand-replaced afterwards, which is why
that script only regenerates it under --legacy and warns that doing so clobbers the real art.
So the trim has to operate on the shipped pixels.

THE SHARED-MINIMUM RULE, which is the one subtle thing here. The Artificer's sixteen walk
frames are trimmed by the SAME number of rows, taken from the frame with the least padding --
never per frame. Trimming each to its own alpha bounds would manufacture a vertical bob out of
art that deliberately has none (docs/open-items.md records the missing bob as an art task left
for a person), and a procedurally invented one is exactly what was rejected there.

    python Tools/Art/trim_character_alpha.py            # report only
    python Tools/Art/trim_character_alpha.py --apply    # rewrite the PNGs

Re-runnable: a trimmed sprite has no rows left to trim, so a second run reports nothing to do.
Reimport afterwards with Tools > Golem Factory > Reimport Character Art (BottomCenter).
"""

import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from art_paths import art_dir  # noqa: E402
import sys

from PIL import Image

ART_DIR = art_dir()

# Trimmed independently: each is one sprite standing on its own, and nothing compares its
# height to another's.
INDEPENDENT = [
    "chassis_clockwork_scavenger",
    "chassis_brass_presser",
    "chassis_aether_hauler",
    "chassis_mainspring_overclocker",
    "chassis_zeppelin_freight_loader",
    "golem_generic_brass",
    "golem_generic_copper",
    "golem_generic_steel",
]

# Trimmed together, by the smallest padding any of them has. See THE SHARED-MINIMUM RULE.
LOCKSTEP_GROUPS = {
    "artificer walk cycle": [
        f"artificer_walk_{direction}_{frame}"
        for direction in ("down", "left", "right", "up")
        for frame in range(4)
    ],
}


def bottom_padding(image: Image.Image) -> int:
    """Fully transparent rows at the bottom of the canvas."""
    width, height = image.size
    pixels = image.load()
    padding = 0
    for y in range(height - 1, -1, -1):
        if any(pixels[x, y][3] > 0 for x in range(width)):
            break
        padding += 1

    return padding


def load(name: str) -> Image.Image:
    return Image.open(os.path.join(ART_DIR, name + ".png")).convert("RGBA")


def trim(name: str, rows: int, apply: bool) -> None:
    image = load(name)
    width, height = image.size
    if rows <= 0:
        print(f"  {name:38s} {width}x{height}  already on the floor")
        return

    print(f"  {name:38s} {width}x{height} -> {width}x{height - rows}"
          f"  ({rows}px, {rows / 64.0:.3f} of a cell)")
    if apply:
        image.crop((0, 0, width, height - rows)).save(os.path.join(ART_DIR, name + ".png"))


def main() -> None:
    apply = "--apply" in sys.argv
    print(("APPLYING" if apply else "DRY RUN (pass --apply to rewrite)") + " in " + ART_DIR)

    print("independent:")
    for name in INDEPENDENT:
        trim(name, bottom_padding(load(name)), apply)

    for group, names in LOCKSTEP_GROUPS.items():
        shared = min(bottom_padding(load(name)) for name in names)
        print(f"{group}: trimming all {len(names)} frames by the shared minimum of {shared}px")
        for name in names:
            trim(name, shared, apply)


if __name__ == "__main__":
    main()
