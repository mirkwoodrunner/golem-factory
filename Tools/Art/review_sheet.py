"""The sheet the user reviews before a set is committed (docs/art-pipeline-design.md §2 step 10,
§5.8; art-style-bible.md invariant 16).

    python Tools/Art/review_sheet.py --set S1 --frames <file ...>
        --before public/images/<set>/before/ --after public/images/<set>/after/
        --out public/images/<set>/ [--game-before <dir>] [--game-after <dir>]
        [--made-from <json>] [--mirrored-from <json>]

One row per frame, numbered from 1 in --frames order:
- the before and the after frame at 4x pixel view, on a floor tile;
- for a mirrored frame (a west frame, or one --mirrored-from names), the plain flip of its
  source too, so a re-shade gone wrong shows;
- from the `art` scenario's captures, the frame in the game at the default, closest and widest
  zooms, before and after: <stem>-default.png, -close.png and -wide.png in --game-before and
  --game-after (a missing one is drawn as an empty, labelled cell).
A frame with no before (a facing or pose that didn't exist) shows the frame it was made from,
labelled so: --made-from's entry, else the chassis's south base by name.

Pages of at most 28 rows (one chassis) go to review-<page>.png, and review.json maps every
number to its file, so "12 and 31 look wrong" maps back without guessing. Labels use Pillow's
default font: the sheet is for the user, not the game. It refuses an --out under godot/art/.
"""
import argparse
import json
import os
import re
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from art_paths import DEFAULT_ROOT, writes_into_godot_art  # noqa: E402

ROWS_PER_PAGE = 28
ZOOM = 4
CELL = (64 * ZOOM, 96 * ZOOM)
GAME_CELL = (160, 160)
LABEL_W = 220
PAD = 6
BG = (24, 20, 18)
INK = (230, 220, 200)
DIM = (150, 140, 125)
ZOOMS = ("default", "close", "wide")
FRAME_RE = re.compile(r"^(?P<stem>.+?)_(?:base|idle_\d+|work_\d+|stall|carry)_(?P<facing>north|east|south|west)\.png$")
FLOOR = os.path.join(DEFAULT_ROOT, "floor_tile.png")


class SheetError(Exception):
    pass


def numbering(frames):
    """{number: file}, numbered from 1 in the order given."""
    return {i + 1: name for i, name in enumerate(frames)}


def pages(frames, per_page=ROWS_PER_PAGE):
    return [frames[i:i + per_page] for i in range(0, len(frames), per_page)]


def made_from(name, explicit):
    """What a frame with no before was made from: the explicit map, else the south base."""
    if name in explicit:
        return explicit[name]
    match = FRAME_RE.match(name)
    return match.group("stem") + ".png" if match else None


def mirror_source(name, explicit):
    if name in explicit:
        return explicit[name]
    match = FRAME_RE.match(name)
    if match and match.group("facing") == "west":
        return name[: -len("_west.png")] + "_east.png"
    return None


def _floor(size):
    tile = Image.open(FLOOR).convert("RGBA") if os.path.exists(FLOOR) else Image.new("RGBA", (64, 64), (54, 40, 30, 255))
    tile = tile.resize((tile.width * ZOOM, tile.height * ZOOM), Image.NEAREST)
    out = Image.new("RGBA", size)
    for y in range(0, size[1], tile.height):
        for x in range(0, size[0], tile.width):
            out.paste(tile, (x, y))
    return out


