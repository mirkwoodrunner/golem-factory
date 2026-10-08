"""Regenerate every generated sprite into a scratch folder and compare with godot/art/.

    python Tools/Art/verify_art.py            # report; exit 1 if any generated file drifted

Milestone G3's check that moving the generators' output (Unity's Assets/_Project/Art/ ->
godot/art/) changed nothing: each generator runs with --out-root pointed at a temporary
folder, and every file it writes is compared with the committed one -- byte for byte, and
failing that pixel for pixel (a different PIL can re-encode identical pixels differently).

Not every committed sprite is generated, and the report says which are not, so nobody
mistakes "not generated" for "verified": the walk frames come from ConceptArt via
slice_artificer_walk.py, UI/Steampunk is a third-party pack, and a few sprites were drawn by
hand over the generated placeholder (KNOWN_HAND_REPLACED). A hand-replaced file is expected
to differ from what its generator writes and is reported, not failed.
"""
import os
import subprocess
import sys
import tempfile

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
COMMITTED = os.path.join(REPO, "godot", "art")

GENERATORS = [
    "generate_topdown_environment.py",
    "generate_placeholder_art.py",
    "generate_building_art.py",
    "generate_workbench_ui_art.py",
    "generate_tech_tree_art.py",
]

# Sprites whose committed version was drawn by hand over the generated placeholder, so the
# generator's output is expected to differ. Filled from this script's first run against the
# art Unity shipped; a file that drifts and is NOT listed here fails the check.
KNOWN_HAND_REPLACED = set()


def same_pixels(a: str, b: str) -> bool:
    with Image.open(a) as ia, Image.open(b) as ib:
        if ia.size != ib.size:
            return False
        return ia.convert("RGBA").tobytes() == ib.convert("RGBA").tobytes()


def main() -> int:
    scratch = tempfile.mkdtemp(prefix="golem-art-")
    for script in GENERATORS:
        result = subprocess.run(
            [sys.executable, os.path.join(HERE, script), "--out-root", scratch],
            capture_output=True, text=True)
        if result.returncode != 0:
            print(f"{script} failed:\n{result.stdout}\n{result.stderr}")
            return 1

    generated, identical, pixel_same, drifted, hand, missing = set(), [], [], [], [], []
    for root, _, files in os.walk(scratch):
        for name in files:
            if not name.endswith(".png"):
                continue
            rel = os.path.relpath(os.path.join(root, name), scratch).replace(os.sep, "/")
            generated.add(rel)
            ours = os.path.join(COMMITTED, rel)
            if not os.path.exists(ours):
                missing.append(rel)
            elif open(ours, "rb").read() == open(os.path.join(root, name), "rb").read():
                identical.append(rel)
            elif same_pixels(ours, os.path.join(root, name)):
                pixel_same.append(rel)
            elif rel in KNOWN_HAND_REPLACED:
                hand.append(rel)
            else:
                drifted.append(rel)

    committed = set()
    for root, _, files in os.walk(COMMITTED):
        for name in files:
            if name.endswith(".png"):
                committed.add(os.path.relpath(os.path.join(root, name), COMMITTED).replace(os.sep, "/"))
    not_generated = sorted(committed - generated)

    print(f"generated {len(generated)}: {len(identical)} byte-identical, "
          f"{len(pixel_same)} pixel-identical, {len(hand)} hand-replaced (expected), "
          f"{len(drifted)} DRIFTED, {len(missing)} not committed")
    for label, items in (("DRIFTED", drifted), ("not committed", missing), ("hand-replaced", hand)):
        for rel in sorted(items):
            print(f"  {label}: {rel}")
    print(f"committed but not generated ({len(not_generated)}): " + ", ".join(not_generated))
    return 1 if drifted or missing else 0


if __name__ == "__main__":
    sys.exit(main())
