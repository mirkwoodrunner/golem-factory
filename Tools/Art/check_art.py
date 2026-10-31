"""Checks every sprite against Tools/Art/art_manifest.json and the style bible.

    python Tools/Art/check_art.py               # the whole manifest; exit 1 on any FAIL
    python Tools/Art/check_art.py <file> ...    # only those files (paths under godot/art/)
    python Tools/Art/check_art.py --write       # refresh the manifest's mechanical fields

docs/art-pipeline-design.md §5.1-§5.2 specify the manifest and the sixteen checks;
docs/art-style-bible.md says why each number is what it is. One line per finding:

    FAIL  [n] <file>: <message>     breaks the build (exit 1)
    WARN  [n] <file>: <message>     what the art would fail once its set is replaced
    INFO  [n] <file>: <message>     a measurement worth seeing

A check that only binds a replaced set (`palette` other than `legacy` in the manifest) is a
FAIL there and a WARN on art not yet replaced, so today's art never fails the build but the
report says what it would fail (art-pipeline-design.md §5.2, "Checks 8-12 run on the current
art too and report its baseline as warnings while the set is legacy"). Checks 1-3, 12, 15 and 16
bind everything: completeness, sizes, feet, copper ore and the bookkeeping are invariants now.

The manifest's mechanical fields (`size`, `footRow`) are rewritten from the files by --write;
everything else in it is kept by hand.
"""
import json
import os
import re
import sys
from collections import Counter
from itertools import combinations

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import art_metrics as m  # noqa: E402

REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
ART_ROOT = os.path.join(REPO, "godot", "art")
MANIFEST_PATH = os.path.join(HERE, "art_manifest.json")
CHASSIS_JSON = os.path.join(REPO, "godot", "data", "chassis.json")
MOOD_PALETTE_CS = os.path.join(REPO, "godot", "Core", "Golems", "GolemMoodPalette.cs")
LIGHTING_RULES_CS = os.path.join(REPO, "godot", "Core", "World", "LightingRules.cs")
ART_CLAUDE_MD = os.path.join(HERE, "CLAUDE.md")

# The numbers of art-style-bible.md, in one place.
CHASSIS_MAX = (64, 96)
SILHOUETTE_MAX_IOU = 0.50          # §5.1, per facing
BODY_MIN_LIGHTNESS = 0.40          # §5.1
STALLED_MIN_SHIFT = 0.10           # §5.1
STOPPED_TINTS_MIN_APART = 0.06     # §5.1
GOODS_MAX_IOU = 0.80               # §5.4
GOODS_MIN_COLOUR_APART = 0.15      # §5.4
GOODS_MIN_TONES = 3                # §5.4
EDGE_LIGHT_MIN = 0.02              # §5.1
MIRROR_MIN_RATIO = 0.8             # §5.1
COPPER_ORE_HUE = (100.0, 160.0)    # check 12, invariant 6
WOOD_HUE = (0.0, 60.0)             # the carts' wood and ironwork: not ore
FACINGS = ("north", "east", "south", "west")
# GolemFrames.FileName's states (art-style-bible.md §4.3): 7 files a facing.
FRAME_STATES = ("base", "idle_1", "work_0", "work_1", "work_2", "stall", "carry")
FRAME_FILES_PER_CHASSIS = len(FRAME_STATES) * len(FACINGS)
ARTIFICER_FRAMES = 36         # 16 walk frames, 4 standing poses, 16 cranking frames
MOOD_TINTS_CHECKED = ("Stalled", "Starved", "Waiting")

LEGACY = "legacy"


# --- the manifest ------------------------------------------------------------------------------

def load_manifest(path=MANIFEST_PATH):
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)


def replaced(entry):
    """Whether the entry's set has been replaced: its palette is `bible` or a list of ramps."""
    return entry.get("palette", LEGACY) != LEGACY