def _sprite_cell(path, flip=False):
    cell = _floor(CELL)
    if path and os.path.exists(path):
        sprite = Image.open(path).convert("RGBA")
        if flip:
            sprite = sprite.transpose(Image.FLIP_LEFT_RIGHT)
        big = sprite.resize((sprite.width * ZOOM, sprite.height * ZOOM), Image.NEAREST)
        # Feet on the cell's bottom, centred, as the game stands it.
        cell.alpha_composite(big, ((CELL[0] - big.width) // 2, CELL[1] - big.height))
    return cell


def _game_cell(path):
    cell = Image.new("RGBA", GAME_CELL, (40, 34, 30, 255))
    if path and os.path.exists(path):
        shot = Image.open(path).convert("RGBA")
        shot.thumbnail(GAME_CELL, Image.NEAREST)
        cell.alpha_composite(shot, ((GAME_CELL[0] - shot.width) // 2, (GAME_CELL[1] - shot.height) // 2))
    return cell


def build(frames, before, after, game_before=None, game_after=None, made=None, mirrored=None, title="", start=1):
    """One page: a list of frame names drawn as numbered rows. Returns the image."""
    made, mirrored = made or {}, mirrored or {}
    font = ImageFont.load_default()
    width = LABEL_W + 3 * (CELL[0] + PAD) + 2 * len(ZOOMS) * (GAME_CELL[0] + PAD) + PAD
    row_h = CELL[1] + PAD + 14
    sheet = Image.new("RGBA", (width, 40 + row_h * len(frames)), BG + (255,))
    draw = ImageDraw.Draw(sheet)
    draw.text((PAD, 8), title, fill=INK, font=font)
    headers = ["before 4x", "after 4x", "plain flip"] + [f"{w} {z}" for w in ("before", "after") for z in ZOOMS]
    for i, (number, name) in enumerate(zip(range(start, start + len(frames)), frames)):
        y = 40 + i * row_h
        draw.text((PAD, y), f"{number}", fill=INK, font=font)
        draw.text((PAD, y + 16), name, fill=INK, font=font)
        before_path = os.path.join(before, name)
        x = LABEL_W
        if os.path.exists(before_path):
            sheet.alpha_composite(_sprite_cell(before_path), (x, y))
            draw.text((x, y + CELL[1] + 1), headers[0], fill=DIM, font=font)
        else:
            source = made_from(name, made)
            sheet.alpha_composite(_sprite_cell(os.path.join(before, source) if source else None), (x, y))
            draw.text((x, y + CELL[1] + 1), f"no before; made from {source}", fill=DIM, font=font)
        x += CELL[0] + PAD
        sheet.alpha_composite(_sprite_cell(os.path.join(after, name)), (x, y))
        draw.text((x, y + CELL[1] + 1), headers[1], fill=DIM, font=font)
        x += CELL[0] + PAD
        source = mirror_source(name, mirrored)
        if source:
            sheet.alpha_composite(_sprite_cell(os.path.join(after, source), flip=True), (x, y))
            draw.text((x, y + CELL[1] + 1), f"plain flip of {source}", fill=DIM, font=font)
        x += CELL[0] + PAD
        stem = name[:-4]
        for label, folder in (("before", game_before), ("after", game_after)):
            for zoom in ZOOMS:
                path = os.path.join(folder, f"{stem}-{zoom}.png") if folder else None
                sheet.alpha_composite(_game_cell(path), (x, y))
                caption = f"{label} {zoom}" if path and os.path.exists(path) else f"{label} {zoom}: no capture"
                draw.text((x, y + GAME_CELL[1] + 1), caption, fill=DIM, font=font)
                x += GAME_CELL[0] + PAD
    return sheet


def write(set_name, frames, before, after, out, game_before=None, game_after=None, made=None, mirrored=None):
    """Writes review-<page>.png and review.json; returns their paths."""
    if writes_into_godot_art(out):
        raise SheetError("--out is under godot/art/; review sheets go in public/images/<set>/")
    if len(set(frames)) != len(frames):
        raise SheetError("a frame is listed twice")
    os.makedirs(out, exist_ok=True)
    numbers = numbering(frames)
    written, page_of = [], {}
    for p, chunk in enumerate(pages(frames), start=1):
        first = (p - 1) * ROWS_PER_PAGE + 1
        image = build(chunk, before, after, game_before, game_after, made, mirrored,
                      title=f"{set_name} review, page {p}: frames {first}-{first + len(chunk) - 1}", start=first)
        path = os.path.join(out, f"review-{p}.png")
        image.convert("RGB").save(path)
        written.append(path)
        for n in range(first, first + len(chunk)):
            page_of[n] = p
    review = {"set": set_name,
              "pages": [os.path.basename(p) for p in written],
              "frames": {str(n): {"file": f, "page": page_of[n]} for n, f in numbers.items()}}
    path = os.path.join(out, "review.json")
    with open(path, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(review, handle, indent=2)
        handle.write("\n")
    return written + [path]


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--set", required=True)
    parser.add_argument("--frames", nargs="+", required=True)
    parser.add_argument("--before", required=True)
    parser.add_argument("--after", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--game-before")
    parser.add_argument("--game-after")
    parser.add_argument("--made-from")
    parser.add_argument("--mirrored-from")
    args = parser.parse_args(argv)
    made = json.load(open(args.made_from, encoding="utf-8")) if args.made_from else {}
    mirrored = json.load(open(args.mirrored_from, encoding="utf-8")) if args.mirrored_from else {}
    frames = [os.path.basename(f) for f in args.frames]
    try:
        paths = write(args.set, frames, args.before, args.after, args.out, args.game_before, args.game_after, made, mirrored)
    except SheetError as error:
        print(f"refused: {error}", file=sys.stderr)
        return 2
    for path in paths:
        print(f"wrote {path}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
