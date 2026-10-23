"""A compact review grid: every frame at 4x on the floor, 4 facing rows x 7 state columns,
numbered as review.json numbers them (#1-28), with today's sprite beside each row.

    python grid.py <frames dir> <out.png>
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", "..", "..", "..", ".."))
STEM = "chassis_brass_presser"
STATES = ["base", "idle_1", "work_0", "work_1", "work_2", "stall", "carry"]
FACINGS = ["south", "north", "east", "west"]
Z = 4
CELL = (64 * Z, 96 * Z)
PAD, LABEL = 8, 18
BG, INK, DIM = (24, 20, 18), (230, 220, 200), (150, 140, 125)


def name(state, facing):
    return STEM + ".png" if (state, facing) == ("base", "south") else f"{STEM}_{state}_{facing}.png"


def cell(path):
    floor = Image.open(os.path.join(REPO, "godot", "art", "floor_tile.png")).convert("RGBA").resize((64 * Z, 64 * Z), Image.NEAREST)
    out = Image.new("RGBA", CELL)
    for y in range(0, CELL[1], floor.height):
        out.paste(floor, (0, y))
    sprite = Image.open(path).convert("RGBA")
    big = sprite.resize((sprite.width * Z, sprite.height * Z), Image.NEAREST)
    out.alpha_composite(big, ((CELL[0] - big.width) // 2, CELL[1] - big.height))
    return out


def main(frames, out):
    font = ImageFont.load_default()
    cols = 1 + len(STATES)
    sheet = Image.new("RGBA", (PAD + cols * (CELL[0] + PAD), 30 + len(FACINGS) * (CELL[1] + LABEL + PAD)), BG + (255,))
    draw = ImageDraw.Draw(sheet)
    draw.text((PAD, 8), "S1 Brass Presser (creature) - #numbers are review.json's; left column: today's sprite", fill=INK, font=font)
    today = os.path.join(REPO, "godot", "art", STEM + ".png")
    number = 1
    for r, facing in enumerate(FACINGS):
        y = 30 + r * (CELL[1] + LABEL + PAD)
        sheet.alpha_composite(cell(today), (PAD, y))
        draw.text((PAD, y + CELL[1] + 2), f"today ({facing}: none drawn)" if facing != "south" else "today", fill=DIM, font=font)
        for c, state in enumerate(STATES):
            x = PAD + (c + 1) * (CELL[0] + PAD)
            sheet.alpha_composite(cell(os.path.join(frames, name(state, facing))), (x, y))
            draw.text((x, y + CELL[1] + 2), f"#{number}  {state} {facing}", fill=INK, font=font)
            number += 1
    sheet.convert("RGB").save(out)
    print("wrote", out, sheet.size)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
