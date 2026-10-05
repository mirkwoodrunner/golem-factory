#!/usr/bin/env python3
"""Generates the chrome for the Artificer's Ledger -- the in-game tech tree chart
(docs/progression-design.md's research track, drawn by Scripts/UI/TechTreePanel.cs).

Companion to generate_workbench_ui_art.py and deliberately sharing its
mahogany-and-brass palette: the Ledger is a fifth tab on the Management screen, so it has
to look like it was built by the same shop as the Workbench rather than like a chart
pasted over the game.

WHAT THIS DOES AND DOES NOT DRAW. It draws plaques, badges, rules and the drafting
field -- nothing with a node name on it. The chart's *arrangement* is
Progression/TechTreeChartLayout.cs and its *content* is Progression/TechTreeCatalog.cs,
both of which tests hold against the authored recipe and chassis assets. A baked poster
with the tree burned into it would be a second copy of SS5.2/SS6 that no test could reach,
and it would go quietly stale the first time a recipe was retuned.

THE THREE NODE STATES DIFFER BY SILHOUETTE, NOT ONLY BY COLOUR:
  locked     -- iron, shut, a bar across the face and no punch holes
  available  -- brass, open socket notch on the left, punch holes down the edge
  researched -- brass, double keyline, a stamped seal in the corner
Colour carries it a second time; it is never the only channel. A fourth plaque, `planned`,
is a drafting sketch (dashed, unfilled) for the nodes docs/open-items.md lists as designed
but unbuilt -- those can never be researched, and must not look like they might be.

Every plaque is authored with an explicit 9-slice border (10px on the 32px plaques) and is
imported with it by Scripts/Editor/TechTreeArtAuthoring.cs. Re-run to regenerate; requires
Pillow.
"""

import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from art_paths import art_dir  # noqa: E402

from PIL import Image, ImageDraw

OUT_DIR = art_dir("UI", "TechTree")

# Lifted verbatim from generate_workbench_ui_art.py so the two screens share one palette.
MAHOGANY_DARK = (66, 36, 25, 255)
BRASS = (196, 149, 68, 255)
BRASS_LIGHT = (238, 200, 116, 255)
BRASS_DARK = (124, 90, 38, 255)
BRASS_DEEP = (86, 60, 24, 255)
IRON = (74, 70, 66, 255)
IRON_DARK = (40, 38, 36, 255)
IRON_LIGHT = (116, 112, 106, 255)
BLUEPRINT = (26, 46, 62, 255)
BLUEPRINT_LIGHT = (38, 64, 84, 255)
PARCHMENT = (219, 203, 165, 255)
OUTLINE = (24, 14, 10, 255)
TRANSPARENT = (0, 0, 0, 0)


def save(img: Image.Image, name: str) -> None:
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, name)
    img.save(path)
    print(f"wrote {path} ({img.width}x{img.height})")


def bevel(d: ImageDraw.ImageDraw, box, light, dark, inset=0, width=1) -> None:
    """Raised bevel: `light` along top/left, `dark` along bottom/right."""
    x0, y0, x1, y1 = box
    x0 += inset
    y0 += inset
    x1 -= inset
    y1 -= inset
    for i in range(width):
        d.line([(x0 + i, y0 + i), (x1 - i, y0 + i)], fill=light)
        d.line([(x0 + i, y0 + i), (x0 + i, y1 - i)], fill=light)
        d.line([(x0 + i, y1 - i), (x1 - i, y1 - i)], fill=dark)
        d.line([(x1 - i, y0 + i), (x1 - i, y1 - i)], fill=dark)


def rivets(d: ImageDraw.ImageDraw, size: int, colour, highlight) -> None:
    """One rivet per corner, kept inside the 10px slice border so 9-slicing shows four."""
    for cx, cy in ((5, 5), (size - 6, 5), (5, size - 6), (size - 6, size - 6)):
        d.rectangle([cx - 1, cy - 1, cx + 1, cy + 1], fill=colour)
        d.point((cx, cy), fill=highlight)


# --------------------------------------------------------------------------------------
# The drafting field the whole chart sits on.
# --------------------------------------------------------------------------------------


def make_field() -> Image.Image:
    """32x32 tiling drafting paper. Tiled (not stretched) so the graticule stays square
    across a 1720x888 chart instead of smearing into bands."""
    size = 32
    img = Image.new("RGBA", (size, size), BLUEPRINT)
    d = ImageDraw.Draw(img)
    # Minor rule every 8px, major rule on the tile seam -- the seam has to carry a line
    # the eye expects anyway, or the repeat announces itself.
    for i in range(0, size, 8):
        d.line([(i, 0), (i, size - 1)], fill=BLUEPRINT_LIGHT)
        d.line([(0, i), (size - 1, i)], fill=BLUEPRINT_LIGHT)
    d.line([(0, 0), (0, size - 1)], fill=(48, 78, 100, 255))
    d.line([(0, 0), (size - 1, 0)], fill=(48, 78, 100, 255))
    return img