def foot_row(entry, size):
    """The row the contact line is on (art-pipeline-design.md §5.1): the bottom row for
    `bottom-center`; for a fractional pivot whose y is below the middle, the row just under the
    pivot line (row 117 of a 120-high cart at 3/120). None where the contact line isn't a row
    (centred tiles, hanging edges, side walls, UI)."""
    w, h = size
    pivot = entry.get("pivot")
    if pivot == "bottom-center":
        return h - 1
    if isinstance(pivot, list) and pivot[1] < 0.5:
        return min(h - 1, h - int(round(pivot[1] * h)))
    return None


def frame_name(stem, state, facing):
    """GolemFrames.FileName, mirrored: the south base is <stem>.png."""
    if state == "base" and facing == "south":
        return stem + ".png"
    return f"{stem}_{state}_{facing}.png"


def write_mechanical(manifest, art_root=ART_ROOT):
    """--write: size and footRow from the files; every other field is left as it is."""
    order = ["set", "kind", "size", "pivot", "footRow", "owner", "palette"]
    out = {}
    for rel in sorted(manifest):
        entry = dict(manifest[rel])
        path = os.path.join(art_root, rel)
        if os.path.exists(path):
            with Image.open(path) as image:
                entry["size"] = list(image.size)
            entry["footRow"] = foot_row(entry, entry["size"])
        out[rel] = {k: entry[k] for k in order if k in entry}
        out[rel].update({k: v for k, v in entry.items() if k not in order})
    return out


# --- context: what the checks read besides the art --------------------------------------------

class Context:
    """Everything a check reads that isn't a sprite, so a test can hand in its own."""

    def __init__(self, palette, chassis_stems, tints, ambient=(1.0, 1.0, 1.0),
                 known_hand_replaced=frozenset(), claude_text=""):
        self.palette = palette
        self.chassis_stems = list(chassis_stems)
        self.tints = tints
        self.ambient = ambient
        self.known_hand_replaced = set(known_hand_replaced)
        self.claude_text = claude_text

    @classmethod
    def from_repo(cls):
        with open(CHASSIS_JSON, encoding="utf-8") as handle:
            chassis = json.load(handle)
        stems = [d["chassisSprite"][:-4] if d["chassisSprite"].endswith(".png") else d["chassisSprite"]
                 for d in chassis.values() if d.get("chassisSprite")]
        with open(MOOD_PALETTE_CS, encoding="utf-8") as handle:
            tints = read_tints(handle.read())
        ambient = (1.0, 1.0, 1.0)
        if os.path.exists(LIGHTING_RULES_CS):
            with open(LIGHTING_RULES_CS, encoding="utf-8") as handle:
                ambient = read_ambient(handle.read()) or ambient
        import verify_art
        with open(ART_CLAUDE_MD, encoding="utf-8") as handle:
            claude = handle.read()
        return cls(m.Palette.load(), stems, tints, ambient, verify_art.KNOWN_HAND_REPLACED, claude)


_COLOR = r"new Color\(\s*([\d.]+)f\s*,\s*([\d.]+)f\s*,\s*([\d.]+)f"


def read_tints(source):
    """GolemMoodPalette.Tint, read from the C# so the check can't drift from the code: each
    `*Tint` field's colour, and which moods' `case` labels return it. A mood that falls to the
    default (Working, Straining) is white."""
    fields = {name: tuple(float(v) for v in rgb)
              for name, *rgb in re.findall(r"(\w+Tint)\s*=\s*" + _COLOR, source)}
    body = source[source.index("public static Color Tint("):]
    body = body[:body.index("// --- Badge")] if "// --- Badge" in body else body
    tints, pending = {}, []
    for token in re.finditer(r"case GolemMood\.(\w+):|return (\w+);", body):
        if token.group(1):
            pending.append(token.group(1))
        elif token.group(2):
            for mood in pending:
                tints[mood] = fields.get(token.group(2), (1.0, 1.0, 1.0))
            pending = []
    return tints


def read_ambient(source):
    match = re.search(r"Ambient\b[^;]*?" + _COLOR, source)
    return tuple(float(v) for v in match.groups()) if match else None


# --- findings ----------------------------------------------------------------------------------

