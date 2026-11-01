"""Where the art generators write: the Godot project's art folder.

Milestone G3 of docs/godot-conversion-plan.md moved the generators' output from Unity's
Assets/_Project/Art/ to godot/art/, and the G10 cutover removed the Unity project (tag
unity-final). godot/art/ is the only copy.

    python Tools/Art/generate_building_art.py                       # writes godot/art/...
    python Tools/Art/generate_building_art.py --out-root <dir>      # writes <dir>/... instead

--out-root (or the GOLEM_ART_ROOT environment variable) redirects every generator to a
scratch folder, which is how Tools/Art/verify_art.py proves a regeneration reproduces the
committed art without touching it. Subfolders (UI/Workbench, UI/TechTree) are kept beneath
whichever root is used.
"""
import os
import sys

REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
DEFAULT_ROOT = os.path.join(REPO, "godot", "art")


def art_root() -> str:
    """The root art folder: --out-root, then GOLEM_ART_ROOT, then godot/art/."""
    if "--out-root" in sys.argv:
        index = sys.argv.index("--out-root")
        if index + 1 >= len(sys.argv):
            raise SystemExit("--out-root needs a folder")
        return os.path.abspath(sys.argv[index + 1])
    return os.environ.get("GOLEM_ART_ROOT") or DEFAULT_ROOT


def art_dir(*parts: str) -> str:
    """A folder beneath the art root, e.g. art_dir("UI", "TechTree")."""
    return os.path.join(art_root(), *parts)


def writes_into_godot_art(folder: str) -> bool:
    """Whether a folder is godot/art/ or beneath it. A generator skips its HAND_REPLACED names
    only there, so --out-root still renders the originals for verify_art.py; the art tools
    (art-pipeline-design.md §5) refuse to write there at all, except at the commit step."""
    root = os.path.normcase(os.path.abspath(DEFAULT_ROOT))
    here = os.path.normcase(os.path.abspath(folder))
    return here == root or here.startswith(root + os.sep)


def skip_hand_replaced(name: str, hand_replaced, folder: str) -> bool:
    """True (and says so) when `name` is hand-replaced and `folder` is under godot/art/: the
    guard of generate_topdown_environment.py's _save, shared by every generator that owns a file
    a set replaces (art-pipeline-design.md §2, step 13)."""
    if name in hand_replaced and writes_into_godot_art(folder):
        print("skipped %s: godot/art/ holds the hand-replaced redraw" % name)
        return True
    return False