# --------------------------------------------------------------------------------------
# Node plaques. 32x32, 9-slice border 10.
# --------------------------------------------------------------------------------------


def _plaque_base(face, frame, frame_light, frame_dark):
    size = 32
    img = Image.new("RGBA", (size, size), TRANSPARENT)
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, size - 1, size - 1], fill=frame, outline=OUTLINE)
    bevel(d, (0, 0, size - 1, size - 1), frame_light, frame_dark, inset=1)
    d.rectangle([3, 3, size - 4, size - 4], fill=face)
    return img, d, size


def make_node_locked() -> Image.Image:
    """Iron, shut. The bar across the face is the silhouette cue -- at a glance a locked
    card is the one with a line through it, whatever the palette is doing."""
    img, d, size = _plaque_base((44, 42, 40, 255), IRON_DARK, IRON, (22, 21, 20, 255))
    d.line([(3, 3), (size - 4, 3)], fill=(58, 56, 53, 255))
    # The bar. Kept in the vertical middle of the 12px centre region so a stretched
    # plaque draws exactly one bar across the middle of the card, not a stack of them.
    d.rectangle([10, 14, size - 11, 17], fill=IRON, outline=(30, 29, 28, 255))
    rivets(d, size, (30, 29, 28, 255), IRON_LIGHT)
    return img


def make_node_available() -> Image.Image:
    """Brass, open. Punch holes down the left border region and a socket notch cut out of
    the left edge -- the card is ready to take a pin."""
    img, d, size = _plaque_base(BRASS, BRASS_DARK, BRASS_LIGHT, BRASS_DEEP)
    d.line([(3, 3), (size - 4, 3)], fill=BRASS_LIGHT)
    d.line([(3, size - 4), (size - 4, size - 4)], fill=BRASS_DEEP)
    # Punch holes: one per 5px inside the 10px left border, so 9-slicing shows the column
    # once at native height rather than repeating it across the card.
    for y in (7, 12, 17, 22):
        d.rectangle([5, y, 7, y + 1], fill=(120, 92, 40, 255))
    # Socket notch, cut clean out of the frame.
    d.rectangle([0, 13, 2, 18], fill=TRANSPARENT)
    d.line([(3, 13), (3, 18)], fill=BRASS_LIGHT)
    rivets(d, size, BRASS_DEEP, BRASS_LIGHT)
    return img


def make_node_researched() -> Image.Image:
    """Brass, stamped. Double keyline inside the frame plus a seal in the top-right
    corner: the two cues a claimed punch card would physically carry."""
    img, d, size = _plaque_base((214, 168, 84, 255), BRASS, BRASS_LIGHT, BRASS_DEEP)
    d.rectangle([5, 5, size - 6, size - 6], outline=BRASS_DEEP)
    d.rectangle([7, 7, size - 8, size - 8], outline=(240, 208, 132, 255))
    # The seal: a small filled disc with a cross-punch, in the corner the border keeps.
    d.ellipse([size - 9, 4, size - 4, 9], fill=(158, 62, 44, 255), outline=OUTLINE)
    d.point((size - 7, 6), fill=(206, 110, 84, 255))
    rivets(d, size, BRASS_DEEP, (250, 224, 160, 255))
    return img


def make_node_planned() -> Image.Image:
    """A drafting sketch: dashed outline, no fill, no rivets. Reserved for nodes
    docs/open-items.md records as designed and unbuilt -- they can never be researched, so
    they must never wear a plaque that says they could be."""
    size = 32
    img = Image.new("RGBA", (size, size), TRANSPARENT)
    d = ImageDraw.Draw(img)
    d.rectangle([1, 1, size - 2, size - 2], fill=(30, 52, 68, 190))
    dash = (120, 154, 176, 255)
    for x in range(2, size - 2, 4):
        d.line([(x, 1), (min(x + 1, size - 3), 1)], fill=dash)
        d.line([(x, size - 2), (min(x + 1, size - 3), size - 2)], fill=dash)
    for y in range(2, size - 2, 4):
        d.line([(1, y), (1, min(y + 1, size - 3))], fill=dash)
        d.line([(size - 2, y), (size - 2, min(y + 1, size - 3))], fill=dash)
    return img


def make_phase_plate() -> Image.Image:
    """32x32 / border 10 brass banner for the six phase headers."""
    size = 32
    img = Image.new("RGBA", (size, size), TRANSPARENT)
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, size - 1, size - 1], fill=BRASS_DEEP, outline=OUTLINE)
    bevel(d, (0, 0, size - 1, size - 1), BRASS_LIGHT, (60, 42, 16, 255), inset=1)
    d.rectangle([3, 3, size - 4, size - 4], fill=BRASS_DARK)
    d.line([(3, 3), (size - 4, 3)], fill=BRASS)
    # Two rules under the top edge, the way a title plate is engraved.
    d.line([(6, 6), (size - 7, 6)], fill=(60, 42, 16, 255))
    d.line([(6, size - 7), (size - 7, size - 7)], fill=BRASS)
    rivets(d, size, (60, 42, 16, 255), BRASS_LIGHT)
    return img


