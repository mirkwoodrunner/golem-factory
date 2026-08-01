#!/usr/bin/env python3
"""Generates the Workbench screen's mahogany-and-brass UI chrome (see
docs/digital-design.md, "The Programming Interface"). Companion to
generate_placeholder_art.py -- same free/Pillow-only approach, but these are 9-sliced /
tiled *UI* sprites rather than world art, so they live under
Assets/_Project/Art/UI/Workbench/ next to the third-party Steampunk pack.

Every panel/plate/card sprite is authored with an explicit 9-slice border and is meant to
be imported with that border (see the "Workbench UI reskin" notes in
docs/unity-implementation-plan.md); the big backdrops are drawn to tile seamlessly in
their centre region so Unity's Image.Type.Tiled gives real grain across a full-height
column instead of one stretched flat block.

Re-run to regenerate. Requires Pillow.
"""

import os
import random

from PIL import Image, ImageDraw

OUT_DIR = os.path.join(
    os.path.dirname(__file__), "..", "..", "Assets", "_Project", "Art", "UI", "Workbench"
)

# Mahogany-and-brass palette, extending generate_placeholder_art.py's warm wood/brass set.
MAHOGANY_DEEP = (46, 24, 18, 255)
MAHOGANY_DARK = (66, 36, 25, 255)
MAHOGANY = (92, 51, 34, 255)
MAHOGANY_LIGHT = (122, 70, 45, 255)
BRASS = (196, 149, 68, 255)
BRASS_LIGHT = (238, 200, 116, 255)
BRASS_DARK = (124, 90, 38, 255)
IRON = (74, 70, 66, 255)
IRON_DARK = (40, 38, 36, 255)
IRON_LIGHT = (116, 112, 106, 255)
BLUEPRINT = (26, 46, 62, 255)
BLUEPRINT_LIGHT = (38, 64, 84, 255)
PARCHMENT = (219, 203, 165, 255)
PARCHMENT_DARK = (176, 160, 124, 255)
OUTLINE = (24, 14, 10, 255)
TRANSPARENT = (0, 0, 0, 0)


def save(img: Image.Image, name: str) -> None:
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, name)
    img.save(path)
    print(f"wrote {path} ({img.width}x{img.height})")


def bevel(d: ImageDraw.ImageDraw, box, light, dark, inset=0, width=1) -> None:
    """Draws a raised bevel: `light` along top/left, `dark` along bottom/right."""
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


def grain(img: Image.Image, box, base, streak_dark, streak_light, seed) -> None:
    """Vertical wood grain that wraps horizontally, so Image.Type.Tiled has no seam."""
    rng = random.Random(seed)
    d = ImageDraw.Draw(img)
    x0, y0, x1, y1 = box
    d.rectangle([x0, y0, x1, y1], fill=base)
    width = x1 - x0 + 1
    for _ in range(width):
        x = x0 + rng.randrange(width)
        colour = streak_dark if rng.random() < 0.55 else streak_light
        top = y0 + rng.randrange(0, max(1, (y1 - y0) // 2))
        length = rng.randrange(2, max(3, (y1 - y0)))
        d.line([(x, top), (x, min(y1, top + length))], fill=colour)
    # Knots: a couple of short horizontal ticks to break up the verticals.
    for _ in range(max(1, width // 8)):
        x = x0 + rng.randrange(width - 1)
        y = y0 + rng.randrange(y1 - y0 + 1)
        d.line([(x, y), (x + 1, y)], fill=streak_dark)


def framed_panel(base, grain_dark, grain_light, frame, frame_light, frame_dark, seed):
    """48x48 / border 12 panel: outlined bevelled frame around a tiling grain centre."""
    size = 48
    img = Image.new("RGBA", (size, size), TRANSPARENT)
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, size - 1, size - 1], fill=frame)
    d.rectangle([0, 0, size - 1, size - 1], outline=OUTLINE)
    bevel(d, (0, 0, size - 1, size - 1), frame_light, frame_dark, inset=1)
    # Recessed lip just outside the centre.
    d.rectangle([9, 9, size - 10, size - 10], outline=frame_dark)
    d.rectangle([10, 10, size - 11, size - 11], outline=OUTLINE)
    grain(img, (11, 11, size - 12, size - 12), base, grain_dark, grain_light, seed)
    # Corner rivets, kept inside the 12px border so 9-slicing shows one per corner.
    for cx, cy in ((5, 5), (size - 6, 5), (5, size - 6), (size - 6, size - 6)):
        d.rectangle([cx - 1, cy - 1, cx + 1, cy + 1], fill=BRASS)
        d.point((cx - 1, cy - 1), fill=BRASS_LIGHT)
        d.point((cx + 1, cy + 1), fill=BRASS_DARK)
    return img


def make_panel_mahogany() -> Image.Image:
    return framed_panel(
        MAHOGANY, MAHOGANY_DARK, MAHOGANY_LIGHT, MAHOGANY_DARK, MAHOGANY_LIGHT, MAHOGANY_DEEP, seed=11
    )


def make_panel_iron() -> Image.Image:
    return framed_panel(IRON, IRON_DARK, IRON_LIGHT, IRON_DARK, IRON_LIGHT, (26, 24, 22, 255), seed=23)


def make_panel_blueprint() -> Image.Image:
    """The blueprint field: dark drafting-paper blue with a faint graticule that tiles."""
    size = 48
    img = Image.new("RGBA", (size, size), TRANSPARENT)
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, size - 1, size - 1], fill=MAHOGANY_DARK)
    d.rectangle([0, 0, size - 1, size - 1], outline=OUTLINE)
    bevel(d, (0, 0, size - 1, size - 1), MAHOGANY_LIGHT, MAHOGANY_DEEP, inset=1)
    d.rectangle([9, 9, size - 10, size - 10], outline=BRASS_DARK)
    d.rectangle([10, 10, size - 11, size - 11], fill=BLUEPRINT, outline=OUTLINE)
    # 8px graticule inside the 24x24 tiling centre (11..36) so the grid stays continuous.
    for i in range(11, size - 11, 8):
        d.line([(i, 11), (i, size - 12)], fill=BLUEPRINT_LIGHT)
        d.line([(11, i), (size - 12, i)], fill=BLUEPRINT_LIGHT)
    return img


