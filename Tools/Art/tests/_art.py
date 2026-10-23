"""Puts Tools/Art on the import path, so the tests run both ways:
`python -m unittest discover Tools/Art/tests` from the repo root, and `python -m unittest`
from Tools/Art. Every test module imports this first."""
import os
import sys

ART = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
REPO = os.path.normpath(os.path.join(ART, "..", ".."))
GODOT_ART = os.path.join(REPO, "godot", "art")
if ART not in sys.path:
    sys.path.insert(0, ART)
