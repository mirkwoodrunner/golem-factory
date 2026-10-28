"""env_set.py: the seam pass's geometry and the set's bookkeeping, with no ComfyUI."""
import json
import os
import tempfile
import unittest

from PIL import Image

try:
    from . import _art  # noqa: F401
except ImportError:
    import _art  # noqa: F401

import env_set as e


class SeamTests(unittest.TestCase):
    def gradient(self, w, h):
        image = Image.new("RGB", (w, h))
        image.putdata([(x * 4 % 256, y * 4 % 256, 0) for y in range(h) for x in range(w)])
        return image

    def test_a_rolled_tile_wraps_its_old_edges_into_the_middle(self):
        image = self.gradient(16, 8)
        rolled = e.roll(image, "tile")
        # The old top-left corner is now at the centre, and what was the centre is at the corner.
        self.assertEqual(image.getpixel((0, 0)), rolled.getpixel((8, 4)))
        self.assertEqual(image.getpixel((8, 4)), rolled.getpixel((0, 0)))

    def test_a_strip_rolls_only_sideways(self):
        image = self.gradient(16, 8)
        rolled = e.roll(image, "strip")
        for y in range(8):
            self.assertEqual(image.getpixel((0, y)), rolled.getpixel((8, y)))

    def test_the_mask_covers_the_cross_or_the_band(self):
        cross = e.seam_mask((64, 32), "tile", 2)
        band = e.seam_mask((64, 32), "strip", 2)
        self.assertEqual((255, 255, 255), cross.getpixel((31, 0)))
        self.assertEqual((255, 255, 255), cross.getpixel((0, 15)))
        self.assertEqual((0, 0, 0), cross.getpixel((0, 0)))
        self.assertEqual((255, 255, 255), band.getpixel((32, 31)))
        self.assertEqual((0, 0, 0), band.getpixel((0, 15)), "a strip keeps its rows")


class SetTests(unittest.TestCase):
    def write(self, config):
        root = tempfile.mkdtemp()
        with open(os.path.join(root, "set.json"), "w", encoding="utf-8") as handle:
            json.dump(config, handle)
        return root

    def test_a_tile_candidate_is_its_seamed_render(self):
        root = self.write({"files": {"floor_tile": {"kind": "tile", "prompt": "p", "ramps": ["floor"]},
                                     "prop_crate": {"kind": "sprite", "prompt": "p", "ramps": ["mahogany"]}}})
        es = e.EnvSet(root)
        self.assertIn(os.path.join("raw", "floor_tile_seam"), es.candidates("floor_tile")[0][1])
        self.assertIn(os.path.join("raw", "prop_crate", "prop_crate_d"), es.candidates("prop_crate")[0][1])
        self.assertEqual(len(es.denoise) * es.seeds, len(es.candidates("prop_crate")))

    def test_an_unknown_kind_is_refused(self):
        root = self.write({"files": {"x": {"kind": "blob", "prompt": "p", "ramps": []}}})
        with self.assertRaises(e.SetError):
            e.EnvSet(root)

    def test_saving_keeps_the_sets_own_keys_and_the_picks(self):
        root = self.write({"note": "n", "files": {"x": {"kind": "sprite", "prompt": "p", "ramps": []}}})
        es = e.EnvSet(root)
        es.picks["x"] = 3
        es.save()
        with open(os.path.join(root, "set.json"), encoding="utf-8") as handle:
            saved = json.load(handle)
        self.assertEqual({"note", "files", "picks", "approved"}, set(saved))
        self.assertEqual(3, saved["picks"]["x"])

    def test_install_refuses_an_unapproved_set(self):
        root = self.write({"files": {"x": {"kind": "sprite", "prompt": "p", "ramps": []}}})
        with self.assertRaises(e.SetError):
            e.step_install(e.EnvSet(root), log=lambda *_: None)


if __name__ == "__main__":
    unittest.main()