def make_plate_brass() -> Image.Image:
    """32x32 / border 10 brass title plate with corner rivets."""
    size = 32
    img = Image.new("RGBA", (size, size), TRANSPARENT)
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, size - 1, size - 1], fill=BRASS_DARK, outline=OUTLINE)
    bevel(d, (0, 0, size - 1, size - 1), BRASS_LIGHT, (86, 60, 24, 255), inset=1)
    d.rectangle([3, 3, size - 4, size - 4], fill=BRASS)
    d.line([(3, 3), (size - 4, 3)], fill=BRASS_LIGHT)
    d.line([(3, size - 4), (size - 4, size - 4)], fill=BRASS_DARK)
    for cx, cy in ((5, 5), (size - 6, 5), (5, size - 6), (size - 6, size - 6)):
        d.rectangle([cx - 1, cy - 1, cx + 1, cy + 1], fill=(86, 60, 24, 255))
        d.point((cx, cy), fill=BRASS_LIGHT)
    return img


def make_card_face() -> Image.Image:
    """32x32 / border 10 punch-card face. Deliberately near-white so WorkbenchController's
    teal/copper Image.color tint is what carries the colour coding, unchanged."""
    size = 32
    img = Image.new("RGBA", (size, size), TRANSPARENT)
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, size - 1, size - 1], fill=(232, 232, 232, 255), outline=(30, 30, 30, 255))
    bevel(d, (0, 0, size - 1, size - 1), (255, 255, 255, 255), (128, 128, 128, 255), inset=1)
    d.rectangle([3, 3, size - 4, size - 4], fill=(214, 214, 214, 255))
    # Punch holes down the left border region (inside the 10px slice, so exactly one column).
    for y in (7, 12, 17, 20, 25):
        d.rectangle([5, y, 7, y + 1], fill=(120, 120, 120, 255))
    # Brass tab along the right border region.
    d.rectangle([size - 8, 4, size - 5, size - 5], fill=(184, 184, 184, 255))
    return img


def make_slot_socket() -> Image.Image:
    """32x32 / border 10 empty slot: a recessed dark socket with an inner keyline the
    controller tints teal (Logic Core) or copper (Appendage)."""
    size = 32
    img = Image.new("RGBA", (size, size), TRANSPARENT)
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, size - 1, size - 1], fill=(38, 30, 26, 255), outline=OUTLINE)
    bevel(d, (0, 0, size - 1, size - 1), (18, 12, 10, 255), (108, 82, 60, 255), inset=1)
    d.rectangle([3, 3, size - 4, size - 4], fill=(30, 24, 20, 255))
    # Dashed inner keyline, drawn in white so the Image tint sets its colour.
    for x in range(5, size - 5, 4):
        d.line([(x, 5), (min(x + 1, size - 6), 5)], fill=(226, 226, 226, 255))
        d.line([(x, size - 6), (min(x + 1, size - 6), size - 6)], fill=(226, 226, 226, 255))
    for y in range(5, size - 5, 4):
        d.line([(5, y), (5, min(y + 1, size - 6))], fill=(226, 226, 226, 255))
        d.line([(size - 6, y), (size - 6, min(y + 1, size - 6))], fill=(226, 226, 226, 255))
    return img