# --------------------------------------------------------------------------------------
# Kind badges. 16x16, drawn near-white where they are meant to be tinted by the panel.
# --------------------------------------------------------------------------------------


def _badge() -> tuple:
    size = 16
    img = Image.new("RGBA", (size, size), TRANSPARENT)
    return img, ImageDraw.Draw(img), size


def make_badge_cog() -> Image.Image:
    """Chassis. A cog -- the game's own emblem for a machine frame."""
    img, d, size = _badge()
    d.ellipse([3, 3, size - 4, size - 4], fill=BRASS, outline=OUTLINE)
    for x, y in ((7, 1), (7, size - 3), (1, 7), (size - 3, 7)):
        d.rectangle([x, y, x + 1, y + 1], fill=BRASS)
    d.ellipse([6, 6, size - 7, size - 7], fill=MAHOGANY_DARK)
    d.point((6, 6), fill=BRASS_LIGHT)
    return img


def make_badge_crucible() -> Image.Image:
    """Recipe. A crucible pouring -- a conversion, which is what a recipe is."""
    img, d, size = _badge()
    d.polygon([(3, 4), (size - 4, 4), (size - 6, 11), (5, 11)], fill=IRON_LIGHT, outline=OUTLINE)
    d.line([(4, 5), (size - 5, 5)], fill=(150, 146, 140, 255))
    d.rectangle([6, 12, size - 7, 13], fill=(214, 122, 48, 255))
    d.rectangle([7, 8, size - 8, 10], fill=(232, 158, 62, 255))
    return img


def make_badge_anvil() -> Image.Image:
    """Building. An anvil silhouette: a thing that is placed and stays put."""
    img, d, size = _badge()
    d.rectangle([2, 5, size - 3, 8], fill=IRON, outline=OUTLINE)
    d.polygon([(size - 3, 5), (size - 1, 6), (size - 3, 8)], fill=IRON, outline=OUTLINE)
    d.rectangle([6, 9, 9, 11], fill=IRON_DARK)
    d.rectangle([4, 12, size - 5, size - 3], fill=IRON, outline=OUTLINE)
    d.line([(3, 6), (size - 4, 6)], fill=IRON_LIGHT)
    return img


def make_badge_hand() -> Image.Image:
    """Technique. A crank handle -- a way of working rather than a thing to build."""
    img, d, size = _badge()
    d.ellipse([5, 5, size - 6, size - 6], outline=BRASS, width=2)
    d.line([(8, 8), (size - 3, 3)], fill=BRASS)
    d.rectangle([size - 5, 2, size - 3, 4], fill=BRASS_LIGHT, outline=OUTLINE)
    d.point((8, 8), fill=BRASS_LIGHT)
    return img


def make_badge_tower() -> Image.Image:
    """Milestone. The Clock Tower's own silhouette, since every milestone node is one of
    its four stages or the win."""
    img, d, size = _badge()
    d.polygon([(4, size - 2), (4, 5), (7, 2), (10, 5), (10, size - 2)], fill=IRON, outline=OUTLINE)
    d.ellipse([5, 6, 9, 10], fill=PARCHMENT, outline=OUTLINE)
    d.line([(7, 8), (7, 7)], fill=OUTLINE)
    d.line([(7, 8), (8, 8)], fill=OUTLINE)
    return img


# --------------------------------------------------------------------------------------
# Prerequisite lines.
# --------------------------------------------------------------------------------------


def make_line_h() -> Image.Image:
    """8x2 dashed rule, tiled horizontally. Dashed rather than solid because a chart with
    sixty solid rules on it reads as a grid; dashes read as routes."""
    img = Image.new("RGBA", (8, 2), TRANSPARENT)
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, 4, 1], fill=(226, 226, 226, 255))
    return img


def make_line_v() -> Image.Image:
    """2x8 dashed rule, tiled vertically."""
    img = Image.new("RGBA", (2, 8), TRANSPARENT)
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, 1, 4], fill=(226, 226, 226, 255))
    return img


def main() -> None:
    save(make_field(), "tt_field.png")
    save(make_node_locked(), "tt_node_locked.png")
    save(make_node_available(), "tt_node_available.png")
    save(make_node_researched(), "tt_node_researched.png")
    save(make_node_planned(), "tt_node_planned.png")
    save(make_phase_plate(), "tt_phase_plate.png")
    save(make_badge_cog(), "tt_badge_cog.png")
    save(make_badge_crucible(), "tt_badge_crucible.png")
    save(make_badge_anvil(), "tt_badge_anvil.png")
    save(make_badge_hand(), "tt_badge_hand.png")
    save(make_badge_tower(), "tt_badge_tower.png")
    save(make_line_h(), "tt_line_h.png")
    save(make_line_v(), "tt_line_v.png")


if __name__ == "__main__":
    main()