class Report:
    def __init__(self):
        self.findings = []

    def add(self, level, check, rel, message):
        self.findings.append((level, check, rel, message))

    def bound(self, binding, check, rel, message):
        """FAIL when the art is replaced (or the check binds everything), else WARN."""
        self.add("FAIL" if binding else "WARN", check, rel, message)

    @property
    def failed(self):
        return any(f[0] == "FAIL" for f in self.findings)

    def lines(self):
        rank = {"FAIL": 0, "WARN": 1, "INFO": 2}
        for level, check, rel, message in sorted(self.findings, key=lambda f: (rank[f[0]], f[1], f[2])):
            yield f"{level:4s}  [{check}] {rel}: {message}"


# --- the checks --------------------------------------------------------------------------------

class Checker:
    def __init__(self, manifest, art_root, context, only=None):
        self.manifest = manifest
        self.art_root = art_root
        self.ctx = context
        self.only = set(only) if only else None
        self.report = Report()
        self._images = {}

    def image(self, rel):
        if rel not in self._images:
            path = os.path.join(self.art_root, rel)
            self._images[rel] = m.load(path) if os.path.exists(path) else None
        return self._images[rel]

    def wanted(self, rel):
        return self.only is None or rel in self.only

    def entries(self, predicate=lambda rel, e: True):
        return [(rel, e) for rel, e in sorted(self.manifest.items()) if predicate(rel, e) and self.image(rel) is not None]

    def run(self):
        self.check_complete()
        for rel, entry in self.entries():
            if not self.wanted(rel):
                continue
            image = self.image(rel)
            self.check_size(rel, entry, image)
            self.check_foot(rel, entry, image)
            if replaced(entry):
                self.check_alpha(rel, entry, image)
                self.check_palette(rel, entry, image)
                self.check_colour_count(rel, entry, image)
            self.check_reserved(rel, entry, image)
        self.check_golems()
        self.check_goods()
        self.check_copper_ore()
        self.check_frame_sets()
        self.check_light()
        self.check_hand_replaced()
        self.check_no_pack()
        return self.report

    # 1
    def check_complete(self):
        on_disk = set()
        for root, _, files in os.walk(self.art_root):
            for name in files:
                if name.endswith(".png"):
                    on_disk.add(os.path.relpath(os.path.join(root, name), self.art_root).replace(os.sep, "/"))
        for rel in sorted(on_disk - set(self.manifest)):
            if self.wanted(rel):
                self.report.add("FAIL", 1, rel, "no manifest entry")
        for rel in sorted(set(self.manifest) - on_disk):
            if self.wanted(rel):
                self.report.add("FAIL", 1, rel, "in the manifest but no file")

    # 2
    def check_size(self, rel, entry, image):
        if list(image.size) != list(entry.get("size", [])):
            self.report.add("FAIL", 2, rel, f"is {image.size[0]}x{image.size[1]}, the manifest says "
                                            f"{'x'.join(map(str, entry.get('size', [])))}")
        if entry.get("chassis") and (image.size[0] > CHASSIS_MAX[0] or image.size[1] > CHASSIS_MAX[1]):
            self.report.add("FAIL", 2, rel, f"a chassis frame is at most {CHASSIS_MAX[0]}x{CHASSIS_MAX[1]}")

    # 3
    def check_foot(self, rel, entry, image):
        row = foot_row(entry, image.size)
        if row is None or entry.get("wallHung"):
            return
        lowest = m.lowest_opaque_row(image)
        if lowest is None:
            self.report.add("FAIL", 3, rel, "has no opaque pixel to stand on")
            return
        slack = entry.get("footSlack", 0)
        shadow = entry.get("shadowRows")
        if entry.get("pivot") == "bottom-center":
            low_ok = row - slack
            if lowest < low_ok:
                self.report.add("FAIL", 3, rel, f"floats: lowest opaque row {lowest}, its feet belong on row {row}")
            elif slack and lowest < row:
                self.report.add("INFO", 3, rel, f"stands {row - lowest} rows up, allowed: {entry.get('footNote', 'footSlack')}")
            return
        bottom = shadow[1] if shadow else image.size[1] - 1
        if lowest < row - 1 - slack:
            self.report.add("FAIL", 3, rel, f"floats: lowest opaque row {lowest}, its contact line is row {row}")
        elif lowest > bottom:
            self.report.add("FAIL", 3, rel, f"draws below its shadow rows: row {lowest}")

    # 4
    def check_alpha(self, rel, entry, image):
        if entry.get("kind") in ("fx", "shadow") or entry.get("partialAlpha") or entry.get("glow"):
            return
        shadow = entry.get("shadowRows")
        px = image.load()
        w, h = image.size
        bad = [(x, y) for y in range(h) for x in range(w) if 0 < px[x, y][3] < 255
               and not (shadow and shadow[0] <= y <= shadow[1])]
        if bad:
            self.report.add("FAIL", 4, rel, f"{len(bad)} half-transparent pixels, first at {bad[0]}")

    def _ramps(self, entry):
        palette = entry.get("palette")
        return palette if isinstance(palette, list) else None

    # 5
    def check_palette(self, rel, entry, image):
        allowed = self.ctx.palette.colours(self._ramps(entry))
        allowed |= {m.hex_to_rgb(c) for c in entry.get("extraColours", [])}
        strays = Counter(tuple(p[:3]) for _, _, p in m.opaque_pixels(image, 1) if tuple(p[:3]) not in allowed)
        if strays:
            top = ", ".join(f"{m.rgb_to_hex(c)} x{n}" for c, n in strays.most_common(5))
            self.report.add("FAIL", 5, rel, f"{sum(strays.values())} off-palette pixels ({len(strays)} colours): {top}")

    # 6
    def check_colour_count(self, rel, entry, image):
        limit = {"standing": "standing", "good": "good", "floor": "floor"}.get(entry.get("kind"))
        if not limit:
            return
        cap = self.ctx.palette.colour_counts.get(limit)
        count = len({tuple(p[:3]) for _, _, p in m.opaque_pixels(image)})
        if cap and count > cap:
            self.report.add("FAIL", 6, rel, f"{count} opaque colours, a {limit} sprite has at most {cap}")

    # 7
    def check_reserved(self, rel, entry, image):
        if entry.get("kind") not in ("standing", "good", "floor", "tile"):
            return
        limits = self.ctx.palette.reserved
        pixels = m.opaque_pixels(image)
        if not pixels:
            return
        for name, test in (("red", m.is_reserved_red), ("steelBlue", m.is_reserved_blue)):
            if name not in limits:
                continue
            share = sum(1 for _, _, p in pixels if test(p, limits[name])) / len(pixels)
            if share > limits[name]["maxFraction"]:
                self.report.bound(replaced(entry), 7, rel,
                                  f"{share:.0%} reserved {'red' if name == 'red' else 'steel blue'} "
                                  f"(at most {limits[name]['maxFraction']:.0%})")

    # 8, 9, 10
    def check_golems(self):
        frames = self.entries(lambda rel, e: bool(e.get("chassis")))
        if not frames:
            return
        bases = {}
        for rel, e in frames:
            if e.get("state") == "base":
                bases.setdefault(e.get("facing"), {})[e["chassis"]] = (rel, e)
        # 8: silhouettes, per facing.
        for facing, by_chassis in sorted(bases.items()):
            for (a, (ra, ea)), (b, (rb, eb)) in combinations(sorted(by_chassis.items()), 2):
                if not (self.wanted(ra) or self.wanted(rb)):
                    continue
                overlap = m.iou(m.foot_mask(self.image(ra)), m.foot_mask(self.image(rb)))
                if overlap > SILHOUETTE_MAX_IOU:
                    self.report.bound(replaced(ea) and replaced(eb), 8, ra,
                                      f"silhouette overlaps {rb}'s {overlap:.2f} facing {facing} (at most {SILHOUETTE_MAX_IOU:.2f})")
        # 9: a pose keeps its chassis.
        for rel, e in frames:
            if e.get("state") == "base" or not self.wanted(rel):
                continue
            facing_bases = bases.get(e.get("facing"), {})
            own = facing_bases.get(e["chassis"])
            if own is None:
                continue
            mask = m.foot_mask(self.image(rel))
            mine = m.iou(mask, m.foot_mask(self.image(own[0])))
            for other, (orel, _) in facing_bases.items():
                if other == e["chassis"]:
                    continue
                theirs = m.iou(mask, m.foot_mask(self.image(orel)))
                if theirs >= mine:
                    self.report.bound(replaced(e), 9, rel,
                                      f"overlaps {orel} ({theirs:.2f}) at least as much as its own base ({mine:.2f})")
        # 10: tint shift.
        white = (1.0, 1.0, 1.0)
        amb = self.ctx.ambient
        for rel, e in frames:
            if not self.wanted(rel):
                continue
            image = self.image(rel)
            lit = m.mean_lab(image, amb)
            binding = replaced(e)
            if lit[0] < BODY_MIN_LIGHTNESS:
                self.report.bound(binding, 10, rel, f"mean OKLab lightness {lit[0]:.2f} (at least {BODY_MIN_LIGHTNESS:.2f})")
            tinted = {mood: m.mean_lab(image, tuple(t * a for t, a in zip(self.ctx.tints.get(mood, white), amb)))
                      for mood in MOOD_TINTS_CHECKED}
            shift = m.lab_distance(lit, tinted["Stalled"])
            if shift < STALLED_MIN_SHIFT:
                self.report.bound(binding, 10, rel, f"the Stalled tint moves it {shift:.3f} (at least {STALLED_MIN_SHIFT:.2f})")
            for a, b in combinations(MOOD_TINTS_CHECKED, 2):
                apart = m.lab_distance(tinted[a], tinted[b])
                if apart < STOPPED_TINTS_MIN_APART:
                    self.report.bound(binding, 10, rel,
                                      f"{a} and {b} tints {apart:.3f} apart (at least {STOPPED_TINTS_MIN_APART:.2f})")

    # 11
    def check_goods(self):
        goods = self.entries(lambda rel, e: e.get("kind") == "good")
        if not goods:
            return
        for phase in range(4):
            reduced = {rel: m.reduce_phase(self.image(rel), phase) for rel, _ in goods}
            for rel, e in goods:
                if not self.wanted(rel):
                    continue
                big, small = self.image(rel), reduced[rel]
                pieces32 = m.connected_pieces(m.xy_mask(big))
                pieces24 = m.connected_pieces(m.xy_mask(small))
                if pieces24 != pieces32:
                    self.report.bound(replaced(e), 11, rel, f"phase {phase}: {pieces32} pieces at 32 become {pieces24} at 24")
                tones32 = {tuple(p[:3]) for _, _, p in m.opaque_pixels(big)}
                tones24 = {tuple(p[:3]) for _, _, p in m.opaque_pixels(small)} & tones32
                if len(tones24) < min(GOODS_MIN_TONES, len(tones32)):
                    self.report.bound(replaced(e), 11, rel, f"phase {phase}: {len(tones24)} tones survive at 24 (at least {GOODS_MIN_TONES})")
            for (ra, ea), (rb, eb) in combinations(goods, 2):
                if not (self.wanted(ra) or self.wanted(rb)):
                    continue
                overlap = m.iou(m.xy_mask(reduced[ra]), m.xy_mask(reduced[rb]))
                apart = m.lab_distance(m.mean_lab(reduced[ra]), m.mean_lab(reduced[rb]))
                if overlap > GOODS_MAX_IOU and apart < GOODS_MIN_COLOUR_APART:
                    self.report.bound(replaced(ea) and replaced(eb), 11, ra,
                                      f"phase {phase}: like {rb} (overlap {overlap:.2f}, colours {apart:.2f} apart)")

    # 12
    def check_copper_ore(self):
        # The ore pixels: saturated enough to have a hue, and not the wood-and-iron range the
        # carts and the goods' outlines share. A copper ore turned orange leaves none.
        for rel in ("item_copper_ore.png", "stall_copper_ore.png"):
            if rel not in self.manifest or self.image(rel) is None or not self.wanted(rel):
                continue
            hues = Counter()
            for _, _, p in m.opaque_pixels(self.image(rel)):
                _, s, l = m.hsl(p)
                hue = m.hsv_hue(p)
                if s < 0.08 or l < 0.08 or l > 0.95:
                    continue
                if WOOD_HUE[0] <= hue <= WOOD_HUE[1]:
                    continue
                hues[int(hue // 10) * 10] += 1
            if not hues:
                self.report.add("FAIL", 12, rel, "no green ore pixels at all (invariant 6: copper ore is malachite green)")
                continue
            top = hues.most_common(1)[0][0] + 5
            if not COPPER_ORE_HUE[0] <= top <= COPPER_ORE_HUE[1]:
                self.report.add("FAIL", 12, rel, f"the ore's main hue is {top:.0f} degrees, not green "
                                                 f"({COPPER_ORE_HUE[0]:.0f}-{COPPER_ORE_HUE[1]:.0f}; invariant 6)")

    # 13
    def check_frame_sets(self):
        for stem in self.ctx.chassis_stems:
            names = [frame_name(stem, s, f) for f in FACINGS for s in FRAME_STATES]
            present = [n for n in names if n in self.manifest and self.image(n) is not None]
            if not any(self.wanted(n) for n in names):
                continue
            binding = any(replaced(self.manifest[n]) for n in present)
            if stem + ".png" not in present:
                self.report.add("FAIL", 13, stem + ".png", "the chassis has no south base")
            missing = len(names) - len(present)
            if missing:
                if binding:
                    for n in names:
                        if n not in present:
                            self.report.add("FAIL", 13, n, "missing: a chassis with any replaced frame needs all 28")
                else:
                    self.report.add("WARN", 13, stem + ".png",
                                    f"{len(present)} of {FRAME_FILES_PER_CHASSIS} frame files; the other {missing} "
                                    f"fall back to the base (not yet replaced)")
            sizes = {self.image(n).size for n in present}
            feet = {m.lowest_opaque_row(self.image(n)) for n in present}
            if len(sizes) > 1:
                self.report.add("FAIL", 13, stem + ".png", f"frames on {len(sizes)} canvas sizes: {sorted(sizes)}")
            if len(feet) > 1:
                self.report.add("FAIL", 13, stem + ".png", f"frames on {len(feet)} foot rows: {sorted(feet)}")
        walk = [rel for rel, e in self.manifest.items() if e.get("set") == "S1b"]
        if walk and any(self.wanted(r) for r in walk):
            present = [r for r in walk if self.image(r) is not None]
            if len(present) != ARTIFICER_FRAMES:
                self.report.add("FAIL", 13, "artificer_walk_*", f"{len(present)} frames, the Artificer has {ARTIFICER_FRAMES}")
            if len({self.image(r).size for r in present}) > 1:
                self.report.add("FAIL", 13, "artificer_walk_*", "walk frames on more than one canvas size")

    # 14
    def check_light(self):
        outline = self.ctx.palette.outline()
        for rel, e in self.entries(lambda rel, e: e.get("kind") == "standing"):
            if not self.wanted(rel):
                continue
            golem = bool(e.get("chassis")) or e.get("set") == "S1b"
            if not replaced(e) and not golem:
                continue
            image = self.image(rel)
            score = m.edge_light_score(image, outline if replaced(e) else None)
            if score < EDGE_LIGHT_MIN and e.get("lightNote"):
                self.report.add("INFO", 14, rel, f"edge-light score {score:+.3f}, allowed: {e['lightNote']}")
            elif score < EDGE_LIGHT_MIN:
                self.report.bound(replaced(e), 14, rel, f"edge-light score {score:+.3f}: not lit from the upper left "
                                                        f"(at least {EDGE_LIGHT_MIN:+.2f})")
            source = e.get("mirroredFrom")
            if not source:
                continue
            if self.image(source) is None:
                self.report.add("FAIL", 14, rel, f"mirrored from {source}, which isn't there")
                continue
            # Either the lightness or the tone places hold: a mirror may put a darker material on
            # the lit edge (lightness drops, tones hold); a plain flip fails both.
            src_score = m.edge_light_score(self.image(source), outline)
            tone = m.edge_tone_score(image, self.ctx.palette)
            src_tone = m.edge_tone_score(self.image(source), self.ctx.palette)
            if score < MIRROR_MIN_RATIO * src_score and tone < MIRROR_MIN_RATIO * src_tone:
                self.report.add("FAIL", 14, rel, f"edge-light {score:+.3f} and edge-tone {tone:+.3f}, under "
                                                 f"{MIRROR_MIN_RATIO} x its source's {src_score:+.3f} and {src_tone:+.3f}")
            flip = self.image(source).transpose(Image.FLIP_LEFT_RIGHT)
            if m.xy_mask(flip) != m.xy_mask(image):
                self.report.add("FAIL", 14, rel, "its alpha mask isn't the plain flip's: re-shading changed the shape")
            names = self._ramps(e)
            fp, ip = flip.load(), image.load()
            w, h = image.size
            moved = 0
            for y in range(h):
                for x in range(w):
                    if ip[x, y][3] >= m.OPAQUE:
                        a = self.ctx.palette.classify(fp[x, y], names)
                        b = self.ctx.palette.classify(ip[x, y], names)
                        if (a and a[0]) != (b and b[0]):
                            moved += 1
            if moved:
                self.report.add("FAIL", 14, rel, f"{moved} pixels changed ramp from the plain flip (re-shading changes tone, never material)")

    # 15
    def check_hand_replaced(self):
        marked = {rel for rel, e in self.manifest.items() if e.get("owner") == "hand-replaced"}
        for rel in sorted(marked):
            if not self.wanted(rel):
                continue
            if rel not in self.ctx.known_hand_replaced:
                self.report.add("FAIL", 15, rel, "owner hand-replaced, but not in verify_art.py's KNOWN_HAND_REPLACED")
            stem = os.path.basename(rel)[:-4]
            if f"`{stem}`" not in self.ctx.claude_text:
                self.report.add("FAIL", 15, rel, "owner hand-replaced, but Tools/Art/CLAUDE.md doesn't name it")
        for rel in sorted(self.ctx.known_hand_replaced - marked):
            if self.wanted(rel):
                self.report.add("FAIL", 15, rel, "in KNOWN_HAND_REPLACED, but its manifest entry doesn't say hand-replaced")

    # 16
    def check_no_pack(self):
        landed = any(rel.startswith("UI/Hud/") for rel in self.manifest)
        pack = [rel for rel, e in self.manifest.items() if e.get("owner") == "third-party" or rel.startswith("UI/Steampunk/")]
        if not pack:
            return
        if landed:
            for rel in sorted(pack):
                self.report.add("FAIL", 16, rel, "the Steampunk pack is gone once S6 lands")
        elif self.only is None:
            self.report.add("INFO", 16, "UI/Steampunk/", f"{len(pack)} pack pieces, not yet replaced (S6)")


def main(argv):
    manifest = load_manifest()
    if "--write" in argv:
        refreshed = write_mechanical(manifest)
        with open(MANIFEST_PATH, "w", encoding="utf-8", newline="\n") as handle:
            json.dump(refreshed, handle, indent=2)
            handle.write("\n")
        print(f"wrote {MANIFEST_PATH}: {len(refreshed)} entries")
        return 0
    only = []
    for arg in argv:
        if arg.startswith("--"):
            continue
        path = os.path.abspath(arg)
        only.append(os.path.relpath(path, ART_ROOT).replace(os.sep, "/") if os.path.exists(path) else arg.replace("\\", "/"))
    report = Checker(manifest, ART_ROOT, Context.from_repo(), only or None).run()
    lines = list(report.lines())
    for line in lines:
        print(line)
    counts = Counter(f[0] for f in report.findings)
    print(f"check_art: {len(manifest)} entries; {counts['FAIL']} FAIL, {counts['WARN']} WARN, {counts['INFO']} INFO")
    return 1 if report.failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