def make_tape() -> Image.Image:
    """32x64 punched paper tape, drawn to tile horizontally at native height for the
    diagnostic ticker (Image.Type.Tiled, no 9-slice border)."""
    w, h = 32, 64
    img = Image.new("RGBA", (w, h), TRANSPARENT)
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, w - 1, h - 1], fill=PARCHMENT)
    d.line([(0, 0), (w - 1, 0)], fill=PARCHMENT_DARK)
    d.line([(0, h - 1), (w - 1, h - 1)], fill=PARCHMENT_DARK)
    d.line([(0, 1), (w - 1, 1)], fill=(238, 226, 194, 255))
    # Sprocket holes along both edges, spaced so the 32px tile repeats cleanly.
    for x in (7, 23):
        for y in (6, h - 9):
            d.rectangle([x, y, x + 2, y + 2], fill=(96, 84, 60, 255))
    # Faint centre rule so long tape reads as one continuous strip.
    d.line([(0, h // 2), (w - 1, h // 2)], fill=(206, 190, 152, 255))
    return img


def make_lever_track() -> Image.Image:
    """40x120 iron lever housing. Fixed size (not 9-sliced) -- it's a single prop."""
    w, h = 40, 120
    img = Image.new("RGBA", (w, h), TRANSPARENT)
    d = ImageDraw.Draw(img)
    d.rectangle([4, 0, w - 5, h - 1], fill=IRON, outline=OUTLINE)
    bevel(d, (4, 0, w - 5, h - 1), IRON_LIGHT, IRON_DARK, inset=1)
    # Machined ridges, so the housing reads as metal rather than a flat dark box.
    for y in range(14, h - 12, 6):
        d.line([(6, y), (w - 7, y)], fill=IRON_DARK)
        d.line([(6, y + 1), (w - 7, y + 1)], fill=IRON_LIGHT)
    # The slot the handle rides in: one narrow recess, shaded on the left only so it
    # doesn't read as two parallel rails.
    d.rectangle([w // 2 - 2, 9, w // 2 + 1, h - 10], fill=(16, 14, 12, 255))
    d.line([(w // 2 - 2, 9), (w // 2 - 2, h - 10)], fill=(8, 7, 6, 255))
    d.line([(w // 2 + 2, 9), (w // 2 + 2, h - 10)], fill=IRON_LIGHT)
    # Brass mounting flanges + bolts.
    for y in (5, h - 6):
        d.rectangle([1, y - 4, w - 2, y + 3], fill=BRASS_DARK, outline=OUTLINE)
        d.line([(1, y - 4), (w - 2, y - 4)], fill=BRASS)
        for x in (5, w - 6):
            d.rectangle([x - 1, y - 1, x + 1, y + 1], fill=BRASS_LIGHT)
    # Detent ticks along the right rail.
    for y in range(18, h - 16, 12):
        d.line([(w - 8, y), (w - 6, y)], fill=BRASS_DARK)
    return img


def make_lever_handle() -> Image.Image:
    """34x30 brass handle: a knurled ball on a short arm."""
    w, h = 34, 30
    img = Image.new("RGBA", (w, h), TRANSPARENT)
    d = ImageDraw.Draw(img)
    # Pivot boss that sits in the track slot.
    d.rectangle([1, h // 2 - 5, 7, h // 2 + 4], fill=BRASS_DARK, outline=OUTLINE)
    d.line([(2, h // 2 - 4), (6, h // 2 - 4)], fill=BRASS_LIGHT)
    # Arm reaching right to the grip.
    d.rectangle([6, h // 2 - 3, w - 12, h // 2 + 2], fill=BRASS, outline=OUTLINE)
    d.line([(7, h // 2 - 2), (w - 13, h // 2 - 2)], fill=BRASS_LIGHT)
    # Ball grip.
    d.ellipse([w - 18, 1, w - 2, h - 2], fill=BRASS, outline=OUTLINE)
    d.ellipse([w - 16, 3, w - 9, h // 2 - 1], fill=BRASS_LIGHT)
    for y in range(6, h - 5, 4):
        d.line([(w - 15, y), (w - 5, y)], fill=BRASS_DARK)
    return img


def make_gauge_needle() -> Image.Image:
    """16x16 brass indicator pip used as the tape ticker's leading marker."""
    size = 16
    img = Image.new("RGBA", (size, size), TRANSPARENT)
    d = ImageDraw.Draw(img)
    d.polygon([(2, 2), (13, 8), (2, 14)], fill=BRASS, outline=OUTLINE)
    d.line([(3, 4), (10, 8)], fill=BRASS_LIGHT)
    return img


def main() -> None:
    save(make_panel_mahogany(), "wb_panel_mahogany.png")
    save(make_panel_iron(), "wb_panel_iron.png")
    save(make_panel_blueprint(), "wb_panel_blueprint.png")
    save(make_plate_brass(), "wb_plate_brass.png")
    save(make_card_face(), "wb_card_face.png")
    save(make_slot_socket(), "wb_slot_socket.png")
    save(make_tape(), "wb_tape.png")
    save(make_lever_track(), "wb_lever_track.png")
    save(make_lever_handle(), "wb_lever_handle.png")
    save(make_gauge_needle(), "wb_gauge_needle.png")


if __name__ == "__main__":
    main()
