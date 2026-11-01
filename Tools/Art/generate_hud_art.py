#!/usr/bin/env python3
"""Generates the HUD's shared chrome in godot/art/UI/Hud/ (art-pipeline-design.md section 4,
S6): the bolted and plain panels, the four buttons, and the Workbench header's gear. They
replaced the third-party Steampunk pack piece for piece, at the same sizes and 9-slice
margins, so no Ugui number moved (decision 22).

Drawn as pixel art in the Workbench's palette rather than redrawn in ComfyUI: they are small
and 9-sliced, where a redraw goes muddy (decisions 18-20). The code tints every piece
(SelfModulate or a stylebox modulate), so each keeps the pack's tone range: the panels a dark
brown, the button faces a light tan under a pale brass rim. The buttons carry no glyph or
lettering (art-style-bible.md invariant 12): the code draws "Close", "Save" and the rest in the
font, and a glyph in a 9-slice centre stretched under the words on a wide button.

Re-run to regenerate. Requires Pillow.
"""

import math
import os
import sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from art_paths import art_dir  # noqa: E402

from PIL import Image, ImageDraw

OUT_DIR = art_dir("UI", "Hud")

OUTLINE = (40, 24, 10, 255)
TRANSPARENT = (0, 0, 0, 0)
# The panels: the pack's dark brown, a step either side.
PANEL = (80, 44, 14, 255)
PANEL_LIGHT = (112, 68, 28, 255)
PANEL_DARK = (56, 30, 9, 255)
PANEL_DEEP = (38, 20, 6, 255)
# The buttons: the pack's tan face under a pale brass rim.
FACE = (198, 152, 102, 255)
FACE_LIGHT = (216, 174, 126, 255)
FACE_DARK = (166, 124, 80, 255)
RIM = (214, 200, 98, 255)
RIM_LIGHT = (240, 232, 150, 255)
RIM_DARK = (150, 128, 52, 255)
IRON = (123, 122, 121, 255)
IRON_LIGHT = (170, 168, 164, 255)
IRON_DARK = (82, 80, 78, 255)
# The gear: the pack's dull bronze, which the Workbench header darkens further.
GEAR = (98, 76, 44, 255)
GEAR_LIGHT = (134, 108, 66, 255)
GEAR_DARK = (68, 50, 26, 255)


def save(img: Image.Image, name: str) -> None:
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, name)
    img.save(path)
    print(f"wrote {path} ({img.width}x{img.height})")


def bevel(d: ImageDraw.ImageDraw, box, light, dark, width=1) -> None:
    """Raised bevel: `light` along top/left, `dark` along bottom/right."""
    x0, y0, x1, y1 = box
    for i in range(width):
        d.line([(x0 + i, y0 + i), (x1 - i, y0 + i)], fill=light)
        d.line([(x0 + i, y0 + i), (x0 + i, y1 - i)], fill=light)
        d.line([(x0 + i, y1 - i), (x1 - i, y1 - i)], fill=dark)
        d.line([(x1 - i, y0 + i), (x1 - i, y1 - i)], fill=dark)


def _panel(bolts: bool) -> Image.Image:
    """64x64. Bolts sit only in the corners, inside the 12x10 corner slices, so a stretched
    edge never smears one."""
    s = 64
    img = Image.new("RGBA", (s, s), PANEL)
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, s - 1, s - 1], outline=OUTLINE)
    bevel(d, (1, 1, s - 2, s - 2), PANEL_LIGHT, PANEL_DARK, width=2)
    # A groove inset from the edge: a dark line with a light lip under it.
    d.rectangle([5, 5, s - 6, s - 6], outline=PANEL_DEEP)
    d.line([(6, s - 5), (s - 5, s - 5)], fill=PANEL_LIGHT)
    d.line([(s - 5, 6), (s - 5, s - 5)], fill=PANEL_LIGHT)
    if bolts:
        for cx, cy in ((8, 7), (s - 9, 7), (8, s - 8), (s - 9, s - 8)):
            d.rectangle([cx - 1, cy - 1, cx + 1, cy + 1], fill=PANEL_DEEP)
            d.rectangle([cx - 1, cy - 1, cx, cy], fill=PANEL_LIGHT)
            d.point((cx + 1, cy + 1), fill=OUTLINE)
    return img


def make_panel_bolted() -> Image.Image:
    """hud_panel_bolted: 64x64, 9-slice 12/10/12/10. Menus, cards, the alerts strip, the guide."""
    return _panel(bolts=True)


def make_panel_plain() -> Image.Image:
    """hud_panel_plain: 64x64, 9-slice 8. Full-screen backgrounds and side panels."""
    return _panel(bolts=False)


