"""Where the art generators write: the Godot project's art folder.

Milestone G3 of docs/godot-conversion-plan.md moved the generators' output from Unity's
Assets/_Project/Art/ to godot/art/. The Unity project is frozen until cutover, so its copy
of the art no longer changes; godot/art/ is the live one.

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
