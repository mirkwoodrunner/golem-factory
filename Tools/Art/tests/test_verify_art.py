"""art-pipeline-design.md §5.5: the placeholder generator honours --out-root, so verify_art.py
regenerates and compares its goods; and the HAND_REPLACED guard skips only under godot/art/."""
import os
import subprocess
import sys
import tempfile
import unittest

try:
    from . import _art
except ImportError:
    import _art

import art_paths  # noqa: E402
import generate_placeholder_art  # noqa: E402


def _snapshot(folder):
    return {os.path.join(root, n): os.stat(os.path.join(root, n)).st_mtime_ns
            for root, _, files in os.walk(folder) for n in files}


class PlaceholderOutRootTests(unittest.TestCase):
    def test_out_root_writes_the_goods_there_and_nothing_under_godot_art(self):
        before = _snapshot(_art.GODOT_ART)
        with tempfile.TemporaryDirectory() as scratch:
            result = subprocess.run(
                [sys.executable, os.path.join(_art.ART, "generate_placeholder_art.py"), "--out-root", scratch],
                capture_output=True, text=True)
            self.assertEqual(0, result.returncode, result.stderr)
            written = sorted(os.listdir(scratch))
        self.assertEqual(24, len(written), written)
        self.assertTrue(all(n.startswith("item_") and n.endswith(".png") for n in written), written)
        self.assertIn("item_copper_ore.png", written)
        self.assertEqual(before, _snapshot(_art.GODOT_ART))

    def test_the_folder_after_out_root_is_not_a_section(self):
        self.assertEqual(["belts", "goods"], generate_placeholder_art.sections(["--out-root", "C:/scratch"]))
        self.assertEqual(["goods"], generate_placeholder_art.sections(["goods", "--out-root", "x"]))
        self.assertEqual(["belts", "goods"], generate_placeholder_art.sections([]))
        self.assertEqual(["routing"], generate_placeholder_art.sections(["--legacy", "routing"]))


class HandReplacedGuardTests(unittest.TestCase):
    def test_skips_only_a_listed_name_and_only_under_godot_art(self):
        listed = {"item_gear.png"}
        self.assertTrue(art_paths.skip_hand_replaced("item_gear.png", listed, art_paths.DEFAULT_ROOT))
        self.assertTrue(art_paths.skip_hand_replaced(
            "item_gear.png", listed, os.path.join(art_paths.DEFAULT_ROOT, "UI", "Workbench")))
        self.assertFalse(art_paths.skip_hand_replaced("item_coal.png", listed, art_paths.DEFAULT_ROOT))
        with tempfile.TemporaryDirectory() as scratch:
            self.assertFalse(art_paths.skip_hand_replaced("item_gear.png", listed, scratch))

    def test_godot_art_is_recognised_and_a_sibling_is_not(self):
        self.assertTrue(art_paths.writes_into_godot_art(art_paths.DEFAULT_ROOT))
        self.assertFalse(art_paths.writes_into_godot_art(art_paths.DEFAULT_ROOT + "_scratch"))
        self.assertFalse(art_paths.writes_into_godot_art(os.path.dirname(art_paths.DEFAULT_ROOT)))


if __name__ == "__main__":
    unittest.main()