def _button(rim, rim_light, rim_dark, stitched: bool) -> Image.Image:
    """34x34, 9-slice 8: a rounded tan face under a two-pixel rim. Everything that shapes it is
    inside the 8px border, so the centre is flat face and stretches cleanly."""
    s = 34
    img = Image.new("RGBA", (s, s), TRANSPARENT)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([0, 0, s - 1, s - 1], radius=5, fill=rim, outline=OUTLINE)
    d.rounded_rectangle([1, 1, s - 2, s - 2], radius=4, outline=rim_light)
    d.line([(4, s - 2), (s - 5, s - 2)], fill=rim_dark)
    d.line([(s - 2, 4), (s - 2, s - 5)], fill=rim_dark)
    d.rounded_rectangle([3, 3, s - 4, s - 4], radius=3, fill=FACE, outline=OUTLINE)
    # The face's own bevel: lit from the upper left.
    d.line([(6, 4), (s - 7, 4)], fill=FACE_LIGHT)
    d.line([(4, 6), (4, s - 7)], fill=FACE_LIGHT)
    d.line([(6, s - 5), (s - 7, s - 5)], fill=FACE_DARK)
    d.line([(s - 5, 6), (s - 5, s - 7)], fill=FACE_DARK)
    if stitched:
        # Stitching just inside the face, kept to the border so it repeats along the edges.
        for x in range(8, s - 8, 3):
            d.point((x, 6), fill=FACE_DARK)
            d.point((x, s - 7), fill=FACE_DARK)
        for y in range(8, s - 8, 3):
            d.point((6, y), fill=FACE_DARK)
            d.point((s - 7, y), fill=FACE_DARK)
    return img


def make_button() -> Image.Image:
    """hud_button: the plain button (menus, tabs, Load, Take all, the Workbench's chassis)."""
    return _button(RIM, RIM_LIGHT, RIM_DARK, stitched=False)


def make_button_active() -> Image.Image:
    """hud_button_active: an iron rim, so the selected row and Patent read as set apart."""
    return _button(IRON, IRON_LIGHT, IRON_DARK, stitched=True)


def make_button_accept() -> Image.Image:
    """hud_button_accept: Save. The plain button, stitched; the code tints it."""
    return _button(RIM, RIM_LIGHT, RIM_DARK, stitched=True)


def make_button_close() -> Image.Image:
    """hud_button_close: Close. A deeper rim than accept's; the code tints it red."""
    return _button(RIM_DARK, RIM, OUTLINE, stitched=True)


def _outline(img: Image.Image) -> None:
    """Ink every opaque pixel that touches a transparent one (4-neighbours, or the canvas
    edge), so the silhouette's outline is one pixel all round however its parts overlap."""
    w, h = img.size
    px = img.load()
    edge = [(x, y) for y in range(h) for x in range(w) if px[x, y][3]
            and any(not (0 <= nx < w and 0 <= ny < h) or not px[nx, ny][3]
                    for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))]
    for p in edge:
        px[p] = OUTLINE


def make_gear() -> Image.Image:
    """hud_gear: 58x59, the Workbench header's gear: ten teeth, six spokes, a hub."""
    w, h = 58, 59
    img = Image.new("RGBA", (w, h), TRANSPARENT)
    d = ImageDraw.Draw(img)
    cx, cy = 28.5, 29.0
    # The silhouette in one colour, then the outline round it, then the shading inside.
    for k in range(10):
        a = 2 * math.pi * k / 10
        d.polygon([(cx + r * math.cos(a + t), cy + r * math.sin(a + t))
                   for r, t in ((20, -0.20), (28, -0.12), (28, 0.12), (20, 0.20))], fill=GEAR)
    d.ellipse([cx - 23, cy - 23, cx + 23, cy + 23], fill=GEAR)
    d.ellipse([cx - 16, cy - 16, cx + 16, cy + 16], fill=TRANSPARENT)
    for k in range(6):
        a = 2 * math.pi * k / 6 + math.pi / 6
        d.line([(cx, cy), (cx + 17 * math.cos(a), cy + 17 * math.sin(a))], fill=GEAR, width=5)
    d.ellipse([cx - 7, cy - 7, cx + 7, cy + 7], fill=GEAR)
    d.ellipse([cx - 3, cy - 3, cx + 3, cy + 3], fill=TRANSPARENT)
    _outline(img)
    d.arc([cx - 21, cy - 21, cx + 21, cy + 21], 160, 290, fill=GEAR_LIGHT, width=2)
    d.arc([cx - 21, cy - 21, cx + 21, cy + 21], 340, 110, fill=GEAR_DARK, width=2)
    d.arc([cx - 6, cy - 6, cx + 6, cy + 6], 160, 290, fill=GEAR_LIGHT, width=2)
    return img


def main() -> None:
    save(make_panel_bolted(), "hud_panel_bolted.png")
    save(make_panel_plain(), "hud_panel_plain.png")
    save(make_button(), "hud_button.png")
    save(make_button_active(), "hud_button_active.png")
    save(make_button_accept(), "hud_button_accept.png")
    save(make_button_close(), "hud_button_close.png")
    save(make_gear(), "hud_gear.png")


if __name__ == "__main__":
    main()
